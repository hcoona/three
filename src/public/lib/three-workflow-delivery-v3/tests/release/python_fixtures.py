"""Synthetic authorized Python set DAGs for pure publication scenario tests."""

from datetime import timedelta

from three_workflow_delivery_v3.adapters.pypi import PythonIndexObservation
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.release.python_publication import (
    PythonApprovalBundle,
    PythonMutationMarker,
    PythonPublicationAuthorization,
    PythonPublicationSnapshot,
    PythonRemoteObservation,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..python_fixtures import NOW, governance, qualification, reference


def native_observation(decision, distributions=(), classification="absent"):
    """Describe modeled readback without native destination evidence claims."""
    return PythonIndexObservation(
        decision.snapshot.governance.registry,
        decision.snapshot.model.provider.nbgv.pep440_version,
        "sha256:" + "d" * 64,
        tuple(distributions),
        classification,
    )


def publication_snapshot(decision, native):
    """Bind one fresh observation into its immutable publication Snapshot."""
    observed = PythonRemoteObservation(
        decision, reference(decision.to_document()), native, NOW
    )
    return PythonPublicationSnapshot(
        observed, reference(observed.to_document(), 502)
    )


def prepared_publication(name="testpypi"):
    """Construct a fully synthetic approved marker with exact record lineage."""
    decision, payloads, distributions = qualification(name)
    absence = native_observation(decision)
    snapshot = publication_snapshot(decision, absence)
    lines = [
        "Python distribution set approval",
        f"Target: {snapshot.attempt.execution.target}",
        f"Run: {snapshot.attempt.workflow_run_id}",
        f"Version: {decision.snapshot.model.provider.nbgv.pep440_version}",
        f"Destination: {snapshot.registry.origin}",
        f"Profile: {snapshot.registry.profile_digest}",
        *(
            f"{i}: {artifact.filename} {artifact.reference.payload_digest}"
            for i, artifact in enumerate(decision.artifacts)
        ),
        (
            "Upload wheel once, verify exact bytes, then upload sdist once and "
            "verify the complete set. Partial success remains failure. "
            "No retry, completion, rollback or deletion."
        ),
    ]
    summary = ("\n".join(lines) + "\n").encode()
    summary_reference = ArtifactReference(
        503,
        "sha256:" + "3" * 64,
        "https://example.invalid/artifacts/503",
        "summary.txt",
        python_digest(summary),
    )
    bundle = PythonApprovalBundle(
        snapshot, reference(snapshot.to_document(), 504), summary_reference
    )
    proof = {
        "repository": "hcoona/three",
        "run-id": snapshot.attempt.workflow_run_id,
        "run-attempt": 1,
        "target": snapshot.attempt.execution.target,
        "environment": snapshot.registry.environment,
        "environment-id": 1901,
        "deployment-id": 2001,
        "reviewer-id": 712433,
        "reviewer": "hcoona",
        "state": "approved",
        "native-response-digest": "sha256:" + "e" * 64,
        "sentinel": snapshot.registry.environment + "/v1",
    }
    authorization = PythonPublicationAuthorization(
        bundle,
        reference(bundle.to_document(), 505),
        canonicalize(proof),
        NOW + timedelta(seconds=1),
    )
    marker = PythonMutationMarker(
        authorization,
        reference(authorization.to_document(), 506),
        governance(name, NOW + timedelta(seconds=2)),
        absence,
        NOW + timedelta(seconds=2),
    )
    return marker, reference(marker.to_document(), 507), payloads, distributions
