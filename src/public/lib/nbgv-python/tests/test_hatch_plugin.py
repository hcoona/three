"""Tests for the hatchling plugin integration."""

from __future__ import annotations

from typing import TYPE_CHECKING

from nbgv_python.hatch_plugin import NbgvVersionSource
from nbgv_python.models import GitVersion

if TYPE_CHECKING:
    from pathlib import Path

    import pytest


class DummyRunner:
    """Mock runner that returns a fixed version."""

    def __init__(self, *_, **__):
        """Initialize the dummy runner."""

    def get_version(self, _cwd: Path) -> GitVersion:
        """Return a fixed version payload."""
        payload = {
            "SimpleVersion": "1.2.3-beta.1",
            "SemVer2": "1.2.3-beta.1+gabcdef",
        }
        return GitVersion.from_payload(payload)


def test_hatch_plugin_writes_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Plugin should normalise the version and write templated files."""
    monkeypatch.setattr("nbgv_python.hatch_plugin.NbgvRunner", DummyRunner)
    config = {
        "version-field": "SimpleVersion",
        "write": {
            "file": "build/_version.py",
            "template": "__version__ = '{version}'",
        },
        "template-fields": {
            "version-tuple": {"mode": "pep440"},
        },
    }
    plugin = NbgvVersionSource(
        str(tmp_path), {"source": "nbgv", "nbgv": config}
    )

    data = plugin.get_version_data()

    written = (tmp_path / "build" / "_version.py").read_text(encoding="utf-8")
    assert written == "__version__ = '1.2.3-beta.1'\n"
    assert data["version"] == "1.2.3b1"
    assert "template_fields" in data
    fields = plugin.get_template_fields()
    assert fields is not None
    assert fields["version_tuple"].startswith("(1, 2, 3")


def test_hatch_plugin_applies_epoch(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Configured epoch should prefix the normalized version."""
    monkeypatch.setattr("nbgv_python.hatch_plugin.NbgvRunner", DummyRunner)
    config = {
        "version-field": "SimpleVersion",
        "epoch": 2,
        "template-fields": {
            "version-tuple": {"mode": "pep440", "epoch": True},
        },
    }
    plugin = NbgvVersionSource(
        str(tmp_path), {"source": "nbgv", "nbgv": config}
    )

    data = plugin.get_version_data()

    assert data["version"] == "2!1.2.3b1"
    fields = plugin.get_template_fields()
    assert fields is not None
    assert fields["version_tuple"].startswith("(2, 1, 2, 3")
