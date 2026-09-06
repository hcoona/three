"""Built-distribution contracts for Workflow Delivery v3."""

from __future__ import annotations

import os
import shutil
import subprocess
import tarfile
import venv
import zipfile
from pathlib import Path, PurePosixPath

import pytest

REPO_ROOT = Path(__file__).resolve().parents[5]
PACKAGE_RELATIVE_PATH = Path("src/public/lib/three-workflow-delivery-v3")
RELEASE_SOURCE_PATH = Path("src/three_workflow_delivery_v3/release")
RELEASE_ARCHIVE_PATH = PurePosixPath("three_workflow_delivery_v3/release")
SDIST_SOURCE_PREFIX = PurePosixPath("src")
APPROVED_RELEASE_MODULES = (
    "__init__.py",
    "eligibility.py",
    "exact_satisfied.py",
    "finalizer.py",
    "governance_git.py",
    "identity.py",
    "live.py",
    "observation.py",
    "planner.py",
    "publication.py",
    "qualification.py",
    "simulation.py",
    "static_reference_authority.py",
    "static_reference_model.py",
    "static_reference_policy.py",
    "static_reference_projection.py",
    "static_reference_session.py",
    "static_reference_source.py",
    "workflow.py",
)
APPROVED_RELEASE_MEMBERS = frozenset(
    (RELEASE_ARCHIVE_PATH / module).as_posix()
    for module in APPROVED_RELEASE_MODULES
)
DELETED_PACKAGE_MEMBER = "three_workflow_delivery_v3/authorization_formatter.py"
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


def _process_output(process: subprocess.CompletedProcess[str]) -> str:
    return f"stdout:\n{process.stdout}\nstderr:\n{process.stderr}"


def _wheel_package_members(path: Path) -> frozenset[str]:
    with zipfile.ZipFile(path) as wheel:
        return frozenset(
            PurePosixPath(member.filename).as_posix()
            for member in wheel.infolist()
            if not member.is_dir()
        )


def _source_package_members(path: Path) -> frozenset[str]:
    members = set()
    with tarfile.open(path, "r:gz") as source_distribution:
        for member in source_distribution.getmembers():
            if not member.isfile():
                continue
            member_path = PurePosixPath(member.name)
            project_path = PurePosixPath(*member_path.parts[1:])
            if not project_path.is_relative_to(SDIST_SOURCE_PREFIX):
                continue
            members.add(
                project_path.relative_to(SDIST_SOURCE_PREFIX).as_posix()
            )
    return frozenset(members)


def test_built_distribution_contains_release_and_runs_cli(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Ship the release package and execute the installed console script."""
    isolated_repository = tmp_path / "repository"
    isolated_package = isolated_repository / PACKAGE_RELATIVE_PATH
    isolated_package.parent.mkdir(parents=True)
    shutil.copytree(REPO_ROOT / PACKAGE_RELATIVE_PATH, isolated_package)
    shutil.copy2(REPO_ROOT / ".gitignore", isolated_repository / ".gitignore")
    stale_module = (
        isolated_package / RELEASE_SOURCE_PATH / "stale_reference_policy.py"
    )
    stale_module.write_text(
        '"""Stale release policy that must not be distributed."""\n',
        encoding="utf-8",
    )

    output = tmp_path / "dist"
    build = subprocess.run(  # noqa: S603
        [
            UV_BINARY,
            "build",
            str(isolated_package),
            "--out-dir",
            str(output),
        ],
        cwd=isolated_repository,
        check=False,
        capture_output=True,
        text=True,
    )
    assert build.returncode == 0, _process_output(build)

    wheels = tuple(output.glob("*.whl"))
    source_distributions = tuple(output.glob("*.tar.gz"))
    assert len(wheels) == 1
    assert len(source_distributions) == 1

    wheel_members = _wheel_package_members(wheels[0])
    wheel_release_members = {
        member
        for member in wheel_members
        if PurePosixPath(member).is_relative_to(RELEASE_ARCHIVE_PATH)
    }
    assert wheel_release_members == APPROVED_RELEASE_MEMBERS
    assert DELETED_PACKAGE_MEMBER not in wheel_members

    source_package_members = _source_package_members(source_distributions[0])
    source_release_members = {
        member
        for member in source_package_members
        if PurePosixPath(member).is_relative_to(RELEASE_ARCHIVE_PATH)
    }
    assert source_release_members == APPROVED_RELEASE_MEMBERS
    assert DELETED_PACKAGE_MEMBER not in source_package_members

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
    assert install.returncode == 0, _process_output(install)

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
    assert origin.returncode == 0, _process_output(origin)
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
    assert invocation.returncode == 0, _process_output(invocation)
    assert invocation.stdout.startswith("usage: three-workflow-delivery-v3")
