"""Release-only Python qualification and current-Attempt evidence contracts."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.python import PythonCiEvidence
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
    qualify_python_release,
)
from three_workflow_delivery_v3.repository.python_model import PYTHON_QUALITY

from ..python_fixtures import (
    consumer,
    governance,
    model,
    qualification,
    reference,
)


def test_python_release_qualification_proves_own_originals_and_attempt():
    """Release qualification exposes only its independently passing pair."""
    decision, _, _ = qualification()
    assert decision.result == "passed"
    assert [item.definition for item in decision.evidence] == list(
        PYTHON_QUALITY
    )
    assert tuple(item.variant for item in decision.artifacts) == (
        "wheel",
        "sdist",
    )
    assert (
        decision.snapshot.attempt.workflow_run_id
        == decision.snapshot.intent.workflow_run_id
    )
    assert decision.to_document()["producer"] == "finalize-python-qualification"


def test_python_release_destinations_have_independent_attempt_evidence():
    """Buddy records cannot become Official evidence or transport lineage."""
    buddy, _, _ = qualification("testpypi")
    official, _, _ = qualification(
        "pypi", buddy.snapshot.intent.workflow_run_id + 1
    )
    assert buddy.snapshot.attempt != official.snapshot.attempt
    assert buddy.snapshot.snapshot_digest != official.snapshot.snapshot_digest
    assert buddy.decision_digest != official.decision_digest
    with pytest.raises(ValueError, match="current Snapshot"):
        PythonQualificationDecision(official.snapshot, buddy.evidence)


@pytest.mark.parametrize(
    "change",
    ["actor", "channel", "run", "model-reference", "ci-model", "control"],
)
def test_python_release_snapshot_rejects_foreign_authority(change):
    """Protected current-request/source/actor bindings precede Qualification."""
    decision, _, _ = qualification()
    snapshot = decision.snapshot
    changes = {}
    if change == "actor":
        changes["intent"] = replace(snapshot.intent, actor="other-operator")
    elif change == "channel":
        changes["governance"] = governance("pypi")
    elif change == "run":
        changes["intent"] = replace(
            snapshot.intent, workflow_run_id=snapshot.intent.workflow_run_id + 1
        )
    elif change == "model-reference":
        changes["model_reference"] = reference({})
    else:
        changed = model(
            "slice-validation" if change == "ci-model" else "live-release",
            control="foreign-control" if change == "control" else None,
        )
        changes.update(
            model=changed, model_reference=reference(changed.to_document())
        )
    with pytest.raises(ValueError, match="protected Live bindings"):
        replace(snapshot, **changes)


@pytest.mark.parametrize("count", [0, 1, 2])
def test_python_release_incomplete_evidence_cannot_expose_artifacts(count):
    """No publication pair is available before all obligations pass."""
    full, _, _ = qualification()
    incomplete = PythonQualificationDecision(
        full.snapshot, full.evidence[:count]
    )
    assert incomplete.result == "incomplete"
    with pytest.raises(ValueError, match="successful Qualification"):
        _ = incomplete.artifacts


@pytest.mark.parametrize(
    "failure", ["exception", "foreign-digest", "foreign-variant"]
)
def test_python_release_consumer_failure_is_retained(failure):
    """A consumer failure leaves one failed obligation and no qualified pair."""
    full, payloads, _ = qualification()

    def run(distribution):
        if distribution.variant == "sdist":
            if failure == "exception":
                message = "consumer failed"
                raise ValueError(message)
            result = consumer(distribution)
            return replace(
                result,
                **(
                    {"original_digest": "sha256:" + "0" * 64}
                    if failure == "foreign-digest"
                    else {"variant": "wheel"}
                ),
            )
        return consumer(distribution)

    evidence = qualify_python_release(
        full.snapshot, full.artifacts, payloads, consumer=run
    )
    assert [item.result for item in evidence] == ["passed", "passed", "failed"]
    assert parse_canonical_json(evidence[-1].detail) == {
        "error-kind": "ValueError"
    }
    assert (
        PythonQualificationDecision(full.snapshot, evidence).result == "failed"
    )


@pytest.mark.parametrize(
    "change", ["ci", "duplicate", "snapshot", "run", "different-build"]
)
def test_python_release_decision_rejects_foreign_or_conflicting_evidence(
    change,
):
    """Only current Release Evidence over one original build can join."""
    full, _, _ = qualification()
    evidence = full.evidence
    first = evidence[0]
    if change == "ci":
        first = PythonCiEvidence(
            first.snapshot_digest,
            first.definition,
            first.artifacts,
            "passed",
            canonicalize({}),
        )
    elif change == "duplicate":
        evidence = (*evidence, first)
    elif change == "snapshot":
        first = replace(first, snapshot_digest="sha256:" + "0" * 64)
    else:
        artifact = first.artifacts[0]
        changed = (
            replace(
                artifact,
                transport=replace(artifact.transport, workflow_run_id=99),
            )
            if change == "run"
            else replace(
                artifact,
                reference=replace(
                    artifact.reference, payload_digest="sha256:" + "0" * 64
                ),
            )
        )
        first = replace(first, artifacts=(changed, first.artifacts[1]))
    if change != "duplicate":
        evidence = (first, *evidence[1:])
    with pytest.raises(ValueError, match="Python"):
        PythonQualificationDecision(full.snapshot, evidence)


def test_python_release_rejects_missing_original_payload():
    """A truncated original pair cannot form passing quality evidence."""
    full, payloads, _ = qualification()
    with pytest.raises(ValueError, match="shorter"):
        qualify_python_release(
            full.snapshot, full.artifacts, payloads[:1], consumer=consumer
        )
