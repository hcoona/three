"""Ruby version projection and exact immutable Provider boundaries."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3._ruby_native import RUBY_ROOT, run_ruby
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    RubyNbgvFacts,
    provide_ruby_repository_facts,
    ruby_nbgv_facts_from_document,
    ruby_provider_result_from_document,
)

from ..ruby_fixtures import EPOCH, RUN_ID, binding, commit, git, repository


def raw(semver="0.1.0-beta.7"):
    """Include a misleading alternate field to test the selected authority."""
    return {
        "SemVer2": semver,
        "SimpleVersion": "9.9.9",
        "GitCommitId": "a" * 40,
        "VersionHeight": 7,
        "PublicRelease": False,
    }


@pytest.mark.parametrize(
    ("version", "native"),
    [
        ("1.2.3", "1.2.3"),
        ("0.1.0-beta.7", "0.1.0.pre.beta.7"),
        ("0.1.0-beta.7.gabc1234", "0.1.0.pre.beta.7.gabc1234"),
    ],
)
def test_ruby_projection_preserves_native_prerelease_and_commit(
    version, native, tmp_path
):
    """Projection preserves native prerelease and commit."""
    projected = run_ruby(
        {"operation": "version", "raw": version},
        cwd=tmp_path,
        home=tmp_path / "home",
    )
    facts = RubyNbgvFacts(canonicalize(raw(version)), projected["native"])
    restored = ruby_nbgv_facts_from_document(facts.to_document())
    assert restored.native_version == native
    assert restored.to_document()["raw"]["SemVer2"] == version
    assert restored.to_document()["selected-field"] == "SemVer2"
    assert restored.target == "a" * 40


@pytest.mark.parametrize(
    "version",
    ["", " 1.2.3", "1.2.3 ", "1.2.3\n", "1.2.3+build", "1. 2.3", "garbage"],
)
def test_ruby_native_projection_rejects_invalid_or_rewritten_versions(
    version, tmp_path
):
    """Native projection rejects invalid or rewritten versions."""
    with pytest.raises(ValueError, match="invalid Ruby version input"):
        run_ruby(
            {"operation": "version", "raw": version},
            cwd=tmp_path,
            home=tmp_path / "home",
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("SemVer2", "01.2.3"),
        ("SemVer2", "1.2.3+build"),
        ("GitCommitId", "A" * 40),
        ("VersionHeight", True),
        ("VersionHeight", -1),
        ("PublicRelease", 1),
    ],
)
def test_ruby_frozen_facts_reject_malformed_primitives(field, value):
    """Frozen facts reject malformed primitives."""
    document = raw()
    document[field] = value
    with pytest.raises(ValueError, match="Ruby"):
        RubyNbgvFacts(canonicalize(document), "0.1.0.pre.beta.7")


@pytest.mark.parametrize(
    "native", ["9.9.9", "0.1.0-beta.7", "0.1.0.pre.beta.8"]
)
def test_ruby_frozen_projection_cannot_be_replaced(native):
    """Frozen projection cannot be replaced."""
    with pytest.raises(ValueError, match="projection"):
        RubyNbgvFacts(canonicalize(raw()), native)


@pytest.fixture(scope="module")
def native_provider(tmp_path_factory):
    """Native provider."""
    source, origin, target = repository(
        tmp_path_factory.mktemp("ruby-provider")
    )
    result = provide_ruby_repository_facts(
        source,
        binding(target),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    return source, origin, result


def test_ruby_provider_freezes_exact_full_history_and_native_specification(
    native_provider,
):
    """Provider freezes exact full history and native specification."""
    source, _, provider = native_provider
    assert provider.checkout.target == git(source, "rev-parse", "HEAD")
    assert provider.checkout.ancestry_complete
    assert provider.checkout.tags_complete
    assert not provider.checkout.shallow
    assert not provider.checkout.credentials_persisted
    assert provider.source_date_epoch == EPOCH
    assert provider.nbgv.to_document()["raw"]["PublicRelease"] is False
    assert ".g" in provider.nbgv.native_version
    assert parse_canonical_json(provider.specification)["platform"] == "ruby"
    assert parse_canonical_json(provider.native_profile)["rubygems"] == "4.0.20"


def test_ruby_provider_public_main_projection_uses_same_target(native_provider):
    """Provider public main projection uses same target."""
    source, _, prior = native_provider
    result = provide_ruby_repository_facts(
        source,
        binding(prior.binding.target, "live-release"),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    assert result.nbgv.target == prior.nbgv.target
    assert result.nbgv.to_document()["raw"]["PublicRelease"] is True
    assert result.nbgv.native_version == "0.1.0.pre.beta.1"
    assert "run-attempt" not in result.to_document()["binding"]
    assert (
        ruby_provider_result_from_document(
            result.to_document()
        ).binding.run_attempt
        is None
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("target", "c" * 40),
        ("head", "c" * 40),
        ("shallow", True),
        ("ancestry_complete", False),
        ("tags_complete", False),
        ("credentials_persisted", True),
    ],
)
def test_ruby_provider_rejects_checkout_substitution(
    native_provider, field, value
):
    """Provider rejects checkout substitution."""
    provider = native_provider[2]
    with pytest.raises(ValueError, match=r"Ruby|checkout"):
        replace(provider, checkout=replace(provider.checkout, **{field: value}))


@pytest.mark.parametrize(
    "field", ["native_profile", "specification", "source_input_manifest"]
)
def test_ruby_provider_rejects_native_or_source_contract_drift(
    native_provider, field
):
    """Provider rejects native or source contract drift."""
    provider = native_provider[2]
    if field == "source_input_manifest":
        value = tuple(
            pair
            for pair in provider.source_input_manifest
            if not pair[0].endswith(".gemspec")
        )
    else:
        value = parse_canonical_json(getattr(provider, field))
        value["rubygems" if field == "native_profile" else "platform"] = "other"
        value = canonicalize(value)
    with pytest.raises(ValueError, match="Ruby"):
        replace(provider, **{field: value})


def test_ruby_provider_rejects_gemspec_source_mutation(tmp_path):
    """Provider rejects gemspec source mutation."""
    source, origin, _ = repository(tmp_path)
    spec = origin / RUBY_ROOT / "hcoona-release-smoke-ruby.gemspec"
    spec.write_text('File.write("README.md", "mutated")\n' + spec.read_text())
    target = commit(origin, epoch=EPOCH + 1)
    git(source, "fetch", "origin")
    git(source, "checkout", "--detach", target)
    with pytest.raises(ValueError, match="evaluation changed source"):
        provide_ruby_repository_facts(
            source,
            binding(target),
            CheckoutMaterialization(0, credentials_persisted=False),
        )
    assert (source / RUBY_ROOT / "README.md").read_bytes() != b"mutated"


def test_ruby_provider_serialization_retains_native_and_request_binding(
    native_provider,
):
    """Round trip full discovery while retaining exact selected identities."""
    provider = native_provider[2]
    result = ruby_provider_result_from_document(provider.to_document())
    assert result.binding.request_id == "ruby-foundation"
    assert result.binding.workflow_run_id == RUN_ID
    assert result.source_date_epoch == EPOCH
    assert result.nbgv.native_version.endswith(provider.binding.target[:7])
    assert (
        parse_canonical_json(result.specification)["name"]
        == "hcoona-release-smoke-ruby"
    )
    assert result.result_digest == provider.result_digest


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "foreign"),
        ("provider", "node/npm-v1"),
        ("execution-class", "publisher/credentialed-v1"),
        ("build-definition", "ruby/other"),
        ("transport-id", 91),
        ("inputs", [["global.json"]]),
        ("source-date-epoch", True),
    ],
)
def test_ruby_provider_transport_rejects_open_or_foreign_variants(
    native_provider, field, value
):
    """Serialized facts cannot add authority or coerce native primitives."""
    document = native_provider[2].to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Ruby"):
        ruby_provider_result_from_document(document)


@pytest.mark.parametrize(
    ("section", "field", "value", "error"),
    [
        ("binding", "workflow-run-id", True, ValueError),
        ("binding", "transport-id", 91, ValueError),
        ("checkout", "shallow", "false", TypeError),
        ("checkout", "credentials-persisted", 0, TypeError),
        ("nbgv", "selected-field", "SimpleVersion", ValueError),
    ],
)
def test_ruby_provider_transport_rejects_nested_binding_substitution(
    native_provider, section, field, value, error
):
    """Nested fields preserve strict request, checkout and version meaning."""
    document = native_provider[2].to_document()
    document[section][field] = value
    with pytest.raises(error):
        ruby_provider_result_from_document(document)
