"""Ruby Model compiles exact Git inputs and immutable current-request facts."""

from dataclasses import replace

import pytest
import yaml
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_DESCRIPTOR,
    RUBY_POLICY,
    RUBY_QUALITY_PATH,
    RubyRepositoryModelSnapshot,
    admit_ruby_provider_facts,
    compile_ruby_repository_model,
    ruby_provider_manifest,
    ruby_repository_model_from_document,
)

from ..ruby_integration_fixtures import (
    EPOCH,
    RUN_ID,
    admitted,
    committed_source,
    contents,
    context,
    git,
    model,
)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("target", "b" * 40),
        ("control", "different-reviewed-control"),
        ("request_id", "other-request"),
        ("workflow_run_id", RUN_ID + 1),
        ("run_attempt", 2),
        ("purpose", "ci-pr-slice-shadow"),
    ],
)
def test_ruby_manifest_freezes_current_context(field, value):
    """Freeze source, control, purpose and workflow identity."""
    original = ruby_provider_manifest(context())
    changed = ruby_provider_manifest(replace(context(), **{field: value}))
    assert (
        changed.requests[0].request_digest
        != original.requests[0].request_digest
    )
    assert changed.manifest_digest != original.manifest_digest
    assert changed.requests[0].producer == "discover-ruby"
    assert changed.requests[0].execution_mode == "target-evaluating"


@pytest.mark.parametrize(
    "unit",
    ["hcoona-release-smoke-npm", "hcoona-release-smoke-python", "unknown-unit"],
)
def test_ruby_manifest_rejects_foreign_simulation_unit(unit):
    """A valid other-language unit cannot borrow Ruby discovery."""
    ctx = replace(
        context(purpose="release-simulation"),
        release_unit=unit,
    )
    with pytest.raises(
        ValueError, match=r"own selected unit|registered Release Unit"
    ):
        ruby_provider_manifest(ctx)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("artifact_id", 103),
        ("transport_digest", "sha256:" + "3" * 64),
        ("artifact_url", "https://example.invalid/artifacts/foreign"),
        ("producer", "untrusted-target"),
        ("workflow_run_id", RUN_ID + 1),
        ("run_attempt", 2),
    ],
)
def test_ruby_provider_admission_rejects_foreign_result_transport(field, value):
    """Identical payloads do not grant a different result transport identity."""
    facts = admitted()
    with pytest.raises(ValueError, match=r"transport|current-request"):
        replace(
            facts,
            result_transport=replace(facts.result_transport, **{field: value}),
        )


@pytest.mark.parametrize(
    "change",
    ["payload", "request-reference", "result-reference", "binding", "manifest"],
)
def test_ruby_provider_admission_requires_canonical_current_request(change):
    """Bind current payload to the frozen request and producer."""
    facts = admitted()
    if change == "payload":
        with pytest.raises(ValueError, match="payload digest"):
            admit_ruby_provider_facts(
                canonicalize({"foreign": True}),
                manifest=facts.manifest,
                request_reference=facts.request_reference,
                result_reference=facts.result_reference,
                result_transport=facts.result_transport,
            )
        return
    if change in {"request-reference", "result-reference"}:
        field = change.replace("-", "_")
        updates = {
            field: replace(
                getattr(facts, field), payload_digest="sha256:" + "9" * 64
            )
        }
    elif change == "binding":
        result = replace(
            facts.result,
            binding=replace(facts.result.binding, request_id="other"),
        )
        updates = {
            "result": result,
            "result_reference": replace(
                facts.result_reference, payload_digest=result.result_digest
            ),
        }
    else:
        updates = {
            "manifest": replace(
                facts.manifest,
                requests=(
                    replace(facts.manifest.requests[0], producer="foreign"),
                ),
            )
        }
    with pytest.raises(ValueError, match="Ruby"):
        replace(facts, **updates)


def test_ruby_compiler_requires_admitted_transport(tmp_path):
    """A raw Provider record cannot skip transport admission."""
    with pytest.raises(TypeError, match="admitted Provider"):
        compile_ruby_repository_model(tmp_path, admitted().result)


def test_ruby_compiler_uses_exact_committed_source_and_epoch(tmp_path):
    """Exclude dirty authoring and ambient time from the Model."""
    source = contents()
    root, target = committed_source(tmp_path / "source", source)
    facts = admitted(context(target), source)
    (root / RUBY_POLICY).write_text("untrusted: dirty\n")
    (root / RUBY_DESCRIPTOR).write_text("untrusted: dirty\n")
    snapshot = compile_ruby_repository_model(root, facts)
    request = snapshot.build_request()
    assert snapshot.context.target == target
    assert snapshot.to_document()["ready"] is True
    assert request.source_input_manifest == tuple(
        sorted((p, d) for p, d in facts.result.source_input_manifest)
    )
    assert request.source_date_epoch == EPOCH
    assert int(git(root, "show", "-s", "--format=%at", target)) == EPOCH - 100
    assert request.native_profile == facts.result.native_profile
    assert snapshot.to_document()["release-unit"]["builds"][0]["outputs"] == [
        {"id": "gem", "role": "primary-package", "kind": "ruby-gem"}
    ]


@pytest.mark.parametrize(
    "change", ["omitted", "injected", "digest", "epoch", "author-epoch"]
)
def test_ruby_compiler_rejects_inexact_target_inputs(tmp_path, change):
    """Well-shaped technical discovery cannot substitute target bytes/time."""
    source = contents()
    optional = "src/public/lib/hcoona-release-smoke-ruby/additional.txt"
    source[optional] = b"tracked additional source"
    root, target = committed_source(tmp_path / "source", source)
    supplied = dict(source)
    epoch = EPOCH
    if change == "omitted":
        supplied.pop(optional)
    elif change == "injected":
        supplied[optional + ".injected"] = b"not in Git"
    elif change == "digest":
        supplied[optional] = b"substituted bytes"
    elif change == "author-epoch":
        epoch -= 100
    else:
        epoch += 1
    facts = admitted(context(target), supplied, epoch=epoch)
    with pytest.raises(
        ValueError, match=r"target inputs|exact target|immutable target"
    ):
        compile_ruby_repository_model(root, facts)


@pytest.mark.parametrize(
    "path", [RUBY_DESCRIPTOR, RUBY_POLICY, RUBY_QUALITY_PATH]
)
def test_ruby_compiler_rejects_correctly_hashed_foreign_authoring(
    tmp_path, path
):
    """Exact source hashes do not authorize extra outputs or borrowed policy."""
    source = contents()
    doc = yaml.safe_load(source[path])
    if path == RUBY_DESCRIPTOR:
        doc["builds"][0]["outputs"].append(
            {"id": "extra", "role": "source-package", "kind": "ruby-gem"}
        )
    elif path == RUBY_POLICY:
        doc["channels"]["official"]["governance"] = doc["channels"]["buddy"][
            "governance"
        ]
    else:
        doc["ecosystems"]["ruby"]["preset"] = "ruby/foreign-v1"
    source[path] = yaml.safe_dump(doc).encode()
    root, target = committed_source(tmp_path / "source", source)
    with pytest.raises(ValueError, match="one-gem contract"):
        compile_ruby_repository_model(root, admitted(context(target), source))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/python-repository-model-snapshot"),
        ("extra", True),
        ("ready", False),
        ("ready", 1),
        ("quality", []),
        ("reverse-index", {}),
        ("release-policy", {}),
        ("release-unit", {}),
    ],
)
def test_ruby_model_import_rejects_foreign_or_open_contract(field, value):
    """Closed Model data cannot alias another schema, shape or primitive."""
    document = model().to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Ruby"):
        ruby_repository_model_from_document(document)


@pytest.mark.parametrize(
    "purpose",
    [
        "slice-validation",
        "ci-pr-slice-shadow",
        "release-simulation",
        "live-release",
    ],
)
def test_ruby_model_request_preserves_purpose_source_profile_and_destinations(
    purpose,
):
    """Freeze purpose without run or destination data in gem bytes."""
    snapshot = model(purpose)
    parsed = ruby_repository_model_from_document(snapshot.to_document())
    request = parsed.build_request()
    assert request.witness.purpose == purpose
    assert request.witness.target == snapshot.context.target
    assert request.witness.nbgv.native_version == "0.1.0.pre.beta.7"
    assert request.witness.catalog_digest == snapshot.context.catalog_digest
    assert request.witness.control_digest == canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": snapshot.context.control,
        }
    )
    assert (
        request.source_input_manifest == snapshot.provider.source_input_manifest
    )
    assert request.source_date_epoch == EPOCH
    assert request.native_profile == snapshot.provider.native_profile
    assert {
        "workflow-run-id",
        "run-attempt",
        "destination",
        "channel",
    }.isdisjoint(request.witness.to_document())
    assert parsed.to_document()["reverse-index"] == {
        "hcoona-release-smoke-ruby": ["ruby-gem"]
    }
    channels = parsed.to_document()["release-policy"]["channels"]
    assert channels["buddy"]["destination"] == "ruby/github-packages-v1"
    assert channels["official"]["destination"] == "ruby/rubygems-v1"
    assert channels["buddy"]["governance"] != channels["official"]["governance"]


def test_ruby_provider_admission_ignores_nonauthoritative_archive_label():
    """Changing the display label cannot change immutable Provider authority."""
    facts = admitted()
    relabeled = replace(
        facts,
        result_transport=replace(
            facts.result_transport, artifact_name="different-valid-label"
        ),
    )
    assert relabeled.result == facts.result
    assert relabeled.result_reference == facts.result_reference
    assert (
        relabeled.result_transport.artifact_id
        == facts.result_transport.artifact_id
    )
    assert relabeled.manifest.manifest_digest == facts.manifest.manifest_digest


@pytest.mark.parametrize("channel", ["buddy", "official"])
def test_ruby_model_admits_both_closed_simulation_channels(channel):
    """Retain simulation channel and unit without granting live authority."""
    ctx = replace(context(purpose="release-simulation"), channel=channel)
    facts = admitted(ctx)
    snapshot = RubyRepositoryModelSnapshot(
        ctx, facts.result, facts.request_reference, facts.result_reference
    )
    assert snapshot.context.channel == channel
    assert snapshot.context.release_unit == "hcoona-release-smoke-ruby"
    assert snapshot.build_request().witness.purpose == "release-simulation"
    assert snapshot.context.run_attempt == 1


@pytest.mark.parametrize(
    "path", [RUBY_DESCRIPTOR, RUBY_QUALITY_PATH, RUBY_POLICY]
)
def test_ruby_model_requires_all_authoring_in_provider_source(path):
    """Admitted technical facts cannot omit required Model authoring inputs."""
    source = contents()
    source.pop(path)
    facts = admitted(source=source)
    with pytest.raises(ValueError, match="current-source binding"):
        RubyRepositoryModelSnapshot(
            facts.manifest.context,
            facts.result,
            facts.request_reference,
            facts.result_reference,
        )
