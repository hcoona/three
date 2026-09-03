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
    CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
    BuddyExecutionIdentity,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    PublicationAction,
    PublicationObservationReference,
    PublicationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    publication_action_inputs,
    publication_capability_requirements,
    publication_expected_result,
    publication_lock_group,
    publication_lock_projection,
    publication_mutable_resource_key_basis,
    publication_mutable_resource_keys,
    publication_receipt_contract,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.records.release_transport import (
    release_record_from_document,
)
from three_workflow_delivery_v3.release.finalizer import (
    UnsupportedPublicationPrimitiveError,
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


def _live_publication_context(scenario):
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit=scenario.snapshot.release_unit,
            target=scenario.snapshot.target,
        ),
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
    )
    source_projection = scenario.snapshot.destination_projections[0]
    coordinate = replace(
        source_projection.coordinate,
        channel="buddy",
        destination_id="npm/github-packages-hcoona-three-v1",
    )
    projection = replace(
        source_projection,
        projection_id="projection:npm:github-packages",
        destination_id=coordinate.destination_id,
        registry="https://npm.pkg.github.com",
        coordinate=coordinate,
        operation=CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
        observation_contract_id="npm/github-packages-observation-v1",
        potential_action_id="publish-github-packages",
    )
    source_potential_action = scenario.snapshot.potential_actions[0]
    potential_action = replace(
        source_potential_action,
        contract_id=projection.potential_action_id,
        projection_id=projection.projection_id,
        operation=projection.operation,
        capability_requirements=publication_capability_requirements(projection),
        mutable_resource_key_basis=publication_mutable_resource_key_basis(
            projection
        ),
    )
    live_snapshot = replace(
        scenario.snapshot,
        subject=attempt,
        channel="buddy",
        destination_projections=(projection,),
        potential_actions=(potential_action,),
    )
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
    return live_snapshot, live_decision, live_artifact


def _publication_action(snapshot, artifact) -> PublicationAction:
    projection = snapshot.destination_projections[0]
    return PublicationAction(
        action_id=projection.potential_action_id,
        projection=projection,
        operation=projection.operation,
        artifact=artifact,
        artifact_digest=artifact.artifact_digest,
        artifact_output=artifact.output,
        prerequisites=(),
        action_inputs=publication_action_inputs(projection, artifact),
        mutable_resource_keys=publication_mutable_resource_keys(
            projection,
            artifact,
        ),
        lock_projection=publication_lock_projection(projection),
        lock_group=publication_lock_group(projection),
        capability_requirements=publication_capability_requirements(projection),
        expected_result=publication_expected_result(projection),
        receipt_contract=publication_receipt_contract(projection),
    )


def _live_publication_snapshot(scenario) -> PublicationSnapshot:
    live_snapshot, live_decision, live_artifact = _live_publication_context(
        scenario
    )
    live_exact = _admit_synthetic_projection_observation(
        live_snapshot,
        live_decision,
        live_artifact,
        classification="exact-satisfied",
        owner="hcoona",
    )
    return materialize_publication_snapshot(
        live_snapshot,
        live_decision,
        (live_exact,),
        (live_artifact,),
    )


def _live_publication_action(scenario) -> PublicationAction:
    snapshot, _, artifact = _live_publication_context(scenario)
    return _publication_action(snapshot, artifact)


def _live_action_publication_snapshot(scenario) -> PublicationSnapshot:
    snapshot, decision, artifact = _live_publication_context(scenario)
    observation = _admit_synthetic_projection_observation(
        snapshot,
        decision,
        artifact,
        classification="absent",
    )
    action = _publication_action(snapshot, artifact)
    return PublicationSnapshot(
        attempt=snapshot.subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result=decision.terminal_result,
        projection_ids=(action.projection.projection_id,),
        artifact_digests=(artifact.artifact_digest,),
        artifact_output_ids=(artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=action.projection.projection_id,
                observation_digest=observation.observation_digest,
                classification=observation.value.classification,
            ),
        ),
        materialized_actions=(action,),
    )


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

    publication = _live_publication_snapshot(scenario)
    assert isinstance(publication.attempt.execution, BuddyExecutionIdentity)
    assert publication.materialized_actions == ()
    assert tuple(
        reference.classification
        for reference in publication.observation_references
    ) == ("exact-satisfied",)
    assert (
        release_record_from_document(
            publication.to_document(),
            expected_type=PublicationSnapshot,
        )
        == publication
    )


def test_publication_action_uses_current_normal_live_bindings(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    snapshot, decision, artifact = _live_publication_context(scenario)
    action = _publication_action(snapshot, artifact)
    assert action.action_id == "publish-github-packages"
    assert decision.admitted_artifact_digests == (action.artifact_digest,)
    assert action.artifact.subject == snapshot.subject
    assert action.artifact.purpose == "live-release"
    assert (
        action.operation
        == action.projection.operation
        == "conditional-create-npm-version-and-target-tag"
    )
    assert action.artifact_digest == action.artifact.artifact_digest
    assert action.artifact_output == action.artifact.output
    assert action.prerequisites == ()
    assert action.action_inputs == publication_action_inputs(
        action.projection,
        action.artifact,
    )
    action_inputs = dict(action.action_inputs)
    assert action_inputs["operation"] == action.operation
    assert action_inputs["artifact-digest"] == action.artifact_digest
    assert action.mutable_resource_keys == publication_mutable_resource_keys(
        action.projection,
        action.artifact,
    )
    assert len(action.mutable_resource_keys) == 2
    assert action.mutable_resource_keys[0].startswith(
        "external-package-coordinate:"
    )
    assert action.mutable_resource_keys[1].startswith("npm-dist-tag:")
    assert action.lock_projection == publication_lock_projection(
        action.projection
    )
    assert action.lock_group == publication_lock_group(action.projection)
    assert action.lock_group == action.lock_projection
    assert action.lock_group.startswith("destination-package:")
    assert action.capability_requirements == (
        "github/packages-conditional-version-and-tag-v1",
    )
    assert action.expected_result == "created-version-and-target-tag-or-exact"
    assert (
        action.receipt_contract
        == "npm/conditional-version-and-target-tag-receipt-v1"
    )


def test_publication_snapshot_rejects_more_than_one_action(
    qualified_simulation,
) -> None:
    publication = _live_action_publication_snapshot(qualified_simulation)
    action = publication.materialized_actions[0]

    assert publication.materialized_actions == (action,)
    with pytest.raises(ValueError, match="at most one action"):
        replace(publication, materialized_actions=(action, action))

    document = publication.to_document()
    actions = document["materialized-actions"]
    assert isinstance(actions, list)
    actions.append(action.to_document())
    with pytest.raises(ValueError, match="at most one action"):
        release_record_from_document(
            document,
            expected_type=PublicationSnapshot,
        )


def test_absent_publication_requires_action_but_materializer_is_blocked(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    publication = _live_action_publication_snapshot(scenario)
    with pytest.raises(
        ValueError,
        match="actions must exactly cover absent projections",
    ):
        replace(publication, materialized_actions=())

    document = publication.to_document()
    document["materialized-actions"] = []
    with pytest.raises(
        ValueError,
        match="actions must exactly cover absent projections",
    ):
        release_record_from_document(
            document,
            expected_type=PublicationSnapshot,
        )

    snapshot, decision, artifact = _live_publication_context(scenario)
    observation = _admit_synthetic_projection_observation(
        snapshot,
        decision,
        artifact,
        classification="absent",
    )

    with pytest.raises(
        UnsupportedPublicationPrimitiveError,
        match="destination primitive is not implemented",
    ):
        materialize_publication_snapshot(
            snapshot,
            decision,
            (observation,),
            (artifact,),
        )


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


def test_qualification_decision_constructor_rejects_empty_dispositions(
    qualified_simulation,
) -> None:
    decision = qualified_simulation.decision
    assert decision.terminal_result == "success"
    assert len(decision.obligation_dispositions) == 4

    with pytest.raises(
        ValueError,
        match=r"^Qualification Decision requires at least one disposition$",
    ):
        replace(decision, obligation_dispositions=())


def test_qualification_decision_transport_rejects_empty_dispositions(
    qualified_simulation,
) -> None:
    decision = qualified_simulation.decision
    assert decision.terminal_result == "success"
    document = decision.to_document()
    dispositions = document["obligation-dispositions"]
    assert isinstance(dispositions, list)
    assert len(dispositions) == 4
    document["obligation-dispositions"] = []

    with pytest.raises(
        ValueError,
        match=r"^Qualification Decision requires at least one disposition$",
    ):
        release_record_from_document(
            document,
            expected_type=type(decision),
        )


@pytest.mark.parametrize("disposition_index", [0, 1, 2, 3])
@pytest.mark.parametrize("outcome", ["failed", "incomplete"])
def test_success_decision_constructor_rejects_unsatisfied_disposition(
    qualified_simulation,
    disposition_index: int,
    outcome: str,
) -> None:
    decision = qualified_simulation.decision
    assert decision.terminal_result == "success"
    assert all(
        disposition.outcome == "satisfied"
        for disposition in decision.obligation_dispositions
    )
    dispositions = tuple(
        replace(disposition, outcome=outcome)
        if index == disposition_index
        else disposition
        for index, disposition in enumerate(decision.obligation_dispositions)
    )

    with pytest.raises(
        ValueError,
        match=(
            r"^Successful Qualification Decision requires every "
            r"disposition to be satisfied$"
        ),
    ):
        replace(decision, obligation_dispositions=dispositions)


@pytest.mark.parametrize("disposition_index", [0, 1, 2, 3])
@pytest.mark.parametrize("outcome", ["failed", "incomplete"])
def test_success_decision_transport_rejects_unsatisfied_disposition(
    qualified_simulation,
    disposition_index: int,
    outcome: str,
) -> None:
    decision = qualified_simulation.decision
    assert decision.terminal_result == "success"
    document = decision.to_document()
    dispositions = document["obligation-dispositions"]
    assert isinstance(dispositions, list)
    substituted_disposition = dispositions[disposition_index]
    assert isinstance(substituted_disposition, dict)
    assert substituted_disposition["outcome"] == "satisfied"
    substituted_disposition["outcome"] = outcome

    with pytest.raises(
        ValueError,
        match=(
            r"^Successful Qualification Decision requires every "
            r"disposition to be satisfied$"
        ),
    ):
        release_record_from_document(
            document,
            expected_type=type(decision),
        )
