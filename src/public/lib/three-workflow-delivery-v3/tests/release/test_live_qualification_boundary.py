"""Scenario coverage for the shared live qualification boundary."""

from __future__ import annotations

import hashlib
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    ApprovalBoundary,
    ApprovalBundle,
    AttemptOutcome,
    BuddyExecutionIdentity,
    DestinationOperationProfile,
    DestinationProjection,
    ExactSatisfiedGovernanceProof,
    GovernanceProof,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    ProjectionObservation,
    PublicationAuthorization,
    PublicationObservationReference,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    Receipt,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    admit_release_record,
    form_publication_action,
    publication_capability_requirements,
    publication_mutable_resource_key_basis,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
)
from three_workflow_delivery_v3.release.finalizer import (
    desired_projection_state_digest,
)
from three_workflow_delivery_v3.release.planner import (
    plan_live_qualification,
)
from three_workflow_delivery_v3.release.simulation import (
    release_adapter_context_from_bytes,
)
from three_workflow_delivery_v3.release.workflow import (
    node_build_request,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PLATFORM_RUN_ATTEMPT = 3


@pytest.fixture
def live_attempt_binding(
    live_intent,
    live_admitted_repository_model,
) -> ReleaseAttemptBinding:
    """Use the replacement seven-field protected Governance provenance."""
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit=live_intent.release_unit,
        target=live_intent.target,
    )
    attempt = ReleaseAttemptIdentity(
        execution=execution,
        workflow_run_id=live_intent.workflow_run_id,
    )
    return ReleaseAttemptBinding(
        intent_digest=live_intent.intent_digest,
        request_id=live_intent.request_id,
        execution=execution,
        attempt=attempt,
        repository_model_digest=(
            live_admitted_repository_model.canonical_digest
        ),
        live_eligibility_artifact_id=7001,
        live_eligibility_artifact_digest="sha256:" + ("a" * 64),
        live_eligibility_payload_digest="sha256:" + ("b" * 64),
        attestation_provenance=(
            ("blob-oid", "c" * 40),
            ("canonical-content-digest", "sha256:" + ("d" * 64)),
            ("eligibility-main-sha", live_intent.target),
            ("git-object-format", "sha1"),
            (
                "path",
                (
                    ".github/workflow-delivery/governance/"
                    "hcoona-release-smoke-npm.json"
                ),
            ),
            ("ref", "refs/heads/main"),
            ("repository", "hcoona/three"),
        ),
    )


def _write(path: Path, content: bytes) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def _transport_digest(path: Path) -> str:
    return f"sha256:{hashlib.sha256(path.read_bytes()).hexdigest()}"


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


def _referenced_uploaded_arguments(
    name: str,
    path: Path,
    semantic_digest: str,
    reference: ArtifactReference,
) -> list[str]:
    option = name.replace("_", "-")
    return [
        *_uploaded_arguments(
            name,
            path,
            semantic_digest,
            reference.artifact_id,
        ),
        f"--{option}-artifact-url",
        reference.artifact_url,
        f"--{option}-payload-path",
        reference.payload_path,
    ]


def _live_observation(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    *,
    classification: str,
) -> ProjectionObservation:
    if not isinstance(snapshot.subject, ReleaseAttemptIdentity):
        message = "live observation requires a Release Attempt"
        raise TypeError(message)
    projection = snapshot.destination_projections[0]
    desired_state_digest = desired_projection_state_digest(
        snapshot,
        projection.projection_id,
        artifact,
    )
    exact = classification == "exact-satisfied"
    value = ObservationValue(
        classification=classification,
        owner="hcoona" if exact else None,
        coordinate=projection.coordinate if exact else None,
        content_sha512=artifact.content.content_sha512 if exact else None,
        witness_digest=artifact.witness_digest if exact else None,
        routing=(),
    )
    request_facts = ObservationRequestFacts(
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=desired_state_digest,
        method="GET",
        url="https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm/versions",
        headers=(),
    )
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
            "request-digest": request_facts.request_digest,
            "facts": response_facts.to_document(),
            "value": value.to_document(),
        }
    )
    return ProjectionObservation(
        subject=snapshot.subject,
        purpose="live-release",
        target=snapshot.target,
        producer="observe-github-packages",
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=desired_state_digest,
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_facts.request_digest,
        response_facts=response_facts,
        response_digest=response_digest,
        value=value,
    )


def _live_artifact(
    source: ReleaseArtifact,
    snapshot: QualificationSnapshot,
) -> ReleaseArtifact:
    if not isinstance(snapshot.subject, ReleaseAttemptIdentity):
        message = "live artifact requires a Release Attempt"
        raise TypeError(message)
    attempt = snapshot.subject
    transport = replace(
        source.transport,
        artifact_name=release_artifact_transport_name(
            repository=source.repository,
            purpose="live-release",
            output=source.output,
            qualification_snapshot_digest=snapshot.snapshot_digest,
            workflow_run_id=attempt.workflow_run_id,
            run_attempt=None,
            producer=source.transport.producer,
        ),
        run_attempt=None,
    )
    provenance = source.provenance_document()
    provenance.update(
        {
            "subject": attempt.to_document(),
            "qualification-snapshot-digest": snapshot.snapshot_digest,
            "repository-model-digest": snapshot.repository_model_digest,
            "purpose": "live-release",
            "transport": transport.to_document(),
        }
    )
    return replace(
        source,
        subject=attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        repository_model_digest=snapshot.repository_model_digest,
        purpose="live-release",
        transport=transport,
        provenance_digest=canonical_sha256(provenance),
    )


def _action_bearing_publication(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
) -> PublicationSnapshot:
    if not isinstance(snapshot.subject, ReleaseAttemptIdentity):
        message = "action-bearing publication requires a Release Attempt"
        raise TypeError(message)
    projection = snapshot.destination_projections[0]
    action = form_publication_action(
        destination_operation_profile=(
            github_packages_destination_operation_profile()
        ),
        projection=projection,
        artifact=artifact,
    )
    return PublicationSnapshot(
        attempt=snapshot.subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(artifact.artifact_digest,),
        artifact_output_ids=(artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("6" * 64),
                classification="absent",
            ),
        ),
        materialized_actions=(action,),
    )


def _rebind_observation_basis(
    observation: ProjectionObservation,
    *,
    projection: DestinationProjection | None = None,
    desired_state_digest: str | None = None,
) -> ProjectionObservation:
    rebound_projection = (
        observation.projection if projection is None else projection
    )
    rebound_desired_state = (
        observation.desired_state_digest
        if desired_state_digest is None
        else desired_state_digest
    )
    request_facts = replace(
        observation.request_facts,
        projection_digest=rebound_projection.projection_digest,
        desired_state_digest=rebound_desired_state,
    )
    response_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/observation-response",
            "request-digest": request_facts.request_digest,
            "facts": observation.response_facts.to_document(),
            "value": observation.value.to_document(),
        }
    )
    return replace(
        observation,
        projection=rebound_projection,
        desired_state_digest=rebound_desired_state,
        observation_contract_id=(rebound_projection.observation_contract_id),
        request_facts=request_facts,
        request_digest=request_facts.request_digest,
        response_digest=response_digest,
    )


def test_live_plan_build_transport_and_finalization_are_attempt_bound(  # noqa: PLR0913, PLR0915, PLR0917
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    qualified_simulation,
    live_intent,
    live_admitted_repository_model,
    live_attempt_binding,
    live_qualification_snapshot,
) -> None:
    """Carry one live Attempt through the shared unprivileged boundary."""
    current = [
        "--workflow-run-id",
        str(live_intent.workflow_run_id),
        "--run-attempt",
        str(PLATFORM_RUN_ATTEMPT),
        "--target",
        live_intent.target,
    ]
    intent_path = _write(
        tmp_path / "live-intent.json",
        canonicalize(live_intent.to_document()),
    )
    model_path = _write(
        tmp_path / "live-repository-model.json",
        live_admitted_repository_model.canonical_bytes,
    )
    binding_path = _write(
        tmp_path / "live-attempt-binding.json",
        canonicalize(live_attempt_binding.to_document()),
    )
    snapshot_path = tmp_path / "live-qualification-snapshot.json"
    context_path = tmp_path / "live-adapter-context.json"
    plan_output = tmp_path / "plan-output.txt"

    tool_outputs = {
        "git": str(qualified_simulation.request.source_date_epoch),
        "node": "v24.14.0",
        "pnpm": qualified_simulation.request.pnpm_version,
        "npm": qualified_simulation.request.npm_version,
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
                "admit-intent",
                "--purpose",
                "live-release",
                *current,
                *_uploaded_arguments(
                    "intent",
                    intent_path,
                    live_intent.intent_digest,
                    101,
                ),
            ]
        )
        == 0
    )
    assert (
        cli_module.main(
            [
                "release",
                "plan-live-qualification",
                "--repo-root",
                str(REPO_ROOT),
                *current,
                *_uploaded_arguments(
                    "intent",
                    intent_path,
                    live_intent.intent_digest,
                    101,
                ),
                *_uploaded_arguments(
                    "repository_model",
                    model_path,
                    live_admitted_repository_model.canonical_digest,
                    102,
                ),
                *_uploaded_arguments(
                    "attempt_binding",
                    binding_path,
                    live_attempt_binding.binding_digest,
                    103,
                ),
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
        live_qualification_snapshot.to_document()
    )
    plan_outputs = dict(
        line.split("=", 1)
        for line in plan_output.read_text(encoding="utf-8").splitlines()
    )
    context = release_adapter_context_from_bytes(
        context_path.read_bytes(),
        snapshot=live_qualification_snapshot,
        expected_digest=plan_outputs["adapter-context-digest"],
    )
    assert context.subject == live_attempt_binding.attempt
    assert context.witness.purpose == "live-release"
    assert "subject" in context.to_document()
    assert "simulation" not in context.to_document()

    request = node_build_request(
        REPO_ROOT,
        live_qualification_snapshot,
        context,
    )
    build_result = replace(
        qualified_simulation.build_result,
        witness=request.witness.canonical_bytes,
        expectation=replace(
            qualified_simulation.build_result.expectation,
            witness_bytes=request.witness.canonical_bytes,
        ),
    )
    monkeypatch.setattr(
        node_adapter,
        "build_node_package",
        lambda _request: build_result,
    )

    snapshot_arguments = _uploaded_arguments(
        "qualification_snapshot",
        snapshot_path,
        live_qualification_snapshot.snapshot_digest,
        104,
    )
    attempt_arguments = _uploaded_arguments(
        "attempt_binding",
        binding_path,
        live_attempt_binding.binding_digest,
        103,
    )
    context_arguments = _uploaded_arguments(
        "adapter_context",
        context_path,
        context.context_digest,
        105,
    )
    tarball_path = tmp_path / "live-package.tgz"
    mechanical_path = tmp_path / "live-mechanical-result.json"
    failure_path = tmp_path / "live-build-failure.json"
    assert (
        cli_module.main(
            [
                "release",
                "run-build",
                "--repo-root",
                str(REPO_ROOT),
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *context_arguments,
                "--tarball-output",
                str(tarball_path),
                "--mechanical-output",
                str(mechanical_path),
                "--failure-evidence-output",
                str(failure_path),
            ]
        )
        == 0
    )
    assert not failure_path.exists()

    artifact_path = tmp_path / "live-release-artifact.json"
    evidence_path = tmp_path / "live-build-evidence.json"
    assert (
        cli_module.main(
            [
                "release",
                "form-uploaded-artifact",
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *context_arguments,
                "--mechanical-result",
                str(mechanical_path),
                "--tarball",
                str(tarball_path),
                "--tarball-artifact-id",
                "106",
                "--tarball-artifact-name",
                plan_outputs["tarball-artifact-name"],
                "--tarball-artifact-url",
                "https://github.com/hcoona/three/actions/runs/7301/artifacts/106",
                "--tarball-artifact-digest",
                _transport_digest(tarball_path),
                "--artifact-output",
                str(artifact_path),
                "--evidence-output",
                str(evidence_path),
            ]
        )
        == 0
    )
    decision_path = tmp_path / "live-qualification-decision.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-qualification",
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *_uploaded_arguments(
                    "build_evidence",
                    evidence_path,
                    _transport_digest(evidence_path),
                    107,
                ),
                *_uploaded_arguments(
                    "release_artifact",
                    artifact_path,
                    _transport_digest(artifact_path),
                    108,
                ),
                "--output",
                str(decision_path),
            ]
        )
        == 0
    )
    decision = admit_release_record(
        decision_path.read_bytes(),
        expected_type=QualificationDecision,
        expected_digest=_transport_digest(decision_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(decision, QualificationDecision)
    assert decision.subject == live_attempt_binding.attempt
    assert decision.terminal_result == "incomplete"
    assert decision.next_action == "new-attempt"

    outcome_path = tmp_path / "live-attempt-outcome.json"
    summary_path = tmp_path / "live-attempt-summary.md"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *_uploaded_arguments(
                    "build_evidence",
                    evidence_path,
                    _transport_digest(evidence_path),
                    107,
                ),
                *_uploaded_arguments(
                    "release_artifact",
                    artifact_path,
                    _transport_digest(artifact_path),
                    108,
                ),
                *_uploaded_arguments(
                    "qualification_decision",
                    decision_path,
                    decision.decision_digest,
                    109,
                ),
                "--outcome-output",
                str(outcome_path),
                "--summary-output",
                str(summary_path),
            ]
        )
        == 1
    )
    outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=_transport_digest(outcome_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(outcome, AttemptOutcome)
    assert outcome.attempt == live_attempt_binding.attempt
    assert outcome.terminal_phase == "qualification"
    assert outcome.result == "incomplete"
    assert outcome.publication_snapshot_digest is None
    assert outcome.approval_bundle_digest is None
    assert outcome.publication_authorization_digest is None
    assert outcome.exact_satisfied_governance_proof_digest is None
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"

    project_evidence_path = tmp_path / "live-project-test-evidence.json"
    assert (
        cli_module.main(
            [
                "release",
                "run-project-test",
                "--repo-root",
                str(REPO_ROOT),
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *context_arguments,
                "--output",
                str(project_evidence_path),
            ]
        )
        == 0
    )
    artifact_arguments = _uploaded_arguments(
        "release_artifact",
        artifact_path,
        _transport_digest(artifact_path),
        108,
    )
    contents_evidence_path = tmp_path / "live-artifact-contents-evidence.json"
    assert (
        cli_module.main(
            [
                "release",
                "run-artifact-contents",
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *context_arguments,
                *artifact_arguments,
                "--tarball",
                str(tarball_path),
                "--output",
                str(contents_evidence_path),
            ]
        )
        == 0
    )
    install_evidence_path = tmp_path / "live-install-import-evidence.json"
    assert (
        cli_module.main(
            [
                "release",
                "run-install-import",
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *context_arguments,
                *artifact_arguments,
                "--tarball",
                str(tarball_path),
                "--output",
                str(install_evidence_path),
            ]
        )
        == 0
    )
    build_evidence_arguments = _uploaded_arguments(
        "build_evidence",
        evidence_path,
        _transport_digest(evidence_path),
        107,
    )
    project_evidence_arguments = _uploaded_arguments(
        "project_test_evidence",
        project_evidence_path,
        _transport_digest(project_evidence_path),
        110,
    )
    contents_evidence_arguments = _uploaded_arguments(
        "artifact_contents_evidence",
        contents_evidence_path,
        _transport_digest(contents_evidence_path),
        111,
    )
    install_evidence_arguments = _uploaded_arguments(
        "install_import_evidence",
        install_evidence_path,
        _transport_digest(install_evidence_path),
        112,
    )
    success_decision_path = (
        tmp_path / "successful-live-qualification-decision.json"
    )
    assert (
        cli_module.main(
            [
                "release",
                "finalize-qualification",
                *current,
                "--purpose",
                "live-release",
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                "--output",
                str(success_decision_path),
            ]
        )
        == 0
    )
    success_decision = admit_release_record(
        success_decision_path.read_bytes(),
        expected_type=QualificationDecision,
        expected_digest=_transport_digest(success_decision_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(success_decision, QualificationDecision)
    assert success_decision.terminal_result == "success"
    success_decision_arguments = _uploaded_arguments(
        "qualification_decision",
        success_decision_path,
        success_decision.decision_digest,
        113,
    )
    preparation_outcome_path = (
        tmp_path / "publication-preparation-attempt-outcome.json"
    )
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                "--publication-preparation-interrupted",
                "--outcome-output",
                str(preparation_outcome_path),
                "--summary-output",
                str(tmp_path / "publication-preparation-summary.md"),
            ]
        )
        == 1
    )
    preparation_outcome = admit_release_record(
        preparation_outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=_transport_digest(preparation_outcome_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(preparation_outcome, AttemptOutcome)
    assert preparation_outcome.terminal_phase == "publication-preparation"
    assert preparation_outcome.result == "incomplete"
    assert preparation_outcome.publication_snapshot_digest is None
    assert preparation_outcome.uncertainty is True
    assert preparation_outcome.possibly_mutated is False
    assert preparation_outcome.next_action == "new-attempt"

    missing_evidence_outcome_path = (
        tmp_path / "missing-evidence-preparation-outcome.json"
    )
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                "--publication-preparation-interrupted",
                "--outcome-output",
                str(missing_evidence_outcome_path),
                "--summary-output",
                str(tmp_path / "missing-evidence-preparation-summary.md"),
            ]
        )
        == 1
    )
    assert not missing_evidence_outcome_path.exists()

    artifact = admit_release_record(
        artifact_path.read_bytes(),
        expected_type=ReleaseArtifact,
        expected_digest=_transport_digest(artifact_path),
        expected_bindings=ReleaseAdmissionBindings(
            producer="build-tarball",
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(artifact, ReleaseArtifact)
    observation = _live_observation(
        live_qualification_snapshot,
        artifact,
        classification="conflicting",
    )
    observation_path = _write(
        tmp_path / "blocking-observation.json",
        canonicalize(observation.to_document()),
    )
    observation_arguments = _uploaded_arguments(
        "observation",
        observation_path,
        observation.observation_digest,
        114,
    )
    observation_outcome_path = tmp_path / "observation-attempt-outcome.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                *observation_arguments,
                "--publication-preparation-interrupted",
                "--outcome-output",
                str(observation_outcome_path),
                "--summary-output",
                str(tmp_path / "observation-attempt-summary.md"),
            ]
        )
        == 1
    )
    observation_outcome = admit_release_record(
        observation_outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=_transport_digest(observation_outcome_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(observation_outcome, AttemptOutcome)
    assert observation_outcome.terminal_phase == "observation"
    assert observation_outcome.result == "failure"
    assert observation_outcome.observation_digests == (
        observation.observation_digest,
    )
    assert observation_outcome.next_action == "reconcile"

    unplanned_projection = replace(
        observation.projection,
        projection_id="projection:npm:unplanned",
    )
    substituted_observations = (
        (
            "unplanned-projection",
            _rebind_observation_basis(
                observation,
                projection=unplanned_projection,
            ),
        ),
        (
            "mismatched-desired-state",
            _rebind_observation_basis(
                observation,
                desired_state_digest="sha256:" + ("0" * 64),
            ),
        ),
    )
    for index, (name, substituted_observation) in enumerate(
        substituted_observations,
        start=115,
    ):
        substituted_observation_path = _write(
            tmp_path / f"{name}-observation.json",
            canonicalize(substituted_observation.to_document()),
        )
        substituted_outcome_path = tmp_path / f"{name}-outcome.json"
        assert (
            cli_module.main(
                [
                    "release",
                    "finalize-live",
                    *current,
                    *attempt_arguments,
                    *snapshot_arguments,
                    *build_evidence_arguments,
                    *project_evidence_arguments,
                    *contents_evidence_arguments,
                    *install_evidence_arguments,
                    *artifact_arguments,
                    *success_decision_arguments,
                    *_uploaded_arguments(
                        "observation",
                        substituted_observation_path,
                        substituted_observation.observation_digest,
                        index,
                    ),
                    "--publication-preparation-interrupted",
                    "--outcome-output",
                    str(substituted_outcome_path),
                    "--summary-output",
                    str(tmp_path / f"{name}-summary.md"),
                ]
            )
            == 1
        )
        assert not substituted_outcome_path.exists()

    projection = live_qualification_snapshot.destination_projections[0]
    publication = PublicationSnapshot(
        attempt=live_attempt_binding.attempt,
        qualification_snapshot_digest=live_qualification_snapshot.snapshot_digest,
        qualification_decision_digest=success_decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(artifact.artifact_digest,),
        artifact_output_ids=(artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("f" * 64),
                classification="exact-satisfied",
            ),
        ),
        materialized_actions=(),
    )
    proof = ExactSatisfiedGovernanceProof(
        attempt=live_attempt_binding.attempt,
        publication_snapshot=publication,
        governance_provenance=live_attempt_binding.attestation_provenance,
        governance_current_main_sha=live_intent.target,
        governance_expires_at="2026-09-30T00:00:00Z",
        governance_live_enabled=True,
        governance_observed_at="2026-09-03T07:30:00Z",
        proved_at="2026-09-03T07:31:00Z",
        producer="prove-exact-satisfied",
        control=f"workflow-delivery-v3:{live_intent.target}",
    )
    publication_path = _write(
        tmp_path / "live-publication-snapshot.json",
        canonicalize(publication.to_document()),
    )
    proof_path = _write(
        tmp_path / "exact-satisfied-governance-proof.json",
        canonicalize(proof.to_document()),
    )
    publication_arguments = _uploaded_arguments(
        "publication_snapshot",
        publication_path,
        publication.snapshot_digest,
        114,
    )
    proof_arguments = _uploaded_arguments(
        "exact_satisfied_governance_proof",
        proof_path,
        proof.proof_digest,
        115,
    )
    success_outcome_path = tmp_path / "successful-live-attempt-outcome.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                *publication_arguments,
                *proof_arguments,
                "--outcome-output",
                str(success_outcome_path),
                "--summary-output",
                str(tmp_path / "successful-live-attempt-summary.md"),
            ]
        )
        == 0
    )
    success_outcome = admit_release_record(
        success_outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=_transport_digest(success_outcome_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert isinstance(success_outcome, AttemptOutcome)
    assert success_outcome.result == "success"
    assert success_outcome.terminal_phase == "finalized-no-op"
    assert success_outcome.publication_snapshot_digest == (
        publication.snapshot_digest
    )
    assert success_outcome.exact_satisfied_governance_proof_digest == (
        proof.proof_digest
    )
    assert success_outcome.approval_bundle_digest is None
    assert success_outcome.publication_authorization_digest is None
    assert success_outcome.action_result_digests == ()

    substituted_proof = replace(
        proof,
        governance_provenance=tuple(
            ("blob-oid", "e" * 40) if name == "blob-oid" else (name, value)
            for name, value in proof.governance_provenance
        ),
    )
    substituted_proof_path = _write(
        tmp_path / "substituted-exact-satisfied-governance-proof.json",
        canonicalize(substituted_proof.to_document()),
    )
    substituted_outcome_path = tmp_path / "substituted-proof-outcome.json"
    capsys.readouterr()
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                *publication_arguments,
                *_uploaded_arguments(
                    "exact_satisfied_governance_proof",
                    substituted_proof_path,
                    substituted_proof.proof_digest,
                    116,
                ),
                "--outcome-output",
                str(substituted_outcome_path),
                "--summary-output",
                str(tmp_path / "substituted-proof-summary.md"),
            ]
        )
        == 1
    )
    assert (
        "Live finalization exact-satisfied Governance authority binding "
        "mismatch" in capsys.readouterr().err
    )
    assert not substituted_outcome_path.exists()

    wrong_binding = replace(
        live_attempt_binding,
        repository_model_digest=(
            f"sha256:{hashlib.sha256(b'wrong-repository-model').hexdigest()}"
        ),
    )
    wrong_binding_path = _write(
        tmp_path / "wrong-live-attempt-binding.json",
        canonicalize(wrong_binding.to_document()),
    )
    wrong_outcome_path = tmp_path / "wrong-attempt-outcome.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *_uploaded_arguments(
                    "attempt_binding",
                    wrong_binding_path,
                    wrong_binding.binding_digest,
                    116,
                ),
                *snapshot_arguments,
                *build_evidence_arguments,
                *project_evidence_arguments,
                *contents_evidence_arguments,
                *install_evidence_arguments,
                *artifact_arguments,
                *success_decision_arguments,
                *publication_arguments,
                *proof_arguments,
                "--outcome-output",
                str(wrong_outcome_path),
                "--summary-output",
                str(tmp_path / "wrong-attempt-summary.md"),
            ]
        )
        == 1
    )
    assert not wrong_outcome_path.exists()

    malformed_decision = replace(
        decision,
        failure_class="substituted-incomplete-qualification",
    )
    malformed_decision_path = _write(
        tmp_path / "malformed-live-qualification-decision.json",
        canonicalize(malformed_decision.to_document()),
    )
    malformed_outcome_path = tmp_path / "malformed-attempt-outcome.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-live",
                *current,
                *attempt_arguments,
                *snapshot_arguments,
                *_uploaded_arguments(
                    "build_evidence",
                    evidence_path,
                    _transport_digest(evidence_path),
                    107,
                ),
                *_uploaded_arguments(
                    "release_artifact",
                    artifact_path,
                    _transport_digest(artifact_path),
                    108,
                ),
                *_uploaded_arguments(
                    "qualification_decision",
                    malformed_decision_path,
                    malformed_decision.decision_digest,
                    117,
                ),
                "--outcome-output",
                str(malformed_outcome_path),
                "--summary-output",
                str(tmp_path / "malformed-attempt-summary.md"),
            ]
        )
        == 1
    )
    assert not malformed_outcome_path.exists()


@pytest.mark.parametrize(
    ("classification", "action_count"),
    [
        pytest.param("exact-satisfied", 0, id="zero-action"),
        pytest.param("absent", 1, id="action-bearing"),
    ],
)
def test_materialize_publication_cli_emits_path_specific_review_outputs(  # noqa: PLR0913, PLR0917
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
    live_intent,
    live_qualification_snapshot,
    classification: str,
    action_count: int,
) -> None:
    """Emit reviewer evidence only when a publication action needs approval."""
    intent_path = _write(
        tmp_path / "live-intent.json",
        canonicalize(live_intent.to_document()),
    )
    artifact = _live_artifact(
        qualified_simulation.artifact,
        live_qualification_snapshot,
    )
    observation = _live_observation(
        live_qualification_snapshot,
        artifact,
        classification=classification,
    )
    observation_path = _write(
        tmp_path / "observation.json",
        canonicalize(observation.to_document()),
    )
    projection = live_qualification_snapshot.destination_projections[0]
    publication = (
        _action_bearing_publication(
            live_qualification_snapshot,
            qualified_simulation.decision,
            artifact,
        )
        if action_count
        else PublicationSnapshot(
            attempt=live_qualification_snapshot.subject,
            qualification_snapshot_digest=(
                live_qualification_snapshot.snapshot_digest
            ),
            qualification_decision_digest=(
                qualified_simulation.decision.decision_digest
            ),
            qualification_result="success",
            projection_ids=(projection.projection_id,),
            artifact_digests=(artifact.artifact_digest,),
            artifact_output_ids=(artifact.output.output_id,),
            observation_references=(
                PublicationObservationReference(
                    projection_id=projection.projection_id,
                    observation_digest=observation.observation_digest,
                    classification=classification,
                ),
            ),
            materialized_actions=(),
        )
    )
    if action_count:
        publication = replace(
            publication,
            observation_references=(
                PublicationObservationReference(
                    projection_id=projection.projection_id,
                    observation_digest=observation.observation_digest,
                    classification=classification,
                ),
            ),
        )
    monkeypatch.setattr(
        cli_module,
        "_load_live_qualification_snapshot",
        lambda _arguments: live_qualification_snapshot,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_live_qualification_decision",
        lambda _arguments: qualified_simulation.decision,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_live_release_artifact_record",
        lambda _arguments: artifact,
    )
    destination_operation_profiles: list[DestinationOperationProfile] = []

    def capture_materialization(
        *_arguments: object,
        destination_operation_profile: DestinationOperationProfile,
    ) -> PublicationSnapshot:
        destination_operation_profiles.append(destination_operation_profile)
        return publication

    monkeypatch.setattr(
        cli_module,
        "materialize_publication_snapshot",
        capture_materialization,
    )
    output_path = tmp_path / "publication-snapshot.json"
    summary_path = tmp_path / "reviewer-summary.md"
    github_output = tmp_path / "github-output.txt"
    current = [
        "--workflow-run-id",
        str(live_intent.workflow_run_id),
        "--run-attempt",
        str(PLATFORM_RUN_ATTEMPT),
        "--target",
        live_intent.target,
    ]

    status = cli_module.main(
        [
            "release",
            "materialize-publication",
            *current,
            "--selected-ref",
            live_intent.selected_ref,
            *_uploaded_arguments(
                "intent",
                intent_path,
                live_intent.intent_digest,
                201,
            ),
            *_uploaded_arguments(
                "qualification_snapshot",
                intent_path,
                live_qualification_snapshot.snapshot_digest,
                202,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                intent_path,
                qualified_simulation.decision.decision_digest,
                203,
            ),
            *_uploaded_arguments(
                "release_artifact",
                intent_path,
                artifact.artifact_digest,
                204,
            ),
            *_uploaded_arguments(
                "observation",
                observation_path,
                observation.observation_digest,
                205,
            ),
            "--output",
            str(output_path),
            "--summary-output",
            str(summary_path),
            "--github-output",
            str(github_output),
        ]
    )

    assert status == 0
    assert output_path.read_bytes() == canonicalize(publication.to_document())
    emitted = github_output.read_text(encoding="utf-8")
    if not action_count:
        assert not summary_path.exists()
        assert "publish-required=false\n" in emitted
        assert "resource-concurrency-key=no-op\n" in emitted
        assert "reviewer-digest=" not in emitted
        return

    assert len(destination_operation_profiles) == 1
    (destination_operation_profile,) = destination_operation_profiles
    assert (
        destination_operation_profile
        == github_packages_destination_operation_profile()
    )
    summary = summary_path.read_text(encoding="utf-8")
    coordinate = projection.coordinate
    required_values = (
        live_intent.selected_ref,
        live_intent.target,
        f"{coordinate.package_name}@{coordinate.native_version}",
        coordinate.destination_id,
        coordinate.channel,
        artifact.artifact_digest,
        artifact.content.content_sha256,
        artifact.content.content_sha512,
        artifact.content.basename,
        *artifact.entries,
        *(
            value
            for lifecycle_script in artifact.lifecycle_scripts
            for value in lifecycle_script
        ),
        classification,
        publication.snapshot_digest,
    )
    assert all(
        value is not None and value in summary for value in required_values
    )
    assert "Materialized actions: `1`" in summary
    assert "### Action" in summary
    reviewer_digest = (
        "sha256:" + hashlib.sha256(summary_path.read_bytes()).hexdigest()
    )
    assert "publish-required=true\n" in emitted
    assert f"reviewer-digest={reviewer_digest}\n" in emitted
    assert (
        "resource-concurrency-key="
        f"{publication.materialized_actions[0].serialization_projection}\n"
    ) in emitted


def test_materialize_publication_rejects_selected_ref_substitution(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
    live_intent,
) -> None:
    """Reject a workflow-selected ref that differs from immutable Intent."""
    intent_path = _write(
        tmp_path / "live-intent.json",
        canonicalize(live_intent.to_document()),
    )
    load_snapshot = Mock(
        spec=cli_module._load_live_qualification_snapshot,  # noqa: SLF001
        side_effect=AssertionError(
            "selected-ref binding must precede Snapshot loading"
        ),
    )
    monkeypatch.setattr(
        cli_module,
        "_load_live_qualification_snapshot",
        load_snapshot,
    )
    output_path = tmp_path / "publication-snapshot.json"
    summary_path = tmp_path / "reviewer-summary.md"

    status = cli_module.main(
        [
            "release",
            "materialize-publication",
            "--workflow-run-id",
            str(live_intent.workflow_run_id),
            "--run-attempt",
            str(PLATFORM_RUN_ATTEMPT),
            "--target",
            live_intent.target,
            "--selected-ref",
            "refs/heads/substituted",
            *_uploaded_arguments(
                "intent",
                intent_path,
                live_intent.intent_digest,
                301,
            ),
            *_uploaded_arguments(
                "qualification_snapshot",
                intent_path,
                live_intent.intent_digest,
                302,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                intent_path,
                live_intent.intent_digest,
                303,
            ),
            *_uploaded_arguments(
                "release_artifact",
                intent_path,
                live_intent.intent_digest,
                304,
            ),
            *_uploaded_arguments(
                "observation",
                intent_path,
                live_intent.intent_digest,
                305,
            ),
            "--output",
            str(output_path),
            "--summary-output",
            str(summary_path),
        ]
    )
    error = capsys.readouterr().err

    assert status == 1
    assert "selected ref does not match the admitted Release Intent" in error
    load_snapshot.assert_not_called()
    assert not output_path.exists()
    assert not summary_path.exists()


def test_live_qualification_snapshot_closes_attempt_execution_identity(
    live_qualification_snapshot: QualificationSnapshot,
) -> None:
    """Reject live subjects whose execution identity is foreign."""
    attempt = live_qualification_snapshot.subject
    assert isinstance(attempt, ReleaseAttemptIdentity)
    execution = attempt.execution
    assert isinstance(execution, BuddyExecutionIdentity)

    invalid_subjects = (
        replace(
            attempt,
            execution=replace(execution, target="f" * 40),
        ),
        replace(
            attempt,
            execution=replace(
                execution,
                release_unit="src/public/lib/other-package",
            ),
        ),
        replace(
            attempt,
            execution=OfficialExecutionIdentity(
                product=OfficialProductIdentity(
                    channel="official",
                    release_unit=live_qualification_snapshot.release_unit,
                    canonical_version="1.2.3",
                ),
                target=live_qualification_snapshot.target,
            ),
        ),
    )

    for invalid_subject in invalid_subjects:
        with pytest.raises(
            ValueError,
            match="Qualification Snapshot live Attempt is inconsistent",
        ):
            replace(
                live_qualification_snapshot,
                subject=invalid_subject,
            )


def test_live_planner_rejects_attempt_binding_for_different_workflow_run(
    live_intent,
    live_attempt_binding,
    live_admitted_repository_model,
) -> None:
    """Keep the retained live Attempt binding aligned with its workflow run."""
    mismatched_binding = replace(
        live_attempt_binding,
        attempt=replace(
            live_attempt_binding.attempt,
            workflow_run_id=live_intent.workflow_run_id + 1,
        ),
    )

    with pytest.raises(
        ValueError,
        match="Release Planner workflow run binding mismatch",
    ):
        plan_live_qualification(
            live_intent,
            mismatched_binding,
            live_admitted_repository_model,
        )


def test_live_qualification_snapshot_transport_rejects_wrong_channel(
    live_qualification_snapshot: QualificationSnapshot,
) -> None:
    """Reject a transported Buddy identity with an Official channel."""
    document = live_qualification_snapshot.to_document()
    subject = document["subject"]
    assert isinstance(subject, dict)
    execution = subject["execution"]
    assert isinstance(execution, dict)
    execution["channel"] = "official"

    with pytest.raises(
        ValueError,
        match="Buddy Execution Identity channel must be buddy",
    ):
        release_record_from_document(
            document,
            expected_type=QualificationSnapshot,
        )


def test_live_qualification_snapshot_closes_projection_actions(
    live_qualification_snapshot: QualificationSnapshot,
) -> None:
    """Reject cross-channel projections and unbound action contracts."""
    projection = live_qualification_snapshot.destination_projections[0]
    action = live_qualification_snapshot.potential_actions[0]
    assert action.capability_requirements == (
        publication_capability_requirements(projection)
    )
    assert action.mutable_resource_key_basis == (
        publication_mutable_resource_key_basis(projection)
    )

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection channel is not closed",
    ):
        replace(
            live_qualification_snapshot,
            destination_projections=(
                replace(
                    projection,
                    coordinate=replace(
                        projection.coordinate,
                        channel="official",
                    ),
                ),
            ),
        )
    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection version is not closed",
    ):
        replace(
            live_qualification_snapshot,
            destination_projections=(
                replace(
                    projection,
                    coordinate=replace(
                        projection.coordinate,
                        native_version="9.9.9",
                    ),
                ),
            ),
        )
    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection action is not closed",
    ):
        replace(
            live_qualification_snapshot,
            destination_projections=(
                replace(
                    projection,
                    potential_action_id="publish-foreign-package",
                ),
            ),
        )
    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection action is not closed",
    ):
        replace(
            live_qualification_snapshot,
            potential_actions=(
                replace(action, operation="npm-publish-overwrite"),
            ),
        )
    for invalid_action in (
        replace(
            action,
            capability_requirements=("github/packages-admin-v1",),
        ),
        replace(
            action,
            mutable_resource_key_basis=("external-package-coordinate",),
        ),
        replace(action, prerequisites=(action.contract_id,)),
    ):
        with pytest.raises(
            ValueError,
            match="Qualification Snapshot action policy is not closed",
        ):
            replace(
                live_qualification_snapshot,
                potential_actions=(invalid_action,),
            )


def test_live_qualification_snapshot_transport_closes_projection_actions(
    live_qualification_snapshot: QualificationSnapshot,
) -> None:
    """Reject transported cross-channel or unbound projection actions."""
    channel_document = live_qualification_snapshot.to_document()
    channel_projections = channel_document["destination-projections"]
    assert isinstance(channel_projections, list)
    channel_projection = channel_projections[0]
    assert isinstance(channel_projection, dict)
    coordinate = channel_projection["coordinate"]
    assert isinstance(coordinate, dict)
    coordinate["channel"] = "official"

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection channel is not closed",
    ):
        release_record_from_document(
            channel_document,
            expected_type=QualificationSnapshot,
        )

    version_document = live_qualification_snapshot.to_document()
    version_projections = version_document["destination-projections"]
    assert isinstance(version_projections, list)
    version_projection = version_projections[0]
    assert isinstance(version_projection, dict)
    version_coordinate = version_projection["coordinate"]
    assert isinstance(version_coordinate, dict)
    version_coordinate["native-version"] = "9.9.9"

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection version is not closed",
    ):
        release_record_from_document(
            version_document,
            expected_type=QualificationSnapshot,
        )

    action_document = live_qualification_snapshot.to_document()
    action_projections = action_document["destination-projections"]
    assert isinstance(action_projections, list)
    action_projection = action_projections[0]
    assert isinstance(action_projection, dict)
    action_projection["potential-action-id"] = "publish-foreign-package"

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection action is not closed",
    ):
        release_record_from_document(
            action_document,
            expected_type=QualificationSnapshot,
        )

    policy_document = live_qualification_snapshot.to_document()
    actions = policy_document["potential-actions"]
    assert isinstance(actions, list)
    policy_action = actions[0]
    assert isinstance(policy_action, dict)
    policy_action["capability-requirements"] = ["github/packages-admin-v1"]

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot action policy is not closed",
    ):
        release_record_from_document(
            policy_document,
            expected_type=QualificationSnapshot,
        )


def test_official_live_snapshot_closes_each_product_identity_field(
    qualified_simulation,
) -> None:
    """Reject each mismatched Official execution identity component."""
    snapshot = qualified_simulation.snapshot
    execution = OfficialExecutionIdentity(
        product=OfficialProductIdentity(
            channel="official",
            release_unit=snapshot.release_unit,
            canonical_version=snapshot.nbgv.canonical_version,
        ),
        target=snapshot.target,
    )
    attempt = ReleaseAttemptIdentity(
        execution=execution,
        workflow_run_id=qualified_simulation.binding.simulation.workflow_run_id,
    )
    official_snapshot = replace(snapshot, subject=attempt)
    official_projection = official_snapshot.destination_projections[0]
    official_action = official_snapshot.potential_actions[0]
    assert official_action.capability_requirements == (
        publication_capability_requirements(official_projection)
    )
    assert official_action.mutable_resource_key_basis == (
        publication_mutable_resource_key_basis(official_projection)
    )
    invalid_executions = (
        replace(execution, target="f" * 40),
        replace(
            execution,
            product=replace(
                execution.product,
                release_unit="src/public/lib/other-package",
            ),
        ),
        replace(
            execution,
            product=replace(
                execution.product,
                canonical_version="9.9.9",
            ),
        ),
    )

    for invalid_execution in invalid_executions:
        with pytest.raises(
            ValueError,
            match="Qualification Snapshot live Attempt is inconsistent",
        ):
            replace(
                official_snapshot,
                subject=replace(attempt, execution=invalid_execution),
            )


_OPTIONAL_TRANSPORT_GROUPS = (
    "observation",
    "publication_snapshot",
    "approval_bundle",
    "publication_authorization",
    "exact_satisfied_governance_proof",
    "action_result",
)
_OPTIONAL_TRANSPORT_MEMBERS = (
    "path",
    "record-digest",
    "artifact-id",
    "artifact-digest",
)
_REPRESENTATIVE_PARTIAL_TRANSPORT_PATTERNS = (
    ("path", "only"),
    ("record-digest", "missing"),
    ("artifact-id", "missing"),
)
_PARTIAL_OPTIONAL_TRANSPORT_CASES = [
    pytest.param(
        group,
        member,
        mode,
        id=f"{group.replace('_', '-')}-{mode}-{member}",
    )
    for group in _OPTIONAL_TRANSPORT_GROUPS
    for member, mode in _REPRESENTATIVE_PARTIAL_TRANSPORT_PATTERNS
]


@pytest.mark.parametrize(
    ("group", "selected_member", "provided_member_mode"),
    _PARTIAL_OPTIONAL_TRANSPORT_CASES,
)
def test_finalize_live_rejects_representative_partial_transport_groups(  # noqa: PLR0913, PLR0917
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    monkeypatch: pytest.MonkeyPatch,
    live_intent,
    live_attempt_binding,
    live_qualification_snapshot,
    group: str,
    selected_member: str,
    provided_member_mode: str,
) -> None:
    """Distinguish every optional transport field before record loading."""
    binding_path = _write(
        tmp_path / "live-attempt-binding.json",
        canonicalize(live_attempt_binding.to_document()),
    )
    snapshot_path = _write(
        tmp_path / "live-qualification-snapshot.json",
        canonicalize(live_qualification_snapshot.to_document()),
    )
    decision = cli_module.finalize_qualification(
        live_qualification_snapshot,
        (),
        (),
    )
    decision_path = _write(
        tmp_path / "live-qualification-decision.json",
        canonicalize(decision.to_document()),
    )
    optional_path = _write(
        tmp_path / f"{group.replace('_', '-')}.json",
        canonicalize({}),
    )
    option = group.replace("_", "-")
    optional_members = {
        "path": (f"--{option}", str(optional_path)),
        "record-digest": (
            f"--{option}-digest",
            _transport_digest(optional_path),
        ),
        "artifact-id": (f"--{option}-artifact-id", "901"),
        "artifact-digest": (
            f"--{option}-artifact-digest",
            _transport_digest(optional_path),
        ),
    }
    provided_members = (
        {selected_member}
        if provided_member_mode == "only"
        else set(optional_members) - {selected_member}
    )
    partial_arguments = [
        value
        for member, option_and_value in optional_members.items()
        if member in provided_members
        for value in option_and_value
    ]
    outcome_path = tmp_path / "live-attempt-outcome.json"
    summary_path = tmp_path / "live-attempt-summary.md"
    load_attempt_binding = Mock(
        spec=cli_module._load_attempt_binding,  # noqa: SLF001
        side_effect=AssertionError(
            "optional transport preflight must precede record loading"
        ),
    )
    monkeypatch.setattr(
        cli_module,
        "_load_attempt_binding",
        load_attempt_binding,
    )

    status = cli_module.main(
        [
            "release",
            "finalize-live",
            "--workflow-run-id",
            str(live_intent.workflow_run_id),
            "--run-attempt",
            str(PLATFORM_RUN_ATTEMPT),
            "--target",
            live_intent.target,
            *_uploaded_arguments(
                "attempt_binding",
                binding_path,
                live_attempt_binding.binding_digest,
                101,
            ),
            *_uploaded_arguments(
                "qualification_snapshot",
                snapshot_path,
                live_qualification_snapshot.snapshot_digest,
                102,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                decision_path,
                decision.decision_digest,
                103,
            ),
            *partial_arguments,
            "--outcome-output",
            str(outcome_path),
            "--summary-output",
            str(summary_path),
        ]
    )
    error = capsys.readouterr().err

    assert status == 1
    missing_labels = ", ".join(
        "artifact ID" if member == "artifact-id" else member.replace("-", " ")
        for member in optional_members
        if member not in provided_members
    )
    expected_error = (
        f"{group} uploaded record transport is partial: "
        f"missing {missing_labels}; path, record digest, artifact ID, and "
        "artifact digest must be all "
        "present or all absent"
    )
    assert expected_error in error
    assert len(partial_arguments) == (
        2 if provided_member_mode == "only" else 6
    )
    assert not outcome_path.exists()
    assert not summary_path.exists()
    load_attempt_binding.assert_not_called()


def test_finalize_live_persists_bundle_without_authorization(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
    live_attempt_binding,
    live_qualification_snapshot,
) -> None:
    """Persist the durable Bundle as the incomplete direct predecessor."""
    artifact = _live_artifact(
        qualified_simulation.artifact,
        live_qualification_snapshot,
    )
    target = live_attempt_binding.attempt.execution.target
    decision = replace(
        qualified_simulation.decision,
        subject=live_attempt_binding.attempt,
        qualification_snapshot_digest=(
            live_qualification_snapshot.snapshot_digest
        ),
        admitted_artifact_digests=(artifact.artifact_digest,),
    )
    publication = _action_bearing_publication(
        live_qualification_snapshot,
        decision,
        artifact,
    )
    control = f"workflow-delivery-v3:{target}"

    binding_path = _write(
        tmp_path / "attempt-binding.json",
        canonicalize(live_attempt_binding.to_document()),
    )
    snapshot_path = _write(
        tmp_path / "qualification-snapshot.json",
        canonicalize(live_qualification_snapshot.to_document()),
    )
    decision_path = _write(
        tmp_path / "qualification-decision.json",
        canonicalize(decision.to_document()),
    )
    publication_path = _write(
        tmp_path / "publication-snapshot.json",
        canonicalize(publication.to_document()),
    )
    publication_reference = ArtifactReference(
        artifact_id=910,
        artifact_digest=_transport_digest(publication_path),
        artifact_url="https://example.test/artifacts/910",
        payload_path=publication_path.name,
        payload_digest=publication.snapshot_digest,
    )
    bundle = ApprovalBundle(
        attempt=live_attempt_binding.attempt,
        publication_snapshot_reference=publication_reference,
        reviewer_summary_reference=ArtifactReference(
            artifact_id=911,
            artifact_digest="sha256:" + ("7" * 64),
            artifact_url="https://example.test/artifacts/911",
            payload_path="reviewer-summary.md",
            payload_digest="sha256:" + ("8" * 64),
        ),
        producer="materialize-publication",
        control=control,
        workflow_run_id=live_attempt_binding.attempt.workflow_run_id,
    )
    bundle_path = _write(
        tmp_path / "approval-bundle.json",
        canonicalize(bundle.to_document()),
    )
    bundle_reference = ArtifactReference(
        artifact_id=912,
        artifact_digest=_transport_digest(bundle_path),
        artifact_url="https://example.test/artifacts/912",
        payload_path=bundle_path.name,
        payload_digest=bundle.bundle_digest,
    )
    outcome_path = tmp_path / "attempt-outcome.json"
    summary_path = tmp_path / "attempt-summary.md"

    monkeypatch.setattr(
        cli_module,
        "finalize_qualification",
        lambda *_args: decision,
    )

    status = cli_module.main(
        [
            "release",
            "finalize-live",
            "--workflow-run-id",
            str(live_attempt_binding.attempt.workflow_run_id),
            "--run-attempt",
            str(PLATFORM_RUN_ATTEMPT),
            "--target",
            target,
            *_uploaded_arguments(
                "attempt_binding",
                binding_path,
                live_attempt_binding.binding_digest,
                907,
            ),
            *_uploaded_arguments(
                "qualification_snapshot",
                snapshot_path,
                live_qualification_snapshot.snapshot_digest,
                908,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                decision_path,
                decision.decision_digest,
                909,
            ),
            *_referenced_uploaded_arguments(
                "publication_snapshot",
                publication_path,
                publication.snapshot_digest,
                publication_reference,
            ),
            *_referenced_uploaded_arguments(
                "approval_bundle",
                bundle_path,
                bundle.bundle_digest,
                bundle_reference,
            ),
            "--outcome-output",
            str(outcome_path),
            "--summary-output",
            str(summary_path),
        ]
    )

    assert status == 1
    outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=_transport_digest(outcome_path),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_attempt_binding.attempt.workflow_run_id,
            run_attempt=None,
            target=target,
        ),
    )
    assert isinstance(outcome, AttemptOutcome)
    assert outcome.result == "incomplete"
    assert outcome.terminal_phase == "approval-contract"
    assert outcome.approval_bundle_digest == bundle.bundle_digest
    assert outcome.publication_authorization_digest is None
    assert outcome.possibly_mutated is False
    assert summary_path.is_file()


@pytest.mark.parametrize(
    ("platform_flag", "expected_platform_facts"),
    [
        pytest.param(
            "--publication-preparation-interrupted",
            (True, False, False),
            id="publication-preparation-interrupted",
        ),
        pytest.param(
            "--platform-terminated",
            (False, True, False),
            id="platform-terminated",
        ),
        pytest.param(
            "--publication-may-have-started",
            (False, False, True),
            id="publication-may-have-started",
        ),
    ],
)
def test_finalize_live_forwards_loaded_downstream_records_transport_and_platform_facts(  # noqa: E501, PLR0913, PLR0915, PLR0917
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
    live_intent,
    live_attempt_binding,
    live_qualification_snapshot,
    platform_flag: str,
    expected_platform_facts: tuple[bool, bool, bool],
) -> None:
    """Forward admitted downstream records and exact platform facts."""
    attempt = live_attempt_binding.attempt
    artifact = _live_artifact(
        qualified_simulation.artifact,
        live_qualification_snapshot,
    )
    simulation_evidence_by_obligation = {
        evidence.obligation.obligation_id: evidence
        for evidence in qualified_simulation.evidence
    }
    live_evidence = tuple(
        replace(
            simulation_evidence_by_obligation[obligation.obligation_id],
            evidence_id=obligation.expected_evidence_id,
            subject=attempt,
            qualification_snapshot_digest=(
                live_qualification_snapshot.snapshot_digest
            ),
            obligation=obligation,
            workflow_run_id=attempt.workflow_run_id,
            run_attempt=None,
            artifact_digests=(
                (artifact.artifact_digest,)
                if simulation_evidence_by_obligation[
                    obligation.obligation_id
                ].artifact_digests
                else ()
            ),
        )
        for obligation in live_qualification_snapshot.obligations
    )
    decision = cli_module.finalize_qualification(
        live_qualification_snapshot,
        live_evidence,
        (artifact,),
    )
    assert decision.terminal_result == "success"
    publication = _action_bearing_publication(
        live_qualification_snapshot,
        decision,
        artifact,
    )
    action = publication.materialized_actions[0]
    projection = live_qualification_snapshot.destination_projections[0]
    control = f"workflow-delivery-v3:{live_intent.target}"
    approval_bundle = ApprovalBundle(
        attempt=attempt,
        publication_snapshot_reference=ArtifactReference(
            artifact_id=910,
            artifact_digest=publication.snapshot_digest,
            artifact_url="https://example.test/artifacts/910",
            payload_path="publication-snapshot.json",
            payload_digest=publication.snapshot_digest,
        ),
        reviewer_summary_reference=ArtifactReference(
            artifact_id=911,
            artifact_digest="sha256:" + ("7" * 64),
            artifact_url="https://example.test/artifacts/911",
            payload_path="reviewer-summary.md",
            payload_digest="sha256:" + ("8" * 64),
        ),
        producer="materialize-publication",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
    )
    authorization = PublicationAuthorization(
        attempt=attempt,
        approval_bundle_reference=ArtifactReference(
            artifact_id=912,
            artifact_digest=approval_bundle.bundle_digest,
            artifact_url="https://example.test/artifacts/912",
            payload_path="approval-bundle.json",
            payload_digest=approval_bundle.bundle_digest,
        ),
        approval_boundary=ApprovalBoundary(
            environment="workflow-delivery-v3-buddy-approval",
            job="approve-publication",
            sentinel_name="WDV3_APPROVAL_ENVIRONMENT_MARKER",
            sentinel_value="workflow-delivery-v3-buddy-approval/v1",
            sentinel_result="success",
        ),
        governance_proof=GovernanceProof(
            provenance=live_attempt_binding.attestation_provenance,
            current_main_sha=live_intent.target,
            observed_at="2026-08-19T07:59:00Z",
            expires_at="2026-09-01T00:00:00Z",
            live_enabled=True,
        ),
        completed_at="2026-08-19T08:00:00Z",
        producer="approve-publication",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
    )
    receipt = Receipt(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        coordinate=projection.coordinate,
        mutable_resource_keys=action.mutable_resource_keys,
        lock_group=action.serialization_projection,
        artifact_transport=artifact.transport,
        artifact_content_sha256=artifact.content.content_sha256,
        artifact_content_sha512=artifact.content.content_sha512,
        witness_digest=artifact.witness_digest,
        creation_result="created",
        tag_mapping=(
            (
                f"buddy-sha-{attempt.execution.target}",
                projection.coordinate.native_version,
            ),
        ),
        response_identity_digest="sha256:" + ("c" * 64),
        producer="publish-github-packages",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
    )

    binding_path = _write(
        tmp_path / "forwarded-attempt-binding.json",
        canonicalize(live_attempt_binding.to_document()),
    )
    snapshot_path = _write(
        tmp_path / "forwarded-qualification-snapshot.json",
        canonicalize(live_qualification_snapshot.to_document()),
    )
    decision_path = _write(
        tmp_path / "forwarded-qualification-decision.json",
        canonicalize(decision.to_document()),
    )
    artifact_path = _write(
        tmp_path / "forwarded-release-artifact.json",
        canonicalize(artifact.to_document()),
    )
    publication_path = _write(
        tmp_path / "forwarded-publication-snapshot.json",
        canonicalize(publication.to_document()),
    )
    approval_bundle_path = _write(
        tmp_path / "forwarded-approval-bundle.json",
        canonicalize(approval_bundle.to_document()),
    )
    authorization_path = _write(
        tmp_path / "forwarded-publication-authorization.json",
        canonicalize(authorization.to_document()),
    )
    action_result = ActionResult(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.serialization_projection,
        outcome="success",
        mutation_disposition="created",
        response_identity_digest=receipt.response_identity_digest,
        receipt=receipt,
        diagnostic_reference=None,
        producer="publish-github-packages",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
    )
    action_result_path = _write(
        tmp_path / "forwarded-action-result.json",
        canonicalize(action_result.to_document()),
    )
    expected_outcome = AttemptOutcome(
        attempt=attempt,
        qualification_decision_digest=decision.decision_digest,
        publication_snapshot_digest=publication.snapshot_digest,
        approval_bundle_digest=approval_bundle.bundle_digest,
        publication_authorization_digest=(authorization.authorization_digest),
        action_result_digests=(action_result.result_digest,),
        terminal_phase="finalized",
        result="success",
        uncertainty=False,
        possibly_mutated=False,
        next_action="none",
    )
    captured_calls: list[dict[str, object]] = []

    def capture_finalize_attempt_outcome(  # noqa: PLR0913
        *,
        attempt: ReleaseAttemptIdentity,
        qualification_decision: QualificationDecision,
        publication_snapshot: PublicationSnapshot | None,
        exact_satisfied_governance_proof: (
            ExactSatisfiedGovernanceProof | None
        ),
        approval_bundle: ApprovalBundle | None,
        publication_authorization: PublicationAuthorization | None,
        action_results: tuple[ActionResult, ...],
        qualification_snapshot: QualificationSnapshot | None = None,
        release_artifact: ReleaseArtifact | None = None,
        destination_operation_profile: DestinationOperationProfile | None = (
            None
        ),
        publication_snapshot_reference: ArtifactReference | None = None,
        approval_bundle_reference: ArtifactReference | None = None,
        observations: tuple[ProjectionObservation, ...] = (),
        publication_preparation_interrupted: bool = False,
        platform_terminated: bool = False,
        publication_may_have_started: bool = False,
    ) -> AttemptOutcome:
        captured_calls.append(
            {
                "attempt": attempt,
                "qualification_decision": qualification_decision,
                "publication_snapshot": publication_snapshot,
                "exact_satisfied_governance_proof": (
                    exact_satisfied_governance_proof
                ),
                "approval_bundle": approval_bundle,
                "publication_authorization": publication_authorization,
                "action_results": action_results,
                "qualification_snapshot": qualification_snapshot,
                "release_artifact": release_artifact,
                "destination_operation_profile": (
                    destination_operation_profile
                ),
                "publication_snapshot_reference": (
                    publication_snapshot_reference
                ),
                "approval_bundle_reference": approval_bundle_reference,
                "observations": observations,
                "publication_preparation_interrupted": (
                    publication_preparation_interrupted
                ),
                "platform_terminated": platform_terminated,
                "publication_may_have_started": (publication_may_have_started),
            }
        )
        return expected_outcome

    monkeypatch.setattr(
        cli_module,
        "finalize_attempt_outcome",
        capture_finalize_attempt_outcome,
    )
    monkeypatch.setattr(
        cli_module,
        "finalize_qualification",
        lambda *_args: decision,
    )
    outcome_path = tmp_path / "forwarded-attempt-outcome.json"
    summary_path = tmp_path / "forwarded-attempt-summary.md"

    status = cli_module.main(
        [
            "release",
            "finalize-live",
            "--workflow-run-id",
            str(live_intent.workflow_run_id),
            "--run-attempt",
            str(PLATFORM_RUN_ATTEMPT),
            "--target",
            live_intent.target,
            *_uploaded_arguments(
                "attempt_binding",
                binding_path,
                live_attempt_binding.binding_digest,
                914,
            ),
            *_uploaded_arguments(
                "qualification_snapshot",
                snapshot_path,
                live_qualification_snapshot.snapshot_digest,
                915,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                decision_path,
                decision.decision_digest,
                916,
            ),
            *_uploaded_arguments(
                "release_artifact",
                artifact_path,
                artifact.artifact_digest,
                917,
            ),
            *_referenced_uploaded_arguments(
                "publication_snapshot",
                publication_path,
                publication.snapshot_digest,
                approval_bundle.publication_snapshot_reference,
            ),
            *_referenced_uploaded_arguments(
                "approval_bundle",
                approval_bundle_path,
                approval_bundle.bundle_digest,
                authorization.approval_bundle_reference,
            ),
            *_uploaded_arguments(
                "publication_authorization",
                authorization_path,
                authorization.authorization_digest,
                919,
            ),
            *_uploaded_arguments(
                "action_result",
                action_result_path,
                action_result.result_digest,
                921,
            ),
            platform_flag,
            "--outcome-output",
            str(outcome_path),
            "--summary-output",
            str(summary_path),
        ]
    )

    assert status == 0
    assert len(captured_calls) == 1
    captured = captured_calls[0]
    assert captured["attempt"] == attempt
    assert captured["attempt"] is not attempt
    assert captured["qualification_decision"] == decision
    assert captured["qualification_decision"] is not decision
    assert captured["qualification_snapshot"] == live_qualification_snapshot
    assert captured["qualification_snapshot"] is not live_qualification_snapshot
    assert captured["release_artifact"] == artifact
    assert captured["release_artifact"] is not artifact
    assert (
        captured["destination_operation_profile"]
        == github_packages_destination_operation_profile()
    )
    assert type(captured["publication_snapshot"]) is PublicationSnapshot
    assert captured["publication_snapshot"] == publication
    assert captured["publication_snapshot"] is not publication
    assert captured["exact_satisfied_governance_proof"] is None
    assert type(captured["approval_bundle"]) is ApprovalBundle
    assert captured["approval_bundle"] == approval_bundle
    assert captured["approval_bundle"] is not approval_bundle
    assert (
        type(captured["publication_authorization"]) is PublicationAuthorization
    )
    assert captured["publication_authorization"] == authorization
    assert captured["publication_authorization"] is not authorization
    assert (
        captured["publication_snapshot_reference"]
        == approval_bundle.publication_snapshot_reference
    )
    assert (
        captured["approval_bundle_reference"]
        == authorization.approval_bundle_reference
    )
    loaded_action_results = captured["action_results"]
    assert isinstance(loaded_action_results, tuple)
    assert loaded_action_results == (action_result,)
    assert type(loaded_action_results[0]) is ActionResult
    assert loaded_action_results[0] is not action_result
    assert loaded_action_results[0].receipt == receipt
    assert loaded_action_results[0].receipt is not receipt
    assert captured["observations"] == ()
    assert (
        captured["publication_preparation_interrupted"],
        captured["platform_terminated"],
        captured["publication_may_have_started"],
    ) == expected_platform_facts

    admitted_outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=expected_outcome.outcome_digest,
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=None,
            target=live_intent.target,
        ),
    )
    assert type(admitted_outcome) is AttemptOutcome
    assert admitted_outcome == expected_outcome
    assert outcome_path.read_bytes() == canonicalize(
        expected_outcome.to_document()
    )
    assert summary_path.read_text(encoding="utf-8") == (
        "# Workflow Delivery v3 live finalization\n\n"
        "- Result: `success`\n"
        "- Terminal phase: `finalized`\n"
        "- Next action: `none`\n"
    )
