"""Native tarball fixture for retained Buddy publication tests."""

from __future__ import annotations

import gzip
import hashlib
import importlib.util
import io
import json
import sys
import tarfile
from pathlib import Path
from typing import Any, cast

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import ArtifactContentIdentity
from three_workflow_delivery_v3.records.release import ReleaseArtifact
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
)


def _nul_filled(content: bytes, width: int) -> bytes:
    return content + bytes(width - len(content))


def _make_tarball(entries: dict[str, bytes]) -> bytes:
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w:gz") as archive:
        for name, content in sorted(entries.items()):
            info = tarfile.TarInfo(name)
            info.size = len(content)
            info.mtime = 0
            info.mode = 0o644
            archive.addfile(info, io.BytesIO(content))
    payload = bytearray(gzip.decompress(output.getvalue()))
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        members = archive.getmembers()
    for member in members:
        header = bytearray(payload[member.offset : member.offset + 512])
        header[0:100] = _nul_filled(member.name.encode(), 100)
        header[100:108] = b"000644 \0"
        header[108:116] = bytes(8)
        header[116:124] = bytes(8)
        header[124:136] = f"{member.size:010o} \0".encode()
        header[136:148] = b"0000000000 \0"
        header[148:156] = b"        "
        header[156:157] = tarfile.REGTYPE
        header[157:257] = bytes(100)
        header[257:263] = b"ustar\0"
        header[263:265] = b"00"
        header[265:297] = bytes(32)
        header[297:329] = bytes(32)
        header[329:337] = b"000000 \0"
        header[337:345] = b"000000 \0"
        header[345:500] = bytes(155)
        header[500:512] = bytes(12)
        checksum = sum(header)
        header[148:156] = f"{checksum:06o} \0".encode()
        payload[member.offset : member.offset + 512] = header
    for offset in range(0, len(payload), tarfile.BLOCKSIZE):
        block = payload[offset : offset + tarfile.BLOCKSIZE]
        if not any(block):
            payload = payload[: offset + (tarfile.BLOCKSIZE * 2)]
            break
    return gzip.compress(bytes(payload), compresslevel=9, mtime=0)


type QualifiedSimulation = Any

RELEASE_CONFTEST = Path(__file__).resolve().parents[1] / "release/conftest.py"
_SPEC = importlib.util.spec_from_file_location(
    "wdv3_release_conftest_for_npmjs",
    RELEASE_CONFTEST,
)
assert _SPEC is not None
assert _SPEC.loader is not None
_release_fixtures = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = _release_fixtures
_SPEC.loader.exec_module(_release_fixtures)


@pytest.fixture
def qualified_simulation(
    monkeypatch: pytest.MonkeyPatch,
) -> QualifiedSimulation:
    """Return the release fixture without enabling cross-directory plugins."""
    intent = _release_fixtures.intent.__wrapped__()
    policy = _release_fixtures.policy.__wrapped__()
    admitted = _release_fixtures.admitted_repository_model.__wrapped__(
        intent,
        policy,
    )
    binding = _release_fixtures.binding.__wrapped__(intent, admitted)
    snapshot = _release_fixtures.qualification_snapshot.__wrapped__(
        intent,
        binding,
        admitted,
    )
    return _release_fixtures.qualified_simulation.__wrapped__(
        monkeypatch,
        intent,
        admitted,
        binding,
        snapshot,
    )


def _package_manifest(
    simulation: QualifiedSimulation,
    *,
    name: str = FIRST_SLICE_PACKAGE,
    version: str | None = None,
) -> bytes:
    return json.dumps(
        {
            "name": name,
            "version": version or simulation.snapshot.nbgv.npm_package_version,
            "files": list(simulation.expectation.files_allowlist),
            "scripts": dict(simulation.expectation.lifecycle_scripts),
        },
        separators=(",", ":"),
    ).encode()


def _tarball(
    simulation: QualifiedSimulation,
    *,
    witness: bytes | None = None,
    manifest: bytes | None = None,
    extra: tuple[tuple[str, bytes], ...] = (),
) -> bytes:
    entries = {
        "package/README.md": b"# smoke\n",
        "package/dist/index.js": b"export const smokeMessage = () => 'x';\n",
        "package/package.json": manifest or _package_manifest(simulation),
        "package/workflow-delivery/provenance.json": (
            witness or simulation.expectation.witness_bytes
        ),
    }
    entries.update(dict(extra))
    return _make_tarball(entries)


def _artifact_for_tarball(
    simulation: QualifiedSimulation,
    tarball: bytes,
) -> ReleaseArtifact:
    content = ArtifactContentIdentity(
        output_id=simulation.artifact.output.output_id,
        logical_role=simulation.artifact.output.logical_role,
        media_kind=simulation.artifact.output.media_kind,
        basename=simulation.artifact.content.basename,
        byte_size=len(tarball),
        content_sha256=f"sha256:{hashlib.sha256(tarball).hexdigest()}",
        content_sha512=f"sha512:{hashlib.sha512(tarball).hexdigest()}",
    )
    provenance = {
        "schema": "workflow-delivery/v3/release-artifact-provenance",
        "subject": simulation.artifact.subject.to_document(),
        "repository": simulation.artifact.repository,
        "qualification-snapshot-digest": (
            simulation.artifact.qualification_snapshot_digest
        ),
        "repository-model-digest": simulation.artifact.repository_model_digest,
        "target": simulation.artifact.target,
        "purpose": simulation.artifact.purpose,
        "output": simulation.artifact.output.to_document(),
        "build-request-digest": simulation.artifact.build_request_digest,
        "transport": simulation.artifact.transport.to_document(),
        "content": content.to_document(),
        "witness-digest": simulation.artifact.witness_digest,
        "source-input-manifest": [
            [path, digest]
            for path, digest in simulation.artifact.source_input_manifest
        ],
        "toolchain": [
            [name, value] for name, value in simulation.artifact.toolchain
        ],
    }
    return ReleaseArtifact(
        subject=simulation.artifact.subject,
        repository=simulation.artifact.repository,
        qualification_snapshot_digest=(
            simulation.artifact.qualification_snapshot_digest
        ),
        repository_model_digest=simulation.artifact.repository_model_digest,
        target=simulation.artifact.target,
        purpose=simulation.artifact.purpose,
        output=simulation.artifact.output,
        build_request_digest=simulation.artifact.build_request_digest,
        transport=simulation.artifact.transport,
        content=content,
        entries=simulation.artifact.entries,
        lifecycle_scripts=simulation.artifact.lifecycle_scripts,
        witness_digest=simulation.artifact.witness_digest,
        source_input_manifest=simulation.artifact.source_input_manifest,
        toolchain=simulation.artifact.toolchain,
        provenance_digest=canonical_sha256(cast("Any", provenance)),
    )
