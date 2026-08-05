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
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from email.message import Message

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
PROJECT_ROOT = PACKAGE_ROOT.parent
WORKSPACE_ROOT = Path(__file__).resolve().parents[6]
PYPROJECT_PATH = PACKAGE_ROOT / "pyproject.toml"
VERSION_POLICY_PATH = PROJECT_ROOT / "version.json"
BUNDLE_SCRIPT_RELATIVE_PATH = Path(
    "eng/scripts/azureauth-credprovider/New-DeploymentValidationBundle.ps1",
)
BUNDLE_SCRIPT_PATH = WORKSPACE_ROOT / BUNDLE_SCRIPT_RELATIVE_PATH
CSHARP_PROJECT_RELATIVE_PATHS = (
    Path(
        "Hcoona.AzureAuth.CredProvider.Cli/"
        "Hcoona.AzureAuth.CredProvider.Cli.csproj",
    ),
    Path(
        "Hcoona.AzureAuth.CredProvider.Contracts/"
        "Hcoona.AzureAuth.CredProvider.Contracts.csproj",
    ),
    Path(
        "Hcoona.AzureAuth.CredProvider.Platform/"
        "Hcoona.AzureAuth.CredProvider.Platform.csproj",
    ),
)
NBGV_COMMIT_ID_LENGTH = 7


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
    version_data = _get_version(PACKAGE_ROOT)
    source_revision = version_data["GitCommitIdShort"]
    wheel_path = _build_wheel(WORKSPACE_ROOT, tmp_path)
    metadata = _read_wheel_metadata(wheel_path)
    metadata_version = metadata["Version"]

    assert metadata["Name"] == "azureauth-credprovider-keyring"
    assert metadata_version == wheel_path.name.split("-")[1]
    assert metadata_version.startswith(version_data["SimpleVersion"])
    if not version_data["PublicRelease"]:
        assert f"g{source_revision}" in metadata_version
    assert metadata_version != "0.0.0.dev0"


def test_project_version_policy_inherits_repository_defaults_and_filters(
    tmp_path: Path,
) -> None:
    """The local recipe keeps root policy while defining AzureAuth inputs."""
    local_policy = json.loads(VERSION_POLICY_PATH.read_text(encoding="utf-8"))

    assert local_policy == {
        "$schema": (
            "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/"
            "main/src/NerdBank.GitVersioning/version.schema.json"
        ),
        "version": "1.0.0-beta.{height}",
        "pathFilters": [
            ".",
            ":^python/tests",
            ":/eng/scripts/azureauth-credprovider",
        ],
        "inherit": True,
    }

    _, project_root = _create_version_repository(tmp_path)
    version_data = _get_version(project_root)
    version_options = version_data["VersionOptions"]

    assert version_data["VersionFileFound"] is True
    assert (
        version_options["GitCommitIdShortFixedLength"] == NBGV_COMMIT_ID_LENGTH
    )
    assert (
        version_options["GitCommitIdShortAutoMinimum"] == NBGV_COMMIT_ID_LENGTH
    )
    assert version_options["NuGetPackageVersion"] == {"SemVer": 2.0}
    assert version_options["PublicReleaseRefSpec"] == [
        "^refs/heads/main$",
        "^refs/heads/release/.*$",
        "^refs/tags/release/.+/v.+$",
    ]
    assert [
        (entry["RepoRelativePath"], entry["IsInclude"], entry["IsExclude"])
        for entry in version_options["PathFilters"]
    ] == [
        ("src/private/app/azureauth-credprovider", True, False),
        (
            "src/private/app/azureauth-credprovider/python/tests",
            False,
            True,
        ),
        ("eng/scripts/azureauth-credprovider", True, False),
    ]


@pytest.mark.parametrize(
    "project_relative_path",
    CSHARP_PROJECT_RELATIVE_PATHS,
    ids=["cli", "contracts", "platform"],
)
def test_all_azureauth_csharp_projects_select_project_version_root(
    project_relative_path: Path,
) -> None:
    """Every AzureAuth C# project resolves the product-level version file."""
    dotnet = _required_executable("dotnet")
    completed = subprocess.run(  # noqa: S603
        [
            dotnet,
            "msbuild",
            str(PROJECT_ROOT / project_relative_path),
            "-getProperty:GitVersionBaseDirectory",
        ],
        cwd=WORKSPACE_ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )

    assert completed.returncode == 0, completed.stderr
    assert Path(completed.stdout.strip()).resolve() == PROJECT_ROOT.resolve()


@pytest.mark.parametrize(
    "changed_relative_path",
    [
        (
            "src/private/app/azureauth-credprovider/"
            "Hcoona.AzureAuth.CredProvider.Cli/Program.cs"
        ),
        "src/private/app/azureauth-credprovider/docs/versioning.md",
        "src/private/app/azureauth-credprovider/python/pyproject.toml",
        (
            "src/private/app/azureauth-credprovider/python/src/"
            "azureauth_credprovider_keyring/backend.py"
        ),
        (
            "eng/scripts/azureauth-credprovider/"
            "New-DeploymentValidationBundle.ps1"
        ),
        "eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",
        (
            "eng/scripts/azureauth-credprovider/"
            "Install-DeploymentValidationBundle.ps1"
        ),
        (
            "eng/scripts/azureauth-credprovider/"
            "Uninstall-DeploymentValidationBundle.ps1"
        ),
    ],
    ids=[
        "csharp-source",
        "docs",
        "python-metadata",
        "python-source",
        "bundle-builder",
        "foundation-builder",
        "bundle-installer",
        "bundle-uninstaller",
    ],
)
def test_azureauth_product_changes_advance_public_wheel_identity(
    tmp_path: Path,
    changed_relative_path: str,
) -> None:
    """Approved product inputs advance height and public wheel identity."""
    repository, project_root = _create_version_repository(tmp_path)
    baseline = _get_version(project_root)

    _commit_path(repository, changed_relative_path)
    changed = _get_version(project_root)

    assert baseline["VersionHeight"] == 1
    assert changed["VersionHeight"] == baseline["VersionHeight"] + 1
    assert _wheel_version(baseline["SemVer2"]) == "1.0.0b1"
    assert _wheel_version(changed["SemVer2"]) == "1.0.0b2"


@pytest.mark.parametrize(
    "changed_relative_path",
    [
        "src/private/app/other-product/source.cs",
        (
            "tests/private/app/azureauth-credprovider/"
            "Hcoona.AzureAuth.CredProvider.Cli.Tests/VersionTests.cs"
        ),
        (
            "src/private/app/azureauth-credprovider/python/tests/"
            "test_version_only.py"
        ),
        ".github/workflows/azureauth-ci.yml",
    ],
    ids=[
        "unrelated-product",
        "csharp-tests",
        "python-tests",
        "ci",
    ],
)
def test_non_product_changes_preserve_public_wheel_identity(
    tmp_path: Path,
    changed_relative_path: str,
) -> None:
    """Unrelated, test-only, and CI-only changes do not advance the product."""
    repository, project_root = _create_version_repository(tmp_path)
    baseline = _get_version(project_root)

    _commit_path(repository, changed_relative_path)
    changed = _get_version(project_root)

    assert baseline["VersionHeight"] == 1
    assert changed["VersionHeight"] == 1
    assert _wheel_version(baseline["SemVer2"]) == "1.0.0b1"
    assert _wheel_version(changed["SemVer2"]) == "1.0.0b1"


def test_csharp_projects_and_python_wheel_share_version(
    tmp_path: Path,
) -> None:
    """C# consumers and the built wheel resolve one NBGV product identity."""
    csharp_versions = [
        _get_version((PROJECT_ROOT / path).parent)
        for path in CSHARP_PROJECT_RELATIVE_PATHS
    ]
    python_version = _get_version(PACKAGE_ROOT)
    all_versions = [*csharp_versions, python_version]

    assert len({version["VersionHeight"] for version in all_versions}) == 1
    assert len({version["GitCommitId"] for version in all_versions}) == 1
    assert len({version["SemVer2"] for version in all_versions}) == 1
    assert all(
        version["NuGetPackageVersion"] == python_version["SemVer2"]
        for version in csharp_versions
    )

    wheel_path = _build_wheel(WORKSPACE_ROOT, tmp_path)
    metadata = _read_wheel_metadata(wheel_path)
    expected_wheel_version = _wheel_version(python_version["SemVer2"])

    assert metadata["Name"] == "azureauth-credprovider-keyring"
    assert metadata["Version"] == expected_wheel_version
    assert wheel_path.name.split("-")[1] == expected_wheel_version


def test_bundle_builds_versioned_wheel_from_clean_checkout(
    tmp_path: Path,
) -> None:
    """A clean full-history checkout produces one correctly versioned wheel."""
    repository = tmp_path / "repository"
    git = _required_executable("git")
    completed = subprocess.run(  # noqa: S603
        [
            git,
            "clone",
            "--quiet",
            "--shared",
            str(WORKSPACE_ROOT),
            str(repository),
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert completed.returncode == 0, completed.stderr

    _run_git(repository, "switch", "--quiet", "--force-create", "main")
    assert _run_git(repository, "status", "--porcelain") == ""
    assert _run_git(
        repository,
        "ls-files",
        "--error-unmatch",
        str(VERSION_POLICY_PATH.relative_to(WORKSPACE_ROOT)),
    ) == str(VERSION_POLICY_PATH.relative_to(WORKSPACE_ROOT))

    project_root = repository / "src/private/app/azureauth-credprovider"
    version_data = _get_version(project_root / "python", repository)
    product_version = version_data["SemVer2"]
    source_revision = _run_git(repository, "rev-parse", "HEAD")
    output_root = tmp_path / "deployment-validation"
    pwsh = _required_executable("pwsh")
    completed = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(repository / BUNDLE_SCRIPT_RELATIVE_PATH),
            "-BuildOs",
            "Linux",
            "-TargetRid",
            "linux-x64",
            "-Configuration",
            "Release",
            "-ProductVersion",
            product_version,
            "-SourceRevision",
            source_revision,
            "-OutputRoot",
            str(output_root),
        ],
        cwd=repository,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert completed.returncode == 0, (
        f"stdout:\n{completed.stdout}\nstderr:\n{completed.stderr}"
    )

    bundle_paths = list(output_root.glob("*.zip"))
    assert len(bundle_paths) == 1
    expected_wheel_version = _wheel_version(product_version)
    expected_wheel_name = (
        "azureauth_credprovider_keyring-"
        f"{expected_wheel_version}-py3-none-any.whl"
    )

    with zipfile.ZipFile(bundle_paths[0]) as bundle:
        wheel_entries = [
            name for name in bundle.namelist() if name.endswith(".whl")
        ]
        assert wheel_entries == [f"python/{expected_wheel_name}"]
        wheel_bytes = bundle.read(wheel_entries[0])
        manifest = json.loads(bundle.read("manifest.json"))

    nested_wheel_path = tmp_path / expected_wheel_name
    nested_wheel_path.write_bytes(wheel_bytes)
    metadata = _read_wheel_metadata(nested_wheel_path)

    assert metadata["Name"] == "azureauth-credprovider-keyring"
    assert metadata["Version"] == expected_wheel_version
    assert manifest["productVersion"] == product_version
    assert manifest["sourceRevision"] == source_revision
    assert manifest["entrypoints"]["pythonWheel"] == wheel_entries[0]


def _required_executable(name: str) -> str:
    executable = shutil.which(name)
    assert executable is not None
    return executable


def _run_git(repository: Path, *arguments: str) -> str:
    completed = subprocess.run(  # noqa: S603
        [_required_executable("git"), *arguments],
        cwd=repository,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert completed.returncode == 0, completed.stderr
    return completed.stdout.strip()


def _create_version_repository(tmp_path: Path) -> tuple[Path, Path]:
    repository = tmp_path / "repository"
    project_root = repository / "src/private/app/azureauth-credprovider"
    project_root.mkdir(parents=True)
    shutil.copy2(WORKSPACE_ROOT / "version.json", repository / "version.json")
    shutil.copy2(VERSION_POLICY_PATH, project_root / "version.json")
    (project_root / "product.txt").write_text(
        "baseline product input\n",
        encoding="utf-8",
    )

    _run_git(repository, "init", "--quiet", "--initial-branch=main")
    _run_git(repository, "config", "user.email", "tests@example.invalid")
    _run_git(repository, "config", "user.name", "AzureAuth version tests")
    _run_git(repository, "add", ".")
    _run_git(repository, "commit", "--quiet", "--message", "baseline")
    return repository, project_root


def _commit_path(repository: Path, relative_path: str) -> None:
    changed_path = repository / relative_path
    changed_path.parent.mkdir(parents=True, exist_ok=True)
    changed_path.write_text(
        f"committed change at {relative_path}\n",
        encoding="utf-8",
    )
    _run_git(repository, "add", "--", relative_path)
    _run_git(repository, "commit", "--quiet", "--message", relative_path)


def _get_version(
    project_root: Path,
    command_root: Path = WORKSPACE_ROOT,
) -> dict[str, object]:
    completed = subprocess.run(  # noqa: S603
        [
            _required_executable("dotnet"),
            "tool",
            "run",
            "nbgv",
            "get-version",
            "--format",
            "json",
            "--project",
            str(project_root),
        ],
        cwd=command_root,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert completed.returncode == 0, completed.stderr
    return json.loads(completed.stdout)


def _build_wheel(repository: Path, output_directory: Path) -> Path:
    completed = subprocess.run(  # noqa: S603
        [
            _required_executable("uv"),
            "build",
            "--package",
            "azureauth-credprovider-keyring",
            "--wheel",
            "--out-dir",
            str(output_directory),
        ],
        cwd=repository,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )
    assert completed.returncode == 0, completed.stderr
    wheel_paths = list(output_directory.glob("*.whl"))
    assert len(wheel_paths) == 1
    return wheel_paths[0]


def _read_wheel_metadata(wheel_path: Path) -> Message:
    with zipfile.ZipFile(wheel_path) as wheel:
        metadata_paths = [
            name
            for name in wheel.namelist()
            if name.endswith(".dist-info/METADATA")
        ]
        assert len(metadata_paths) == 1
        metadata_text = wheel.read(metadata_paths[0]).decode("utf-8")
    return Parser().parsestr(metadata_text)


def _wheel_version(semver2: object) -> str:
    version = str(semver2)
    if "-" not in version:
        return version

    base, prerelease = version.split("-", maxsplit=1)
    channel, height, *metadata = prerelease.split(".")
    assert channel == "beta"
    assert len(metadata) <= 1

    wheel_version = f"{base}b{height}"
    if metadata:
        wheel_version += f"+{metadata[0]}"
    return wheel_version
