"""Passive adapter boundaries with controlled native property/config results."""

from __future__ import annotations

import importlib.util
import json
import sys
import tomllib
import warnings
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, PropertyMock

import pytest

ROOT = Path(__file__).resolve().parents[6]
SPEC = importlib.util.spec_from_file_location(
    "workflow_delivery_python_passive",
    ROOT / "src/private/app/workflow-delivery/Native/Python/passive.py",
)
assert SPEC is not None
assert SPEC.loader is not None
passive = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(passive)

MANIFEST = """[project]
name = "example"
version = "1.0"
[build-system]
requires = ["hatchling", "nbgv-python"]
build-backend = "hatchling.build"
"""


@pytest.fixture
def project(tmp_path):
    """Prepare a physical project; native extraction remains substituted."""
    directory = tmp_path / "project with spaces"
    directory.mkdir()
    (directory / "pyproject.toml").write_text(MANIFEST)
    return directory


@pytest.fixture
def builder(monkeypatch):
    """Expose only the native property, so backend-hook calls fail the test."""
    factory = Mock(return_value=SimpleNamespace(build_system_requires=set()))
    monkeypatch.setitem(
        sys.modules, "build", SimpleNamespace(ProjectBuilder=factory)
    )
    return factory


def test_declared_requirements_preserve_native_strings(project, builder):
    """The adapter must not normalize, parse or round-trip requirements."""
    requirements = {
        "nbgv_Python[Mixed.Extra]>=2 ; python_version >= '3.14'",
        "Hatchling >= 1.25",
    }
    builder.return_value.build_system_requires = requirements
    (project / "pyproject.toml").write_text(
        MANIFEST
        + '[tool.uv.workspace]\nmembers = ["packages/*"]\n'
        + "[tool.uv.sources]\nnbgv-python = { workspace = true }\n"
    )
    before = (project / "pyproject.toml").read_bytes()

    result = passive.inspect_project(project)

    assert result == {
        "directory": str(project.resolve()),
        "build_requirements": sorted(requirements),
        "nbgv": None,
    }
    builder.assert_called_once_with(project.resolve())
    assert (project / "pyproject.toml").read_bytes() == before


@pytest.mark.parametrize("manifest", ["", '[project]\nname = "root"\n'])
def test_tableless_context_does_not_inject_a_backend(
    project, builder, manifest
):
    """Root and nonpackage quality contexts do not synthesize default builds."""
    (project / "pyproject.toml").write_text(manifest)

    result = passive.inspect_project(project)

    assert result["build_requirements"] is None
    assert result["nbgv"] is None
    builder.assert_not_called()


@pytest.mark.parametrize(
    "manifest",
    [
        MANIFEST.replace('name = "example"\n', ""),
        MANIFEST.replace('name = "example"', "name = 42"),
        MANIFEST.replace('name = "example"', 'name = "  "'),
        'project = false\n[build-system]\nbuild-backend = "hatchling.build"\n',
        'build-system = false\n[project]\nname = "example"\n',
        MANIFEST.replace('build-backend = "hatchling.build"\n', ""),
        MANIFEST.replace("hatchling.build", "setuptools.build_meta"),
        MANIFEST + 'backend-path = ["."]\n',
        MANIFEST + "[tool.uv]\npackage = false\n",
        MANIFEST + '[tool.uv]\nindex-url = "https://example.invalid"\n',
    ],
)
def test_unsupported_context_fails_before_native_extraction(
    project, builder, manifest
):
    """Request guards act before the fallback-capable native boundary."""
    (project / "pyproject.toml").write_text(manifest)

    with pytest.raises(
        passive.UnsupportedContextError, match=r"supported|native adaptation"
    ):
        passive.inspect_project(project)

    builder.assert_not_called()


def test_other_version_source_does_not_invoke_nbgv(project, builder):
    """Only native NBGV configuration needs plugin coordinate extraction."""
    (project / "pyproject.toml").write_text(
        MANIFEST + '[tool.hatch.version]\nsource = "code"\n'
    )

    result = passive.inspect_project(project)

    assert result["nbgv"] is None
    assert result["build_requirements"] == []
    builder.assert_called_once_with(project.resolve())


@pytest.mark.parametrize("writes_file", [False, True])
def test_native_plugin_coordinates_are_retained_without_generation(
    project, builder, monkeypatch, writes_file
):
    """Transport native coordinates, including absent generated outputs."""
    manifest = (
        MANIFEST
        + """[tool.hatch.version]
source = "nbgv"
[tool.hatch.version.nbgv]
working-directory = "../shared"
[tool.hatch.version.nbgv.write]
file = "generated/version.py"
template = "__version__ = '{version}'"
"""
    )
    (project / "pyproject.toml").write_text(manifest)
    generated = project / "generated/version.py"
    working = project / "../shared"
    config = SimpleNamespace(
        working_directory=working,
        write=SimpleNamespace(file=generated) if writes_file else None,
    )
    factory = Mock(return_value=config)
    monkeypatch.setitem(sys.modules, "nbgv_python", SimpleNamespace())
    monkeypatch.setitem(
        sys.modules,
        "nbgv_python.config",
        SimpleNamespace(PluginConfig=SimpleNamespace(from_mapping=factory)),
    )

    result = passive.inspect_project(project)

    assert result["nbgv"] == {
        "working_directory": str(working),
        "generated_file": str(generated) if writes_file else None,
    }
    assert result["build_requirements"] == []
    builder.assert_called_once_with(project.resolve())
    factory.assert_called_once_with(
        project.resolve(),
        {
            "working-directory": "../shared",
            "write": {
                "file": "generated/version.py",
                "template": "__version__ = '{version}'",
            },
        },
    )
    assert not generated.exists()
    assert (project / "pyproject.toml").read_text() == manifest


@pytest.mark.parametrize("failure_phase", ["constructor", "property", "plugin"])
def test_native_failure_is_terminal_and_cli_is_sanitized(
    project, builder, monkeypatch, capsys, failure_phase
):
    """A failed native read yields no partial facts or raw diagnostics."""
    failure = RuntimeError("private-source-token")
    if failure_phase == "constructor":
        builder.side_effect = failure
    elif failure_phase == "property":
        instance = Mock(spec=["build_system_requires"])
        monkeypatch.setattr(
            type(instance),
            "build_system_requires",
            PropertyMock(side_effect=failure),
            raising=False,
        )
        builder.return_value = instance
    else:
        (project / "pyproject.toml").write_text(
            MANIFEST + '[tool.hatch.version]\nsource = "nbgv"\n'
        )
        monkeypatch.setitem(sys.modules, "nbgv_python", SimpleNamespace())
        monkeypatch.setitem(
            sys.modules,
            "nbgv_python.config",
            SimpleNamespace(
                PluginConfig=SimpleNamespace(
                    from_mapping=Mock(side_effect=failure)
                )
            ),
        )
    monkeypatch.setattr(sys, "argv", ["passive", str(project)])

    assert passive.main() == 1

    result = capsys.readouterr()
    assert result.out == ""
    assert result.err == "Passive extraction failed (RuntimeError).\n"
    assert "private-source-token" not in result.err
    builder.assert_called_once_with(project.resolve())


@pytest.mark.parametrize(
    ("manifest", "exception_class"),
    [
        ("[invalid", tomllib.TOMLDecodeError),
        ("tool = false\n", passive.UnsupportedContextError),
        ("[tool]\nuv = false\n", passive.UnsupportedContextError),
    ],
)
def test_malformed_structure_cannot_reach_native_extraction(
    project, builder, manifest, exception_class
):
    """Syntax and physical table failures remain terminal."""
    (project / "pyproject.toml").write_text(manifest)

    with pytest.raises(exception_class):
        passive.inspect_project(project)

    builder.assert_not_called()


def test_cli_emits_one_complete_response(project, builder, monkeypatch, capsys):
    """The command transfers only successfully extracted facts."""
    builder.return_value.build_system_requires = {"hatchling", "nbgv-python"}
    monkeypatch.setattr(sys, "argv", ["passive", str(project)])

    assert passive.main() == 0

    result = capsys.readouterr()
    assert json.loads(result.out) == {
        "directory": str(project.resolve()),
        "build_requirements": ["hatchling", "nbgv-python"],
        "nbgv": None,
    }
    assert result.err == ""


def test_cli_explains_unsupported_context(
    project, builder, monkeypatch, capsys
):
    """Application request guards identify the unsupported shape safely."""
    (project / "pyproject.toml").write_text(
        MANIFEST + '[tool.uv]\nindex-url = "private-source-token"\n'
    )
    monkeypatch.setattr(sys, "argv", ["passive", str(project)])

    assert passive.main() == 1

    result = capsys.readouterr()
    assert result.out == ""
    assert result.err == (
        "Unsupported Python context: "
        "The requested UV settings need native adaptation.\n"
    )
    builder.assert_not_called()


@pytest.mark.parametrize("fails", [False, True])
def test_native_warnings_preserve_cli_outcome_and_do_not_escape(
    project, builder, monkeypatch, capsys, fails
):
    """Contain warning payloads and preserve native outcomes."""
    native_result = builder.return_value

    def native_constructor(_directory):
        warnings.warn("private-source-warning", UserWarning, stacklevel=1)
        if fails:
            message = "private-native-error"
            raise RuntimeError(message)
        return native_result

    builder.side_effect = native_constructor
    monkeypatch.setattr(sys, "argv", ["passive", str(project)])

    with warnings.catch_warnings(record=True) as outside:
        warnings.simplefilter("always")
        assert passive.main() == int(fails)
        warnings.warn("outside-extraction", UserWarning, stacklevel=1)

    assert [str(warning.message) for warning in outside] == [
        "outside-extraction"
    ]
    result = capsys.readouterr()
    if fails:
        assert result.out == ""
        assert result.err == "Passive extraction failed (RuntimeError).\n"
    else:
        assert json.loads(result.out) == {
            "directory": str(project.resolve()),
            "build_requirements": [],
            "nbgv": None,
        }
        assert result.err == ""
    assert "private-source-warning" not in result.out + result.err
    assert "private-native-error" not in result.out + result.err
    builder.assert_called_once_with(project.resolve())
