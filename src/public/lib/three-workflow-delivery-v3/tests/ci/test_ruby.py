"""Ruby CI selection and independent two-obligation qualification scenarios."""

from dataclasses import replace
from subprocess import TimeoutExpired

import pytest
from three_workflow_delivery_v3.adapters.ruby import RubyDistribution
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.ruby import (
    RubyCiDecision,
    RubyCiPlan,
    ruby_ci_evidence_from_document,
    ruby_ci_plan_from_document,
    run_ruby_ci_quality,
)
from three_workflow_delivery_v3.records import ruby as ruby_records
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.ruby import RubyArtifact
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
        (
            (
                (
                    "tests/private/app/workflow-delivery/fixtures/products/"
                    "hcoona-release-smoke-npm/src/index.js"
                ),
            ),
            False,
        ),
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
        evidence = (
            replace(
                evidence[0],
                artifact=artifact,
                detail=canonicalize(
                    {"digest": artifact.reference.payload_digest}
                ),
            ),
            evidence[1],
        )
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


def test_ruby_ci_consumer_timeout_retains_sanitized_failed_evidence(
    monkeypatch,
):
    """Timeout preserves separate failed evidence without native execution."""
    source = model("ci-pr-slice-shadow")
    planned = RubyCiPlan(source, reference(source.to_document()), None)
    witness = source.build_request().witness
    filename = f"hcoona-release-smoke-ruby-{witness.nbgv.native_version}.gem"
    payload = b"modeled-inspected-original-for-timeout-boundary"
    distribution = RubyDistribution(filename, payload, witness, b"{}")
    artifact_ref = replace(
        reference({}, 801, filename), payload_digest=distribution.digest
    )
    transport = ArtifactTransportIdentity(
        artifact_ref.artifact_id,
        "ruby-timeout-fixture",
        artifact_ref.artifact_url,
        artifact_ref.artifact_digest,
        "build-ruby",
        source.context.workflow_run_id,
        source.context.run_attempt,
    )
    artifact = RubyArtifact(
        filename, len(payload), witness, artifact_ref, transport
    )
    monkeypatch.setattr(
        ruby_records, "inspect_ruby_distribution", lambda *_args: distribution
    )
    attempts = []

    def timeout(current):
        attempts.append(current.digest)
        raise TimeoutExpired(
            ["private-native-command"],
            300,
            output=b"private-native-stdout",
            stderr=b"private-native-stderr",
        )

    evidence = run_ruby_ci_quality(planned, artifact, payload, consumer=timeout)
    decision = RubyCiDecision(planned, evidence)
    assert attempts == [distribution.digest]
    assert [item.result for item in evidence] == ["passed", "failed"]
    assert [item.definition for item in evidence] == [
        "ruby/gem-contents-v1",
        "ruby/gem-install-require-v1",
    ]
    assert parse_canonical_json(evidence[0].detail) == {
        "digest": distribution.digest
    }
    assert parse_canonical_json(evidence[1].detail) == {
        "error-kind": "TimeoutExpired"
    }
    assert decision.result == "failed"
    documents = str([item.to_document() for item in evidence]) + str(
        decision.to_document()
    )
    for sensitive in (
        "private-native-command",
        "private-native-stdout",
        "private-native-stderr",
    ):
        assert sensitive not in documents

    assert decision.to_document()["authority"] == "non-authoritative"


@pytest.mark.parametrize("boundary", ["constructor", "import"])
@pytest.mark.parametrize(
    "detail",
    [
        {},
        None,
        [],
        {"digest": None},
        {"digest": True},
        {"digest": "sha256:" + "f" * 64},
        {"digest": "payload", "extra": True},
        {"error-kind": "ValueError"},
        "container-digest",
        "extra-valid",
    ],
)
def test_ruby_ci_passed_content_detail_requires_original_digest(
    qualified, boundary, detail
):
    """The passed flag cannot authorize absent or foreign inspection detail."""
    _planned, evidence, _artifact, _original = qualified
    item = evidence[0]
    if detail == "container-digest":
        assert (
            item.artifact.artifact_digest
            != item.artifact.reference.payload_digest
        )
        detail = {"digest": item.artifact.artifact_digest}
    elif detail == "extra-valid":
        detail = {
            "digest": item.artifact.reference.payload_digest,
            "extra": True,
        }
    with pytest.raises((ValueError, TypeError)):
        _admit_detail(item, boundary, detail)


@pytest.mark.parametrize("boundary", ["constructor", "import"])
@pytest.mark.parametrize(
    "field",
    [
        "schema",
        "artifact-digest",
        "witness-digest",
        "project-id",
        "version",
        "install-output",
        "extra",
        "empty",
        "container-digest",
    ],
)
def test_ruby_ci_passed_consumer_detail_requires_closed_original_binding(
    qualified, boundary, field
):
    """Admission validates every consumer field against the same original."""
    _planned, evidence, artifact, _original = qualified
    item = evidence[1]
    valid = parse_canonical_json(item.detail)
    if field == "empty":
        invalid = [{}]
    elif field == "extra":
        invalid = [dict(valid, extra="private stdout")]
    elif field == "container-digest":
        assert artifact.artifact_digest != artifact.reference.payload_digest
        invalid = [dict(valid, **{"artifact-digest": artifact.artifact_digest})]
    else:
        missing = dict(valid)
        missing.pop(field)
        invalid = [
            missing,
            dict(valid, **{field: None}),
            dict(valid, **{field: True}),
        ]
        if field != "install-output":
            invalid.append(dict(valid, **{field: "foreign"}))
    for detail in invalid:
        with pytest.raises((ValueError, TypeError)):
            _admit_detail(item, boundary, detail)


@pytest.mark.parametrize("boundary", ["constructor", "import"])
@pytest.mark.parametrize("obligation", [0, 1])
def test_ruby_ci_failed_detail_requires_only_nonempty_error_kind(
    qualified, boundary, obligation
):
    """Failed Evidence cannot carry success details or unsanitized payloads."""
    _planned, evidence, _artifact, _original = qualified
    item = evidence[obligation]
    invalid = [
        {},
        None,
        [],
        {"error-kind": ""},
        {"error-kind": None},
        {"error-kind": True},
        {"error-kind": 1},
        {"error-kind": "ValueError", "stderr": "private output"},
        parse_canonical_json(item.detail),
    ]
    for detail in invalid:
        with pytest.raises((ValueError, TypeError)):
            _admit_detail(item, boundary, detail, result="failed")


def test_ruby_ci_detail_accepts_empty_log_and_custom_sanitized_error(
    qualified,
):
    """No output heuristic or speculative exception allowlist owns success."""
    planned, evidence, artifact, _original = qualified
    detail = parse_canonical_json(evidence[1].detail)
    detail["install-output"] = ""
    passed = replace(evidence[1], detail=canonicalize(detail))
    imported = ruby_ci_evidence_from_document(passed.to_document())
    assert parse_canonical_json(imported.detail)["install-output"] == ""
    assert RubyCiDecision(planned, (evidence[0], imported)).result == "passed"
    assert parse_canonical_json(evidence[0].detail) == {
        "digest": artifact.reference.payload_digest
    }
    for obligation in (0, 1):
        failed = replace(
            evidence[obligation],
            result="failed",
            detail=canonicalize({"error-kind": "CustomConsumerFailure"}),
        )
        failed = ruby_ci_evidence_from_document(failed.to_document())
        assert parse_canonical_json(failed.detail) == {
            "error-kind": "CustomConsumerFailure"
        }
        current = list(evidence)
        current[obligation] = failed
        outcome = RubyCiDecision(planned, tuple(current))
        assert outcome.result == "failed"
        assert outcome.to_document()["authority"] == "non-authoritative"


@pytest.mark.parametrize("change", ["missing-output", "typed-output", "extra"])
def test_ruby_ci_malformed_consumer_output_retains_failed_evidence(
    qualified, change
):
    """Producer shape failures become sanitized failure evidence."""
    planned, _evidence, artifact, original = qualified

    def malformed(current):
        detail = consumer(current)
        if change == "missing-output":
            detail.pop("install-output")
        elif change == "typed-output":
            detail["install-output"] = True
        else:
            detail["private-output"] = "must not leak"
        return detail

    evidence = run_ruby_ci_quality(
        planned, artifact, original.content, consumer=malformed
    )
    assert [item.result for item in evidence] == ["passed", "failed"]
    assert parse_canonical_json(evidence[1].detail) == {
        "error-kind": "TypeError" if change == "typed-output" else "ValueError"
    }
    outcome = RubyCiDecision(planned, evidence)
    assert outcome.result == "failed"


def _admit_detail(item, boundary, detail, *, result="passed"):
    """Send identical candidate facts through either public admission path."""
    if boundary == "constructor":
        return replace(item, result=result, detail=canonicalize(detail))
    document = item.to_document()
    document.update(result=result, detail=detail)
    return ruby_ci_evidence_from_document(document)
