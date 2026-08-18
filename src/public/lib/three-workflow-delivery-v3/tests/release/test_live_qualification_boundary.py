"""Scenario coverage for the shared live qualification boundary."""

from __future__ import annotations

import hashlib
from dataclasses import replace
from pathlib import Path

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
