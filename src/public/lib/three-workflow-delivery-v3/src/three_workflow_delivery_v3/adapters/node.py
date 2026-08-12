"""Isolated Node Build and Quality Adapters for the first v3 slice."""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import stat
import subprocess
import tarfile
import zlib
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_RELEASE_UNIT,
)
from three_workflow_delivery_v3.repository.node_provider import (
    NbgvFacts,
    validate_nbgv_facts,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from three_workflow_delivery_v3.canonical import JsonValue

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_PURPOSES = frozenset(
    {
        "ci-pr-slice-shadow",
        "slice-validation",
        "live-release",
        "release-simulation",
    }
)
_WITNESS_PATH = "workflow-delivery/provenance.json"
_PACKED_WITNESS_PATH = f"package/{_WITNESS_PATH}"
_WITNESS_SCHEMA = "workflow-delivery/v3/package-target-witness"
_BUILD_DEFINITION = "node/npm-package-v1"
_ADAPTER_VERSION = "node/npm-package-v1"
_FIRST_SLICE_SMOKE_MESSAGE = "hcoona-release-smoke-npm"
_FIRST_SLICE_INPUTS = (
    "README.md",
    "package.json",
    "scripts/build.mjs",
    "src/index.js",
)
_FIRST_SLICE_TEST_INPUTS = (
    "package.json",
    "src/index.js",
    "test/index.test.js",
)
_FIRST_SLICE_FILES = ("dist", "README.md")
_TAR_NAME_FIELD = slice(0, 100)
_TAR_MODE_FIELD = slice(100, 108)
_TAR_UID_FIELD = slice(108, 116)
_TAR_GID_FIELD = slice(116, 124)
_TAR_SIZE_FIELD = slice(124, 136)
_TAR_MTIME_FIELD = slice(136, 148)
_TAR_CHECKSUM_FIELD = slice(148, 156)
_TAR_TYPE_FIELD = slice(156, 157)
_TAR_LINKNAME_FIELD = slice(157, 257)
_TAR_MAGIC_FIELD = slice(257, 263)
_TAR_VERSION_FIELD = slice(263, 265)
_TAR_UNAME_FIELD = slice(265, 297)
_TAR_GNAME_FIELD = slice(297, 329)
_TAR_DEVMAJOR_FIELD = slice(329, 337)
_TAR_DEVMINOR_FIELD = slice(337, 345)
_TAR_PREFIX_FIELD = slice(345, 500)
_TAR_RESERVED_FIELD = slice(500, 512)
_NPM_TAR_MODE = b"000644 \0"
_NPM_TAR_DEVICE_NUMBER = b"000000 \0"
_USTAR_MAGIC = b"ustar\0"
_USTAR_VERSION = b"00"
_ISOLATED_NPM_CONFIG = (
    "audit=false\n"
    "fund=false\n"
    "ignore-scripts=true\n"
    "package-lock=false\n"
    "update-notifier=false\n"
)


@dataclass(frozen=True, slots=True)
class PackageTargetWitness:
    """Frozen, execution-independent target identity packed in the npm file."""

    target: str
    release_unit: str
    nbgv: NbgvFacts
    build_definition: str
    catalog_digest: str
    control_digest: str
    purpose: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed Package Target Witness document."""
        return {
            "schema": "workflow-delivery/v3/package-target-witness",
            "target": self.target,
            "release-unit": self.release_unit,
            "nbgv": self.nbgv.to_document(),
            "build-definition": self.build_definition,
            "catalog-digest": self.catalog_digest,
            "control-digest": self.control_digest,
            "purpose": self.purpose,
        }

    @property
    def canonical_bytes(self) -> bytes:
        """Return the exact bytes that must be present in the tarball."""
        validate_package_target_witness(self)
        return canonicalize(self.to_document())


@dataclass(frozen=True, slots=True)
class BuildRequest:
    """Closed input set for one isolated npm package build."""

    source_root: Path
    declared_inputs: tuple[str, ...]
    npm_package_version: str
    witness: PackageTargetWitness
    source_date_epoch: int
    node_version: str
    pnpm_version: str
    npm_version: str


@dataclass(frozen=True, slots=True)
class RuntimeRequest:
    """Frozen runtime versions required by Node quality operations."""

    node_version: str
    npm_version: str


@dataclass(frozen=True, slots=True)
class ArtifactExpectation:
    """Exact package facts used by both tarball-dependent quality checks."""

    package_name: str
    npm_package_version: str
    files_allowlist: tuple[str, ...]
    lifecycle_scripts: tuple[tuple[str, str], ...]
    entry_allowlist: tuple[str, ...]
    witness_bytes: bytes


@dataclass(frozen=True, slots=True)
class ArtifactManifest:
    """Verified immutable npm artifact facts."""

    basename: str
    entries: tuple[str, ...]
    lifecycle_scripts: tuple[tuple[str, str], ...]
    sha256: str
    sha512: str
    byte_size: int


@dataclass(frozen=True, slots=True)
class BuildResult:
    """One immutable tarball and its verified manifest/provenance."""

    tarball: bytes
    manifest: ArtifactManifest
    expectation: ArtifactExpectation
    witness: bytes
    source_input_manifest: tuple[tuple[str, str], ...]
    toolchain: tuple[tuple[str, str], ...]


@dataclass(frozen=True, slots=True)
class InstallImportResult:
    """Concrete observables from an isolated install/import qualification."""

    smoke_message: str
    witness_sha256: str


def validate_package_target_witness(witness: PackageTargetWitness) -> None:
    """Reject witnesses outside the exact first-slice contract."""
    if type(witness) is not PackageTargetWitness:
        message = "Package Target Witness must have exact runtime type"
        raise TypeError(message)
    if type(witness.target) is not str or not _SHA_PATTERN.fullmatch(
        witness.target,
    ):
        message = "Package Target Witness target must be a full lowercase SHA"
        raise ValueError(message)
    for label, value in (
        ("release-unit", witness.release_unit),
        ("build-definition", witness.build_definition),
    ):
        if type(value) is not str or not value:
            message = (
                f"Package Target Witness {label} must be a nonempty string"
            )
            raise ValueError(message)
    if witness.release_unit != FIRST_SLICE_RELEASE_UNIT:
        message = "Package Target Witness release-unit is not the first slice"
        raise ValueError(message)
    if witness.build_definition != _BUILD_DEFINITION:
        message = "Package Target Witness build-definition is unsupported"
        raise ValueError(message)
    for label, value in (
        ("catalog-digest", witness.catalog_digest),
        ("control-digest", witness.control_digest),
    ):
        if type(value) is not str or not _DIGEST_PATTERN.fullmatch(value):
            message = f"Package Target Witness {label} must be a SHA-256 digest"
            raise ValueError(message)
    if type(witness.purpose) is not str or witness.purpose not in _PURPOSES:
        message = "Package Target Witness purpose is unsupported"
        raise ValueError(message)
    validate_nbgv_facts(witness.nbgv, target=witness.target)


def _required_object(
    document: dict[str, JsonValue],
    key: str,
) -> dict[str, JsonValue]:
    value = document.get(key)
    if not isinstance(value, dict):
        message = f"Package Target Witness {key} must be an object"
        raise TypeError(message)
    return value


def _required_string(document: dict[str, JsonValue], key: str) -> str:
    value = document.get(key)
    if type(value) is not str:
        message = f"Package Target Witness {key} must be a string"
        raise TypeError(message)
    return value


def _required_integer(document: dict[str, JsonValue], key: str) -> int:
    value = document.get(key)
    if type(value) is not int:
        message = f"Package Target Witness {key} must be an integer"
        raise TypeError(message)
    return value


def _required_boolean(document: dict[str, JsonValue], key: str) -> bool:
    value = document.get(key)
    if type(value) is not bool:
        message = f"Package Target Witness {key} must be Boolean"
        raise TypeError(message)
    return value


def _package_target_witness_from_document(
    document: dict[str, JsonValue],
) -> PackageTargetWitness:
    """Parse and validate the exact Package Target Witness schema."""
    expected_keys = {
        "schema",
        "target",
        "release-unit",
        "nbgv",
        "build-definition",
        "catalog-digest",
        "control-digest",
        "purpose",
    }
    if set(document) != expected_keys:
        message = "Package Target Witness schema keys mismatch"
        raise ValueError(message)
    if document["schema"] != _WITNESS_SCHEMA:
        message = "Package Target Witness schema mismatch"
        raise ValueError(message)
    nbgv_document = _required_object(document, "nbgv")
    if set(nbgv_document) != {
        "canonical",
        "native",
        "node-api-result-digest",
    }:
        message = "Package Target Witness NBGV keys mismatch"
        raise ValueError(message)
    canonical = _required_object(nbgv_document, "canonical")
    native = _required_object(nbgv_document, "native")
    if set(canonical) != {
        "version",
        "semVer1",
        "semVer2",
        "versionHeight",
        "gitCommitId",
        "publicRelease",
    } or set(native) != {"npmPackageVersion"}:
        message = "Package Target Witness NBGV binding keys mismatch"
        raise ValueError(message)
    witness = PackageTargetWitness(
        target=_required_string(document, "target"),
        release_unit=_required_string(document, "release-unit"),
        nbgv=NbgvFacts(
            canonical_version=_required_string(canonical, "version"),
            sem_ver1=_required_string(canonical, "semVer1"),
            sem_ver2=_required_string(canonical, "semVer2"),
            version_height=_required_integer(canonical, "versionHeight"),
            git_commit_id=_required_string(canonical, "gitCommitId"),
            public_release=_required_boolean(canonical, "publicRelease"),
            npm_package_version=_required_string(native, "npmPackageVersion"),
            node_api_result_digest=_required_string(
                nbgv_document,
                "node-api-result-digest",
            ),
        ),
        build_definition=_required_string(document, "build-definition"),
        catalog_digest=_required_string(document, "catalog-digest"),
        control_digest=_required_string(document, "control-digest"),
        purpose=_required_string(document, "purpose"),
    )
    validate_package_target_witness(witness)
    if document != witness.to_document():
        message = "Package Target Witness document binding mismatch"
        raise ValueError(message)
    return witness


def package_target_witness_from_document(
    document: dict[str, JsonValue],
) -> PackageTargetWitness:
    """Parse the closed Package Target Witness document."""
    return _package_target_witness_from_document(document)


def _validate_relative_paths(paths: tuple[str, ...]) -> None:
    if type(paths) is not tuple or not paths:
        message = "declared inputs must be a nonempty exact tuple"
        raise ValueError(message)
    if len(paths) != len(set(paths)):
        message = "declared inputs must not contain duplicates"
        raise ValueError(message)
    for value in paths:
        path = PurePosixPath(value)
        if (
            type(value) is not str
            or not value
            or path.is_absolute()
            or ".." in path.parts
            or value != path.as_posix()
        ):
            message = f"unsafe declared input path: {value!r}"
            raise ValueError(message)


def _validate_build_request(request: BuildRequest) -> None:
    if type(request) is not BuildRequest:
        message = "Build Request must have exact runtime type"
        raise TypeError(message)
    _validate_relative_paths(request.declared_inputs)
    if request.declared_inputs != _FIRST_SLICE_INPUTS:
        message = "declared inputs do not match the first-slice build closure"
        raise ValueError(message)
    required = {"package.json", "scripts/build.mjs"}
    if not required.issubset(request.declared_inputs):
        message = (
            "declared inputs must include package.json and scripts/build.mjs"
        )
        raise ValueError(message)
    if (
        type(request.npm_package_version) is not str
        or not request.npm_package_version
        or request.npm_package_version == "0.0.0-placeholder"
        or request.npm_package_version
        != request.witness.nbgv.npm_package_version
    ):
        message = "frozen npmPackageVersion is missing or inconsistent"
        raise ValueError(message)
    if (
        type(request.source_date_epoch) is not int
        or request.source_date_epoch < 0
    ):
        message = "SOURCE_DATE_EPOCH must be a nonnegative integer"
        raise ValueError(message)
    for label, value in (
        ("Node", request.node_version),
        ("PNPM", request.pnpm_version),
        ("npm", request.npm_version),
    ):
        if type(value) is not str or not value:
            message = f"{label} version must be frozen"
            raise ValueError(message)
    validate_package_target_witness(request.witness)


def _validate_runtime_request(request: RuntimeRequest) -> None:
    if type(request) is not RuntimeRequest:
        message = "Runtime Request must have exact runtime type"
        raise TypeError(message)
    for label, value in (
        ("Node", request.node_version),
        ("npm", request.npm_version),
    ):
        if type(value) is not str or not value:
            message = f"{label} version must be frozen"
            raise ValueError(message)


def _validate_first_slice_package_name(package_name: object) -> str:
    if package_name != FIRST_SLICE_PACKAGE:
        message = "package identity is not the first-slice npm package"
        raise ValueError(message)
    return FIRST_SLICE_PACKAGE


def _run(
    command: Sequence[str],
    cwd: Path,
    environment: Mapping[str, str],
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603 - closed Adapter commands only
        command,
        cwd=cwd,
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )


def _credential_free_environment(
    home: Path,
    *,
    source_date_epoch: int | None = None,
) -> dict[str, str]:
    path = os.environ.get("PATH")
    if type(path) is not str or not path:
        message = "credential-free Node execution requires an explicit PATH"
        raise ValueError(message)
    isolated_home = home.resolve()
    isolated_home.mkdir(parents=True, exist_ok=False)
    npm_cache = isolated_home / "npm-cache"
    npm_cache.mkdir()
    npm_user_config = isolated_home / "npmrc"
    npm_user_config.write_text(_ISOLATED_NPM_CONFIG, encoding="utf-8")
    npm_user_config.chmod(0o600)
    npm_global_config = isolated_home / "global-npmrc"
    npm_global_config.write_bytes(b"")
    npm_global_config.chmod(0o600)
    environment = {
        "HOME": str(isolated_home),
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
        "NPM_CONFIG_CACHE": str(npm_cache),
        "NPM_CONFIG_GLOBALCONFIG": str(npm_global_config),
        "NPM_CONFIG_USERCONFIG": str(npm_user_config),
        "PATH": path,
        "TZ": "UTC",
        "XDG_CONFIG_HOME": str(isolated_home / "config"),
    }
    if source_date_epoch is not None:
        environment["SOURCE_DATE_EPOCH"] = str(source_date_epoch)
    return environment


def _verify_toolchain(
    request: BuildRequest,
    environment: Mapping[str, str],
) -> None:
    node_version = _run(("node", "--version"), request.source_root, environment)
    pnpm_version = _run(("pnpm", "--version"), request.source_root, environment)
    npm_version = _run(("npm", "--version"), request.source_root, environment)
    if node_version.stdout.strip().removeprefix("v") != request.node_version:
        message = "runtime Node version differs from frozen Build Request"
        raise ValueError(message)
    if pnpm_version.stdout.strip() != request.pnpm_version:
        message = "runtime PNPM version differs from frozen Build Request"
        raise ValueError(message)
    if npm_version.stdout.strip() != request.npm_version:
        message = "runtime npm version differs from frozen Build Request"
        raise ValueError(message)


def _verify_quality_toolchain(
    request: RuntimeRequest,
    cwd: Path,
    environment: Mapping[str, str],
) -> None:
    node_version = _run(("node", "--version"), cwd, environment)
    if node_version.stdout.strip() != request.node_version:
        message = "runtime Node version differs from frozen Runtime Request"
        raise ValueError(message)
    npm_version = _run(("npm", "--version"), cwd, environment)
    if npm_version.stdout.strip() != request.npm_version:
        message = "runtime npm version differs from frozen Runtime Request"
        raise ValueError(message)


def _capture_input_sources(
    source_root: Path,
    relative_paths: tuple[str, ...],
) -> tuple[tuple[str, bytes], ...]:
    resolved_root = source_root.resolve()
    resolved_sources: list[tuple[str, Path]] = []
    for relative in relative_paths:
        source = (resolved_root / relative).resolve()
        if not source.is_relative_to(resolved_root) or not source.is_file():
            message = f"declared input is not a regular source file: {relative}"
            raise ValueError(message)
        resolved_sources.append((relative, source))
    return tuple(
        (relative, source.read_bytes()) for relative, source in resolved_sources
    )


def _capture_declared_inputs(
    request: BuildRequest,
) -> tuple[tuple[str, bytes], ...]:
    return _capture_input_sources(request.source_root, request.declared_inputs)


def _source_input_manifest(
    sources: tuple[tuple[str, bytes], ...],
) -> tuple[tuple[str, str], ...]:
    return tuple(
        (relative, f"sha256:{hashlib.sha256(content).hexdigest()}")
        for relative, content in sources
    )


def _stage_captured_inputs(
    sources: tuple[tuple[str, bytes], ...],
    staging_root: Path,
) -> None:
    for relative, content in sources:
        destination = staging_root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)


def _normalize_staging_modes(staging_root: Path) -> None:
    try:
        root_stat = staging_root.stat(follow_symlinks=False)
    except OSError as error:
        message = "isolated npm staging closure could not be inspected"
        raise ValueError(message) from error
    if not stat.S_ISDIR(root_stat.st_mode):
        message = "isolated npm staging root must be a directory"
        raise ValueError(message)

    pending = [staging_root]
    while pending:
        directory = pending.pop()
        try:
            directory.chmod(
                0o755,
                follow_symlinks=False,
            )
            with os.scandir(directory) as entries:
                children = tuple(entries)
        except OSError as error:
            message = "isolated npm staging closure could not be normalized"
            raise ValueError(message) from error
        for entry in children:
            if entry.is_symlink():
                message = (
                    "isolated npm staging closure must not contain symlinks"
                )
                raise ValueError(message)
            if entry.is_dir(follow_symlinks=False):
                pending.append(Path(entry.path))
                continue
            if entry.is_file(follow_symlinks=False):
                try:
                    Path(entry.path).chmod(0o644, follow_symlinks=False)
                except OSError as error:
                    message = (
                        "isolated npm staging closure could not be normalized"
                    )
                    raise ValueError(message) from error
                continue
            message = (
                "isolated npm staging closure must contain directories and "
                "regular files only"
            )
            raise ValueError(message)


def _load_manifest(path: Path) -> dict[str, JsonValue]:
    parsed = parse_json_strict(path.read_bytes())
    if not isinstance(parsed, dict):
        message = "package.json must be a JSON object"
        raise TypeError(message)
    return parsed


def _manifest_scripts(
    manifest: dict[str, JsonValue],
) -> tuple[tuple[str, str], ...]:
    scripts = manifest.get("scripts", {})
    if not isinstance(scripts, dict):
        message = "package scripts must be an object"
        raise TypeError(message)
    lifecycle: list[tuple[str, str]] = []
    for name, command in scripts.items():
        if not isinstance(name, str) or not isinstance(command, str):
            message = "package scripts must map strings to strings"
            raise TypeError(message)
        lifecycle.append((name, command))
    return tuple(sorted(lifecycle))


def _prepare_staged_manifest(
    request: BuildRequest,
    staging_root: Path,
) -> tuple[str, tuple[str, ...], tuple[tuple[str, str], ...]]:
    manifest_path = staging_root / "package.json"
    manifest = _load_manifest(manifest_path)
    package_name = manifest.get("name")
    files = manifest.get("files")
    package_name = _validate_first_slice_package_name(package_name)
    if not isinstance(files, list) or not files:
        message = "package files must be a nonempty array"
        raise ValueError(message)
    if any(not isinstance(item, str) or not item for item in files):
        message = "package files entries must be nonempty strings"
        raise ValueError(message)
    if len(files) != len(set(files)):
        message = "package files must not contain duplicates"
        raise ValueError(message)
    if tuple(files) != _FIRST_SLICE_FILES:
        message = "source package files do not match the first-slice allowlist"
        raise ValueError(message)
    if _WITNESS_PATH in files:
        message = "source package files must not predeclare the target witness"
        raise ValueError(message)
    files.append(_WITNESS_PATH)
    manifest["version"] = request.npm_package_version
    manifest["files"] = files
    manifest_path.write_text(
        f"{json.dumps(manifest, indent=2)}\n",
        encoding="utf-8",
    )
    typed_files = tuple(cast("str", item) for item in files)
    return package_name, typed_files, _manifest_scripts(manifest)


def _expected_entries(files: tuple[str, ...]) -> tuple[str, ...]:
    entries: list[str] = ["package/package.json"]
    for item in files:
        if item == "dist":
            entries.append("package/dist/index.js")
        elif item == "README.md":
            entries.append("package/README.md")
        elif item == _WITNESS_PATH:
            entries.append(_PACKED_WITNESS_PATH)
        else:
            message = f"unsupported first-slice package files entry: {item!r}"
            raise ValueError(message)
    return tuple(sorted(entries))


def _expected_basename(package_name: str, version: str) -> str:
    normalized = package_name.removeprefix("@").replace("/", "-")
    return f"{normalized}-{version}.tgz"


def build_node_package(request: BuildRequest) -> BuildResult:
    """Build and verify one npm tarball without mutating the source checkout."""
    _validate_build_request(request)
    source_root = request.source_root.resolve()
    input_sources = _capture_declared_inputs(request)
    source_manifest = _source_input_manifest(input_sources)
    with TemporaryDirectory(prefix="wdv3-node-build-") as temporary:
        temporary_root = Path(temporary).resolve()
        staging_root = temporary_root / "stage"
        output_root = temporary_root / "output"
        if staging_root.is_relative_to(source_root):
            message = "staging tree must be outside the source checkout"
            raise ValueError(message)
        staging_root.mkdir()
        output_root.mkdir()
        environment = _credential_free_environment(
            temporary_root / "home",
            source_date_epoch=request.source_date_epoch,
        )
        _verify_toolchain(request, environment)
        _stage_captured_inputs(input_sources, staging_root)
        package_name, files, lifecycle = _prepare_staged_manifest(
            request,
            staging_root,
        )
        witness_bytes = request.witness.canonical_bytes
        witness_path = staging_root / _WITNESS_PATH
        witness_path.parent.mkdir(parents=True)
        witness_path.write_bytes(witness_bytes)

        _run(("node", "scripts/build.mjs"), staging_root, environment)
        _normalize_staging_modes(staging_root)
        completed = _run(
            (
                "npm",
                "pack",
                "--ignore-scripts",
                "--json",
                "--pack-destination",
                str(output_root),
            ),
            staging_root,
            environment,
        )
        pack_result = parse_json_strict(completed.stdout)
        if (
            not isinstance(pack_result, list)
            or len(pack_result) != 1
            or not isinstance(pack_result[0], dict)
            or not isinstance(pack_result[0].get("filename"), str)
        ):
            message = "npm pack did not emit one closed JSON result"
            raise ValueError(message)
        basename = pack_result[0]["filename"]
        expected_basename = _expected_basename(
            package_name,
            request.npm_package_version,
        )
        if basename != expected_basename:
            message = "npm pack emitted an unexpected tarball basename"
            raise ValueError(message)
        tarball = (output_root / basename).read_bytes()

    expectation = ArtifactExpectation(
        package_name=package_name,
        npm_package_version=request.npm_package_version,
        files_allowlist=files,
        lifecycle_scripts=lifecycle,
        entry_allowlist=_expected_entries(files),
        witness_bytes=witness_bytes,
    )
    manifest = qualify_npm_artifact_contents(tarball, expectation)
    return BuildResult(
        tarball=tarball,
        manifest=manifest,
        expectation=expectation,
        witness=witness_bytes,
        source_input_manifest=source_manifest,
        toolchain=(
            ("node", request.node_version),
            ("pnpm", request.pnpm_version),
            ("npm", request.npm_version),
            ("adapter", _ADAPTER_VERSION),
        ),
    )


def _strict_gzip_payload(tarball: bytes) -> bytes:
    decompressor = zlib.decompressobj(zlib.MAX_WBITS | 16)
    try:
        payload = decompressor.decompress(tarball)
        payload += decompressor.flush()
    except (EOFError, zlib.error) as error:
        message = "invalid npm tarball"
        raise ValueError(message) from error
    if (
        not decompressor.eof
        or decompressor.unconsumed_tail
        or decompressor.unused_data
    ):
        message = "invalid npm tarball"
        raise ValueError(message)
    return payload


def _aligned_tar_offset(offset: int) -> int:
    return (
        (offset + tarfile.BLOCKSIZE - 1)
        // tarfile.BLOCKSIZE
        * tarfile.BLOCKSIZE
    )


def _invalid_tarball() -> ValueError:
    return ValueError("invalid npm tarball")


def _parse_canonical_tar_octal(field: bytes, digit_count: int) -> int:
    digits = field[:digit_count]
    if (
        len(field) != digit_count + 2
        or field[digit_count:] != b" \0"
        or any(byte < ord("0") or byte > ord("7") for byte in digits)
    ):
        raise _invalid_tarball()
    return int(digits, 8)


def _validate_nul_filled_tar_name(field: bytes) -> None:
    try:
        first_nul = field.index(0)
    except ValueError as error:
        raise _invalid_tarball() from error
    if first_nul == 0 or any(field[first_nul:]):
        raise _invalid_tarball()


def _validate_ustar_regular_file_header(header: bytes) -> int:
    if len(header) != tarfile.BLOCKSIZE:
        raise _invalid_tarball()
    if (
        header[_TAR_MAGIC_FIELD] != _USTAR_MAGIC
        or header[_TAR_VERSION_FIELD] != _USTAR_VERSION
        or header[_TAR_TYPE_FIELD] != tarfile.REGTYPE
        or header[_TAR_MODE_FIELD] != _NPM_TAR_MODE
        or header[_TAR_UID_FIELD] != bytes(8)
        or header[_TAR_GID_FIELD] != bytes(8)
        or header[_TAR_LINKNAME_FIELD] != bytes(100)
        or header[_TAR_UNAME_FIELD] != bytes(32)
        or header[_TAR_GNAME_FIELD] != bytes(32)
        or header[_TAR_DEVMAJOR_FIELD] != _NPM_TAR_DEVICE_NUMBER
        or header[_TAR_DEVMINOR_FIELD] != _NPM_TAR_DEVICE_NUMBER
        or header[_TAR_PREFIX_FIELD] != bytes(155)
        or header[_TAR_RESERVED_FIELD] != bytes(12)
    ):
        raise _invalid_tarball()

    _validate_nul_filled_tar_name(header[_TAR_NAME_FIELD])
    size = _parse_canonical_tar_octal(header[_TAR_SIZE_FIELD], 10)
    _parse_canonical_tar_octal(header[_TAR_MTIME_FIELD], 10)
    checksum = _parse_canonical_tar_octal(header[_TAR_CHECKSUM_FIELD], 6)
    checksum_header = bytearray(header)
    checksum_header[_TAR_CHECKSUM_FIELD] = b" " * 8
    if sum(checksum_header) != checksum:
        raise _invalid_tarball()
    return size


def _validate_physical_tar_stream(payload: bytes) -> None:
    offset = 0
    while offset < len(payload):
        header_end = offset + tarfile.BLOCKSIZE
        header = payload[offset:header_end]
        if not any(header):
            trailer = payload[offset:]
            if len(trailer) != tarfile.BLOCKSIZE * 2 or any(trailer):
                message = "invalid npm tarball"
                raise ValueError(message)
            return

        physical_size = _validate_ustar_regular_file_header(header)
        data_end = header_end + physical_size
        aligned_data_end = _aligned_tar_offset(data_end)
        if aligned_data_end > len(payload) or any(
            payload[data_end:aligned_data_end]
        ):
            message = "invalid npm tarball"
            raise ValueError(message)
        offset = aligned_data_end

    message = "invalid npm tarball"
    raise ValueError(message)


def _read_tarball(tarball: bytes) -> dict[str, bytes]:
    payload = _strict_gzip_payload(tarball)
    if len(payload) % tarfile.BLOCKSIZE != 0:
        message = "invalid npm tarball"
        raise ValueError(message)
    entries: dict[str, bytes] = {}
    try:
        _validate_physical_tar_stream(payload)
        with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
            for member in archive.getmembers():
                if not member.isfile() or member.name in entries:
                    message = (
                        "tarball member closure must contain unique regular "
                        "files only"
                    )
                    raise ValueError(message)
                extracted = archive.extractfile(member)
                if extracted is None:
                    message = "tarball regular file could not be read"
                    raise ValueError(message)
                content = extracted.read()
                if len(content) != member.size:
                    message = "tarball regular file ended prematurely"
                    raise ValueError(message)
                padding_start = member.offset_data + member.size
                padding_end = _aligned_tar_offset(padding_start)
                if any(payload[padding_start:padding_end]):
                    message = "invalid npm tarball"
                    raise ValueError(message)
                entries[member.name] = content
    except (EOFError, OSError, tarfile.TarError) as error:
        message = "invalid npm tarball"
        raise ValueError(message) from error
    return entries


def _validate_artifact_expectation(
    expectation: ArtifactExpectation,
) -> PackageTargetWitness:
    if type(expectation) is not ArtifactExpectation:
        message = "artifact expectation must have exact runtime type"
        raise TypeError(message)
    _validate_first_slice_package_name(expectation.package_name)
    if (
        expectation.npm_package_version == "0.0.0-placeholder"
        or type(expectation.npm_package_version) is not str
        or not expectation.npm_package_version
        or type(expectation.files_allowlist) is not tuple
        or expectation.files_allowlist != (*_FIRST_SLICE_FILES, _WITNESS_PATH)
        or type(expectation.lifecycle_scripts) is not tuple
        or type(expectation.entry_allowlist) is not tuple
        or expectation.entry_allowlist
        != _expected_entries(expectation.files_allowlist)
        or type(expectation.witness_bytes) is not bytes
    ):
        message = "artifact expectation is outside the first-slice closure"
        raise ValueError(message)
    expected_witness = _package_target_witness_from_document(
        parse_canonical_json(expectation.witness_bytes),
    )
    if (
        expected_witness.nbgv.npm_package_version
        != expectation.npm_package_version
    ):
        message = "Package Target Witness version binding mismatch"
        raise ValueError(message)
    return expected_witness


def qualify_npm_artifact_contents(
    tarball: bytes,
    expectation: ArtifactExpectation,
) -> ArtifactManifest:
    """Validate the exact npm artifact manifest and target witness."""
    expected_witness = _validate_artifact_expectation(expectation)
    entries = _read_tarball(tarball)
    actual_names = tuple(sorted(entries))
    if actual_names != expectation.entry_allowlist:
        message = "npm tarball entry allowlist mismatch"
        raise ValueError(message)
    manifest = _load_packed_manifest(entries["package/package.json"])
    if manifest.get("name") != expectation.package_name:
        message = "packed package identity mismatch"
        raise ValueError(message)
    if manifest.get("version") != expectation.npm_package_version:
        message = "packed package version mismatch"
        raise ValueError(message)
    files = manifest.get("files")
    if files != list(expectation.files_allowlist):
        message = "packed package files allowlist mismatch"
        raise ValueError(message)
    lifecycle = _manifest_scripts(manifest)
    if lifecycle != expectation.lifecycle_scripts:
        message = "packed lifecycle-script manifest mismatch"
        raise ValueError(message)
    witness = entries.get(_PACKED_WITNESS_PATH)
    if witness is None:
        message = "packed Package Target Witness is missing"
        raise ValueError(message)
    if witness != expectation.witness_bytes:
        message = "packed Package Target Witness bytes mismatch"
        raise ValueError(message)
    actual_witness = _package_target_witness_from_document(
        parse_canonical_json(witness),
    )
    if actual_witness != expected_witness:
        message = "packed Package Target Witness binding mismatch"
        raise ValueError(message)
    sha256 = hashlib.sha256(tarball).hexdigest()
    sha512 = hashlib.sha512(tarball).hexdigest()
    return ArtifactManifest(
        basename=_expected_basename(
            expectation.package_name,
            expectation.npm_package_version,
        ),
        entries=actual_names,
        lifecycle_scripts=lifecycle,
        sha256=f"sha256:{sha256}",
        sha512=f"sha512:{sha512}",
        byte_size=len(tarball),
    )


def _load_packed_manifest(document: bytes) -> dict[str, JsonValue]:
    parsed = parse_json_strict(document)
    if not isinstance(parsed, dict):
        message = "packed package.json must be an object"
        raise TypeError(message)
    return parsed


def run_node_project_build(request: BuildRequest) -> None:
    """Run the project build from isolated declared inputs."""
    _validate_build_request(request)
    input_sources = _capture_declared_inputs(request)
    with TemporaryDirectory(prefix="wdv3-node-quality-build-") as temporary:
        temporary_root = Path(temporary).resolve()
        staging_root = temporary_root / "stage"
        staging_root.mkdir()
        environment = _credential_free_environment(
            temporary_root / "home",
            source_date_epoch=request.source_date_epoch,
        )
        _verify_toolchain(request, environment)
        _stage_captured_inputs(input_sources, staging_root)
        _prepare_staged_manifest(request, staging_root)
        witness_path = staging_root / _WITNESS_PATH
        witness_path.parent.mkdir(parents=True)
        witness_path.write_bytes(request.witness.canonical_bytes)
        _run(("node", "scripts/build.mjs"), staging_root, environment)
        if (staging_root / "dist/index.js").read_bytes() != (
            staging_root / "src/index.js"
        ).read_bytes():
            message = "isolated project build output mismatch"
            raise ValueError(message)


def run_node_project_tests(
    project_root: Path,
    request: RuntimeRequest,
) -> None:
    """Run the first-slice Node tests without publication credentials."""
    _validate_runtime_request(request)
    input_sources = _capture_input_sources(
        project_root,
        _FIRST_SLICE_TEST_INPUTS,
    )
    with TemporaryDirectory(prefix="wdv3-node-quality-test-") as temporary:
        temporary_root = Path(temporary).resolve()
        staging_root = temporary_root / "stage"
        staging_root.mkdir()
        _stage_captured_inputs(input_sources, staging_root)
        environment = _credential_free_environment(temporary_root / "home")
        _verify_quality_toolchain(request, staging_root, environment)
        _run(
            ("npm", "test", "--ignore-scripts"),
            staging_root,
            environment,
        )


def qualify_npm_install_import(
    tarball: bytes,
    expectation: ArtifactExpectation,
    request: RuntimeRequest,
) -> InstallImportResult:
    """Install with scripts disabled, import the package, and verify witness."""
    _validate_runtime_request(request)
    qualify_npm_artifact_contents(tarball, expectation)
    with TemporaryDirectory(prefix="wdv3-node-consumer-") as temporary:
        temporary_root = Path(temporary).resolve()
        consumer = temporary_root / "consumer"
        consumer.mkdir()
        tarball_path = consumer / "package.tgz"
        tarball_path.write_bytes(tarball)
        (consumer / "package.json").write_text(
            '{"private":true,"type":"module"}\n',
            encoding="utf-8",
        )
        environment = _credential_free_environment(temporary_root / "home")
        _verify_quality_toolchain(request, consumer, environment)
        _run(
            (
                "npm",
                "install",
                "--ignore-scripts",
                "--no-audit",
                "--no-fund",
                "--package-lock=false",
                str(tarball_path),
            ),
            consumer,
            environment,
        )
        package_root = consumer / "node_modules" / expectation.package_name
        installed_witness = (package_root / _WITNESS_PATH).read_bytes()
        if installed_witness != expectation.witness_bytes:
            message = "installed Package Target Witness bytes mismatch"
            raise ValueError(message)
        package_specifier = json.dumps(expectation.package_name)
        script = (
            f"import {{smokeMessage}} from {package_specifier};"
            "process.stdout.write(smokeMessage());"
        )
        completed = _run(
            ("node", "--input-type=module", "-e", script), consumer, environment
        )
        if completed.stdout != _FIRST_SLICE_SMOKE_MESSAGE:
            message = "installed smokeMessage export mismatch"
            raise ValueError(message)
    witness_digest = hashlib.sha256(installed_witness).hexdigest()
    return InstallImportResult(
        smoke_message=completed.stdout,
        witness_sha256=f"sha256:{witness_digest}",
    )
