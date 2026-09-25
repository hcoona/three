"""Protected native request admission and exact retained evidence bytes."""

import io
import zipfile
from copy import deepcopy
from pathlib import Path
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import python_native_contract
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    FIXTURE_KEYS,
    SLOT_PATH,
    NativeRequest,
    load_request,
    pack_bundle,
    unpack_bundle,
    write_exclusive,
)
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.repository.python_provider import python_digest


def request_document(registry="testpypi"):
    """Supply prospective synthetic identities, never operator authorization."""
    destination = PythonRegistry(registry)
    return {
        "schema": "workflow-delivery/v3/python-native-request",
        "generation": "1" * 32,
        "registry": registry,
        "project": "hcoona-release-smoke-python",
        "profile-digest": destination.profile_digest,
        "targets": {
            "a": {"commit": "a" * 40, "version": "0.1.0b7"},
            "b": {"commit": "b" * 40, "version": "0.1.0b8"},
        },
        "fixture-digests": {
            key: python_digest(key.encode()) for key in FIXTURE_KEYS
        },
        "environment": {
            "id": 17,
            "name": destination.environment,
            "sentinel": "synthetic-environment-sentinel",
        },
        **{
            role: {
                "url": f"https://example.invalid/evidence/{role}",
                "digest": python_digest(role.encode()),
            }
            for role in ("authorization", "configuration", "ownership")
        },
    }


@pytest.mark.parametrize("registry", ["testpypi", "pypi"])
def test_python_native_request_closes_exact_destination(registry):
    """Each destination retains its own immutable request and profile."""
    document = request_document(registry)
    original = canonicalize(document)
    request = NativeRequest(original)
    assert request.registry == PythonRegistry(registry)
    assert request.digest == python_digest(original)
    assert request.target("a") == document["targets"]["a"]
    changed = request.document
    changed["targets"]["a"]["version"] = "99.0.0"
    assert request.target("a")["version"] == "0.1.0b7"
    assert request.content == original


@pytest.mark.parametrize(
    ("path", "value"),
    [
        (("schema",), "workflow-delivery/v3/python-governance-v1"),
        (("project",), "disposable-python-project"),
        (("registry",), "https://test.pypi.org"),
        (("profile-digest",), PythonRegistry("pypi").profile_digest),
        (("generation",), "A" * 32),
        (("generation",), "1" * 31),
        (("targets", "a", "commit"), "a" * 39),
        (("targets", "b", "commit"), "a" * 40),
        (("targets", "b", "version"), "0.1.0b7"),
        (("targets", "a", "version"), "0.1.0b7+local"),
        (("targets", "a", "version"), "0.1.0-beta.7"),
        (("targets", "a", "version"), "0.1.0"),
        (("environment", "id"), True),
        (("environment", "id"), "17"),
        (("environment", "id"), 0),
        (("environment", "name"), PythonRegistry("pypi").environment),
        (("environment", "sentinel"), ""),
        (("authorization", "url"), "http://example.invalid/request"),
        (("configuration", "url"), "https://token@example.invalid/config"),
        (("ownership", "digest"), "sha256:" + "A" * 64),
        (("fixture-digests", "a/original/wheel"), "sha256:" + "f" * 63),
    ],
)
def test_python_native_request_rejects_foreign_tuple(path, value):
    """Strict primitives and exact tuple identities cannot be coerced."""
    document = request_document()
    selected = document
    for part in path[:-1]:
        selected = selected[part]
    selected[path[-1]] = value
    with pytest.raises(
        ValueError, match=r"Python|acceptance|registry|smoke|evidence reference"
    ):
        NativeRequest(canonicalize(document))


@pytest.mark.parametrize(
    "section", [None, "targets", "fixture-digests", "environment"]
)
@pytest.mark.parametrize("change", ["extra", "missing"])
def test_python_native_request_rejects_open_or_incomplete_schema(
    section, change
):
    """An unrecognized or absent field cannot silently broaden admission."""
    document = request_document()
    selected = document if section is None else document[section]
    if change == "extra":
        selected["unexpected"] = None
    else:
        selected.pop(next(iter(selected)))
    with pytest.raises(
        ValueError, match=r"Python|acceptance|registry|smoke|evidence reference"
    ):
        NativeRequest(canonicalize(document))


@pytest.mark.parametrize("change", ["whitespace", "duplicate-key", "array"])
def test_python_native_request_requires_canonical_object_bytes(change):
    """Canonical protected bytes cannot hide duplicate keys or altered text."""
    content = canonicalize(request_document())
    if change == "whitespace":
        content += b"\n"
    elif change == "duplicate-key":
        content = b'{"project":"foreign",' + content[1:]
    else:
        content = b"[]"
    with pytest.raises((ValueError, TypeError)):
        NativeRequest(content)


def test_python_native_protected_slots_are_disabled_and_destination_bound(
    tmp_path,
):
    """Only one exact non-null protected slot and digest can load a request."""
    root = Path(__file__).resolve().parents[6]
    assert parse_json_strict((root / SLOT_PATH).read_bytes()) == {
        "testpypi": None,
        "pypi": None,
    }
    path = tmp_path / SLOT_PATH
    path.parent.mkdir(parents=True)
    document = request_document()
    request = NativeRequest(canonicalize(document))
    path.write_bytes(canonicalize({"testpypi": document, "pypi": None}))
    assert load_request(tmp_path, "testpypi", request.digest) == request
    for registry, expected in (
        ("pypi", request.digest),
        ("testpypi", "sha256:" + "f" * 64),
    ):
        with pytest.raises(
            ValueError,
            match=r"Python|acceptance|registry|smoke|evidence reference",
        ):
            load_request(tmp_path, registry, expected)
    path.write_bytes(canonicalize({"testpypi": None, "pypi": document}))
    with pytest.raises(ValueError, match="request mismatch"):
        load_request(tmp_path, "pypi", request.digest)


def _archive(files):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        for name, content in files.items():
            archive.writestr(name, content)
    return buffer.getvalue()


def test_python_native_bundle_preserves_exact_original_bytes():
    """Raw index text and binary originals survive an exact retained bundle."""
    files = {
        "raw/index.json": b'{ "files" : [] }\n',
        "raw/file.bin": b"\x00\xff\r\n",
    }
    packed = pack_bundle(files)
    assert unpack_bundle(packed) == files
    assert pack_bundle(dict(reversed(tuple(files.items())))) == packed
    with zipfile.ZipFile(io.BytesIO(packed)) as archive:
        assert set(archive.namelist()) == {*files, "manifest.json"}


@pytest.mark.parametrize(
    "change", ["bytes", "digest", "missing", "extra", "traversal"]
)
def test_python_native_bundle_rejects_corrupt_or_incomplete_retained_facts(
    change,
):
    """Raw evidence must match the complete manifest, not just an outer ZIP."""
    data = b"original\x00"
    files = {"raw.bin": data}
    manifest = {"raw.bin": {"bytes": len(data), "digest": python_digest(data)}}
    if change == "bytes":
        files["raw.bin"] = b"changed!\x00"
    elif change == "digest":
        manifest["raw.bin"]["digest"] = "sha256:" + "f" * 64
    elif change == "missing":
        files.clear()
    elif change == "extra":
        files["unexpected.bin"] = b"unbound"
    else:
        files["../outside"] = data
        manifest["../outside"] = deepcopy(manifest["raw.bin"])
    files["manifest.json"] = canonicalize(manifest)
    with pytest.raises(
        ValueError, match=r"Python|acceptance|registry|smoke|evidence reference"
    ):
        unpack_bundle(_archive(files))


def test_python_native_marker_is_exclusive_and_fsynced_before_return(
    tmp_path, monkeypatch
):
    """An ordinal marker cannot be replaced or returned before local fsync."""
    seen = []

    def observed_fsync(descriptor):
        assert descriptor >= 0
        seen.append((tmp_path / "markers/1.json").read_bytes())

    monkeypatch.setattr(python_native_contract.os, "fsync", observed_fsync)
    write_exclusive(tmp_path, "markers/1.json", b"admitted-original")
    assert seen == [b"admitted-original"]
    with pytest.raises(FileExistsError):
        write_exclusive(tmp_path, "markers/1.json", b"replacement")
    assert (tmp_path / "markers/1.json").read_bytes() == b"admitted-original"
    assert seen == [b"admitted-original"]


def test_python_native_marker_propagates_local_durability_failure(
    tmp_path, monkeypatch
):
    """A failed fsync cannot return a successful marker."""
    sync = Mock(side_effect=OSError("local fsync unavailable"))
    monkeypatch.setattr(python_native_contract.os, "fsync", sync)
    with pytest.raises(OSError, match="fsync unavailable"):
        write_exclusive(tmp_path, "marker.json", b"admitted")
    assert (tmp_path / "marker.json").read_bytes() == b"admitted"
    assert sync.call_count == 1
