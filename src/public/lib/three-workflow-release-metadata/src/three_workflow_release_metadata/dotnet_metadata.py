""".NET metadata observation helper for workflow release planning."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Final

from three_workflow_release_contracts import (
    ContractValidationError,
    validate_contract,
)

Json = dict[str, object]
Runner = Callable[
    [Sequence[str], Path],
    subprocess.CompletedProcess[str],
]

_DOTNET_METADATA_INPUT_API_VERSION: Final = (
    "three.release.dotnet-planner-metadata-input/v1alpha1"
)
_DOTNET_METADATA_API_VERSION: Final = (
    "three.release.dotnet-planner-metadata/v1alpha1"
)
_MAX_ERROR_LENGTH: Final = 2000
_MAX_NUGET_PACKAGE_ID_LENGTH: Final = 100
_NUGET_PACKAGE_ID_RE: Final = re.compile(r"^\w+([.-]\w+)*$", flags=re.ASCII)


class DotnetMetadataError(ValueError):
    """Raised when .NET metadata cannot be collected unambiguously."""

    def __init__(self, diagnostics: Sequence[Mapping[str, object]]) -> None:
        """Initialize with blocking diagnostics."""
        self.diagnostics = tuple(dict(item) for item in diagnostics)
        message = "; ".join(str(item["message"]) for item in diagnostics)
        super().__init__(message)

    def document(self) -> Json:
        """Return the closed planner diagnostics document."""
        return diagnostics_document(self.diagnostics)


@dataclass(frozen=True, slots=True)
class _ProjectInput:
    project_id: str
    descriptor_path: str
    primary_manifest_path: str
    requires_package_id: bool


def collect_dotnet_metadata(
    metadata_input: object,
    repo_root: Path,
    *,
    runner: Runner | None = None,
) -> Json:
    """Collect NBGV version and optional PackageId for .NET projects."""
    metadata = _validate_metadata_input(metadata_input)
    nbgv = _trusted_nbgv_command()

    resolved_repo = repo_root.resolve()
    run = runner or _subprocess_runner
    commit_sha = str(metadata["commit-sha"])
    diagnostics: list[Json] = []
    projects: dict[str, Json] = {}
    for project in _project_inputs(metadata):
        entry = _collect_project(project, commit_sha, resolved_repo, nbgv, run)
        if isinstance(entry, _ProjectFailure):
            diagnostics.append(entry.diagnostic)
            continue
        projects[project.project_id] = entry

    if diagnostics:
        raise DotnetMetadataError(diagnostics)

    document: Json = {
        "api-version": _DOTNET_METADATA_API_VERSION,
        "kind": "dotnet-planner-metadata",
        "commit-sha": commit_sha,
        "projects": projects,
    }
    validate_contract(document, metadata_input=metadata)
    return document


def _validate_metadata_input(metadata_input: object) -> Mapping[str, object]:
    """Require and validate the closed .NET metadata input handoff."""
    if not isinstance(metadata_input, Mapping):
        message = "input must be a JSON object"
        raise _metadata_input_error(
            message,
            {"actual-type": type(metadata_input).__name__},
        )
    if (
        metadata_input.get("kind") != "dotnet-planner-metadata-input"
        or metadata_input.get("api-version")
        != _DOTNET_METADATA_INPUT_API_VERSION
    ):
        message = "input must be dotnet-planner-metadata-input"
        raise _metadata_input_error(
            message,
            {
                "api-version": str(metadata_input.get("api-version")),
                "kind": str(metadata_input.get("kind")),
            },
        )
    try:
        validate_contract(metadata_input)
    except ContractValidationError as exc:
        message = "dotnet-planner-metadata-input violates the closed contract"
        raise _metadata_input_error(
            message,
            {
                "issues": [
                    {
                        "path": issue.path,
                        "message": issue.message,
                    }
                    for issue in exc.issues
                ]
            },
        ) from exc
    return metadata_input


def _metadata_input_error(
    message: str,
    details: Mapping[str, object],
) -> DotnetMetadataError:
    """Create a request-scoped .NET metadata input error."""
    return DotnetMetadataError(
        [
            _diagnostic(
                "request",
                message,
                details=details,
            )
        ]
    )


def diagnostics_document(diagnostics: Sequence[Mapping[str, object]]) -> Json:
    """Create the closed planner diagnostics container."""
    document: Json = {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": [dict(item) for item in diagnostics],
    }
    validate_contract(document)
    return document


@dataclass(frozen=True, slots=True)
class _ProjectFailure:
    diagnostic: Json


def _collect_project(
    project: _ProjectInput,
    commit_sha: str,
    repo_root: Path,
    nbgv: str,
    runner: Runner,
) -> Json | _ProjectFailure:
    manifest_path = _safe_repo_path(repo_root, project.primary_manifest_path)
    if manifest_path is None:
        return _ProjectFailure(
            _project_diagnostic(
                project.project_id,
                "primary manifest path is not a normalized repo-relative path",
                details={
                    "primary-manifest-path": project.primary_manifest_path
                },
            )
        )
    version = _resolved_version(
        project.project_id,
        manifest_path,
        commit_sha,
        repo_root,
        nbgv,
        runner,
    )
    if isinstance(version, _ProjectFailure):
        return version

    entry: Json = {
        "descriptor-path": project.descriptor_path,
        "primary-manifest-path": project.primary_manifest_path,
        "resolved-version": version,
    }
    if project.requires_package_id:
        package_id = _package_id(
            project.project_id,
            manifest_path,
            repo_root,
            runner,
        )
        if isinstance(package_id, _ProjectFailure):
            return package_id
        entry["package-id"] = package_id
    return entry


def _resolved_version(  # noqa: PLR0913
    project_id: str,
    manifest_path: Path,
    commit_sha: str,
    repo_root: Path,
    nbgv: str,
    runner: Runner,
) -> str | _ProjectFailure:
    project_dir = manifest_path.parent.relative_to(repo_root).as_posix()
    result = _run(
        [
            nbgv,
            "get-version",
            commit_sha,
            "--project",
            project_dir,
            "--format",
            "json",
        ],
        repo_root,
        runner,
    )
    if result.returncode != 0:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "build-system NBGV version authority could not be resolved",
                details={
                    "command": "nbgv get-version",
                    "error": _command_error(result),
                },
            )
        )
    try:
        version = json.loads(result.stdout)["SemVer2"]
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "build-system NBGV version authority emitted invalid JSON",
                details={"error": str(exc)},
            )
        )
    if not isinstance(version, str) or not version:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "build-system NBGV version authority emitted an empty version",
                details={},
            )
        )
    return version


def _trusted_nbgv_command() -> str:
    """Return the trusted NBGV executable path for metadata collection."""
    variable = "THREE_WORKFLOW_RELEASE_NBGV_PATH"
    configured = os.environ.get(variable)
    if configured is not None:
        configured = configured.strip()
    if not configured:
        message = "trusted NBGV CLI path is not configured"
        raise _metadata_input_error(
            message,
            {"environment-variable": variable},
        )
    path = Path(configured)
    if not path.is_absolute():
        message = "trusted NBGV CLI path must be absolute"
        raise _metadata_input_error(
            message,
            {
                "environment-variable": variable,
                "configured-path": configured,
            },
        )
    return configured


def _package_id(
    project_id: str,
    manifest_path: Path,
    repo_root: Path,
    runner: Runner,
) -> str | _ProjectFailure:
    explicit_package_id = _explicit_package_id(
        project_id,
        manifest_path,
        repo_root,
        runner,
    )
    if isinstance(explicit_package_id, _ProjectFailure):
        return explicit_package_id
    dotnet = shutil.which("dotnet") or "dotnet"
    relative_manifest = manifest_path.relative_to(repo_root).as_posix()
    result = _run(
        [
            dotnet,
            "msbuild",
            relative_manifest,
            "-nologo",
            "-v:quiet",
            "-getProperty:PackageId",
        ],
        repo_root,
        runner,
    )
    if result.returncode != 0:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "MSBuild PackageId evaluation failed",
                details={
                    "command": "dotnet msbuild -getProperty:PackageId",
                    "error": _command_error(result),
                },
            )
        )
    return _validated_package_id(
        project_id, explicit_package_id, result.stdout.strip()
    )


def _validated_package_id(
    project_id: str,
    explicit_package_id: str,
    package_id: str,
) -> str | _ProjectFailure:
    """Validate the required evaluated PackageId."""
    if not package_id:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "required MSBuild PackageId is missing or empty",
                details={},
            )
        )
    if not explicit_package_id:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "required MSBuild PackageId is missing before SDK fallback",
                details={"evaluated-package-id": package_id},
            )
        )
    invalid_package_id = _invalid_package_id_failure(
        project_id,
        explicit_package_id,
        "explicit-package-id",
    )
    if invalid_package_id is not None:
        return invalid_package_id
    invalid_package_id = _invalid_package_id_failure(
        project_id,
        package_id,
        "evaluated-package-id",
    )
    if invalid_package_id is not None:
        return invalid_package_id
    if explicit_package_id.lower() != package_id.lower():
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "explicit MSBuild PackageId does not match evaluated PackageId",
                details={
                    "explicit-package-id": explicit_package_id,
                    "evaluated-package-id": package_id,
                },
            )
        )
    return package_id


def _invalid_package_id_failure(
    project_id: str,
    package_id: str,
    source: str,
) -> _ProjectFailure | None:
    """Return a failure when a PackageId violates NuGet package ID rules."""
    if len(package_id) > _MAX_NUGET_PACKAGE_ID_LENGTH:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "MSBuild PackageId exceeds NuGet package ID maximum length",
                details={
                    source: package_id,
                    "actual-length": len(package_id),
                    "max-length": _MAX_NUGET_PACKAGE_ID_LENGTH,
                },
            )
        )
    if _NUGET_PACKAGE_ID_RE.fullmatch(package_id):
        return None
    return _ProjectFailure(
        _project_diagnostic(
            project_id,
            "MSBuild PackageId violates NuGet package ID format",
            details={
                source: package_id,
                "required-format": r"^\w+([.-]\w+)*$",
            },
        )
    )


def _explicit_package_id(
    project_id: str,
    manifest_path: Path,
    repo_root: Path,
    runner: Runner,
) -> str | _ProjectFailure:
    """Return active PackageId before SDK pack target fallback."""
    dotnet = shutil.which("dotnet") or "dotnet"
    relative_manifest = manifest_path.relative_to(repo_root).as_posix()
    result = _run(
        [
            dotnet,
            "msbuild",
            relative_manifest,
            "-nologo",
            "-v:quiet",
            "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false",
            "-getProperty:PackageId",
        ],
        repo_root,
        runner,
    )
    if result.returncode != 0:
        return _ProjectFailure(
            _project_diagnostic(
                project_id,
                "pre-fallback MSBuild PackageId evaluation failed",
                details={
                    "command": (
                        "dotnet msbuild "
                        "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false "
                        "-getProperty:PackageId"
                    ),
                    "error": _command_error(result),
                },
            )
        )
    return result.stdout.strip()


def _project_inputs(
    metadata_input: Mapping[str, object],
) -> tuple[_ProjectInput, ...]:
    projects = metadata_input["projects"]
    if not isinstance(projects, Mapping):
        msg = "validated metadata input projects must be a mapping"
        raise TypeError(msg)
    result: list[_ProjectInput] = []
    for project_id in sorted(projects):
        value = projects[project_id]
        if not isinstance(value, Mapping):
            msg = "validated metadata input project entries must be mappings"
            raise TypeError(msg)
        result.append(
            _ProjectInput(
                project_id=str(project_id),
                descriptor_path=str(value["descriptor-path"]),
                primary_manifest_path=str(value["primary-manifest-path"]),
                requires_package_id=bool(value["requires-package-id"]),
            )
        )
    return tuple(result)


def _safe_repo_path(repo_root: Path, raw_path: str) -> Path | None:
    path = Path(raw_path)
    if (
        not raw_path
        or "\\" in raw_path
        or path.is_absolute()
        or any(part in {"", ".", ".."} for part in path.parts)
    ):
        return None
    resolved = (repo_root / path).resolve()
    try:
        resolved.relative_to(repo_root)
    except ValueError:
        return None
    return resolved


def _run(
    args: Sequence[str],
    cwd: Path,
    runner: Runner,
) -> subprocess.CompletedProcess[str]:
    try:
        return runner(args, cwd)
    except OSError as exc:
        return subprocess.CompletedProcess(args, 127, "", str(exc))


def _subprocess_runner(
    args: Sequence[str],
    cwd: Path,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603
        list(args),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
    )


def _diagnostic(
    scope_kind: str,
    message: str,
    *,
    project_id: str | None = None,
    details: Mapping[str, object],
) -> Json:
    diagnostic: Json = {
        "api-version": "three.release.planner-diagnostic/v1alpha1",
        "kind": "planner-diagnostic",
        "code": "DOTNET_METADATA_FAILED",
        "message": message,
        "phase": "normalization",
        "scope-kind": scope_kind,
        "blocking": True,
        "details": dict(details),
    }
    if project_id is not None:
        diagnostic["project-id"] = project_id
    return diagnostic


def _project_diagnostic(
    project_id: str,
    message: str,
    *,
    details: Mapping[str, object],
) -> Json:
    return _diagnostic(
        "project",
        message,
        project_id=project_id,
        details=details,
    )


def _command_error(result: subprocess.CompletedProcess[str]) -> str:
    text = result.stderr.strip() or result.stdout.strip()
    if len(text) > _MAX_ERROR_LENGTH:
        return f"{text[:_MAX_ERROR_LENGTH]}..."
    return text
