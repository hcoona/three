"""Valid native comparison archives, source identity and clean consumers."""

# Native integration uses fixed local Git commands without a shell.
# ruff: noqa: S603, S607
import io
import subprocess
import tarfile
import zipfile
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance import python_native_fixture
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    FIXTURE_KEYS,
    NativeRequest,
    pack_bundle,
    unpack_bundle,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    NativeFixtures,
    build_fixture_set,
    comparison_distribution,
    fixture_witness,
    fixtures_from_files,
    prepare_fixtures,
)
from three_workflow_delivery_v3.adapters.python import (
    inspect_python_distribution,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_ROOT,
    PythonNbgvFacts,
    python_digest,
)

from ..adapters.test_pypi import _distribution
from ..repository import test_python_provider as provider_tests
from ..repository.test_python_provider import _provider
from .test_python_native_contract import request_document

native_python_provider_repository = (
    provider_tests.native_python_provider_repository
)
_BASE_RUN = 911


def _members(distribution):
    if distribution.variant == "wheel":
        with zipfile.ZipFile(io.BytesIO(distribution.content)) as archive:
            return {name: archive.read(name) for name in archive.namelist()}
    with tarfile.open(
        fileobj=io.BytesIO(distribution.content), mode="r:gz"
    ) as archive:
        return {item.name: archive.extractfile(item).read() for item in archive}


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


def _synthetic_witness(label):
    return fixture_witness(_synthetic_provider(label))


@pytest.fixture
def modeled_fixtures():
    """Valid archives with explicitly synthetic consumer evidence."""
    distributions = {}
    evidence = {}
    for label in ("a", "b"):
        provider = _synthetic_provider(label)
        witness = fixture_witness(provider)
        evidence[f"provider/{label}.json"] = canonicalize(
            provider.to_document()
        )
        for variant in ("wheel", "sdist"):
            original = _distribution(variant, witness)
            comparison = comparison_distribution(original)
            for candidate, item in (
                ("original", original),
                ("comparison", comparison),
            ):
                key = f"{label}/{candidate}/{variant}"
                distributions[key] = item
                evidence[f"consumer/{key}.json"] = canonicalize(
                    {
                        "variant": variant,
                        "original-digest": item.digest,
                        "installed": {
                            "version": witness.nbgv.pep440_version,
                            "project-id": "hcoona-release-smoke-python",
                            "witness": witness.to_document(),
                            "module": "/synthetic/consumer/package/__init__.py",
                        },
                        "commands": [
                            {
                                "argv": ["synthetic-test-only"],
                                "exit-code": 0,
                                "stdout": "",
                                "stderr": "",
                            }
                        ],
                    }
                )
        staged = next(
            content
            for name, content in _members(
                distributions[f"{label}/original/sdist"]
            ).items()
            if name.endswith("/pyproject.toml")
        )
        evidence[f"build/{label}.json"] = canonicalize(
            {
                "source-manifest": [
                    list(pair) for pair in provider.source_input_manifest
                ],
                "staged-manifest-digest": python_digest(staged),
                "versions": {
                    "argv": [
                        "uv",
                        "pip",
                        "freeze",
                        "--python",
                        "/synthetic/producer/bin/python",
                    ],
                    "exit-code": 0,
                    "stdout": "hatchling==1.32.0\n",
                    "stderr": "",
                },
                "commands": [
                    {
                        "argv": ["uv", "build", "--sdist", "--wheel"],
                        "exit-code": 0,
                        "stdout": "",
                        "stderr": "",
                    }
                ],
            }
        )
    return NativeFixtures(distributions, evidence)


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


@pytest.mark.parametrize("variant", ["wheel", "sdist"])
def test_python_native_different_bytes_remain_valid(modeled_fixtures, variant):
    """Representation changes preserve every native member and exact witness."""
    original = modeled_fixtures.distributions[f"a/original/{variant}"]
    changed = comparison_distribution(original)
    repeated = comparison_distribution(original)
    assert changed.filename == original.filename
    assert changed.digest != original.digest
    assert changed.content == repeated.content
    assert changed.witness == original.witness
    assert _members(changed) == _members(original)
    assert (
        inspect_python_distribution(
            changed.filename, changed.content, variant, original.witness
        )
        == changed
    )
    if variant == "sdist":
        assert changed.content[:4] == original.content[:4]
        assert changed.content[4:8] != original.content[4:8]
        assert changed.content[8:] == original.content[8:]


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


@pytest.fixture(scope="module")
def actual_native_fixtures(native_python_provider_repository):
    """Build both targets and qualify all eight files with real tools."""
    source, target_a = native_python_provider_repository
    origin = source.parent / "origin"
    readme = origin / PYTHON_ROOT / "README.md"
    readme.write_text(
        readme.read_text() + "\nSecond native acceptance target.\n"
    )
    subprocess.run(("git", "add", "."), cwd=origin, check=True)
    subprocess.run(
        (
            "git",
            "-c",
            "user.name=Python Native Fixture Test",
            "-c",
            "user.email=python-native@example.invalid",
            "-c",
            "core.hooksPath=/dev/null",
            "commit",
            "--quiet",
            "-m",
            "Advance Python native acceptance source",
        ),
        cwd=origin,
        check=True,
    )
    target_b = subprocess.check_output(
        ("git", "rev-parse", "HEAD"), cwd=origin, text=True
    ).strip()
    subprocess.run(
        ("git", "fetch", "--quiet", "origin"), cwd=source, check=True
    )
    subprocess.run(
        ("git", "checkout", "--quiet", "--detach", target_b),
        cwd=source,
        check=True,
    )
    targets = {"a": target_a, "b": target_b}
    return source, targets, build_fixture_set(source, targets, 911)


def test_real_python_native_fixture_pair_is_valid_and_exact(
    actual_native_fixtures,
):
    """All eight real builds/comparisons have successful isolated consumers."""
    _, targets, fixtures = actual_native_fixtures
    fixtures.match(fixture_request(fixtures))
    assert set(fixtures.distributions) == set(FIXTURE_KEYS)
    for key, item in fixtures.distributions.items():
        label, _, variant = key.split("/")
        proof = parse_canonical_json(fixtures.evidence[f"consumer/{key}.json"])
        assert item.witness.target == targets[label]
        assert item.witness.purpose == "destination-acceptance"
        assert "+" not in item.witness.nbgv.pep440_version
        assert proof["variant"] == variant
        assert proof["original-digest"] == item.digest
        assert proof["installed"]["version"] == item.witness.nbgv.pep440_version
        assert proof["installed"]["project-id"] == "hcoona-release-smoke-python"
        assert proof["installed"]["witness"] == item.witness.to_document()
        assert all(command["exit-code"] == 0 for command in proof["commands"])
        provider = parse_canonical_json(
            fixtures.evidence[f"provider/{label}.json"]
        )
        assert provider["binding"]["workflow-run-id"] == _BASE_RUN


def test_real_python_native_fixture_bytes_are_execution_independent(
    actual_native_fixtures,
):
    """A different execution preserves all eight native byte identities."""
    source, targets, first = actual_native_fixtures
    second = build_fixture_set(source, targets, 912)
    assert {
        key: item.content for key, item in second.distributions.items()
    } == {key: item.content for key, item in first.distributions.items()}
    for label in ("a", "b"):
        assert (
            second.evidence[f"provider/{label}.json"]
            != first.evidence[f"provider/{label}.json"]
        )


def test_python_native_prepared_binding_changes_without_changing_package_bytes(
    modeled_fixtures, monkeypatch, tmp_path
):
    """Execution and request identity belong to the prepared envelope only."""
    calls = []

    def built(repo_root, targets, run_id):
        calls.append((repo_root, targets, run_id))
        evidence = dict(modeled_fixtures.evidence)
        for label in ("a", "b"):
            key = f"provider/{label}.json"
            provider = parse_canonical_json(evidence[key])
            provider["binding"]["workflow-run-id"] = run_id
            evidence[key] = canonicalize(provider)
        return NativeFixtures(modeled_fixtures.distributions, evidence)

    monkeypatch.setattr(python_native_fixture, "build_fixture_set", built)
    first_request = fixture_request(modeled_fixtures)
    second_document = first_request.document
    second_document["generation"] = "2" * 32
    second_request = NativeRequest(canonicalize(second_document))
    first = prepare_fixtures(tmp_path, first_request, 911, "c" * 40)
    second = prepare_fixtures(tmp_path, second_request, 912, "d" * 40)
    first_binding = parse_canonical_json(first["binding.json"])
    second_binding = parse_canonical_json(second["binding.json"])
    assert first_binding["request-digest"] == first_request.digest
    assert second_binding == {
        "request-digest": second_request.digest,
        "run-id": 912,
        "run-attempt": 1,
        "tooling-sha": "d" * 40,
        "producer": "prepare-python-native",
    }
    assert first["binding.json"] != second["binding.json"]
    assert calls == [
        (tmp_path, {"a": "a" * 40, "b": "b" * 40}, 911),
        (tmp_path, {"a": "a" * 40, "b": "b" * 40}, 912),
    ]
    for name, content in modeled_fixtures.files().items():
        if name.startswith("provider/"):
            assert first[name] == content
            assert first[name] != second[name]
        else:
            assert first[name] == second[name] == content


def test_real_python_native_final_audit_freshly_consumes_downloaded_files(
    actual_native_fixtures, tmp_path
):
    """Four captured actual originals pass independent clean audit consumers."""
    from three_workflow_delivery_v3.acceptance.python_native_suite import (  # noqa: PLC0415
        audit_suite,
        run_suite,
    )

    from .test_python_native_suite import RegistryBoundary  # noqa: PLC0415

    _, _, fixtures = actual_native_fixtures
    request = fixture_request(fixtures)
    boundary = RegistryBoundary(fixtures, winners=("comparison", "original"))
    retained = run_suite(
        request,
        fixtures,
        boundary,
        "pypi-synthetic-audit-token",
        tmp_path / "probe",
    )
    calls = tuple(boundary.reads), tuple(boundary.posts)
    result = audit_suite(request, fixtures, retained)
    proofs = [
        parse_canonical_json(content)
        for name, content in result.items()
        if name.startswith("consumer/")
    ]
    assert len(proofs) == 4  # noqa: PLR2004 - protocol final files
    assert {proof["original-digest"] for proof in proofs} == {
        item.digest for item in boundary.stored.values()
    }
    for proof in proofs:
        assert proof["installed"]["project-id"] == "hcoona-release-smoke-python"
        assert all(command["exit-code"] == 0 for command in proof["commands"])
    assert (tuple(boundary.reads), tuple(boundary.posts)) == calls
    assert (
        parse_canonical_json(result["audit.json"])["native-admission"] is False
    )
