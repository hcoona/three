"""Native distribution metadata and registered installed-product checks.

UV owns selection, builds and installation. Hatchling and nbgv-python own
project metadata/version projection; this helper does not parse their config.
"""

from __future__ import annotations

import email.policy
import hashlib
import importlib
import importlib.metadata
import json
import stat
import sys
import tarfile
import zipfile
from email.parser import BytesParser
from pathlib import Path, PurePosixPath

MAX_ARCHIVE = 128 * 1024 * 1024
MAX_CONTENTS = 256 * 1024 * 1024
MAX_METADATA = 1024 * 1024
MAX_MEMBERS = 20000
MAX_INDEX_FILES = 4096
QUALITY = "python/hcoona-release-smoke-python-v1"


def native_identity(project: Path) -> dict:
    """Use the maintained backend, including its maintained version plugin."""
    from hatchling.builders.wheel import WheelBuilder  # noqa: PLC0415

    metadata = WheelBuilder(str(project.resolve())).metadata
    return {"name": metadata.core.name, "version": metadata.version}


def native_version_facts(project: Path) -> dict:
    """Expose maintained backend and version facts for the caller."""
    from hatchling.builders.wheel import WheelBuilder  # noqa: PLC0415
    from packaging.version import Version  # noqa: PLC0415

    metadata = WheelBuilder(str(project.resolve())).metadata
    source = metadata.hatch.version.source
    if source.PLUGIN_NAME != "nbgv":
        message = "Unsupported selected native version source"
        raise ValueError(message)
    data = source.get_version_data()
    version = Version(metadata.version)
    if Version(data["version"]) != version:
        message = "Native backend version recomputation differs"
        raise ValueError(message)
    native = data["metadata"]
    return {
        "name": metadata.core.name,
        "version": str(version),
        "commit": native["GitCommitId"],
        "public_release": native["PublicRelease"],
        "local_version": version.local,
        "dependencies": metadata.core.dependencies,
        "implementation": sys.implementation.name,
        "interpreter_version": ".".join(
            str(part) for part in sys.version_info[:3]
        ),
    }


def member_path(name: str) -> str:
    """Reject reader-ambiguous archive coordinates before metadata lookup."""
    value = name.removesuffix("/")
    if (
        not value
        or "\\" in value
        or ":" in value
        or "\x00" in value
        or any(part in ("", ".", "..") for part in value.split("/"))
        or PurePosixPath(value).is_absolute()
    ):
        message = "Invalid archive member path"
        raise ValueError(message)
    return value


def metadata_identity(data: bytes, name: str, version: str) -> None:
    """Compare native metadata through maintained packaging semantics."""
    from packaging.utils import canonicalize_name  # noqa: PLC0415
    from packaging.version import Version  # noqa: PLC0415

    message = BytesParser(policy=email.policy.compat32).parsebytes(data)
    names = message.get_all("Name", [])
    versions = message.get_all("Version", [])
    if (
        len(names) != 1
        or len(versions) != 1
        or canonicalize_name(names[0]) != canonicalize_name(name)
        or Version(versions[0]) != Version(version)
    ):
        message = "Distribution metadata identity differs"
        raise ValueError(message)


class _Inventory:
    """Bound native member inventory used by both standard archive readers."""

    def __init__(self) -> None:
        self.members: set[str] = set()
        self.files: set[str] = set()
        self.total = 0

    def add(self, coordinate: str, size: int, *, regular: bool) -> str:
        coordinate = member_path(coordinate)
        if (
            coordinate in self.members
            or len(self.members) >= MAX_MEMBERS
            or size < 0
        ):
            message = "Ambiguous or oversized archive"
            raise ValueError(message)
        self.members.add(coordinate)
        self.total += size
        if self.total > MAX_CONTENTS:
            message = "Oversized archive contents"
            raise ValueError(message)
        if regular:
            self.files.add(coordinate)
        return coordinate


def _wheel(path: Path) -> tuple[set[str], bytes, set[str]]:
    inventory = _Inventory()
    with zipfile.ZipFile(path) as archive:
        infos = archive.infolist()
        for info in infos:
            if stat.S_ISLNK(info.external_attr >> 16):
                message = "Unsupported archive link"
                raise ValueError(message)
            inventory.add(
                info.filename, info.file_size, regular=not info.is_dir()
            )
        metadata = [
            info
            for info in infos
            if info.filename in inventory.files
            and info.filename.count("/") == 1
            and info.filename.endswith(".dist-info/METADATA")
        ]
        if len(metadata) != 1 or metadata[0].file_size > MAX_METADATA:
            message = "Missing or ambiguous wheel metadata"
            raise ValueError(message)
        data = archive.read(metadata[0])
    return inventory.files, data, {"hcoona_release_smoke_python/__init__.py"}


def _sdist(path: Path) -> tuple[set[str], bytes, set[str]]:
    inventory = _Inventory()
    metadata = []
    with tarfile.open(path, "r:*") as archive:
        for info in archive:
            if not (info.isfile() or info.isdir()):
                message = "Unsupported archive member type"
                raise ValueError(message)
            coordinate = inventory.add(
                info.name, info.size, regular=info.isfile()
            )
            if (
                info.isfile()
                and coordinate.count("/") == 1
                and coordinate.endswith("/PKG-INFO")
            ):
                metadata.append(info)
        roots = {coordinate.split("/")[0] for coordinate in inventory.members}
        if len(roots) != 1 or len(metadata) != 1:
            message = "Missing or ambiguous sdist metadata/root"
            raise ValueError(message)
        if metadata[0].size > MAX_METADATA:
            message = "Oversized sdist metadata"
            raise ValueError(message)
        stream = archive.extractfile(metadata[0])
        if stream is None:
            message = "Missing sdist metadata"
            raise ValueError(message)
        with stream:
            data = stream.read(MAX_METADATA + 1)
    root = roots.pop()
    required = {
        root + "/src/hcoona_release_smoke_python/__init__.py",
        root + "/pyproject.toml",
        root + "/README.md",
        root + "/LICENSE",
    }
    return inventory.files, data, required


def inspect_archive(
    path: Path,
    name: str,
    version: str,
    quality: str,
    *,
    check_contents: bool = True,
) -> dict:
    """Inspect supported native archives without extracting source payload."""
    # Consumers use the standard library; inspection uses prepared tools.
    from packaging.utils import (  # noqa: PLC0415
        canonicalize_name,
        parse_sdist_filename,
        parse_wheel_filename,
    )
    from packaging.version import Version  # noqa: PLC0415

    if quality != QUALITY:
        message = "Unsupported registered Python package quality"
        raise ValueError(message)
    if (
        path.is_symlink()
        or not path.is_file()
        or path.stat().st_size > MAX_ARCHIVE
    ):
        message = "Invalid original distribution"
        raise ValueError(message)
    if path.suffix == ".whl":
        native_name, native_version, _, _ = parse_wheel_filename(path.name)
        kind = "python-wheel"
        files, data, required = _wheel(path)
    else:
        native_name, native_version = parse_sdist_filename(path.name)
        kind = "python-sdist"
        files, data, required = _sdist(path)
    metadata_identity(data, name, version)
    if (
        canonicalize_name(name) != native_name
        or Version(version) != native_version
    ):
        message = "Distribution filename identity differs"
        raise ValueError(message)
    if check_contents and not required.issubset(files):
        message = "Missing registered product payload"
        raise ValueError(message)
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {
        "kind": kind,
        "path": str(path.resolve()),
        "length": path.stat().st_size,
        "sha256": digest,
    }


def inspect_outputs(
    directory: Path, name: str, version: str, quality: str
) -> list:
    """Retain the complete output set without product payload checks."""
    outputs = [
        inspect_archive(path, name, version, quality, check_contents=False)
        for path in directory.iterdir()
    ]
    if sorted(output["kind"] for output in outputs) != [
        "python-sdist",
        "python-wheel",
    ]:
        message = "Incomplete or undeclared native distribution set"
        raise ValueError(message)
    return sorted(outputs, key=lambda output: output["kind"])


def consumer_absent(name: str, quality: str) -> dict:
    """Require a native venv without the product before remote installation."""
    if (
        quality != QUALITY
        or name != "hcoona-release-smoke-python"
        or sys.prefix == sys.base_prefix
    ):
        message = "Unsupported remote consumer environment"
        raise ValueError(message)
    try:
        importlib.metadata.distribution(name)
    except importlib.metadata.PackageNotFoundError:
        return {"name": name, "absent": True}
    message = "Product is already installed in remote consumer environment"
    raise ValueError(message)


def consume_smoke(name: str, version: str, quality: str) -> dict:
    """Check native installed metadata and the registered API from this venv."""
    if quality != QUALITY or name != "hcoona-release-smoke-python":
        message = "Unsupported registered Python package quality"
        raise ValueError(message)
    distribution = importlib.metadata.distribution(name)
    if distribution.metadata["Name"] != name or distribution.version != version:
        message = "Installed native metadata identity differs"
        raise ValueError(message)
    root = Path(sys.prefix).resolve()
    files = distribution.files
    if files is None:
        message = "Installed distribution has no native file inventory"
        raise ValueError(message)
    installed = {
        Path(distribution.locate_file(file)).resolve() for file in files
    }
    module = importlib.import_module("hcoona_release_smoke_python")
    origin = Path(module.__file__).resolve()
    if (
        sys.prefix == sys.base_prefix
        or not origin.is_relative_to(root)
        or origin not in installed
        or module.project_id() != "hcoona-release-smoke-python"
    ):
        message = "Installed origin or registered API differs"
        raise ValueError(message)
    return {
        "name": distribution.metadata["Name"],
        "version": distribution.version,
        "origin": str(origin),
    }


def index_facts(path: Path, name: str, version: str) -> dict:
    """Interpret Simple Index coordinates through maintained packaging."""
    from packaging.specifiers import SpecifierSet  # noqa: PLC0415
    from packaging.utils import (  # noqa: PLC0415
        canonicalize_name,
        parse_sdist_filename,
        parse_wheel_filename,
    )
    from packaging.version import Version  # noqa: PLC0415

    index = json.loads(path.read_bytes())
    api = Version(index["meta"]["api-version"])
    if (
        api.major != 1
        or api.is_prerelease
        or not isinstance(index["files"], list)
    ):
        message = "Unsupported Simple Index metadata"
        raise ValueError(message)
    expected_name = canonicalize_name(name)
    matches_name = canonicalize_name(index["name"]) == expected_name
    expected_version = Version(version)
    selected = []
    if len(index["files"]) > MAX_INDEX_FILES:
        message = "Simple Index inventory exceeds its bound"
        raise ValueError(message)
    for entry in index["files"]:
        filename = entry["filename"]
        identity = (
            parse_wheel_filename(filename)
            if filename.endswith(".whl")
            else parse_sdist_filename(filename)
        )
        matches_name = matches_name and identity[0] == expected_name
        if identity[1] != expected_version:
            continue
        yanked = entry.get("yanked", False)
        if not isinstance(yanked, (bool, str)):
            message = "Unsupported native file visibility"
            raise TypeError(message)
        requires_python = entry.get("requires-python")
        if requires_python is not None:
            SpecifierSet(requires_python)
        selected.append(
            {
                "filename": filename,
                "url": entry["url"],
                "sha256": entry["hashes"]["sha256"],
                "yanked": yanked is not False,
                "size": entry.get("size"),
            }
        )
    return {"matches_name": matches_name, "files": selected}


def _dispatch() -> dict | list:  # noqa: PLR0911 -- Match the closed native operation shapes.
    match sys.argv[1:]:
        case ["identity", project]:
            return native_identity(Path(project))
        case ["version-facts", project]:
            return native_version_facts(Path(project))
        case ["outputs", directory, name, version, quality]:
            return inspect_outputs(Path(directory), name, version, quality)
        case ["archive", path, name, version, quality]:
            return inspect_archive(Path(path), name, version, quality)
        case ["consume", name, version, quality]:
            return consume_smoke(name, version, quality)
        case ["index-facts", path, name, version]:
            return index_facts(Path(path), name, version)
        case ["consumer-absent", name, quality]:
            return consumer_absent(name, quality)
        case _:
            message = "Invalid package helper request"
            raise ValueError(message)


def main() -> int:
    """Emit complete facts or a sanitized terminal failure."""
    try:
        result = _dispatch()
    except Exception as error:  # noqa: BLE001 -- Native failures yield no partial facts.
        sys.stderr.write(
            f"Python package check failed ({type(error).__name__}).\n"
        )
        return 1
    else:
        sys.stdout.write(json.dumps(result, sort_keys=True) + "\n")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
