"""Real frozen Hatchling rebuilds join separate controlled release Attempts."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.adapters.python import (
    build_python_distributions,
    qualify_python_consumer,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.python import PythonArtifact
from three_workflow_delivery_v3.release.python_publication import (
    audit_python_publication_result,
    execute_python_publication,
    python_publication_result_from_document,
)
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
    PythonQualificationSnapshot,
    qualify_python_release,
)
from three_workflow_delivery_v3.repository.compiler import provider_binding
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.python_model import (
    PythonRepositoryModelSnapshot,
    python_provider_manifest,
)
from three_workflow_delivery_v3.repository.python_provider import (
    provide_python_repository_facts,
)

from ..adapters.test_python import (
    native_python_build,  # noqa: F401 - reuse native fixture
)
from ..adapters.test_python_observation import Boundary
from ..python_fixtures import NOW, qualification, reference
from .python_fixtures import prepared_publication
from .test_python_partial_recovery import (
    _OK,
    _TOKEN,
    execute_recovery,
    finalize_recovery,
)
from .test_python_publication import _readback


def independently_qualified_attempt(source, native_provider, run_id):
    """Run real provider, build, archive and both consumer boundaries anew."""
    template, _, _ = qualification(run_id=run_id)
    context = replace(
        template.snapshot.model.context,
        target=native_provider.nbgv.target,
        control=f"workflow-delivery-v3:{native_provider.nbgv.target}",
    )
    manifest = python_provider_manifest(context)
    provider = provide_python_repository_facts(
        source,
        provider_binding(manifest, "python-smoke"),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    model = PythonRepositoryModelSnapshot(
        context,
        provider,
        reference(manifest.to_document(), run_id * 10),
        reference(provider.to_document(), run_id * 10 + 1),
    )
    built = build_python_distributions(source, model.build_request())
    intent = replace(
        template.snapshot.intent,
        target=context.target,
        workflow_sha=context.target,
    )
    snapshot = PythonQualificationSnapshot(
        intent,
        model,
        reference(model.to_document(), run_id * 10 + 2),
        replace(template.snapshot.governance, source_commit=context.target),
    )
    artifacts = []
    for ordinal, distribution in enumerate(built.distributions):
        artifact_id = run_id * 10 + ordinal + 3
        url = f"https://example.invalid/artifacts/{artifact_id}"
        ref = ArtifactReference(
            artifact_id,
            distribution.digest,
            url,
            distribution.filename,
            distribution.digest,
        )
        transport = ArtifactTransportIdentity(
            artifact_id,
            distribution.variant,
            url,
            distribution.digest,
            "build-python",
            run_id,
            None,
        )
        artifacts.append(
            PythonArtifact(
                distribution.variant,
                distribution.filename,
                len(distribution.content),
                distribution.witness,
                ref,
                transport,
            )
        )
    payloads = tuple(d.content for d in built.distributions)
    evidence = qualify_python_release(
        snapshot,
        tuple(artifacts),
        payloads,
        consumer=qualify_python_consumer,
    )
    decision = PythonQualificationDecision(snapshot, evidence)
    assert decision.result == "passed"
    assert {e.result for e in evidence} == {"passed"}
    return decision, payloads, built.distributions


@pytest.mark.parametrize("proof_mode", ["none", "stop-after-wheel"])
def test_real_python_rebuilt_attempt_completes_partial_publication(
    tmp_path,
    proof_mode,
    native_python_build,  # noqa: F811 - imported pytest fixture
):
    """New native artifacts recover one partial failure without rewriting it."""
    source, provider, _, _, _ = native_python_build
    first_run, recovery_run = 1701, 1702
    first = independently_qualified_attempt(source, provider, first_run)
    marker_a, ref_a, payloads_a, originals_a = prepared_publication(
        qualified=first, proof_mode=proof_mode
    )
    boundary_a = Boundary(
        _OK,
        *_readback(marker_a.pre_state.registry, originals_a[:1]),
        PythonHttpResponse(400, b"sdist rejected", "text/plain"),
    )
    result_a = execute_python_publication(
        marker_a,
        ref_a,
        payloads_a,
        token=_TOKEN,
        transport=boundary_a,
        claim_path=tmp_path / "first-claim",
        clock=lambda: NOW + timedelta(seconds=3),
        monotonic=boundary_a.clock,
        wait=boundary_a.wait,
    )
    audit_python_publication_result(result_a, marker_a)
    outcome_a = finalize_recovery(result_a, marker_a, ref_a)
    assert result_a.result == "failed"
    assert [entry.status for entry in result_a.operations] == [
        "succeeded",
        "not-attempted" if proof_mode == "stop-after-wheel" else "failed",
    ]
    assert outcome_a.disposition == "publication-failed"
    assert outcome_a.possibly_mutated is True
    original_failure = (
        canonicalize(result_a.to_document()),
        canonicalize(outcome_a.to_document()),
    )

    second = independently_qualified_attempt(source, provider, recovery_run)
    assert first[1] == second[1]
    assert [(d.filename, d.digest) for d in first[2]] == [
        (d.filename, d.digest) for d in second[2]
    ]
    assert first[0].snapshot.attempt != second[0].snapshot.attempt
    assert (
        first[0].snapshot.snapshot_digest != second[0].snapshot.snapshot_digest
    )
    assert {a.reference.artifact_id for a in first[0].artifacts}.isdisjoint(
        a.reference.artifact_id for a in second[0].artifacts
    )
    assert all(
        a.transport.workflow_run_id == recovery_run for a in second[0].artifacts
    )
    assert {e.evidence_digest for e in first[0].evidence}.isdisjoint(
        e.evidence_digest for e in second[0].evidence
    )
    with pytest.raises(ValueError, match="Python"):
        qualify_python_release(
            second[0].snapshot,
            first[0].artifacts,
            second[1],
            consumer=qualify_python_consumer,
        )
    marker_b, ref_b, originals_b, boundary_b, result_b = execute_recovery(
        tmp_path,
        "wheel",
        qualified=second,
    )
    assert marker_b.pre_state.files[0].content == originals_a[0].content
    assert canonical_sha256(
        marker_a.authorization.to_document()
    ) != canonical_sha256(marker_b.authorization.to_document())
    assert canonical_sha256(
        marker_a.authorization.bundle.to_document()
    ) != canonical_sha256(marker_b.authorization.bundle.to_document())
    assert [call[0] for call in boundary_b.calls] == [
        "POST",
        "GET",
        "GET",
        "GET",
    ]
    assert originals_b[1].content in boundary_b.calls[0][3]
    assert originals_b[0].content not in boundary_b.calls[0][3]
    result_b = python_publication_result_from_document(result_b.to_document())
    audit_python_publication_result(result_b, marker_b)
    outcome_b = finalize_recovery(result_b, marker_b, ref_b)
    assert result_b.result == "published"
    assert outcome_b.disposition == "published"
    assert outcome_b.attempt.workflow_run_id == recovery_run
    assert (
        outcome_b.direct_predecessor.reference.payload_digest
        == result_b.result_digest
    )
    assert original_failure == (
        canonicalize(result_a.to_document()),
        canonicalize(outcome_a.to_document()),
    )
