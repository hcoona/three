"""Controlled public pytest configuration transfer, without native queries."""

from __future__ import annotations

import importlib.util
import json
import os
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

ROOT = Path(__file__).resolve().parents[6]
SPEC = importlib.util.spec_from_file_location(
    "workflow_delivery_pytest_configuration",
    ROOT
    / "src/private/app/workflow-delivery/Native/Python/pytest_configuration.py",
)
assert SPEC is not None
assert SPEC.loader is not None
query_module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(query_module)


@pytest.fixture
def endpoint(tmp_path, monkeypatch):
    """Keep physical query state in one owned directory."""
    monkeypatch.chdir(tmp_path)
    return tmp_path


@pytest.fixture
def native(endpoint, monkeypatch):
    """Supply public fields without invoking pytest configuration parsing."""
    for name in (
        "PYTEST_ADDOPTS",
        "PYTEST_PLUGINS",
        "PYTEST_DISABLE_PLUGIN_AUTOLOAD",
    ):
        monkeypatch.setenv(
            name, os.environ.get(name, "controlled-ambient-value")
        )
    config = SimpleNamespace(
        rootpath=endpoint,
        inipath=endpoint / "pyproject.toml",
        getini=Mock(
            side_effect=lambda key: {
                "testpaths": ["tests with spaces", "src/member/tests"],
                "addopts": ["--import-mode=importlib"],
                "pythonpath": [],
                "required_plugins": [],
            }[key],
        ),
    )

    def invoke(_arguments, *, plugins):
        assert len(plugins) == 1
        return plugins[0].pytest_cmdline_main(config)

    factory = Mock(side_effect=invoke)
    monkeypatch.setattr(pytest, "main", factory)
    return factory, config


def test_native_query_captures_public_configuration_without_collecting(
    endpoint, native
):
    """The early hook returns only native configuration fields."""
    factory, _config = native

    assert query_module.inspect_configuration(endpoint) == {
        "directory": str(endpoint),
        "root": str(endpoint),
        "configuration_file": str(endpoint / "pyproject.toml"),
        "testpaths": ["tests with spaces", "src/member/tests"],
        "addopts": ["--import-mode=importlib"],
        "pythonpath": [],
        "required_plugins": [],
    }
    arguments = factory.call_args.args[0]
    assert arguments == [
        "--noconftest",
        "--disable-plugin-autoload",
        "-p",
        "no:cacheprovider",
    ]
    hook = factory.call_args.kwargs["plugins"][0]
    assert hook.pytest_cmdline_main(_config) == pytest.ExitCode.OK


def test_native_query_clears_inherited_pytest_configuration(
    endpoint, native, monkeypatch
):
    """Ambient plugin/addopts values cannot enter the native request."""
    factory, config = native
    monkeypatch.setenv("PYTEST_ADDOPTS", "private-addopts")
    monkeypatch.setenv("PYTEST_PLUGINS", "private-plugin")
    monkeypatch.setenv("PYTEST_DISABLE_PLUGIN_AUTOLOAD", "0")

    def invoke(_arguments, *, plugins):
        assert "PYTEST_ADDOPTS" not in os.environ
        assert "PYTEST_PLUGINS" not in os.environ
        assert os.environ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] == "1"
        return plugins[0].pytest_cmdline_main(config)

    factory.side_effect = invoke

    assert query_module.inspect_configuration(endpoint)["root"] == str(endpoint)


@pytest.mark.parametrize(
    "fault", ["nonzero", "missing-hook", "nonzero-after-hook"]
)
def test_native_query_failure_is_terminal(endpoint, native, fault):
    """Capture does not override a native failure or replace a missing hook."""
    factory, config = native

    def invoke(_arguments, *, plugins):
        if fault == "nonzero-after-hook":
            plugins[0].pytest_cmdline_main(config)
        return (
            pytest.ExitCode.OK
            if fault == "missing-hook"
            else pytest.ExitCode.USAGE_ERROR
        )

    factory.side_effect = invoke

    with pytest.raises(ValueError, match="did not complete"):
        query_module.inspect_configuration(endpoint)


@pytest.mark.parametrize("fails", [False, True])
def test_cli_transfers_only_complete_native_configuration(
    endpoint, native, monkeypatch, capsys, fails
):
    """Native output and error payloads do not corrupt or escape JSON."""
    factory, config = native

    def invoke(_arguments, *, plugins):
        sys.stdout.write("private-native-output")
        sys.stderr.write("private-native-diagnostic")
        if fails:
            message = "private-native-error"
            raise RuntimeError(message)
        return plugins[0].pytest_cmdline_main(config)

    factory.side_effect = invoke
    monkeypatch.setattr(sys, "argv", ["query", str(endpoint)])

    assert query_module.main() == int(fails)

    result = capsys.readouterr()
    if fails:
        assert result.out == ""
        assert result.err == (
            "Pytest configuration extraction failed (RuntimeError).\n"
        )
    else:
        assert json.loads(result.out)["testpaths"] == [
            "tests with spaces",
            "src/member/tests",
        ]
        assert result.err == ""
    assert "private-native" not in result.out + result.err


def test_missing_configuration_remains_native_absence(endpoint, native):
    """The producer does not fabricate an input or apply fallback policy."""
    _factory, config = native
    config.inipath = None
    config.getini.side_effect = lambda _key: []

    result = query_module.inspect_configuration(endpoint)

    assert result["configuration_file"] is None
    assert result["testpaths"] == []
    assert result["addopts"] == []


def test_changed_endpoint_fails_before_native_query(native, tmp_path):
    """The requested endpoint cannot silently acquire another context."""
    factory, _config = native
    other = tmp_path / "other"
    other.mkdir()

    with pytest.raises(ValueError, match="requested endpoint"):
        query_module.inspect_configuration(other)

    factory.assert_not_called()
