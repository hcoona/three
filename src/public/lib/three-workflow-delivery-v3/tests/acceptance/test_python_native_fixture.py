"""Historical archive identity and provenance; shared bootstrap witnesses."""

import zipfile
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    FIXTURE_KEYS,
    NativeRequest,
    pack_bundle,
    unpack_bundle,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    NativeFixtures,
    fixture_witness,
    fixtures_from_files,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PythonNbgvFacts,
)

from ..repository.test_python_provider import _provider
from .python_native_history import historical_catalog_digest, historical_files
from .test_python_native_contract import request_document

_BASE_RUN = 911


def _synthetic_provider(label):
    height = 7 if label == "a" else 8
    target = label * 40
    provider = _provider()
    return replace(
        provider,
        binding=replace(
            provider.binding,
            request_id=f"python-native-fixture:{target}",
            purpose="destination-acceptance",
            workflow_run_id=_BASE_RUN,
            target=target,
            producer="prepare-python-native",
            control=f"workflow-delivery-v3:{target}",
            request_digest=canonical_sha256(
                {"target": target, "purpose": "destination-acceptance"}
            ),
        ),
        checkout=replace(provider.checkout, target=target, head=target),
        nbgv=PythonNbgvFacts(
            canonicalize(
                {
                    "SimpleVersion": "0.1.0",
                    "SemVer2": f"0.1.0-beta.{height}",
                    "GitCommitId": target,
                    "VersionHeight": height,
                    "PublicRelease": True,
                }
            ),
            f"0.1.0b{height}",
        ),
    )


@pytest.fixture
def modeled_fixtures(monkeypatch):
    """Decode static synthetic originals; never generate a native suite."""
    fixtures = fixtures_from_files(historical_files("prepared"))
    original_catalog = historical_catalog_digest()
    # Historical readers require their original revision's catalog dependency.
    # Keep all archived bytes unchanged; this is not current runtime admission.
    monkeypatch.setattr(
        "three_workflow_delivery_v3.repository.node_provider.catalog_digest",
        lambda: original_catalog,
    )
    return fixtures


def fixture_request(fixtures):
    """Bind supplied fixture bytes to a synthetic protected request."""
    document = request_document()
    document["fixture-digests"] = {
        key: item.digest for key, item in fixtures.distributions.items()
    }
    document["targets"] = {
        label: {
            "commit": fixtures.distributions[
                f"{label}/original/wheel"
            ].witness.target,
            "version": fixtures.distributions[
                f"{label}/original/wheel"
            ].witness.nbgv.pep440_version,
        }
        for label in ("a", "b")
    }
    return NativeRequest(canonicalize(document))


def test_python_native_fixture_witness_excludes_execution_identity():
    """Execution identity cannot enter native package bytes."""
    provider = _provider()
    original = fixture_witness(provider)
    other = replace(
        provider,
        binding=replace(
            provider.binding,
            request_id="different-generation",
            workflow_run_id=912,
            control="workflow-delivery-v3:" + "d" * 40,
            request_digest="sha256:" + "c" * 64,
        ),
    )
    assert fixture_witness(other).canonical_bytes == original.canonical_bytes
    assert original.purpose == "destination-acceptance"
    assert set(original.to_document()) == {
        "schema",
        "target",
        "release-unit",
        "nbgv",
        "build-definition",
        "catalog-digest",
        "control-digest",
        "purpose",
    }
    assert b"different-generation" not in original.canonical_bytes


def test_python_native_fixture_bundle_reinspects_original_bytes(
    modeled_fixtures,
):
    """Fixture metadata cannot replace the retained originals."""
    request = fixture_request(modeled_fixtures)
    retained = unpack_bundle(pack_bundle(modeled_fixtures.files()))
    restored = fixtures_from_files(retained)
    restored.match(request)
    assert set(restored.distributions) == set(FIXTURE_KEYS)
    assert {
        key: item.content for key, item in restored.distributions.items()
    } == {
        key: item.content
        for key, item in modeled_fixtures.distributions.items()
    }
    retained["fixtures/a/original/wheel.bin"] = b"unusable substituted archive"
    with pytest.raises((ValueError, zipfile.BadZipFile)):
        fixtures_from_files(retained)


@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "extra",
        "identical-comparison",
        "wrong-variant",
        "same-target",
    ],
)
def test_python_native_fixtures_reject_incomplete_or_foreign_pair(
    modeled_fixtures, change
):
    """The eight-key set has valid distinct representations at two sources."""
    distributions = dict(modeled_fixtures.distributions)
    if change == "missing":
        distributions.pop("b/comparison/sdist")
    elif change == "extra":
        distributions["c/original/wheel"] = distributions["a/original/wheel"]
    elif change == "identical-comparison":
        distributions["a/comparison/wheel"] = distributions["a/original/wheel"]
    elif change == "wrong-variant":
        distributions["a/original/wheel"] = distributions["a/original/sdist"]
    else:
        for candidate in ("original", "comparison"):
            for variant in ("wheel", "sdist"):
                distributions[f"b/{candidate}/{variant}"] = distributions[
                    f"a/{candidate}/{variant}"
                ]
    with pytest.raises(ValueError, match=r"fixture|comparison"):
        NativeFixtures(distributions, modeled_fixtures.evidence)


@pytest.mark.parametrize(
    "change",
    [
        "digest",
        "source",
        "consumer-digest",
        "consumer-witness",
        "consumer-commands",
        "missing-consumer",
    ],
)
def test_python_native_fixtures_match_rejects_unbound_preparation(
    modeled_fixtures, change
):
    """Source, eight digests and qualification must agree before capability."""
    document = fixture_request(modeled_fixtures).document
    evidence = dict(modeled_fixtures.evidence)
    key = "consumer/b/comparison/sdist.json"
    if change == "digest":
        document["fixture-digests"]["b/comparison/sdist"] = "sha256:" + "f" * 64
    elif change == "source":
        document["targets"]["b"]["commit"] = "c" * 40
    elif change == "missing-consumer":
        evidence.pop(key)
    else:
        proof = parse_canonical_json(evidence[key])
        if change == "consumer-digest":
            proof["original-digest"] = "sha256:" + "f" * 64
        elif change == "consumer-witness":
            proof["installed"]["witness"]["target"] = "f" * 40
        else:
            proof["commands"] = []
        evidence[key] = canonicalize(proof)
    fixtures = NativeFixtures(modeled_fixtures.distributions, evidence)
    with pytest.raises((ValueError, KeyError)):
        fixtures.match(NativeRequest(canonicalize(document)))


@pytest.mark.parametrize(
    ("path", "value"),
    [
        (("unexpected",), True),
        (("installed", "version"), "99.0.0"),
        (("installed", "project-id"), "foreign-project"),
        (("installed", "module"), 17),
        (("installed", "module"), ""),
        (("installed", "unexpected"), True),
        (("installed",), {}),
        (("commands",), {}),
        (("commands", 0), "success"),
        (("commands", 0, "exit-code"), 1),
        (("commands", 0, "exit-code"), False),
        (("commands", 0, "exit-code"), "0"),
        (("commands", 0, "argv"), "synthetic-consumer"),
        (("commands", 0, "argv"), []),
        (("commands", 0, "argv"), [17]),
        (("commands", 0, "stdout"), None),
        (("commands", 0, "stderr"), []),
        (("commands", 0, "unexpected"), True),
        (("commands", 0), {"exit-code": 0}),
    ],
)
def test_python_native_fixture_rejects_invalid_consumer_proof(
    modeled_fixtures, path, value
):
    """A matching witness cannot hide failed commands or malformed proof."""
    request = fixture_request(modeled_fixtures)
    evidence = dict(modeled_fixtures.evidence)
    key = "consumer/a/original/wheel.json"
    proof = parse_canonical_json(evidence[key])
    parent = proof
    for part in path[:-1]:
        parent = parent[part]
    parent[path[-1]] = value
    evidence[key] = canonicalize(proof)
    with pytest.raises((ValueError, TypeError, KeyError)):
        NativeFixtures(modeled_fixtures.distributions, evidence).match(request)


@pytest.mark.parametrize("kind", ["provider", "build"])
@pytest.mark.parametrize("change", ["missing", "malformed", "foreign"])
def test_python_native_fixtures_require_original_preparation_provenance(
    modeled_fixtures, kind, change
):
    """Clean-consumer proofs cannot substitute for source/build provenance."""
    request = fixture_request(modeled_fixtures)
    evidence = dict(modeled_fixtures.evidence)
    key = f"{kind}/a.json"
    if change == "missing":
        evidence.pop(key)
    elif change == "malformed":
        evidence[key] = b"not a canonical evidence object"
    else:
        evidence[key] = evidence[f"{kind}/b.json"]
    with pytest.raises((ValueError, TypeError, KeyError)):
        NativeFixtures(modeled_fixtures.distributions, evidence).match(
            request, run_id=_BASE_RUN
        )


@pytest.mark.parametrize(
    ("kind", "path", "value"),
    [
        ("provider", ("binding", "workflow-run-id"), 912),
        ("provider", ("binding", "purpose"), "release-simulation"),
        ("provider", ("binding", "producer"), "foreign-producer"),
        ("provider", ("binding", "control"), "foreign-control"),
        ("provider", ("binding", "request-id"), "foreign-request"),
        ("provider", ("binding", "request-digest"), "sha256:" + "f" * 64),
        ("provider", ("binding", "run-attempt"), 2),
        ("provider", ("checkout", "head"), "f" * 40),
        ("build", ("source-manifest", 0, 1), "sha256:" + "f" * 64),
        ("build", ("staged-manifest-digest",), "sha256:" + "f" * 64),
        ("build", ("versions", "exit-code"), 1),
        ("build", ("versions", "stdout"), ""),
        ("build", ("versions", "stdout"), 17),
        ("build", ("commands",), []),
        ("build", ("commands", 0, "exit-code"), 1),
        ("build", ("commands", 0, "exit-code"), False),
        ("build", ("unexpected",), True),
    ],
)
def test_python_native_fixture_rejects_inconsistent_preparation_provenance(
    modeled_fixtures, kind, path, value
):
    """Provider identity and successful build proof bind original bytes."""
    request = fixture_request(modeled_fixtures)
    evidence = dict(modeled_fixtures.evidence)
    key = f"{kind}/a.json"
    document = parse_canonical_json(evidence[key])
    parent = document
    for part in path[:-1]:
        parent = parent[part]
    parent[path[-1]] = value
    evidence[key] = canonicalize(document)
    with pytest.raises((ValueError, TypeError, KeyError)):
        NativeFixtures(modeled_fixtures.distributions, evidence).match(
            request, run_id=_BASE_RUN
        )


def test_python_native_old_provider_requires_original_catalog_revision():
    """Current runtime cannot relabel an archived provider as current proof."""
    files = historical_files("prepared")
    fixtures = fixtures_from_files(files)
    request = NativeRequest(files["request.json"])
    with pytest.raises(
        ValueError, match="catalog digest is not the current static catalog"
    ):
        fixtures.match(request)
