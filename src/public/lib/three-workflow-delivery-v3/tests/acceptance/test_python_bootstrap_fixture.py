"""Bootstrap original pair provenance and actual public-source builds."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    ACCOUNT,
    SENTINEL,
    WORKFLOW,
    BootstrapRequest,
    phase_binding,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_fixture import (
    BootstrapFixtures,
    bootstrap_binding,
    bootstrap_witness,
    build_bootstrap_fixture,
    fixtures_from_files,
    validate_prepared,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    archive_members,
    consumer_evidence,
)
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import _distribution
from ..repository import test_python_provider as provider_tests
from .test_python_native_fixture import _synthetic_provider
from .test_python_native_suite import _consumer

native_python_provider_repository = (
    provider_tests.native_python_provider_repository
)
RUN = 911
TOOLING = "c" * 40


def bootstrap_request(fixtures):
    """Construct prospective authority for one modeled or real original pair."""
    wheel = fixtures.distributions["wheel"]
    return BootstrapRequest(
        canonicalize(
            {
                "schema": "workflow-delivery/v3/python-bootstrap-request",
                "generation": "1" * 32,
                "registry": "testpypi",
                "account": ACCOUNT,
                "project": "hcoona-release-smoke-python",
                "workflow": WORKFLOW,
                "profile-digest": PythonRegistry("testpypi").profile_digest,
                "source": {
                    "commit": wheel.witness.target,
                    "version": wheel.witness.nbgv.pep440_version,
                },
                "fixture-digests": {
                    key: item.digest
                    for key, item in fixtures.distributions.items()
                },
                "environment": {
                    "id": 17,
                    "name": "workflow-delivery-v3-python-testpypi",
                    "sentinel": SENTINEL,
                },
                "authorization": {
                    "url": "https://github.com/hcoona/three/issues/843",
                    "digest": "sha256:" + "a" * 64,
                },
                "configuration": {
                    "url": "https://github.com/hcoona/three/issues/843",
                    "digest": "sha256:" + "b" * 64,
                },
            }
        )
    )


@pytest.fixture
def bootstrap_fixtures():
    """Retain valid archives with explicitly synthetic.

    Retain valid archives with explicitly synthetic external producer
    facts.
    """
    provider = _synthetic_provider("a")
    provider = replace(
        provider, binding=bootstrap_binding(provider.binding.target, RUN)
    )
    witness = bootstrap_witness(provider)
    distributions = {
        variant: _distribution(variant, witness)
        for variant in ("wheel", "sdist")
    }
    staged = next(
        content
        for name, content in archive_members(
            distributions["sdist"].content, "sdist"
        ).items()
        if name.endswith("/pyproject.toml")
    )
    command = {
        "argv": ["synthetic-test-only"],
        "exit-code": 0,
        "stdout": "synthetic-version",
        "stderr": "",
    }
    evidence = {
        "provider.json": canonicalize(provider.to_document()),
        "build.json": canonicalize(
            {
                "source-manifest": [
                    list(pair) for pair in provider.source_input_manifest
                ],
                "staged-manifest-digest": python_digest(staged),
                "versions": command,
                "commands": [command],
            }
        ),
    }
    consume = _consumer([])
    for variant, item in distributions.items():
        evidence[f"consumer/{variant}.json"] = consumer_evidence(consume(item))
    return BootstrapFixtures(distributions, evidence)


def prepared_files(fixtures):
    """Form the actual retained prepare envelope without mocking validators."""
    request = bootstrap_request(fixtures)
    return {
        **fixtures.files(),
        "request.json": request.content,
        "binding.json": canonicalize(
            phase_binding(request, RUN, TOOLING, "prepare")
        ),
    }


@pytest.mark.parametrize(
    ("name", "path", "value"),
    [
        ("provider.json", ("binding", "purpose"), "destination-acceptance"),
        ("provider.json", ("binding", "workflow-run-id"), 912),
        ("provider.json", ("checkout", "head"), "f" * 40),
        ("build.json", ("staged-manifest-digest",), "sha256:" + "f" * 64),
        ("build.json", ("source-manifest",), []),
        ("build.json", ("versions", "stdout"), ""),
        ("build.json", ("commands", 0, "exit-code"), 1),
        ("consumer/wheel.json", ("commands",), []),
        ("consumer/sdist.json", ("installed", "version"), "9.0.0"),
    ],
)
def test_bootstrap_preparation_rejects_foreign_or_failed_provenance(
    bootstrap_fixtures, name, path, value
):
    """Successful consumer labels cannot repair foreign.

    Successful consumer labels cannot repair foreign producer/source
    facts.
    """
    files = prepared_files(bootstrap_fixtures)
    document = parse_canonical_json(files[name])
    parent = document
    for part in path[:-1]:
        parent = parent[part]
    parent[path[-1]] = value
    files[name] = canonicalize(document)
    with pytest.raises((ValueError, TypeError)):
        validate_prepared(
            files, bootstrap_request(bootstrap_fixtures), RUN, TOOLING
        )


def test_bootstrap_fixture_reinspects_originals_and_inventory(
    bootstrap_fixtures,
):
    """Retained bytes and typed evidence round-trip and.

    Retained bytes and typed evidence round-trip and reject missing
    members.
    """
    request = bootstrap_request(bootstrap_fixtures)
    files = prepared_files(bootstrap_fixtures)
    actual = validate_prepared(files, request, RUN, TOOLING)
    assert actual == bootstrap_fixtures
    assert {item.witness.purpose for item in actual.distributions.values()} == {
        "destination-bootstrap"
    }
    files.pop("consumer/sdist.json")
    with pytest.raises(ValueError, match="preparation"):
        validate_prepared(files, request, RUN, TOOLING)
    with pytest.raises(ValueError, match="incomplete"):
        fixtures_from_files(files)


@pytest.fixture(scope="module")
def actual_bootstrap_fixtures(native_python_provider_repository):
    """Build and qualify an original pair using the actual.

    Build and qualify an original pair using the actual local Git/UV
    tools.
    """
    source, target = native_python_provider_repository
    return source, target, build_bootstrap_fixture(source, target, RUN)


def test_real_bootstrap_pair_preserves_public_projection_and_originals(
    actual_bootstrap_fixtures,
):
    """Real wheel/sdist retain public prerelease and distinct.

    Real wheel/sdist retain public prerelease and distinct bootstrap
    purpose.
    """
    _, target, fixtures = actual_bootstrap_fixtures
    fixtures.match(bootstrap_request(fixtures), RUN)
    assert set(fixtures.distributions) == {"wheel", "sdist"}
    for variant, item in fixtures.distributions.items():
        assert item.witness.target == target
        assert item.witness.purpose == "destination-bootstrap"
        assert "+" not in item.witness.nbgv.pep440_version
        proof = parse_canonical_json(
            fixtures.evidence[f"consumer/{variant}.json"]
        )
        assert proof["original-digest"] == item.digest
        assert proof["installed"]["witness"] == item.witness.to_document()
        assert all(command["exit-code"] == 0 for command in proof["commands"])


def test_real_bootstrap_bytes_are_execution_independent(
    actual_bootstrap_fixtures,
):
    """Another actual build run changes only execution evidence, never bytes."""
    source, target, first = actual_bootstrap_fixtures
    second = build_bootstrap_fixture(source, target, RUN + 1)
    assert {key: item.content for key, item in first.distributions.items()} == {
        key: item.content for key, item in second.distributions.items()
    }
    assert first.evidence["provider.json"] != second.evidence["provider.json"]
