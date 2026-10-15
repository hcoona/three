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


def test_remote_consumer_requires_native_product_absence(monkeypatch):
    """An empty native venv cannot be substituted by an installed product."""
    monkeypatch.setattr(packages.sys, "prefix", "/owned/venv")
    monkeypatch.setattr(packages.sys, "base_prefix", "/native/python")
    missing = Mock(side_effect=packages.importlib.metadata.PackageNotFoundError)
    monkeypatch.setattr(packages.importlib.metadata, "distribution", missing)
    assert packages.consumer_absent(NAME, packages.QUALITY) == {
        "name": NAME,
        "absent": True,
    }
    missing.assert_called_once_with(NAME)
    monkeypatch.setattr(
        packages.importlib.metadata, "distribution", lambda _: object()
    )
    with pytest.raises(ValueError, match="already installed"):
        packages.consumer_absent(NAME, packages.QUALITY)


@pytest.mark.parametrize("defect", ["base", "name", "quality"])
def test_remote_consumer_rejects_unsupported_native_environment(
    monkeypatch, defect
):
    """Only the adopted product in an actual venv can be a remote consumer."""
    monkeypatch.setattr(packages.sys, "prefix", "/owned/venv")
    monkeypatch.setattr(packages.sys, "base_prefix", "/native/python")
    name, quality = NAME, packages.QUALITY
    if defect == "base":
        monkeypatch.setattr(packages.sys, "base_prefix", "/owned/venv")
    elif defect == "name":
        name = "other"
    else:
        quality = "other"
    native = Mock()
    monkeypatch.setattr(packages.importlib.metadata, "distribution", native)
    with pytest.raises(ValueError, match="Unsupported remote consumer"):
        packages.consumer_absent(name, quality)
    native.assert_not_called()


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
        monkeypatch.setattr(packages, "MAX_CONTENTS", 1)
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
        assert packages.consume_smoke(NAME, VERSION, packages.QUALITY) == {
            "name": NAME,
            "version": VERSION,
            "origin": str(origin),
        }
    else:
        with pytest.raises(
            ValueError,
            match=r"origin|metadata",
        ):
            packages.consume_smoke(NAME, VERSION, packages.QUALITY)


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
