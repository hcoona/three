"""Modeled Python authority fixtures; no native authorization is represented."""

from dataclasses import replace
from datetime import UTC, datetime, timedelta

from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.adapters.python import PythonConsumerResult
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.python import PythonArtifact
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_WORKFLOW,
    PythonGovernance,
    blocked_python_governance,
    python_publisher_tuple,
)
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
    PythonQualificationSnapshot,
    qualify_python_release,
)
from three_workflow_delivery_v3.repository.python_model import (
    PythonRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
)

from .adapters.test_pypi import _distribution
from .repository.test_python_model import _admitted, _contents, _context

NOW = datetime(2026, 9, 24, 12, tzinfo=UTC)
TARGET = "a" * 40
RUN_ID = 701


def reference(document, artifact_id=501, filename="record.json"):
    """Bind a synthetic immutable transport to exact canonical record bytes."""
    return ArtifactReference(
        artifact_id,
        "sha256:" + "1" * 64,
        f"https://example.invalid/artifacts/{artifact_id}",
        filename,
        canonical_sha256(document),
    )


def model(purpose="slice-validation", run_id=RUN_ID, control=None):
    """Reuse admitted compile-only facts with same-revision authority."""
    context = replace(
        _context(TARGET, purpose),
        request_id="release-request:" + "b" * 64,
        workflow_run_id=run_id,
        control=control or f"workflow-delivery-v3:{TARGET}",
    )
    facts = _admitted(context, _contents())
    return PythonRepositoryModelSnapshot(
        context, facts.result, facts.request_reference, facts.result_reference
    )


def originals(snapshot):
    """Attach independent current-build transports to both native originals."""
    witness = snapshot.build_request().witness
    distributions = tuple(_distribution(v, witness) for v in ("wheel", "sdist"))
    artifacts = []
    for ordinal, distribution in enumerate(distributions):
        artifact_id = 801 + ordinal
        url = f"https://example.invalid/artifacts/{artifact_id}"
        digest = "sha256:" + str(ordinal + 3) * 64
        ref = ArtifactReference(
            artifact_id, digest, url, distribution.filename, distribution.digest
        )
        transport = ArtifactTransportIdentity(
            artifact_id,
            distribution.variant,
            url,
            digest,
            "build-python",
            snapshot.context.workflow_run_id,
            snapshot.context.run_attempt,
        )
        artifacts.append(
            PythonArtifact(
                distribution.variant,
                distribution.filename,
                len(distribution.content),
                witness,
                ref,
                transport,
            )
        )
    return (
        tuple(artifacts),
        tuple(d.content for d in distributions),
        distributions,
    )


def consumer(distribution):
    """Model a trusted clean consumer without executing or contacting a host."""
    return PythonConsumerResult(
        distribution.variant,
        distribution.digest,
        canonicalize(
            {
                "version": distribution.witness.nbgv.pep440_version,
                "project-id": PYTHON_RELEASE_UNIT,
                "witness": distribution.witness.to_document(),
                "module": "/isolated/site-packages/smoke/__init__.py",
            }
        ),
        (canonicalize({"exit-code": 0}),),
        _distribution("wheel", distribution.witness).content
        if distribution.variant == "sdist"
        else None,
    )


def ready_document(registry):
    """Synthetic evidence exercises ready-state application behavior."""
    doc = blocked_python_governance(registry)
    doc.update(
        {
            "live_enabled": True,
            "state": "ready",
            "inspected-at": (NOW - timedelta(hours=1))
            .isoformat()
            .replace("+00:00", "Z"),
            "expires-at": (NOW + timedelta(days=1))
            .isoformat()
            .replace("+00:00", "Z"),
            "source-evidence-revision": TARGET,
            "native-acceptance": {
                "suite": "workflow-delivery/v3/python-native-suite-v1",
                "registry": registry.name,
                "project": PYTHON_RELEASE_UNIT,
                "profile-digest": registry.profile_digest,
                "evidence-digest": "sha256:" + "d" * 64,
                "generation": "e" * 32,
                "review": "https://github.com/hcoona/three/pull/1",
                "passed": True,
            },
            "configuration": {
                "publisher-registration": python_publisher_tuple(registry),
                "environment-id": 1901,
                "reviewer-id": 712433,
                "prevent-self-review": False,
                "can-admins-bypass": False,
                "wait-timer": 0,
                "protected-main-only": True,
                "sentinel": registry.environment + "/v1",
                "secret-count": 0,
                "accepted-writers": ["hcoona"],
                "attestation-digest": "sha256:" + "f" * 64,
                "scope-limitation": (
                    "Reviewed service registration attestation; no runtime "
                    "registration inventory or token-decoding scope proof."
                ),
            },
        }
    )
    return doc


def governance(name="testpypi", observed_at=NOW):
    """Return modeled admission only, never a tracked ready configuration."""
    registry = PythonRegistry(name)
    return PythonGovernance(
        registry, canonicalize(ready_document(registry)), TARGET, observed_at
    )


def qualification(name="testpypi", run_id=RUN_ID):
    """Create current Release evidence using inspected modeled originals."""
    source = model("live-release", run_id)
    admitted = governance(name)
    intent = ReleaseIntent(
        "hcoona/three",
        PYTHON_WORKFLOW,
        "refs/heads/main",
        TARGET,
        source.context.request_id,
        "hcoona",
        run_id,
        "workflow_dispatch",
        "refs/heads/main",
        TARGET,
        admitted.registry.channel,
        "live",
        "live-release",
        PYTHON_RELEASE_UNIT,
    )
    snapshot = PythonQualificationSnapshot(
        intent, source, reference(source.to_document()), admitted
    )
    artifacts, payloads, distributions = originals(source)
    evidence = qualify_python_release(
        snapshot, artifacts, payloads, consumer=consumer
    )
    return (
        PythonQualificationDecision(snapshot, evidence),
        payloads,
        distributions,
    )
