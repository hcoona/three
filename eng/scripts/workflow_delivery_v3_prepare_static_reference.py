"""Prepare the exact executable closure for static-reference authorities."""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.static_reference_policy import (
    static_reference_authority_preparation_document,
    static_reference_authority_preparation_stamp_path,
    validate_static_reference_dependency_closures,
)

_REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
_NUGET_PROJECT = (
    _REPOSITORY_ROOT / "src/private/app/workflow-delivery-v3-nuget-authority/"
    "WorkflowDeliveryV3NuGetAuthority.csproj"
)
_PUBLISH_DIRECTORY = (
    _REPOSITORY_ROOT
    / "artifacts/workflow-delivery-v3/static-reference/nuget-authority"
)
_REQUIRED_PUBLISH_FILES = (
    "NuGet.Packaging.dll",
    "NuGet.ProjectModel.dll",
    "WorkflowDeliveryV3NuGetAuthority.deps.json",
    "WorkflowDeliveryV3NuGetAuthority.dll",
    "WorkflowDeliveryV3NuGetAuthority.runtimeconfig.json",
)


def _run(*arguments: str) -> None:
    subprocess.run(
        arguments,
        cwd=_REPOSITORY_ROOT,
        check=True,
    )


def _write_preparation_stamp() -> None:
    stamp_path = static_reference_authority_preparation_stamp_path(
        _REPOSITORY_ROOT
    )
    stamp_path.parent.mkdir(parents=True, exist_ok=True)
    with stamp_path.open("xb") as stream:
        stream.write(
            canonicalize(static_reference_authority_preparation_document())
        )


def main() -> int:
    """Prepare Node packages and the locked NuGet publish directory."""
    stamp_path = static_reference_authority_preparation_stamp_path(
        _REPOSITORY_ROOT
    )
    stamp_path.unlink(missing_ok=True)
    validate_static_reference_dependency_closures(_REPOSITORY_ROOT)
    _run("pnpm", "install", "--frozen-lockfile", "--ignore-scripts")
    _run("dotnet", "restore", str(_NUGET_PROJECT), "--locked-mode")
    if _PUBLISH_DIRECTORY.exists():
        shutil.rmtree(_PUBLISH_DIRECTORY)
    _PUBLISH_DIRECTORY.mkdir(parents=True)
    _run(
        "dotnet",
        "publish",
        str(_NUGET_PROJECT),
        "--no-restore",
        "--configuration",
        "Release",
        "--output",
        str(_PUBLISH_DIRECTORY),
        "--nologo",
    )
    missing = [
        name
        for name in _REQUIRED_PUBLISH_FILES
        if not (_PUBLISH_DIRECTORY / name).is_file()
    ]
    if missing:
        message = "NuGet authority publish closure is incomplete"
        raise RuntimeError(message)
    validate_static_reference_dependency_closures(_REPOSITORY_ROOT)
    _write_preparation_stamp()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
