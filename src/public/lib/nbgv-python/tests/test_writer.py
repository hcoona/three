"""Tests for file writing."""

from __future__ import annotations

from typing import TYPE_CHECKING

import pytest
from nbgv_python.writer import WriteConfig, write_version_file

if TYPE_CHECKING:
    from pathlib import Path


def test_write_version_file(tmp_path: Path) -> None:
    """Writing should use the provided template."""
    target = tmp_path / "_version.py"
    config = WriteConfig(file=target, template="__version__ = '{version}'")
    write_version_file(config, {"version": "1.2.3"})
    assert target.read_text(encoding="utf-8") == "__version__ = '1.2.3'\n"


def test_write_version_file_default_template(tmp_path: Path) -> None:
    """Default template should be chosen based on suffix."""
    target = tmp_path / "VERSION.txt"
    config = WriteConfig(file=target)
    write_version_file(config, {"version": "1.2.3"})
    assert target.read_text(encoding="utf-8") == "1.2.3\n"


def test_write_version_file_default_template_python_prefers_normalized(
    tmp_path: Path,
) -> None:
    """Python files should default to the normalized version when available."""
    target = tmp_path / "_version.py"
    config = WriteConfig(file=target)
    fields = {"version": "1.2.3-beta.1", "normalized_version": "1.2.3b1"}
    write_version_file(config, fields)
    assert target.read_text(encoding="utf-8") == '__version__ = "1.2.3b1"\n'


def test_write_version_file_default_template_python_fallback(
    tmp_path: Path,
) -> None:
    """Python default template falls back to 'version'."""
    target = tmp_path / "_version.py"
    config = WriteConfig(file=target)
    write_version_file(config, {"version": "1.2.3"})
    assert target.read_text(encoding="utf-8") == '__version__ = "1.2.3"\n'


def test_write_version_file_unknown_suffix(tmp_path: Path) -> None:
    """Unknown suffix without template raises an error."""
    target = tmp_path / "version.json"
    config = WriteConfig(file=target)
    with pytest.raises(RuntimeError, match="No default template is defined"):
        write_version_file(config, {"version": "1.2.3"})
