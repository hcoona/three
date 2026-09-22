"""One mise runtime authority for local, CI and installed package consumers."""

import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "sync_python_version", ROOT / "eng/scripts/sync_python_version.py"
)
assert SPEC is not None
assert SPEC.loader is not None
runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtime)


def test_runtime_projection_checks_drift_without_rewriting(tmp_path):
    """A tool update requires explicit regeneration of both consumed files."""
    (tmp_path / "mise.toml").write_text('[tools]\npython = "3.14"\n')
    (tmp_path / "mise.lock").write_text(
        '[[tools.python]]\nversion = "3.14.3"\n'
    )
    assert runtime.synchronize(tmp_path, write=True) == "3.14.3"
    assert (tmp_path / ".python-version").read_text() == "3.14.3\n"
    package = tmp_path / runtime.PACKAGE_RUNTIME
    assert 'PYTHON_VERSION = "3.14.3"' in package.read_text()
    package.write_text('PYTHON_VERSION = "3.13.12"\n')
    with pytest.raises(ValueError, match="sync:python-version"):
        runtime.synchronize(tmp_path)
    assert package.read_text() == 'PYTHON_VERSION = "3.13.12"\n'
    runtime.synchronize(tmp_path, write=True)
    assert runtime.synchronize(tmp_path) == "3.14.3"
    (tmp_path / "mise.toml").write_text('[tools]\npython = "3.15"\n')
    with pytest.raises(ValueError, match="disagrees"):
        runtime.synchronize(tmp_path, write=True)
    assert (tmp_path / ".python-version").read_text() == "3.14.3\n"
