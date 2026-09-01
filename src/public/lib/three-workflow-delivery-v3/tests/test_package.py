"""Built-distribution contracts for Workflow Delivery v3."""

from __future__ import annotations

import os
import shutil
import subprocess
import tarfile
import venv
import zipfile
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[5]
RELEASE_MEMBER = "three_workflow_delivery_v3/release/static_reference_policy.py"
UV_BINARY = shutil.which("uv")
if UV_BINARY is None:
    pytest.skip(
        "uv is required to validate the Python distribution",
        allow_module_level=True,
    )


def _venv_executable(environment: Path, name: str) -> Path:
    scripts = environment / ("Scripts" if os.name == "nt" else "bin")
    suffix = ".exe" if os.name == "nt" else ""
    return scripts / f"{name}{suffix}"


def test_built_distribution_contains_release_and_runs_cli(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Ship the release package and execute the installed console script."""
    output = tmp_path / "dist"
    build = subprocess.run(  # noqa: S603
        [
            UV_BINARY,
            "build",
            "--package",
            "three-workflow-delivery-v3",
            "--out-dir",
            str(output),
        ],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    assert build.returncode == 0, build.stderr

    wheels = tuple(output.glob("*.whl"))
    source_distributions = tuple(output.glob("*.tar.gz"))
    assert len(wheels) == 1
    assert len(source_distributions) == 1

    with zipfile.ZipFile(wheels[0]) as wheel:
        assert RELEASE_MEMBER in wheel.namelist()
    with tarfile.open(source_distributions[0], "r:gz") as source_distribution:
        assert any(
            name.endswith(f"/{RELEASE_MEMBER}")
            for name in source_distribution.getnames()
        )

    environment = tmp_path / "venv"
    venv.EnvBuilder(
        with_pip=False,
        system_site_packages=False,
    ).create(environment)
    python = _venv_executable(environment, "python")
    monkeypatch.setenv(
        "PYTHONPATH",
        str(REPO_ROOT / "src/public/lib/three-workflow-delivery-v3/src"),
    )
    monkeypatch.setenv("PYTHONHOME", str(tmp_path / "shadow-python-home"))
    child_environment = os.environ.copy()
    for name in ("PYTHONHOME", "PYTHONPATH", "UV_OFFLINE"):
        child_environment.pop(name, None)
    child_environment["UV_CACHE_DIR"] = str(tmp_path / "uv-cache")
    install = subprocess.run(  # noqa: S603
        [
            UV_BINARY,
            "pip",
            "install",
            "--python",
            str(python),
            str(wheels[0]),
        ],
        env=child_environment,
        check=False,
        capture_output=True,
        text=True,
    )
    assert install.returncode == 0, install.stderr

    origin = subprocess.run(  # noqa: S603
        [
            str(python),
            "-I",
            "-c",
            (
                "from importlib.metadata import distribution;"
                "from pathlib import Path;"
                "import three_workflow_delivery_v3.cli as cli;"
                "actual=Path(cli.__file__).resolve();"
                "expected=Path(distribution("
                "'three-workflow-delivery-v3').locate_file("
                "'three_workflow_delivery_v3/cli.py')).resolve();"
                "assert actual == expected, (actual, expected);"
                "print(actual)"
            ),
        ],
        cwd=tmp_path,
        env=child_environment,
        check=False,
        capture_output=True,
        text=True,
    )
    assert origin.returncode == 0, origin.stderr
    assert Path(origin.stdout.strip()).is_relative_to(environment.resolve())

    invocation = subprocess.run(  # noqa: S603
        [
            str(_venv_executable(environment, "three-workflow-delivery-v3")),
            "--help",
        ],
        cwd=tmp_path,
        env=child_environment,
        check=False,
        capture_output=True,
        text=True,
    )
    assert invocation.returncode == 0, invocation.stderr
    assert invocation.stdout.startswith("usage: three-workflow-delivery-v3")
