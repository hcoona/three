"""Scenario coverage for the shared live qualification boundary."""

from __future__ import annotations

import hashlib
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    PublicationObservationReference,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    admit_release_record,
    publication_capability_requirements,
    publication_mutable_resource_key_basis,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
)
from three_workflow_delivery_v3.release.simulation import (
    release_adapter_context_from_bytes,
)
from three_workflow_delivery_v3.release.workflow import (
    node_build_request,
)

REPO_ROOT = Path(__file__).resolve().parents[6]


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


def test_live_plan_build_transport_and_finalization_are_attempt_bound(  # noqa: PLR0913, PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
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
        str(live_intent.run_attempt),
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
            run_attempt=live_intent.run_attempt,
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
            run_attempt=live_intent.run_attempt,
            target=live_intent.target,
        ),
    )
    assert isinstance(outcome, AttemptOutcome)
    assert outcome.attempt == live_attempt_binding.attempt
    assert outcome.terminal_phase == "qualification"
    assert outcome.result == "incomplete"
    assert outcome.publication_snapshot_digest is None
    assert outcome.authorization_digest is None
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
            run_attempt=live_intent.run_attempt,
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
            run_attempt=live_intent.run_attempt,
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
            run_attempt=live_intent.run_attempt,
            target=live_intent.target,
        ),
    )
    assert isinstance(artifact, ReleaseArtifact)
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
    authorization = AuthorizationRecord(
        attempt=live_attempt_binding.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        reviewer_summary_artifact_id=114,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest="sha256:" + ("3" * 64),
        workflow_run_id=live_intent.workflow_run_id,
        run_attempt=live_intent.run_attempt,
        approval_job_id=711,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-smoke-approval",
        channel="buddy",
        completed_at="2026-08-13T16:00:00Z",
        producer="approval",
        control=f"workflow-delivery-v3:{live_intent.target}",
    )
    publication_path = _write(
        tmp_path / "live-publication-snapshot.json",
        canonicalize(publication.to_document()),
    )
    authorization_path = _write(
        tmp_path / "live-authorization.json",
        canonicalize(authorization.to_document()),
    )
    publication_arguments = _uploaded_arguments(
        "publication_snapshot",
        publication_path,
        publication.snapshot_digest,
        114,
    )
    authorization_arguments = _uploaded_arguments(
        "authorization",
        authorization_path,
        authorization.authorization_digest,
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
                *authorization_arguments,
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
            run_attempt=live_intent.run_attempt,
            target=live_intent.target,
        ),
    )
    assert isinstance(success_outcome, AttemptOutcome)
    assert success_outcome.result == "success"
    assert success_outcome.terminal_phase == "finalized-no-op"
    assert success_outcome.publication_snapshot_digest == (
        publication.snapshot_digest
    )
    assert success_outcome.authorization_digest == (
        authorization.authorization_digest
    )

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
                *authorization_arguments,
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
        run_attempt=qualified_simulation.binding.simulation.run_attempt,
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
    "publication_snapshot",
    "authorization",
    "capability_decision",
    "capability_group_bundle",
    "receipt",
)
_OPTIONAL_TRANSPORT_MEMBERS = (
    "path",
    "record-digest",
    "artifact-id",
    "artifact-digest",
)
_PARTIAL_OPTIONAL_TRANSPORT_CASES = [
    *(
        pytest.param(
            group,
            member,
            "missing",
            id=f"{group.replace('_', '-')}-missing-{member}",
        )
        for group in _OPTIONAL_TRANSPORT_GROUPS
        for member in _OPTIONAL_TRANSPORT_MEMBERS
    ),
    *(
        pytest.param(
            group,
            member,
            "only",
            id=f"{group.replace('_', '-')}-only-{member}",
        )
        for group in _OPTIONAL_TRANSPORT_GROUPS
        for member in _OPTIONAL_TRANSPORT_MEMBERS
    ),
]


@pytest.mark.parametrize(
    ("group", "selected_member", "provided_member_mode"),
    _PARTIAL_OPTIONAL_TRANSPORT_CASES,
)
def test_finalize_live_rejects_each_partial_optional_transport_group(  # noqa: PLR0913
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
    """Reject every partial downstream transport before finalization writes."""
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
            str(live_intent.run_attempt),
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
            "--capability-may-have-started",
            (False, False, True),
            id="capability-may-have-started",
        ),
    ],
)
def test_finalize_live_forwards_loaded_downstream_records_transport_and_platform_facts(  # noqa: E501, PLR0913, PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    live_intent,
    live_attempt_binding,
    live_qualification_snapshot,
    platform_flag: str,
    expected_platform_facts: tuple[bool, bool, bool],
) -> None:
    """Forward admitted downstream records and exact platform facts."""
    from three_workflow_delivery_v3.records.artifacts import (  # noqa: PLC0415
        ArtifactTransportIdentity,
    )
    from three_workflow_delivery_v3.records.release import (  # noqa: PLC0415
        ActionResult,
        CapabilityAdmissionDecision,
        CapabilityGroupResultBundle,
        Receipt,
        ReceiptTransportReference,
        publication_capability_group,
        publication_lock_group,
        publication_mutable_resource_keys,
    )

    attempt = live_attempt_binding.attempt
    decision = cli_module.finalize_qualification(
        live_qualification_snapshot,
        (),
        (),
    )
    projection = live_qualification_snapshot.destination_projections[0]
    artifact_digest = "sha256:" + ("4" * 64)
    action_digest = "sha256:" + ("5" * 64)
    action_id = projection.potential_action_id
    capability_group = publication_capability_group(projection)
    mutable_resource_keys = publication_mutable_resource_keys(
        projection,
        attempt,
    )
    lock_group = publication_lock_group(projection)
    control = f"workflow-delivery-v3:{live_intent.target}"
    publication = PublicationSnapshot(
        attempt=attempt,
        qualification_snapshot_digest=live_qualification_snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(artifact_digest,),
        artifact_output_ids=(projection.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("6" * 64),
                classification="exact-satisfied",
            ),
        ),
        materialized_actions=(),
    )
    authorization = AuthorizationRecord(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        reviewer_summary_artifact_id=911,
        reviewer_summary_upload_digest="sha256:" + ("7" * 64),
        reviewer_summary_payload_digest="sha256:" + ("8" * 64),
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
        approval_job_id=912,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-smoke-approval",
        channel="buddy",
        completed_at="2026-08-19T08:00:00Z",
        producer="approval",
        control=control,
    )
    capability_decision = CapabilityAdmissionDecision(
        attempt=attempt,
        authorization_digest=authorization.authorization_digest,
        publication_snapshot_digest=publication.snapshot_digest,
        reviewer_summary_artifact_id=authorization.reviewer_summary_artifact_id,
        reviewer_summary_upload_digest=(
            authorization.reviewer_summary_upload_digest
        ),
        reviewer_summary_payload_digest=(
            authorization.reviewer_summary_payload_digest
        ),
        action_digests=(action_digest,),
        artifact_digests=(artifact_digest,),
        resource_key_sets=((action_id, mutable_resource_keys),),
        lock_groups=((action_id, lock_group),),
        capability_group_manifest=((capability_group, (action_id,)),),
        live_eligibility_artifact_id=(
            live_attempt_binding.live_eligibility_artifact_id
        ),
        live_eligibility_artifact_digest=(
            live_attempt_binding.live_eligibility_artifact_digest
        ),
        governance_provenance=live_attempt_binding.attestation_provenance,
        governance_content_sha256=dict(
            live_attempt_binding.attestation_provenance
        )["content-sha256"],
        governance_expires_at="2026-09-01T00:00:00Z",
        governance_live_enabled=True,
        producer="approval-finalizer",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
        result="success",
        diagnostics=(),
    )
    artifact_transport = ArtifactTransportIdentity(
        artifact_id=913,
        artifact_name="forwarded-release.tgz",
        artifact_url="https://example.test/artifacts/913",
        transport_digest=artifact_digest,
        producer="build-tarball",
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
    )
    receipt = Receipt(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action_id,
        action_digest=action_digest,
        coordinate=projection.coordinate,
        mutable_resource_keys=mutable_resource_keys,
        lock_group=lock_group,
        artifact_transport=artifact_transport,
        artifact_content_sha256="sha256:" + ("9" * 64),
        artifact_content_sha512="sha512:" + ("a" * 128),
        witness_digest="sha256:" + ("b" * 64),
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
        run_attempt=attempt.run_attempt,
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
    publication_path = _write(
        tmp_path / "forwarded-publication-snapshot.json",
        canonicalize(publication.to_document()),
    )
    authorization_path = _write(
        tmp_path / "forwarded-authorization.json",
        canonicalize(authorization.to_document()),
    )
    capability_path = _write(
        tmp_path / "forwarded-capability-admission-decision.json",
        canonicalize(capability_decision.to_document()),
    )
    receipt_path = _write(
        tmp_path / "forwarded-publication-receipt.json",
        canonicalize(receipt.to_document()),
    )
    receipt_artifact_id = 919
    receipt_upload_digest = _transport_digest(receipt_path)
    action_result = ActionResult(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action_id,
        action_digest=action_digest,
        lock_group=lock_group,
        outcome="success",
        mutation_disposition="created",
        response_identity_digest=receipt.response_identity_digest,
        receipt_artifact_id=receipt_artifact_id,
        receipt_artifact_name=receipt_path.name,
        receipt_artifact_digest=receipt_upload_digest,
        receipt_payload_digest=receipt.receipt_digest,
        receipt_digest=receipt.receipt_digest,
        diagnostic_reference=None,
        producer="publish-github-packages",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
    )
    group_bundle = CapabilityGroupResultBundle(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        capability_group=capability_group,
        planned_action_ids=(action_id,),
        action_results=(action_result,),
        completion_state="complete",
        producer="publish-github-packages",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
    )
    group_bundle_path = _write(
        tmp_path / "forwarded-capability-group-result-bundle.json",
        canonicalize(group_bundle.to_document()),
    )
    expected_receipt_transport = ReceiptTransportReference(
        action_id=receipt.action_id,
        artifact_id=receipt_artifact_id,
        artifact_name=receipt_path.name,
        upload_digest=receipt_upload_digest,
        payload_digest=receipt.receipt_digest,
    )
    expected_outcome = AttemptOutcome(
        attempt=attempt,
        qualification_decision_digest=decision.decision_digest,
        publication_snapshot_digest=publication.snapshot_digest,
        authorization_digest=authorization.authorization_digest,
        capability_admission_digests=(capability_decision.decision_digest,),
        capability_group_bundle_digests=(group_bundle.bundle_digest,),
        receipt_digests=(receipt.receipt_digest,),
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
        authorization: AuthorizationRecord | None,
        capability_decisions: tuple[CapabilityAdmissionDecision, ...],
        group_bundles: tuple[CapabilityGroupResultBundle, ...],
        receipts: tuple[Receipt, ...],
        receipt_transport_references: tuple[
            ReceiptTransportReference, ...
        ] = (),
        publication_preparation_interrupted: bool = False,
        platform_terminated: bool = False,
        capability_may_have_started: bool = False,
    ) -> AttemptOutcome:
        captured_calls.append(
            {
                "attempt": attempt,
                "qualification_decision": qualification_decision,
                "publication_snapshot": publication_snapshot,
                "authorization": authorization,
                "capability_decisions": capability_decisions,
                "group_bundles": group_bundles,
                "receipts": receipts,
                "receipt_transport_references": (receipt_transport_references),
                "publication_preparation_interrupted": (
                    publication_preparation_interrupted
                ),
                "platform_terminated": platform_terminated,
                "capability_may_have_started": capability_may_have_started,
            }
        )
        return expected_outcome

    monkeypatch.setattr(
        cli_module,
        "finalize_attempt_outcome",
        capture_finalize_attempt_outcome,
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
            str(live_intent.run_attempt),
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
                "publication_snapshot",
                publication_path,
                publication.snapshot_digest,
                917,
            ),
            *_uploaded_arguments(
                "authorization",
                authorization_path,
                authorization.authorization_digest,
                918,
            ),
            *_uploaded_arguments(
                "capability_decision",
                capability_path,
                capability_decision.decision_digest,
                920,
            ),
            *_uploaded_arguments(
                "capability_group_bundle",
                group_bundle_path,
                group_bundle.bundle_digest,
                921,
            ),
            *_uploaded_arguments(
                "receipt",
                receipt_path,
                receipt.receipt_digest,
                receipt_artifact_id,
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
    assert type(captured["publication_snapshot"]) is PublicationSnapshot
    assert captured["publication_snapshot"] == publication
    assert captured["publication_snapshot"] is not publication
    assert type(captured["authorization"]) is AuthorizationRecord
    assert captured["authorization"] == authorization
    assert captured["authorization"] is not authorization

    loaded_capability_decisions = captured["capability_decisions"]
    assert isinstance(loaded_capability_decisions, tuple)
    assert loaded_capability_decisions == (capability_decision,)
    assert type(loaded_capability_decisions[0]) is CapabilityAdmissionDecision
    assert loaded_capability_decisions[0] is not capability_decision
    loaded_group_bundles = captured["group_bundles"]
    assert isinstance(loaded_group_bundles, tuple)
    assert loaded_group_bundles == (group_bundle,)
    assert type(loaded_group_bundles[0]) is CapabilityGroupResultBundle
    assert loaded_group_bundles[0] is not group_bundle
    loaded_receipts = captured["receipts"]
    assert isinstance(loaded_receipts, tuple)
    assert loaded_receipts == (receipt,)
    assert type(loaded_receipts[0]) is Receipt
    assert loaded_receipts[0] is not receipt

    loaded_receipt_transports = captured["receipt_transport_references"]
    assert isinstance(loaded_receipt_transports, tuple)
    assert loaded_receipt_transports == (expected_receipt_transport,)
    assert type(loaded_receipt_transports[0]) is ReceiptTransportReference
    assert loaded_receipt_transports[0].artifact_id == receipt_artifact_id
    assert loaded_receipt_transports[0].artifact_name == receipt_path.name
    assert loaded_receipt_transports[0].upload_digest == receipt_upload_digest
    assert loaded_receipt_transports[0].payload_digest == receipt.receipt_digest
    assert (
        captured["publication_preparation_interrupted"],
        captured["platform_terminated"],
        captured["capability_may_have_started"],
    ) == expected_platform_facts

    admitted_outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=expected_outcome.outcome_digest,
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=live_intent.workflow_run_id,
            run_attempt=live_intent.run_attempt,
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
