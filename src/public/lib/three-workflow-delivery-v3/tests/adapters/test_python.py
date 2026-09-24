"""Original Python distributions, archive admission and clean consumers."""

from __future__ import annotations

# Trusted native integration fixtures execute fixed commands without a shell.
# ruff: noqa: S603, S607
import base64
import csv
import hashlib
import io
import os
import shutil
import stat
import subprocess
import tarfile
import tomllib
import zipfile
from dataclasses import replace
from pathlib import Path

import pytest
import tomli_w
from nbgv_python.versioning import normalize_version_field
from three_workflow_delivery_v3.adapters.python import (
    WITNESS_PATH,
    PythonBuildRequest,
    PythonPackageTargetWitness,
    build_python_distributions,
    inspect_python_distribution,
    python_package_target_witness_from_document,
    qualify_python_consumer,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository import python_provider
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
    ProviderBinding,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_IMPORT,
    PYTHON_MANIFEST,
    PYTHON_POLICY,
    PYTHON_RELEASE_UNIT,
    PYTHON_ROOT,
    PythonNbgvFacts,
    provide_python_repository_facts,
    require_public_python_version,
    validate_python_source_manifest,
)

_ROOT = Path(__file__).resolve().parents[6]


def _witness():
    facts = PythonNbgvFacts(
        canonicalize(
            {
                "SimpleVersion": "0.1.0",
                "SemVer2": "0.1.0-beta.7",
                "GitCommitId": "a" * 40,
                "VersionHeight": 7,
                "PublicRelease": False,
            }
        ),
        "0.1.0b7",
    )
    return PythonPackageTargetWitness(
        "a" * 40,
        facts,
        "sha256:" + "b" * 64,
        "sha256:" + "c" * 64,
        "release-simulation",
    )


def _members(variant, witness):
    version = witness.nbgv.pep440_version
    metadata = (
        f"Metadata-Version: 2.4\nName: {PYTHON_RELEASE_UNIT}\n"
        f"Version: {version}\nRequires-Python: >=3.14\n\n"
    ).encode()
    module = b"def project_id(): return 'hcoona-release-smoke-python'\n"
    if variant == "wheel":
        prefix = f"{PYTHON_IMPORT}-{version}.dist-info"
        return {
            f"{PYTHON_IMPORT}/__init__.py": module,
            WITNESS_PATH: witness.canonical_bytes,
            f"{prefix}/METADATA": metadata,
            f"{prefix}/WHEEL": (
                b"Wheel-Version: 1.0\nRoot-Is-Purelib: true\n"
                b"Tag: py3-none-any\n"
            ),
            f"{prefix}/licenses/LICENSE": b"MIT License\n",
        }
    prefix = f"{PYTHON_IMPORT}-{version}/"
    manifest = validate_python_source_manifest(
        (_ROOT / PYTHON_MANIFEST).read_bytes()
    )
    manifest["project"].pop("dynamic")
    manifest["project"]["version"] = version
    manifest["build-system"]["requires"] = ["hatchling==1.32.0"]
    manifest["tool"]["hatch"].pop("version")
    manifest["tool"].pop("uv")
    return {
        prefix + "pyproject.toml": tomli_w.dumps(manifest).encode(),
        prefix + "PKG-INFO": metadata,
        prefix + "README.md": b"Python smoke\n",
        prefix + "LICENSE": b"MIT License\n",
        prefix + f"src/{PYTHON_IMPORT}/__init__.py": module,
        prefix + f"src/{WITNESS_PATH}": witness.canonical_bytes,
    }


def _archive(variant, members, *, duplicate=None):
    """Create native bytes with independently calculated RECORD hashes."""
    members = dict(members)
    if variant == "wheel":
        metadata = next(p for p in members if p.endswith("/METADATA"))
        record = metadata.removesuffix("METADATA") + "RECORD"
        buffer = io.StringIO()
        writer = csv.writer(buffer)
        for name, content in members.items():
            digest = (
                base64.urlsafe_b64encode(hashlib.sha256(content).digest())
                .decode()
                .rstrip("=")
            )
            writer.writerow([name, "sha256=" + digest, len(content)])
        writer.writerow([record, "", ""])
        members[record] = buffer.getvalue().encode()
    output = io.BytesIO()
    entries = list(members.items())
    if duplicate is not None:
        entries.append((duplicate, members[duplicate]))
    if variant == "wheel":
        with zipfile.ZipFile(output, "w") as archive:
            for name, content in entries:
                archive.writestr(name, content)
    else:
        with tarfile.open(fileobj=output, mode="w:gz") as archive:
            for name, content in entries:
                info = tarfile.TarInfo(name)
                info.size = len(content)
                archive.addfile(info, io.BytesIO(content))
    return output.getvalue()


def _filename(variant, witness):
    suffix = "-py3-none-any.whl" if variant == "wheel" else ".tar.gz"
    return f"{PYTHON_IMPORT}-{witness.nbgv.pep440_version}{suffix}"


def test_python_witness_has_only_stable_package_identity():
    """Embedded target and purpose omit execution authority."""
    document = _witness().to_document()
    assert set(document) == {
        "schema",
        "target",
        "release-unit",
        "nbgv",
        "build-definition",
        "catalog-digest",
        "control-digest",
        "purpose",
    }
    assert document["release-unit"] == "hcoona-release-smoke-python"
    assert document["build-definition"] == "python/distribution-set-v1"
    assert (
        parse_canonical_json(_witness().canonical_bytes)["nbgv"][
            "pep440-version"
        ]
        == "0.1.0b7"
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/dotnet-package-target-witness"),
        ("release-unit", "other-package"),
        ("build-definition", "python/other"),
        ("target", "d" * 40),
        ("purpose", "borrowed-approval"),
        ("control-digest", "not-a-digest"),
        ("destination", "pypi"),
    ],
)
def test_python_witness_rejects_substituted_identity(field, value):
    """Reject cross-target, schema, purpose and open provenance records."""
    document = _witness().to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_package_target_witness_from_document(document)


@pytest.mark.parametrize("variant", ["wheel", "sdist"])
def test_python_distribution_inspection_admits_exact_native_contents(variant):
    """Native inspection binds format, bytes and witness."""
    witness = _witness()
    content = _archive(variant, _members(variant, witness))
    result = inspect_python_distribution(
        _filename(variant, witness), content, variant, witness
    )
    assert result.variant == variant
    assert result.digest == "sha256:" + hashlib.sha256(content).hexdigest()
    assert result.witness.target == "a" * 40
    assert result.witness.nbgv.pep440_version == "0.1.0b7"


@pytest.mark.parametrize("variant", ["wheel", "sdist"])
@pytest.mark.parametrize(
    "change",
    [
        "traversal",
        "absolute",
        "backslash",
        "duplicate",
        "extra",
        "missing",
        "version",
        "dependency",
        "duplicate-name",
        "witness",
    ],
)
def test_python_distribution_rejects_unsafe_or_ambiguous_archives(
    variant, change
):
    """Unsafe members and native identity mismatches fail before execution."""
    witness = _witness()
    members = _members(variant, witness)
    metadata = next(
        p for p in members if p.endswith(("/METADATA", "/PKG-INFO"))
    )
    witness_path = next(
        p for p in members if p.endswith("_workflow_delivery_provenance.json")
    )
    duplicate = None
    if change in {"traversal", "absolute", "backslash", "extra"}:
        path = {
            "traversal": "../outside",
            "absolute": "/outside",
            "backslash": "pkg\\outside",
            "extra": "undeclared.txt",
        }[change]
        members[path] = b"untrusted"
    elif change == "duplicate":
        duplicate = metadata
    elif change == "missing":
        members.pop(witness_path)
    elif change == "version":
        members[metadata] = members[metadata].replace(
            b"Version: 0.1.0b7", b"Version: 0.1.0b8"
        )
    elif change == "dependency":
        members[metadata] = members[metadata].replace(
            b"\n\n", b"\nRequires-Dist: requests\n\n"
        )
    elif change == "duplicate-name":
        members[metadata] = members[metadata].replace(
            b"\n\n", b"\nName: hcoona-release-smoke-python\n\n"
        )
    else:
        members[witness_path] = replace(
            witness, purpose="live-release"
        ).canonical_bytes
    if duplicate is not None and variant == "wheel":
        with pytest.warns(UserWarning, match="Duplicate name"):
            content = _archive(variant, members, duplicate=duplicate)
    else:
        content = _archive(variant, members, duplicate=duplicate)
    with pytest.raises(ValueError, match=r"Python|wheel|sdist"):
        inspect_python_distribution(
            _filename(variant, witness), content, variant, witness
        )


def test_python_wheel_rejects_corrupt_record_hash():
    """A valid ZIP CRC cannot replace wheel RECORD integrity."""
    witness = _witness()
    original = _archive("wheel", _members("wheel", witness))
    output = io.BytesIO()
    with (
        zipfile.ZipFile(io.BytesIO(original)) as source,
        zipfile.ZipFile(output, "w") as destination,
    ):
        for info in source.infolist():
            value = source.read(info)
            if info.filename.endswith("/__init__.py"):
                value = b"tampered = True\n"
            destination.writestr(info.filename, value)
    with pytest.raises(ValueError, match="RECORD integrity"):
        inspect_python_distribution(
            _filename("wheel", witness), output.getvalue(), "wheel", witness
        )


@pytest.mark.parametrize("variant", ["wheel", "sdist"])
def test_python_distribution_rejects_archive_symlinks(variant):
    """An archive link is never treated as ordinary package file bytes."""
    witness = _witness()
    members = _members(variant, witness)
    name = next(iter(members))
    output = io.BytesIO()
    if variant == "wheel":
        with zipfile.ZipFile(output, "w") as archive:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = (stat.S_IFLNK | 0o777) << 16
            archive.writestr(info, "../../outside")
    else:
        with tarfile.open(fileobj=output, mode="w:gz") as archive:
            info = tarfile.TarInfo(name)
            info.type = tarfile.SYMTYPE
            info.linkname = "../../outside"
            archive.addfile(info)
    with pytest.raises(ValueError, match="unsafe"):
        inspect_python_distribution(
            _filename(variant, witness), output.getvalue(), variant, witness
        )


@pytest.mark.parametrize(
    "change",
    [
        "dynamic",
        "nbgv",
        "workspace",
        "custom-hook",
        "wrong-version",
        "build-requirement",
    ],
)
def test_python_sdist_rejects_ambient_or_dynamic_build_metadata(change):
    """An sdist consumer cannot fall back to NBGV, workspace or custom hooks."""
    witness = _witness()
    members = _members("sdist", witness)
    path = next(p for p in members if p.endswith("/pyproject.toml"))
    manifest = tomllib.loads(members[path].decode())
    if change == "dynamic":
        manifest["project"]["dynamic"] = ["version"]
    elif change == "nbgv":
        manifest["tool"]["hatch"]["version"] = {"source": "nbgv"}
    elif change == "workspace":
        manifest["tool"]["uv"] = {
            "sources": {"nbgv-python": {"workspace": True}}
        }
    elif change == "custom-hook":
        manifest["tool"]["hatch"]["build"]["hooks"] = {"custom": {}}
    elif change == "wrong-version":
        manifest["project"]["version"] = "0.1.0b8"
    else:
        manifest["build-system"]["requires"].append("nbgv-python")
    members[path] = tomli_w.dumps(manifest).encode()
    with pytest.raises(ValueError, match=r"Python|sdist"):
        inspect_python_distribution(
            _filename("sdist", witness),
            _archive("sdist", members),
            "sdist",
            witness,
        )


@pytest.mark.parametrize("change", ["platform", "impure", "duplicate-tag"])
def test_python_wheel_rejects_unsupported_tags_or_purity(change):
    """The declared universal pure-Python closure is checked in native WHEEL."""
    witness = _witness()
    members = _members("wheel", witness)
    path = next(p for p in members if p.endswith("/WHEEL"))
    if change == "platform":
        members[path] = members[path].replace(
            b"py3-none-any", b"cp314-cp314-linux_x86_64"
        )
    elif change == "impure":
        members[path] = members[path].replace(b"true", b"false")
    else:
        members[path] += b"Tag: py3-none-any\n"
    with pytest.raises(ValueError, match="universal pure-Python"):
        inspect_python_distribution(
            _filename("wheel", witness),
            _archive("wheel", members),
            "wheel",
            witness,
        )


@pytest.fixture(scope="module")
def native_python_build(tmp_path_factory):
    """Evaluate actual NBGV against a small complete local Git history."""
    root = tmp_path_factory.mktemp("python-native")
    origin = root / "origin"
    origin.mkdir()
    for path in (
        PYTHON_ROOT,
        "src/public/lib/nbgv-python",
        "src/public/lib/three-workflow-delivery-v3/src",
    ):
        shutil.copytree(
            _ROOT / path,
            origin / path,
            ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
        )
    for path in (
        ".config/dotnet-tools.json",
        "global.json",
        "mise.lock",
        "mise.toml",
        "pyproject.toml",
        "uv.lock",
        PYTHON_BUILD_CONSTRAINTS,
        PYTHON_POLICY,
    ):
        destination = origin / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(_ROOT / path, destination)
    subprocess.run(
        ("git", "init", "--quiet", "--initial-branch=main"),
        cwd=origin,
        check=True,
    )
    subprocess.run(("git", "add", "."), cwd=origin, check=True)
    commit = (
        "git",
        "-c",
        "user.name=Python Native Test",
        "-c",
        "user.email=python-test@example.invalid",
        "-c",
        "core.hooksPath=/dev/null",
        "commit",
        "--quiet",
        "-m",
    )
    subprocess.run(
        (*commit, "Seed complete Python inputs"), cwd=origin, check=True
    )
    readme = origin / PYTHON_ROOT / "README.md"
    readme.write_text(readme.read_text() + "\nNative integration revision.\n")
    subprocess.run(("git", "add", "."), cwd=origin, check=True)
    subprocess.run(
        (*commit, "Advance versioned Python source"), cwd=origin, check=True
    )
    subprocess.run(
        ("git", "-c", "tag.gpgSign=false", "tag", "python-fixture-history"),
        cwd=origin,
        check=True,
    )
    source = root / "source"
    subprocess.run(
        ("git", "clone", "--quiet", "--no-local", str(origin), str(source)),
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
    )
    subprocess.run(
        ("git", "checkout", "--quiet", "--detach"), cwd=source, check=True
    )
    target = subprocess.check_output(
        ("git", "rev-parse", "HEAD"), cwd=source, text=True
    ).strip()
    binding = ProviderBinding(
        "native-python-test",
        "release-simulation",
        201,
        1,
        target,
        "python-provider",
        target,
        catalog_digest(),
        "sha256:" + "d" * 64,
    )
    calls = []
    real_native = python_provider.run_native

    def run_native(command, cwd):
        calls.append(command)
        return real_native(command, cwd)

    with pytest.MonkeyPatch.context() as patch:
        patch.setattr(python_provider, "run_native", run_native)
        provider = provide_python_repository_facts(
            source,
            binding,
            CheckoutMaterialization(0, credentials_persisted=False),
        )
    witness = PythonPackageTargetWitness(
        target,
        provider.nbgv,
        catalog_digest(),
        "sha256:" + "e" * 64,
        "release-simulation",
    )
    request = PythonBuildRequest(
        witness, provider.source_input_manifest, provider.build_constraints
    )
    return (
        source,
        provider,
        request,
        build_python_distributions(source, request),
        calls,
    )


def test_real_python_build_freezes_native_facts_and_original_pair(
    native_python_build,
):
    """Full-history NBGV evaluates once to produce two static archives."""
    source, provider, request, result, calls = native_python_build
    evaluations = [
        call for call in calls if call[:3] == ("dotnet", "nbgv", "get-version")
    ]
    assert len(evaluations) == 1
    assert evaluations[0][-1] == PYTHON_ROOT
    assert provider.checkout.ancestry_complete is True
    assert provider.checkout.tags_complete is True
    assert provider.nbgv.target == request.witness.target
    assert provider.nbgv.pep440_version.startswith("0.1.0b")
    assert "+" in provider.nbgv.pep440_version
    with pytest.raises(ValueError, match="public version"):
        require_public_python_version(provider.nbgv)
    assert tuple(d.variant for d in result.distributions) == ("wheel", "sdist")
    assert all(
        d.witness.canonical_bytes == request.witness.canonical_bytes
        for d in result.distributions
    )
    assert b"nbgv" not in b"".join(result.command_evidence)
    assert (source / ".git").is_dir()
    frozen = parse_canonical_json(result.producer_versions)
    assert "hatchling==1.32.0" in frozen["stdout"]


def test_real_python_build_is_reproducible_for_frozen_inputs(
    native_python_build, monkeypatch
):
    """Independent builds retain identical original bytes and filenames."""
    source, _, request, first, _ = native_python_build
    manifest = source / PYTHON_MANIFEST
    original = manifest.read_bytes()
    manifest.write_bytes(
        b"Uncommitted checkout substitution must not enter Build.\n"
    )
    monkeypatch.setenv("SOURCE_DATE_EPOCH", "1700000000")
    monkeypatch.setenv("NBGV_PackageVersion", "99.0.0")
    try:
        second = build_python_distributions(source, request)
    finally:
        manifest.write_bytes(original)
    assert tuple((d.filename, d.digest) for d in second.distributions) == tuple(
        (d.filename, d.digest) for d in first.distributions
    )
    assert second.staged_manifest_digest == first.staged_manifest_digest


def test_real_python_live_provider_uses_public_ref_without_rewriting(
    native_python_build,
):
    """The disposable main ref selects the native public projection."""
    source, provider, _, _, _ = native_python_build
    live = provide_python_repository_facts(
        source,
        replace(provider.binding, purpose="live-release", run_attempt=None),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    require_public_python_version(live.nbgv)
    assert "+" not in live.nbgv.pep440_version
    assert live.nbgv.target == provider.nbgv.target
    assert parse_canonical_json(live.nbgv.raw_bytes)["PublicRelease"] is True
    assert live.nbgv.pep440_version == normalize_version_field(
        parse_canonical_json(live.nbgv.raw_bytes)["SemVer2"], field="SemVer2"
    )
    assert (
        subprocess.run(
            ("git", "symbolic-ref", "--quiet", "HEAD"),
            cwd=source,
            check=False,
            capture_output=True,
        ).returncode
        == 1
    )


def test_real_python_build_rejects_substituted_frozen_source_digest(
    native_python_build,
):
    """A request cannot relabel committed package bytes with another digest."""
    source, _, request, _, _ = native_python_build
    changed = replace(
        request,
        source_input_manifest=tuple(
            (path, "sha256:" + "0" * 64 if path == PYTHON_MANIFEST else digest)
            for path, digest in request.source_input_manifest
        ),
    )
    with pytest.raises(ValueError, match="frozen target"):
        build_python_distributions(source, changed)


@pytest.mark.parametrize("variant", ["wheel", "sdist"])
def test_real_python_consumer_installs_exact_original_outside_repository(
    native_python_build, monkeypatch, variant
):
    """Fresh consumers prove version, project API and target witness."""
    _, _, request, built, _ = native_python_build
    for name in (
        "PYTHONPATH",
        "NBGV_PackageVersion",
        "GITHUB_TOKEN",
        "UV_INDEX_URL",
    ):
        monkeypatch.setenv(name, "must-not-survive")
    original = next(d for d in built.distributions if d.variant == variant)
    result = qualify_python_consumer(original)
    installed = parse_canonical_json(result.installed)
    assert result.original_digest == original.digest
    assert installed["version"] == request.witness.nbgv.pep440_version
    assert installed["project-id"] == "hcoona-release-smoke-python"
    assert installed["witness"]["target"] == request.witness.target
    assert not Path(installed["module"]).is_relative_to(_ROOT)
    assert (result.rebuilt_wheel is not None) is (variant == "sdist")
    assert b"must-not-survive" not in b"".join(result.command_evidence)
