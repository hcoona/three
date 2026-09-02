"""Scenario tests for commit-6 qualification and simulation finalization."""

from __future__ import annotations

# ruff: noqa: D103, EM101, PLR2004, TRY003
from dataclasses import replace
from typing import cast

import pytest
import three_workflow_delivery_v3.release as release_api
from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    PublicationAction,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.release.finalizer import (
    _admit_synthetic_projection_observation,
    finalize_qualification,
    finalize_simulation,
    materialize_hypothetical_actions,
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.qualification import (
    admit_evidence_for_snapshot,
    execute_release_build,
    form_uploaded_release_artifact,
)


def test_complete_qualification_succeeds_with_exact_artifact_binding(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    artifact = scenario.artifact
    decision = scenario.decision

    assert decision.terminal_result == "success"
    assert decision.failure_class == "none"
    assert len(decision.admitted_evidence_digests) == 4
    assert decision.admitted_artifact_digests == (artifact.artifact_digest,)
    assert artifact.repository == "hcoona/three"
    assert artifact.output == scenario.snapshot.outputs[0]
    assert artifact.transport.workflow_run_id == (
        scenario.binding.simulation.workflow_run_id
    )
    assert artifact.transport.run_attempt == (
        scenario.binding.simulation.run_attempt
    )
    assert artifact.content.content_sha256.startswith("sha256:")
    assert artifact.content.content_sha512 is not None
    assert artifact.provenance_digest.startswith("sha256:")
    assert scenario.evidence[2].obligation.definition_id == (
        "node/npm-artifact-contents-v1"
    )
    assert scenario.evidence[3].obligation.definition_id == (
        "node/npm-install-import-v1"
    )
    assert scenario.evidence[2].evidence_id != scenario.evidence[3].evidence_id


def test_build_adapter_failure_forms_failed_evidence_and_no_artifact(
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
) -> None:
    scenario = qualified_simulation

    def fail_build(_request):
        raise ValueError("mechanical build failed")

    monkeypatch.setattr(node_adapter, "build_node_package", fail_build)
    mechanics, evidence = execute_release_build(
        scenario.snapshot,
        scenario.request,
    )

    assert mechanics is None
    assert evidence is not None
    assert evidence.normalized_outcome == "failed"
    assert evidence.artifact_digests == ()
    assert evidence.diagnostics == ("mechanical build failed",)


def test_build_transport_rejects_prior_attempt_substitution(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    prior_transport = ArtifactTransportIdentity(
        artifact_id=803,
        artifact_name=release_artifact_transport_name(
            repository=scenario.snapshot.repository,
            purpose="release-simulation",
            output=scenario.snapshot.outputs[0],
            qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
            workflow_run_id=scenario.binding.simulation.workflow_run_id,
            run_attempt=2,
            producer="build-tarball",
        ),
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/7301/artifacts/803"
        ),
        transport_digest="sha256:" + ("f" * 64),
        producer="build-tarball",
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
        run_attempt=2,
    )

    with pytest.raises(ValueError, match="another run context"):
        form_uploaded_release_artifact(
            scenario.snapshot,
            scenario.mechanics,
            prior_transport,
        )


def test_upload_metadata_binds_after_single_mechanical_build(
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    calls = 0

    def build_once(_request):
        nonlocal calls
        calls += 1
        return scenario.build_result

    monkeypatch.setattr(node_adapter, "build_node_package", build_once)
    mechanics, failed_evidence = execute_release_build(
        scenario.snapshot,
        scenario.request,
    )
    assert mechanics is not None
    assert failed_evidence is None
    assert not hasattr(mechanics, "transport")
    assert mechanics.normalized_outcome == "satisfied"
    assert mechanics.tarball is scenario.build_result.tarball
    assert calls == 1

    transport = ArtifactTransportIdentity(
        artifact_id=804,
        artifact_name=release_artifact_transport_name(
            repository=scenario.snapshot.repository,
            purpose=mechanics.purpose,
            output=mechanics.output,
            qualification_snapshot_digest=(
                mechanics.qualification_snapshot_digest
            ),
            workflow_run_id=mechanics.subject.workflow_run_id,
            run_attempt=scenario.binding.simulation.run_attempt,
            producer="build-tarball",
        ),
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/7301/artifacts/804"
        ),
        transport_digest="sha256:" + ("e" * 64),
        producer="build-tarball",
        workflow_run_id=mechanics.subject.workflow_run_id,
        run_attempt=scenario.binding.simulation.run_attempt,
    )
    assert transport.artifact_name.startswith(
        "wdv3-release-simulation-primary-package-ra3-"
    )
    assert transport.artifact_name.endswith(".tgz")
    artifact, evidence = form_uploaded_release_artifact(
        scenario.snapshot,
        mechanics,
        transport,
    )

    assert calls == 1
    assert artifact.content.content_sha256 == mechanics.content.content_sha256
    assert evidence.artifact_digests == (artifact.artifact_digest,)

    with pytest.raises(ValueError, match="transport name is not exact"):
        form_uploaded_release_artifact(
            scenario.snapshot,
            mechanics,
            replace(transport, artifact_name=f"{transport.artifact_name}-x"),
        )
    with pytest.raises(ValueError, match="transport name is not exact"):
        form_uploaded_release_artifact(
            scenario.snapshot,
            mechanics,
            replace(
                transport,
                artifact_name=transport.artifact_name.removesuffix(".tgz"),
            ),
        )
    with pytest.raises(ValueError, match="transport URL is not exact"):
        form_uploaded_release_artifact(
            scenario.snapshot,
            mechanics,
            replace(
                transport,
                artifact_url=(
                    "https://github.com/other/repository/actions/runs/"
                    "7301/artifacts/804"
                ),
            ),
        )
    with pytest.raises(ValueError, match="producer is not exact"):
        form_uploaded_release_artifact(
            scenario.snapshot,
            mechanics,
            replace(transport, producer="other-producer"),
        )
    assert calls == 1


def test_definitive_failure_continues_to_closed_failed_decision(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    failed_build = replace(
        scenario.evidence[0],
        raw_result="failure",
        normalized_outcome="failed",
        artifact_digests=(),
        result_facts=(),
        diagnostics=("build failed",),
    )
    decision = finalize_qualification(
        scenario.snapshot,
        (failed_build, scenario.evidence[1]),
        (),
    )

    assert decision.terminal_result == "failure"
    assert decision.failure_class == "quality-failure"
    assert tuple(
        disposition.outcome for disposition in decision.obligation_dispositions
    ) == ("failed", "satisfied", "incomplete", "incomplete")
    assert decision.obligation_dispositions[2].explanation.endswith(
        "blocked-by-prerequisite"
    )
    assert decision.obligation_dispositions[3].explanation.endswith(
        "blocked-by-prerequisite"
    )


@pytest.mark.parametrize("evidence_index", [2, 3])
def test_failed_artifact_dependent_evidence_with_unknown_artifact_is_incomplete(
    qualified_simulation,
    evidence_index: int,
) -> None:
    scenario = qualified_simulation
    failed_evidence = replace(
        scenario.evidence[evidence_index],
        raw_result="failure",
        normalized_outcome="failed",
        artifact_digests=("sha256:" + ("f" * 64),),
        diagnostics=("artifact-dependent check failed",),
    )
    evidence = (
        *scenario.evidence[:evidence_index],
        failed_evidence,
        *scenario.evidence[evidence_index + 1 :],
    )

    decision = finalize_qualification(
        scenario.snapshot,
        evidence,
        (scenario.artifact,),
    )

    assert decision.terminal_result == "incomplete"
    assert decision.failure_class == "incomplete-qualification"
    assert decision.next_action == "rerun-simulation"


def test_missing_evidence_finalizes_incomplete_without_false_success(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    decision = finalize_qualification(
        scenario.snapshot,
        scenario.evidence[:-1],
        (scenario.artifact,),
    )

    assert decision.terminal_result == "incomplete"
    assert decision.failure_class == "incomplete-qualification"
    assert decision.obligation_dispositions[-1].outcome == "incomplete"
    assert decision.obligation_dispositions[-1].evidence_digests == ()


def test_duplicate_evidence_is_rejected(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation

    with pytest.raises(ValueError, match="duplicate or conflicting"):
        finalize_qualification(
            scenario.snapshot,
            (*scenario.evidence, scenario.evidence[0]),
            (scenario.artifact,),
        )


def test_cross_purpose_and_prior_attempt_evidence_are_rejected(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    evidence = scenario.evidence[1]
    prior_simulation = replace(
        scenario.binding.simulation,
        run_attempt=2,
    )
    prior_evidence = replace(
        evidence,
        subject=prior_simulation,
        run_attempt=2,
    )
    with pytest.raises(ValueError, match="current Snapshot"):
        admit_evidence_for_snapshot(scenario.snapshot, prior_evidence)

    live_attempt = ReleaseAttemptIdentity(
        execution=OfficialExecutionIdentity(
            OfficialProductIdentity(
                "official",
                scenario.snapshot.release_unit,
                scenario.snapshot.nbgv.canonical_version,
            ),
            scenario.snapshot.target,
        ),
        workflow_run_id=evidence.workflow_run_id,
    )
    live_evidence = replace(evidence, subject=live_attempt, run_attempt=None)
    with pytest.raises(ValueError, match="current Snapshot"):
        admit_evidence_for_snapshot(scenario.snapshot, live_evidence)


def test_synthetic_absent_and_exact_observations_plan_actions_only(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    absent = _admit_synthetic_projection_observation(
        scenario.snapshot,
        scenario.decision,
        scenario.artifact,
        classification="absent",
    )
    absent_actions = materialize_hypothetical_actions(
        scenario.snapshot,
        scenario.decision,
        (absent,),
        (scenario.artifact,),
    )
    assert len(absent_actions) == 1
    assert (
        absent_actions[0]
        .mutable_resource_keys[0]
        .startswith("external-package-coordinate:")
    )
    assert absent_actions[0].capability_requirements == (
        "npmjs/trusted-publishing-oidc-v1",
    )

    exact = _admit_synthetic_projection_observation(
        scenario.snapshot,
        scenario.decision,
        scenario.artifact,
        classification="exact-satisfied",
        owner="hcoona",
    )
    exact_actions = materialize_hypothetical_actions(
        scenario.snapshot,
        scenario.decision,
        (exact,),
        (scenario.artifact,),
    )
    assert exact_actions == ()

    substituted_value = replace(
        exact.value,
        content_sha512="sha512:" + ("c" * 128),
    )
    substituted_exact = replace(
        exact,
        value=substituted_value,
        response_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/observation-response",
                "request-digest": exact.request_digest,
                "facts": exact.response_facts.to_document(),
                "value": substituted_value.to_document(),
            }
        ),
    )
    with pytest.raises(ValueError, match="does not match desired artifact"):
        materialize_hypothetical_actions(
            scenario.snapshot,
            scenario.decision,
            (substituted_exact,),
            (scenario.artifact,),
        )


def test_successful_simulation_requires_observation_for_each_projection(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    with pytest.raises(ValueError, match="exactly one Observation"):
        finalize_simulation(
            scenario.snapshot,
            scenario.decision,
            artifacts=(scenario.artifact,),
        )


def test_synthetic_observation_helper_uses_current_observer_producer(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    observation = _admit_synthetic_projection_observation(
        scenario.snapshot,
        scenario.decision,
        scenario.artifact,
        classification="absent",
    )

    assert observation.producer == "observe-npmjs"


def test_synthetic_observation_helper_is_not_public_release_api() -> None:
    assert not hasattr(release_api, "admit_synthetic_projection_observation")
    assert "admit_synthetic_projection_observation" not in release_api.__all__


def _live_publication_action(scenario) -> PublicationAction:
    attempt = ReleaseAttemptIdentity(
        execution=OfficialExecutionIdentity(
            OfficialProductIdentity(
                "official",
                scenario.snapshot.release_unit,
                scenario.snapshot.nbgv.canonical_version,
            ),
            scenario.snapshot.target,
        ),
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
    )
    live_snapshot = replace(scenario.snapshot, subject=attempt)
    live_transport = replace(
        scenario.artifact.transport,
        artifact_name=release_artifact_transport_name(
            repository=scenario.artifact.repository,
            purpose="live-release",
            output=scenario.artifact.output,
            qualification_snapshot_digest=live_snapshot.snapshot_digest,
            workflow_run_id=attempt.workflow_run_id,
            run_attempt=None,
            producer=scenario.artifact.transport.producer,
        ),
        run_attempt=None,
    )
    live_provenance = {
        "schema": "workflow-delivery/v3/release-artifact-provenance",
        "subject": attempt.to_document(),
        "repository": scenario.artifact.repository,
        "qualification-snapshot-digest": live_snapshot.snapshot_digest,
        "repository-model-digest": scenario.artifact.repository_model_digest,
        "target": scenario.artifact.target,
        "purpose": "live-release",
        "output": scenario.artifact.output.to_document(),
        "build-request-digest": scenario.artifact.build_request_digest,
        "transport": live_transport.to_document(),
        "content": scenario.artifact.content.to_document(),
        "witness-digest": scenario.artifact.witness_digest,
        "source-input-manifest": [
            [path, digest]
            for path, digest in scenario.artifact.source_input_manifest
        ],
        "toolchain": [
            [name, version] for name, version in scenario.artifact.toolchain
        ],
    }
    live_artifact = ReleaseArtifact(
        subject=attempt,
        repository=scenario.artifact.repository,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        repository_model_digest=scenario.artifact.repository_model_digest,
        target=scenario.artifact.target,
        purpose="live-release",
        output=scenario.artifact.output,
        build_request_digest=scenario.artifact.build_request_digest,
        transport=live_transport,
        content=scenario.artifact.content,
        entries=scenario.artifact.entries,
        lifecycle_scripts=scenario.artifact.lifecycle_scripts,
        witness_digest=scenario.artifact.witness_digest,
        source_input_manifest=scenario.artifact.source_input_manifest,
        toolchain=scenario.artifact.toolchain,
        provenance_digest=canonical_sha256(
            cast("dict[str, JsonValue]", live_provenance)
        ),
    )
    live_decision = replace(
        scenario.decision,
        subject=attempt,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        admitted_artifact_digests=(live_artifact.artifact_digest,),
    )
    live_absent = _admit_synthetic_projection_observation(
        live_snapshot,
        live_decision,
        live_artifact,
        classification="absent",
    )
    publication = materialize_publication_snapshot(
        live_snapshot,
        live_decision,
        (live_absent,),
        (live_artifact,),
    )
    return publication.materialized_actions[0]


def test_publication_snapshot_guards_success_observation_and_artifacts(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    with pytest.raises(TypeError, match="cannot be emitted for simulation"):
        materialize_publication_snapshot(
            scenario.snapshot,
            scenario.decision,
            (),
            (scenario.artifact,),
        )

    action = _live_publication_action(scenario)
    assert action.projection == scenario.snapshot.destination_projections[0]
    assert action.operation == action.projection.operation
    assert action.artifact_digest == action.artifact.artifact_digest
    assert action.artifact_output == action.artifact.output
    assert action.mutable_resource_keys
    assert action.lock_projection
    assert action.lock_group
    assert action.capability_group
    assert action.capability_requirements == (
        "npmjs/trusted-publishing-oidc-v1",
    )
    assert action.receipt_contract == "npm/package-publication-receipt-v1"


@pytest.mark.parametrize(
    ("_binding_category", "mutate"),
    [
        (
            "action-id",
            lambda action: replace(action, action_id=f"{action.action_id}:x"),
        ),
        (
            "operation",
            lambda action: replace(action, operation=f"{action.operation}:x"),
        ),
        (
            "artifact-digest",
            lambda action: replace(
                action,
                artifact_digest="sha256:" + ("f" * 64),
            ),
        ),
        (
            "artifact-output",
            lambda action: replace(
                action,
                artifact_output=replace(
                    action.artifact_output,
                    output_id="other-output",
                ),
            ),
        ),
        (
            "prerequisites",
            lambda action: replace(action, prerequisites=("unexpected",)),
        ),
        (
            "action-inputs",
            lambda action: replace(
                action,
                action_inputs=(
                    ("artifact-content-sha256", "sha256:" + ("0" * 64)),
                    *action.action_inputs[1:],
                ),
            ),
        ),
        (
            "mutable-resource-keys",
            lambda action: replace(action, mutable_resource_keys=()),
        ),
        (
            "lock-projection",
            lambda action: replace(
                action,
                lock_projection=f"{action.lock_projection}:x",
            ),
        ),
        (
            "lock-group",
            lambda action: replace(action, lock_group=f"{action.lock_group}:x"),
        ),
        (
            "capability-group",
            lambda action: replace(
                action,
                capability_group=f"{action.capability_group}:x",
            ),
        ),
        (
            "capability-requirements",
            lambda action: replace(
                action,
                capability_requirements=("npmjs/trusted-publishing-oidc-v2",),
            ),
        ),
        (
            "expected-result",
            lambda action: replace(
                action,
                expected_result=f"{action.expected_result}:x",
            ),
        ),
        (
            "receipt-contract",
            lambda action: replace(
                action,
                receipt_contract=f"{action.receipt_contract}:x",
            ),
        ),
    ],
)
def test_publication_action_rejects_substituted_concrete_bindings(
    qualified_simulation,
    _binding_category,
    mutate,
) -> None:
    action = _live_publication_action(qualified_simulation)

    with pytest.raises(ValueError, match="Publication Action"):
        mutate(action)


def test_failed_decision_cannot_materialize_hypothetical_actions(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    failed = replace(
        scenario.decision,
        terminal_result="failure",
        failure_class="quality-failure",
        next_action="fix-quality-failure-and-rerun",
    )
    observation = _admit_synthetic_projection_observation(
        scenario.snapshot,
        scenario.decision,
        scenario.artifact,
        classification="absent",
    )

    with pytest.raises(ValueError, match="successful qualification"):
        materialize_hypothetical_actions(
            scenario.snapshot,
            failed,
            (observation,),
            (scenario.artifact,),
        )
