"""Python lineage, terminal uncertainty and shared Result admission."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    PublicationResult,
    admit_release_record,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
)
from three_workflow_delivery_v3.release.attempt_finalizer import (
    finalize_attempt_outcome,
)
from three_workflow_delivery_v3.release.python_finalizer import (
    PythonExactSatisfiedProof,
    PythonFinalizationInputs,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonOperationResult,
    PythonPublicationResult,
)
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
)

from ..python_fixtures import (
    NOW,
    RUN_ID,
    TARGET,
    governance,
    qualification,
    reference,
)
from .python_fixtures import (
    native_observation,
    prepared_publication,
    publication_snapshot,
)

_DIGEST = "sha256:" + "1" * 64


def _inputs(marker):
    authorization = marker.authorization
    bundle = authorization.bundle
    snapshot = bundle.snapshot
    observation = snapshot.observation
    return PythonFinalizationInputs(
        observation.decision,
        observation.decision_reference,
        (observation, snapshot.observation_reference),
        (snapshot, bundle.snapshot_reference),
        (bundle, authorization.bundle_reference),
        (authorization, marker.authorization_reference),
    )


def _result(marker, marker_ref, state="published"):
    wheel = PythonOperationResult(
        0, "succeeded", _DIGEST, _DIGEST, readback_exact=True
    )
    sdist = PythonOperationResult(
        1, "succeeded", _DIGEST, _DIGEST, readback_exact=True
    )
    if state == "failed":
        sdist = PythonOperationResult(
            1, "unknown", None, None, readback_exact=False
        )
    return PythonPublicationResult(
        marker.attempt,
        marker_ref,
        (wheel, sdist),
        _DIGEST if state == "published" else None,
        final_readback_exact=state == "published",
    )


def _finalize(inputs, **overrides):
    terminal = inputs.terminal
    options = {
        "current": ReleaseAdmissionBindings(
            "live-release", RUN_ID, None, TARGET
        ),
        "run_attempt": 1,
        "publisher_conclusion": "success",
        "publication_step_outcome": "success",
        "publication_terminal_reference": canonicalize(
            None if terminal is None else terminal[1].to_document()
        ).decode(),
        "observation_conclusion": "success",
    }
    options.update(overrides)
    return finalize_attempt_outcome(inputs, **options)


@pytest.mark.parametrize("state", ["published", "failed"])
def test_python_result_finalizes_through_shared_dispatch(state):
    """The shared Outcome keeps Result lineage and partial-effect facts."""
    marker, marker_ref, _, _ = prepared_publication()
    result = _result(marker, marker_ref, state)
    result_ref = reference(result.to_document(), 508)
    inputs = replace(
        _inputs(marker),
        terminal=(result, result_ref),
        result_marker=(marker, marker_ref),
    )
    outcome = _finalize(inputs)
    assert type(outcome) is AttemptOutcome
    assert outcome.disposition == (
        "published" if state == "published" else "publication-failed"
    )
    assert outcome.possibly_mutated is (state == "failed")
    assert outcome.direct_predecessor.reference == result_ref
    assert outcome.direct_predecessor.kind == "publication-result"
    assert outcome.attempt == marker.attempt


@pytest.mark.parametrize("conclusion", ["success", "failure", "cancelled"])
def test_python_marker_only_is_unknown_despite_job_conclusion(conclusion):
    """No successful step or cancellation may replace a missing Result."""
    marker, marker_ref, _, _ = prepared_publication()
    outcome = _finalize(
        replace(_inputs(marker), terminal=(marker, marker_ref)),
        publisher_conclusion=conclusion,
        publication_step_outcome=conclusion,
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated
    assert outcome.direct_predecessor.kind == "mutation-marker"
    assert outcome.direct_predecessor.reference == marker_ref


@pytest.mark.parametrize("conclusion", ["success", "failure", "cancelled"])
def test_python_executed_step_without_terminal_is_unknown(conclusion):
    """Explicit null is admissible, but cannot prove no upload occurred."""
    marker, _, _, _ = prepared_publication()
    outcome = _finalize(
        _inputs(marker),
        publisher_conclusion=conclusion,
        publication_step_outcome=conclusion,
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated


@pytest.mark.parametrize("value", [None, ""])
@pytest.mark.parametrize("conclusion", ["success", "failure", "cancelled"])
def test_python_running_publisher_requires_explicit_terminal_output(
    value, conclusion
):
    """Missing wire output is an admission error rather than implicit null."""
    marker, _, _, _ = prepared_publication()
    with pytest.raises(ValueError, match="omitted its terminal"):
        _finalize(
            _inputs(marker),
            publisher_conclusion=conclusion,
            publication_terminal_reference=value,
        )


@pytest.mark.parametrize(
    "value", [" null", "null\n", "{}", "[]", "true", "{invalid"]
)
def test_python_terminal_scalar_rejects_noncanonical_or_foreign_shapes(value):
    """The existing scalar parser remains the sole wire admission boundary."""
    marker, _, _, _ = prepared_publication()
    with pytest.raises(
        (ValueError, TypeError),
        match=r"canonical JSON|artifact reference|Expecting",
    ):
        _finalize(_inputs(marker), publication_terminal_reference=value)


@pytest.mark.parametrize(
    "change", ["run", "target", "purpose", "rerun", "boolean-attempt"]
)
def test_python_finalizer_rejects_foreign_current_attempt(change):
    """Foreign run, target and simulation inputs cannot authorize Live."""
    marker, _, _, _ = prepared_publication()
    overrides = {}
    if change in {"run", "target", "purpose"}:
        overrides["current"] = ReleaseAdmissionBindings(
            "release-simulation" if change == "purpose" else "live-release",
            RUN_ID + 1 if change == "run" else RUN_ID,
            1 if change == "purpose" else None,
            "f" * 40 if change == "target" else TARGET,
        )
    else:
        overrides["run_attempt"] = True if change == "boolean-attempt" else 2
    with pytest.raises(ValueError, match="current Attempt"):
        _finalize(_inputs(marker), **overrides)


@pytest.mark.parametrize(
    "link",
    [
        "decision_reference",
        "observation",
        "publication",
        "bundle",
        "authorization",
        "terminal",
        "result_marker",
    ],
)
def test_python_finalizer_rejects_substituted_transport_references(link):
    """Every direct record reference must bind its exact canonical bytes."""
    marker, marker_ref, _, _ = prepared_publication()
    result = _result(marker, marker_ref)
    inputs = replace(
        _inputs(marker),
        terminal=(result, reference(result.to_document(), 508)),
        result_marker=(marker, marker_ref),
    )
    if link == "decision_reference":
        inputs = replace(inputs, decision_reference=reference({}))
    else:
        pair = getattr(inputs, link)
        inputs = replace(inputs, **{link: (pair[0], reference({}))})
    with pytest.raises(ValueError, match="reference"):
        _finalize(inputs)


@pytest.mark.parametrize(
    "link",
    ["observation", "publication", "bundle", "authorization", "result_marker"],
)
def test_python_result_requires_complete_direct_predecessor_chain(link):
    """An otherwise valid Result does not make a missing ancestor optional."""
    marker, marker_ref, _, _ = prepared_publication()
    result = _result(marker, marker_ref)
    inputs = replace(
        _inputs(marker),
        terminal=(result, reference(result.to_document(), 508)),
        result_marker=(marker, marker_ref),
    )
    inputs = replace(inputs, **{link: None})
    with pytest.raises(
        ValueError, match=r"lineage|Authorization|marker predecessor"
    ):
        _finalize(inputs)


def _exact_inputs():
    decision, _, distributions = qualification()
    native = native_observation(decision, distributions, "complete")
    snapshot = publication_snapshot(decision, native)
    snapshot_ref = reference(snapshot.to_document(), 504)
    proof = PythonExactSatisfiedProof(
        snapshot,
        snapshot_ref,
        governance(observed_at=NOW + timedelta(seconds=1)),
        native,
        NOW + timedelta(seconds=1),
    )
    return PythonFinalizationInputs(
        decision,
        snapshot.observation.decision_reference,
        (snapshot.observation, snapshot.observation_reference),
        (snapshot, snapshot_ref),
        exact_proof=(proof, reference(proof.to_document(), 509)),
    )


def test_python_exact_satisfied_needs_fresh_proof_and_no_publisher():
    """Fresh originals prove exact satisfaction without credentials."""
    inputs = _exact_inputs()
    outcome = _finalize(
        inputs,
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    assert outcome.disposition == "exact-satisfied"
    assert not outcome.possibly_mutated
    assert (
        outcome.direct_predecessor.kind == "exact-satisfied-finalization-proof"
    )
    assert outcome.direct_predecessor.reference == inputs.exact_proof[1]


def test_python_zero_action_without_fresh_proof_remains_unknown():
    """Initial exact observation cannot replace a fresh Finalizer proof."""
    inputs = replace(_exact_inputs(), exact_proof=None)
    outcome = _finalize(
        inputs,
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    assert outcome.disposition == "unknown"
    assert not outcome.possibly_mutated


@pytest.mark.parametrize("proof", [False, True])
def test_python_zero_action_rejects_scheduled_publisher(proof):
    """A zero-action Snapshot cannot consume Environment authority."""
    inputs = _exact_inputs()
    if not proof:
        inputs = replace(inputs, exact_proof=None)
    with pytest.raises(
        ValueError, match=r"foreign authority|scheduled a publisher"
    ):
        _finalize(inputs)


@pytest.mark.parametrize(
    "change", ["time", "governance-time", "partial", "foreign-destination"]
)
def test_python_exact_proof_rejects_stale_partial_or_foreign_state(change):
    """Exact satisfaction needs a new destination-specific whole-set read."""
    proof = _exact_inputs().exact_proof[0]
    changes = {
        "time": {"observed_at": NOW},
        "governance-time": {"fresh_governance": governance()},
        "partial": {
            "native": replace(
                proof.native,
                files=proof.native.files[:1],
                classification="partial",
            )
        },
        "foreign-destination": {"fresh_governance": governance("pypi")},
    }
    with pytest.raises(ValueError, match="Python"):
        replace(proof, **changes[change])


def test_python_later_exact_read_cannot_upgrade_marker_only_attempt():
    """Exact zero-action proof cannot be borrowed to erase a lost Result."""
    marker, marker_ref, _, _ = prepared_publication()
    inputs = replace(
        _inputs(marker),
        terminal=(marker, marker_ref),
        exact_proof=_exact_inputs().exact_proof,
    )
    with pytest.raises(ValueError, match="terminal scalar or current Attempt"):
        _finalize(inputs)


@pytest.mark.parametrize(
    "stage",
    ["decision", "observation", "publication", "bundle", "authorization"],
)
def test_python_prepublication_failure_selects_last_direct_predecessor(stage):
    """Skipped mutation retains the latest accepted predecessor."""
    marker, _, _, _ = prepared_publication()
    full = _inputs(marker)
    stages = ["observation", "publication", "bundle", "authorization"]
    count = 0 if stage == "decision" else stages.index(stage) + 1
    inputs = replace(full, **dict.fromkeys(stages[count:]))
    outcome = _finalize(
        inputs,
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    assert outcome.disposition == "failed-before-publication"
    assert not outcome.possibly_mutated
    expected_ref = (
        inputs.decision_reference
        if stage == "decision"
        else getattr(inputs, stage)[1]
    )
    assert outcome.direct_predecessor.reference == expected_ref


@pytest.mark.parametrize("state", ["failed", "incomplete"])
def test_python_failed_qualification_has_no_downstream_authority(state):
    """Incomplete Qualification cannot borrow a later publication Snapshot."""
    marker, _, _, _ = prepared_publication()
    full = _inputs(marker)
    evidence = (
        (
            replace(full.decision.evidence[0], result="failed"),
            *full.decision.evidence[1:],
        )
        if state == "failed"
        else ()
    )
    incomplete = PythonQualificationDecision(full.decision.snapshot, evidence)
    assert incomplete.result == state
    inputs = PythonFinalizationInputs(
        incomplete, reference(incomplete.to_document())
    )
    assert (
        _finalize(
            inputs,
            publisher_conclusion="skipped",
            publication_step_outcome=None,
            publication_terminal_reference=None,
        )
        is None
    )
    with pytest.raises(ValueError, match="downstream authority"):
        _finalize(replace(inputs, observation=full.observation))


@pytest.mark.parametrize("state", ["published", "failed"])
def test_python_result_roundtrips_through_shared_transport_admission(state):
    """The Python variant preserves shared schema and run bindings."""
    marker, marker_ref, _, _ = prepared_publication()
    result = _result(marker, marker_ref, state)
    admitted = admit_release_record(
        canonicalize(result.to_document()),
        expected_type=PythonPublicationResult,
        expected_digest=result.result_digest,
        expected_bindings=ReleaseAdmissionBindings(
            "live-release", RUN_ID, None, TARGET, "publish-python"
        ),
    )
    assert type(admitted) is PythonPublicationResult
    assert admitted == result
    with pytest.raises(ValueError, match=r"unknown field|missing required"):
        release_record_from_document(
            result.to_document(), expected_type=PublicationResult
        )


@pytest.mark.parametrize("field", ["workflow_run_id", "target", "producer"])
def test_python_result_shared_transport_rejects_foreign_bindings(field):
    """Shared admission enforces destination producer and run scope."""
    marker, marker_ref, _, _ = prepared_publication()
    result = _result(marker, marker_ref)
    bindings = ReleaseAdmissionBindings(
        "live-release", RUN_ID, None, TARGET, "publish-python"
    )
    bindings = replace(
        bindings,
        **{field: RUN_ID + 1 if field == "workflow_run_id" else "foreign"},
    )
    with pytest.raises(ValueError, match=r"binding|producer|target|run"):
        admit_release_record(
            canonicalize(result.to_document()),
            expected_type=PythonPublicationResult,
            expected_digest=result.result_digest,
            expected_bindings=bindings,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("ordinal", True),
        ("ordinal", 2),
        ("ordinal", "0"),
        ("status", "published"),
        ("response-digest", None),
        ("readback-digest", None),
        ("readback-exact", 1),
        ("extra", "ignored"),
    ],
)
def test_python_shared_result_parser_rejects_malformed_operation(field, value):
    """Closed operations reject Boolean aliases and missing facts."""
    marker, marker_ref, _, _ = prepared_publication()
    document = _result(marker, marker_ref).to_document()
    document["operations"][0][field] = value
    with pytest.raises((ValueError, TypeError), match="Python"):
        release_record_from_document(
            document, expected_type=PythonPublicationResult
        )


@pytest.mark.parametrize(
    "change",
    [
        "absent-sdist-readback",
        "inexact-sdist-readback",
        "different-final-digest",
        "inexact-final-with-digest",
    ],
)
def test_python_shared_result_parser_rejects_contradictory_final_readback(
    change,
):
    """Canonical wire bytes cannot conceal inconsistent final evidence."""
    marker, marker_ref, _, _ = prepared_publication()
    document = _result(marker, marker_ref).to_document()
    sdist = document["operations"][1]
    if change == "absent-sdist-readback":
        sdist["readback-digest"] = None
        sdist["readback-exact"] = False
    elif change == "inexact-sdist-readback":
        sdist["readback-exact"] = False
    elif change == "different-final-digest":
        document["final-readback-digest"] = "sha256:" + "2" * 64
    else:
        sdist["readback-exact"] = False
        document["final-readback-exact"] = False
        document["result"] = "failed"
    with pytest.raises(ValueError, match=r"Python.*readback"):
        admit_release_record(
            canonicalize(document),
            expected_type=PythonPublicationResult,
            expected_digest=reference(document).payload_digest,
            expected_bindings=ReleaseAdmissionBindings(
                "live-release", RUN_ID, None, TARGET, "publish-python"
            ),
        )


@pytest.mark.parametrize(
    "change",
    ["foreign-attempt", "scalar", "skipped", "dangling", "marker-predecessor"],
)
def test_python_finalizer_rejects_terminal_lineage_or_scalar_substitution(
    change,
):
    """A valid record cannot replace this Attempt's exact terminal reference."""
    marker, marker_ref, _, _ = prepared_publication()
    inputs = replace(_inputs(marker), terminal=(marker, marker_ref))
    overrides = {}
    if change == "foreign-attempt":
        result = _result(marker, marker_ref)
        foreign, _, _ = qualification(run_id=RUN_ID + 1)
        result = replace(result, attempt=foreign.snapshot.attempt)
        inputs = replace(
            inputs, terminal=(result, reference(result.to_document()))
        )
    elif change == "scalar":
        overrides["publication_terminal_reference"] = canonicalize(
            reference({}).to_document()
        ).decode()
    elif change == "skipped":
        overrides.update(
            publisher_conclusion="skipped", publication_step_outcome=None
        )
    elif change == "dangling":
        inputs = replace(inputs, terminal=None)
        overrides["publication_terminal_reference"] = canonicalize(
            marker_ref.to_document()
        ).decode()
    else:
        inputs = replace(inputs, result_marker=(marker, marker_ref))
    with pytest.raises(ValueError, match=r"terminal|Marker-only"):
        _finalize(inputs, **overrides)


def test_python_failed_final_readback_retains_known_mutation_flag():
    """Two completed uploads cannot be reported as an unmutated failure."""
    marker, marker_ref, _, _ = prepared_publication()
    published = _result(marker, marker_ref)
    result = replace(
        published,
        operations=(
            published.operations[0],
            replace(published.operations[1], readback_exact=False),
        ),
        final_readback_exact=False,
        final_readback_digest=None,
    )
    assert result.result == "failed"
    assert result.mutation_classification == "mutated"
    admitted = admit_release_record(
        canonicalize(result.to_document()),
        expected_type=PythonPublicationResult,
        expected_digest=result.result_digest,
        expected_bindings=ReleaseAdmissionBindings(
            "live-release", RUN_ID, None, TARGET, "publish-python"
        ),
    )
    assert admitted.final_readback_digest is None
    assert admitted.operations[1].readback_digest == _DIGEST
    assert admitted.operations[1].readback_exact is False
    assert admitted.result == "failed"
    inputs = replace(
        _inputs(marker),
        terminal=(result, reference(result.to_document(), 508)),
        result_marker=(marker, marker_ref),
    )
    outcome = _finalize(inputs)
    assert outcome.disposition == "publication-failed"
    assert outcome.possibly_mutated
