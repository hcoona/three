"""Ruby CI selection and independent two-obligation qualification scenarios."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.canonical import parse_canonical_json
from three_workflow_delivery_v3.ci.ruby import (
    RubyCiDecision,
    RubyCiPlan,
    ruby_ci_evidence_from_document,
    ruby_ci_plan_from_document,
    run_ruby_ci_quality,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_QUALITY,
    RubyRepositoryModelSnapshot,
)

from ..ruby_integration_fixtures import (
    admitted,
    consumer,
    context,
    model,
    reference,
)


@pytest.mark.parametrize(
    ("paths", "selected"),
    [
        (None, True),
        ((), False),
        (("docs/README.md",), False),
        (("src/public/lib/hcoona-release-smoke-python/lib.py",), False),
        (("src/public/lib/hcoona-release-smoke-npm/src/index.js",), False),
        (("src/public/lib/hcoona-release-smoke-ruby/deleted.rb",), True),
        (("src/public/lib/three-workflow-delivery-v3/src/control.py",), True),
        ((".github/workflows/workflow-delivery-v3-ruby-smoke.yml",), True),
        (("mise.lock",), True),
        (
            (
                "docs/README.md",
                "src/public/lib/hcoona-release-smoke-ruby/deleted.rb",
            ),
            True,
        ),
    ],
)
def test_ruby_ci_selects_own_inputs_before_repository_fallback(paths, selected):
    """Select deleted Ruby sources before repository fallback."""
    source = model()
    plan = RubyCiPlan(source, reference(source.to_document()), paths)
    assert plan.selected is selected
    assert plan.to_document()["obligations"] == (
        list(RUBY_QUALITY) if selected else []
    )


@pytest.mark.parametrize(
    "path",
    [
        "",
        "../outside",
        "/absolute",
        "a\\b",
        "a/./b",
        "src/public/lib/hcoona-release-smoke-ruby-other/lib.rb",
    ],
)
def test_ruby_ci_unknown_or_malformed_paths_block_even_when_affected(path):
    """An affected source cannot conceal a malformed path or unknown sibling."""
    source = model()
    with pytest.raises(ValueError, match=r"comparison path|unclassified"):
        RubyCiPlan(source, reference(source.to_document()), ("mise.lock", path))


@pytest.mark.parametrize("change", ["release", "control", "model-reference"])
def test_ruby_ci_plan_rejects_foreign_context_or_reference(change):
    """CI consumes its own same-revision Model without Release promotion."""
    source = model(
        "live-release" if change == "release" else "slice-validation"
    )
    if change == "control":
        facts = admitted(replace(context(), control="foreign-control"))
        source = RubyRepositoryModelSnapshot(
            facts.manifest.context,
            facts.result,
            facts.request_reference,
            facts.result_reference,
        )
    ref = reference({} if change == "model-reference" else source.to_document())
    with pytest.raises(ValueError, match="same-revision Model"):
        RubyCiPlan(source, ref, None)


@pytest.fixture
def qualified(ruby_integration_original):
    """Use one actual native original and a controlled clean-consumer seam."""
    source, artifact, distribution = ruby_integration_original
    plan = RubyCiPlan(source, reference(source.to_document()), None)
    evidence = run_ruby_ci_quality(
        plan, artifact, distribution.content, consumer=consumer
    )
    return plan, evidence, artifact, distribution


def test_ruby_ci_qualifies_two_obligations_without_release_authority(
    qualified,
):
    """Both content and consumer evidence belong to one immutable original."""
    plan, evidence, artifact, _ = qualified
    assert [e.definition for e in evidence] == [
        "ruby/gem-contents-v1",
        "ruby/gem-install-require-v1",
    ]
    assert [e.result for e in evidence] == ["passed", "passed"]
    assert all(e.artifact == artifact for e in evidence)
    assert parse_canonical_json(evidence[0].detail) == {
        "digest": artifact.reference.payload_digest
    }
    assert (
        parse_canonical_json(evidence[1].detail)["project-id"]
        == "hcoona-release-smoke-ruby"
    )
    decision = RubyCiDecision(plan, evidence)
    assert decision.result == "passed"
    assert decision.to_document()["authority"] == "non-authoritative"
    assert decision.to_document()["evidence-digests"] == [
        e.evidence_digest for e in evidence
    ]


@pytest.mark.parametrize(
    "failure",
    [
        "exception",
        "artifact-digest",
        "witness-digest",
        "project-id",
        "version",
        "schema",
    ],
)
def test_ruby_ci_consumer_failure_is_retained_as_failed(qualified, failure):
    """Inspection cannot hide a failed or foreign clean-consumer result."""
    plan, _, artifact, distribution = qualified

    def failed(current):
        if failure == "exception":
            message = "controlled consumer failure"
            raise RuntimeError(message)
        detail = consumer(current)
        detail[failure] = "foreign"
        return detail

    evidence = run_ruby_ci_quality(
        plan, artifact, distribution.content, consumer=failed
    )
    assert [e.result for e in evidence] == ["passed", "failed"]
    assert RubyCiDecision(plan, evidence).result == "failed"
    assert parse_canonical_json(evidence[1].detail) == {
        "error-kind": "RuntimeError" if failure == "exception" else "ValueError"
    }


@pytest.mark.parametrize("count", [0, 1])
def test_ruby_ci_missing_obligations_stay_incomplete(qualified, count):
    """Every selected obligation needs evidence from the current plan."""
    plan, evidence, _, _ = qualified
    assert RubyCiDecision(plan, evidence[:count]).result == "incomplete"


@pytest.mark.parametrize(
    "change", ["duplicate", "plan", "different-build", "other-run"]
)
def test_ruby_ci_rejects_conflicting_evidence_lineage(qualified, change):
    """No duplicate, borrowed plan/run or mixed original may qualify."""
    plan, evidence, artifact, _ = qualified
    if change == "duplicate":
        evidence = (*evidence, evidence[0])
    elif change == "plan":
        evidence = (
            replace(evidence[0], plan_digest="sha256:" + "0" * 64),
            evidence[1],
        )
    else:
        if change == "different-build":
            artifact = replace(
                artifact,
                reference=replace(
                    artifact.reference, payload_digest="sha256:" + "0" * 64
                ),
            )
        else:
            artifact = replace(
                artifact,
                transport=replace(artifact.transport, workflow_run_id=1000),
            )
        evidence = (replace(evidence[0], artifact=artifact), evidence[1])
    with pytest.raises(ValueError, match="Ruby"):
        RubyCiDecision(plan, evidence)


def test_ruby_ci_unselected_plan_has_no_execution_or_borrowed_evidence(
    qualified,
):
    """Repository-only CI is empty and cannot run or import quality work."""
    plan, evidence, artifact, distribution = qualified
    empty = replace(plan, changed_paths=())
    assert RubyCiDecision(empty, ()).result == "empty"
    with pytest.raises(ValueError, match="unselected"):
        run_ruby_ci_quality(
            empty, artifact, distribution.content, consumer=consumer
        )
    with pytest.raises(ValueError, match="current Plan"):
        RubyCiDecision(empty, evidence)


@pytest.mark.parametrize("record", ["plan", "evidence"])
@pytest.mark.parametrize("change", ["schema", "extra", "primitive"])
def test_ruby_ci_importers_reject_foreign_open_or_coerced_records(
    qualified, record, change
):
    """CI records keep their own exact schema and primitive types."""
    plan, evidence, _, _ = qualified
    document = (
        plan.to_document() if record == "plan" else evidence[0].to_document()
    )
    parse = (
        ruby_ci_plan_from_document
        if record == "plan"
        else ruby_ci_evidence_from_document
    )
    if change == "schema":
        document["schema"] = "workflow-delivery/v3/ruby-qualification-evidence"
    elif change == "extra":
        document["authority"] = "live-release"
    else:
        document["selected" if record == "plan" else "result"] = 1
    with pytest.raises(ValueError, match="Ruby"):
        parse(document)
