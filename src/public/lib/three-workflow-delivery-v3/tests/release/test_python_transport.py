"""Replay strict Python records against current immutable predecessors."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.release.python_transport import (
    admit_python_publication_snapshot,
    admit_python_qualification_decision,
    python_approval_bundle_from_document,
    python_authorization_from_document,
    python_marker_from_document,
    python_native_observation_from_document,
    python_qualification_evidence_from_document,
    python_qualification_snapshot_from_document,
    python_remote_observation_from_document,
)

from ..python_fixtures import qualification
from .python_fixtures import native_observation, prepared_publication


@pytest.fixture
def replay():
    """Expose all transport stages with explicit admitted predecessor inputs."""
    marker, _, _, originals = prepared_publication()
    authorization = marker.authorization
    bundle = authorization.bundle
    publication = bundle.snapshot
    observation = publication.observation
    decision = observation.decision
    return {
        "snapshot": (
            decision.snapshot,
            python_qualification_snapshot_from_document,
            (),
        ),
        "evidence": (
            decision.evidence[0],
            python_qualification_evidence_from_document,
            (),
        ),
        "decision": (
            decision,
            admit_python_qualification_decision,
            (decision.snapshot, decision.evidence),
        ),
        "observation": (
            observation,
            python_remote_observation_from_document,
            (decision, observation.decision_reference, originals),
        ),
        "publication": (
            publication,
            admit_python_publication_snapshot,
            (observation, publication.observation_reference),
        ),
        "bundle": (
            bundle,
            python_approval_bundle_from_document,
            (publication, bundle.snapshot_reference),
        ),
        "authorization": (
            authorization,
            python_authorization_from_document,
            (bundle, authorization.bundle_reference),
        ),
        "marker": (
            marker,
            python_marker_from_document,
            (authorization, marker.authorization_reference),
        ),
    }


@pytest.mark.parametrize(
    "kind",
    [
        "snapshot",
        "evidence",
        "decision",
        "observation",
        "publication",
        "bundle",
        "authorization",
        "marker",
    ],
)
def test_python_transport_replays_exact_record_and_predecessors(replay, kind):
    """Serialization preserves concrete native fields and direct lineage."""
    record, parser, arguments = replay[kind]
    admitted = parser(record.to_document(), *arguments)
    assert type(admitted) is type(record)
    assert admitted == record
    assert admitted.to_document() == record.to_document()


@pytest.mark.parametrize(
    "kind",
    [
        "snapshot",
        "evidence",
        "decision",
        "observation",
        "publication",
        "bundle",
        "authorization",
        "marker",
    ],
)
@pytest.mark.parametrize("change", ["schema", "producer", "extra", "missing"])
def test_python_transport_rejects_foreign_or_open_serialized_shapes(
    replay, kind, change
):
    """Exact replay rejects schema coercion and unreviewed wire additions."""
    record, parser, arguments = replay[kind]
    document = deepcopy(record.to_document())
    if change == "missing":
        del document["schema"]
    else:
        document[change] = "foreign"
    with pytest.raises((ValueError, TypeError), match="Python"):
        parser(document, *arguments)


@pytest.mark.parametrize(
    "kind", ["observation", "publication", "bundle", "authorization", "marker"]
)
def test_python_transport_rejects_relocated_current_predecessor(replay, kind):
    """Equal bytes cannot silently change the declared transport."""
    record, parser, arguments = replay[kind]
    predecessor, ref, *tail = arguments
    relocated = replace(ref, artifact_id=ref.artifact_id + 1)
    with pytest.raises(ValueError, match="current inputs"):
        parser(record.to_document(), predecessor, relocated, *tail)


@pytest.mark.parametrize(
    "kind",
    [
        "snapshot",
        "decision",
        "observation",
        "publication",
        "bundle",
        "authorization",
        "marker",
    ],
)
def test_python_transport_rejects_forged_attempt_in_wire_document(replay, kind):
    """An embedded Attempt claim cannot override admitted current inputs."""
    record, parser, arguments = replay[kind]
    document = deepcopy(record.to_document())
    document["attempt"] = {}
    with pytest.raises(ValueError, match="Python"):
        parser(document, *arguments)


@pytest.mark.parametrize("classification", ["absent", "partial", "complete"])
def test_python_native_replay_resolves_only_exact_original_bytes(
    classification,
):
    """Native replay reconstructs prior evidence, never performs a new read."""
    decision, _, originals = qualification()
    count = {"absent": 0, "partial": 1, "complete": 2}[classification]
    observation = native_observation(
        decision, originals[:count], classification
    )
    admitted = python_native_observation_from_document(
        observation.to_document(), originals
    )
    assert admitted == observation
    assert admitted.files == originals[:count]
    assert all(a is b for a, b in zip(admitted.files, originals, strict=False))


@pytest.mark.parametrize(
    "change",
    [
        "digest",
        "filename",
        "variant",
        "extra",
        "missing-original",
        "ambiguous-original",
        "files-shape",
    ],
)
def test_python_native_replay_rejects_unknown_or_ambiguous_file_identity(
    change,
):
    """No substituted or ambiguously resolved native bytes enter lineage."""
    decision, _, originals = qualification()
    document = native_observation(decision, originals, "complete").to_document()
    if change == "missing-original":
        originals = originals[:1]
    elif change == "ambiguous-original":
        originals = (*originals, originals[0])
    elif change == "files-shape":
        document["files"] = {}
    else:
        document["files"][0][change] = "foreign"
    with pytest.raises((ValueError, TypeError), match="Python"):
        python_native_observation_from_document(document, originals)


def test_python_decision_replay_rejects_another_run_or_missing_evidence(replay):
    """Decision replay does not trust the serialized success claim."""
    decision, parser, _ = replay["decision"]
    foreign, _, _ = qualification(
        run_id=decision.snapshot.intent.workflow_run_id + 1
    )
    with pytest.raises(ValueError, match="current inputs"):
        parser(decision.to_document(), foreign.snapshot, foreign.evidence)
    with pytest.raises(ValueError, match="current inputs"):
        parser(decision.to_document(), decision.snapshot, ())


def test_python_evidence_replay_rejects_ci_schema_and_artifact_container(
    replay,
):
    """Release evidence remains separate from CI and ordered native lists."""
    evidence, parser, _ = replay["evidence"]
    document = evidence.to_document()
    document["schema"] = "workflow-delivery/v3/python-ci-evidence"
    with pytest.raises(ValueError, match="current inputs"):
        parser(document)
    document = evidence.to_document()
    document["artifacts"] = {}
    with pytest.raises(TypeError, match="artifacts must be a list"):
        parser(document)


def test_python_marker_replay_preserves_distinct_governance_observation_time():
    """Marker replay retains the actual fresh-Governance read instant."""
    marker, _, _, _ = prepared_publication()
    later = replace(
        marker, observed_at=marker.observed_at + timedelta(seconds=1)
    )
    document = later.to_document()
    assert document["governance-observed-at"] != document["observed-at"]
    admitted = python_marker_from_document(
        document, later.authorization, later.authorization_reference
    )
    assert admitted == later
    assert (
        admitted.fresh_governance.observed_at
        == marker.fresh_governance.observed_at
    )
    assert admitted.observed_at > admitted.fresh_governance.observed_at


@pytest.mark.parametrize("change", ["missing", "stale", "future"])
def test_python_marker_replay_rejects_missing_or_invalid_governance_time(
    change,
):
    """Lossy replay cannot fabricate freshness from the marker timestamp."""
    marker, _, _, _ = prepared_publication()
    later = replace(
        marker, observed_at=marker.observed_at + timedelta(seconds=1)
    )
    document = later.to_document()
    if change == "missing":
        del document["governance-observed-at"]
    elif change == "stale":
        document["governance-observed-at"] = (
            (marker.authorization.completed_at - timedelta(seconds=1))
            .isoformat()
            .replace("+00:00", "Z")
        )
    else:
        document["governance-observed-at"] = (
            (later.observed_at + timedelta(seconds=1))
            .isoformat()
            .replace("+00:00", "Z")
        )
    with pytest.raises(ValueError, match="Python"):
        python_marker_from_document(
            document, later.authorization, later.authorization_reference
        )
