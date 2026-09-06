"""Scenario coverage for the shared live qualification boundary."""

from __future__ import annotations

import hashlib
import json
from dataclasses import replace
from pathlib import Path
from typing import TYPE_CHECKING
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    PublicationObservationReference,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    RemoteStateObservation,
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
from three_workflow_delivery_v3.release.planner import (
    plan_live_qualification,
)
from three_workflow_delivery_v3.release.simulation import (
    release_adapter_context_from_bytes,
)
from three_workflow_delivery_v3.release.workflow import (
    node_build_request,
)

from .observation_fixtures import (
    authority_arguments,
    qualification_arguments,
    uploaded_arguments,
)
from .test_observation_admission import (
    _observation,
)
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference

REPO_ROOT = Path(__file__).resolve().parents[6]
PLATFORM_RUN_ATTEMPT = 3


@pytest.fixture
def live_attempt_binding(
    observation_case,
) -> ReleaseAttemptBinding:
    """Retain the exact parser-admitted Eligibility transport and provenance."""
    return observation_case.attempt_binding


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
    reference_arguments = []
    if name == "qualification_decision":
        run_id = json.loads(path.read_bytes())["subject"]["workflow-run-id"]
        reference_arguments = [
            "--qualification-decision-artifact-url",
            f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/{artifact_id}",
            "--qualification-decision-payload-path",
            path.name,
        ]
    return [
        f"--{option}",
        str(path),
        f"--{option}-digest",
        semantic_digest,
        f"--{option}-artifact-id",
        str(artifact_id),
        f"--{option}-artifact-digest",
        _transport_digest(path),
        *reference_arguments,
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
    case,
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    decision_reference: ArtifactReference,
    *,
    classification: str,
) -> RemoteStateObservation:
    return replace(
        _observation(
            replace(
                case,
                snapshot=snapshot,
                artifact=artifact,
                decision_reference=decision_reference,
            ),
            classification=(
                classification
                if classification in {"absent", "exact-satisfied"}
                else "absent"
            ),
        ),
        classification=classification,
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


def test_live_plan_build_transport_and_qualification_are_attempt_bound(  # noqa: PLR0913, PLR0917
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


@pytest.mark.parametrize(
    ("classification", "action_count"),
    [
        pytest.param("exact-satisfied", 0, id="zero-action"),
        pytest.param("absent", 1, id="action-bearing"),
    ],
)
def test_materialize_publication_cli_emits_path_specific_review_outputs(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    observation_case,
    classification: str,
    action_count: int,
) -> None:
    """Emit reviewer evidence only when a publication action needs approval."""
    case = observation_case
    live_intent = case.intent
    artifact = case.artifact
    observation = _observation(case, classification=classification)
    observation_path = _write(
        tmp_path / "observation.json",
        canonicalize(observation.to_document()),
    )
    projection = case.snapshot.destination_projections[0]
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
            *authority_arguments(tmp_path, case, monkeypatch),
            *qualification_arguments(tmp_path, case),
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
    publication = admit_release_record(
        output_path.read_bytes(),
        expected_type=PublicationSnapshot,
        expected_digest=_transport_digest(output_path),
    )
    assert len(publication.materialized_actions) == action_count
    emitted = github_output.read_text(encoding="utf-8")
    if not action_count:
        assert not summary_path.exists()
        assert "publish-required=false\n" in emitted
        assert "resource-concurrency-key=no-op\n" in emitted
        assert "reviewer-digest=" not in emitted
        return

    assert publication.materialized_actions[
        0
    ].destination_operation_profile_digest == (
        github_packages_destination_operation_profile().profile_digest
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
    observation_case,
) -> None:
    """Reject a workflow-selected ref that differs from immutable Intent."""
    case = observation_case
    live_intent = case.intent
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
            *authority_arguments(tmp_path, case, monkeypatch),
            *qualification_arguments(tmp_path, case),
            *uploaded_arguments(
                tmp_path,
                "observation",
                _observation(case).to_document(),
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
    "exact_satisfied_finalization_proof",
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
