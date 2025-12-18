"""Tests for configuration parsing."""

from __future__ import annotations

from typing import TYPE_CHECKING

import pytest
from nbgv_python.config import PluginConfig
from nbgv_python.templating import DEFAULT_NBGV_FIELDS

if TYPE_CHECKING:
    from pathlib import Path


def test_plugin_config_defaults(tmp_path: Path) -> None:
    """Ensure defaults use the project root and SemVer2 field."""
    config = PluginConfig.from_mapping(tmp_path, None)
    assert config.command is None
    assert config.version_field == "SemVer2"
    assert config.working_directory == tmp_path
    assert config.template_fields.version_tuple.mode == "nbgv"
    assert config.template_fields.version_tuple.fields == DEFAULT_NBGV_FIELDS
    assert config.template_fields.version_tuple.normalized_prerelease is False
    assert config.epoch is None
    assert config.write is None


def test_plugin_config_parses_command_and_directory(tmp_path: Path) -> None:
    """String commands should be normalised and directories resolved."""
    mapping = {
        "command": "python stub.py",
        "working-directory": "src/project",
        "version-field": "SimpleVersion",
    }
    config = PluginConfig.from_mapping(tmp_path, mapping)
    assert config.command == ("python", "stub.py")
    assert config.version_field == "SimpleVersion"
    assert config.working_directory == tmp_path / "src/project"
    assert config.template_fields.version_tuple.mode == "nbgv"
    assert config.epoch is None
    assert config.write is None


def test_plugin_config_rejects_unknown_keys(tmp_path: Path) -> None:
    """Reject unsupported configuration options."""
    with pytest.raises(ValueError, match="Unsupported configuration keys"):
        PluginConfig.from_mapping(tmp_path, {"unexpected": 1})


def test_plugin_config_parses_template_and_write(tmp_path: Path) -> None:
    """Template and write configuration should be interpreted correctly."""
    target_file = tmp_path / "build" / "_version.py"
    mapping = {
        "template-fields": {
            "version-tuple": {
                "mode": "pep440",
                "epoch": True,
                "double-quote": False,
                "normalized-prerelease": True,
            }
        },
        "epoch": "2",
        "write": {
            "file": "build/_version.py",
            "template": "__version__ = '{version}'",
            "encoding": "utf-8",
        },
    }
    config = PluginConfig.from_mapping(tmp_path, mapping)
    assert config.template_fields.version_tuple.mode == "pep440"
    assert config.template_fields.version_tuple.epoch is True
    assert config.template_fields.version_tuple.double_quote is False
    assert config.template_fields.version_tuple.normalized_prerelease is True
    assert config.epoch == 2  # noqa: PLR2004
    assert config.write is not None
    assert config.write.file == target_file
    assert config.write.template == "__version__ = '{version}'"
    assert config.write.encoding == "utf-8"


def test_plugin_config_invalid_epoch(tmp_path: Path) -> None:
    """Epoch must be a non-negative integer."""
    with pytest.raises(ValueError, match="'epoch' must be an integer >= 0"):
        PluginConfig.from_mapping(tmp_path, {"epoch": -1})
