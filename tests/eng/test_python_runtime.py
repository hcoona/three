"""One mise runtime authority for local, CI and installed package consumers."""

import importlib.util
import os
import shlex
import sys
import tomllib
from pathlib import Path
from types import SimpleNamespace

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


@pytest.fixture
def test_runner(monkeypatch):
    """Load the actual bounded runner with its existing selector import."""
    monkeypatch.syspath_prepend(str(ROOT / "eng/scripts"))
    spec = importlib.util.spec_from_file_location(
        "run_python_tests", ROOT / "eng/scripts/run_python_tests.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize(
    ("sync_status", "test_status"), [(0, 0), (17, None), (0, 23)]
)
def test_full_python_runner_preserves_outer_uv_and_propagates_status(
    test_runner, monkeypatch, tmp_path, sync_status, test_status
):
    """Real selection drives exact sync, full tests and bounded failures."""
    outer = tmp_path / "outer"
    shadow = tmp_path / "shadow"
    name = "uv.exe" if os.name == "nt" else "uv"
    for directory in (outer, shadow):
        directory.mkdir()
        executable = directory / name
        executable.touch()
        executable.chmod(0o755)
    original_path = os.environ["PATH"]
    monkeypatch.setenv("PATH", os.pathsep.join((str(outer), original_path)))
    arguments = ["-q", "--junitxml=artifacts/python result.xml"]
    monkeypatch.setattr(sys, "argv", ["run_python_tests.py", *arguments])
    calls = []

    def command(argv, *, cwd, check):
        calls.append((argv, cwd, check))
        if argv[1] == "sync":
            # Model the external command changing PATH between stages.
            monkeypatch.setenv(
                "PATH", os.pathsep.join((str(shadow), original_path))
            )
            return SimpleNamespace(returncode=sync_status)
        return SimpleNamespace(returncode=test_status)

    # Substitute only process execution; real selection and which() still run.
    monkeypatch.setattr(test_runner, "subprocess", SimpleNamespace(run=command))
    assert test_runner.main() == (sync_status or test_status)
    packages = [
        "hcoona-three-monorepo",
        "azureauth-credprovider-keyring",
        "git-commit-heatmap",
        "llm-text-splitter",
        "nbgv-python",
        "three-workflow-delivery-v3",
    ]
    uv = str(outer / name)
    assert calls[0] == (
        [
            uv,
            "sync",
            "--frozen",
            *[part for package in packages for part in ("--package", package)],
        ],
        ROOT,
        False,
    )
    if sync_status:
        assert len(calls) == 1
    else:
        config = tomllib.loads((ROOT / "pyproject.toml").read_text())
        roots = config["tool"]["pytest"]["ini_options"]["testpaths"]
        assert calls[1:] == [
            (
                [
                    uv,
                    "run",
                    "--no-sync",
                    "python",
                    "-m",
                    "pytest",
                    *roots,
                    *arguments,
                ],
                ROOT,
                False,
            )
        ]


def test_full_python_runner_missing_uv_fails_before_any_process(
    test_runner, monkeypatch
):
    """No missing-tool fallback may silently bypass exact preparation."""
    calls = []
    monkeypatch.setattr(
        test_runner, "shutil", SimpleNamespace(which=lambda _name: None)
    )
    monkeypatch.setattr(
        test_runner,
        "subprocess",
        SimpleNamespace(
            run=lambda *args, **kwargs: calls.append((args, kwargs))
        ),
    )
    with pytest.raises(FileNotFoundError, match="UV executable is unavailable"):
        test_runner.main()
    assert calls == []


def test_python_tasks_preserve_exact_preparation_and_full_runner():
    """Documented tasks cannot retain unrelated workspace executables."""
    tasks = tomllib.loads((ROOT / "mise.toml").read_text())["tasks"]
    assert shlex.split(tasks["test:python"]["run"]) == [
        "python",
        "eng/scripts/run_python_tests.py",
    ]
    assert shlex.split(tasks["test:v3"]["run"]) == [
        "uv",
        "run",
        "--exact",
        "--frozen",
        "--package",
        "three-workflow-delivery-v3",
        "pytest",
        "src/public/lib/three-workflow-delivery-v3/tests",
    ]
    for task in ("test:python", "test:v3"):
        assert tasks[task]["depends"] == [
            "prepare:static-reference-authorities"
        ]
