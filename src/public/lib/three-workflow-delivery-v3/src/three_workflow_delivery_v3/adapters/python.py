"""Frozen two-format Hatch builds, native inspection and isolated consumers."""

from __future__ import annotations

import base64
import csv
import io
import os
import platform
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tomllib
import zipfile
from dataclasses import dataclass
from email import policy
from email.parser import BytesParser
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory

import tomli_w
from packaging.utils import (
    canonicalize_name,
    parse_sdist_filename,
    parse_wheel_filename,
)
from packaging.version import Version

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_BUILD_DEFINITION,
    PYTHON_IMPORT,
    PYTHON_RELEASE_UNIT,
    PYTHON_ROOT,
    PYTHON_SOURCE_FILES,
    PythonNbgvFacts,
    python_digest,
    python_nbgv_facts_from_document,
    python_object,
    python_text,
    validate_python_source_manifest,
)

WITNESS_BASENAME = "_workflow_delivery_provenance.json"
WITNESS_PATH = f"{PYTHON_IMPORT}/{WITNESS_BASENAME}"
_MAX_ARCHIVE_BYTES = 8 * 1024 * 1024
_MAX_MEMBERS = 32
_RECORD_FIELDS = 3
_DISTRIBUTION_COUNT = 2
_SOURCE_DATE_EPOCH = "315532800"


@dataclass(frozen=True, slots=True)
class PythonPackageTargetWitness:
    """Execution-independent identity embedded in both original formats."""

    target: str
    nbgv: PythonNbgvFacts
    catalog_digest: str
    control_digest: str
    purpose: str

    def __post_init__(self) -> None:
        """Close the target and canonical input identities."""
        if (
            type(self.nbgv) is not PythonNbgvFacts
            or self.target != self.nbgv.target
            or self.purpose
            not in {
                "ci-pr-slice-shadow",
                "slice-validation",
                "live-release",
                "release-simulation",
                "destination-acceptance",
                "destination-bootstrap",
            }
            or any(
                not re.fullmatch(r"sha256:[0-9a-f]{64}", d)
                for d in (self.catalog_digest, self.control_digest)
            )
        ):
            message = "invalid Python package target witness"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return only stable package provenance, never run or destination."""
        return {
            "schema": "workflow-delivery/v3/python-package-target-witness",
            "target": self.target,
            "release-unit": PYTHON_RELEASE_UNIT,
            "nbgv": self.nbgv.to_document(),
            "build-definition": PYTHON_BUILD_DEFINITION,
            "catalog-digest": self.catalog_digest,
            "control-digest": self.control_digest,
            "purpose": self.purpose,
        }

    @property
    def canonical_bytes(self) -> bytes:
        """Return the exact embedded witness bytes."""
        return canonicalize(self.to_document())


def python_package_target_witness_from_document(
    value: JsonValue,
) -> PythonPackageTargetWitness:
    """Read the closed Python witness variant."""
    doc = python_object(
        value,
        {
            "schema",
            "target",
            "release-unit",
            "nbgv",
            "build-definition",
            "catalog-digest",
            "control-digest",
            "purpose",
        },
    )
    if (
        doc["schema"] != "workflow-delivery/v3/python-package-target-witness"
        or doc["release-unit"] != PYTHON_RELEASE_UNIT
        or doc["build-definition"] != PYTHON_BUILD_DEFINITION
    ):
        message = "unsupported Python witness variant"
        raise ValueError(message)
    return PythonPackageTargetWitness(
        python_text(doc["target"]),
        python_nbgv_facts_from_document(doc["nbgv"]),
        python_text(doc["catalog-digest"]),
        python_text(doc["control-digest"]),
        python_text(doc["purpose"]),
    )


@dataclass(frozen=True, slots=True)
class PythonDistribution:
    """Original native-format bytes and their logical identity."""

    variant: str
    filename: str
    content: bytes
    witness: PythonPackageTargetWitness

    @property
    def digest(self) -> str:
        """Return original bytes, independent of transport packaging."""
        return python_digest(self.content)


def _member_path(name: str) -> None:
    path = PurePosixPath(name)
    if (
        not name
        or path.is_absolute()
        or path.as_posix() != name
        or "\\" in name
        or ":" in name
        or any(p in {".", ".."} for p in path.parts)
    ):
        message = "unsafe Python archive member"
        raise ValueError(message)


def _archive_members(content: bytes, variant: str) -> dict[str, bytes]:
    if not content or len(content) > _MAX_ARCHIVE_BYTES:
        message = "Python archive exceeds the bounded smoke profile"
        raise ValueError(message)
    members: dict[str, bytes] = {}
    total = 0
    if variant == "wheel":
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            for info in archive.infolist():
                _member_path(info.filename)
                total += info.file_size
                mode = info.external_attr >> 16
                if (
                    info.filename in members
                    or info.is_dir()
                    or stat.S_ISLNK(mode)
                    or info.flag_bits & 1
                    or total > _MAX_ARCHIVE_BYTES
                    or len(members) >= _MAX_MEMBERS
                ):
                    message = "duplicate, unsafe or oversized wheel member"
                    raise ValueError(message)
                members[info.filename] = archive.read(info)
    elif variant == "sdist":
        with tarfile.open(fileobj=io.BytesIO(content), mode="r:gz") as archive:
            seen: set[str] = set()
            for info in archive:
                _member_path(info.name)
                total += info.size
                if (
                    info.name in seen
                    or not info.isfile()
                    or total > _MAX_ARCHIVE_BYTES
                    or len(seen) >= _MAX_MEMBERS
                ):
                    message = "duplicate, unsafe or oversized sdist member"
                    raise ValueError(message)
                seen.add(info.name)
                stream = archive.extractfile(info)
                if stream is None:
                    message = "unreadable sdist member"
                    raise ValueError(message)
                members[info.name] = stream.read()
    else:
        message = "unsupported Python distribution variant"
        raise ValueError(message)
    return members


def _metadata(content: bytes, version: str) -> None:
    metadata = BytesParser(policy=policy.default).parsebytes(content)
    expected = {
        "Name": PYTHON_RELEASE_UNIT,
        "Version": version,
        "Requires-Python": ">=3.14",
    }
    if metadata.defects or metadata.get_all("Requires-Dist"):
        message = "invalid or runtime-dependent Python metadata"
        raise ValueError(message)
    for name, value in expected.items():
        if metadata.get_all(name) != [value]:
            message = f"Python distribution metadata mismatch: {name}"
            raise ValueError(message)


def _wheel_record(members: dict[str, bytes], prefix: str) -> None:
    record = f"{prefix}/RECORD"
    rows = list(csv.reader(io.StringIO(members[record].decode("utf-8"))))
    if len(rows) != len(members) or any(
        len(row) != _RECORD_FIELDS for row in rows
    ):
        message = "wheel RECORD does not close its members"
        raise ValueError(message)
    if {row[0] for row in rows} != set(members):
        message = "wheel RECORD duplicates or omits members"
        raise ValueError(message)
    for name, digest, size in rows:
        if name == record:
            valid = digest == size == ""
        else:
            expected = (
                base64.urlsafe_b64encode(
                    bytes.fromhex(
                        python_digest(members[name]).removeprefix("sha256:")
                    )
                )
                .rstrip(b"=")
                .decode("ascii")
            )
            valid = digest == "sha256=" + expected and size == str(
                len(members[name])
            )
        if not valid:
            message = "wheel RECORD integrity mismatch"
            raise ValueError(message)


def _inspect_wheel(members: dict[str, bytes], version: str) -> bytes:
    prefix = f"{PYTHON_IMPORT}-{version}.dist-info"
    expected = {
        f"{PYTHON_IMPORT}/__init__.py",
        WITNESS_PATH,
        f"{prefix}/METADATA",
        f"{prefix}/WHEEL",
        f"{prefix}/RECORD",
        f"{prefix}/licenses/LICENSE",
    }
    if set(members) != expected:
        message = "wheel payload differs from the declared pure-Python closure"
        raise ValueError(message)
    _metadata(members[f"{prefix}/METADATA"], version)
    wheel = BytesParser(policy=policy.default).parsebytes(
        members[f"{prefix}/WHEEL"]
    )
    if wheel.get_all("Root-Is-Purelib") != ["true"] or wheel.get_all("Tag") != [
        "py3-none-any"
    ]:
        message = "wheel is not the admitted universal pure-Python variant"
        raise ValueError(message)
    _wheel_record(members, prefix)
    return members[WITNESS_PATH]


def _inspect_sdist(members: dict[str, bytes], version: str) -> bytes:
    prefix = f"{PYTHON_IMPORT}-{version}/"
    expected = {
        prefix + path
        for path in (*PYTHON_SOURCE_FILES, "PKG-INFO", f"src/{WITNESS_PATH}")
    }
    if set(members) != expected:
        message = "sdist payload differs from the declared source closure"
        raise ValueError(message)
    _metadata(members[prefix + "PKG-INFO"], version)
    manifest = tomllib.loads(members[prefix + "pyproject.toml"].decode("utf-8"))
    try:
        project = manifest["project"]
        backend = manifest["build-system"]
        tool = manifest["tool"]
        hatch = tool["hatch"]
        valid = (
            project.get("version") == version
            and "dynamic" not in project
            and backend["requires"] == ["hatchling==1.32.0"]
            and "version" not in hatch
            and "uv" not in tool
        )
    except (KeyError, TypeError, AttributeError) as error:
        message = "sdist lacks self-contained static metadata"
        raise ValueError(message) from error
    if not valid:
        message = "sdist lacks self-contained static metadata"
        raise ValueError(message)
    project.pop("version")
    project["dynamic"] = ["version"]
    backend["requires"].append("nbgv-python")
    hatch["version"] = {
        "source": "nbgv",
        "nbgv": {"version-field": "SemVer2"},
    }
    tool["uv"] = {"sources": {"nbgv-python": {"workspace": True}}}
    validate_python_source_manifest(tomli_w.dumps(manifest).encode())
    return members[prefix + f"src/{WITNESS_PATH}"]


def inspect_python_distribution(
    filename: str,
    content: bytes,
    variant: str,
    witness: PythonPackageTargetWitness,
) -> PythonDistribution:
    """Verify native identity, complete safe contents and exact witness."""
    version = witness.nbgv.pep440_version
    expected_filename = (
        f"{PYTHON_IMPORT}-{version}-py3-none-any.whl"
        if variant == "wheel"
        else f"{PYTHON_IMPORT}-{version}.tar.gz"
    )
    if filename != expected_filename:
        message = "Python distribution filename mismatch"
        raise ValueError(message)
    parsed = (
        parse_wheel_filename(filename)
        if variant == "wheel"
        else parse_sdist_filename(filename)
    )
    if parsed[0] != canonicalize_name(PYTHON_RELEASE_UNIT) or parsed[
        1
    ] != Version(version):
        message = "Python distribution native identity mismatch"
        raise ValueError(message)
    members = _archive_members(content, variant)
    actual = (
        _inspect_wheel(members, version)
        if variant == "wheel"
        else _inspect_sdist(members, version)
    )
    if actual != witness.canonical_bytes:
        message = "Python distribution witness mismatch"
        raise ValueError(message)
    return PythonDistribution(variant, filename, content, witness)


@dataclass(frozen=True, slots=True)
class PythonBuildRequest:
    """Frozen original source, version and producer build prerequisites."""

    witness: PythonPackageTargetWitness
    source_input_manifest: tuple[tuple[str, str], ...]
    build_constraints: bytes

    def __post_init__(self) -> None:
        """Require a unique complete source manifest and frozen constraints."""
        paths = tuple(path for path, _ in self.source_input_manifest)
        if (
            paths != tuple(sorted(set(paths)))
            or not {f"{PYTHON_ROOT}/{p}" for p in PYTHON_SOURCE_FILES}.issubset(
                paths
            )
            or dict(self.source_input_manifest).get(PYTHON_BUILD_CONSTRAINTS)
            != python_digest(self.build_constraints)
        ):
            message = "Python Build Request source closure mismatch"
            raise ValueError(message)
        for path, digest in self.source_input_manifest:
            _member_path(path)
            if not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
                message = "invalid Python Build Request source digest"
                raise ValueError(message)


@dataclass(frozen=True, slots=True)
class PythonBuildResult:
    """One invocation's exact ordered pair and retained build evidence."""

    distributions: tuple[PythonDistribution, PythonDistribution]
    staged_manifest_digest: str
    producer_versions: bytes
    command_evidence: tuple[bytes, ...]


def _environment(path: str) -> dict[str, str]:
    allowed = {"HOME", "TMPDIR", "TMP", "TEMP", "SYSTEMROOT", "WINDIR"}
    env = {
        name: value
        for name, value in os.environ.items()
        if name.upper() in allowed
    }
    env.update(
        {
            "PATH": path,
            "SOURCE_DATE_EPOCH": _SOURCE_DATE_EPOCH,
            "PYTHONNOUSERSITE": "1",
            "UV_PYTHON_DOWNLOADS": "never",
            "UV_KEYRING_PROVIDER": "disabled",
            "PYTHONDONTWRITEBYTECODE": "1",
        }
    )
    return env


def _execute(
    command: tuple[str, ...], cwd: Path, environment: dict[str, str]
) -> bytes:
    result = subprocess.run(  # noqa: S603 - fixed native tools, no shell
        command,
        cwd=cwd,
        env=environment,
        capture_output=True,
        timeout=180,
        check=False,
    )
    evidence = canonicalize(
        {
            "argv": list(command),
            "exit-code": result.returncode,
            "stdout": result.stdout.decode("utf-8", errors="replace"),
            "stderr": result.stderr.decode("utf-8", errors="replace"),
        }
    )
    if result.returncode:
        message = "Python native operation failed: " + evidence.decode("utf-8")
        raise ValueError(message)
    return evidence


def _tools(cwd: Path) -> tuple[str, str]:
    uv = shutil.which("uv")
    if (
        platform.system() != "Linux"
        or platform.python_version() != "3.14.3"
        or not uv
    ):
        message = (
            "Python smoke requires Linux with pinned CPython 3.14.3 and UV"
        )
        raise ValueError(message)
    evidence = parse_canonical_json(
        _execute((uv, "--version"), cwd, _environment(""))
    )
    if evidence["stdout"] != "uv 0.10.9\n":
        message = "Python smoke UV tool version mismatch"
        raise ValueError(message)
    return uv, str(Path(sys.executable).resolve())


def build_python_distributions(
    repo_root: Path, request: PythonBuildRequest
) -> PythonBuildResult:
    """Stage exact Git bytes and apply the already frozen version once."""
    uv, interpreter = _tools(repo_root)
    with TemporaryDirectory(prefix="wdv3-python-build-") as temporary:
        root = Path(temporary)
        stage = root / "source"
        stage.mkdir()
        original: dict[str, bytes] = {}
        for path, expected_digest in request.source_input_manifest:
            result = subprocess.run(  # noqa: S603 - exact Git data read
                ("git", "show", f"{request.witness.target}:{path}"),  # noqa: S607
                cwd=repo_root,
                capture_output=True,
                check=True,
                timeout=30,
            )
            if python_digest(result.stdout) != expected_digest:
                message = "Python Build source differs from the frozen target"
                raise ValueError(message)
            if path.startswith(PYTHON_ROOT + "/"):
                original[path.removeprefix(PYTHON_ROOT + "/")] = result.stdout
        for name in PYTHON_SOURCE_FILES:
            destination = stage / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(original[name])
        validate_python_source_manifest(original["pyproject.toml"])
        manifest = tomllib.loads(original["pyproject.toml"].decode("utf-8"))
        project = manifest["project"]
        project.pop("dynamic")
        project["version"] = request.witness.nbgv.pep440_version
        manifest["build-system"]["requires"] = ["hatchling==1.32.0"]
        manifest["tool"]["hatch"].pop("version")
        manifest["tool"].pop("uv")
        staged = tomli_w.dumps(manifest).encode("utf-8")
        (stage / "pyproject.toml").write_bytes(staged)
        (stage / "src" / WITNESS_PATH).write_bytes(
            request.witness.canonical_bytes
        )
        constraints = root / "build-requirements.txt"
        constraints.write_bytes(request.build_constraints)
        environment = root / "producer"
        python = str(environment / "bin/python")
        env = _environment(str(environment / "bin"))
        evidence = [
            _execute(
                (
                    uv,
                    "venv",
                    "--no-config",
                    "--python",
                    interpreter,
                    str(environment),
                ),
                root,
                env,
            )
        ]
        evidence.append(
            _execute(
                (
                    uv,
                    "pip",
                    "install",
                    "--no-config",
                    "--no-cache",
                    "--python",
                    python,
                    "--default-index",
                    "https://pypi.org/simple",
                    "--require-hashes",
                    "--only-binary",
                    ":all:",
                    "-r",
                    str(constraints),
                ),
                root,
                env,
            )
        )
        versions = _execute(
            (uv, "pip", "freeze", "--python", python), root, env
        )
        output = root / "dist"
        evidence.append(
            _execute(
                (
                    uv,
                    "build",
                    "--no-config",
                    "--no-cache",
                    "--no-sources",
                    "--no-build-isolation",
                    "--python",
                    python,
                    "--sdist",
                    "--wheel",
                    "--no-create-gitignore",
                    "--out-dir",
                    str(output),
                    str(stage),
                ),
                root,
                env,
            )
        )
        files = sorted(output.iterdir())
        if len(files) != _DISTRIBUTION_COUNT or any(
            not p.is_file() or p.is_symlink() for p in files
        ):
            message = (
                "Python Build must produce exactly one wheel and one sdist"
            )
            raise ValueError(message)
        pair = tuple(
            inspect_python_distribution(
                path.name,
                path.read_bytes(),
                variant,
                request.witness,
            )
            for variant in ("wheel", "sdist")
            for path in files
            if path.name.endswith(".whl" if variant == "wheel" else ".tar.gz")
        )
        if len(pair) != _DISTRIBUTION_COUNT or tuple(
            p.variant for p in pair
        ) != (
            "wheel",
            "sdist",
        ):
            message = "Python Build output variants are incomplete"
            raise ValueError(message)
        return PythonBuildResult(
            (pair[0], pair[1]), python_digest(staged), versions, tuple(evidence)
        )


@dataclass(frozen=True, slots=True)
class PythonConsumerResult:
    """Installed metadata/API proof and optional evidence-only rebuilt wheel."""

    variant: str
    original_digest: str
    installed: bytes
    command_evidence: tuple[bytes, ...]
    rebuilt_wheel: bytes | None


def qualify_python_consumer(
    distribution: PythonDistribution,
) -> PythonConsumerResult:
    """Install exact bytes outside Git with no ambient package substitution."""
    inspected = inspect_python_distribution(
        distribution.filename,
        distribution.content,
        distribution.variant,
        distribution.witness,
    )
    with TemporaryDirectory(prefix="wdv3-python-consumer-") as temporary:
        root = Path(temporary)
        uv, interpreter = _tools(root)
        archive = root / inspected.filename
        archive.write_bytes(inspected.content)
        environment = root / "consumer"
        python = str(environment / "bin/python")
        env = _environment(str(environment / "bin"))
        evidence = [
            _execute(
                (
                    uv,
                    "venv",
                    "--no-config",
                    "--python",
                    interpreter,
                    str(environment),
                ),
                root,
                env,
            )
        ]
        rebuilt: bytes | None = None
        if inspected.variant == "sdist":
            output = root / "rebuilt"
            evidence.append(
                _execute(
                    (
                        uv,
                        "build",
                        "--verbose",
                        "--no-config",
                        "--no-cache",
                        "--no-sources",
                        "--python",
                        python,
                        "--wheel",
                        "--default-index",
                        "https://pypi.org/simple",
                        "--no-create-gitignore",
                        "--out-dir",
                        str(output),
                        str(archive),
                    ),
                    root,
                    env,
                )
            )
            wheels = tuple(output.iterdir())
            if len(wheels) != 1:
                message = "Python sdist consumer did not produce one wheel"
                raise ValueError(message)
            archive = wheels[0]
            rebuilt = archive.read_bytes()
            inspect_python_distribution(
                archive.name, rebuilt, "wheel", inspected.witness
            )
        evidence.append(
            _execute(
                (
                    uv,
                    "pip",
                    "install",
                    "--no-config",
                    "--no-cache",
                    "--no-sources",
                    "--no-deps",
                    "--no-index",
                    "--python",
                    python,
                    str(archive),
                ),
                root,
                env,
            )
        )
        program = (
            "import importlib.metadata as m, json, pathlib; "
            f"import {PYTHON_IMPORT} as p; "
            f"d=m.distribution({PYTHON_RELEASE_UNIT!r}); "
            f"w=pathlib.Path(p.__file__).with_name({WITNESS_BASENAME!r}); "
            "w=w.read_text(); "
            "print(json.dumps({'version':d.version,'project-id':p.project_id(),"
            "'witness':json.loads(w),'module':str(pathlib.Path(p.__file__).resolve())}))"
        )
        installed_evidence = _execute((python, "-I", "-c", program), root, env)
        evidence.append(installed_evidence)
        stdout = parse_canonical_json(installed_evidence)["stdout"]
        if not isinstance(stdout, str):
            message = "Python consumer stdout must be text"
            raise TypeError(message)
        observed = parse_json_strict(stdout)
        doc = python_object(
            observed, {"version", "project-id", "witness", "module"}
        )
        if (
            doc["version"] != inspected.witness.nbgv.pep440_version
            or doc["project-id"] != PYTHON_RELEASE_UNIT
            or canonicalize(doc["witness"]) != inspected.witness.canonical_bytes
            or not Path(python_text(doc["module"])).is_relative_to(environment)
        ):
            message = "Python clean installed-consumer evidence mismatch"
            raise ValueError(message)
        return PythonConsumerResult(
            inspected.variant,
            inspected.digest,
            canonicalize(doc),
            tuple(evidence),
            rebuilt,
        )
