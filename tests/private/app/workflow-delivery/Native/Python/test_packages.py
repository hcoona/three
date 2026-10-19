"""Controlled package boundary tests; no adopted backend build is executed."""

from __future__ import annotations

import importlib.util
import io
import json
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

ROOT = Path(__file__).resolve().parents[6]
SPEC = importlib.util.spec_from_file_location(
    "workflow_delivery_python_packages",
    ROOT / "src/private/app/workflow-delivery/Native/Python/packages.py",
)
assert SPEC is not None
assert SPEC.loader is not None
packages = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(packages)
NAME = "hcoona-release-smoke-python"
VERSION = "1.0.0.dev3+g1234567"
STEM = "hcoona_release_smoke_python-" + VERSION
METADATA = f"Metadata-Version: 2.4\nName: {NAME}\nVersion: {VERSION}\n".encode()


def public_index(tmp_path, files, *, name=NAME, api="1.4"):
    """Write one supplied public index without contacting a destination."""
    path = tmp_path / "index.json"
    path.write_text(
        json.dumps({"meta": {"api-version": api}, "name": name, "files": files})
    )
    return path


def index_entry(filename, **values):
    """Supply native file metadata without installed/source identities."""
    return {
        "filename": filename,
        "url": "https://test-files.pythonhosted.org/" + filename,
        "hashes": {"sha256": "a" * 64},
        **values,
    }


def test_public_index_uses_native_names_versions_and_filename_semantics(
    tmp_path,
):
    """Packaging owns normalization and selected-version equivalence."""
    entries = [
        index_entry("hcoona_release_smoke_python-1.2.3.0-py3-none-any.whl"),
        index_entry("hcoona_release_smoke_python-1.2.3.tar.gz"),
        index_entry("hcoona_release_smoke_python-2.0.tar.gz"),
    ]
    facts = packages.index_facts(
        public_index(tmp_path, entries, name="Hcoona_Release.Smoke-Python"),
        NAME,
        "1.2.3",
    )
    assert facts["matches_name"] is True
    assert [entry["filename"] for entry in facts["files"]] == [
        entry["filename"] for entry in entries[:2]
    ]
    assert all(entry["sha256"] == "a" * 64 for entry in facts["files"])
    assert all(entry["yanked"] is False for entry in facts["files"])


def test_public_index_cli_uses_isolated_native_packaging_and_complete_json(
    tmp_path,
):
    """Exercise the isolated native protocol without network or credentials."""
    path = public_index(
        tmp_path, [index_entry("hcoona_release_smoke_python-1.2.3.tar.gz")]
    )
    result = subprocess.run(  # noqa: S603 - Controlled maintained Python helper.
        [
            sys.executable,
            "-I",
            "-B",
            SPEC.origin,
            "index-facts",
            str(path),
            NAME,
            "1.2.3",
        ],
        cwd=tmp_path,
        env={},
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert result.stderr == ""
    assert json.loads(result.stdout) == packages.index_facts(
        path, NAME, "1.2.3"
    )


@pytest.mark.parametrize("yanked", [True, "withdrawn", ""])
def test_public_index_preserves_native_yanked_visibility(tmp_path, yanked):
    """An empty native reason still means that the selected file is yanked."""
    facts = packages.index_facts(
        public_index(tmp_path, [index_entry(STEM + ".tar.gz", yanked=yanked)]),
        NAME,
        VERSION,
    )
    assert facts["files"][0]["yanked"] is True


@pytest.mark.parametrize("foreign", ["project", "file"])
def test_public_index_reports_contradictory_native_names(tmp_path, foreign):
    """Foreign native coordinates cannot become a complete matching set."""
    filename = "other" if foreign == "file" else "hcoona_release_smoke_python"
    facts = packages.index_facts(
        public_index(
            tmp_path,
            [index_entry(filename + "-1.2.3.tar.gz")],
            name="other" if foreign == "project" else NAME,
        ),
        NAME,
        "1.2.3",
    )
    assert facts["matches_name"] is False


@pytest.mark.parametrize(
    "defect", ["api", "filename", "requires-python", "yanked", "inventory"]
)
def test_public_index_rejects_unavailable_or_unsupported_native_facts(
    tmp_path, defect
):
    """Native interpretation failures supply no partial successful facts."""
    entry = index_entry(STEM + ".tar.gz")
    if defect == "filename":
        entry["filename"] = "uninterpretable"
    elif defect == "requires-python":
        entry["requires-python"] = "unsupported constraint"
    elif defect == "yanked":
        entry["yanked"] = None
    entries = [entry] * (4097 if defect == "inventory" else 1)
    with pytest.raises(
        (ValueError, TypeError),
        match=r"metadata|filename|specifier|visibility|bound",
    ):
        packages.index_facts(
            public_index(
                tmp_path, entries, api="2.0" if defect == "api" else "1.0"
            ),
            NAME,
            VERSION,
        )


def wheel(directory, *, metadata=METADATA, payload=True, extra=None):
    """Wheel."""
    path = directory / (STEM + "-py3-none-any.whl")
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(STEM + ".dist-info/METADATA", metadata)
        if payload:
            archive.writestr(
                "hcoona_release_smoke_python/__init__.py", b"product"
            )
        if extra:
            archive.writestr(*extra)
    return path


def sdist(directory, *, metadata=METADATA, payload=True, extra=None):
    """Sdist."""
    path = directory / (STEM + ".tar.gz")
    files = {"PKG-INFO": metadata}
    if payload:
        files.update(
            {
                "src/hcoona_release_smoke_python/__init__.py": b"product",
                "pyproject.toml": b"native project",
                "README.md": b"readme",
                "LICENSE": b"license",
            }
        )
    with tarfile.open(path, "w:gz") as archive:
        for name, data in files.items():
            info = tarfile.TarInfo(STEM + "/" + name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
        if extra:
            archive.addfile(extra)
    return path


def test_complete_original_set_retains_native_identity_and_bytes(tmp_path):
    """Complete original set retains native identity and bytes."""
    originals = [wheel(tmp_path), sdist(tmp_path)]
    before = {str(path): path.read_bytes() for path in originals}
    outputs = packages.inspect_outputs(
        tmp_path, NAME, VERSION, packages.QUALITY
    )
    assert {output["kind"] for output in outputs} == {
        "python-wheel",
        "python-sdist",
    }
    for output in outputs:
        assert output["path"] in before
        assert output["length"] == len(before[output["path"]])
        assert len(output["sha256"]) == 64
        assert Path(output["path"]).read_bytes() == before[output["path"]]


def test_output_paths_keep_the_requested_directory_spelling(tmp_path):
    """Output paths keep the requested directory spelling."""
    target = tmp_path / "target"
    target.mkdir()
    wheel(target)
    sdist(target)
    alias = tmp_path / "alias"
    try:
        alias.symlink_to(target, target_is_directory=True)
    except OSError:
        pytest.skip("Directory links are unavailable on this platform")
    outputs = packages.inspect_outputs(alias, NAME, VERSION, packages.QUALITY)
    assert [Path(output["path"]).parent for output in outputs] == [alias, alias]


@pytest.mark.parametrize(
    "defect", ["missing", "extra", "wrong-version", "wrong-name"]
)
def test_complete_set_rejects_missing_extra_or_wrong_identity(tmp_path, defect):
    """Complete set rejects missing extra or wrong identity."""
    wheel(tmp_path)
    if defect != "missing":
        metadata = METADATA
        if defect == "wrong-version":
            metadata = metadata.replace(VERSION.encode(), b"2.0")
        if defect == "wrong-name":
            metadata = metadata.replace(NAME.encode(), b"other")
        sdist(tmp_path, metadata=metadata)
    if defect == "extra":
        (tmp_path / "extra.txt").write_text("undeclared")
    with pytest.raises(
        ValueError, match=r"identity|archive|distribution|sdist"
    ):
        packages.inspect_outputs(tmp_path, NAME, VERSION, packages.QUALITY)


@pytest.mark.parametrize("make", [wheel, sdist])
def test_product_payload_is_separate_from_complete_build(tmp_path, make):
    """Product payload is separate from complete build."""
    path = make(tmp_path, payload=False)
    result = packages.inspect_archive(
        path, NAME, VERSION, packages.QUALITY, check_contents=False
    )
    assert result["path"] == str(path)
    with pytest.raises(ValueError, match="registered product payload"):
        packages.inspect_archive(path, NAME, VERSION, packages.QUALITY)


@pytest.mark.parametrize(
    "coordinate", ["/absolute", "a/../b", "a//b", "C:/b", "a\\b"]
)
def test_reader_rejects_ambiguous_paths(tmp_path, coordinate):
    """Reader rejects ambiguous paths."""
    path = wheel(tmp_path, extra=(coordinate, b"unsafe"))
    with pytest.raises(ValueError, match="member path"):
        packages.inspect_archive(path, NAME, VERSION, packages.QUALITY)


@pytest.mark.parametrize(
    "defect", ["duplicate-header", "duplicate-member", "link", "size"]
)
def test_reader_rejects_ambiguity_links_and_limits(
    tmp_path, monkeypatch, defect
):
    """Reader rejects ambiguity links and limits."""
    if defect == "duplicate-header":
        path = wheel(tmp_path, metadata=METADATA + b"Version: 2.0\n")
    elif defect == "duplicate-member":
        with pytest.warns(UserWarning, match="Duplicate name"):
            path = wheel(
                tmp_path, extra=(STEM + ".dist-info/METADATA", METADATA)
            )
    elif defect == "link":
        info = tarfile.TarInfo(STEM + "/link")
        info.type = tarfile.SYMTYPE
        info.linkname = "outside"
        path = sdist(tmp_path, extra=info)
    else:
        path = sdist(tmp_path)
        monkeypatch.setattr(packages, "MAX_SDIST_CONTENTS", 1)
    with pytest.raises(
        ValueError, match=r"identity|archive|distribution|sdist"
    ):
        packages.inspect_archive(path, NAME, VERSION, packages.QUALITY)


def test_native_identity_uses_maintained_backend_without_version_engine(
    tmp_path, monkeypatch
):
    """Native identity uses maintained backend without version engine."""
    factory = Mock(
        return_value=SimpleNamespace(
            metadata=SimpleNamespace(
                core=SimpleNamespace(name=NAME), version=VERSION
            )
        )
    )
    monkeypatch.setitem(
        sys.modules,
        "hatchling.builders.wheel",
        SimpleNamespace(WheelBuilder=factory),
    )
    assert packages.native_identity(tmp_path) == {
        "name": NAME,
        "version": VERSION,
    }
    factory.assert_called_once_with(str(tmp_path.resolve()))


NBGV = "nbgv-python"
NBGV_VERSION = "2.1.0.dev1"
NBGV_STEM = "nbgv_python-" + NBGV_VERSION
NBGV_MODULES = ["__init__.py", "cli.py", "hatch_plugin.py"]
NBGV_METADATA = (
    f"Metadata-Version: 2.4\nName: {NBGV}\nVersion: {NBGV_VERSION}\n"
    "Requires-Dist: packaging>=24.0\n"
).encode()


def nbgv_wheel(directory, metadata=NBGV_METADATA):
    """Wheel carrying the registered nbgv-python payload."""
    path = directory / (NBGV_STEM + "-py3-none-any.whl")
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(NBGV_STEM + ".dist-info/METADATA", metadata)
        for module in NBGV_MODULES:
            archive.writestr("nbgv_python/" + module, b"product")
    return path


def nbgv_sdist(directory, metadata=NBGV_METADATA):
    """Sdist carrying the registered nbgv-python payload."""
    path = directory / (NBGV_STEM + ".tar.gz")
    files = {"PKG-INFO": metadata, "pyproject.toml": b"p", "README.md": b"r"}
    files.update({"src/nbgv_python/" + module: b"m" for module in NBGV_MODULES})
    with tarfile.open(path, "w:gz") as archive:
        for name, data in files.items():
            info = tarfile.TarInfo(NBGV_STEM + "/" + name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
    return path


@pytest.mark.parametrize("requires", [None, "", "packaging>=99\n"])
def test_nbgv_originals_require_their_payload_and_declared_dependency(
    tmp_path, requires
):
    """Nbgv originals require their payload and declared dependency."""
    metadata = NBGV_METADATA
    if requires is not None:
        metadata = metadata.replace(b"packaging>=24.0\n", b"")
        metadata = metadata.replace(b"Requires-Dist: ", b"")
        if requires:
            metadata += b"Requires-Dist: " + requires.encode()
    nbgv_wheel(tmp_path, metadata)
    nbgv_sdist(tmp_path, metadata)
    if requires is None:
        outputs = packages.inspect_outputs(
            tmp_path, NBGV, NBGV_VERSION, packages.QUALITY
        )
        assert [output["kind"] for output in outputs] == [
            "python-sdist",
            "python-wheel",
        ]
    else:
        with pytest.raises(ValueError, match="identity"):
            packages.inspect_outputs(
                tmp_path, NBGV, NBGV_VERSION, packages.QUALITY
            )


def test_unregistered_product_is_unsupported(tmp_path):
    """Unregistered product is unsupported."""
    with pytest.raises(ValueError, match="Unsupported registered"):
        packages.inspect_archive(
            nbgv_wheel(tmp_path), "other", NBGV_VERSION, packages.QUALITY
        )


@pytest.mark.parametrize("dynamic", [False, True])
def test_static_version_facts_come_from_the_maintained_backend(
    tmp_path, monkeypatch, dynamic
):
    """Static version facts come from the maintained backend."""
    core = SimpleNamespace(
        name=NBGV,
        dynamic=["version"] if dynamic else [],
        dependencies=["packaging>=24.0"],
    )
    factory = Mock(
        return_value=SimpleNamespace(
            metadata=SimpleNamespace(core=core, version=NBGV_VERSION)
        )
    )
    monkeypatch.setitem(
        sys.modules,
        "hatchling.builders.wheel",
        SimpleNamespace(WheelBuilder=factory),
    )
    if dynamic:
        with pytest.raises(ValueError, match="version source"):
            packages.native_static_version_facts(tmp_path)
        return
    facts = packages.native_static_version_facts(tmp_path)
    assert facts["name"] == NBGV
    assert facts["version"] == NBGV_VERSION
    assert facts["local_version"] is None
    assert facts["dependencies"] == ["packaging>=24.0"]
    factory.assert_called_once_with(str(tmp_path.resolve()))


@pytest.mark.parametrize("defect", [None, "base-dependency", "api"])
def test_nbgv_consumer_requires_api_and_dependency_from_its_venv(
    tmp_path, monkeypatch, defect
):
    """Nbgv consumer requires api and dependency from its venv."""
    root = tmp_path / "venv"
    origin = root / "lib/nbgv_python/__init__.py"
    installed = SimpleNamespace(
        metadata={"Name": NBGV},
        version=NBGV_VERSION,
        files=["lib/nbgv_python/__init__.py"],
        locate_file=lambda file: root / file,
    )
    dependency_root = tmp_path if defect == "base-dependency" else root / "lib"
    dependency = SimpleNamespace(locate_file=lambda _file: dependency_root)
    distributions = {NBGV: installed, "packaging": dependency}
    api = {
        name: (lambda: None)
        for name in ["get_version", "forward", "NbgvRunner", "GitVersion"]
    }
    if defect == "api":
        del api["forward"]
    module = SimpleNamespace(__file__=str(origin), **api)
    monkeypatch.setattr(
        packages.importlib.metadata, "distribution", distributions.__getitem__
    )
    monkeypatch.setattr(
        packages.importlib, "import_module", lambda _name: module
    )
    monkeypatch.setattr(sys, "prefix", str(root))
    monkeypatch.setattr(sys, "base_prefix", str(tmp_path / "base"))
    if defect is None:
        assert packages.consume(NBGV, NBGV_VERSION, packages.QUALITY) == {
            "name": NBGV,
            "version": NBGV_VERSION,
            "origin": str(origin),
        }
    else:
        with pytest.raises(ValueError, match="registered API"):
            packages.consume(NBGV, NBGV_VERSION, packages.QUALITY)


@pytest.mark.parametrize(
    "defect",
    [None, "source-origin", "unlisted-origin", "version", "api", "no-venv"],
)
def test_installed_consumer_requires_native_metadata_origin_and_registered_api(
    tmp_path, monkeypatch, defect
):
    """Installed consumer requires native metadata origin and registered api."""
    root = tmp_path / "venv"
    origin = root / "lib/package.py"
    if defect == "source-origin":
        origin = tmp_path / "checkout/package.py"
    distribution = SimpleNamespace(
        metadata={"Name": NAME},
        version="2.0" if defect == "version" else VERSION,
        files=[] if defect == "unlisted-origin" else ["lib/package.py"],
        locate_file=lambda file: root / file,
    )
    module = SimpleNamespace(
        __file__=str(origin),
        project_id=lambda: "other" if defect == "api" else NAME,
    )
    monkeypatch.setattr(
        packages.importlib.metadata, "distribution", lambda _name: distribution
    )
    monkeypatch.setattr(
        packages.importlib, "import_module", lambda _name: module
    )
    monkeypatch.setattr(sys, "prefix", str(root))
    monkeypatch.setattr(
        sys, "base_prefix", str(root if defect == "no-venv" else tmp_path)
    )
    if defect is None:
        assert packages.consume(NAME, VERSION, packages.QUALITY) == {
            "name": NAME,
            "version": VERSION,
            "origin": str(origin),
        }
    else:
        with pytest.raises(
            ValueError,
            match=r"origin|metadata",
        ):
            packages.consume(NAME, VERSION, packages.QUALITY)


def test_failed_cli_emits_no_partial_facts_or_private_diagnostics(
    monkeypatch, capsys
):
    """Failed cli emits no partial facts or private diagnostics."""
    monkeypatch.setattr(sys, "argv", ["packages", "identity", "/project"])
    monkeypatch.setattr(
        packages,
        "native_identity",
        Mock(side_effect=RuntimeError("private-token")),
    )
    assert packages.main() == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err == "Python package check failed (RuntimeError).\n"


def test_cli_transfers_complete_facts(monkeypatch, capsys):
    """Cli transfers complete facts."""
    monkeypatch.setattr(sys, "argv", ["packages", "identity", "/project"])
    monkeypatch.setattr(
        packages,
        "native_identity",
        lambda _project: {"name": NAME, "version": VERSION},
    )
    assert packages.main() == 0
    assert json.loads(capsys.readouterr().out) == {
        "name": NAME,
        "version": VERSION,
    }
