"""Independent original NuGet builds join fresh modeled release Attempts."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.exact_satisfied import (
    prove_nuget_exact_satisfied,
)
from three_workflow_delivery_v3.release.nuget_qualification import (
    NugetMechanicalBuildResult,
    form_uploaded_nuget_release_artifact,
    qualify_release_nuget_consumer,
    qualify_release_nuget_contents,
)
from three_workflow_delivery_v3.repository import compiler

from ..nuget_repro_fixtures import write_json
from .test_nuget_destination_runtime import (
    LATER,
    TOKEN,
    ModeledFeed,
    _arguments,
    _finalization,
    _finish,
    _native_case,
    _observe,
    _reference,
    _snapshot,
)
from .test_nuget_qualification import (
    NugetScenario,
    _plan_from_provider,
    _transport,
    _witness,
)


def _scenario(campaign, label, run_id):
    request, result = campaign.requests[label], campaign.results[label]
    target = request.witness.target
    context = compiler.CompilationContext(
        request_id=f"release-request:{run_id:064x}",
        purpose="live-release",
        workflow_run_id=run_id,
        run_attempt=None,
        target=target,
        producer="compile-nuget-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    provider_result = replace(
        campaign.provider,
        binding=compiler.provider_binding(manifest, "dotnet-nuget-slice"),
    )
    intent, binding, model, provider, snapshot = _plan_from_provider(
        request.source_root, context, manifest, provider_result
    )
    assert _witness(snapshot, model) == request.witness
    return NugetScenario(
        intent,
        binding,
        model,
        provider,
        snapshot,
        request,
        result,
        request.helper,
    )


def _qualify_original(scenario, _patch, evidence):
    """Admit this Attempt's actual independent build and run real Quality."""
    contract = scenario.snapshot.build_requests[0]
    assert scenario.request.witness == _witness(
        scenario.snapshot, scenario.model
    )
    assert (
        scenario.request.source_input_manifest == contract.source_input_manifest
    )
    mechanics = NugetMechanicalBuildResult(
        scenario.snapshot.snapshot_digest,
        contract.request_digest,
        scenario.result,
    )
    transport = _transport(scenario.snapshot)
    # Real Actions artifact IDs are unique across runs, even for equal payloads.
    artifact_id = scenario.intent.workflow_run_id * 10 + 1
    transport = replace(
        transport,
        artifact_id=artifact_id,
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{scenario.intent.workflow_run_id}/artifacts/{artifact_id}",
    )
    artifact, build = form_uploaded_nuget_release_artifact(
        scenario.snapshot, mechanics, transport
    )
    contents = qualify_release_nuget_contents(
        scenario.snapshot,
        artifact,
        scenario.result.package,
        scenario.result.expectation,
        scenario.helper,
    )
    consumer = qualify_release_nuget_consumer(
        scenario.snapshot,
        artifact,
        scenario.result.package,
        scenario.result.expectation,
        scenario.helper,
        evidence_directory=evidence / "consumer",
    )
    return artifact, (build, contents, consumer), ()


@pytest.fixture(scope="module")
def independently_qualified_nuget_attempts(native_nuget_reproducibility):
    """Two independently built originals get separate models and authority."""
    campaign = native_nuget_reproducibility
    cases = []
    # Only remote authority and destination behavior are modeled. Package
    # construction, archive parsing and clean consumers execute natively.
    with pytest.MonkeyPatch.context() as patch:
        for label, run_id in (("first", 9481), ("second-longer-root", 9482)):
            case = _native_case(
                _scenario(campaign, label, run_id),
                patch,
                campaign.evidence / f"attempt-{run_id}",
                qualify=_qualify_original,
            )
            assert case.decision.terminal_result == "success"
            cases.append(case)
        yield cases


@pytest.mark.parametrize("remote", ["exact", "different-bytes"])
def test_fresh_nuget_attempt_recovers_independently_rebuilt_bytes(
    independently_qualified_nuget_attempts, native_nuget_reproducibility, remote
):
    """Fresh exact proof is zero-action; a conflicting archive fails closed."""
    first, recovery = independently_qualified_nuget_attempts
    assert first.scenario.result is not recovery.scenario.result
    assert first.scenario.result.package == recovery.scenario.result.package
    assert first.scenario.result.witness == recovery.scenario.result.witness
    assert first.attempt_binding.attempt != recovery.attempt_binding.attempt
    assert first.snapshot.snapshot_digest != recovery.snapshot.snapshot_digest
    assert (
        first.artifact.transport.artifact_id
        != recovery.artifact.transport.artifact_id
    )
    assert (
        recovery.artifact.transport.workflow_run_id
        == recovery.intent.workflow_run_id
    )
    assert {e.evidence_digest for e in first.evidence}.isdisjoint(
        e.evidence_digest for e in recovery.evidence
    )
    assert recovery.current == ReleaseAdmissionBindings(
        "live-release", 9482, None, recovery.intent.target
    )

    # A failed original Attempt remains failed even if its bytes are present.
    first_observation = _observe(first, ModeledFeed(first, present=False))
    first_publication = _snapshot(first, first_observation)
    assert len(first_publication.materialized_actions) == 1
    failed = _finish(
        first,
        replace(
            _finalization(first),
            observations=(
                (
                    first_observation,
                    _reference(
                        first_observation.observation_digest,
                        first.intent.workflow_run_id,
                        506,
                    ),
                ),
            ),
            publication=(
                first_publication,
                _reference(
                    first_publication.snapshot_digest,
                    first.intent.workflow_run_id,
                    502,
                ),
            ),
        ),
    )
    assert failed.disposition == "failed-before-publication"
    original_failure = canonicalize(failed.to_document())
    feed = ModeledFeed(recovery, present=True)
    feed.content = first.scenario.result.package
    if remote == "different-bytes":
        feed.content = native_nuget_reproducibility.results[
            "timestamp-control"
        ].package
    observation = _observe(recovery, feed)
    records = replace(
        _finalization(recovery),
        observations=(
            (
                observation,
                _reference(
                    observation.observation_digest,
                    recovery.intent.workflow_run_id,
                    506,
                ),
            ),
        ),
    )
    if remote == "exact":
        publication = _snapshot(recovery, observation)
        reference = _reference(
            publication.snapshot_digest, recovery.intent.workflow_run_id, 502
        )
        records = replace(records, publication=(publication, reference))
        assert observation.classification == "exact-satisfied"
        assert publication.materialized_actions == ()
        proof = prove_nuget_exact_satisfied(
            **_arguments(recovery),
            observation=observation,
            publication_snapshot=publication,
            publication_snapshot_reference=reference,
            publisher_conclusion="skipped",
            authority=feed.authority,
            governance_client=recovery.client,
            transport=feed,
            token=TOKEN,
            clock=lambda: LATER,
        )
        outcome = _finish(
            recovery,
            replace(
                records,
                exact_proof=(
                    proof,
                    _reference(
                        proof.proof_digest, recovery.intent.workflow_run_id, 507
                    ),
                ),
            ),
        )
        assert outcome.disposition == "exact-satisfied"
        assert outcome.possibly_mutated is False
        assert (
            outcome.attempt.workflow_run_id == recovery.intent.workflow_run_id
        )
    else:
        assert observation.classification == "conflicting"
        with pytest.raises(ValueError, match="not ready"):
            _snapshot(recovery, observation)
        outcome = _finish(recovery, records)
        assert outcome.disposition == "failed-before-publication"
        assert outcome.possibly_mutated is False
    assert feed.events
    assert {method for method, _ in feed.events} == {"GET"}
    assert canonicalize(failed.to_document()) == original_failure
    write_json(
        native_nuget_reproducibility.evidence / f"recovery-{remote}.json",
        {
            "first_attempt": first.attempt_binding.attempt.to_document(),
            "recovery_attempt": recovery.attempt_binding.attempt.to_document(),
            "first_package_sha256": first.scenario.result.manifest.sha256,
            "recovery_package_sha256": recovery.scenario.result.manifest.sha256,
            "first_failure": failed.to_document(),
            "recovery_outcome": outcome.to_document(),
            "remote_calls": feed.events,
        },
    )
