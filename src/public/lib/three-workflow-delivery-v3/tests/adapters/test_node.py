"""Scenarios for first-slice isolated Node Build and Quality Adapters."""

from __future__ import annotations

# ruff: noqa: C901, D103, I001

import dataclasses
import gzip
import hashlib
import io
import json
import os
import shutil
import subprocess
import tarfile
from collections import Counter
from contextlib import contextmanager
from dataclasses import replace
from itertools import pairwise
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest
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


@pytest.fixture(scope="module")
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


@pytest.fixture(scope="module")
def build_request(witness: PackageTargetWitness) -> BuildRequest:
    """Return a closed request using only declared project/build inputs."""
    return BuildRequest(
        source_root=PROJECT_ROOT,
        declared_inputs=DECLARED_INPUTS,
        npm_package_version=NPM_VERSION,
        witness=witness,
        source_date_epoch=1_700_000_000,
        node_version="24.19.0",
        pnpm_version="11.22.0",
        npm_version="11.17.0",
    )


@pytest.fixture(scope="module")
def native_build_request(build_request: BuildRequest) -> BuildRequest:
    """Freeze the installed toolchain once for native integration scenarios."""
    versions = {
        tool: subprocess.check_output(  # noqa: S603
            (tool, "--version"), text=True
        ).strip()
        for tool in ("node", "pnpm", "npm")
    }
    return replace(
        build_request,
        node_version=versions["node"].removeprefix("v"),
        pnpm_version=versions["pnpm"],
        npm_version=versions["npm"],
    )


@pytest.fixture(scope="module")
def built_result(
    native_build_request: BuildRequest,
) -> node_adapter.BuildResult:
    """Build the real smoke package once for artifact quality scenarios."""
    return build_node_package(native_build_request)


def _source_snapshot(
    source_root: Path = PROJECT_ROOT,
) -> dict[str, tuple[str, bytes | str]]:
    snapshot: dict[str, tuple[str, bytes | str]] = {}
    for path in sorted(source_root.rglob("*")):
        relative_path = path.relative_to(source_root)
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


@pytest.fixture
def raw_tarball_seed() -> tuple[bytes, dict[str, bytes]]:
    """Provide independently expected ordinary entries for raw format cases."""
    entries = {
        "package/README.md": b"Raw TAR fixture.\n",
        "package/dist/index.js": b"export const fixture = true;\n",
        "package/package.json": b'{"name":"@example/raw-tar-fixture"}\n',
        "package/workflow-delivery/provenance.json": (
            b'{"fixture":"raw-format"}\n'
        ),
    }
    tarball = _make_tarball(entries)

    assert node_adapter._read_tarball(tarball) == entries  # noqa: SLF001
    assert tuple(member[1] for member in _tar_member_observables(tarball)) == (
        "package/README.md",
        "package/dist/index.js",
        "package/package.json",
        "package/workflow-delivery/provenance.json",
    )
    return tarball, entries


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


def test_source_snapshot_covers_controlled_source_projection(
    tmp_path: Path,
) -> None:
    source = tmp_path / "source"
    (source / "src").mkdir(parents=True)
    (source / "docs").mkdir()
    (source / "node_modules").mkdir()
    (source / "nested/node_modules").mkdir(parents=True)
    (source / "package.json").write_bytes(b'{"name":"source-projection"}\n')
    (source / "src/index.js").write_bytes(b"export const value = 1;\n")
    (source / "docs/unlisted.txt").write_bytes(b"unlisted source document\n")
    (source / "link.txt").symlink_to("docs/unlisted.txt")
    (source / "node_modules/excluded.txt").write_bytes(b"excluded root\n")
    (source / "nested/node_modules/excluded.txt").write_bytes(
        b"excluded nested\n"
    )

    before = _source_snapshot(source)

    assert before == {
        "package.json": ("file", b'{"name":"source-projection"}\n'),
        "src/index.js": ("file", b"export const value = 1;\n"),
        "docs/unlisted.txt": ("file", b"unlisted source document\n"),
        "link.txt": ("symlink", "docs/unlisted.txt"),
    }

    (source / "package.json").write_bytes(b'{"name":"changed-projection"}\n')
    (source / "src/index.js").unlink()
    (source / "extra").mkdir()
    (source / "extra/new.txt").write_bytes(b"added source file\n")
    after = _source_snapshot(source)

    assert after == {
        "package.json": ("file", b'{"name":"changed-projection"}\n'),
        "docs/unlisted.txt": ("file", b"unlisted source document\n"),
        "link.txt": ("symlink", "docs/unlisted.txt"),
        "extra/new.txt": ("file", b"added source file\n"),
    }
    assert after != before


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
            return subprocess.CompletedProcess(command, 0, "v24.19.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.22.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.17.0\n", "")
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
    native_build_request: BuildRequest,
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
                native_build_request,
                source_root=project,
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
    native_build_request: BuildRequest,
    field: str,
    value: str,
    message: str,
) -> None:
    with pytest.raises(ValueError, match=message):
        build_node_package(
            replace(native_build_request, **cast("Any", {field: value}))
        )


@pytest.mark.parametrize(
    ("frozen_node", "reported_node"),
    [
        ("24.14.0", "v24.14.0"),
        ("24.14.0", "24.14.0"),
        ("v24.14.0", "v24.14.0"),
        ("v24.14.0", "24.14.0"),
    ],
    ids=[
        "plain-to-cli-prefixed",
        "plain-to-plain",
        "cli-prefixed-to-cli-prefixed",
        "cli-prefixed-to-plain",
    ],
)
@pytest.mark.parametrize(
    "verification",
    ["build", "quality"],
    ids=["build", "quality"],
)
def test_node_runtime_version_accepts_only_the_optional_cli_prefix(
    build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
    frozen_node: str,
    reported_node: str,
    verification: str,
) -> None:
    """Treat Node's CLI marker consistently without normalizing npm or PNPM."""

    def report_version(
        command: tuple[str, ...],
        _cwd: Path,
        _environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        versions: dict[tuple[str, ...], str] = {
            ("node", "--version"): reported_node,
            ("pnpm", "--version"): build_request.pnpm_version,
            ("npm", "--version"): build_request.npm_version,
        }
        return subprocess.CompletedProcess(
            command,
            0,
            f"{versions[command]}\n",
            "",
        )

    monkeypatch.setattr(node_adapter, "_run", report_version)
    if verification == "build":
        node_adapter._verify_toolchain(  # noqa: SLF001
            replace(build_request, node_version=frozen_node),
            {},
        )
        return
    node_adapter._verify_quality_toolchain(  # noqa: SLF001
        _make_runtime_request(
            node_version=frozen_node,
            npm_version=build_request.npm_version,
        ),
        PROJECT_ROOT,
        {},
    )


def test_build_is_deterministic_across_process_umasks_and_normalizes_modes(
    native_build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    before = _source_snapshot()
    source_manifest = json.loads(
        (native_build_request.source_root / "package.json").read_text()
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
                results[mask] = build_node_package(native_build_request)
                assert _source_snapshot() == before
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

    assert permissive.manifest.entries == (
        "package/README.md",
        "package/dist/index.js",
        "package/package.json",
        "package/workflow-delivery/provenance.json",
    )
    assert permissive.manifest.lifecycle_scripts == (
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
    assert permissive.expectation.files_allowlist == (
        "dist",
        "README.md",
        "workflow-delivery/provenance.json",
    )
    assert permissive.witness == native_build_request.witness.canonical_bytes
    assert permissive.toolchain == (
        ("node", native_build_request.node_version),
        ("pnpm", native_build_request.pnpm_version),
        ("npm", native_build_request.npm_version),
        ("adapter", "node/npm-package-v1"),
    )
    assert tuple(
        path for path, _digest in permissive.source_input_manifest
    ) == (DECLARED_INPUTS)
    assert all(
        digest.startswith("sha256:") and len(digest) == PREFIXED_SHA256_LENGTH
        for _path, digest in permissive.source_input_manifest
    )
    assert _source_snapshot() == before


def test_lifecycle_evidence_binds_every_manifest_script(
    native_build_request: BuildRequest,
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
            native_build_request,
            source_root=project,
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
            return subprocess.CompletedProcess(command, 0, "v24.19.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.22.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.17.0\n", "")
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
                failed_command[0][-1],
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
                failed_command[0][-1],
            ),
        ],
    }
    assert Counter(observed_commands) == Counter(expected_commands[failure])
    queries = [
        command
        for command in expected_commands[failure]
        if command[-1] == "--version"
    ]
    operations = [
        command
        for command in expected_commands[failure]
        if command not in queries
    ]
    assert all(
        observed_commands.index(query) < observed_commands.index(operation)
        for query in queries
        for operation in operations
    )
    if failure == "pack":
        assert observed_commands.index(("node", "scripts/build.mjs")) < (
            observed_commands.index(failed_command[0])
        )
    assert observed_commands[-1] == failed_command[0]
    assert _source_snapshot() == before


def _assert_owned_node_state(environment: dict[str, str]) -> None:
    home = Path(environment["HOME"])
    assert home.is_dir()
    assert not home.is_relative_to(PROJECT_ROOT.resolve())
    state_keys = (
        "NPM_CONFIG_USERCONFIG",
        "NPM_CONFIG_GLOBALCONFIG",
        "NPM_CONFIG_CACHE",
        "XDG_CONFIG_HOME",
    )
    paths = [Path(environment[key]) for key in state_keys]
    assert len(set(paths)) == len(paths)
    assert all(path.is_relative_to(home.parent) for path in paths)
    assert Path(environment["NPM_CONFIG_USERCONFIG"]).is_file()
    assert Path(environment["NPM_CONFIG_GLOBALCONFIG"]).is_file()
    assert Path(environment["NPM_CONFIG_CACHE"]).is_dir()


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
        node_version="v24.19.0",
        npm_version="11.17.0",
    )

    def record(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        _assert_owned_node_state(environment)
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
            return subprocess.CompletedProcess(command, 0, "v24.19.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.17.0\n", "")
        assert command == ("npm", "test", "--ignore-scripts")
        return subprocess.CompletedProcess(command, 0, "passed", "")

    monkeypatch.setenv("NODE_AUTH_TOKEN", "secret")
    monkeypatch.setenv("NPM_TOKEN", "secret")
    monkeypatch.setenv("GITHUB_TOKEN", "secret")
    monkeypatch.setenv("UNRELATED_SENTINEL", "ambient")
    monkeypatch.setattr(node_adapter, "_run", record)

    run_node_project_tests(PROJECT_ROOT, runtime_request)

    commands = [command for command, *_ in observed]
    assert Counter(commands) == Counter(
        [
            ("node", "--version"),
            ("npm", "--version"),
            ("npm", "test", "--ignore-scripts"),
        ]
    )
    test_index = commands.index(("npm", "test", "--ignore-scripts"))
    for tool in ("node", "npm"):
        assert commands.index((tool, "--version")) < test_index
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
        assert cwd.is_relative_to(Path(environment["HOME"]).parent)
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
    native_build_request: BuildRequest,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    original_run = node_adapter._run  # noqa: SLF001
    runtime_request = _make_runtime_request(
        node_version=f"v{native_build_request.node_version}",
        npm_version=native_build_request.npm_version,
    )
    observations: dict[
        str,
        list[tuple[tuple[str, ...], Path, dict[str, str], str, bytes]],
    ] = {}
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
        _assert_owned_node_state(environment)
        if command[:2] == ("npm", "pack"):
            output_root = Path(command[-1])
            assert output_root.is_dir()
            assert output_root.is_relative_to(Path(environment["HOME"]).parent)
            assert not output_root.is_relative_to(PROJECT_ROOT.resolve())
        if command[:2] == ("npm", "install"):
            tarball_path = Path(command[-1])
            assert tarball_path.is_relative_to(cwd)
            assert tarball_path.read_bytes() == built_result.tarball
        observations[operation].append(
            (
                command,
                cwd,
                dict(environment),
                Path(environment["NPM_CONFIG_USERCONFIG"]).read_text(),
                Path(environment["NPM_CONFIG_GLOBALCONFIG"]).read_bytes(),
            )
        )
        return original_run(command, cwd, environment)

    monkeypatch.setattr(node_adapter, "_run", record_and_run)

    before = _source_snapshot()
    operation = "artifact-build"
    observations[operation] = []
    built_result = build_node_package(native_build_request)
    assert _source_snapshot() == before
    operation = "project-build"
    observations[operation] = []
    run_node_project_build(native_build_request)
    assert _source_snapshot() == before
    operation = "project-tests"
    observations[operation] = []
    run_node_project_tests(PROJECT_ROOT, runtime_request)
    assert _source_snapshot() == before
    operation = "install-import"
    observations[operation] = []
    result = qualify_npm_install_import(
        built_result.tarball,
        built_result.expectation,
        runtime_request,
    )
    assert _source_snapshot() == before
    assert result.witness_sha256 == (
        "sha256:" + hashlib.sha256(built_result.witness).hexdigest()
    )
    assert result.smoke_message == "hcoona-release-smoke-npm"

    action_roles = {
        "artifact-build": [("node", "scripts/build.mjs"), ("npm", "pack")],
        "project-build": [("node", "scripts/build.mjs")],
        "project-tests": [("npm", "test")],
        "install-import": [("npm", "install"), ("node", "--input-type=module")],
    }
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
    for operation, group in observations.items():
        is_build = operation in {"artifact-build", "project-build"}
        probes = [
            ("node", "--version"),
            ("npm", "--version"),
            *([("pnpm", "--version")] if is_build else []),
        ]
        actions = action_roles[operation]
        roles = [command[:2] for command, *_ in group]
        assert Counter(roles) == Counter([*probes, *actions])
        by_role = {item[0][:2]: item for item in group}
        for probe in probes:
            assert by_role[probe][0] == probe
            for action in actions:
                assert roles.index(probe) < roles.index(action)
        for earlier, later in pairwise(actions):
            assert roles.index(earlier) < roles.index(later)
        operation_cwd = by_role[actions[0]][1]
        operation_environment = by_role[actions[0]][2]
        for action in actions:
            assert by_role[action][1] == operation_cwd
        assert not is_build or by_role[("node", "scripts/build.mjs")][0] == (
            "node",
            "scripts/build.mjs",
        )
        if operation == "artifact-build":
            assert by_role[("npm", "pack")][0][:-1] == (
                "npm",
                "pack",
                "--ignore-scripts",
                "--json",
                "--pack-destination",
            )
        elif operation == "project-tests":
            assert by_role[("npm", "test")][0] == (
                "npm",
                "test",
                "--ignore-scripts",
            )
        elif operation == "install-import":
            assert by_role[("npm", "install")][0][:-1] == (
                "npm",
                "install",
                "--ignore-scripts",
                "--no-audit",
                "--no-fund",
                "--package-lock=false",
            )
            import_command = by_role[("node", "--input-type=module")][0]
            assert import_command[:3] == ("node", "--input-type=module", "-e")
            assert len(import_command) == EXPECTED_IMPORT_COMMAND_ARG_COUNT
            assert import_command[-1]
        for command, cwd, environment, npm_config, global_config in group:
            assert environment == operation_environment
            if command[:2] in actions:
                assert not cwd.is_relative_to(PROJECT_ROOT.resolve())
                assert cwd.is_relative_to(Path(environment["HOME"]).parent)
            expected_keys = {
                *safe_environment_keys,
                *({"SOURCE_DATE_EPOCH"} if is_build else set()),
            }
            assert environment.get("SOURCE_DATE_EPOCH") == (
                str(native_build_request.source_date_epoch)
                if is_build
                else None
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
            assert environment["LANG"] == "C.UTF-8"
            assert environment["LC_ALL"] == "C.UTF-8"
            assert environment["TZ"] == "UTC"
            assert "ambient-secret" not in environment.values()
            assert npm_config == (
                "audit=false\n"
                "fund=false\n"
                "ignore-scripts=true\n"
                "package-lock=false\n"
                "update-notifier=false\n"
            )
            assert global_config == b""

    for key in (
        "HOME",
        "NPM_CONFIG_USERCONFIG",
        "NPM_CONFIG_GLOBALCONFIG",
        "NPM_CONFIG_CACHE",
        "XDG_CONFIG_HOME",
    ):
        owned_paths = {
            environment[key]
            for group in observations.values()
            for _, _, environment, *_ in group
        }
        assert len(owned_paths) == EXPECTED_ISOLATED_HOME_COUNT


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


def test_artifact_contents_rejects_incomplete_expected_file_closure(
    built_result: node_adapter.BuildResult,
) -> None:
    expectation = replace(
        built_result.expectation,
        files_allowlist=("dist", "README.md"),
    )

    with pytest.raises(ValueError, match="first-slice closure"):
        qualify_npm_artifact_contents(built_result.tarball, expectation)


def test_install_import_rejects_mutated_artifact_export(
    built_result: node_adapter.BuildResult,
) -> None:
    toolchain = dict(built_result.toolchain)
    runtime_request = _make_runtime_request(
        node_version=toolchain["node"],
        npm_version=toolchain["npm"],
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
    runner_staging_roots: list[Path] = []
    runner_staged_sources: list[dict[str, bytes]] = []
    packed_evidence_bytes: list[bytes] = []
    evidence_marker = b"\n/* declared-input-evidence\n"
    original_read_bytes = Path.read_bytes

    def capture_source_read(path: Path) -> bytes:
        resolved_path = path.resolve()
        if resolved_path not in source_by_path:
            return original_read_bytes(path)
        relative = source_by_path[resolved_path]
        read_counts[resolved_path] += 1
        content = original_read_bytes(path)
        if read_counts[resolved_path] == 1:
            captured_sources[relative] = content
            path.write_bytes(mutated_sources[relative])
        return content

    def deterministic_runner(
        command: tuple[str, ...],
        cwd: Path,
        environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        del environment
        if command == ("node", "--version"):
            return subprocess.CompletedProcess(command, 0, "v24.19.0\n", "")
        if command == ("pnpm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.22.0\n", "")
        if command == ("npm", "--version"):
            return subprocess.CompletedProcess(command, 0, "11.17.0\n", "")
        if command == ("node", "scripts/build.mjs"):
            runner_staging_roots.append(cwd.resolve())
            runner_staged_sources.append(
                {
                    relative: original_read_bytes(cwd / relative)
                    for relative in DECLARED_INPUTS
                }
            )
            runner_sources = runner_staged_sources[-1]
            packed_evidence = canonicalize(
                {
                    relative: {
                        "byte-size": len(runner_sources[relative]),
                        "bytes-hex": runner_sources[relative].hex(),
                        "sha256": (
                            "sha256:"
                            + hashlib.sha256(
                                runner_sources[relative]
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
    monkeypatch.setattr(node_adapter, "_run", deterministic_runner)

    result = build_node_package(
        replace(build_request, source_root=resolved_project)
    )

    assert all(
        source.is_relative_to(resolved_project) and source.is_file()
        for source in resolved_sources.values()
    )
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
    assert len(runner_staging_roots) == 1
    assert not runner_staging_roots[0].is_relative_to(resolved_project)
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
    expected_runner_manifest = {
        **original_manifest,
        "version": build_request.npm_package_version,
        "files": ["dist", "README.md", "workflow-delivery/provenance.json"],
    }
    assert runner_manifest == expected_runner_manifest

    packed_entries = _tar_entries(result.tarball)
    assert (
        packed_entries["package/package.json"]
        == (runner_sources["package.json"])
    )
    assert json.loads(packed_entries["package/package.json"]) == (
        expected_runner_manifest
    )
    assert packed_entries["package/workflow-delivery/provenance.json"] == (
        build_request.witness.canonical_bytes
    )
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
        expected_bytes = (
            runner_sources[relative]
            if relative == "package.json"
            else original_sources[relative]
        )
        assert evidence["byte-size"] == len(expected_bytes)
        assert evidence["sha256"] == (
            "sha256:" + hashlib.sha256(expected_bytes).hexdigest()
        )
        assert bytes.fromhex(evidence["bytes-hex"]) == expected_bytes
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
    assert manifest.byte_size == len(built_result.tarball)
    assert manifest.basename == (
        "hcoona-hcoona-release-smoke-npm-1.2.3-beta.42.ge123456.tgz"
    )
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
def test_tarball_reader_rejects_gnu_long_name_or_long_link_header(
    raw_tarball_seed: tuple[bytes, dict[str, bytes]],
    extension_kind: str,
    physical_type: bytes,
) -> None:
    original_tarball, original_entries = raw_tarball_seed
    original_payload = gzip.decompress(original_tarball)
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
    with pytest.raises(ValueError):  # noqa: PT011 - Reader wording is internal.
        node_adapter._read_tarball(extension_tarball)  # noqa: SLF001


@pytest.mark.parametrize(
    ("extension_kind", "physical_type"),
    [
        pytest.param("pax-extended", tarfile.XHDTYPE, id="pax-local-x"),
        pytest.param("pax-global", tarfile.XGLTYPE, id="pax-global-g"),
    ],
)
def test_tarball_reader_rejects_pax_physical_header(
    raw_tarball_seed: tuple[bytes, dict[str, bytes]],
    extension_kind: str,
    physical_type: bytes,
) -> None:
    original_tarball, original_entries = raw_tarball_seed
    original_payload = gzip.decompress(original_tarball)
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
    with pytest.raises(ValueError):  # noqa: PT011 - Reader wording is internal.
        node_adapter._read_tarball(extension_tarball)  # noqa: SLF001


@pytest.mark.parametrize(
    ("profile_kind", "replacements"),
    [
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
def test_tarball_reader_rejects_noncanonical_ustar_magic_or_version(
    raw_tarball_seed: tuple[bytes, dict[str, bytes]],
    profile_kind: str,
    replacements: dict[str, bytes],
) -> None:
    original_tarball, original_entries = raw_tarball_seed
    mutated_tarball = _tarball_with_first_header_fields(
        original_tarball,
        replacements,
    )
    mutated_header = gzip.decompress(mutated_tarball)[: tarfile.BLOCKSIZE]

    assert profile_kind
    assert mutated_header[257:265] != b"ustar\000"
    assert _tar_entries(mutated_tarball) == original_entries
    with pytest.raises(ValueError):  # noqa: PT011 - Reader wording is internal.
        node_adapter._read_tarball(mutated_tarball)  # noqa: SLF001


def test_tarball_reader_checks_hidden_name_suffix_in_later_member(
    raw_tarball_seed: tuple[bytes, dict[str, bytes]],
) -> None:
    """Check physical name tails after preceding valid members."""
    member_index = 2
    original_tarball, original_entries = raw_tarball_seed
    original_payload = gzip.decompress(original_tarball)
    original_observables = _tar_member_observables(original_tarball)
    member_offset = cast("int", original_observables[member_index][13])
    name_start, name_end = TAR_HEADER_FIELDS["name"]
    absolute_start = member_offset + name_start
    absolute_end = member_offset + name_end
    original_name = original_payload[absolute_start:absolute_end]
    first_nul = original_name.index(0)
    mutated_name = bytearray(original_name)
    mutated_name[first_nul + 1] = NONZERO_PADDING_BYTE
    replacement = bytes(mutated_name)
    mutated_tarball = _tarball_with_member_header_fields(
        original_tarball,
        member_index,
        {"name": replacement},
    )
    mutated_payload = gzip.decompress(mutated_tarball)

    assert len(original_observables) == len(original_entries)
    assert original_observables[member_index][0:2] == (
        member_index,
        "package/package.json",
    )
    assert original_name != replacement
    assert mutated_payload[absolute_start:absolute_end] == replacement
    assert _tar_member_observables(mutated_tarball) == original_observables
    assert _tar_entries(mutated_tarball) == original_entries
    with pytest.raises(ValueError):  # noqa: PT011 - Reader wording is internal.
        node_adapter._read_tarball(mutated_tarball)  # noqa: SLF001


@pytest.mark.parametrize("field", ["name", "linkname", "uname", "prefix"])
def test_artifact_contents_checks_fixed_string_field_tail(
    built_result: node_adapter.BuildResult,
    field: str,
) -> None:
    original_header = gzip.decompress(built_result.tarball)[: tarfile.BLOCKSIZE]
    start, end = TAR_HEADER_FIELDS[field]
    original_field = original_header[start:end]
    first_nul = original_field.index(0)
    replacement = bytearray(original_field)
    mutation_index = len(original_field) - 1
    replacement[mutation_index] = NONZERO_PADDING_BYTE
    mutated_tarball = _tarball_with_first_header_fields(
        built_result.tarball,
        {field: bytes(replacement)},
    )
    mutated_field = gzip.decompress(mutated_tarball)[start:end]

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
        pytest.param(
            "uid",
            bytes(7) + b"X",
            id="uid-hidden-suffix",
        ),
        pytest.param(
            "linkname",
            b"X" + bytes(99),
            id="linkname-nonempty",
        ),
        pytest.param(
            "prefix",
            b"X" + bytes(154),
            id="prefix-nonempty",
        ),
        pytest.param(
            "reserved",
            bytes(11) + bytes((NONZERO_PADDING_BYTE,)),
            id="reserved-nonzero-final",
        ),
    ],
)
def test_artifact_contents_rejects_unsupported_physical_header_fields(
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
        pytest.param("mode", b"000644\0X", b" \0", id="mode-hidden-suffix"),
        pytest.param(
            "size",
            b"0000000110\0X",
            b" \0",
            id="size-hidden-suffix",
        ),
        pytest.param("mode", None, b" \0", id="mode-base256"),
        pytest.param("size", None, b" \0", id="size-base256"),
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


def test_artifact_contents_rejects_arithmetic_checksum_mismatch(
    built_result: node_adapter.BuildResult,
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

    assert incorrect_checksum[-2:] == b" \0"
    assert all(ord("0") <= byte <= ord("7") for byte in incorrect_checksum[:-2])
    assert mutated_payload[:checksum_start] == original_payload[:checksum_start]
    assert mutated_payload[checksum_end:] == original_payload[checksum_end:]

    with pytest.raises(ValueError, match=r"^invalid npm tarball$"):
        qualify_npm_artifact_contents(
            mutated_tarball,
            built_result.expectation,
        )


@pytest.mark.parametrize(
    ("type_flag", "type_name"),
    [
        pytest.param(tarfile.LNKTYPE, "hard-link", id="hard-link-1"),
        pytest.param(tarfile.SYMTYPE, "symbolic-link", id="symbolic-link-2"),
        pytest.param(tarfile.CHRTYPE, "character-device", id="char-device-3"),
        pytest.param(tarfile.DIRTYPE, "directory", id="directory-5"),
        pytest.param(tarfile.FIFOTYPE, "fifo", id="fifo-6"),
        pytest.param(tarfile.GNUTYPE_SPARSE, "gnu-sparse", id="gnu-sparse-S"),
        pytest.param(b"?", "unknown-special", id="unknown-special-question"),
    ],
)
def test_tarball_reader_rejects_nonregular_and_unsupported_members(
    raw_tarball_seed: tuple[bytes, dict[str, bytes]],
    type_flag: bytes,
    type_name: str,
) -> None:
    original_tarball, original_entries = raw_tarball_seed
    original_payload = gzip.decompress(original_tarball)
    original_names = list(original_entries)
    regular_header = _special_tar_header(original_payload, tarfile.REGTYPE)
    regular_tarball = gzip.compress(regular_header + original_payload, mtime=0)
    assert node_adapter._read_tarball(regular_tarball) == {  # noqa: SLF001
        "package/special-entry": b"",
        **original_entries,
    }
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
    with pytest.raises(ValueError):  # noqa: PT011 - Reader wording is internal.
        node_adapter._read_tarball(special_tarball)  # noqa: SLF001


@pytest.mark.parametrize(
    "invalid_kind",
    [
        pytest.param("malformed", id="malformed-gzip"),
        pytest.param("missing-trailer", id="missing-gzip-trailer"),
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


def test_runtime_request_is_frozen() -> None:
    request = RuntimeRequest(
        node_version="v24.4.1",
        npm_version="11.4.2",
    )

    with pytest.raises(dataclasses.FrozenInstanceError):
        cast("Any", request).node_version = "v24.4.2"


@pytest.mark.parametrize(
    ("scenario", "expected_exception", "message"),
    [
        pytest.param(
            "empty-node",
            ValueError,
            "Node version must be frozen",
            id="empty-node-version",
        ),
        pytest.param(
            "empty-npm",
            ValueError,
            "npm version must be frozen",
            id="empty-npm-version",
        ),
    ],
)
def test_project_tests_reject_malformed_runtime_requests_before_commands(
    monkeypatch: pytest.MonkeyPatch,
    scenario: str,
    expected_exception: type[Exception],
    message: str,
) -> None:
    node_version = "" if scenario == "empty-node" else "v24.4.1"
    npm_version = "" if scenario == "empty-npm" else "11.4.2"
    runtime_request = RuntimeRequest(
        node_version=node_version, npm_version=npm_version
    )
    observed: list[tuple[str, ...]] = []

    def reject_command(
        command: tuple[str, ...],
        _cwd: Path,
        _environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        observed.append(command)
        pytest.fail(f"malformed Runtime Request reached a command: {command!r}")

    monkeypatch.setattr(node_adapter, "_run", reject_command)
    with pytest.raises(expected_exception, match=message):
        run_node_project_tests(PROJECT_ROOT, runtime_request)
    assert observed == []


def test_install_import_rejects_malformed_runtime_request_before_commands(
    built_result: node_adapter.BuildResult,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    observed: list[tuple[str, ...]] = []

    def reject_command(
        command: tuple[str, ...],
        _cwd: Path,
        _environment: dict[str, str],
    ) -> subprocess.CompletedProcess[str]:
        observed.append(command)
        pytest.fail(f"malformed Runtime Request reached a command: {command!r}")

    monkeypatch.setattr(node_adapter, "_run", reject_command)
    with pytest.raises(ValueError, match="Node version must be frozen"):
        qualify_npm_install_import(
            built_result.tarball,
            built_result.expectation,
            RuntimeRequest(node_version="", npm_version="11.4.2"),
        )
    assert observed == []


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
    ],
)
def test_quality_adapters_probe_frozen_runtime_before_operations(  # noqa: PLR0915
    monkeypatch: pytest.MonkeyPatch,
    request: pytest.FixtureRequest,
    operation: str,
    scenario: str,
) -> None:
    runtime_request = RuntimeRequest(
        node_version="v24.4.1", npm_version="11.4.2"
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
        _assert_owned_node_state(environment)
        assert not cwd.is_relative_to(PROJECT_ROOT.resolve())
        assert cwd.is_relative_to(Path(environment["HOME"]).parent)
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
            tarball_path = Path(command[-1])
            assert tarball_path.is_relative_to(cwd)
            assert tarball_path.read_bytes() == built_result.tarball
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

    probes = [("node", "--version"), ("npm", "--version")]
    if scenario in {"node-mismatch", "npm-mismatch"}:
        with pytest.raises(ValueError, match="version"):
            invoke_quality_operation()
        commands = [command for command, *_ in observed]
        assert Counter(commands) <= Counter(probes)
        mismatching_tool = "node" if scenario == "node-mismatch" else "npm"
        assert (mismatching_tool, "--version") in commands
        assert_observed_environments_are_closed()
        return

    result = invoke_quality_operation()
    commands = [command for command, *_ in observed]
    assert_observed_environments_are_closed()
    actions = (
        [("npm", "test")]
        if operation == "project-tests"
        else [("npm", "install"), ("node", "--input-type=module")]
    )
    roles = [command[:2] for command in commands]
    assert Counter(roles) == Counter([*probes, *actions])
    by_role = {command[:2]: command for command in commands}
    for probe in probes:
        assert by_role[probe] == probe
        for action in actions:
            assert roles.index(probe) < roles.index(action)
    if operation == "project-tests":
        assert result is None
        assert by_role[("npm", "test")] == ("npm", "test", "--ignore-scripts")
        return

    assert built_result is not None
    assert roles.index(("npm", "install")) < roles.index(
        ("node", "--input-type=module")
    )
    assert by_role[("npm", "install")][:-1] == (
        "npm",
        "install",
        "--ignore-scripts",
        "--no-audit",
        "--no-fund",
        "--package-lock=false",
    )
    import_command = by_role[("node", "--input-type=module")]
    assert import_command[:3] == ("node", "--input-type=module", "-e")
    assert len(import_command) == EXPECTED_IMPORT_COMMAND_ARG_COUNT
    assert import_command[-1]
    assert result == node_adapter.InstallImportResult(
        smoke_message="hcoona-release-smoke-npm",
        witness_sha256=(
            "sha256:" + hashlib.sha256(built_result.witness).hexdigest()
        ),
    )


def test_adapter_operations_preserve_required_commands_and_effect_boundaries(  # noqa: PLR0915
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
        node_version="24.4.1",
        npm_version="11.4.2",
    )
    observed_commands: list[tuple[str, ...]] = []
    build_output_destinations: list[Path] = []
    consumer_tarball_copies: list[tuple[Path, Path, bytes]] = []

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
            consumer_tarball_copies.append(
                (
                    cwd.resolve(),
                    tarball_path.resolve(),
                    tarball_path.read_bytes(),
                )
            )
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
            return subprocess.CompletedProcess(
                argv,
                0,
                "hcoona-release-smoke-npm",
                "",
            )
        if argv == ("npm", "test", "--ignore-scripts"):
            return subprocess.CompletedProcess(argv, 0, "passed", "")
        pytest.fail(f"unexpected Adapter command: {argv}")

    monkeypatch.setattr(node_adapter, "_run", record_and_emulate)

    groups = {}
    start = len(observed_commands)
    built_result = node_adapter.build_node_package(request)
    groups["build-package"] = observed_commands[start:]
    start = len(observed_commands)
    node_adapter.run_node_project_build(request)
    groups["project-build"] = observed_commands[start:]
    start = len(observed_commands)
    node_adapter.run_node_project_tests(source_root, runtime_request)
    groups["project-test"] = observed_commands[start:]
    start = len(observed_commands)
    node_adapter.qualify_npm_install_import(
        built_result.tarball,
        built_result.expectation,
        runtime_request,
    )
    groups["install-import"] = observed_commands[start:]

    build_queries = (
        ("node", "--version"),
        ("pnpm", "--version"),
        ("npm", "--version"),
    )
    runtime_queries = (("node", "--version"), ("npm", "--version"))
    expected_roles = {
        "build-package": (
            *build_queries,
            ("node", "scripts/build.mjs"),
            ("npm", "pack"),
        ),
        "project-build": (*build_queries, ("node", "scripts/build.mjs")),
        "project-test": (*runtime_queries, ("npm", "test")),
        "install-import": (
            *runtime_queries,
            ("npm", "install"),
            ("node", "--input-type=module"),
        ),
    }
    commands_by_group = {}
    for name, commands in groups.items():
        assert Counter(command[:2] for command in commands) == Counter(
            expected_roles[name]
        )
        by_role = {command[:2]: command for command in commands}
        commands_by_group[name] = by_role
        queries = (
            build_queries
            if name in {"build-package", "project-build"}
            else runtime_queries
        )
        for query in queries:
            assert by_role[query] == query
        assert all(
            commands.index(query) < commands.index(command)
            for query in queries
            for role, command in by_role.items()
            if role not in queries
        )

    assert len(build_output_destinations) == 1
    assert len(consumer_tarball_copies) == 1
    observed_build_output = build_output_destinations[0]
    consumer_root, observed_consumer_tarball, installed_bytes = (
        consumer_tarball_copies[0]
    )
    for name in ("build-package", "project-build"):
        assert commands_by_group[name][("node", "scripts/build.mjs")] == (
            "node",
            "scripts/build.mjs",
        )
    pack_command = commands_by_group["build-package"][("npm", "pack")]
    assert pack_command == (
        "npm",
        "pack",
        "--ignore-scripts",
        "--json",
        "--pack-destination",
        str(observed_build_output),
    )
    assert groups["build-package"].index(("node", "scripts/build.mjs")) < (
        groups["build-package"].index(pack_command)
    )
    assert commands_by_group["project-test"][("npm", "test")] == (
        "npm",
        "test",
        "--ignore-scripts",
    )
    install_command = commands_by_group["install-import"][("npm", "install")]
    assert install_command == (
        "npm",
        "install",
        "--ignore-scripts",
        "--no-audit",
        "--no-fund",
        "--package-lock=false",
        str(observed_consumer_tarball),
    )
    import_command = commands_by_group["install-import"][
        ("node", "--input-type=module")
    ]
    assert len(import_command) == EXPECTED_IMPORT_COMMAND_ARG_COUNT
    assert import_command[:3] == ("node", "--input-type=module", "-e")
    assert import_command[3]
    assert groups["install-import"].index(install_command) < (
        groups["install-import"].index(import_command)
    )
    assert not observed_build_output.resolve().is_relative_to(
        source_root.resolve()
    )
    assert not consumer_root.is_relative_to(source_root.resolve())
    assert observed_consumer_tarball.is_relative_to(consumer_root)
    assert installed_bytes == built_result.tarball

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
