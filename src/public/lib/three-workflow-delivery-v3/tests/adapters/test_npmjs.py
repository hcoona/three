"""Credential-free npmjs observer scenarios for commit 7."""

from __future__ import annotations

# ruff: noqa: D103
import gzip
import hashlib
import importlib.util
import io
import json
import sys
import tarfile
import urllib.error
import urllib.request
from dataclasses import replace
from email.message import Message
from pathlib import Path
from typing import TYPE_CHECKING, Any, Self, cast

import pytest
from three_workflow_delivery_v3.adapters import npmjs as npmjs_module
from three_workflow_delivery_v3.adapters.npmjs import (
    HttpResponse,
    NpmjsNetworkError,
    NpmjsTimeoutError,
    StdlibHttpTransport,
    observe_npmjs_projection,
)
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
)
from three_workflow_delivery_v3.records.release import (
    QualificationDecision,
    ReleaseArtifact,
)
from three_workflow_delivery_v3.release.finalizer import (
    desired_projection_state_digest,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
)
from three_workflow_delivery_v3.repository.node_provider import NbgvFacts

if TYPE_CHECKING:
    from collections.abc import Mapping

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


class ScriptedTransport:
    """In-memory HTTP transport that proves tests never use the network."""

    def __init__(
        self,
        responses: Mapping[str, HttpResponse | BaseException],
    ) -> None:
        """Store exact URL responses."""
        self.responses = dict(responses)
        self.requests: list[tuple[str, int, tuple[tuple[str, str], ...]]] = []

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> HttpResponse:
        """Return a scripted response without touching the network."""
        del timeout
        self.requests.append((url, max_bytes, headers))
        response = self.responses[url]
        if isinstance(response, BaseException):
            raise response
        return response


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


def _decision_for_artifact(
    simulation: QualifiedSimulation,
    artifact: ReleaseArtifact,
) -> QualificationDecision:
    return replace(
        simulation.decision,
        admitted_artifact_digests=(artifact.artifact_digest,),
    )


def _metadata_response(  # noqa: PLR0913
    simulation: QualifiedSimulation,
    *,
    status: int = 200,
    name: str = FIRST_SLICE_PACKAGE,
    version: str | None = None,
    tarball_url: str = (
        "https://registry.npmjs.org/@hcoona/hcoona-release-smoke-npm/-/pkg.tgz"
    ),
    headers: tuple[tuple[str, str], ...] = (),
    url: str | None = None,
    redirects: tuple[str, ...] = (),
    integrity: str = "sha512-not-authoritative",
) -> HttpResponse:
    version = version or simulation.snapshot.nbgv.npm_package_version
    body = json.dumps(
        {
            "name": name,
            "version": version,
            "dist": {
                "tarball": tarball_url,
                "integrity": integrity,
            },
        },
        separators=(",", ":"),
    ).encode()
    return HttpResponse(
        status=status,
        url=url or _metadata_url(simulation),
        headers=headers,
        body=body,
        redirects=redirects,
    )


def _metadata_url(simulation: QualifiedSimulation) -> str:
    version = simulation.snapshot.nbgv.npm_package_version
    return (
        "https://registry.npmjs.org/"
        f"@hcoona%2Fhcoona-release-smoke-npm/{version}"
    )


def _observe(
    simulation: QualifiedSimulation,
    *,
    metadata: HttpResponse | BaseException,
    tarball: HttpResponse | BaseException | None = None,
    artifact: ReleaseArtifact | None = None,
) -> tuple[str, ScriptedTransport]:
    tarball_url = (
        "https://registry.npmjs.org/@hcoona/hcoona-release-smoke-npm/-/pkg.tgz"
    )
    artifact = artifact or simulation.artifact
    transport = ScriptedTransport(
        {
            _metadata_url(simulation): metadata,
            tarball_url: tarball
            or HttpResponse(
                status=200,
                url=tarball_url,
                headers=(),
                body=b"not used",
            ),
        }
    )
    observation = observe_npmjs_projection(
        simulation.snapshot,
        _decision_for_artifact(simulation, artifact),
        artifact,
        simulation.expectation,
        transport=transport,
    )
    return observation.value.classification, transport


def _observe_full(
    simulation: QualifiedSimulation,
    *,
    metadata: HttpResponse | BaseException,
    tarball: HttpResponse | BaseException | None = None,
    artifact: ReleaseArtifact | None = None,
    expanded_tarball_limit_bytes: int = 100_000_000,
) -> tuple[Any, ScriptedTransport]:
    tarball_url = (
        "https://registry.npmjs.org/@hcoona/hcoona-release-smoke-npm/-/pkg.tgz"
    )
    artifact = artifact or simulation.artifact
    transport = ScriptedTransport(
        {
            _metadata_url(simulation): metadata,
            tarball_url: tarball
            or HttpResponse(
                status=200,
                url=tarball_url,
                headers=(),
                body=b"not used",
            ),
        }
    )
    observation = observe_npmjs_projection(
        simulation.snapshot,
        _decision_for_artifact(simulation, artifact),
        artifact,
        simulation.expectation,
        transport=transport,
        expanded_tarball_limit_bytes=expanded_tarball_limit_bytes,
    )
    return observation, transport


def test_npmjs_observer_classifies_exact_404_as_absent(
    qualified_simulation: QualifiedSimulation,
) -> None:
    classification, transport = _observe(
        qualified_simulation,
        metadata=HttpResponse(
            404,
            _metadata_url(qualified_simulation),
            (),
            b"",
        ),
    )

    assert classification == "absent"
    assert transport.requests[0][0] == _metadata_url(qualified_simulation)
    assert len(transport.requests) == 1


def test_npmjs_observer_treats_exact_version_404_as_absent(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation, transport = _observe_full(
        qualified_simulation,
        metadata=HttpResponse(
            404,
            _metadata_url(qualified_simulation),
            (("content-type", "application/json"),),
            b'{"error":"version not found"}',
        ),
    )

    assert observation.value.classification == "absent"
    assert _metadata_url(qualified_simulation).startswith(
        "https://registry.npmjs.org/@hcoona%2Fhcoona-release-smoke-npm/"
    )
    assert "@hcoona/hcoona-release-smoke-npm" not in _metadata_url(
        qualified_simulation
    )
    projection = qualified_simulation.snapshot.destination_projections[0]
    assert (
        observation.request_digest == observation.request_facts.request_digest
    )
    assert observation.request_facts.qualification_snapshot_digest == (
        qualified_simulation.snapshot.snapshot_digest
    )
    assert observation.request_facts.projection_digest == (
        projection.projection_digest
    )
    assert observation.request_facts.desired_state_digest == (
        desired_projection_state_digest(
            qualified_simulation.snapshot,
            projection.projection_id,
            qualified_simulation.artifact,
        )
    )
    assert observation.request_facts.method == "GET"
    assert observation.request_facts.url == _metadata_url(qualified_simulation)
    assert transport.requests == [
        (
            _metadata_url(qualified_simulation),
            1_000_000,
            (
                (
                    "Accept",
                    "application/vnd.npm.install-v1+json, application/json",
                ),
                ("Accept-Encoding", "identity"),
                ("Cache-Control", "no-cache"),
            ),
        )
    ]


def test_npmjs_observer_accepts_exact_bytes_and_witness(
    qualified_simulation: QualifiedSimulation,
) -> None:
    tarball = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, tarball)

    classification, transport = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            tarball,
        ),
        artifact=artifact,
    )

    assert classification == "exact-satisfied"
    assert transport.requests == [
        (
            _metadata_url(qualified_simulation),
            1_000_000,
            (
                (
                    "Accept",
                    "application/vnd.npm.install-v1+json, application/json",
                ),
                ("Accept-Encoding", "identity"),
                ("Cache-Control", "no-cache"),
            ),
        ),
        (
            "https://registry.npmjs.org/@hcoona/"
            "hcoona-release-smoke-npm/-/pkg.tgz",
            25_000_000,
            (
                ("Accept", "application/octet-stream"),
                ("Accept-Encoding", "identity"),
                ("Cache-Control", "no-cache"),
            ),
        ),
    ]


def test_npmjs_observer_reports_byte_conflict(
    qualified_simulation: QualifiedSimulation,
) -> None:
    local = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, local)
    remote = _tarball(
        qualified_simulation,
        extra=(("package/extra.txt", b"different complete bytes"),),
    )

    observation, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            remote,
        ),
        artifact=artifact,
    )

    assert observation.value.classification == "conflicting"
    assert observation.value.owner == "scope:@hcoona"
    assert observation.value.coordinate == (
        qualified_simulation.snapshot.destination_projections[0].coordinate
    )
    assert observation.value.content_sha512 == (
        f"sha512:{hashlib.sha512(remote).hexdigest()}"
    )
    assert observation.value.witness_digest == artifact.witness_digest


def test_npmjs_observer_reports_target_witness_conflict(
    qualified_simulation: QualifiedSimulation,
) -> None:
    local = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, local)
    other_target = "f" * 40
    original_nbgv = qualified_simulation.request.witness.nbgv
    other_witness = replace(
        qualified_simulation.request.witness,
        target=other_target,
        nbgv=NbgvFacts(
            canonical_version=original_nbgv.canonical_version,
            sem_ver1=original_nbgv.sem_ver1,
            sem_ver2=original_nbgv.sem_ver2,
            version_height=original_nbgv.version_height,
            git_commit_id=other_target,
            public_release=original_nbgv.public_release,
            npm_package_version=original_nbgv.npm_package_version,
            node_api_result_digest=original_nbgv.node_api_result_digest,
        ),
    ).canonical_bytes

    observation, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            _tarball(qualified_simulation, witness=other_witness),
        ),
        artifact=artifact,
    )

    assert observation.value.classification == "conflicting"
    assert observation.value.witness_digest == canonical_sha256(
        cast("Any", json.loads(other_witness))
    )


@pytest.mark.parametrize(
    "manifest",
    [
        lambda simulation: _package_manifest(
            simulation,
            name="@hcoona/wrong-package",
        ),
        lambda simulation: _package_manifest(
            simulation,
            version="9.9.9",
        ),
    ],
)
def test_npmjs_observer_reports_packed_manifest_identity_conflict(
    qualified_simulation: QualifiedSimulation,
    manifest: Any,
) -> None:
    local = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, local)
    remote = _tarball(
        qualified_simulation,
        manifest=manifest(qualified_simulation),
    )

    observation, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            remote,
        ),
        artifact=artifact,
    )

    assert observation.value.classification == "conflicting"
    assert observation.value.coordinate == (
        qualified_simulation.snapshot.destination_projections[0].coordinate
    )
    assert observation.value.content_sha512 == (
        f"sha512:{hashlib.sha512(remote).hexdigest()}"
    )
    assert observation.value.witness_digest == artifact.witness_digest


def test_npmjs_observer_bounds_expanded_tarball_and_parses_once(
    qualified_simulation: QualifiedSimulation,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    local = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, local)
    remote = _tarball(
        qualified_simulation,
        extra=(("package/highly-compressible.txt", b"x" * 100_000),),
    )
    parse_calls = 0
    original_read_tarball = npmjs_module._read_tarball  # noqa: SLF001
    expanded_limit = 8_192

    def counted_read_tarball(
        tarball: bytes,
        *,
        max_payload_bytes: int | None = None,
    ) -> dict[str, bytes]:
        nonlocal parse_calls
        parse_calls += 1
        return original_read_tarball(
            tarball,
            max_payload_bytes=max_payload_bytes,
        )

    monkeypatch.setattr(npmjs_module, "_read_tarball", counted_read_tarball)

    observation, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            remote,
        ),
        artifact=artifact,
        expanded_tarball_limit_bytes=expanded_limit,
    )

    assert len(remote) < expanded_limit
    assert observation.value.classification == "unprovable"
    assert parse_calls == 1


@pytest.mark.parametrize("status", [401, 403, 418])
def test_npmjs_observer_hard_4xx_is_unprovable(
    qualified_simulation: QualifiedSimulation,
    status: int,
) -> None:
    classification, _transport = _observe(
        qualified_simulation,
        metadata=HttpResponse(status, "https://registry.npmjs.org/x", (), b""),
    )

    assert classification == "unprovable"


@pytest.mark.parametrize("status", [429, 500, 503])
def test_npmjs_observer_retryable_status_is_unknown(
    qualified_simulation: QualifiedSimulation,
    status: int,
) -> None:
    classification, _transport = _observe(
        qualified_simulation,
        metadata=HttpResponse(status, "https://registry.npmjs.org/x", (), b""),
    )

    assert classification == "unknown"


def test_npmjs_observer_timeout_is_unknown(
    qualified_simulation: QualifiedSimulation,
) -> None:
    classification, _transport = _observe(
        qualified_simulation,
        metadata=NpmjsTimeoutError("timeout"),
    )

    assert classification == "unknown"


def test_npmjs_observer_malformed_or_wrong_metadata_is_unprovable(
    qualified_simulation: QualifiedSimulation,
) -> None:
    malformed, _ = _observe(
        qualified_simulation,
        metadata=HttpResponse(200, "https://registry.npmjs.org/x", (), b"{"),
    )
    wrong_name, _ = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation, name="@hcoona/other"),
    )
    wrong_version, _ = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation, version="9.9.9"),
    )

    assert malformed == "unprovable"
    assert wrong_name == "unprovable"
    assert wrong_version == "unprovable"


def test_npmjs_observer_rejects_metadata_off_origin_final_url_or_redirect(
    qualified_simulation: QualifiedSimulation,
) -> None:
    final_url, final_transport = _observe(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            url="https://example.invalid/@hcoona%2Fhcoona-release-smoke-npm",
        ),
    )
    redirect, redirect_transport = _observe(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            redirects=("https://example.invalid/metadata",),
        ),
    )

    assert final_url == "unprovable"
    assert redirect == "unprovable"
    assert len(final_transport.requests) == 1
    assert len(redirect_transport.requests) == 1


def test_npmjs_observer_rejects_metadata_redirect_limit(
    qualified_simulation: QualifiedSimulation,
) -> None:
    redirects = tuple(
        f"https://registry.npmjs.org/redirect-{index}" for index in range(6)
    )

    classification, transport = _observe(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            redirects=redirects,
        ),
    )

    assert classification == "unprovable"
    assert len(transport.requests) == 1


def test_npmjs_observer_missing_or_off_host_tarball_is_unprovable(
    qualified_simulation: QualifiedSimulation,
) -> None:
    for metadata in (
        _metadata_response(
            qualified_simulation, tarball_url="https://evil/t.tgz"
        ),
        _metadata_response(qualified_simulation, tarball_url="not-a-url"),
    ):
        classification, _transport = _observe(
            qualified_simulation,
            metadata=metadata,
        )
        assert classification == "unprovable"


def test_npmjs_observer_rejects_off_host_redirect_and_nonidentity_encoding(
    qualified_simulation: QualifiedSimulation,
) -> None:
    tarball = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, tarball)

    redirected, _ = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/pkg.tgz",
            (),
            tarball,
            redirects=("https://example.invalid/pkg.tgz",),
        ),
        artifact=artifact,
    )
    encoded, _ = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/pkg.tgz",
            (("content-encoding", "gzip"),),
            tarball,
        ),
        artifact=artifact,
    )

    assert redirected == "unprovable"
    assert encoded == "unprovable"


def test_npmjs_observer_missing_or_malformed_witness_is_unprovable(
    qualified_simulation: QualifiedSimulation,
) -> None:
    local = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, local)
    missing_witness = _make_tarball(
        {
            "package/README.md": b"# smoke\n",
            "package/dist/index.js": (
                b"export const smokeMessage = () => 'x';\n"
            ),
            "package/package.json": _package_manifest(qualified_simulation),
        }
    )

    missing, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            missing_witness,
        ),
        artifact=artifact,
    )
    malformed, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            _tarball(qualified_simulation, witness=b"{"),
        ),
        artifact=artifact,
    )

    assert missing.value.classification == "unprovable"
    assert missing.value.witness_digest is None
    assert malformed.value.classification == "unprovable"
    assert malformed.value.witness_digest is None


def test_npmjs_observer_size_truncation_is_unknown(
    qualified_simulation: QualifiedSimulation,
) -> None:
    classification, _transport = _observe(
        qualified_simulation,
        metadata=HttpResponse(
            200,
            "https://registry.npmjs.org/x",
            (),
            b"{}",
            truncated=True,
        ),
    )

    assert classification == "unknown"


def test_npmjs_observer_integrity_only_is_not_exact(
    qualified_simulation: QualifiedSimulation,
) -> None:
    classification, _transport = _observe(
        qualified_simulation,
        metadata=_metadata_response(qualified_simulation),
        tarball=HttpResponse(
            404,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (),
            b"",
        ),
    )

    assert classification == "unprovable"


def test_npmjs_observer_response_digest_binds_remote_facts(
    qualified_simulation: QualifiedSimulation,
) -> None:
    tarball = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, tarball)

    baseline, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            integrity="sha512-baseline",
        ),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (("etag", "tarball-a"),),
            tarball,
        ),
        artifact=artifact,
    )
    metadata_changed, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            integrity="sha512-changed",
        ),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (("etag", "tarball-a"),),
            tarball,
        ),
        artifact=artifact,
    )
    redirect_changed, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            redirects=("https://registry.npmjs.org/exact-version",),
        ),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (("etag", "tarball-a"),),
            tarball,
        ),
        artifact=artifact,
    )
    tarball_header_changed, _transport = _observe_full(
        qualified_simulation,
        metadata=_metadata_response(
            qualified_simulation,
            integrity="sha512-baseline",
        ),
        tarball=HttpResponse(
            200,
            "https://registry.npmjs.org/@hcoona/pkg.tgz",
            (("etag", "tarball-b"),),
            tarball,
        ),
        artifact=artifact,
    )

    expected_distinct_digests = 4
    digests = {
        baseline.response_digest,
        metadata_changed.response_digest,
        redirect_changed.response_digest,
        tarball_header_changed.response_digest,
    }
    assert len(digests) == expected_distinct_digests


def test_npmjs_observer_does_not_fetch_after_failed_qualification(
    qualified_simulation: QualifiedSimulation,
) -> None:
    transport = ScriptedTransport({})
    failed = replace(
        qualified_simulation.decision,
        terminal_result="failure",
        failure_class="quality-failure",
        next_action="fix-quality-failure-and-rerun",
    )

    with pytest.raises(ValueError, match="successful qualification"):
        observe_npmjs_projection(
            qualified_simulation.snapshot,
            failed,
            qualified_simulation.artifact,
            qualified_simulation.expectation,
            transport=transport,
        )

    assert transport.requests == []


def test_npmjs_observer_rejects_wrong_coordinate_before_network(
    qualified_simulation: QualifiedSimulation,
) -> None:
    projection = qualified_simulation.snapshot.destination_projections[0]
    coordinate = replace(
        projection.coordinate,
        package_name="@hcoona/other",
    )
    snapshot = replace(
        qualified_simulation.snapshot,
        destination_projections=(replace(projection, coordinate=coordinate),),
    )
    transport = ScriptedTransport({})

    with pytest.raises(ValueError, match=r"current|outside the first slice"):
        observe_npmjs_projection(
            snapshot,
            qualified_simulation.decision,
            qualified_simulation.artifact,
            qualified_simulation.expectation,
            transport=transport,
        )

    assert transport.requests == []


def test_qualification_snapshot_rejects_wrong_native_version(
    qualified_simulation: QualifiedSimulation,
) -> None:
    projection = qualified_simulation.snapshot.destination_projections[0]
    coordinate = replace(
        projection.coordinate,
        native_version="9.9.9",
    )

    with pytest.raises(
        ValueError,
        match="Qualification Snapshot projection version is not closed",
    ):
        replace(
            qualified_simulation.snapshot,
            destination_projections=(
                replace(projection, coordinate=coordinate),
            ),
        )


@pytest.mark.parametrize(
    "field", ["metadata_limit_bytes", "tarball_limit_bytes"]
)
@pytest.mark.parametrize("value", [0, -1, -2, True, False])
def test_npmjs_observer_rejects_invalid_size_limits_before_network(
    qualified_simulation: QualifiedSimulation,
    field: str,
    value: object,
) -> None:
    transport = ScriptedTransport({})

    with pytest.raises((TypeError, ValueError), match=r"integer|positive"):
        observe_npmjs_projection(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            qualified_simulation.artifact,
            qualified_simulation.expectation,
            transport=transport,
            **{field: value},  # type: ignore[bad-argument-type]
        )

    assert transport.requests == []


class _FakeResponse:
    def __init__(
        self,
        *,
        url: str,
        status: int,
        body: bytes,
        declared_length: int,
    ) -> None:
        self.url = url
        self.status = status
        self.body = body
        self.headers = Message()
        self.headers["Content-Length"] = str(declared_length)

    def __enter__(self) -> Self:
        return self

    def __exit__(self, *args: object) -> None:
        del args

    def read(self, amount: int) -> bytes:
        return self.body[:amount]


class _ScriptedOpener:
    def __init__(self, outcomes: list[object]) -> None:
        self.outcomes = outcomes
        self.requests: list[urllib.request.Request] = []

    def open(
        self,
        request: urllib.request.Request,
        *,
        timeout: float,
    ) -> object:
        del timeout
        self.requests.append(request)
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome


def _metadata_body(simulation: QualifiedSimulation) -> bytes:
    return _metadata_response(simulation).body


def test_stdlib_transport_ignores_inherited_proxy_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("HTTPS_PROXY", "http://user:secret@proxy.invalid:8080")
    monkeypatch.setenv("https_proxy", "http://user:secret@proxy.invalid:8080")
    opener = _ScriptedOpener(
        [
            _FakeResponse(
                url="https://registry.npmjs.org/test",
                status=200,
                body=b"{}",
                declared_length=2,
            )
        ]
    )
    handlers: tuple[object, ...] = ()

    def build_opener(*provided: object) -> _ScriptedOpener:
        nonlocal handlers
        handlers = provided
        return opener

    monkeypatch.setattr(urllib.request, "build_opener", build_opener)

    response = StdlibHttpTransport().get(
        "https://registry.npmjs.org/test",
        headers=(),
        timeout=1,
        max_bytes=10,
    )

    proxy_handlers = [
        handler
        for handler in handlers
        if isinstance(handler, urllib.request.ProxyHandler)
    ]
    assert response.body == b"{}"
    assert len(proxy_handlers) == 1
    proxy_handler = cast("Any", proxy_handlers[0])
    assert proxy_handler.proxies == {}
    assert opener.requests[0].get_header("Proxy-authorization") is None


@pytest.mark.parametrize("value", [0, -1, -2, True, False])
def test_stdlib_transport_rejects_invalid_limit_before_request(
    monkeypatch: pytest.MonkeyPatch,
    value: object,
) -> None:
    called = False

    def build_opener(*handlers: object) -> _ScriptedOpener:
        del handlers
        nonlocal called
        called = True
        return _ScriptedOpener([])

    monkeypatch.setattr(urllib.request, "build_opener", build_opener)

    with pytest.raises((TypeError, ValueError), match=r"integer|positive"):
        StdlibHttpTransport().get(
            "https://registry.npmjs.org/test",
            headers=(),
            timeout=1,
            max_bytes=value,  # type: ignore[bad-argument-type]
        )

    assert called is False


def test_stdlib_transport_detects_incomplete_http_error_body(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    headers = Message()
    headers["Content-Length"] = "10"
    error = urllib.error.HTTPError(
        "https://registry.npmjs.org/test",
        404,
        "not found",
        headers,
        io.BytesIO(b"short"),
    )
    monkeypatch.setattr(
        urllib.request,
        "build_opener",
        lambda *_handlers: _ScriptedOpener([error]),
    )

    with pytest.raises(NpmjsNetworkError, match="Content-Length"):
        StdlibHttpTransport().get(
            "https://registry.npmjs.org/test",
            headers=(),
            timeout=1,
            max_bytes=100,
        )


def test_npmjs_observer_maps_incomplete_metadata_body_to_unknown(
    qualified_simulation: QualifiedSimulation,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    body = _metadata_body(qualified_simulation)
    opener = _ScriptedOpener(
        [
            _FakeResponse(
                url=_metadata_url(qualified_simulation),
                status=200,
                body=body,
                declared_length=len(body) + 1,
            )
        ]
    )
    monkeypatch.setattr(
        urllib.request,
        "build_opener",
        lambda *_handlers: opener,
    )

    observation = observe_npmjs_projection(
        qualified_simulation.snapshot,
        qualified_simulation.decision,
        qualified_simulation.artifact,
        qualified_simulation.expectation,
        transport=StdlibHttpTransport(),
    )

    assert observation.value.classification == "unknown"
    assert observation.response_facts.status == "network-error"
    assert len(opener.requests) == 1


def test_npmjs_observer_maps_incomplete_matching_tarball_to_unknown(
    qualified_simulation: QualifiedSimulation,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    tarball = _tarball(qualified_simulation)
    artifact = _artifact_for_tarball(qualified_simulation, tarball)
    metadata_body = _metadata_body(qualified_simulation)
    tarball_url = (
        "https://registry.npmjs.org/@hcoona/hcoona-release-smoke-npm/-/pkg.tgz"
    )
    opener = _ScriptedOpener(
        [
            _FakeResponse(
                url=_metadata_url(qualified_simulation),
                status=200,
                body=metadata_body,
                declared_length=len(metadata_body),
            ),
            _FakeResponse(
                url=tarball_url,
                status=200,
                body=tarball,
                declared_length=len(tarball) + 1,
            ),
        ]
    )
    monkeypatch.setattr(
        urllib.request,
        "build_opener",
        lambda *_handlers: opener,
    )

    observation = observe_npmjs_projection(
        qualified_simulation.snapshot,
        _decision_for_artifact(qualified_simulation, artifact),
        artifact,
        qualified_simulation.expectation,
        transport=StdlibHttpTransport(),
    )

    assert observation.value.classification == "unknown"
    assert observation.response_facts.stage == "tarball"
    assert observation.response_facts.status == "network-error"
    expected_requests = 2
    assert len(opener.requests) == expected_requests
