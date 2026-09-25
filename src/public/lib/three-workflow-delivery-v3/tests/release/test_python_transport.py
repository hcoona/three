"""Replay strict Python records against current immutable predecessors."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.adapters.python_observation import (
    response_document,
)
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
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import _entry, _index
from ..python_fixtures import qualification, reference
from .python_fixtures import native_observation, prepared_publication
from .test_python_finalizer import _exact_inputs, _finalize


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


@pytest.mark.parametrize(
    "change", ["null", "404", "empty", "yanked", "hash", "classification"]
)
def test_python_native_replay_rejects_index_claim_contradictions(change):
    """Approved file identities cannot override the original scoped index."""
    decision, _, originals = qualification()
    native = native_observation(decision, originals, "complete")
    document = native.to_document()
    if change == "null":
        document["index-response"] = None
    elif change == "classification":
        document["classification"] = "partial"
    else:
        entries = [_entry(native.registry, item) for item in originals]
        if change == "yanked":
            entries[0]["yanked"] = True
        elif change == "hash":
            entries[0]["hashes"]["sha256"] = "f" * 64
        response = (
            PythonHttpResponse(404, b"not found", "text/plain")
            if change == "404"
            else _index([] if change == "empty" else entries)
        )
        document["index-response"] = response_document(response)
        document["index-digest"] = python_digest(response.body)
    assert len(document["files"]) == len(originals)
    with pytest.raises((ValueError, TypeError)):
        python_native_observation_from_document(document, originals)


def finalize_replayed_native(document, originals):
    """Admit a fresh native proof before calling the actual shared Finalizer."""
    native = python_native_observation_from_document(document, originals)
    inputs = _exact_inputs()
    proof = replace(inputs.exact_proof[0], native=native)
    proof_ref = reference(proof.to_document(), 509)
    outcome = _finalize(
        replace(inputs, exact_proof=(proof, proof_ref)),
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    return native, proof_ref, outcome


def test_python_native_replay_cannot_forge_exact_satisfied_proof():
    """A 404 with declared complete files cannot enter zero-action proof."""
    decision, _, originals = qualification()
    response = PythonHttpResponse(404, b"missing project", "text/plain")
    native = replace(
        native_observation(decision, originals, "complete"),
        index_response=response,
        index_digest=python_digest(response.body),
    )
    with pytest.raises(ValueError, match=r"Python.*(?:index|inventory|files)"):
        finalize_replayed_native(native.to_document(), originals)


@pytest.mark.parametrize("scenario", ["404-absence", "unrelated-version"])
def test_python_native_replay_preserves_absence_and_unrelated_versions(
    scenario,
):
    """Replay preserves valid absence and exact originals in version scope."""
    decision, _, originals = qualification()
    native = native_observation(decision, originals, "complete")
    if scenario == "404-absence":
        response = PythonHttpResponse(404, b"missing project", "text/plain")
        absent = replace(
            native,
            index_response=response,
            index_digest=python_digest(response.body),
            files=(),
            classification="absent",
        )
        admitted = python_native_observation_from_document(
            absent.to_document(), originals
        )
        assert admitted == absent
        assert admitted.files == ()
        assert admitted.classification == "absent"
        return
    entries = [_entry(native.registry, item) for item in originals]
    unrelated = deepcopy(entries[0])
    unrelated["filename"] = unrelated["filename"].replace(
        native.version, "9.9.9"
    )
    response = _index([*entries, unrelated])
    native = replace(
        native,
        index_response=response,
        index_digest=python_digest(response.body),
    )
    admitted, proof_ref, outcome = finalize_replayed_native(
        native.to_document(), originals
    )
    assert admitted == native
    assert all(a is b for a, b in zip(admitted.files, originals, strict=True))
    assert outcome.disposition == "exact-satisfied"
    assert outcome.possibly_mutated is False
    assert outcome.direct_predecessor.reference == proof_ref


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
