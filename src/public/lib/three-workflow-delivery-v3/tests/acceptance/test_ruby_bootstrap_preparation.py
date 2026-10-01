"""Native first-project preparation and strict bootstrap-only evidence."""

from dataclasses import replace
from subprocess import TimeoutExpired

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.acceptance import (
    ruby_bootstrap_preparation as preparation,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_preparation import (
    RubyBootstrapEvidence,
    RubyBootstrapPlan,
    RubyBootstrapQualification,
    build_ruby_bootstrap,
    provide_ruby_bootstrap,
    ruby_bootstrap_evidence_from_document,
    run_ruby_bootstrap_quality,
)
from three_workflow_delivery_v3.adapters import ruby as ruby_adapter
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.ruby import ruby_ci_evidence_from_document
from three_workflow_delivery_v3.release.ruby_qualification import (
    ruby_qualification_evidence_from_document,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)
from three_workflow_delivery_v3.repository.ruby_model import RUBY_QUALITY

from ..ruby_fixtures import git
from ..ruby_integration_fixtures import consumer
from .ruby_bootstrap_fixtures import (
    CURRENT,
    EXPIRED,
    artifact_for,
    modeled_plan,
    native_inputs,
    plan_for_provider,
    run_for_provider,
    transport,
)


@pytest.fixture(scope="module")
def native(tmp_path_factory):
    """Build one original with frozen facts and guarded native commands."""
    root, run, materialization = native_inputs(
        tmp_path_factory.mktemp("ruby-bootstrap")
    )
    payload = provide_ruby_bootstrap(
        root, run, materialization, clock=lambda: CURRENT
    )
    plan = RubyBootstrapPlan(run, payload, *transport(run, payload))
    commands = []
    native_command = ruby_adapter.run_native

    def guarded(command, *args, **kwargs):
        commands.append(command)
        if "nbgv" in command:
            message = "Build must not evaluate NBGV"
            raise AssertionError(message)
        return native_command(command, *args, **kwargs)

    with pytest.MonkeyPatch.context() as patch:
        patch.setattr(ruby_adapter, "run_native", guarded)
        distribution = build_ruby_bootstrap(root, plan, clock=lambda: CURRENT)
    return (
        root,
        plan,
        artifact_for(plan, distribution),
        distribution,
        materialization,
        commands,
    )


@pytest.fixture
def plan(tmp_path):
    """Create model-only inputs for narrow plan and contract mutations."""
    return modeled_plan(tmp_path)


def test_native_provider_build_and_independent_consumers_preserve_one_original(
    native,
):
    """Preserve native bytes through both independent quality obligations."""
    root, plan, artifact, distribution, _, commands = native
    provider = plan.provider
    assert (
        provider.checkout.head
        == plan.run.target
        == git(root, "rev-parse", "HEAD")
    )
    assert provider.nbgv.to_document() == plan.run.request.document["nbgv"]
    assert provider.nbgv.to_document()["raw"]["PublicRelease"] is True
    assert (
        provider.source_input_manifest
        == plan.build_request().source_input_manifest
    )
    assert (
        dict(provider.source_input_manifest)[RUBY_ENVELOPE_PATH]
        == plan.run.request.document["envelope-digest"]
    )
    assert distribution.witness.purpose == "destination-bootstrap"
    assert (
        distribution.witness.nbgv.native_version == provider.nbgv.native_version
    )
    assert distribution.witness.target == plan.run.target
    assert commands == [("git", "show", "-s", "--format=%ct", plan.run.target)]
    evidence = tuple(
        run_ruby_bootstrap_quality(
            plan,
            artifact,
            distribution.content,
            definition,
            clock=lambda: CURRENT,
        )
        for definition in RUBY_QUALITY
    )
    assert [item.result for item in evidence] == ["passed", "passed"]
    assert parse_canonical_json(evidence[0].detail) == {
        "digest": distribution.digest
    }
    consumed = parse_canonical_json(evidence[1].detail)
    assert consumed["artifact-digest"] == distribution.digest
    assert consumed["version"] == provider.nbgv.native_version
    assert "Successfully installed" in consumed["install-output"]
    qualified = RubyBootstrapQualification(plan, evidence)
    assert qualified.result == "passed"
    assert qualified.to_document() == {
        "schema": "workflow-delivery/v3/ruby-bootstrap-qualification-v1",
        "plan-digest": plan.plan_digest,
        "purpose": "destination-bootstrap",
        "result": "passed",
        "evidence-digests": [item.evidence_digest for item in evidence],
        "publication-authority": False,
        "producer": "finalize-ruby-bootstrap-qualification",
    }
    assert all(item.artifact == artifact for item in evidence)
    assert distribution.digest == ruby_digest(distribution.content)


def test_plan_freezes_bootstrap_recipe_without_normal_model(plan):
    """All recipe fields derive from the exact admitted Provider artifact."""
    request = plan.build_request()
    assert request.source_input_manifest == plan.provider.source_input_manifest
    assert request.source_date_epoch == plan.provider.source_date_epoch
    assert request.native_profile == plan.provider.native_profile
    assert request.witness.control_digest == canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": plan.provider.binding.control,
        }
    )
    doc = plan.to_document()
    assert doc["schema"] == "workflow-delivery/v3/ruby-bootstrap-plan-v1"
    assert doc["producer"] == "plan-ruby-bootstrap"
    assert doc["obligations"] == list(RUBY_QUALITY)
    assert doc["witness"]["purpose"] == "destination-bootstrap"
    assert doc["provider-reference"] == plan.provider_reference.to_document()
    assert doc["provider-transport"] == plan.provider_transport.to_document()
    assert plan.plan_digest == canonical_sha256(doc)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/ruby-provider"),
        ("run-binding-digest", "sha256:" + "e" * 64),
        ("target-tree", "e" * 40),
        ("producer", "discover-ruby"),
        ("authority", True),
    ],
)
def test_plan_rejects_rebound_foreign_provider_envelope(plan, field, value):
    """Rehashing cannot repair a contradictory Provider envelope."""
    document = parse_canonical_json(plan.provider_content)
    document[field] = value
    payload = canonicalize(document)
    with pytest.raises(ValueError, match=r"Ruby|keys"):
        RubyBootstrapPlan(plan.run, payload, *transport(plan.run, payload))


@pytest.mark.parametrize("control", ["envelope", "configuration"])
@pytest.mark.parametrize("change", ["missing", "changed"])
def test_plan_requires_protected_controls_even_when_manifest_is_resealed(
    tmp_path, control, change
):
    """Reject resealed manifests that lack the admitted protected controls."""
    original = modeled_plan(tmp_path / "original")
    path = (
        RUBY_ENVELOPE_PATH
        if control == "envelope"
        else original.run.request.document["binding"]["configuration-path"]
    )
    source = dict(original.provider.source_input_manifest)
    if change == "missing":
        del source[path]
    else:
        source[path] = "sha256:" + "e" * 64
    provider = replace(
        original.provider, source_input_manifest=tuple(sorted(source.items()))
    )
    run = run_for_provider(
        tmp_path / "resealed",
        original.run.inputs.configuration,
        original.run.request.envelope,
        provider,
    )
    provider = replace(provider, binding=run.provider_binding())
    with pytest.raises(ValueError, match="protected controls"):
        plan_for_provider(run, provider)


@pytest.mark.parametrize("phase", ["provider", "build"])
def test_native_target_tree_is_checked_before_work(
    native, tmp_path, monkeypatch, phase
):
    """Reject a wrong tree before either native worker executes."""
    root, plan, _, _, materialization, _ = native
    run = run_for_provider(
        tmp_path,
        plan.run.inputs.configuration,
        plan.run.request.envelope,
        plan.provider,
    )
    wrong_plan = plan_for_provider(
        run, replace(plan.provider, binding=run.provider_binding())
    )
    calls = []
    monkeypatch.setattr(
        preparation,
        "provide_ruby_repository_facts",
        lambda *_args: calls.append("provider"),
    )
    monkeypatch.setattr(
        preparation, "build_ruby_package", lambda *_args: calls.append("build")
    )

    def invoke():
        if phase == "provider":
            provide_ruby_bootstrap(
                root, run, materialization, clock=lambda: CURRENT
            )
        else:
            build_ruby_bootstrap(root, wrong_plan, clock=lambda: CURRENT)

    with pytest.raises(ValueError, match="checkout tree"):
        invoke()
    assert calls == []


@pytest.mark.parametrize("phase", ["provider", "build", "quality"])
@pytest.mark.parametrize("when", ["entry", "completion"])
def test_every_native_phase_rechecks_lifetime_on_entry_and_completion(
    native, monkeypatch, phase, when
):
    """Native work cannot succeed after its authority expires."""
    root, plan, artifact, distribution, materialization, _ = native
    calls = []

    def provider(*_args):
        calls.append("provider")
        return plan.provider

    def build(*_args):
        calls.append("build")
        return distribution

    def consume(value):
        calls.append("quality")
        return consumer(value)

    monkeypatch.setattr(preparation, "provide_ruby_repository_facts", provider)
    monkeypatch.setattr(preparation, "build_ruby_package", build)
    times = iter([EXPIRED] if when == "entry" else [CURRENT, EXPIRED])

    def clock():
        return next(times)

    def invoke():
        if phase == "provider":
            provide_ruby_bootstrap(root, plan.run, materialization, clock=clock)
        elif phase == "build":
            build_ruby_bootstrap(root, plan, clock=clock)
        else:
            run_ruby_bootstrap_quality(
                plan,
                artifact,
                distribution.content,
                RUBY_QUALITY[1],
                clock=clock,
                consumer=consume,
            )

    with pytest.raises(ValueError, match="lifetime"):
        invoke()
    assert calls == ([] if when == "entry" else [phase])


def test_provider_rechecks_actual_tree_after_native_evaluation(
    native, monkeypatch
):
    """A changed second observation cannot export apparently current facts."""
    root, plan, _, _, materialization, _ = native
    observed = []

    def command(args, cwd):
        observed.append((args, cwd))
        return (
            plan.run.request.document["tree"]
            if len(observed) == 1
            else "e" * 40
        )

    monkeypatch.setattr(preparation, "_run_command", command)
    monkeypatch.setattr(
        preparation,
        "provide_ruby_repository_facts",
        lambda *_args: plan.provider,
    )
    with pytest.raises(ValueError, match="checkout tree"):
        provide_ruby_bootstrap(
            root, plan.run, materialization, clock=lambda: CURRENT
        )
    assert (
        observed
        == [(("git", "rev-parse", plan.run.target + "^{tree}"), root)] * 2
    )


@pytest.mark.parametrize(
    "failure", [OSError, TypeError, ValueError, RuntimeError, TimeoutExpired]
)
def test_consumer_failure_remains_failed_bound_evidence(native, failure):
    """Retain a sanitized consumer error bound to the exact plan."""
    _, plan, artifact, distribution, _, _ = native
    seen = []

    def failing(value):
        seen.append(value.digest)
        if failure is TimeoutExpired:
            message = "private-command"
            raise TimeoutExpired(message, 1)
        message = "private-error-detail"
        raise failure(message)

    result = run_ruby_bootstrap_quality(
        plan,
        artifact,
        distribution.content,
        RUBY_QUALITY[1],
        clock=lambda: CURRENT,
        consumer=failing,
    )
    assert seen == [distribution.digest]
    assert (result.result, result.plan_digest, result.artifact) == (
        "failed",
        plan.plan_digest,
        artifact,
    )
    assert parse_canonical_json(result.detail) == {
        "error-kind": failure.__name__
    }
    assert b"private" not in canonicalize(result.to_document())
    assert RubyBootstrapQualification(plan, (result,)).result == "failed"


def test_malformed_consumer_success_cannot_pass(native):
    """Reject a consumer success record with foreign facts."""
    _, plan, artifact, distribution, _, _ = native
    result = run_ruby_bootstrap_quality(
        plan,
        artifact,
        distribution.content,
        RUBY_QUALITY[1],
        clock=lambda: CURRENT,
        consumer=lambda _: {"success": True},
    )
    assert result.result == "failed"
    assert parse_canonical_json(result.detail) == {"error-kind": "ValueError"}


def test_contents_obligation_never_invokes_clean_consumer(native):
    """Run the content obligation without invoking the consumer."""
    _, plan, artifact, distribution, _, _ = native
    calls = []
    result = run_ruby_bootstrap_quality(
        plan,
        artifact,
        distribution.content,
        RUBY_QUALITY[0],
        clock=lambda: CURRENT,
        consumer=lambda _: calls.append("unexpected"),
    )
    assert calls == []
    assert result.result == "passed"
    assert parse_canonical_json(result.detail) == {
        "digest": distribution.digest
    }


@pytest.mark.parametrize(
    "mode",
    [
        "unknown-obligation",
        "modified-payload",
        "rehashed-malformed-payload",
        "prior-run",
        "prior-attempt",
        "normal-purpose",
        "archive",
        "wrong-url",
    ],
)
def test_quality_rejects_foreign_or_malformed_original_before_consumer(
    native, mode
):
    """Reject rehashed malformed archives and foreign provenance."""
    _, plan, artifact, distribution, _, _ = native
    payload = distribution.content
    definition = RUBY_QUALITY[1]
    if mode == "unknown-obligation":
        definition = "ruby/other"
    elif mode in {"modified-payload", "rehashed-malformed-payload"}:
        payload = b"not-a-gem"
        if mode == "rehashed-malformed-payload":
            ref, identity = transport(
                plan.run,
                payload,
                artifact_id=301,
                producer="build-ruby",
                path=artifact.filename,
            )
            artifact = replace(
                artifact,
                byte_size=len(payload),
                reference=ref,
                transport=identity,
            )
    elif mode == "normal-purpose":
        artifact = replace(
            artifact,
            witness=replace(artifact.witness, purpose="slice-validation"),
        )
    elif mode == "archive":
        digest = "sha256:" + "e" * 64
        artifact = replace(
            artifact,
            reference=replace(artifact.reference, artifact_digest=digest),
            transport=replace(artifact.transport, transport_digest=digest),
        )
    elif mode == "wrong-url":
        url = "https://example.invalid/artifacts/301"
        artifact = replace(
            artifact,
            reference=replace(artifact.reference, artifact_url=url),
            transport=replace(artifact.transport, artifact_url=url),
        )
    else:
        field = "workflow_run_id" if mode == "prior-run" else "run_attempt"
        artifact = replace(
            artifact,
            transport=replace(
                artifact.transport, **{field: 992 if mode == "prior-run" else 2}
            ),
        )
    calls = []
    with pytest.raises(ValueError, match=r"Ruby|ruby|gem"):
        run_ruby_bootstrap_quality(
            plan,
            artifact,
            payload,
            definition,
            clock=lambda: CURRENT,
            consumer=lambda _: calls.append("unexpected"),
        )
    assert calls == []


def passed_contents(plan, artifact):
    """Create a content fact for pure aggregate/importer contracts only."""
    return RubyBootstrapEvidence(
        plan.plan_digest,
        RUBY_QUALITY[0],
        artifact,
        "passed",
        canonicalize({"digest": artifact.reference.payload_digest}),
    )


@pytest.mark.parametrize("mode", ["none", "contents", "consumer"])
def test_missing_independent_obligations_remain_incomplete(native, mode):
    """Neither an empty set nor either sole obligation can qualify."""
    _, plan, artifact, distribution, _, _ = native
    if mode == "none":
        evidence = ()
    elif mode == "contents":
        evidence = (passed_contents(plan, artifact),)
    else:
        evidence = (
            RubyBootstrapEvidence(
                plan.plan_digest,
                RUBY_QUALITY[1],
                artifact,
                "passed",
                canonicalize(consumer(distribution)),
            ),
        )
    qualification = RubyBootstrapQualification(plan, evidence)
    assert qualification.result == "incomplete"
    assert qualification.to_document()["publication-authority"] is False


@pytest.mark.parametrize(
    "mode", ["duplicate", "foreign-plan", "separate-original", "wrong-type"]
)
def test_qualification_rejects_duplicate_foreign_or_separate_artifact_evidence(
    native, mode
):
    """Require unique passing facts over the same uploaded original."""
    _, plan, artifact, distribution, _, _ = native
    first = passed_contents(plan, artifact)
    if mode == "duplicate":
        evidence = (first, first)
    elif mode == "foreign-plan":
        evidence = (replace(first, plan_digest="sha256:" + "e" * 64),)
    elif mode == "wrong-type":
        evidence = (first.to_document(),)
    else:
        second_artifact = artifact_for(plan, distribution, artifact_id=302)
        second = RubyBootstrapEvidence(
            plan.plan_digest,
            RUBY_QUALITY[1],
            second_artifact,
            "passed",
            canonicalize(consumer(distribution)),
        )
        evidence = (first, second)
    with pytest.raises(ValueError, match=r"Evidence|originals"):
        RubyBootstrapQualification(plan, evidence)


def test_bootstrap_evidence_round_trip_is_distinct_from_ci_and_live(native):
    """Keep bootstrap evidence separate from CI and Live records."""
    _, plan, artifact, _, _, _ = native
    evidence = passed_contents(plan, artifact)
    restored = ruby_bootstrap_evidence_from_document(evidence.to_document())
    assert restored == evidence
    assert restored.evidence_digest == canonical_sha256(evidence.to_document())
    for importer in (
        ruby_ci_evidence_from_document,
        ruby_qualification_evidence_from_document,
    ):
        with pytest.raises(ValueError, match=r"Ruby|fields"):
            importer(evidence.to_document())


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/ruby-ci-evidence"),
        ("producer", "qualify-ruby"),
        ("plan-digest", "sha256:bad"),
        ("definition", "ruby/other"),
        ("result", "skipped"),
        ("detail", {"digest": "sha256:" + "e" * 64}),
        ("publication-authority", True),
    ],
)
def test_evidence_importer_rejects_open_or_relabeled_records(
    native, field, value
):
    """Validate closed schema, producer and original content facts."""
    _, plan, artifact, _, _, _ = native
    doc = passed_contents(plan, artifact).to_document()
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|fields"):
        ruby_bootstrap_evidence_from_document(doc)


@pytest.mark.parametrize("change", ["nbgv", "source"])
def test_provider_checks_fresh_facts_before_export(native, monkeypatch, change):
    """Fresh fact mismatches stop before exporting a Provider artifact."""
    root, plan, _, _, materialization, _ = native
    provider = plan.provider
    if change == "nbgv":
        raw = parse_canonical_json(provider.nbgv.raw_bytes)
        raw["AdditionalObservedFact"] = "changed"
        provider = replace(
            provider,
            nbgv=replace(provider.nbgv, raw_bytes=canonicalize(raw)),
        )
    else:
        inputs = list(provider.source_input_manifest)
        path, _ = inputs[0]
        inputs[0] = (path, "sha256:" + "e" * 64)
        provider = replace(provider, source_input_manifest=tuple(inputs))
    seen = []

    def observe(*args):
        seen.append(args)
        return provider

    monkeypatch.setattr(preparation, "provide_ruby_repository_facts", observe)
    with pytest.raises(ValueError, match="exact binding"):
        provide_ruby_bootstrap(
            root, plan.run, materialization, clock=lambda: CURRENT
        )
    assert seen == [(root, plan.run.provider_binding(), materialization)]


def test_plan_rejects_noncanonical_provider_even_with_matching_transport(plan):
    """Rehashing cannot widen the canonical Provider import contract."""
    payload = plan.provider_content + b"\n"
    with pytest.raises(ValueError, match="canonical"):
        RubyBootstrapPlan(plan.run, payload, *transport(plan.run, payload))
