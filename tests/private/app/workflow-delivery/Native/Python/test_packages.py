"""Controlled package boundary tests; no adopted backend build is executed."""

from __future__ import annotations

import importlib.util
import io
import json
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
