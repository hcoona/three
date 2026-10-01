"""Release qualification preserves current-Attempt lineage."""

from dataclasses import replace
from functools import partial

import pytest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.ruby import RubyCiEvidence
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.ruby_governance import RUBY_WORKFLOW
from three_workflow_delivery_v3.release.ruby_qualification import (
    RubyQualificationDecision,
    RubyQualificationSnapshot,
    admit_ruby_qualification_decision,
    qualify_ruby_release,
    ruby_qualification_evidence_from_document,
    ruby_qualification_snapshot_from_document,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RubyRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.ruby_provider import RubyNbgvFacts

from ..ruby_integration_fixtures import (
    admitted,
    consumer,
    context,
    governance,
    model,
    reference,
)


def snapshot(source, name="github-packages"):
    """Bind modeled ready Governance to the explicit protected Live request."""
    gov = governance(name)
    ctx = source.context
    intent = ReleaseIntent(
        "hcoona/three",
        RUBY_WORKFLOW,
        "refs/heads/main",
        ctx.target,
        ctx.request_id,
        "hcoona",
        ctx.workflow_run_id,
        "workflow_dispatch",
        "refs/heads/main",
        ctx.target,
        gov.registry.channel,
        "live",
        "live-release",
        "hcoona-release-smoke-ruby",
    )
    return RubyQualificationSnapshot(
        intent, source, reference(source.to_document()), gov
    )


@pytest.fixture
def qualified(ruby_release_original):
    """Qualify a separate Release original with controlled consumption."""
    source, artifact, distribution = ruby_release_original
    planned = snapshot(source)
    evidence = qualify_ruby_release(
        planned, artifact, distribution.content, consumer=consumer
    )
    return planned, evidence, artifact, distribution


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_release_qualifies_original_for_independent_destination(
    ruby_release_original, name
):
    """Keep independent destination Snapshots and evidence."""
    source, artifact, distribution = ruby_release_original
    planned = snapshot(source, name)
    evidence = qualify_ruby_release(
        planned, artifact, distribution.content, consumer=consumer
    )
    decision = RubyQualificationDecision(planned, evidence)
    assert decision.result == "passed"
    assert decision.artifact == artifact
    assert [e.definition for e in evidence] == [
        "ruby/gem-contents-v1",
        "ruby/gem-install-require-v1",
    ]
    assert [e.result for e in evidence] == ["passed", "passed"]
    assert all(e.snapshot_digest == planned.snapshot_digest for e in evidence)
    assert planned.governance.registry.name == name
    assert planned.intent.channel == (
        "buddy" if name == "github-packages" else "official"
    )
    assert decision.to_document()["attempt"] == planned.attempt.to_document()
    assert (
        planned.snapshot_digest
        != snapshot(
            source,
            "rubygems" if name == "github-packages" else "github-packages",
        ).snapshot_digest
    )


@pytest.mark.parametrize(
    "change",
    [
        "actor",
        "repository",
        "selected-ref",
        "control",
        "workflow",
        "request",
        "run",
        "destination",
        "reference",
        "model-purpose",
        "target",
    ],
)
def test_ruby_release_requires_exact_protected_current_bindings(change):
    """Keep admission bound to the current request and registry."""
    planned = snapshot(model("live-release"))
    intent_changes = {
        "actor": {"actor": "foreign"},
        "repository": {"repository": "hcoona/foreign"},
        "selected-ref": {
            "selected_ref": "refs/heads/feature",
            "workflow_ref": "refs/heads/feature",
        },
        "workflow": {
            "workflow_path": (
                ".github/workflows/workflow-delivery-v3-ruby-bootstrap.yml"
            )
        },
        "request": {"request_id": "release-request:" + "c" * 64},
        "run": {"workflow_run_id": 1000},
        "target": {"target": "c" * 40, "workflow_sha": "c" * 40},
    }
    if change in intent_changes:
        updates = {"intent": replace(planned.intent, **intent_changes[change])}
    elif change == "control":
        facts = admitted(
            replace(context(purpose="live-release"), control="foreign-control")
        )
        source = RubyRepositoryModelSnapshot(
            facts.manifest.context,
            facts.result,
            facts.request_reference,
            facts.result_reference,
        )
        updates = {
            "model": source,
            "model_reference": reference(source.to_document()),
        }
    elif change == "destination":
        updates = {"governance": governance("rubygems")}
    elif change == "reference":
        updates = {"model_reference": reference({})}
    else:
        source = model()
        updates = {
            "model": source,
            "model_reference": reference(source.to_document()),
        }
    with pytest.raises(ValueError, match="Ruby"):
        replace(planned, **updates)


def test_ruby_release_requires_public_main_nbgv_facts():
    """Reject private NBGV facts for live main admission."""
    source = model("live-release")
    raw = parse_canonical_json(source.provider.nbgv.raw_bytes)
    raw["PublicRelease"] = False
    provider = replace(
        source.provider,
        nbgv=RubyNbgvFacts(
            canonicalize(raw), source.provider.nbgv.native_version
        ),
    )
    changed = RubyRepositoryModelSnapshot(
        source.context,
        provider,
        source.request_reference,
        reference(provider.to_document()),
    )
    with pytest.raises(ValueError, match="public-main NBGV"):
        snapshot(changed)


def test_ruby_release_rejects_ci_artifact_and_ci_evidence(
    qualified, ruby_integration_original
):
    """Neither bytes nor successful Evidence from CI provide Release lineage."""
    planned, evidence, _, _ = qualified
    _, ci_artifact, ci_distribution = ruby_integration_original
    with pytest.raises(ValueError, match="current-build"):
        qualify_ruby_release(
            planned, ci_artifact, ci_distribution.content, consumer=consumer
        )
    ci_evidence = RubyCiEvidence(
        planned.snapshot_digest,
        evidence[0].definition,
        evidence[0].artifact,
        "passed",
        evidence[0].detail,
    )
    with pytest.raises(ValueError, match="Release Evidence"):
        RubyQualificationDecision(planned, (ci_evidence, evidence[1]))


@pytest.mark.parametrize("count", [0, 1])
def test_ruby_release_missing_obligations_hide_artifact(qualified, count):
    """Incomplete qualification exposes no publication artifact."""
    planned, evidence, _, _ = qualified
    decision = RubyQualificationDecision(planned, evidence[:count])
    assert decision.result == "incomplete"
    with pytest.raises(ValueError, match="successful Qualification"):
        _ = decision.artifact


@pytest.mark.parametrize(
    "failure",
    ["exception", "artifact-digest", "witness-digest", "project-id", "version"],
)
def test_ruby_release_consumer_failure_cannot_expose_artifact(
    qualified, failure
):
    """Retain consumer failure despite successful inspection."""
    planned, _, artifact, distribution = qualified

    def failed(current):
        if failure == "exception":
            message = "controlled consumer failure"
            raise RuntimeError(message)
        detail = consumer(current)
        detail[failure] = "foreign"
        return detail

    evidence = qualify_ruby_release(
        planned, artifact, distribution.content, consumer=failed
    )
    decision = RubyQualificationDecision(planned, evidence)
    assert [e.result for e in evidence] == ["passed", "failed"]
    assert decision.result == "failed"
    assert parse_canonical_json(evidence[1].detail) == {
        "error-kind": "RuntimeError" if failure == "exception" else "ValueError"
    }
    with pytest.raises(ValueError, match="successful Qualification"):
        _ = decision.artifact


@pytest.mark.parametrize(
    "change", ["duplicate", "snapshot", "run", "different-build"]
)
def test_ruby_release_rejects_cross_attempt_or_conflicting_evidence(
    qualified, change
):
    """All obligations must bind the same original in this exact Attempt."""
    planned, evidence, artifact, _ = qualified
    if change == "duplicate":
        evidence = (*evidence, evidence[0])
    elif change == "snapshot":
        evidence = (
            replace(evidence[0], snapshot_digest="sha256:" + "0" * 64),
            evidence[1],
        )
    else:
        if change == "run":
            artifact = replace(
                artifact,
                transport=replace(artifact.transport, workflow_run_id=1000),
            )
        else:
            artifact = replace(
                artifact,
                reference=replace(
                    artifact.reference, payload_digest="sha256:" + "0" * 64
                ),
            )
        evidence = (replace(evidence[0], artifact=artifact), evidence[1])
    with pytest.raises(ValueError, match="Ruby"):
        RubyQualificationDecision(planned, evidence)


def test_ruby_release_records_roundtrip_replays_complete_decision(qualified):
    """Strict imported predecessors reproduce only their own qualification."""
    planned, evidence, artifact, _ = qualified
    imported = ruby_qualification_snapshot_from_document(planned.to_document())
    imported_evidence = tuple(
        ruby_qualification_evidence_from_document(e.to_document())
        for e in evidence
    )
    original = RubyQualificationDecision(planned, evidence)
    admitted = admit_ruby_qualification_decision(
        original.to_document(), imported, imported_evidence
    )
    assert admitted.artifact == artifact
    assert admitted.result == "passed"
    assert admitted.decision_digest == original.decision_digest
    assert admitted.snapshot.intent.mode == "live"


@pytest.mark.parametrize("record", ["snapshot", "evidence", "decision"])
@pytest.mark.parametrize("change", ["schema", "extra", "outcome"])
def test_ruby_release_importers_reject_foreign_or_tampered_records(
    qualified, record, change
):
    """A foreign namespace, open field or tampered conclusion never imports."""
    planned, evidence, _, _ = qualified
    decision = RubyQualificationDecision(planned, evidence)
    document = {
        "snapshot": planned.to_document,
        "evidence": evidence[0].to_document,
        "decision": decision.to_document,
    }[record]()
    if change == "schema":
        document["schema"] = "workflow-delivery/v3/ruby-ci-evidence"
    elif change == "extra":
        document["authority"] = "publication"
    elif record == "snapshot":
        document["obligations"] = []
    else:
        document["result"] = "incomplete"
    parse = {
        "snapshot": ruby_qualification_snapshot_from_document,
        "evidence": ruby_qualification_evidence_from_document,
        "decision": partial(
            admit_ruby_qualification_decision,
            snapshot=planned,
            evidence=evidence,
        ),
    }[record]
    with pytest.raises(ValueError, match="Ruby"):
        parse(document)
