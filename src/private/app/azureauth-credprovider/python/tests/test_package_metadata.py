"""Tests for the keyring wheel's repository-managed version metadata."""
# ruff: noqa: S101

from __future__ import annotations

import json
import shutil
import subprocess
import tomllib
import zipfile
from email.parser import Parser
from pathlib import Path

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
WORKSPACE_ROOT = Path(__file__).resolve().parents[6]
PYPROJECT_PATH = PACKAGE_ROOT / "pyproject.toml"
BUNDLE_SCRIPT_PATH = (
    WORKSPACE_ROOT
    / "eng"
    / "scripts"
    / "azureauth-credprovider"
    / "New-DeploymentValidationBundle.ps1"
)


def test_package_manifest_uses_repository_nbgv_hatch_pattern() -> None:
    """The package follows the monorepo's dynamic NBGV/Hatch convention."""
    manifest = tomllib.loads(PYPROJECT_PATH.read_text(encoding="utf-8"))

    assert manifest["build-system"]["build-backend"] == "hatchling.build"
    assert manifest["build-system"]["requires"] == [
        "hatchling>=1.24.2",
        "nbgv-python",
    ]
    assert manifest["project"]["dynamic"] == ["version"]
    assert "version" not in manifest["project"]
    assert manifest["tool"]["hatch"]["version"] == {
        "source": "nbgv",
        "nbgv": {"version-field": "SemVer2"},
    }
    assert manifest["tool"]["uv"]["sources"]["nbgv-python"] == {
        "workspace": True,
    }


def test_bundle_build_selects_versioned_workspace_package() -> None:
    """The bundle uses the same workspace build path as package validation."""
    script = BUNDLE_SCRIPT_PATH.read_text(encoding="utf-8")

    assert "Push-Location $repoRoot" in script
    assert "--package azureauth-credprovider-keyring" in script
    assert "uv build" in script


def test_built_wheel_uses_nbgv_version_metadata_not_fallback(
    tmp_path: Path,
) -> None:
    """The actual wheel carries one valid NBGV version in name and metadata."""
    uv_executable = shutil.which("uv")
    dotnet_executable = shutil.which("dotnet")
    assert uv_executable is not None
    assert dotnet_executable is not None
    version_result = subprocess.run(  # noqa: S603
        [
            dotnet_executable,
            "tool",
            "run",
            "nbgv",
            "get-version",
            "--format",
            "json",
        ],
        cwd=PACKAGE_ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert version_result.returncode == 0, version_result.stderr
    version_data = json.loads(version_result.stdout)
    source_revision = version_data["GitCommitIdShort"]

    completed = subprocess.run(  # noqa: S603
        [
            uv_executable,
            "build",
            "--package",
            "azureauth-credprovider-keyring",
            "--wheel",
            "--out-dir",
            str(tmp_path),
        ],
        cwd=WORKSPACE_ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )

    assert completed.returncode == 0, completed.stderr
    wheel_paths = list(tmp_path.glob("*.whl"))
    assert len(wheel_paths) == 1
    wheel_path = wheel_paths[0]
    wheel_version = wheel_path.name.split("-")[1]

    with zipfile.ZipFile(wheel_path) as wheel:
        metadata_paths = [
            name
            for name in wheel.namelist()
            if name.endswith(".dist-info/METADATA")
        ]
        assert len(metadata_paths) == 1
        metadata_text = wheel.read(metadata_paths[0]).decode("utf-8")

    metadata = Parser().parsestr(metadata_text)
    metadata_version = metadata["Version"]
    assert metadata["Name"] == "azureauth-credprovider-keyring"
    assert metadata_version == wheel_version
    assert metadata_version.startswith(version_data["SimpleVersion"])
    if not version_data["PublicRelease"]:
        assert f"g{source_revision}" in metadata_version
    assert metadata_version != "0.0.0.dev0"
    assert completed.stdout == ""
    assert "Successfully built " in completed.stderr
    assert wheel_path.name in completed.stderr
