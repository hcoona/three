"""Python CI selection, evidence lineage and conservative Decision contracts."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.canonical import (
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.python import (
    PythonCiDecision,
    PythonCiPlan,
    python_ci_evidence_from_document,
    python_ci_plan_from_document,
    run_python_ci_quality,
)
from three_workflow_delivery_v3.repository.python_model import PYTHON_QUALITY

from ..python_fixtures import consumer, model, originals, reference


@pytest.fixture
def ci():
    """A selected current-revision Plan and both immutable original archives."""
    source = model()
    plan = PythonCiPlan(source, reference(source.to_document()), None)
    artifacts, payloads, _ = originals(source)
    return plan, artifacts, payloads


@pytest.mark.parametrize(
    ("paths", "selected"),
    [
        (None, True),
        ((), False),
        (("docs/README.md",), False),
        (("src/public/lib/nbgv-python/deleted.py",), True),
        (("src/public/lib/hcoona-release-smoke-python/deleted.py",), True),
        ((".github/workflows/workflow-delivery-v3-python-smoke.yml",), True),
        (("uv.lock",), True),
    ],
)
def test_python_ci_selects_conservatively_including_deleted_inputs(
    ci, paths, selected
):
    """Unknown comparison and deleted bounded inputs select the slice."""
    plan, _, _ = ci
    changed = replace(plan, changed_paths=paths)
    assert changed.selected is selected
    assert changed.to_document()["obligations"] == (
        list(PYTHON_QUALITY) if selected else []
    )


@pytest.mark.parametrize(
    "path", ["", "../outside", "/absolute", "a\\b", "a/./b"]
)
def test_python_ci_rejects_noncanonical_comparison_paths(ci, path):
    """Malformed paths cannot shrink the affected system."""
    with pytest.raises(ValueError, match="comparison path"):
        replace(ci[0], changed_paths=(path,))


@pytest.mark.parametrize("purpose", ["live-release", "release-simulation"])
def test_python_ci_plan_rejects_release_authority(purpose):
    """Release identity cannot enter CI's independent evidence namespace."""
    source = model(purpose)
    with pytest.raises(ValueError, match="CI Plan"):
        PythonCiPlan(source, reference(source.to_document()), None)


@pytest.mark.parametrize("change", ["control", "model-reference"])
def test_python_ci_plan_rejects_foreign_control_or_model(change):
    """The Plan binds both reviewed same-revision control and Model bytes."""
    source = model(control="different-control" if change == "control" else None)
    ref = reference(source.to_document() if change == "control" else {})
    with pytest.raises(ValueError, match="same-revision Model"):
        PythonCiPlan(source, ref, None)


def test_python_ci_quality_requires_both_original_payloads(ci):
    """A single original cannot produce complete CI qualification."""
    plan, artifacts, payloads = ci
    with pytest.raises(ValueError, match="both original payloads"):
        run_python_ci_quality(plan, artifacts, payloads[:1], consumer=consumer)


def test_python_ci_quality_proves_three_obligations_without_release_authority(
    ci,
):
    """Inspection and both consumers contribute to one CI Decision."""
    plan, artifacts, payloads = ci
    calls = []

    def run(distribution):
        calls.append(distribution.variant)
        return consumer(distribution)

    evidence = run_python_ci_quality(plan, artifacts, payloads, consumer=run)
    assert calls == ["wheel", "sdist"]
    assert [item.definition for item in evidence] == list(PYTHON_QUALITY)
    assert [item.result for item in evidence] == ["passed"] * len(
        PYTHON_QUALITY
    )
    decision = PythonCiDecision(plan, evidence)
    assert decision.result == "passed"
    assert decision.to_document()["authority"] == "non-authoritative"
    assert parse_canonical_json(evidence[-1].detail)[
        "rebuilt-wheel-digest"
    ].startswith("sha256:")


@pytest.mark.parametrize(
    "failure", ["exception", "foreign-digest", "foreign-variant"]
)
def test_python_ci_consumer_failure_is_retained_and_cannot_pass(ci, failure):
    """A failed consumer obligation cannot be replaced by archive inspection."""
    plan, artifacts, payloads = ci

    def run(distribution):
        if distribution.variant == "wheel":
            if failure == "exception":
                message = "consumer failed"
                raise ValueError(message)
            result = consumer(distribution)
            return replace(
                result,
                **(
                    {"original_digest": "sha256:" + "0" * 64}
                    if failure == "foreign-digest"
                    else {"variant": "sdist"}
                ),
            )
        return consumer(distribution)

    evidence = run_python_ci_quality(plan, artifacts, payloads, consumer=run)
    assert [item.result for item in evidence] == ["passed", "failed", "passed"]
    assert PythonCiDecision(plan, evidence).result == "failed"
    assert parse_canonical_json(evidence[1].detail) == {
        "error-kind": "ValueError"
    }


@pytest.mark.parametrize("count", [0, 1, 2])
def test_python_ci_missing_evidence_is_incomplete(ci, count):
    """Missing obligations never infer success from neighboring jobs."""
    plan, artifacts, payloads = ci
    evidence = run_python_ci_quality(
        plan, artifacts, payloads, consumer=consumer
    )
    assert PythonCiDecision(plan, evidence[:count]).result == "incomplete"


@pytest.mark.parametrize("change", ["duplicate", "plan", "different-build"])
def test_python_ci_rejects_conflicting_evidence_lineage(ci, change):
    """Each obligation belongs to one current Plan and original build set."""
    plan, artifacts, payloads = ci
    evidence = run_python_ci_quality(
        plan, artifacts, payloads, consumer=consumer
    )
    if change == "duplicate":
        evidence = (*evidence, evidence[0])
    elif change == "plan":
        evidence = (
            replace(evidence[0], plan_digest="sha256:" + "0" * 64),
            *evidence[1:],
        )
    else:
        changed = replace(
            artifacts[0],
            reference=replace(
                artifacts[0].reference, payload_digest="sha256:" + "0" * 64
            ),
        )
        evidence = (
            replace(evidence[0], artifacts=(changed, artifacts[1])),
            *evidence[1:],
        )
    with pytest.raises(ValueError, match="Python CI"):
        PythonCiDecision(plan, evidence)


def test_python_ci_empty_selection_has_no_execution_or_evidence(ci):
    """An unselected Plan is empty and admits no borrowed obligations."""
    plan, artifacts, payloads = ci
    empty = replace(plan, changed_paths=())
    assert PythonCiDecision(empty, ()).result == "empty"
    with pytest.raises(ValueError, match="unselected"):
        run_python_ci_quality(empty, artifacts, payloads, consumer=consumer)
    evidence = run_python_ci_quality(
        plan, artifacts, payloads, consumer=consumer
    )
    with pytest.raises(ValueError, match="current Plan"):
        PythonCiDecision(empty, evidence)


@pytest.mark.parametrize("record", ["plan", "evidence"])
@pytest.mark.parametrize("change", ["schema", "extra", "primitive"])
def test_python_ci_serialized_records_reject_foreign_or_open_shapes(
    ci, record, change
):
    """Serialized CI records cannot alias Release or JSON primitive variants."""
    plan, artifacts, payloads = ci
    if record == "plan":
        document = plan.to_document()
        parse = python_ci_plan_from_document
    else:
        document = run_python_ci_quality(
            plan, artifacts, payloads, consumer=consumer
        )[0].to_document()
        parse = python_ci_evidence_from_document
    if change == "schema":
        document["schema"] = (
            "workflow-delivery/v3/python-qualification-evidence"
        )
    elif change == "extra":
        document["authority"] = "live-release"
    else:
        document["selected" if record == "plan" else "result"] = 1
    with pytest.raises(ValueError, match="Python"):
        parse(document)


@pytest.mark.parametrize(
    "paths",
    [
        ("unrelated/readme.md",),
        ("src/public/lib/new-project/source.py",),
        ("uv.lock", "unrelated/readme.md"),
        ("unrelated/readme.md", "uv.lock"),
    ],
)
def test_python_ci_unknown_paths_block_even_with_affected_input(ci, paths):
    """Every changed path needs classification before a Plan can form."""
    with pytest.raises(ValueError, match="unclassified"):
        replace(ci[0], changed_paths=paths)
