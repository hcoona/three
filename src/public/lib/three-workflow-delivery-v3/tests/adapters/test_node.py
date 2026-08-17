"""Scenarios for first-slice isolated Node Build and Quality Adapters."""

from __future__ import annotations

# ruff: noqa: C901, D103, I001

import dataclasses
import gzip
import hashlib
import io
import inspect
import json
import os
import shutil
import subprocess
import tarfile
import types
from contextlib import contextmanager
from dataclasses import fields, replace
from inspect import signature
from pathlib import Path
from typing import TYPE_CHECKING, cast
from typing import get_type_hints

import pytest
import three_workflow_delivery_v3.adapters as adapters_package
import three_workflow_delivery_v3.adapters.node as node_adapter
from three_workflow_delivery_v3.adapters.node import (
    BuildRequest,
    PackageTargetWitness,
    RuntimeRequest,
    build_node_package,
    qualify_npm_artifact_contents,
    qualify_npm_install_import,
    run_node_project_build,
    run_node_project_tests,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.repository.node_provider import NbgvFacts

if TYPE_CHECKING:
    from collections.abc import Iterator
    from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[6]
PROJECT_ROOT = REPO_ROOT / "src/public/lib/hcoona-release-smoke-npm"
TARGET = "e" * 40
NPM_VERSION = "1.2.3-beta.42.ge123456"
DIGEST_A = "sha256:" + ("a" * 64)
DIGEST_B = "sha256:" + ("b" * 64)
PREFIXED_SHA256_LENGTH = 71
EXPECTED_TARGET_COMMAND_COUNT = 6
EXPECTED_ISOLATED_HOME_COUNT = 4
EXPECTED_IMPORT_COMMAND_ARG_COUNT = 4
NONZERO_PADDING_BYTE = 0xA5
TAR_CHECKSUM_SUFFIX_WIDTH = 2
FROZEN_TARBALL_BYTE_SIZE = 983
EXPECTED_FROZEN_TAR_MEMBER_COUNT = 4
DECLARED_INPUTS = (
    "README.md",
    "package.json",
    "scripts/build.mjs",
    "src/index.js",
)
TAR_HEADER_FIELDS = {
    "name": (0, 100),
    "mode": (100, 108),
    "uid": (108, 116),
    "gid": (116, 124),
    "size": (124, 136),
    "mtime": (136, 148),
    "checksum": (148, 156),
    "type": (156, 157),
    "linkname": (157, 257),
    "magic": (257, 263),
    "version": (263, 265),
    "uname": (265, 297),
    "gname": (297, 329),
    "devmajor": (329, 337),
    "devminor": (337, 345),
    "prefix": (345, 500),
    "reserved": (500, 512),
}


def _nbgv_facts() -> NbgvFacts:
    return NbgvFacts(
        canonical_version="1.2.3",
        sem_ver1="1.2.3-beta-42",
        sem_ver2=NPM_VERSION,
        version_height=42,
        git_commit_id=TARGET,
        public_release=False,
        npm_package_version=NPM_VERSION,
        node_api_result_digest=DIGEST_A,
    )


@pytest.fixture
def witness() -> PackageTargetWitness:
    """Return the canonical first-slice Package Target Witness."""
    return PackageTargetWitness(
        target=TARGET,
        release_unit="hcoona-release-smoke-npm",
        nbgv=_nbgv_facts(),
        build_definition="node/npm-package-v1",
        catalog_digest=DIGEST_A,
        control_digest=DIGEST_B,
        purpose="slice-validation",
    )


@pytest.fixture
def build_request(witness: PackageTargetWitness) -> BuildRequest:
    """Return a closed request using only declared project/build inputs."""
    return BuildRequest(
        source_root=PROJECT_ROOT,
        declared_inputs=DECLARED_INPUTS,
        npm_package_version=NPM_VERSION,
        witness=witness,
        source_date_epoch=1_700_000_000,
        node_version="24.14.0",
        pnpm_version="11.21.0",
        npm_version="11.9.0",
    )


@pytest.fixture(scope="module")
def built_result() -> node_adapter.BuildResult:
    """Build the real smoke package once for artifact quality scenarios."""
    witness = PackageTargetWitness(
        target=TARGET,
        release_unit="hcoona-release-smoke-npm",
        nbgv=_nbgv_facts(),
        build_definition="node/npm-package-v1",
        catalog_digest=DIGEST_A,
        control_digest=DIGEST_B,
        purpose="slice-validation",
    )
    return build_node_package(
        BuildRequest(
            source_root=PROJECT_ROOT,
            declared_inputs=DECLARED_INPUTS,
            npm_package_version=NPM_VERSION,
            witness=witness,
            source_date_epoch=1_700_000_000,
            node_version="24.14.0",
            pnpm_version="11.21.0",
            npm_version="11.9.0",
        )
    )


def _source_snapshot() -> dict[str, tuple[str, bytes | str]]:
    snapshot: dict[str, tuple[str, bytes | str]] = {}
    for path in sorted(PROJECT_ROOT.rglob("*")):
        relative_path = path.relative_to(PROJECT_ROOT)
        if "node_modules" in relative_path.parts:
            continue
        relative = relative_path.as_posix()
        if path.is_symlink():
            snapshot[relative] = ("symlink", path.readlink().as_posix())
        elif path.is_file():
            snapshot[relative] = ("file", path.read_bytes())
    return snapshot


def _tar_entries(tarball: bytes) -> dict[str, bytes]:
    with tarfile.open(fileobj=io.BytesIO(tarball), mode="r:gz") as archive:
        return {
            member.name: cast(
                "tarfile.ExFileObject", archive.extractfile(member)
            ).read()
            for member in archive.getmembers()
            if member.isfile()
        }


def _make_tarball(
    entries: dict[str, bytes],
    *,
    directories: tuple[str, ...] = (),
) -> bytes:
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w:gz") as archive:
        for name in sorted(directories):
            info = tarfile.TarInfo(name)
            info.type = tarfile.DIRTYPE
            info.mtime = 0
            info.mode = 0o755
            archive.addfile(info)
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
        if not member.isfile():
            continue
        header_start = member.offset
        header_end = header_start + tarfile.BLOCKSIZE
        header = bytearray(payload[header_start:header_end])
        header[0:100] = _nul_filled(member.name.encode(), 100)
        header[100:108] = b"000644 \0"
        header[108:116] = bytes(8)
        header[116:124] = bytes(8)
        header[124:136] = f"{member.size:010o} \0".encode()
        header[136:148] = f"{cast('int', member.mtime):010o} \0".encode()
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
        payload[header_start:header_end] = _tar_header_with_checksum(
            bytes(header)
        )
    data_end = max(
        (
            (member.offset_data + member.size + tarfile.BLOCKSIZE - 1)
            // tarfile.BLOCKSIZE
            * tarfile.BLOCKSIZE
            for member in members
        ),
        default=0,
    )
    closed_payload = payload[:data_end] + bytes(tarfile.BLOCKSIZE * 2)
    return gzip.compress(bytes(closed_payload), mtime=0)


def _nul_filled(value: bytes, width: int) -> bytes:
    assert len(value) < width
    return value + bytes(width - len(value))


def _tar_header_with_checksum(
    header: bytes,
    *,
    checksum_suffix: bytes = b" \0",
) -> bytes:
    assert len(header) == tarfile.BLOCKSIZE
    assert len(checksum_suffix) == TAR_CHECKSUM_SUFFIX_WIDTH
    mutated = bytearray(header)
    checksum_start, checksum_end = TAR_HEADER_FIELDS["checksum"]
    mutated[checksum_start:checksum_end] = b" " * 8
    checksum = sum(mutated)
    mutated[checksum_start:checksum_end] = (
        f"{checksum:06o}".encode() + checksum_suffix
    )
    return bytes(mutated)


def _tarball_with_first_header_fields(
    tarball: bytes,
    replacements: dict[str, bytes],
    *,
    checksum_suffix: bytes = b" \0",
) -> bytes:
    return _tarball_with_member_header_fields(
        tarball,
        0,
        replacements,
        checksum_suffix=checksum_suffix,
    )


def _tarball_with_member_header_fields(
    tarball: bytes,
    member_index: int,
    replacements: dict[str, bytes],
    *,
    checksum_suffix: bytes = b" \0",
) -> bytes:
    payload = bytearray(gzip.decompress(tarball))
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        members = archive.getmembers()
    member = members[member_index]
    header_start = member.offset
    header_end = header_start + tarfile.BLOCKSIZE
    header = bytearray(payload[header_start:header_end])
    for field, replacement in replacements.items():
        assert field != "checksum"
        start, end = TAR_HEADER_FIELDS[field]
        assert len(replacement) == end - start
        header[start:end] = replacement
    payload[header_start:header_end] = _tar_header_with_checksum(
        bytes(header),
        checksum_suffix=checksum_suffix,
    )
    return gzip.compress(bytes(payload), mtime=0)


def _tar_member_observables(tarball: bytes) -> tuple[tuple[object, ...], ...]:
    with tarfile.open(
        fileobj=io.BytesIO(gzip.decompress(tarball)),
        mode="r:",
    ) as archive:
        return tuple(
            (
                index,
                member.name,
                member.mode,
                member.uid,
                member.gid,
                member.size,
                member.mtime,
                member.type,
                member.linkname,
                member.uname,
                member.gname,
                member.devmajor,
                member.devminor,
                member.offset,
                member.offset_data,
            )
            for index, member in enumerate(archive.getmembers())
        )


def _physical_extension_prefix(
    extension_kind: str,
    insertion_member: tarfile.TarInfo,
) -> bytes:
    if extension_kind == "gnu-long-name":
        extension_content = insertion_member.name.encode() + b"\0"
        extension_info = tarfile.TarInfo("././@LongLink")
        extension_info.type = tarfile.GNUTYPE_LONGNAME
        extension_info.size = len(extension_content)
        extension_prefix = (
            extension_info.tobuf(format=tarfile.GNU_FORMAT) + extension_content
        )
        return extension_prefix + bytes(
            -len(extension_prefix) % tarfile.BLOCKSIZE
        )
    if extension_kind == "gnu-long-link":
        extension_content = b"unused-long-link-target\0"
        extension_info = tarfile.TarInfo("././@LongLink")
        extension_info.type = tarfile.GNUTYPE_LONGLINK
        extension_info.size = len(extension_content)
        extension_prefix = (
            extension_info.tobuf(format=tarfile.GNU_FORMAT) + extension_content
        )
        return extension_prefix + bytes(
            -len(extension_prefix) % tarfile.BLOCKSIZE
        )
    if extension_kind == "pax-extended":
        extension_info = tarfile.TarInfo(insertion_member.name)
        extension_info.pax_headers = {"path": insertion_member.name}
        extension_with_member_header = extension_info.tobuf(
            format=tarfile.PAX_FORMAT
        )
        return extension_with_member_header[: -tarfile.BLOCKSIZE]
    if extension_kind == "pax-solaris":
        extension_prefix = bytearray(
            _physical_extension_prefix("pax-extended", insertion_member)
        )
        type_start, type_end = TAR_HEADER_FIELDS["type"]
        extension_prefix[type_start:type_end] = tarfile.SOLARIS_XHDTYPE
        extension_prefix[: tarfile.BLOCKSIZE] = _tar_header_with_checksum(
            bytes(extension_prefix[: tarfile.BLOCKSIZE])
        )
        return bytes(extension_prefix)

    assert extension_kind == "pax-global"
    return tarfile.TarInfo.create_pax_global_header(
        {"comment": "physical-extension-padding-probe"}
    )


def _special_tar_header(payload: bytes, type_flag: bytes) -> bytes:
    assert len(type_flag) == 1
    header = bytearray(payload[: tarfile.BLOCKSIZE])
    name_start, name_end = TAR_HEADER_FIELDS["name"]
    header[name_start:name_end] = _nul_filled(
        b"package/special-entry",
        name_end - name_start,
    )
    size_start, size_end = TAR_HEADER_FIELDS["size"]
    header[size_start:size_end] = b"0000000000 \0"
    type_start, type_end = TAR_HEADER_FIELDS["type"]
    header[type_start:type_end] = type_flag
    link_start, link_end = TAR_HEADER_FIELDS["linkname"]
    linkname = (
        b"package/dist/index.js"
        if type_flag in (tarfile.LNKTYPE, tarfile.SYMTYPE)
        else b""
    )
    header[link_start:link_end] = _nul_filled(
        linkname,
        link_end - link_start,
    )
    return _tar_header_with_checksum(bytes(header))


def _current_process_umask() -> int:
    current = os.umask(0)
    os.umask(current)
    return current


@contextmanager
def _temporary_process_umask(mask: int) -> Iterator[None]:
    previous = os.umask(mask)
    try:
        yield
    finally:
        os.umask(previous)


def _copy_declared_project_inputs(destination_root: Path) -> None:
    for relative in DECLARED_INPUTS:
        source = PROJECT_ROOT / relative
        destination = destination_root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)


def _make_runtime_request(
    *,
    node_version: str,
    npm_version: str,
) -> Any:
    runtime_request_type = getattr(node_adapter, "RuntimeRequest", None)
    assert runtime_request_type is not None, (
        "node adapter must define RuntimeRequest for quality operations"
    )
    return runtime_request_type(
        node_version=node_version,
        npm_version=npm_version,
    )


def test_source_snapshot_covers_complete_fixture_project() -> None:
    assert set(_source_snapshot()) == {
        "README.md",
        "dist/index.js",
        "package.json",
        "scripts/build.mjs",
        "scripts/nbgv-version.mjs",
        "src/index.js",
        "test/index.test.js",
        "three.release.yml",
        "version.json",
        "workflow-delivery.quality.yml",
        "workflow-delivery.release-unit.yml",
    }


def test_package_target_witness_is_canonical_and_execution_independent(
    witness: PackageTargetWitness,
) -> None:
    """Pin every normative witness binding and exclusion."""
    document = witness.to_document()

    assert witness.canonical_bytes == canonicalize(document)
    assert document == {
        "schema": "workflow-delivery/v3/package-target-witness",
        "target": TARGET,
        "release-unit": "hcoona-release-smoke-npm",
        "nbgv": witness.nbgv.to_document(),
        "build-definition": "node/npm-package-v1",
        "catalog-digest": DIGEST_A,
        "control-digest": DIGEST_B,
        "purpose": "slice-validation",
    }
    serialized = witness.canonical_bytes.decode()
    assert "run-id" not in serialized
    assert "run-attempt" not in serialized
    assert "attempt-id" not in serialized


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("target", "E" * 40, "full lowercase SHA"),
        ("release_unit", "", "nonempty string"),
        ("release_unit", "adjacent-package", "release-unit"),
        ("build_definition", "", "nonempty string"),
        ("build_definition", "node/other", "build-definition"),
        ("catalog_digest", "sha256:abc", "SHA-256 digest"),
        ("control_digest", "b" * 64, "SHA-256 digest"),
        ("purpose", "publication", "unsupported"),
    ],
)
def test_package_target_witness_rejects_invalid_binding_matrix(
    witness: PackageTargetWitness,
    field: str,
    value: str,
    message: str,
) -> None:
    mutated = replace(witness, **cast("Any", {field: value}))

    with pytest.raises((TypeError, ValueError), match=message):
        _ = mutated.canonical_bytes


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (("README.md", "../package.json"), "unsafe"),
        (("package.json", "package.json", "scripts/build.mjs"), "duplicates"),
        (("README.md", "src/index.js"), "build closure"),
        ((*DECLARED_INPUTS, "undeclared.txt"), "build closure"),
    ],
)
def test_build_rejects_unsafe_or_incomplete_declared_inputs_before_execution(
    build_request: BuildRequest,
    mutation: tuple[str, ...],
    message: str,
) -> None:
    with pytest.raises(ValueError, match=message):
        build_node_package(replace(build_request, declared_inputs=mutation))


@pytest.mark.parametrize(
    "version",
    ["", "0.0.0-placeholder", "9.9.9"],
)
def test_build_rejects_missing_placeholder_or_inconsistent_frozen_version(
    build_request: BuildRequest,
    version: str,
) -> None:
    with pytest.raises(ValueError, match="frozen npmPackageVersion"):
        build_node_package(replace(build_request, npm_package_version=version))


def test_build_rejects_non_first_slice_package_identity_before_build(
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    project = tmp_path / "project"
    _copy_declared_project_inputs(project)
    manifest_path = project / "package.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["name"] = "@hcoona/adjacent"
    manifest_path.write_text(f"{json.dumps(manifest, indent=2)}\n")

    def no_build_runner(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del cwd, environment
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, "v24.14.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.21.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.9.0\n", "")
        pytest.fail(
            f"unexpected build command after identity rejection: {command}"
        )

    monkeypatch.setattr(node_adapter, "_run", no_build_runner)

    with pytest.raises(ValueError, match="first-slice npm package"):
        build_node_package(replace(build_request, source_root=project))


def test_build_rejects_outside_root_symlink_before_read_copy_or_runner(
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    project = tmp_path / "project"
    _copy_declared_project_inputs(project)
    outside = tmp_path / "outside.txt"
    outside.write_text(
        "outside-root secret must not be read\n", encoding="utf-8"
    )
    (project / "README.md").unlink()
    (project / "README.md").symlink_to(outside)

    def fail_on_runner(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del cwd, environment
        pytest.fail(
            f"unexpected runner command before symlink rejection: {command}"
        )

    monkeypatch.setattr(node_adapter, "_run", fail_on_runner)

    with pytest.raises(ValueError, match="regular source file"):
        build_node_package(replace(build_request, source_root=project))


@pytest.mark.parametrize(
    "files",
    [
        ["README.md"],
        ["dist"],
        ["dist", "README.md", "README.md"],
        ["dist", "README.md", "extra"],
        ["dist", "README.md", "workflow-delivery/provenance.json"],
    ],
)
def test_build_rejects_non_exact_source_package_files_allowlist(
    build_request: BuildRequest,
    tmp_path: Path,
    files: list[str],
) -> None:
    """Reject malformed source package file allowlists."""
    project = tmp_path / "project"
    for relative in DECLARED_INPUTS:
        source = PROJECT_ROOT / relative
        destination = project / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    manifest_path = project / "package.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["files"] = files
    manifest_path.write_text(f"{json.dumps(manifest, indent=2)}\n")

    with pytest.raises(ValueError, match="files"):
        build_node_package(
            replace(
                build_request,
                source_root=project,
                pnpm_version="11.21.0",
            )
        )


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("node_version", "0.0.0", "Node version differs"),
        ("pnpm_version", "0.0.0", "PNPM version differs"),
        ("npm_version", "0.0.0", "npm version differs"),
    ],
)
def test_build_rejects_runtime_toolchain_mismatch(
    build_request: BuildRequest,
    field: str,
    value: str,
    message: str,
) -> None:
    with pytest.raises(ValueError, match=message):
        build_node_package(
            replace(build_request, **cast("Any", {field: value}))
        )


def test_adapter_identity_is_pinned_and_not_request_forgeable(
    build_request: BuildRequest,
    built_result: node_adapter.BuildResult,
) -> None:
    assert "adapter_version" not in {
        field.name for field in fields(BuildRequest)
    }
    with pytest.raises(TypeError, match="adapter_version"):
        cast("Any", replace)(
            build_request,
            adapter_version="forged/adapter-v99",
        )
    assert ("adapter", "node/npm-package-v1") in built_result.toolchain


def test_build_is_deterministic_and_preserves_source_checkout(
    build_request: BuildRequest,
) -> None:
    """Pin two builds' bytes, hashes, manifest, and source preservation."""
    before = _source_snapshot()

    first = build_node_package(build_request)
    second = build_node_package(build_request)

    assert first.tarball == second.tarball
    assert first.manifest.sha256 == (
        "sha256:" + hashlib.sha256(first.tarball).hexdigest()
    )
    assert first.manifest.sha512 == (
        "sha512:" + hashlib.sha512(first.tarball).hexdigest()
    )
    assert first.manifest.entries == (
        "package/README.md",
        "package/dist/index.js",
        "package/package.json",
        "package/workflow-delivery/provenance.json",
    )
    assert first.manifest.lifecycle_scripts == (
        (
            "build",
            "node ./scripts/nbgv-version.mjs stamp && node ./scripts/build.mjs",
        ),
        ("postpack", "node ./scripts/nbgv-version.mjs reset"),
        ("prepack", "node ./scripts/nbgv-version.mjs stamp"),
        ("test", "node --test"),
        ("version:reset", "node ./scripts/nbgv-version.mjs reset"),
        ("version:stamp", "node ./scripts/nbgv-version.mjs stamp"),
    )
    assert first.expectation.files_allowlist == (
        "dist",
        "README.md",
        "workflow-delivery/provenance.json",
    )
    assert first.witness == build_request.witness.canonical_bytes
    assert first.toolchain == (
        ("node", "24.14.0"),
        ("pnpm", "11.21.0"),
        ("npm", "11.9.0"),
        ("adapter", "node/npm-package-v1"),
    )
    assert tuple(path for path, _digest in first.source_input_manifest) == (
        DECLARED_INPUTS
    )
    assert all(
        digest.startswith("sha256:") and len(digest) == PREFIXED_SHA256_LENGTH
        for _path, digest in first.source_input_manifest
    )
    assert _source_snapshot() == before


def test_build_is_deterministic_across_process_umasks_and_normalizes_modes(
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    source_manifest = json.loads(
        (build_request.source_root / "package.json").read_text()
    )
    assert source_manifest["version"] == "0.0.0-placeholder"

    original_run = node_adapter._run  # noqa: SLF001
    active_umask: int | None = None
    staged_modes: dict[int, dict[str, str]] = {}

    def record_staged_modes_and_run(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        if command[:2] == ("npm", "pack"):
            assert active_umask is not None
            paths = {
                "directory:stage": cwd,
                "directory:build-output": cwd / "dist",
                "directory:declared-scripts": cwd / "scripts",
                "directory:declared-src": cwd / "src",
                "directory:witness": cwd / "workflow-delivery",
                "regular:README.md": cwd / "README.md",
                "regular:build-output": cwd / "dist/index.js",
                "regular:build-script": cwd / "scripts/build.mjs",
                "regular:manifest": cwd / "package.json",
                "regular:source": cwd / "src/index.js",
                "regular:witness": (cwd / "workflow-delivery/provenance.json"),
            }
            staged_modes[active_umask] = {
                label: f"{path.stat().st_mode & 0o777:04o}"
                for label, path in paths.items()
            }
        return original_run(command, cwd, environment)

    monkeypatch.setattr(
        node_adapter,
        "_run",
        record_staged_modes_and_run,
    )
    initial_umask = _current_process_umask()
    results: dict[int, node_adapter.BuildResult] = {}
    umask_restored: dict[int, bool] = {}
    try:
        for mask in (0o022, 0o077):
            active_umask = mask
            with _temporary_process_umask(mask):
                assert _current_process_umask() == mask
                results[mask] = build_node_package(build_request)
            umask_restored[mask] = _current_process_umask() == initial_umask
    finally:
        os.umask(initial_umask)

    packed_modes: dict[int, dict[str, tuple[str, str]]] = {}
    packed_executables: dict[int, tuple[str, ...]] = {}
    for mask, result in results.items():
        with tarfile.open(
            fileobj=io.BytesIO(result.tarball),
            mode="r:gz",
        ) as archive:
            members = archive.getmembers()
        packed_modes[mask] = {
            member.name: (
                "directory" if member.isdir() else "regular",
                f"{member.mode & 0o777:04o}",
            )
            for member in members
        }
        packed_executables[mask] = tuple(
            member.name
            for member in members
            if member.isfile() and member.mode & 0o111
        )

    permissive = results[0o022]
    restrictive = results[0o077]
    expected_staged_modes = {
        "directory:stage": "0755",
        "directory:build-output": "0755",
        "directory:declared-scripts": "0755",
        "directory:declared-src": "0755",
        "directory:witness": "0755",
        "regular:README.md": "0644",
        "regular:build-output": "0644",
        "regular:build-script": "0644",
        "regular:manifest": "0644",
        "regular:source": "0644",
        "regular:witness": "0644",
    }
    expected_packed_modes = dict.fromkeys(
        permissive.manifest.entries,
        ("regular", "0644"),
    )
    permissive_sha256 = permissive.manifest.sha256
    permissive_sha512 = permissive.manifest.sha512
    permissive_byte_size = permissive.manifest.byte_size
    evidence = {
        "tarball-bytes-identical": (permissive.tarball == restrictive.tarball),
        "tarball-byte-sizes": {
            mask: result.manifest.byte_size for mask, result in results.items()
        },
        "sha256-values": {
            mask: result.manifest.sha256 for mask, result in results.items()
        },
        "sha512-values": {
            mask: result.manifest.sha512 for mask, result in results.items()
        },
        "sha256-identical": (
            permissive.manifest.sha256 == restrictive.manifest.sha256
        ),
        "sha512-identical": (
            permissive.manifest.sha512 == restrictive.manifest.sha512
        ),
        "sha256-binds-exact-bytes": {
            mask: result.manifest.sha256
            == f"sha256:{hashlib.sha256(result.tarball).hexdigest()}"
            for mask, result in results.items()
        },
        "sha512-binds-exact-bytes": {
            mask: result.manifest.sha512
            == f"sha512:{hashlib.sha512(result.tarball).hexdigest()}"
            for mask, result in results.items()
        },
        "staged-modes": staged_modes,
        "packed-member-modes": packed_modes,
        "packed-executables": packed_executables,
        "umask-restored": umask_restored,
    }
    assert evidence == {
        "tarball-bytes-identical": True,
        "tarball-byte-sizes": {
            0o022: permissive_byte_size,
            0o077: permissive_byte_size,
        },
        "sha256-values": {
            0o022: permissive_sha256,
            0o077: permissive_sha256,
        },
        "sha512-values": {
            0o022: permissive_sha512,
            0o077: permissive_sha512,
        },
        "sha256-identical": True,
        "sha512-identical": True,
        "sha256-binds-exact-bytes": {0o022: True, 0o077: True},
        "sha512-binds-exact-bytes": {0o022: True, 0o077: True},
        "staged-modes": {
            0o022: expected_staged_modes,
            0o077: expected_staged_modes,
        },
        "packed-member-modes": {
            0o022: expected_packed_modes,
            0o077: expected_packed_modes,
        },
        "packed-executables": {0o022: (), 0o077: ()},
        "umask-restored": {0o022: True, 0o077: True},
    }


def test_lifecycle_evidence_binds_every_manifest_script(
    build_request: BuildRequest,
    tmp_path: Path,
) -> None:
    project = tmp_path / "project"
    _copy_declared_project_inputs(project)
    manifest_path = project / "package.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["scripts"].update(
        {
            "dependencies": 'node -e "process.exit(90)"',
            "preprepare": 'node -e "process.exit(91)"',
            "postprepare": 'node -e "process.exit(92)"',
        }
    )
    manifest_path.write_text(f"{json.dumps(manifest, indent=2)}\n")
    expected_scripts = tuple(sorted(manifest["scripts"].items()))

    result = build_node_package(
        replace(
            build_request,
            source_root=project,
            pnpm_version="11.21.0",
        ),
    )

    assert result.expectation.lifecycle_scripts == expected_scripts
    assert result.manifest.lifecycle_scripts == expected_scripts
    assert (
        qualify_npm_artifact_contents(
            result.tarball,
            result.expectation,
        ).lifecycle_scripts
        == expected_scripts
    )


def test_project_build_uses_isolated_inputs_and_preserves_source(
    build_request: BuildRequest,
) -> None:
    before = _source_snapshot()

    run_node_project_build(build_request)

    assert _source_snapshot() == before


@pytest.mark.parametrize("failure", ["build", "pack", "test", "install"])
def test_failure_paths_preserve_complete_source_checkout(
    failure: str,
    build_request: BuildRequest,
    built_result: node_adapter.BuildResult,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    before = _source_snapshot()
    failed_command: list[tuple[str, ...]] = []
    observed_commands: list[tuple[str, ...]] = []

    def fail_selected_command(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del cwd, environment
        observed_commands.append(command)
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, "v24.14.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.21.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.9.0\n", "")
        is_selected = (
            (
                failure == "build"
                and command[:2] == ("node", "scripts/build.mjs")
            )
            or (failure == "pack" and command[:2] == ("npm", "pack"))
            or (failure == "test" and command[:2] == ("npm", "test"))
            or (failure == "install" and command[:2] == ("npm", "install"))
        )
        if is_selected:
            failed_command.append(command)
            raise subprocess.CalledProcessError(7, command)
        if command[:2] == ("node", "scripts/build.mjs"):
            return subprocess.CompletedProcess(command, 0, "", "")
        pytest.fail(
            f"unexpected command before injected {failure} failure: {command}"
        )

    monkeypatch.setattr(node_adapter, "_run", fail_selected_command)

    def invoke_failure() -> None:
        if failure in {"build", "pack"}:
            build_node_package(build_request)
        elif failure == "test":
            run_node_project_tests(
                PROJECT_ROOT,
                _make_runtime_request(
                    node_version=f"v{build_request.node_version}",
                    npm_version=build_request.npm_version,
                ),
            )
        else:
            qualify_npm_install_import(
                built_result.tarball,
                built_result.expectation,
                _make_runtime_request(
                    node_version=f"v{build_request.node_version}",
                    npm_version=build_request.npm_version,
                ),
            )

    with pytest.raises(subprocess.CalledProcessError):
        invoke_failure()
    assert len(failed_command) == 1
    expected_commands = {
        "build": [
            ("node", "--version"),
            ("pnpm", "--version"),
            ("npm", "--version"),
            ("node", "scripts/build.mjs"),
        ],
        "pack": [
            ("node", "--version"),
            ("pnpm", "--version"),
            ("npm", "--version"),
            ("node", "scripts/build.mjs"),
            (
                "npm",
                "pack",
                "--ignore-scripts",
                "--json",
                "--pack-destination",
                observed_commands[-1][-1],
            ),
        ],
        "test": [
            ("node", "--version"),
            ("npm", "--version"),
            ("npm", "test", "--ignore-scripts"),
        ],
        "install": [
            ("node", "--version"),
            ("npm", "--version"),
            (
                "npm",
                "install",
                "--ignore-scripts",
                "--no-audit",
                "--no-fund",
                "--package-lock=false",
                observed_commands[-1][-1],
            ),
        ],
    }
    assert observed_commands == expected_commands[failure]
    assert _source_snapshot() == before


def test_project_test_adapter_uses_isolated_stage_and_minimal_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    observed: list[
        tuple[
            tuple[str, ...],
            Path,
            dict[str, str],
            tuple[str, ...],
            str,
            str | None,
            bool,
            bytes | None,
        ]
    ] = []
    runtime_request = _make_runtime_request(
        node_version="v24.14.0",
        npm_version="11.9.0",
    )

    def record(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        global_config_value = environment.get("NPM_CONFIG_GLOBALCONFIG")
        global_config_path = (
            Path(global_config_value)
            if global_config_value is not None
            else None
        )
        global_config_exists = (
            global_config_path is not None and global_config_path.is_file()
        )
        observed.append(
            (
                command,
                cwd,
                dict(environment),
                tuple(
                    sorted(
                        path.relative_to(cwd).as_posix()
                        for path in cwd.rglob("*")
                        if path.is_file()
                    )
                ),
                Path(environment["NPM_CONFIG_USERCONFIG"]).read_text(),
                global_config_value,
                global_config_exists,
                (
                    global_config_path.read_bytes()
                    if global_config_exists and global_config_path is not None
                    else None
                ),
            )
        )
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, "v24.14.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.9.0\n", "")
        assert command == ("npm", "test", "--ignore-scripts")
        return subprocess.CompletedProcess(command, 0, "passed", "")

    monkeypatch.setenv("NODE_AUTH_TOKEN", "secret")
    monkeypatch.setenv("NPM_TOKEN", "secret")
    monkeypatch.setenv("GITHUB_TOKEN", "secret")
    monkeypatch.setenv("UNRELATED_SENTINEL", "ambient")
    monkeypatch.setattr(node_adapter, "_run", record)

    run_node_project_tests(PROJECT_ROOT, runtime_request)

    assert [command for command, *_ in observed] == [
        ("node", "--version"),
        ("npm", "--version"),
        ("npm", "test", "--ignore-scripts"),
    ]
    expected_staged_files = (
        "package.json",
        "src/index.js",
        "test/index.test.js",
    )
    expected_environment_keys = {
        "HOME",
        "LANG",
        "LC_ALL",
        "NPM_CONFIG_CACHE",
        "NPM_CONFIG_GLOBALCONFIG",
        "NPM_CONFIG_USERCONFIG",
        "PATH",
        "TZ",
        "XDG_CONFIG_HOME",
    }
    expected_npm_config = (
        "audit=false\n"
        "fund=false\n"
        "ignore-scripts=true\n"
        "package-lock=false\n"
        "update-notifier=false\n"
    )
    for (
        _command,
        cwd,
        environment,
        staged_files,
        npm_config,
        global_config_value,
        global_config_exists,
        global_config_bytes,
    ) in observed:
        assert not cwd.is_relative_to(PROJECT_ROOT.resolve())
        assert staged_files == expected_staged_files
        assert set(environment) == expected_environment_keys
        assert all(
            secret not in environment
            for secret in (
                "GITHUB_TOKEN",
                "NODE_AUTH_TOKEN",
                "NPM_TOKEN",
                "UNRELATED_SENTINEL",
            )
        )
        assert environment["LANG"] == "C.UTF-8"
        assert environment["LC_ALL"] == "C.UTF-8"
        assert environment["TZ"] == "UTC"
        home = Path(environment["HOME"])
        assert Path(environment["NPM_CONFIG_USERCONFIG"]) == home / "npmrc"
        assert Path(environment["NPM_CONFIG_CACHE"]) == home / "npm-cache"
        assert Path(environment["XDG_CONFIG_HOME"]) == home / "config"
        assert npm_config == expected_npm_config
        assert global_config_value is not None
        global_config_path = Path(global_config_value)
        assert global_config_path.is_relative_to(home.parent)
        assert global_config_path != Path(environment["NPM_CONFIG_USERCONFIG"])
        assert global_config_exists
        assert global_config_bytes == b""
    assert all(
        environment == observed[0][2] for _, _, environment, *_ in observed
    )


def test_target_controlled_commands_use_minimal_isolated_environments(  # noqa: PLR0915
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    original_run = node_adapter._run  # noqa: SLF001
    runtime_request = _make_runtime_request(
        node_version=f"v{build_request.node_version}",
        npm_version=build_request.npm_version,
    )
    observations: list[
        tuple[
            tuple[str, ...],
            Path,
            dict[str, str],
            str,
            str | None,
            bool,
            bytes | None,
        ]
    ] = []
    for name in (
        "AWS_SECRET_ACCESS_KEY",
        "GITHUB_TOKEN",
        "HOME",
        "NPM_CONFIG_GLOBALCONFIG",
        "NPM_CONFIG_USERCONFIG",
        "NODE_AUTH_TOKEN",
        "NPM_TOKEN",
        "UNRELATED_SENTINEL",
    ):
        monkeypatch.setenv(name, "ambient-secret")

    def record_and_run(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        config_path = Path(environment["NPM_CONFIG_USERCONFIG"])
        global_config_value = environment.get("NPM_CONFIG_GLOBALCONFIG")
        global_config_path = (
            None if global_config_value is None else Path(global_config_value)
        )
        global_config_exists = (
            global_config_path is not None and global_config_path.is_file()
        )
        observations.append(
            (
                command,
                cwd,
                dict(environment),
                config_path.read_text(),
                global_config_value,
                global_config_exists,
                (
                    global_config_path.read_bytes()
                    if global_config_exists and global_config_path is not None
                    else None
                ),
            )
        )
        return original_run(command, cwd, environment)

    monkeypatch.setattr(node_adapter, "_run", record_and_run)

    built_result = build_node_package(build_request)
    run_node_project_build(build_request)
    run_node_project_tests(PROJECT_ROOT, runtime_request)
    result = qualify_npm_install_import(
        built_result.tarball,
        built_result.expectation,
        runtime_request,
    )

    missing_global_config = [
        command
        for command, _, _, _, value, _, _ in observations
        if value is None
    ]
    assert not missing_global_config, (
        "NPM_CONFIG_GLOBALCONFIG missing for target-controlled commands: "
        f"{missing_global_config!r}"
    )
    for (
        command,
        _cwd,
        environment,
        _npm_config,
        global_config_value,
        global_config_exists,
        global_config_bytes,
    ) in observations:
        assert global_config_value is not None
        assert global_config_value != "ambient-secret", (
            f"{command!r} inherited ambient NPM_CONFIG_GLOBALCONFIG"
        )
        global_config_path = Path(global_config_value)
        assert global_config_path.is_relative_to(
            Path(environment["HOME"]).parent
        ), (
            f"{command!r} global npm config is outside isolated state: "
            f"{global_config_path}"
        )
        assert global_config_exists, (
            f"{command!r} global npm config did not exist before execution: "
            f"{global_config_path}"
        )
        assert global_config_bytes == b"", (
            f"{command!r} global npm config was not empty: "
            f"{global_config_bytes!r}"
        )

    assert [command[:2] for command, *_ in observations] == [
        ("node", "--version"),
        ("pnpm", "--version"),
        ("npm", "--version"),
        ("node", "scripts/build.mjs"),
        ("npm", "pack"),
        ("node", "--version"),
        ("pnpm", "--version"),
        ("npm", "--version"),
        ("node", "scripts/build.mjs"),
        ("node", "--version"),
        ("npm", "--version"),
        ("npm", "test"),
        ("node", "--version"),
        ("npm", "--version"),
        ("npm", "install"),
        ("node", "--input-type=module"),
    ]
    artifact_build = observations[3]
    artifact_pack = observations[4]
    assert artifact_build[0] == ("node", "scripts/build.mjs")
    assert artifact_pack[0][:3] == ("npm", "pack", "--ignore-scripts")
    assert artifact_build[1] == artifact_pack[1]
    assert artifact_build[2] == artifact_pack[2]
    assert result.smoke_message == "hcoona-release-smoke-npm"
    homes = {environment["HOME"] for _, _, environment, *_ in observations}
    assert "ambient-secret" not in homes
    assert len(homes) == EXPECTED_ISOLATED_HOME_COUNT
    safe_environment_keys = {
        "HOME",
        "LANG",
        "LC_ALL",
        "NPM_CONFIG_CACHE",
        "NPM_CONFIG_GLOBALCONFIG",
        "NPM_CONFIG_USERCONFIG",
        "PATH",
        "TZ",
        "XDG_CONFIG_HOME",
    }
    operation_indexes = {3, 4, 8, 11, 14, 15}
    source_date_command_count = 9
    for index, (
        _command,
        cwd,
        environment,
        npm_config,
        _global_config_value,
        _global_config_exists,
        _global_config_bytes,
    ) in enumerate(observations):
        if index in operation_indexes:
            assert not cwd.is_relative_to(PROJECT_ROOT.resolve())
        expected_keys = safe_environment_keys
        if index < source_date_command_count:
            expected_keys = {*expected_keys, "SOURCE_DATE_EPOCH"}
            assert environment["SOURCE_DATE_EPOCH"] == str(
                build_request.source_date_epoch
            )
        assert set(environment) == expected_keys
        assert all(
            name not in environment
            for name in (
                "AWS_SECRET_ACCESS_KEY",
                "GITHUB_TOKEN",
                "NODE_AUTH_TOKEN",
                "NPM_TOKEN",
                "UNRELATED_SENTINEL",
            )
        )
        home = Path(environment["HOME"])
        assert environment["LANG"] == "C.UTF-8"
        assert environment["LC_ALL"] == "C.UTF-8"
        assert environment["TZ"] == "UTC"
        assert Path(environment["NPM_CONFIG_USERCONFIG"]) == home / "npmrc"
        assert Path(environment["NPM_CONFIG_CACHE"]) == home / "npm-cache"
        assert Path(environment["XDG_CONFIG_HOME"]) == home / "config"
        assert npm_config == (
            "audit=false\n"
            "fund=false\n"
            "ignore-scripts=true\n"
            "package-lock=false\n"
            "update-notifier=false\n"
        )

    for group_indexes in (
        range(5),
        range(5, 9),
        range(9, 12),
        range(12, 16),
    ):
        group = [observations[index] for index in group_indexes]
        operation_environment = group[-1][2]
        assert all(
            environment == operation_environment
            for _, _, environment, *_ in group
        )


def test_artifact_contents_accepts_exact_tarball(
    built_result: node_adapter.BuildResult,
) -> None:
    manifest = qualify_npm_artifact_contents(
        built_result.tarball,
        built_result.expectation,
    )

    assert manifest == built_result.manifest
    assert manifest.byte_size == len(built_result.tarball)
    assert manifest.basename == (
        "hcoona-hcoona-release-smoke-npm-1.2.3-beta.42.ge123456.tgz"
    )


def test_artifact_contents_rejects_non_first_slice_expectation_identity(
    built_result: node_adapter.BuildResult,
) -> None:
    expectation = replace(
        built_result.expectation,
        package_name="@hcoona/adjacent",
    )

    with pytest.raises(ValueError, match="first-slice npm package"):
        qualify_npm_artifact_contents(built_result.tarball, expectation)


@pytest.mark.parametrize(
    "mutation",
    [
        "missing-witness",
        "altered-witness",
        "misplaced-witness",
        "sidecar-only",
        "extra-entry",
        "wrong-name",
        "wrong-version",
        "dropped-files-entry",
        "duplicate-files-entry",
        "extra-files-entry",
        "changed-lifecycle-script",
    ],
)
def test_artifact_contents_rejects_strict_negative_matrix(
    built_result: node_adapter.BuildResult,
    mutation: str,
) -> None:
    entries = _tar_entries(built_result.tarball)
    manifest = json.loads(entries["package/package.json"])
    witness_path = "package/workflow-delivery/provenance.json"
    if mutation == "missing-witness":
        entries.pop(witness_path)
    elif mutation == "altered-witness":
        entries[witness_path] = entries[witness_path].replace(
            TARGET.encode(), b"f" * 40
        )
    elif mutation == "misplaced-witness":
        entries["package/provenance.json"] = entries.pop(witness_path)
    elif mutation == "sidecar-only":
        entries["package/workflow-delivery/provenance.json.sha256"] = (
            hashlib.sha256(entries.pop(witness_path)).hexdigest().encode()
        )
    elif mutation == "extra-entry":
        entries["package/undeclared.txt"] = b"undeclared"
    elif mutation == "wrong-name":
        manifest["name"] = "@hcoona/other"
    elif mutation == "wrong-version":
        manifest["version"] = "9.9.9"
    elif mutation == "dropped-files-entry":
        manifest["files"].pop(0)
    elif mutation == "duplicate-files-entry":
        manifest["files"].append("dist")
    elif mutation == "extra-files-entry":
        manifest["files"].append("extra")
    else:
        manifest["scripts"]["prepack"] = "node malicious.mjs"
    entries["package/package.json"] = (
        f"{json.dumps(manifest, indent=2)}\n".encode()
    )

    with pytest.raises(ValueError, match="mismatch"):
        qualify_npm_artifact_contents(
            _make_tarball(entries),
            built_result.expectation,
        )


def test_artifact_contents_rejects_noncanonical_witness(
    built_result: node_adapter.BuildResult,
) -> None:
    entries = _tar_entries(built_result.tarball)
    witness_path = "package/workflow-delivery/provenance.json"
    noncanonical = json.dumps(
        json.loads(entries[witness_path]),
        indent=2,
    ).encode()
    entries[witness_path] = noncanonical
    expectation = replace(built_result.expectation, witness_bytes=noncanonical)

    with pytest.raises(ValueError, match="not canonical"):
        qualify_npm_artifact_contents(_make_tarball(entries), expectation)


def test_artifact_contents_rejects_explicit_directory_member(
    built_result: node_adapter.BuildResult,
) -> None:
    entries = _tar_entries(built_result.tarball)
    tarball = _make_tarball(
        entries,
        directories=("package/undeclared-directory/",),
    )

    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(tarball, built_result.expectation)


@pytest.mark.parametrize(
    ("witness_bytes", "message"),
    [
        (canonicalize({}), "schema keys mismatch"),
        (
            canonicalize(
                {
                    "schema": "workflow-delivery/v3/other",
                    "target": TARGET,
                    "release-unit": "hcoona-release-smoke-npm",
                    "nbgv": _nbgv_facts().to_document(),
                    "build-definition": "node/npm-package-v1",
                    "catalog-digest": DIGEST_A,
                    "control-digest": DIGEST_B,
                    "purpose": "slice-validation",
                }
            ),
            "schema mismatch",
        ),
        (
            canonicalize(
                {
                    "schema": "workflow-delivery/v3/package-target-witness",
                    "target": TARGET,
                    "release-unit": "adjacent-package",
                    "nbgv": _nbgv_facts().to_document(),
                    "build-definition": "node/npm-package-v1",
                    "catalog-digest": DIGEST_A,
                    "control-digest": DIGEST_B,
                    "purpose": "slice-validation",
                }
            ),
            "release-unit",
        ),
    ],
)
def test_artifact_contents_rejects_arbitrary_canonical_witness_documents(
    built_result: node_adapter.BuildResult,
    witness_bytes: bytes,
    message: str,
) -> None:
    entries = _tar_entries(built_result.tarball)
    entries["package/workflow-delivery/provenance.json"] = witness_bytes
    expectation = replace(built_result.expectation, witness_bytes=witness_bytes)

    with pytest.raises(ValueError, match=message):
        qualify_npm_artifact_contents(_make_tarball(entries), expectation)


def test_artifact_contents_rejects_list_backed_expectation(
    built_result: node_adapter.BuildResult,
) -> None:
    expectation = replace(
        built_result.expectation,
        files_allowlist=cast("tuple[str, ...]", ["dist", "README.md"]),
    )

    with pytest.raises(ValueError, match="first-slice closure"):
        qualify_npm_artifact_contents(built_result.tarball, expectation)


def test_install_import_uses_tarball_and_verifies_export_and_witness(
    built_result: node_adapter.BuildResult,
) -> None:
    before = _source_snapshot()
    runtime_request = _make_runtime_request(
        node_version="v24.14.0",
        npm_version="11.9.0",
    )
    assert (
        "expected_smoke_message"
        not in signature(qualify_npm_install_import).parameters
    )

    result = qualify_npm_install_import(
        built_result.tarball,
        built_result.expectation,
        runtime_request,
    )

    assert result.smoke_message == "hcoona-release-smoke-npm"
    assert result.witness_sha256 == (
        "sha256:" + hashlib.sha256(built_result.witness).hexdigest()
    )
    assert _source_snapshot() == before


def test_install_import_rejects_mutated_artifact_export(
    built_result: node_adapter.BuildResult,
) -> None:
    runtime_request = _make_runtime_request(
        node_version="v24.14.0",
        npm_version="11.9.0",
    )
    entries = _tar_entries(built_result.tarball)
    entries["package/dist/index.js"] = (
        b"export function smokeMessage() { return 'adjacent-package'; }\n"
    )

    with pytest.raises(ValueError, match="smokeMessage export mismatch"):
        qualify_npm_install_import(
            _make_tarball(entries),
            built_result.expectation,
            runtime_request,
        )


def test_build_reads_declared_inputs_once_and_reuses_immutable_bytes(  # noqa: PLR0915
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    project = tmp_path / "project"
    _copy_declared_project_inputs(project)
    index_path = project / "src/index.js"
    index_path.write_bytes(
        b"export function smokeMessage() {\n"
        b"  return 'hcoona-release-smoke-npm';\n"
        b"}\n"
    )
    original_sources = {
        relative: (project / relative).read_bytes()
        for relative in DECLARED_INPUTS
    }
    mutated_manifest = json.loads(original_sources["package.json"])
    mutated_manifest["description"] = "mutated-after-capture:package.json"
    mutated_sources = {
        "README.md": b"# mutated-after-capture:README.md\n",
        "package.json": (
            f"{json.dumps(mutated_manifest, indent=2)}\n".encode()
        ),
        "scripts/build.mjs": b"// mutated-after-capture:scripts/build.mjs\n",
        "src/index.js": (
            b"export function smokeMessage() {\n"
            b"  return 'mutated-after-capture:src/index.js';\n"
            b"}\n"
        ),
    }
    assert set(original_sources) == set(mutated_sources) == set(DECLARED_INPUTS)
    assert all(
        original_sources[relative] != mutated_sources[relative]
        for relative in DECLARED_INPUTS
    )

    resolved_project = project.resolve()
    resolved_sources = {
        relative: (resolved_project / relative).resolve()
        for relative in DECLARED_INPUTS
    }
    source_by_path = {
        source: relative for relative, source in resolved_sources.items()
    }
    read_counts = dict.fromkeys(resolved_sources.values(), 0)
    captured_sources: dict[str, bytes] = {}
    observed_source_reads: list[Path] = []
    staged_sources: dict[str, bytes] = {}
    prepared_staging_roots: list[Path] = []
    runner_staging_roots: list[Path] = []
    runner_staged_sources: list[dict[str, bytes]] = []
    packed_evidence_bytes: list[bytes] = []
    evidence_marker = b"\n/* declared-input-evidence\n"
    original_read_bytes = Path.read_bytes
    original_prepare_staged_manifest = (
        node_adapter._prepare_staged_manifest  # noqa: SLF001
    )

    def capture_source_read(path: Path) -> bytes:
        resolved_path = path.resolve()
        if resolved_path not in source_by_path:
            return original_read_bytes(path)
        relative = source_by_path[resolved_path]
        observed_source_reads.append(path)
        read_counts[resolved_path] += 1
        content = original_read_bytes(path)
        if read_counts[resolved_path] == 1:
            captured_sources[relative] = content
            path.write_bytes(mutated_sources[relative])
        return content

    def observe_staged_sources(
        request: BuildRequest,
        staging_root: Path,
    ) -> tuple[str, tuple[str, ...], tuple[tuple[str, str], ...]]:
        prepared_staging_roots.append(staging_root.resolve())
        staged_sources.update(
            {
                relative: original_read_bytes(staging_root / relative)
                for relative in DECLARED_INPUTS
            }
        )
        return original_prepare_staged_manifest(request, staging_root)

    def deterministic_runner(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del environment
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, "v24.14.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.21.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.9.0\n", "")
        if command == ("node", "scripts/build.mjs"):
            runner_staging_roots.append(cwd.resolve())
            runner_staged_sources.append(
                {
                    relative: original_read_bytes(cwd / relative)
                    for relative in DECLARED_INPUTS
                }
            )
            packed_evidence = canonicalize(
                {
                    relative: {
                        "byte-size": len(staged_sources[relative]),
                        "bytes-hex": staged_sources[relative].hex(),
                        "sha256": (
                            "sha256:"
                            + hashlib.sha256(
                                staged_sources[relative]
                            ).hexdigest()
                        ),
                    }
                    for relative in DECLARED_INPUTS
                }
            )
            packed_evidence_bytes.append(packed_evidence)
            (cwd / "dist").mkdir()
            (cwd / "dist/index.js").write_bytes(
                runner_staged_sources[-1]["src/index.js"]
                + evidence_marker
                + packed_evidence
                + b"*/\n"
            )
            return subprocess.CompletedProcess(command, 0, "", "")
        if command[:2] == ("npm", "pack"):
            basename = (
                "hcoona-hcoona-release-smoke-npm-"
                f"{build_request.npm_package_version}.tgz"
            )
            tarball = _make_tarball(
                {
                    "package/README.md": original_read_bytes(cwd / "README.md"),
                    "package/dist/index.js": original_read_bytes(
                        cwd / "dist/index.js"
                    ),
                    "package/package.json": original_read_bytes(
                        cwd / "package.json"
                    ),
                    "package/workflow-delivery/provenance.json": (
                        original_read_bytes(
                            cwd / "workflow-delivery/provenance.json"
                        )
                    ),
                }
            )
            (Path(command[-1]) / basename).write_bytes(tarball)
            return subprocess.CompletedProcess(
                command,
                0,
                json.dumps([{"filename": basename}]),
                "",
            )
        pytest.fail(f"unexpected Adapter command: {command}")

    monkeypatch.setattr(Path, "read_bytes", capture_source_read)
    monkeypatch.setattr(
        node_adapter,
        "_prepare_staged_manifest",
        observe_staged_sources,
    )
    monkeypatch.setattr(node_adapter, "_run", deterministic_runner)

    result = build_node_package(
        replace(build_request, source_root=resolved_project)
    )

    assert all(
        source.is_relative_to(resolved_project) and source.is_file()
        for source in resolved_sources.values()
    )
    assert observed_source_reads == [
        resolved_sources[relative] for relative in DECLARED_INPUTS
    ]
    assert read_counts == {
        resolved_sources[relative]: 1 for relative in DECLARED_INPUTS
    }
    assert captured_sources == original_sources
    assert all(type(content) is bytes for content in captured_sources.values())
    assert result.source_input_manifest == tuple(
        (
            relative,
            "sha256:" + hashlib.sha256(captured_sources[relative]).hexdigest(),
        )
        for relative in DECLARED_INPUTS
    )
    assert all(
        dict(result.source_input_manifest)[relative]
        != "sha256:" + hashlib.sha256(mutated_sources[relative]).hexdigest()
        for relative in DECLARED_INPUTS
    )
    assert result.manifest.sha256 == (
        "sha256:" + hashlib.sha256(result.tarball).hexdigest()
    )
    assert result.manifest.sha512 == (
        "sha512:" + hashlib.sha512(result.tarball).hexdigest()
    )
    assert result.manifest.byte_size == len(result.tarball)
    assert prepared_staging_roots == runner_staging_roots
    assert staged_sources == original_sources
    assert len(runner_staged_sources) == 1
    runner_sources = runner_staged_sources[0]
    assert all(
        runner_sources[relative] == original_sources[relative]
        for relative in ("README.md", "scripts/build.mjs", "src/index.js")
    )
    runner_manifest = json.loads(runner_sources["package.json"])
    original_manifest = json.loads(original_sources["package.json"])
    assert {
        key: value
        for key, value in runner_manifest.items()
        if key not in {"files", "version"}
    } == {
        key: value
        for key, value in original_manifest.items()
        if key not in {"files", "version"}
    }
    assert runner_manifest["version"] == build_request.npm_package_version
    assert runner_manifest["files"] == [
        "dist",
        "README.md",
        "workflow-delivery/provenance.json",
    ]

    packed_entries = _tar_entries(result.tarball)
    packed_dist = packed_entries["package/dist/index.js"]
    assert packed_dist.startswith(
        original_sources["src/index.js"] + evidence_marker
    )
    assert packed_dist.endswith(b"*/\n")
    packed_evidence_from_dist = packed_dist[
        len(original_sources["src/index.js"] + evidence_marker) : -3
    ]
    assert packed_evidence_bytes == [packed_evidence_from_dist]
    packed_evidence = json.loads(packed_evidence_bytes[0])
    assert set(packed_evidence) == set(DECLARED_INPUTS)
    for relative in DECLARED_INPUTS:
        evidence = packed_evidence[relative]
        assert evidence["byte-size"] == len(original_sources[relative])
        assert evidence["sha256"] == (
            "sha256:" + hashlib.sha256(original_sources[relative]).hexdigest()
        )
        assert (
            bytes.fromhex(evidence["bytes-hex"]) == original_sources[relative]
        )
    assert packed_entries["package/README.md"] == original_sources["README.md"]
    assert all(
        mutated not in packed_entries.values()
        for mutated in mutated_sources.values()
    )
    assert {
        relative: original_read_bytes(resolved_sources[relative])
        for relative in DECLARED_INPUTS
    } == mutated_sources


@pytest.mark.parametrize(
    "suffix_kind",
    [
        pytest.param("raw-suffix", id="raw-suffix"),
        pytest.param("second-gzip-member", id="second-gzip-member"),
    ],
)
def test_artifact_contents_rejects_suffix_smuggling(
    built_result: node_adapter.BuildResult,
    suffix_kind: str,
) -> None:
    if suffix_kind == "raw-suffix":
        suffix = b"RAW-SUFFIX"
    else:
        suffix = _make_tarball({"package/smuggled.txt": b"second-member"})
        assert _tar_entries(suffix) == {
            "package/smuggled.txt": b"second-member"
        }
    smuggled_tarball = built_result.tarball + suffix

    assert smuggled_tarball[: -len(suffix)] == built_result.tarball
    assert smuggled_tarball[-len(suffix) :] == suffix
    with pytest.raises(ValueError, match="invalid npm tarball"):
        qualify_npm_artifact_contents(
            smuggled_tarball,
            built_result.expectation,
        )


def test_artifact_contents_rejects_concatenated_tar_archive(
    built_result: node_adapter.BuildResult,
) -> None:
    second_archive = io.BytesIO()
    with tarfile.open(fileobj=second_archive, mode="w:") as archive:
        content = b"second-archive"
        member = tarfile.TarInfo("package/smuggled.txt")
        member.size = len(content)
        member.mtime = 0
        member.mode = 0o644
        archive.addfile(member, io.BytesIO(content))

    first_archive_bytes = gzip.decompress(built_result.tarball)
    second_archive_bytes = second_archive.getvalue()
    concatenated_payload = first_archive_bytes + second_archive_bytes
    concatenated_tarball = gzip.compress(concatenated_payload, mtime=0)

    assert gzip.decompress(concatenated_tarball) == concatenated_payload
    with tarfile.open(
        fileobj=io.BytesIO(second_archive_bytes),
        mode="r:",
    ) as archive:
        smuggled = archive.getmember("package/smuggled.txt")
        extracted = archive.extractfile(smuggled)
        assert extracted is not None
        assert extracted.read() == b"second-archive"
    with pytest.raises(ValueError, match="invalid npm tarball"):
        qualify_npm_artifact_contents(
            concatenated_tarball,
            built_result.expectation,
        )


def test_artifact_contents_rejects_nonzero_member_alignment_padding(
    built_result: node_adapter.BuildResult,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    with tarfile.open(
        fileobj=io.BytesIO(original_payload),
        mode="r:",
    ) as archive:
        members = archive.getmembers()
        ordinary_member = archive.getmember("package/dist/index.js")

    padding_start = ordinary_member.offset_data + ordinary_member.size
    padding_end = (
        (padding_start + tarfile.BLOCKSIZE - 1)
        // tarfile.BLOCKSIZE
        * tarfile.BLOCKSIZE
    )
    later_members = [
        member for member in members if member.offset > ordinary_member.offset
    ]
    assert ordinary_member.isfile()
    assert ordinary_member.size % tarfile.BLOCKSIZE != 0
    assert padding_start < padding_end
    assert later_members
    assert padding_end == later_members[0].offset
    assert not any(original_payload[padding_start:padding_end])

    mutated_payload = bytearray(original_payload)
    mutated_payload[padding_start] = 0xA5
    final_data_end = max(
        (member.offset_data + member.size + tarfile.BLOCKSIZE - 1)
        // tarfile.BLOCKSIZE
        * tarfile.BLOCKSIZE
        for member in members
    )
    final_trailer = mutated_payload[final_data_end:]
    assert padding_start < later_members[0].offset_data
    assert len(final_trailer) >= tarfile.BLOCKSIZE * 2
    assert not any(final_trailer)

    malformed_tarball = gzip.compress(bytes(mutated_payload), mtime=0)
    assert _tar_entries(malformed_tarball) == _tar_entries(built_result.tarball)
    with pytest.raises(ValueError, match="invalid npm tarball"):
        qualify_npm_artifact_contents(
            malformed_tarball,
            built_result.expectation,
        )


def test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile(
    built_result: node_adapter.BuildResult,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    original_entries = _tar_entries(built_result.tarball)
    with tarfile.open(
        fileobj=io.BytesIO(original_payload),
        mode="r:",
    ) as archive:
        members = archive.getmembers()

    assert len(built_result.tarball) == FROZEN_TARBALL_BYTE_SIZE
    assert hashlib.sha256(built_result.tarball).hexdigest() == (
        "0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd"
    )
    assert [member.name for member in members] == [
        "package/dist/index.js",
        "package/package.json",
        "package/workflow-delivery/provenance.json",
        "package/README.md",
    ]
    assert set(original_entries) == set(
        built_result.expectation.entry_allowlist
    )

    for member in members:
        header = original_payload[
            member.offset : member.offset + tarfile.BLOCKSIZE
        ]
        checksum_header = bytearray(header)
        checksum_start, checksum_end = TAR_HEADER_FIELDS["checksum"]
        checksum_header[checksum_start:checksum_end] = b" " * 8
        padding_start = member.offset_data + member.size
        padding_end = (
            (padding_start + tarfile.BLOCKSIZE - 1)
            // tarfile.BLOCKSIZE
            * tarfile.BLOCKSIZE
        )

        assert header[0:100] == _nul_filled(member.name.encode(), 100)
        assert header[100:108] == b"000644 \0"
        assert header[108:116] == bytes(8)
        assert header[116:124] == bytes(8)
        assert header[124:136] == f"{member.size:010o} \0".encode()
        assert header[136:148] == b"3560116604 \0"
        assert header[148:156] == (f"{sum(checksum_header):06o} \0".encode())
        assert header[156:157] == tarfile.REGTYPE
        assert header[157:257] == bytes(100)
        assert header[257:263] == b"ustar\0"
        assert header[263:265] == b"00"
        assert header[265:297] == bytes(32)
        assert header[297:329] == bytes(32)
        assert header[329:337] == b"000000 \0"
        assert header[337:345] == b"000000 \0"
        assert header[345:500] == bytes(155)
        assert header[500:512] == bytes(12)
        assert not any(original_payload[padding_start:padding_end])

    final_data_end = max(
        (member.offset_data + member.size + tarfile.BLOCKSIZE - 1)
        // tarfile.BLOCKSIZE
        * tarfile.BLOCKSIZE
        for member in members
    )
    assert original_payload[final_data_end:] == bytes(tarfile.BLOCKSIZE * 2)
    manifest = qualify_npm_artifact_contents(
        built_result.tarball,
        built_result.expectation,
    )
    assert manifest == built_result.manifest
    assert manifest.sha256 == (
        f"sha256:{hashlib.sha256(built_result.tarball).hexdigest()}"
    )
    assert manifest.sha512 == (
        f"sha512:{hashlib.sha512(built_result.tarball).hexdigest()}"
    )


@pytest.mark.parametrize(
    ("extension_kind", "physical_type"),
    [
        pytest.param(
            "gnu-long-name",
            tarfile.GNUTYPE_LONGNAME,
            id="gnu-long-name-L",
        ),
        pytest.param(
            "gnu-long-link",
            tarfile.GNUTYPE_LONGLINK,
            id="gnu-long-link-K",
        ),
    ],
)
def test_artifact_contents_rejects_gnu_long_name_or_long_link_header(
    built_result: node_adapter.BuildResult,
    extension_kind: str,
    physical_type: bytes,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    original_entries = _tar_entries(built_result.tarball)
    with tarfile.open(
        fileobj=io.BytesIO(original_payload),
        mode="r:",
    ) as archive:
        insertion_member = archive.getmember("package/dist/index.js")

    extension_prefix = _physical_extension_prefix(
        extension_kind,
        insertion_member,
    )
    payload_with_extension = (
        original_payload[: insertion_member.offset]
        + extension_prefix
        + original_payload[insertion_member.offset :]
    )
    extension_header = extension_prefix[: tarfile.BLOCKSIZE]
    extension_size = int(extension_header[124:136].rstrip(b"\0 "), 8)
    extension_padding = extension_prefix[tarfile.BLOCKSIZE + extension_size :]

    assert extension_header[156:157] == physical_type
    assert extension_size > 0
    assert extension_padding
    assert not any(extension_padding)
    extension_tarball = gzip.compress(payload_with_extension, mtime=0)
    assert _tar_entries(extension_tarball) == original_entries
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            extension_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("extension_kind", "physical_type"),
    [
        pytest.param("pax-extended", tarfile.XHDTYPE, id="pax-local-x"),
        pytest.param("pax-global", tarfile.XGLTYPE, id="pax-global-g"),
        pytest.param(
            "pax-solaris",
            tarfile.SOLARIS_XHDTYPE,
            id="pax-solaris-X",
        ),
    ],
)
def test_artifact_contents_rejects_pax_physical_header(
    built_result: node_adapter.BuildResult,
    extension_kind: str,
    physical_type: bytes,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    original_entries = _tar_entries(built_result.tarball)
    with tarfile.open(
        fileobj=io.BytesIO(original_payload),
        mode="r:",
    ) as archive:
        insertion_member = archive.getmember("package/dist/index.js")

    extension_prefix = _physical_extension_prefix(
        extension_kind,
        insertion_member,
    )
    extension_header = extension_prefix[: tarfile.BLOCKSIZE]
    extension_size = int(extension_header[124:136].rstrip(b"\0 "), 8)
    extension_content = extension_prefix[
        tarfile.BLOCKSIZE : tarfile.BLOCKSIZE + extension_size
    ]
    payload_with_extension = (
        original_payload[: insertion_member.offset]
        + extension_prefix
        + original_payload[insertion_member.offset :]
    )

    assert extension_header[156:157] == physical_type
    assert extension_content.endswith(b"\n")
    assert b"=" in extension_content
    extension_tarball = gzip.compress(payload_with_extension, mtime=0)
    assert _tar_entries(extension_tarball) == original_entries
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            extension_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("profile_kind", "replacements"),
    [
        pytest.param(
            "gnu-magic",
            {"magic": b"ustar ", "version": b" \0"},
            id="gnu-magic-and-version",
        ),
        pytest.param(
            "v7",
            {"magic": bytes(6), "version": bytes(2)},
            id="v7-zero-magic-and-version",
        ),
        pytest.param(
            "magic",
            {"magic": b"ustar "},
            id="noncanonical-magic",
        ),
        pytest.param(
            "version",
            {"version": b"01"},
            id="unsupported-version",
        ),
    ],
)
def test_artifact_contents_rejects_noncanonical_ustar_magic_or_version(
    built_result: node_adapter.BuildResult,
    profile_kind: str,
    replacements: dict[str, bytes],
) -> None:
    original_entries = _tar_entries(built_result.tarball)
    mutated_tarball = _tarball_with_first_header_fields(
        built_result.tarball,
        replacements,
    )
    mutated_header = gzip.decompress(mutated_tarball)[: tarfile.BLOCKSIZE]

    assert profile_kind
    assert mutated_header[257:265] != b"ustar\000"
    assert _tar_entries(mutated_tarball) == original_entries
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("member_index", "member_name"),
    [
        pytest.param(1, "package/package.json", id="member-1-package-json"),
        pytest.param(
            2,
            "package/workflow-delivery/provenance.json",
            id="member-2-provenance",
        ),
        pytest.param(3, "package/README.md", id="member-3-readme"),
    ],
)
@pytest.mark.parametrize(
    "mutation",
    [
        pytest.param(
            ("mode-alt-terminator", "mode", b"000644\0 ", b" \0"),
            id="mode-alt-terminator",
        ),
        pytest.param(
            ("noncanonical-magic", "magic", b"ustar ", b" \0"),
            id="noncanonical-magic",
        ),
        pytest.param(
            ("unsupported-version", "version", b"01", b" \0"),
            id="unsupported-version",
        ),
        pytest.param(
            ("name-hidden-suffix", "name", None, b" \0"),
            id="name-hidden-suffix",
        ),
        pytest.param(
            (
                "linkname-hidden-suffix",
                "linkname",
                b"\0X" + bytes(98),
                b" \0",
            ),
            id="linkname-hidden-suffix",
        ),
        pytest.param(
            (
                "reserved-nonzero",
                "reserved",
                bytes(11) + bytes((NONZERO_PADDING_BYTE,)),
                b" \0",
            ),
            id="reserved-nonzero",
        ),
        pytest.param(
            ("old-regular-type", "type", tarfile.AREGTYPE, b" \0"),
            id="old-regular-type",
        ),
        pytest.param(
            ("checksum-alt-terminator", None, None, b"\0 "),
            id="checksum-alt-terminator",
        ),
    ],
)
def test_artifact_contents_rejects_later_member_ustar_profile_mutations(
    built_result: node_adapter.BuildResult,
    member_index: int,
    member_name: str,
    mutation: tuple[str, str | None, bytes | None, bytes],
) -> None:
    profile_kind, field, replacement, checksum_suffix = mutation
    original_payload = gzip.decompress(built_result.tarball)
    original_entries = _tar_entries(built_result.tarball)
    original_observables = _tar_member_observables(built_result.tarball)
    member_offset = cast("int", original_observables[member_index][13])
    if profile_kind == "name-hidden-suffix":
        name_start, name_end = TAR_HEADER_FIELDS["name"]
        original_name = original_payload[
            member_offset + name_start : member_offset + name_end
        ]
        first_nul = original_name.index(0)
        mutated_name = bytearray(original_name)
        mutated_name[first_nul + 1] = NONZERO_PADDING_BYTE
        replacement = bytes(mutated_name)
    replacements = {} if field is None else {field: cast("bytes", replacement)}
    mutated_tarball = _tarball_with_member_header_fields(
        built_result.tarball,
        member_index,
        replacements,
        checksum_suffix=checksum_suffix,
    )
    mutated_payload = gzip.decompress(mutated_tarball)
    mutated_observables = _tar_member_observables(mutated_tarball)

    assert len(original_observables) == EXPECTED_FROZEN_TAR_MEMBER_COUNT
    assert original_observables[member_index][0:2] == (
        member_index,
        member_name,
    )
    assert profile_kind
    if field is None:
        checksum_start, checksum_end = TAR_HEADER_FIELDS["checksum"]
        absolute_start = member_offset + checksum_start
        absolute_end = member_offset + checksum_end
        assert (
            original_payload[absolute_start:absolute_end]
            != mutated_payload[absolute_start:absolute_end]
        )
        assert (
            mutated_payload[absolute_end - 2 : absolute_end] == checksum_suffix
        )
    else:
        assert replacement is not None
        start, end = TAR_HEADER_FIELDS[field]
        absolute_start = member_offset + start
        absolute_end = member_offset + end
        assert original_payload[absolute_start:absolute_end] != replacement
        assert mutated_payload[absolute_start:absolute_end] == replacement
    if field == "type":
        original_member = original_observables[member_index]
        mutated_member = mutated_observables[member_index]
        assert mutated_member[7] == tarfile.AREGTYPE
        assert mutated_member[:7] + mutated_member[8:] == (
            original_member[:7] + original_member[8:]
        )
    else:
        assert mutated_observables == original_observables
    assert _tar_entries(mutated_tarball) == original_entries
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    "field",
    [
        pytest.param("name", id="name"),
        pytest.param("linkname", id="linkname"),
        pytest.param("uname", id="uname"),
        pytest.param("gname", id="gname"),
        pytest.param("prefix", id="prefix"),
    ],
)
@pytest.mark.parametrize(
    ("suffix_position", "suffix_offset"),
    [
        pytest.param("after-first-nul", 1, id="after-first-nul"),
        pytest.param("middle", -1, id="middle"),
        pytest.param("final", None, id="final"),
    ],
)
def test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field(  # noqa: E501
    built_result: node_adapter.BuildResult,
    field: str,
    suffix_position: str,
    suffix_offset: int | None,
) -> None:
    original_header = gzip.decompress(built_result.tarball)[: tarfile.BLOCKSIZE]
    start, end = TAR_HEADER_FIELDS[field]
    original_field = original_header[start:end]
    first_nul = original_field.index(0)
    replacement = bytearray(original_field)
    if suffix_offset is None:
        mutation_index = len(original_field) - 1
    elif suffix_offset == -1:
        mutation_index = (first_nul + len(original_field) - 1) // 2
    else:
        mutation_index = first_nul + suffix_offset
    replacement[mutation_index] = NONZERO_PADDING_BYTE
    mutated_tarball = _tarball_with_first_header_fields(
        built_result.tarball,
        {field: bytes(replacement)},
    )
    mutated_field = gzip.decompress(mutated_tarball)[start:end]

    assert suffix_position
    assert first_nul < len(original_field) - 1
    assert first_nul < mutation_index < len(original_field)
    assert not any(original_field[first_nul:])
    assert mutated_field[:first_nul] == original_field[:first_nul]
    assert mutated_field[first_nul] == 0
    assert mutated_field[mutation_index] == NONZERO_PADDING_BYTE
    assert _tar_member_observables(mutated_tarball) == _tar_member_observables(
        built_result.tarball
    )
    assert _tar_entries(mutated_tarball) == _tar_entries(built_result.tarball)
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        pytest.param("uid", b"000000 \0", id="uid-octal-zero"),
        pytest.param(
            "uid",
            bytes(7) + b"X",
            id="uid-hidden-suffix",
        ),
        pytest.param("gid", b"000000 \0", id="gid-octal-zero"),
        pytest.param(
            "gid",
            bytes(7) + b"X",
            id="gid-hidden-suffix",
        ),
        pytest.param(
            "linkname",
            b"X" + bytes(99),
            id="linkname-nonempty",
        ),
        pytest.param(
            "uname",
            b"X" + bytes(31),
            id="uname-nonempty",
        ),
        pytest.param(
            "gname",
            b"X" + bytes(31),
            id="gname-nonempty",
        ),
        pytest.param(
            "prefix",
            b"X" + bytes(154),
            id="prefix-nonempty",
        ),
        pytest.param(
            "reserved",
            bytes((NONZERO_PADDING_BYTE,)) + bytes(11),
            id="reserved-nonzero-first",
        ),
        pytest.param(
            "reserved",
            bytes(6) + bytes((NONZERO_PADDING_BYTE,)) + bytes(5),
            id="reserved-nonzero-middle",
        ),
        pytest.param(
            "reserved",
            bytes(11) + bytes((NONZERO_PADDING_BYTE,)),
            id="reserved-nonzero-final",
        ),
        pytest.param("devmajor", b"000001 \0", id="devmajor-nonzero"),
        pytest.param("devminor", b"000001 \0", id="devminor-nonzero"),
        pytest.param("devmajor", bytes(8), id="devmajor-all-nul"),
        pytest.param("devminor", bytes(8), id="devminor-all-nul"),
    ],
)
def test_artifact_contents_rejects_noncanonical_unused_header_field(
    built_result: node_adapter.BuildResult,
    field: str,
    replacement: bytes,
) -> None:
    original_header = gzip.decompress(built_result.tarball)[: tarfile.BLOCKSIZE]
    start, end = TAR_HEADER_FIELDS[field]
    mutated_tarball = _tarball_with_first_header_fields(
        built_result.tarball,
        {field: replacement},
    )
    mutated_header = gzip.decompress(mutated_tarball)[: tarfile.BLOCKSIZE]

    assert original_header[start:end] != replacement
    assert mutated_header[start:end] == replacement
    mutated_entries = _tar_entries(mutated_tarball)
    original_entries = _tar_entries(built_result.tarball)
    if field == "prefix":
        assert mutated_entries == {
            (f"X/{name}" if name == "package/dist/index.js" else name): content
            for name, content in original_entries.items()
        }
    else:
        assert mutated_entries == original_entries
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("field", "replacement", "checksum_suffix"),
    [
        pytest.param("mode", b"0000644\0", b" \0", id="mode-alt-width"),
        pytest.param(
            "mode",
            b"000644\0 ",
            b" \0",
            id="mode-alt-terminator",
        ),
        pytest.param(
            "mode",
            b"000644  ",
            b" \0",
            id="mode-space-terminator",
        ),
        pytest.param("mode", b"000644\0X", b" \0", id="mode-hidden-suffix"),
        pytest.param(
            "uid",
            b"000000\0 ",
            b" \0",
            id="uid-alt-terminator",
        ),
        pytest.param(
            "uid",
            b"000000  ",
            b" \0",
            id="uid-space-terminator",
        ),
        pytest.param(
            "uid",
            bytes((0, NONZERO_PADDING_BYTE)) + bytes(6),
            b" \0",
            id="uid-hidden-immediate-suffix",
        ),
        pytest.param(
            "gid",
            b"000000\0 ",
            b" \0",
            id="gid-alt-terminator",
        ),
        pytest.param(
            "gid",
            b"000000  ",
            b" \0",
            id="gid-space-terminator",
        ),
        pytest.param(
            "gid",
            bytes((0, NONZERO_PADDING_BYTE)) + bytes(6),
            b" \0",
            id="gid-hidden-immediate-suffix",
        ),
        pytest.param("size", b"00000000110\0", b" \0", id="size-alt-width"),
        pytest.param(
            "size",
            b"0000000110\0 ",
            b" \0",
            id="size-alt-terminator",
        ),
        pytest.param(
            "size",
            b"0000000110  ",
            b" \0",
            id="size-space-terminator",
        ),
        pytest.param(
            "size",
            b"0000000110\0X",
            b" \0",
            id="size-hidden-suffix",
        ),
        pytest.param(
            "mtime",
            b"03560116604\0",
            b" \0",
            id="mtime-alt-width",
        ),
        pytest.param(
            "mtime",
            b"3560116604\0 ",
            b" \0",
            id="mtime-alt-terminator",
        ),
        pytest.param(
            "mtime",
            b"3560116604  ",
            b" \0",
            id="mtime-space-terminator",
        ),
        pytest.param(
            "mtime",
            b"3560116604\0X",
            b" \0",
            id="mtime-hidden-suffix",
        ),
        pytest.param(
            "devmajor",
            b"000000\0 ",
            b" \0",
            id="devmajor-alt-terminator",
        ),
        pytest.param(
            "devmajor",
            b"000000\0X",
            b" \0",
            id="devmajor-hidden-suffix",
        ),
        pytest.param(
            "devmajor",
            b"000000  ",
            b" \0",
            id="devmajor-space-terminator",
        ),
        pytest.param(
            "devminor",
            b"000000\0 ",
            b" \0",
            id="devminor-alt-terminator",
        ),
        pytest.param(
            "devminor",
            b"000000\0X",
            b" \0",
            id="devminor-hidden-suffix",
        ),
        pytest.param(
            "devminor",
            b"000000  ",
            b" \0",
            id="devminor-space-terminator",
        ),
        pytest.param("mode", None, b" \0", id="mode-base256"),
        pytest.param("uid", None, b" \0", id="uid-base256"),
        pytest.param("gid", None, b" \0", id="gid-base256"),
        pytest.param("size", None, b" \0", id="size-base256"),
        pytest.param("mtime", None, b" \0", id="mtime-base256"),
        pytest.param("devmajor", None, b" \0", id="devmajor-base256"),
        pytest.param("devminor", None, b" \0", id="devminor-base256"),
        pytest.param(
            None,
            b"",
            b"\0 ",
            id="checksum-alt-terminator",
        ),
        pytest.param(
            None,
            b"",
            b"  ",
            id="checksum-space-terminator",
        ),
        pytest.param(
            None,
            b"",
            b"\0X",
            id="checksum-hidden-suffix",
        ),
    ],
)
def test_artifact_contents_rejects_noncanonical_numeric_header_encoding(
    built_result: node_adapter.BuildResult,
    field: str | None,
    replacement: bytes | None,
    checksum_suffix: bytes,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    with tarfile.open(
        fileobj=io.BytesIO(original_payload),
        mode="r:",
    ) as original_archive:
        original_member = original_archive.getmember("package/dist/index.js")

    if field is not None and replacement is None:
        start, end = TAR_HEADER_FIELDS[field]
        numeric_value = cast("int", getattr(original_member, field))
        replacement = b"\x80" + numeric_value.to_bytes(
            end - start - 1,
            "big",
        )
    assert replacement is not None
    replacements = {} if field is None else {field: replacement}
    mutated_tarball = _tarball_with_first_header_fields(
        built_result.tarball,
        replacements,
        checksum_suffix=checksum_suffix,
    )
    mutated_payload = gzip.decompress(mutated_tarball)
    with tarfile.open(
        fileobj=io.BytesIO(mutated_payload),
        mode="r:",
    ) as mutated_archive:
        mutated_member = mutated_archive.getmember("package/dist/index.js")

    if field is None:
        checksum_start, checksum_end = TAR_HEADER_FIELDS["checksum"]
        assert (
            original_payload[checksum_start:checksum_end]
            != mutated_payload[checksum_start:checksum_end]
        )
        assert (
            mutated_payload[checksum_end - 2 : checksum_end] == checksum_suffix
        )
    else:
        start, end = TAR_HEADER_FIELDS[field]
        assert original_payload[start:end] != replacement
        assert mutated_payload[start:end] == replacement
    assert (
        original_member.name,
        original_member.mode,
        original_member.uid,
        original_member.gid,
        original_member.size,
        original_member.mtime,
        original_member.devmajor,
        original_member.devminor,
    ) == (
        mutated_member.name,
        mutated_member.mode,
        mutated_member.uid,
        mutated_member.gid,
        mutated_member.size,
        mutated_member.mtime,
        mutated_member.devmajor,
        mutated_member.devminor,
    )
    assert _tar_entries(mutated_tarball) == _tar_entries(built_result.tarball)
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


def test_artifact_contents_rejects_bad_checksum_before_tarfile_parse(
    built_result: node_adapter.BuildResult,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    checksum_start, checksum_end = TAR_HEADER_FIELDS["checksum"]
    original_checksum = int(
        original_payload[checksum_start : checksum_end - 2],
        8,
    )
    incorrect_checksum = f"{original_checksum + 1:06o} \0".encode()
    mutated_payload = bytearray(original_payload)
    mutated_payload[checksum_start:checksum_end] = incorrect_checksum
    mutated_tarball = gzip.compress(bytes(mutated_payload), mtime=0)
    semantic_parse_calls: list[bool] = []

    assert incorrect_checksum[-2:] == b" \0"
    assert all(ord("0") <= byte <= ord("7") for byte in incorrect_checksum[:-2])
    assert mutated_payload[:checksum_start] == original_payload[:checksum_start]
    assert mutated_payload[checksum_end:] == original_payload[checksum_end:]

    def fail_semantic_tar_parse(
        *_args: object,
        **_kwargs: object,
    ) -> object:
        semantic_parse_calls.append(True)
        pytest.fail("semantic TAR parsing ran before raw checksum rejection")

    monkeypatch.setattr(
        node_adapter.tarfile,
        "open",
        fail_semantic_tar_parse,
    )

    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )
    assert semantic_parse_calls == []


@pytest.mark.parametrize(
    ("type_flag", "type_name"),
    [
        pytest.param(tarfile.AREGTYPE, "old-regular", id="old-regular-NUL"),
        pytest.param(tarfile.LNKTYPE, "hard-link", id="hard-link-1"),
        pytest.param(tarfile.SYMTYPE, "symbolic-link", id="symbolic-link-2"),
        pytest.param(tarfile.CHRTYPE, "character-device", id="char-device-3"),
        pytest.param(tarfile.BLKTYPE, "block-device", id="block-device-4"),
        pytest.param(tarfile.DIRTYPE, "directory", id="directory-5"),
        pytest.param(tarfile.FIFOTYPE, "fifo", id="fifo-6"),
        pytest.param(tarfile.CONTTYPE, "contiguous", id="contiguous-7"),
        pytest.param(b"D", "gnu-dump-directory", id="gnu-dump-dir-D"),
        pytest.param(b"M", "gnu-multivolume", id="gnu-multivol-M"),
        pytest.param(b"N", "gnu-names", id="gnu-names-N"),
        pytest.param(tarfile.GNUTYPE_SPARSE, "gnu-sparse", id="gnu-sparse-S"),
        pytest.param(b"V", "gnu-volume-header", id="gnu-volume-V"),
        pytest.param(b"?", "unknown-special", id="unknown-special-question"),
    ],
)
def test_artifact_contents_rejects_every_nonordinary_tar_type(
    built_result: node_adapter.BuildResult,
    type_flag: bytes,
    type_name: str,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    original_names = list(_tar_entries(built_result.tarball))
    special_header = _special_tar_header(original_payload, type_flag)
    payload_with_special = special_header + original_payload
    with tarfile.open(
        fileobj=io.BytesIO(payload_with_special),
        mode="r:",
    ) as archive:
        logical_members = archive.getmembers()

    assert type_name
    assert type_flag != tarfile.REGTYPE
    assert special_header[156:157] == type_flag
    assert special_header[257:265] == b"ustar\0" + b"00"
    assert logical_members[0].name == "package/special-entry"
    assert logical_members[0].type == type_flag
    assert [member.name for member in logical_members[1:]] == original_names
    special_tarball = gzip.compress(payload_with_special, mtime=0)
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            special_tarball,
            built_result.expectation,
        )


def test_artifact_contents_rejects_extra_zero_trailer_block(
    built_result: node_adapter.BuildResult,
) -> None:
    original_payload = gzip.decompress(built_result.tarball)
    extra_trailer_payload = original_payload + bytes(tarfile.BLOCKSIZE)
    extra_trailer_tarball = gzip.compress(extra_trailer_payload, mtime=0)

    assert _tar_entries(extra_trailer_tarball) == _tar_entries(
        built_result.tarball
    )
    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            extra_trailer_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    "invalid_kind",
    [
        pytest.param("malformed", id="malformed-gzip"),
        pytest.param("missing-trailer", id="missing-gzip-trailer"),
        pytest.param("halfway-truncated", id="halfway-truncated-gzip"),
    ],
)
def test_artifact_contents_rejects_malformed_or_premature_streams(
    built_result: node_adapter.BuildResult,
    invalid_kind: str,
) -> None:
    if invalid_kind == "malformed":
        invalid_tarball = b"not-a-gzip-stream"
    elif invalid_kind == "missing-trailer":
        invalid_tarball = built_result.tarball[:-8]
    else:
        invalid_tarball = built_result.tarball[: len(built_result.tarball) // 2]

    assert invalid_tarball != built_result.tarball
    assert len(invalid_tarball) < len(built_result.tarball)
    with pytest.raises(ValueError, match="invalid npm tarball"):
        qualify_npm_artifact_contents(
            invalid_tarball,
            built_result.expectation,
        )


def test_runtime_request_is_minimal_frozen_and_exported() -> None:
    runtime_request_type = getattr(node_adapter, "RuntimeRequest", None)
    assert runtime_request_type is not None, (
        "node adapter must define RuntimeRequest"
    )

    request = runtime_request_type(
        node_version="v24.4.1",
        npm_version="11.4.2",
    )
    runtime_fields = dataclasses.fields(request)

    assert tuple(field.name for field in runtime_fields) == (
        "node_version",
        "npm_version",
    )
    runtime_signature = inspect.signature(runtime_request_type)
    assert tuple(runtime_signature.parameters) == (
        "node_version",
        "npm_version",
    )
    assert all(
        parameter.default is inspect.Parameter.empty
        for parameter in runtime_signature.parameters.values()
    )
    assert type(request.node_version) is str
    assert type(request.npm_version) is str
    assert not hasattr(request, "__dict__")
    assert all(
        forbidden.lower() not in field.name.lower()
        for field in runtime_fields
        for forbidden in (
            "pnpm",
            "snapshot",
            "evidence",
            "planner",
            "run",
            "attempt",
        )
    )
    with pytest.raises(dataclasses.FrozenInstanceError):
        cast("Any", request).node_version = "v24.4.2"

    package_runtime_request = getattr(
        adapters_package,
        "RuntimeRequest",
        None,
    )
    assert package_runtime_request is runtime_request_type


@pytest.mark.parametrize(
    "operation",
    [
        pytest.param("project-tests", id="project-tests"),
        pytest.param("install-import", id="install-import"),
    ],
)
@pytest.mark.parametrize(
    "scenario",
    [
        pytest.param("success", id="matching-versions"),
        pytest.param("node-mismatch", id="node-version-mismatch"),
        pytest.param("npm-mismatch", id="npm-version-mismatch"),
        pytest.param("empty-node", id="empty-node-version"),
        pytest.param("empty-npm", id="empty-npm-version"),
        pytest.param("surrogate", id="surrogate-request"),
        pytest.param("subclass", id="runtime-request-subclass"),
    ],
)
def test_quality_adapters_probe_frozen_runtime_before_operations(  # noqa: PLR0915
    monkeypatch: pytest.MonkeyPatch,
    request: pytest.FixtureRequest,
    operation: str,
    scenario: str,
) -> None:
    runtime_request_type = getattr(node_adapter, "RuntimeRequest", None)
    assert runtime_request_type is not None, (
        "node adapter must define RuntimeRequest before quality operations "
        f"can validate {scenario!r} for {operation!r}"
    )

    node_version = "" if scenario == "empty-node" else "v24.4.1"
    npm_version = "" if scenario == "empty-npm" else "11.4.2"
    if scenario == "surrogate":
        runtime_request: object = types.SimpleNamespace(
            node_version=node_version,
            npm_version=npm_version,
        )
    elif scenario == "subclass":
        runtime_request_subclass = type(
            "RuntimeRequestSubclass",
            (runtime_request_type,),
            {},
        )
        runtime_request = runtime_request_subclass(
            node_version=node_version,
            npm_version=npm_version,
        )
    else:
        runtime_request = runtime_request_type(
            node_version=node_version,
            npm_version=npm_version,
        )

    built_result: node_adapter.BuildResult | None = None
    if operation == "install-import":
        built_result = cast(
            "node_adapter.BuildResult",
            request.getfixturevalue("built_result"),
        )

    observed: list[
        tuple[
            tuple[str, ...],
            Path,
            dict[str, str],
            str,
            str | None,
            bool,
            bytes | None,
        ]
    ] = []
    reported_node = "v24.4.0" if scenario == "node-mismatch" else "v24.4.1"
    reported_npm = "11.4.1" if scenario == "npm-mismatch" else "11.4.2"

    def record_quality_command(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        global_config_value = environment.get("NPM_CONFIG_GLOBALCONFIG")
        global_config_path = (
            Path(global_config_value)
            if global_config_value is not None
            else None
        )
        global_config_exists = (
            global_config_path is not None and global_config_path.is_file()
        )
        observed.append(
            (
                command,
                cwd,
                dict(environment),
                Path(environment["NPM_CONFIG_USERCONFIG"]).read_text(),
                global_config_value,
                global_config_exists,
                (
                    global_config_path.read_bytes()
                    if global_config_exists and global_config_path is not None
                    else None
                ),
            )
        )
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, reported_node, "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, reported_npm, "")
        if command == ("npm", "test", "--ignore-scripts"):
            return subprocess.CompletedProcess(command, 0, "passed", "")
        if command[:2] == ("npm", "install"):
            assert built_result is not None
            package_root = (
                cwd / "node_modules" / built_result.expectation.package_name
            )
            for name, content in _tar_entries(built_result.tarball).items():
                assert name.startswith("package/")
                destination = package_root / name.removeprefix("package/")
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(content)
            return subprocess.CompletedProcess(command, 0, "", "")
        if command[:2] == ("node", "--input-type=module"):
            return subprocess.CompletedProcess(
                command,
                0,
                "hcoona-release-smoke-npm",
                "",
            )
        message = f"unexpected quality command: {command!r}"
        raise AssertionError(message)

    monkeypatch.setattr(node_adapter, "_run", record_quality_command)

    def assert_observed_environments_are_closed() -> None:
        expected_keys = {
            "HOME",
            "LANG",
            "LC_ALL",
            "NPM_CONFIG_CACHE",
            "NPM_CONFIG_GLOBALCONFIG",
            "NPM_CONFIG_USERCONFIG",
            "PATH",
            "TZ",
            "XDG_CONFIG_HOME",
        }
        expected_user_config = (
            "audit=false\n"
            "fund=false\n"
            "ignore-scripts=true\n"
            "package-lock=false\n"
            "update-notifier=false\n"
        )
        for (
            _command,
            _cwd,
            environment,
            user_config,
            global_config_value,
            global_config_exists,
            global_config_bytes,
        ) in observed:
            assert set(environment) == expected_keys
            assert environment["LANG"] == "C.UTF-8"
            assert environment["LC_ALL"] == "C.UTF-8"
            assert environment["TZ"] == "UTC"
            home = Path(environment["HOME"])
            assert Path(environment["NPM_CONFIG_USERCONFIG"]) == home / "npmrc"
            assert Path(environment["NPM_CONFIG_CACHE"]) == home / "npm-cache"
            assert Path(environment["XDG_CONFIG_HOME"]) == home / "config"
            assert user_config == expected_user_config
            assert global_config_value is not None
            global_config_path = Path(global_config_value)
            assert global_config_path.is_relative_to(home.parent)
            assert global_config_path != Path(
                environment["NPM_CONFIG_USERCONFIG"]
            )
            assert global_config_exists
            assert global_config_bytes == b""
        if observed:
            assert all(
                cwd == observed[0][1] and environment == observed[0][2]
                for _, cwd, environment, *_ in observed
            )

    def invoke_quality_operation() -> object:
        typed_runtime_request = cast("RuntimeRequest", runtime_request)
        if operation == "project-tests":
            return node_adapter.run_node_project_tests(
                PROJECT_ROOT,
                typed_runtime_request,
            )
        assert built_result is not None
        return node_adapter.qualify_npm_install_import(
            built_result.tarball,
            built_result.expectation,
            typed_runtime_request,
        )

    if scenario in {"empty-node", "empty-npm", "surrogate", "subclass"}:
        expected_exception = (
            TypeError if scenario in {"surrogate", "subclass"} else ValueError
        )
        with pytest.raises(expected_exception) as caught:
            invoke_quality_operation()
        if scenario in {"surrogate", "subclass"}:
            assert "positional argument" not in str(caught.value)
        assert observed == []
        return

    if scenario in {"node-mismatch", "npm-mismatch"}:
        with pytest.raises(ValueError, match="version"):
            invoke_quality_operation()
        expected_probes = [("node", "--version")]
        if scenario == "npm-mismatch":
            expected_probes.append(("npm", "--version"))
        assert [command for command, *_ in observed] == expected_probes
        assert_observed_environments_are_closed()
        return

    result = invoke_quality_operation()
    commands = [command for command, *_ in observed]
    assert_observed_environments_are_closed()
    if operation == "project-tests":
        assert result is None
        assert commands == [
            ("node", "--version"),
            ("npm", "--version"),
            ("npm", "test", "--ignore-scripts"),
        ]
        return

    assert built_result is not None
    consumer = observed[2][1]
    package_specifier = json.dumps(built_result.expectation.package_name)
    import_script = (
        f"import {{smokeMessage}} from {package_specifier};"
        "process.stdout.write(smokeMessage());"
    )
    assert commands == [
        ("node", "--version"),
        ("npm", "--version"),
        (
            "npm",
            "install",
            "--ignore-scripts",
            "--no-audit",
            "--no-fund",
            "--package-lock=false",
            str(consumer / "package.tgz"),
        ),
        ("node", "--input-type=module", "-e", import_script),
    ]
    assert result == node_adapter.InstallImportResult(
        smoke_message="hcoona-release-smoke-npm",
        witness_sha256=(
            "sha256:" + hashlib.sha256(built_result.witness).hexdigest()
        ),
    )


def test_adapter_public_api_exports_closed_types_and_functions() -> None:
    runtime_request_type = getattr(node_adapter, "RuntimeRequest", None)
    assert runtime_request_type is not None, (
        "node adapter must define RuntimeRequest before exporting it"
    )
    assert getattr(adapters_package, "RuntimeRequest", None) is (
        runtime_request_type
    )

    expected_exports = (
        "PackageTargetWitness",
        "BuildRequest",
        "ArtifactExpectation",
        "ArtifactManifest",
        "BuildResult",
        "InstallImportResult",
        "RuntimeRequest",
        "build_node_package",
        "run_node_project_build",
        "run_node_project_tests",
        "qualify_npm_artifact_contents",
        "qualify_npm_install_import",
    )
    npmjs_exports = (
        "HttpResponse",
        "HttpTransport",
        "NpmjsNetworkError",
        "NpmjsPolicyError",
        "NpmjsTimeoutError",
        "NpmjsTruncatedResponseError",
        "StdlibHttpTransport",
        "observe_npmjs_projection",
    )
    github_packages_exports = (
        "ACCEPTANCE_PACKAGE_COORDINATE",
        "ACCEPTANCE_SCENARIOS",
        "ACCEPTANCE_TAGS",
        "GITHUB_PACKAGES_DESTINATION_ID",
        "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
        "GITHUB_PACKAGES_OPERATION",
        "GITHUB_PACKAGES_PACKAGE",
        "GITHUB_PACKAGES_REGISTRY",
        "GitHubPackagesHttpResponse",
        "GitHubPackagesNetworkError",
        "GitHubPackagesPolicyError",
        "GitHubPackagesPublishPreflight",
        "GitHubPackagesTimeoutError",
        "GitHubPackagesTransport",
        "FixedCoordinateAcceptanceProbeResult",
        "MutationMayHaveStartedMarker",
        "PublicationExecutionResult",
        "PublishCommandResult",
        "PublishRunner",
        "ValidatedAcceptanceRequestProof",
        "classify_github_packages_probe",
        "classify_publish_result",
        "form_mutation_may_have_started_marker",
        "observe_github_packages_projection",
        "preflight_github_packages_action",
        "publish_github_packages_action",
        "run_fixed_coordinate_acceptance_probe",
    )
    for name in expected_exports:
        module_export = getattr(node_adapter, name, None)
        assert module_export is not None, f"node adapter missing export {name}"
        assert getattr(adapters_package, name, None) is module_export
    assert set(adapters_package.__all__) == {
        *expected_exports,
        *github_packages_exports,
        *npmjs_exports,
    }

    project_tests_signature = inspect.signature(
        node_adapter.run_node_project_tests
    )
    install_import_signature = inspect.signature(
        node_adapter.qualify_npm_install_import
    )
    assert tuple(project_tests_signature.parameters) == (
        "project_root",
        "request",
    )
    assert tuple(install_import_signature.parameters) == (
        "tarball",
        "expectation",
        "request",
    )
    assert (
        project_tests_signature.parameters["request"].default
        is inspect.Parameter.empty
    )
    assert (
        install_import_signature.parameters["request"].default
        is inspect.Parameter.empty
    )
    assert get_type_hints(node_adapter.run_node_project_tests)["request"] is (
        runtime_request_type
    )
    assert (
        get_type_hints(node_adapter.qualify_npm_install_import)["request"]
        is runtime_request_type
    )
    assert tuple(
        field.name for field in dataclasses.fields(runtime_request_type)
    ) == ("node_version", "npm_version")
    assert all(
        forbidden.lower() not in export.lower()
        for export in adapters_package.__all__
        for forbidden in ("Snapshot", "Evidence", "Finalizer", "Planner")
    )


def test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands(  # noqa: E501, PLR0915
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_root = tmp_path / "valid-package"
    _copy_declared_project_inputs(source_root)
    project_test = source_root / "test/index.test.js"
    project_test.parent.mkdir(parents=True)
    shutil.copyfile(PROJECT_ROOT / "test/index.test.js", project_test)
    request = replace(
        build_request,
        source_root=source_root,
        node_version="24.4.1",
        npm_version="11.4.2",
    )
    runtime_request = _make_runtime_request(
        node_version="v24.4.1",
        npm_version="11.4.2",
    )
    observed_commands: list[tuple[str, ...]] = []
    build_output_destinations: list[Path] = []
    consumer_tarball_copies: list[tuple[Path, Path]] = []
    observed_import_scripts: list[str] = []
    fixed_import_script = (
        "import {smokeMessage} from "
        '"@hcoona/hcoona-release-smoke-npm";'
        "process.stdout.write(smokeMessage());"
    )

    def record_and_emulate(  # noqa: PLR0911
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del environment
        argv = tuple(command)
        observed_commands.append(argv)
        if argv == ("node", "--version"):
            return subprocess.CompletedProcess(argv, 0, "v24.4.1\n", "")
        if argv == ("pnpm", "--version"):
            return subprocess.CompletedProcess(
                argv,
                0,
                f"{request.pnpm_version}\n",
                "",
            )
        if argv == ("npm", "--version"):
            return subprocess.CompletedProcess(argv, 0, "11.4.2\n", "")
        if argv == ("node", "scripts/build.mjs"):
            built_entry = cwd / "dist/index.js"
            built_entry.parent.mkdir(parents=True, exist_ok=True)
            built_entry.write_bytes((cwd / "src/index.js").read_bytes())
            return subprocess.CompletedProcess(argv, 0, "", "")
        if argv[:2] == ("npm", "pack"):
            output_root = Path(argv[-1])
            build_output_destinations.append(output_root)
            manifest = json.loads((cwd / "package.json").read_text())
            basename = (
                manifest["name"].removeprefix("@").replace("/", "-")
                + "-"
                + manifest["version"]
                + ".tgz"
            )
            tarball = _make_tarball(
                {
                    "package/README.md": (cwd / "README.md").read_bytes(),
                    "package/dist/index.js": (
                        cwd / "dist/index.js"
                    ).read_bytes(),
                    "package/package.json": (cwd / "package.json").read_bytes(),
                    "package/workflow-delivery/provenance.json": (
                        cwd / "workflow-delivery/provenance.json"
                    ).read_bytes(),
                }
            )
            (output_root / basename).write_bytes(tarball)
            return subprocess.CompletedProcess(
                argv,
                0,
                json.dumps([{"filename": basename}]),
                "",
            )
        if argv[:2] == ("npm", "install"):
            tarball_path = Path(argv[-1])
            consumer_tarball_copies.append((cwd, tarball_path))
            package_root = (
                cwd / "node_modules" / "@hcoona/hcoona-release-smoke-npm"
            )
            for name, content in _tar_entries(
                tarball_path.read_bytes()
            ).items():
                destination = package_root / name.removeprefix("package/")
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(content)
            return subprocess.CompletedProcess(argv, 0, "", "")
        if argv[:2] == ("node", "--input-type=module"):
            if len(argv) == EXPECTED_IMPORT_COMMAND_ARG_COUNT:
                observed_import_scripts.append(argv[3])
            return subprocess.CompletedProcess(
                argv,
                0,
                "hcoona-release-smoke-npm",
                "",
            )
        return subprocess.CompletedProcess(argv, 0, "passed", "")

    monkeypatch.setattr(node_adapter, "_run", record_and_emulate)

    built_result = node_adapter.build_node_package(request)
    node_adapter.run_node_project_build(request)
    node_adapter.run_node_project_tests(source_root, runtime_request)
    node_adapter.qualify_npm_install_import(
        built_result.tarball,
        built_result.expectation,
        runtime_request,
    )

    observed_build_output = (
        build_output_destinations[0]
        if build_output_destinations
        else Path("<missing-build-output>")
    )
    observed_consumer_tarball = (
        consumer_tarball_copies[0][1]
        if consumer_tarball_copies
        else Path("<missing-consumer-tarball>")
    )
    expected_commands = [
        ("node", "--version"),
        ("pnpm", "--version"),
        ("npm", "--version"),
        ("node", "scripts/build.mjs"),
        (
            "npm",
            "pack",
            "--ignore-scripts",
            "--json",
            "--pack-destination",
            str(observed_build_output),
        ),
        ("node", "--version"),
        ("pnpm", "--version"),
        ("npm", "--version"),
        ("node", "scripts/build.mjs"),
        ("node", "--version"),
        ("npm", "--version"),
        ("npm", "test", "--ignore-scripts"),
        ("node", "--version"),
        ("npm", "--version"),
        (
            "npm",
            "install",
            "--ignore-scripts",
            "--no-audit",
            "--no-fund",
            "--package-lock=false",
            str(observed_consumer_tarball),
        ),
        (
            "node",
            "--input-type=module",
            "-e",
            fixed_import_script,
        ),
    ]

    lowered_commands = tuple(
        " ".join(command).lower() for command in observed_commands
    )
    assert all(
        "nbgv-version.mjs" not in command for command in lowered_commands
    )
    assert all("stamp" not in command for command in lowered_commands)
    assert all(
        token.lower() != "reset"
        for command in observed_commands
        for token in command
    )
    restoration_prefixes = (
        "git checkout",
        "git restore",
        "git reset",
        "git clean",
    )
    assert all(
        not (command == prefix or command.startswith(f"{prefix} "))
        for command in lowered_commands
        for prefix in restoration_prefixes
    )

    assert observed_commands == expected_commands
    assert observed_commands[0:4] == expected_commands[0:4]
    assert observed_commands[5:9] == expected_commands[5:9]
    assert observed_commands[9:12] == expected_commands[9:12]
    assert observed_commands[12:16] == expected_commands[12:16]
    assert build_output_destinations == [observed_build_output]
    assert observed_build_output.name == "output"
    assert not observed_build_output.is_relative_to(source_root.resolve())
    assert consumer_tarball_copies == [
        (observed_consumer_tarball.parent, observed_consumer_tarball)
    ]
    assert observed_consumer_tarball.name == "package.tgz"
    assert observed_import_scripts == [fixed_import_script]
