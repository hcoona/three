"""Synthetic local fixture scenarios, never native evidence or authorization."""

from __future__ import annotations

import gzip
import hashlib
import io
import json
import subprocess
import tarfile
from dataclasses import FrozenInstanceError, replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import npm_fixture as fixture
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

# Counts and archive offsets are independent scenario expectations.
# ruff: noqa: PLR2004

ROOT = Path(__file__).resolve().parents[6]
MANIFEST = "package/package.json"
WITNESS = "package/workflow-delivery/acceptance-witness.json"
SPEC = fixture.NpmFixtureSpec(
    package="@hcoona/synthetic-native-fixture",
    version="0.0.1-acceptance.1",
    target="a" * 40,
    generation="synthetic-generation",
)


@pytest.fixture
def original():
    """Build local bytes using the actual installed official npm parsers."""
    return fixture.build_npm_fixture(SPEC, repository_root=ROOT)


def _entries(tarball):
    with tarfile.open(fileobj=io.BytesIO(tarball), mode="r:gz") as archive:
        result = {}
        for member in archive:
            stream = archive.extractfile(member)
            assert stream is not None
            result[member.name] = stream.read()
        return result


def _rewrite(tarball, changes):
    """Independently edit archive content, retaining npm USTAR headers."""
    payload = gzip.decompress(tarball)
    result = bytearray()
    entries = _entries(tarball)
    entries.update(changes)
    for name, content in entries.items():
        if content is None:
            continue
        header = bytearray(payload[:512])
        header[:100] = name.encode().ljust(100, b"\0")
        header[124:136] = f"{len(content):010o} \0".encode()
        header[148:156] = b" " * 8
        header[148:156] = f"{sum(header):06o} \0".encode()
        result.extend(header)
        result.extend(content)
        result.extend(bytes(-len(content) % 512))
    result.extend(bytes(1024))
    return gzip.compress(bytes(result), mtime=0)


def test_reproduction_has_only_closed_manifest_and_acceptance_witness(original):
    """Stable inputs reproduce exact bytes, digests and acceptance bindings."""
    repeated = fixture.build_npm_fixture(SPEC, repository_root=ROOT)

    assert repeated == original
    assert repeated is not original
    assert original.tarball[4:8] == bytes(4)
    entries = _entries(original.tarball)
    assert set(entries) == {MANIFEST, WITNESS}
    assert json.loads(entries[MANIFEST]) == {
        "name": SPEC.package,
        "version": SPEC.version,
        "private": False,
        "repository": {
            "type": "git",
            "url": "git+https://github.com/hcoona/three.git",
        },
    }
    assert parse_canonical_json(original.content.witness) == {
        "schema": "workflow-delivery-v3/native-npm-fixture/v1",
        "package": SPEC.package,
        "version": SPEC.version,
        "target": SPEC.target,
        "generation": SPEC.generation,
        "variant": "original",
    }
    assert original.content.witness == entries[WITNESS]
    assert original.content.version == SPEC.version
    assert original.content.target == SPEC.target
    assert original.content.sha256 == (
        "sha256:" + hashlib.sha256(original.tarball).hexdigest()
    )
    assert original.content.sha512 == (
        "sha512:" + hashlib.sha512(original.tarball).hexdigest()
    )
    with pytest.raises(FrozenInstanceError):
        setattr(SPEC, "version", "2.0.0")  # noqa: B010


def test_different_duplicate_changes_bytes_and_witness_not_version(original):
    """A same-version contender cannot be mistaken for the original bytes."""
    different = fixture.build_npm_fixture(
        replace(SPEC, variant="different"), repository_root=ROOT
    )
    observed = fixture.inspect_npm_fixture(
        different.tarball, repository_root=ROOT
    )

    assert observed == different.content
    assert observed is not different.content
    assert different.tarball != original.tarball
    assert observed.version == original.content.version
    assert observed.target == original.content.target
    assert observed.sha256 != original.content.sha256
    assert observed.sha512 != original.content.sha512
    assert observed.witness != original.content.witness
    assert parse_canonical_json(observed.witness)["variant"] == "different"
    assert (
        _entries(different.tarball)[MANIFEST]
        == _entries(original.tarball)[MANIFEST]
    )


def test_inspection_observes_independent_content_not_expected_inputs(original):
    """Internally consistent changed content remains a separate observation."""
    entries = _entries(original.tarball)
    witness = parse_canonical_json(entries[WITNESS])
    witness.update(
        version="0.0.2-acceptance.1",
        target="b" * 40,
        generation="independently-observed",
    )
    manifest = json.loads(entries[MANIFEST])
    manifest["version"] = witness["version"]
    tarball = _rewrite(
        original.tarball,
        {MANIFEST: canonicalize(manifest), WITNESS: canonicalize(witness)},
    )

    observed = fixture.inspect_npm_fixture(tarball, repository_root=ROOT)

    assert observed != original.content
    assert observed.version == "0.0.2-acceptance.1"
    assert observed.target == "b" * 40
    assert observed.witness == canonicalize(witness)
    assert observed.sha256 == "sha256:" + hashlib.sha256(tarball).hexdigest()
    assert observed.sha512 == "sha512:" + hashlib.sha512(tarball).hexdigest()


@pytest.mark.parametrize(
    ("entry", "patch"),
    [
        (MANIFEST, {"name": "@hcoona/other-synthetic-fixture"}),
        (MANIFEST, {"version": "0.0.2-acceptance.1"}),
        (MANIFEST, {"private": True}),
        (MANIFEST, {"private": 0}),
        (MANIFEST, {"repository": "hcoona/other"}),
        (MANIFEST, {"scripts": {"prepublishOnly": "node unwanted.js"}}),
        (MANIFEST, {"publishConfig": {"registry": "https://invalid.example"}}),
        (WITNESS, {"package": "@hcoona/other-synthetic-fixture"}),
        (WITNESS, {"version": "0.0.2-acceptance.1"}),
        (WITNESS, {"schema": "workflow-delivery/v3/package-target-witness"}),
        (WITNESS, {"release": True}),
        (WITNESS, {"target": True}),
    ],
)
def test_inspection_rejects_wrong_manifest_or_witness_closure(
    original, entry, patch
):
    """Reject mutation-sensitive metadata and misbound Release witnesses."""
    document = json.loads(_entries(original.tarball)[entry])
    document.update(patch)
    changed = _rewrite(original.tarball, {entry: canonicalize(document)})

    with pytest.raises(
        (ValueError, TypeError),
        match=r"fixture (manifest|acceptance witness|identity fields)",
    ):
        fixture.inspect_npm_fixture(changed, repository_root=ROOT)


@pytest.mark.parametrize(
    ("package", "version"),
    [
        ("synthetic-native-fixture", "1.2.3"),
        ("@another/synthetic-native-fixture", "1.2.3"),
        ("@hcoona/invalid name", "1.2.3"),
        ("@hcoona/Uppercase", "1.2.3"),
        ("@hcoona/", "1.2.3"),
        ("@hcoona/synthetic-native-fixture@1.2.3", "1.2.3"),
        (SPEC.package, "^1.2.3"),
        (SPEC.package, ">=1.2.3"),
        (SPEC.package, "latest"),
        (SPEC.package, "npm:other@1.2.3"),
        (SPEC.package, "file:../synthetic"),
        (SPEC.package, "v1.2.3"),
        (SPEC.package, "1.2.3+build.1"),
        (SPEC.package, " 1.2.3 "),
        (SPEC.package, "01.2.3"),
        (SPEC.package, ""),
    ],
)
def test_official_parsers_reject_noncanonical_coordinates(package, version):
    """Real npa and package-json enforce npm identity, not a test double."""
    with pytest.raises(ValueError, match="official npm fixture"):
        fixture.build_npm_fixture(
            replace(SPEC, package=package, version=version),
            repository_root=ROOT,
        )


@pytest.mark.parametrize(
    ("version", "generation"),
    [("1.2.3", "0"), ("10.20.30-alpha.1", "G" * 64)],
)
def test_official_parsers_accept_exact_versions_and_generation_bounds(
    version, generation
):
    """Stable and prerelease versions preserve inclusive generation bounds."""
    result = fixture.build_npm_fixture(
        replace(SPEC, version=version, generation=generation),
        repository_root=ROOT,
    )

    assert result.content.version == version
    witness = parse_canonical_json(result.content.witness)
    assert witness["version"] == version
    assert witness["generation"] == generation
    assert witness["package"] == "@hcoona/synthetic-native-fixture"


@pytest.mark.parametrize(
    "change",
    [
        {"target": "a" * 39},
        {"target": "A" * 40},
        {"generation": ""},
        {"generation": "g" * 65},
        {"generation": "../unsafe"},
        {"generation": "space separated"},
        {"variant": "unknown"},
    ],
)
def test_frozen_spec_rejects_invalid_local_bindings(change):
    """Reject unsafe generation/target/variant before producing a fixture."""
    with pytest.raises(
        ValueError, match=r"fixture (target|generation|variant)"
    ):
        replace(SPEC, **change)


def test_inspection_rejects_wrong_entry_closure_and_malformed_tarball(original):
    """The shared strict archive parser supplies controlled failure."""
    for change in (
        {WITNESS: None},
        {"package/unexpected.js": b"synthetic unexpected content"},
    ):
        with pytest.raises(ValueError, match="entry closure"):
            fixture.inspect_npm_fixture(
                _rewrite(original.tarball, change), repository_root=ROOT
            )
    with pytest.raises(ValueError, match="invalid npm tarball"):
        fixture.inspect_npm_fixture(original.tarball[:-8], repository_root=ROOT)


def test_official_parser_process_is_credential_free_and_bounded(monkeypatch):
    """Real parsing succeeds without inherited credentials or Node injection."""
    for key in (
        "GITHUB_TOKEN",
        "NODE_AUTH_TOKEN",
        "NPM_TOKEN",
        "NODE_OPTIONS",
        "NODE_PATH",
        "NPM_CONFIG_USERCONFIG",
    ):
        monkeypatch.setenv(key, "synthetic-do-not-inherit")
    real_run = subprocess.run
    calls = []

    def checked_run(command, **kwargs):
        calls.append(command)
        assert Path(command[0]).is_absolute()
        assert command[1] == str(
            ROOT / "eng/scripts/workflow_delivery_v3_native_npm.mjs"
        )
        assert set(kwargs["env"]) <= {"PATH", "SystemRoot", "WINDIR"}
        assert kwargs["shell"] is False
        assert kwargs["check"] is True
        assert kwargs["capture_output"] is True
        assert kwargs["timeout"] == 15
        assert kwargs["cwd"] == ROOT
        assert json.loads(kwargs["input"]) == {
            "package": SPEC.package,
            "version": SPEC.version,
        }
        return real_run(command, **kwargs)

    monkeypatch.setattr(fixture.subprocess, "run", checked_run)

    result = fixture.build_npm_fixture(SPEC, repository_root=ROOT)

    assert result.content.version == SPEC.version
    assert len(calls) == 1


@pytest.mark.parametrize(
    "error",
    [
        OSError("synthetic parser read failure"),
        subprocess.TimeoutExpired(("node",), 15),
        subprocess.CalledProcessError(1, ("node",)),
    ],
)
def test_parser_process_failure_is_closed(monkeypatch, error):
    """A missing bridge, failed parser or timeout cannot admit fixture bytes."""

    def failed_run(*_args, **_kwargs):
        raise error

    monkeypatch.setattr(fixture.subprocess, "run", failed_run)

    with pytest.raises(ValueError, match="official npm fixture"):
        fixture.build_npm_fixture(SPEC, repository_root=ROOT)


@pytest.mark.parametrize("response", [b"not JSON", b"\xff", b"{}"])
def test_parser_response_failure_is_closed(monkeypatch, response):
    """Malformed output or substituted coordinates never become observations."""
    monkeypatch.setattr(
        fixture.subprocess,
        "run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess(
            ("node",), 0, stdout=response
        ),
    )

    with pytest.raises(ValueError, match="official npm fixture coordinate"):
        fixture.build_npm_fixture(SPEC, repository_root=ROOT)
