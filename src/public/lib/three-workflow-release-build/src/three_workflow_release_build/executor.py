"""Ecosystem-specific build executors for workflow-release variants."""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import tarfile
import time
import uuid
import zipfile
from collections.abc import Callable, Mapping, Sequence
from contextlib import suppress
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from email.parser import Parser
from fnmatch import fnmatchcase
from pathlib import Path

from nbgv_python.errors import NbgvVersionNormalizationError
from nbgv_python.versioning import normalize_version_field
from packaging.version import InvalidVersion, Version
from three_workflow_release_contracts import (
    ContractValidationError,
    validate_contract,
)

Json = dict[str, object]
type Runner = Callable[
    [Sequence[str], Path],
    subprocess.CompletedProcess[str],
]

_DOTNET_PACKAGE_KINDS = {"nuget", "snupkg"}
_NUGET_IDENTITY_NUMERIC_PARTS = 3
_NUGET_MAX_NUMERIC_PARTS = 4
_BROWSER_MIN_VERSION_PARTS = 2
_BROWSER_NORMALIZED_VERSION_PARTS = 3
_BROWSER_MAX_VERSION_PARTS = 4
_BROWSER_MAX_VERSION_PART = 65535
_PROFILE_TELEMETRY_FILE = "release-build-profile-telemetry.json"
_TIMING_DURATION_TOLERANCE_MS = 5000
_SUPPORTED_KINDS = {
    "dotnet": {"nuget", "snupkg", "executable", "inno-setup"},
    "python": {"wheel", "sdist"},
    "node": {"npm-package", "browser-zip", "sources-zip"},
    "ruby": {"rubygem"},
}


class BuildExecutorError(ValueError):
    """Raised when a build request cannot be fulfilled exactly once."""

    def __init__(  # noqa: PLR0913
        self,
        message: str,
        *,
        code: str = "BUILD_FAILED",
        phase: str = "execution",
        scope_kind: str = "request",
        project_id: str | None = None,
        variant_id: str | None = None,
        artifact_id: str | None = None,
        details: Mapping[str, object] | None = None,
    ) -> None:
        """Create a build executor error with closed diagnostic metadata."""
        self.code = code
        self.phase = phase
        self.scope_kind = scope_kind
        self.project_id = project_id
        self.variant_id = variant_id
        self.artifact_id = artifact_id
        self.details = dict(details or {})
        super().__init__(message)


@dataclass(frozen=True, slots=True)
class _ArtifactSlot:
    artifact_id: str
    role: str
    kind_family: str
    concrete_kind: str
    companions: tuple[_ArtifactCompanion, ...]
    projection: Mapping[str, object]


@dataclass(frozen=True, slots=True)
class _ArtifactCompanion:
    path: str
    role: str
    required: bool


@dataclass(frozen=True, slots=True)
class _ProducedArtifact:
    path: Path
    receipt_extra: Json | None = None


class _BuildTelemetry:
    """Collect release-build profile timing without changing contracts."""

    def __init__(self, bundle_dir: Path) -> None:
        self._bundle_dir = bundle_dir
        self._phases: list[Json] = []
        self._subprocess_index = 0
        run_id = (
            f"{datetime.now(UTC).strftime('%Y%m%dT%H%M%S%fZ')}-"
            f"{os.getpid()}-{uuid.uuid4().hex[:12]}"
        )
        self._profile_dir = bundle_dir / "_profile" / "runs" / run_id
        self._binlog_dir = self._profile_dir / "binlogs"
        self._script_dir = self._profile_dir / "powershell"

    @property
    def path(self) -> Path:
        """Return the profile telemetry sidecar path."""
        return self._bundle_dir / _PROFILE_TELEMETRY_FILE

    @property
    def profile_dir(self) -> Path:
        """Return the profile telemetry support directory."""
        return self._profile_dir

    def next_binlog_path(self, phase: str) -> Path:
        """Return a deterministic, collision-free MSBuild binlog path."""
        self._subprocess_index += 1
        self._binlog_dir.mkdir(parents=True, exist_ok=True)
        filename = (
            f"{self._subprocess_index:04d}-"
            f"{_safe_profile_filename(phase)}.binlog"
        )
        return self._binlog_dir / filename

    def next_script_telemetry_path(self, phase: str) -> Path:
        """Return a deterministic PowerShell script telemetry path."""
        self._subprocess_index += 1
        self._script_dir.mkdir(parents=True, exist_ok=True)
        filename = (
            f"{self._subprocess_index:04d}-"
            f"{_safe_profile_filename(phase)}.json"
        )
        return self._script_dir / filename

    def next_script_binlog_dir(self, phase: str) -> Path:
        """Return a deterministic script binlog directory."""
        self._subprocess_index += 1
        path = (
            self._binlog_dir
            / f"{self._subprocess_index:04d}-{_safe_profile_filename(phase)}"
        )
        path.mkdir(parents=True, exist_ok=True)
        return path

    def record_phase(  # noqa: PLR0913
        self,
        phase: str,
        start: tuple[datetime, int],
        *,
        outcome: str,
        cwd: Path | None = None,
        argv: Sequence[str] | None = None,
        exit_code: int | None = None,
        output_paths: Sequence[Path] = (),
        binlog_path: Path | None = None,
        binlog_dir: Path | None = None,
        error: str | None = None,
    ) -> None:
        """Append one structured phase timing record."""
        record: Json = {
            "phase": phase,
            "outcome": outcome,
            **_profile_timing_finish(start),
        }
        if cwd is not None:
            record["cwd"] = cwd.as_posix()
        if argv is not None:
            record["argv"] = [str(item) for item in argv]
        if exit_code is not None:
            record["exit-code"] = exit_code
        if output_paths:
            record["output-paths"] = [
                path.as_posix() for path in output_paths
            ]
        if binlog_path is not None:
            record["binlog-path"] = binlog_path.as_posix()
            record["binlog-exists"] = binlog_path.is_file()
        if binlog_dir is not None:
            record["binlog-directory"] = binlog_dir.as_posix()
            record["binlog-paths"] = [
                path.as_posix() for path in sorted(binlog_dir.glob("*.binlog"))
            ]
        if error is not None:
            record["error"] = error
        self._phases.append(record)

    def write(self) -> None:
        """Write profile telemetry sidecar JSON best-effort."""
        try:
            self._profile_dir.mkdir(parents=True, exist_ok=True)
            self.path.write_text(
                json.dumps(
                    {
                        "kind": "release-build-profile-telemetry",
                        "schema-version": 1,
                        "profile-root": self._profile_dir.as_posix(),
                        "phases": self._phases,
                    },
                    indent=2,
                    sort_keys=True,
                )
                + "\n",
                encoding="utf-8",
            )
        except OSError:
            pass


@dataclass(frozen=True, slots=True)
class _BuildContext:
    request: Mapping[str, object]
    artifacts: tuple[_ArtifactSlot, ...]
    project_root: Path
    manifest: Path
    output_root: Path
    runner: Runner
    repo_root: Path
    telemetry: _BuildTelemetry


@dataclass(frozen=True, slots=True)
class _NodePackageRunner:
    args: tuple[str, ...]
    cwd: Path


@dataclass(frozen=True, slots=True)
class _PinnedWorktree:
    path: Path
    repo_root: Path
    runner: Runner

    def remove(self) -> None:
        """Best-effort cleanup for the detached materialization worktree."""
        try:
            result = self.runner(
                [
                    shutil.which("git") or "git",
                    "worktree",
                    "remove",
                    "--force",
                    str(self.path),
                ],
                self.repo_root,
            )
        except OSError:
            result = subprocess.CompletedProcess((), 1, "", "")
        if result.returncode != 0 and self.path.exists():
            _try_remove_tree(self.path)
        worktree_parent = self.path.parent
        try:
            if worktree_parent.exists() and not any(worktree_parent.iterdir()):
                worktree_parent.rmdir()
        except OSError:
            pass
        with suppress(OSError):
            self.runner(
                [shutil.which("git") or "git", "worktree", "prune"],
                self.repo_root,
            )


def build_diagnostics_document(
    error: BuildExecutorError,
    *,
    request: Mapping[str, object] | None = None,
) -> Json:
    """Create a closed build-diagnostics document for one blocking failure."""
    diagnostic: Json = {
        "api-version": "three.release.build-diagnostic/v1alpha1",
        "kind": "build-diagnostic",
        "code": error.code,
        "message": str(error),
        "phase": error.phase,
        "scope-kind": error.scope_kind,
        "blocking": True,
        "details": error.details,
    }
    if request is not None:
        plan_id = request.get("plan-id")
        if isinstance(plan_id, str) and plan_id != "":
            diagnostic["plan-id"] = plan_id
        _fill_diagnostic_scope(diagnostic, error, request)
    _coerce_valid_diagnostic_scope(diagnostic)
    document: Json = {
        "api-version": "three.release.build-diagnostics/v1alpha1",
        "kind": "build-diagnostics",
        "diagnostics": [diagnostic],
    }
    validate_contract(document)
    return document


def _coerce_valid_diagnostic_scope(diagnostic: Json) -> None:
    """Avoid emitting contract-invalid scoped diagnostics."""
    scope_kind = diagnostic.get("scope-kind")
    project_id = diagnostic.get("project-id")
    variant_id = diagnostic.get("variant-id")
    artifact_id = diagnostic.get("artifact-id")
    if scope_kind == "request":
        _drop_diagnostic_scope_ids(diagnostic)
        return
    if scope_kind == "project" and _is_non_empty_string(project_id):
        diagnostic.pop("variant-id", None)
        diagnostic.pop("artifact-id", None)
        return
    if (
        scope_kind == "variant"
        and _is_non_empty_string(project_id)
        and _is_non_empty_string(variant_id)
    ):
        diagnostic.pop("artifact-id", None)
        return
    if (
        scope_kind == "artifact"
        and _is_non_empty_string(project_id)
        and _is_non_empty_string(variant_id)
        and _is_non_empty_string(artifact_id)
    ):
        return
    diagnostic["scope-kind"] = "request"
    _drop_diagnostic_scope_ids(diagnostic)


def _drop_diagnostic_scope_ids(diagnostic: Json) -> None:
    """Remove all scope identity fields from a diagnostic."""
    diagnostic.pop("project-id", None)
    diagnostic.pop("variant-id", None)
    diagnostic.pop("artifact-id", None)


def _is_non_empty_string(value: object) -> bool:
    """Return whether a JSON value is a non-empty string."""
    return isinstance(value, str) and value != ""


def _fill_diagnostic_scope(
    diagnostic: Json,
    error: BuildExecutorError,
    request: Mapping[str, object],
) -> None:
    """Fill scope identity fields for a build diagnostic."""
    if error.scope_kind == "request":
        return
    project_id = error.project_id or _request_project_id(request)
    if project_id is not None:
        diagnostic["project-id"] = project_id
    if error.scope_kind == "project":
        return
    variant_id = error.variant_id or _request_variant_id(request)
    if variant_id is not None:
        diagnostic["variant-id"] = variant_id
    if error.scope_kind == "variant":
        return
    artifact_id = error.artifact_id or _request_artifact_id(request)
    if artifact_id is not None:
        diagnostic["artifact-id"] = artifact_id


def _request_project_id(request: Mapping[str, object]) -> str | None:
    """Return the request's variant project id when present."""
    variant = request.get("variant")
    if isinstance(variant, Mapping) and isinstance(
        variant.get("project-id"), str
    ):
        return variant["project-id"]
    return None


def _request_variant_id(request: Mapping[str, object]) -> str | None:
    """Derive the unique variant id carried by requested artifacts."""
    artifacts = request.get("artifacts")
    if not isinstance(artifacts, Mapping):
        return None
    variant_ids = {
        artifact.get("variant-id")
        for artifact in artifacts.values()
        if isinstance(artifact, Mapping)
        and isinstance(artifact.get("variant-id"), str)
    }
    if len(variant_ids) != 1:
        return None
    return next(iter(variant_ids))


def _request_artifact_id(request: Mapping[str, object]) -> str | None:
    """Return the request artifact id when exactly one artifact is present."""
    artifacts = request.get("artifacts")
    if not isinstance(artifacts, Mapping) or len(artifacts) != 1:
        return None
    artifact_id = next(iter(artifacts))
    if isinstance(artifact_id, str):
        return artifact_id
    return None


def _profile_timing_start() -> tuple[datetime, int]:
    return datetime.now(UTC), time.perf_counter_ns()


def _profile_timing_finish(start: tuple[datetime, int]) -> Json:
    started_at, started_ns = start
    completed_ns = time.perf_counter_ns()
    completed_at = datetime.now(UTC)
    duration_ms = max(0, (completed_ns - started_ns) // 1_000_000)
    elapsed_ms = int((completed_at - started_at).total_seconds() * 1000)
    if (
        completed_at < started_at
        or abs(elapsed_ms - duration_ms) > _TIMING_DURATION_TOLERANCE_MS
    ):
        completed_at = started_at + timedelta(milliseconds=duration_ms)
    return {
        "started-at": _utc_timestamp_milliseconds(started_at),
        "completed-at": _utc_timestamp_milliseconds(completed_at),
        "duration-ms": duration_ms,
    }


def _utc_timestamp_milliseconds(value: datetime) -> str:
    return value.astimezone(UTC).isoformat(timespec="milliseconds").replace(
        "+00:00",
        "Z",
    )


def _safe_profile_filename(value: str) -> str:
    safe = re.sub(r"[^A-Za-z0-9_.-]+", "-", value).strip(".-")
    return safe or "phase"


def execute_build(  # noqa: PLR0915
    request: Mapping[str, object],
    repo_root: Path,
    bundle_dir: Path,
    *,
    runner: Runner | None = None,
    check_commit: bool = True,
) -> Json:
    """Execute one closed build request and return a build-result object."""
    normalized_request = _validate_request(request)
    resolved_repo = repo_root.resolve()
    resolved_bundle = bundle_dir.resolve()
    try:
        resolved_bundle.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        _raise_output_filesystem_error(
            "bundle directory could not be prepared",
            resolved_bundle,
            exc,
            operation="mkdir",
        )
    telemetry = _BuildTelemetry(resolved_bundle)
    run = runner or _subprocess_runner

    project = _mapping(normalized_request["project"], "project")
    variant = _mapping(normalized_request["variant"], "variant")
    artifacts = _artifact_slots(normalized_request)
    ecosystem = str(project["ecosystem"])
    _validate_supported_artifacts(ecosystem, artifacts)
    variant_id = _variant_id_from_request(normalized_request)

    prepare_timing = _profile_timing_start()
    try:
        output_root, dist_dir = _prepare_bundle_dirs(resolved_bundle)
    except Exception as exc:
        telemetry.record_phase(
            "prepare-bundle-directories",
            prepare_timing,
            outcome="failure",
            cwd=resolved_bundle,
            error=str(exc),
        )
        telemetry.write()
        raise
    telemetry.record_phase(
        "prepare-bundle-directories",
        prepare_timing,
        outcome="success",
        cwd=resolved_bundle,
        output_paths=[output_root, dist_dir],
    )

    if not artifacts:
        msg = (
            "build requests without artifacts cannot identify a variant result"
        )
        raise BuildExecutorError(msg)

    materialized_repo = resolved_repo
    worktree: _PinnedWorktree | None = None
    try:
        if check_commit:
            materialized_repo = _materialize_pinned_worktree(
                normalized_request,
                resolved_repo,
                resolved_bundle,
                run,
                telemetry,
            )
            worktree = _PinnedWorktree(materialized_repo, resolved_repo, run)
        project_root = _safe_repo_path(
            materialized_repo, str(project["release-root"])
        )
        source = _mapping(project["source"], "project.source")
        manifest = _safe_repo_path(
            materialized_repo, str(source["primary-manifest-path"])
        )
        context = _BuildContext(
            request=normalized_request,
            artifacts=artifacts,
            project_root=project_root,
            manifest=manifest,
            output_root=output_root,
            runner=run,
            repo_root=materialized_repo,
            telemetry=telemetry,
        )
        execute_timing = _profile_timing_start()
        try:
            produced = _execute_ecosystem(ecosystem, context)
        except Exception as exc:
            telemetry.record_phase(
                "execute-ecosystem-build",
                execute_timing,
                outcome="failure",
                cwd=context.project_root,
                error=str(exc),
            )
            raise
        telemetry.record_phase(
            "execute-ecosystem-build",
            execute_timing,
            outcome="success",
            cwd=context.project_root,
        )
        if set(produced) != {slot.artifact_id for slot in artifacts}:
            msg = "produced artifact ids do not exactly match build request"
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")

        receipt_timing = _profile_timing_start()
        try:
            receipts = _receipt_produced_artifacts(
                produced, dist_dir, resolved_bundle
            )
        except Exception as exc:
            telemetry.record_phase(
                "receipt-copy-and-hash",
                receipt_timing,
                outcome="failure",
                cwd=dist_dir,
                error=str(exc),
            )
            raise
        telemetry.record_phase(
            "receipt-copy-and-hash",
            receipt_timing,
            outcome="success",
            cwd=dist_dir,
            output_paths=[
                dist_dir / str(item["bundle-relative-path"])
                for item in receipts.values()
            ],
        )
        result = _build_result(
            normalized_request, variant, variant_id, receipts
        )
    finally:
        if worktree is not None:
            worktree.remove()
        telemetry.write()
    return result


def _prepare_bundle_dirs(bundle_dir: Path) -> tuple[Path, Path]:
    """Create clean executor work and distribution directories."""
    output_root = bundle_dir / "_executor-work"
    dist_dir = bundle_dir / "dist"
    try:
        if output_root.exists():
            _remove_tree(output_root)
    except OSError as exc:
        _raise_output_filesystem_error(
            "executor work directory could not be removed",
            output_root,
            exc,
            operation="remove",
        )
    try:
        output_root.mkdir()
    except OSError as exc:
        _raise_output_filesystem_error(
            "executor work directory could not be created",
            output_root,
            exc,
            operation="mkdir",
        )
    try:
        if dist_dir.exists():
            _remove_tree(dist_dir)
    except OSError as exc:
        _raise_output_filesystem_error(
            "distribution directory could not be removed",
            dist_dir,
            exc,
            operation="remove",
        )
    try:
        dist_dir.mkdir()
    except OSError as exc:
        _raise_output_filesystem_error(
            "distribution directory could not be created",
            dist_dir,
            exc,
            operation="mkdir",
        )
    return output_root, dist_dir


def _raise_output_filesystem_error(
    message: str,
    path: Path,
    exc: OSError,
    *,
    operation: str,
) -> None:
    """Raise a closed output diagnostic for filesystem preparation errors."""
    msg = f"{message}: {exc}"
    raise BuildExecutorError(
        msg,
        code="BUILD_OUTPUT_INVALID",
        phase="receipt",
        details={
            "path": path.as_posix(),
            "operation": operation,
            "error": str(exc),
        },
    ) from exc


def _mkdir_output_work_dir(path: Path) -> None:
    """Create an executor output work directory with closed diagnostics."""
    try:
        path.mkdir()
    except OSError as exc:
        _raise_output_filesystem_error(
            "executor output directory could not be created",
            path,
            exc,
            operation="mkdir",
        )


def _receipt_produced_artifacts(
    produced: Mapping[str, Path | _ProducedArtifact],
    dist_dir: Path,
    bundle_dir: Path,
) -> dict[str, Json]:
    """Copy produced artifacts into the bundle and create receipts."""
    receipts: dict[str, Json] = {}
    used_paths: set[str] = set()
    for artifact_id in sorted(produced):
        produced_entry = produced[artifact_id]
        if isinstance(produced_entry, _ProducedArtifact):
            produced_path = produced_entry.path
            receipt_extra = produced_entry.receipt_extra
        else:
            produced_path = produced_entry
            receipt_extra = None
        if not produced_path.is_file():
            msg = f"artifact {artifact_id!r} was not produced as one file"
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
        destination = _copy_to_dist(produced_path, dist_dir)
        relative = destination.relative_to(bundle_dir).as_posix()
        if relative in used_paths:
            msg = f"duplicate bundle-relative path {relative!r}"
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
        used_paths.add(relative)
        receipts[artifact_id] = _artifact_receipt(
            destination, relative, receipt_extra
        )
    return receipts


def _build_result(
    request: Mapping[str, object],
    variant: Mapping[str, object],
    variant_id: str,
    artifacts: Mapping[str, Json],
) -> Json:
    """Create and validate the build-result receipt."""
    result: Json = {
        "api-version": "three.release.build-result/v1alpha1",
        "kind": "build-result",
        "plan-id": request["plan-id"],
        "project-id": variant["project-id"],
        "variant-id": variant_id,
        "artifacts": dict(artifacts),
    }
    validate_contract(result)
    return result


def _validate_supported_artifacts(
    ecosystem: str,
    artifacts: Sequence[_ArtifactSlot],
) -> None:
    """Fail unless all requested concrete kinds are supported."""
    supported = _SUPPORTED_KINDS.get(ecosystem)
    if supported is None:
        msg = f"unsupported ecosystem {ecosystem!r}"
        raise BuildExecutorError(msg)
    unsupported = sorted(
        slot.concrete_kind
        for slot in artifacts
        if slot.concrete_kind not in supported
    )
    if unsupported:
        msg = f"unsupported artifact kinds for {ecosystem}: {unsupported}"
        raise BuildExecutorError(msg)


def _execute_ecosystem(
    ecosystem: str,
    context: _BuildContext,
) -> dict[str, Path | _ProducedArtifact]:
    """Dispatch to the selected ecosystem build executor."""
    frozen_version = _frozen_version(context.request)
    if ecosystem == "python":
        return _plain_produced_artifacts(
            _python_build(
                context.artifacts,
                context.project_root,
                context.output_root,
                context.runner,
                frozen_version,
            )
        )
    if ecosystem == "node":
        return _plain_produced_artifacts(_node_build(context, frozen_version))
    if ecosystem == "ruby":
        return _plain_produced_artifacts(_ruby_build(context, frozen_version))
    if ecosystem == "dotnet":
        return _dotnet_build(context)
    msg = f"unsupported ecosystem {ecosystem!r}"
    raise BuildExecutorError(msg)


def _plain_produced_artifacts(
    produced: Mapping[str, Path],
) -> dict[str, Path | _ProducedArtifact]:
    """Return plain path artifacts with the executor-wide artifact type."""
    return dict(produced)


def _python_build(
    artifacts: Sequence[_ArtifactSlot],
    project_root: Path,
    output_root: Path,
    runner: Runner,
    frozen_version: str,
) -> dict[str, Path]:
    """Build Python wheel and/or sdist artifacts with uv."""
    kinds = {slot.concrete_kind for slot in artifacts}
    output = output_root / "python"
    _mkdir_output_work_dir(output)
    command = [
        shutil.which("uv") or "uv",
        "build",
        project_root.as_posix(),
        "--out-dir",
        output.as_posix(),
        "--no-create-gitignore",
    ]
    if kinds == {"wheel"}:
        command.append("--wheel")
    elif kinds == {"sdist"}:
        command.append("--sdist")
    elif kinds != {"wheel", "sdist"}:
        msg = f"unsupported Python artifact kind set: {sorted(kinds)}"
        raise BuildExecutorError(msg)
    _run_checked(command, project_root, runner)
    produced = _match_outputs(
        artifacts,
        {
            "wheel": tuple(output.glob("*.whl")),
            "sdist": tuple(output.glob("*.tar.gz")),
        },
    )
    _validate_package_versions(
        produced, artifacts, frozen_version, runner, project_root
    )
    return produced


def _node_build(context: _BuildContext, frozen_version: str) -> dict[str, Path]:
    """Build npm package tarballs or browser extension zip artifacts."""
    kinds = {slot.concrete_kind for slot in context.artifacts}
    if kinds in ({"browser-zip"}, {"browser-zip", "sources-zip"}):
        return _node_browser_zip_build(context)
    if kinds != {"npm-package"}:
        msg = f"unsupported node artifact kind set: {sorted(kinds)}"
        raise BuildExecutorError(msg)
    original_manifest_bytes = _read_npm_package_json_bytes(context.manifest)
    package_json = _parse_npm_package_json_bytes(original_manifest_bytes)
    scripts = package_json.get("scripts")
    if not isinstance(scripts, Mapping) or not isinstance(
        scripts.get("build"), str
    ):
        msg = "npm packages must declare an explicit build script"
        raise BuildExecutorError(msg)
    output = context.output_root / "node"
    _mkdir_output_work_dir(output)
    package_runner = _prepare_node_package_runner(context)
    try:
        _run_checked(
            [*package_runner.args, "run", "build"],
            package_runner.cwd,
            context.runner,
        )
        produced: dict[str, Path] = {}
        for index, slot in enumerate(
            sorted(context.artifacts, key=lambda item: item.artifact_id)
        ):
            pack_output = (
                output / f"{index:04d}-{_safe_filename(slot.artifact_id)}"
            )
            _mkdir_output_work_dir(pack_output)
            produced_path, expected_package_name = _node_pack_artifact(
                slot,
                pack_output,
                package_runner,
                context,
            )
            _validate_npm_tarball(
                produced_path,
                expected_package_name,
                frozen_version,
                context.runner,
                context.project_root,
            )
            produced[slot.artifact_id] = produced_path
        return produced
    finally:
        _write_npm_package_json_bytes(context.manifest, original_manifest_bytes)


def _node_browser_zip_build(context: _BuildContext) -> dict[str, Path]:
    """Build one WXT browser extension zip for a browser-dimensioned variant."""
    browser_slots = [
        slot
        for slot in context.artifacts
        if slot.concrete_kind == "browser-zip"
    ]
    source_slots = [
        slot
        for slot in context.artifacts
        if slot.concrete_kind == "sources-zip"
    ]
    if (
        len(browser_slots) != 1
        or len(source_slots) > 1
        or len(context.artifacts) != len(browser_slots) + len(source_slots)
    ):
        msg = "browser-zip node variants may request one optional sources-zip"
        raise BuildExecutorError(msg)
    original_manifest_bytes = _read_npm_package_json_bytes(context.manifest)
    package_json = _parse_npm_package_json_bytes(original_manifest_bytes)
    scripts = package_json.get("scripts")
    browser = _browser_dimension(context.request)
    if source_slots and browser != "firefox":
        msg = (
            "sources-zip browser extension artifacts are only valid for "
            "firefox variants"
        )
        raise BuildExecutorError(msg)
    build_script = _browser_zip_script_name(scripts, browser)
    if build_script is None:
        msg = (
            "browser-zip packages must declare an explicit release zip or "
            "build script"
        )
        raise BuildExecutorError(msg)
    output = context.output_root / "node-browser-zip"
    _mkdir_output_work_dir(output)
    package_runner = _prepare_node_package_runner(context)
    frozen_version = _frozen_version(context.request)
    browser_version = _browser_extension_manifest_version(frozen_version)
    stamped_package_json = dict(package_json)
    stamped_package_json["version"] = browser_version
    _write_npm_package_json_bytes(
        context.manifest,
        json.dumps(stamped_package_json, indent=2).encode() + b"\n",
    )
    try:
        _run_checked(
            [*package_runner.args, "run", build_script],
            package_runner.cwd,
            context.runner,
        )
        produced_browser_zips = _browser_zip_candidates(
            context.project_root, browser
        )
        if len(produced_browser_zips) != 1:
            msg = (
                f"expected one browser zip output for {browser!r}, "
                f"found {len(produced_browser_zips)}"
            )
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
        _validate_browser_zip_manifest_version(
            produced_browser_zips[0], browser_version
        )
        browser_destination = _copy_browser_zip_output(
            produced_browser_zips[0], output, context.request
        )
        produced = {
            slot.artifact_id: browser_destination for slot in browser_slots
        }
        if source_slots:
            produced_sources = _sources_zip_candidates(context.project_root)
            if len(produced_sources) != 1:
                msg = (
                    "expected one sources zip output for firefox, "
                    f"found {len(produced_sources)}"
                )
                raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
            sources_destination = _copy_sources_zip_output(
                produced_sources[0], output, context.request
            )
            produced[source_slots[0].artifact_id] = sources_destination
        return produced
    finally:
        _write_npm_package_json_bytes(context.manifest, original_manifest_bytes)


def _copy_browser_zip_output(
    source: Path, output: Path, request: Mapping[str, object]
) -> Path:
    """Copy a browser zip into the build output with its frozen asset name."""
    return _copy_zip_output(
        source, output / _browser_zip_filename(request), "browser zip"
    )


def _copy_sources_zip_output(
    source: Path, output: Path, request: Mapping[str, object]
) -> Path:
    """Copy a sources zip into the build output with its frozen asset name."""
    return _copy_zip_output(
        source, output / _sources_zip_filename(request), "sources zip"
    )


def _copy_zip_output(source: Path, destination: Path, label: str) -> Path:
    """Copy one WXT zip output into the build output directory."""
    try:
        shutil.copy2(source, destination)
    except OSError as exc:
        msg = f"{label} output could not be copied: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={
                "source": source.as_posix(),
                "destination": destination.as_posix(),
                "error": str(exc),
            },
        ) from exc
    return destination


def _browser_zip_script_name(scripts: object, browser: str) -> str | None:
    """Return the release script to build one WXT browser package."""
    if not isinstance(scripts, Mapping):
        return None
    for candidate in (
        f"workflow-release:zip:{browser}",
        "workflow-release:zip" if browser == "chrome" else "",
        f"zip:{browser}",
        "zip" if browser == "chrome" else "",
        "build",
    ):
        if candidate and isinstance(scripts.get(candidate), str):
            return candidate
    return None


def _browser_dimension(request: Mapping[str, object]) -> str:
    """Return the browser dimension for a browser-zip build request."""
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    browser = dimensions.get("browser")
    if browser not in {"chrome", "firefox", "edge"}:
        msg = "browser-zip variants must carry browser chrome/firefox/edge"
        raise BuildExecutorError(msg)
    return str(browser)


def _browser_zip_candidates(
    project_root: Path, browser: str
) -> tuple[Path, ...]:
    """Return WXT zip outputs matching the requested browser."""
    output_root = project_root / ".output"
    if not output_root.is_dir():
        msg = "browser-zip build did not produce a .output directory"
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
    return tuple(
        sorted(
            path
            for path in output_root.rglob("*.zip")
            if path.is_file() and path.stem.casefold().endswith(f"-{browser}")
        )
    )


def _sources_zip_candidates(project_root: Path) -> tuple[Path, ...]:
    """Return WXT source zip outputs."""
    output_root = project_root / ".output"
    if not output_root.is_dir():
        msg = "sources-zip build did not produce a .output directory"
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
    return tuple(
        sorted(
            path
            for path in output_root.rglob("*.zip")
            if path.is_file() and path.stem.casefold().endswith("-sources")
        )
    )


def _browser_extension_manifest_version(version: str) -> str:
    """Map a frozen release version to a browser manifest numeric version."""
    release_core = re.split(r"[+-]", version, maxsplit=1)[0]
    parts = release_core.split(".")
    if not all(re.fullmatch(r"0|[1-9]\d*", part) for part in parts):
        msg = f"browser-zip resolved version is not version-like: {version!r}"
        raise BuildExecutorError(msg, code="BUILD_INVALID_INPUT")
    release = [int(part) for part in parts]
    if not (
        _BROWSER_MIN_VERSION_PARTS <= len(release) <= _BROWSER_MAX_VERSION_PARTS
    ):
        msg = (
            "browser-zip resolved version must contain 2 to 4 numeric "
            f"components, got {version!r}"
        )
        raise BuildExecutorError(msg, code="BUILD_INVALID_INPUT")
    while len(release) < _BROWSER_NORMALIZED_VERSION_PARTS:
        release.append(0)
    if any(part < 0 or part > _BROWSER_MAX_VERSION_PART for part in release):
        msg = (
            "browser-zip resolved version components must be between "
            f"0 and 65535, got {version!r}"
        )
        raise BuildExecutorError(msg, code="BUILD_INVALID_INPUT")
    return ".".join(str(part) for part in release)


def _validate_browser_zip_manifest_version(
    zip_path: Path, expected_version: str
) -> None:
    """Validate that a browser zip carries the expected manifest version."""
    try:
        with (
            zipfile.ZipFile(zip_path) as archive,
            archive.open("manifest.json") as manifest_file,
        ):
            manifest = json.load(manifest_file)
    except (KeyError, OSError, json.JSONDecodeError, zipfile.BadZipFile) as exc:
        msg = "browser zip output must contain a valid root manifest.json"
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID") from exc
    if not isinstance(manifest, Mapping):
        msg = "browser zip manifest.json must contain a JSON object"
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
    actual_version = manifest.get("version")
    if actual_version != expected_version:
        msg = (
            "browser zip manifest version does not match resolved version: "
            f"expected {expected_version!r}, got {actual_version!r}"
        )
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")


def _browser_zip_filename(request: Mapping[str, object]) -> str:
    """Return the planner-frozen GitHub asset name for browser zips."""
    project = _mapping(request["project"], "project")
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    return (
        f"{variant['project-id']}-"
        f"{project['resolved-version']}-"
        f"{_variant_token(dimensions)}.zip"
    )


def _sources_zip_filename(request: Mapping[str, object]) -> str:
    """Return the planner-frozen GitHub asset name for source archives."""
    project = _mapping(request["project"], "project")
    variant = _mapping(request["variant"], "variant")
    return f"{variant['project-id']}-{project['resolved-version']}-sources.zip"


def _node_pack_artifact(
    slot: _ArtifactSlot,
    output: Path,
    package_runner: _NodePackageRunner,
    context: _BuildContext,
) -> tuple[Path, str]:
    """Pack one npm artifact, applying package-name projection if present."""
    projected = slot.projection.get("package-name")
    original_manifest_bytes: bytes | None = None
    projected_package_json: dict[str, object] | None = None
    if isinstance(projected, str):
        original_manifest_bytes = _read_npm_package_json_bytes(context.manifest)
        package_json = _parse_npm_package_json_bytes(original_manifest_bytes)
        if package_json.get("name") != projected:
            projected_package_json = dict(package_json)
            projected_package_json["name"] = projected
    try:
        if projected_package_json is not None:
            _write_npm_package_json(context.manifest, projected_package_json)
        expected_package_name = _read_effective_npm_package_name(
            context.manifest
        )
        command = [
            *package_runner.args,
            "pack",
            "--json",
            "--pack-destination",
            output.as_posix(),
        ]
        result = _run_checked(command, package_runner.cwd, context.runner)
        _validate_npm_pack_json(result.stdout)
    finally:
        if original_manifest_bytes is not None:
            _write_npm_package_json_bytes(
                context.manifest, original_manifest_bytes
            )
    produced = list(output.glob("*.tgz"))
    if len(produced) != 1:
        msg = (
            f"expected one npm pack output for {slot.artifact_id!r}, "
            f"found {len(produced)}"
        )
        raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
    return produced[0], expected_package_name


def _prepare_node_package_runner(context: _BuildContext) -> _NodePackageRunner:
    """Install pnpm workspace dependencies and return package command prefix."""
    if _is_pnpm_workspace_project(context.repo_root, context.project_root):
        corepack = shutil.which("corepack") or "corepack"
        pnpm = shutil.which("pnpm") or "pnpm"
        _run_checked(
            [corepack, "enable", "pnpm"], context.repo_root, context.runner
        )
        _run_checked(
            [pnpm, "install", "--frozen-lockfile"],
            context.repo_root,
            context.runner,
        )
        return _NodePackageRunner(
            (pnpm, "--dir", context.project_root.as_posix()),
            context.repo_root,
        )
    npm = shutil.which("npm") or "npm"
    return _NodePackageRunner((npm,), context.project_root)


def _is_pnpm_workspace_project(repo_root: Path, project_root: Path) -> bool:
    """Return whether a project is covered by the root pnpm workspace."""
    workspace = repo_root / "pnpm-workspace.yaml"
    lockfile = repo_root / "pnpm-lock.yaml"
    if not workspace.is_file() or not lockfile.is_file():
        return False
    try:
        relative = project_root.resolve().relative_to(repo_root.resolve())
    except ValueError:
        return False
    relative_posix = relative.as_posix()
    try:
        lines = workspace.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        msg = f"pnpm workspace manifest could not be read: {exc}"
        raise BuildExecutorError(msg) from exc
    in_packages = False
    for line in lines:
        stripped = line.strip()
        if stripped == "packages:":
            in_packages = True
            continue
        if not in_packages:
            continue
        if (
            stripped
            and not stripped.startswith("-")
            and not line.startswith(" ")
        ):
            break
        if not stripped.startswith("-"):
            continue
        pattern = stripped[1:].strip().strip("'\"")
        if pattern and _pnpm_workspace_pattern_matches(relative_posix, pattern):
            return True
    return False


def _pnpm_workspace_pattern_matches(relative_posix: str, pattern: str) -> bool:
    """Match pnpm workspace package globs against a relative project path."""
    path_segments = relative_posix.strip("/").split("/")
    pattern_segments = pattern.strip("/").split("/")

    def match_from(path_index: int, pattern_index: int) -> bool:
        if pattern_index == len(pattern_segments):
            return path_index == len(path_segments)
        pattern_segment = pattern_segments[pattern_index]
        if pattern_segment == "**":
            return match_from(path_index, pattern_index + 1) or (
                path_index < len(path_segments)
                and match_from(path_index + 1, pattern_index)
            )
        return (
            path_index < len(path_segments)
            and fnmatchcase(path_segments[path_index], pattern_segment)
            and match_from(path_index + 1, pattern_index + 1)
        )

    return match_from(0, 0)


def _ruby_build(context: _BuildContext, frozen_version: str) -> dict[str, Path]:
    """Build one RubyGem artifact."""
    _require_single_kind(context.artifacts, "rubygem")
    output = context.output_root / "ruby"
    _mkdir_output_work_dir(output)
    package = output / "package.gem"
    command = [
        shutil.which("gem") or "gem",
        "build",
        context.manifest.name,
        "--output",
        package.as_posix(),
    ]
    _run_checked(command, context.project_root, context.runner)
    produced = {context.artifacts[0].artifact_id: package}
    _validate_package_versions(
        produced,
        context.artifacts,
        frozen_version,
        context.runner,
        context.project_root,
    )
    return produced


def _dotnet_build(
    context: _BuildContext,
) -> dict[str, Path | _ProducedArtifact]:
    """Build .NET package, executable, or Inno Setup artifacts."""
    kinds = {slot.concrete_kind for slot in context.artifacts}
    if kinds <= _DOTNET_PACKAGE_KINDS:
        frozen_version = _frozen_version(context.request)
        return _plain_produced_artifacts(_dotnet_pack(context, frozen_version))
    if kinds == {"executable"}:
        return _dotnet_publish_executable(
            context.request,
            context.artifacts,
            context.manifest,
            context.output_root,
            context.runner,
            context.telemetry,
        )
    if kinds == {"inno-setup"}:
        return _plain_produced_artifacts(_dotnet_inno_setup(context))
    msg = f"unsupported .NET artifact kind set: {sorted(kinds)}"
    raise BuildExecutorError(msg)


def _dotnet_pack(
    context: _BuildContext, frozen_version: str
) -> dict[str, Path]:
    """Run dotnet pack and map NuGet package outputs."""
    output = context.output_root / "dotnet-pack"
    _mkdir_output_work_dir(output)
    command = [
        shutil.which("dotnet") or "dotnet",
        "pack",
        context.manifest.as_posix(),
        "--configuration",
        "Release",
        "--output",
        output.as_posix(),
        "--nologo",
        f"-p:PackageVersion={frozen_version}",
        f"-p:WorkflowReleaseFrozenPackageVersion={frozen_version}",
    ]
    binlog_path = context.telemetry.next_binlog_path("dotnet-pack")
    command.append(f"/bl:{binlog_path.as_posix()}")
    if any(slot.concrete_kind == "snupkg" for slot in context.artifacts):
        command.extend(
            ["-p:IncludeSymbols=true", "-p:SymbolPackageFormat=snupkg"]
        )
    _run_checked(
        command,
        context.manifest.parent,
        context.runner,
        telemetry=context.telemetry,
        phase="dotnet-pack",
        binlog_path=binlog_path,
        output_paths=[output],
    )
    produced = _match_outputs(
        context.artifacts,
        {
            "nuget": tuple(
                path
                for path in output.glob("*.nupkg")
                if not path.name.endswith(".symbols.nupkg")
            ),
            "snupkg": tuple(output.glob("*.snupkg")),
        },
    )
    _validate_package_versions(
        produced,
        context.artifacts,
        frozen_version,
        context.runner,
        context.manifest.parent,
    )
    return produced


def _dotnet_publish_executable(  # noqa: PLR0913
    request: Mapping[str, object],
    artifacts: Sequence[_ArtifactSlot],
    manifest: Path,
    output_root: Path,
    runner: Runner,
    telemetry: _BuildTelemetry,
) -> dict[str, Path | _ProducedArtifact]:
    """Run dotnet publish for one single-file executable artifact."""
    _require_single_kind(artifacts, "executable")
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    rid = dimensions.get("rid")
    if not isinstance(rid, str) or not rid:
        msg = "binary/executable .NET variants must carry dimensions.rid"
        raise BuildExecutorError(msg)
    output = output_root / "dotnet-publish"
    _mkdir_output_work_dir(output)
    project = _mapping(request["project"], "project")
    version_property = f"-p:Version={project['resolved-version']}"
    command = [
        shutil.which("dotnet") or "dotnet",
        "publish",
        manifest.as_posix(),
        "--configuration",
        "Release",
        "--runtime",
        rid,
        "--self-contained",
        "true",
        "--output",
        output.as_posix(),
        "--nologo",
        "-p:PublishSingleFile=true",
        version_property,
    ]
    binlog_path = telemetry.next_binlog_path("dotnet-publish")
    command.append(f"/bl:{binlog_path.as_posix()}")
    _run_checked(
        command,
        manifest.parent,
        runner,
        telemetry=telemetry,
        phase="dotnet-publish",
        binlog_path=binlog_path,
        output_paths=[output],
    )
    companion_patterns = tuple(
        companion.path
        for artifact in artifacts
        for companion in artifact.companions
    )
    candidates = tuple(_executable_candidates(output, rid, companion_patterns))
    produced = _match_outputs(artifacts, {"executable": candidates})
    return _archive_executable_artifacts(produced, artifacts, output, request)


def _dotnet_inno_setup(context: _BuildContext) -> dict[str, Path]:
    """Run project Inno Setup packaging scripts."""
    _require_single_kind(context.artifacts, "inno-setup")
    variant = _mapping(context.request["variant"], "variant")
    project_id = str(variant["project-id"])
    if project_id == "image-occlusion-editor":
        publish_script = (
            context.project_root / "script" / "Publish-ImageOcclusionEditor.ps1"
        )
        publish_root_name = "image-occlusion-publish"
        installer_root_name = "image-occlusion-installer"
    else:
        publish_script = context.project_root / "script" / "Publish.ps1"
        publish_root_name = "inno-setup-publish"
        installer_root_name = "inno-setup-installer"
    installer_script = (
        context.project_root / "script" / "Build-InnoInstaller.ps1"
    )
    if not publish_script.is_file() or not installer_script.is_file():
        msg = "Inno Setup packaging scripts are missing"
        raise BuildExecutorError(msg)
    publish_root = context.output_root / publish_root_name
    installer_output = context.output_root / installer_root_name
    _mkdir_output_work_dir(installer_output)
    _restore_dotnet_tools(context.repo_root, context.runner, context.telemetry)
    pwsh = shutil.which("pwsh") or "pwsh"
    publish_telemetry = context.telemetry.next_script_telemetry_path(
        "inno-publish-script"
    )
    publish_binlog_dir = context.telemetry.next_script_binlog_dir(
        "inno-publish-script"
    )
    _run_checked(
        [
            pwsh,
            "-NoLogo",
            "-File",
            publish_script.as_posix(),
            "-Configuration",
            "Release",
            "-OutputRoot",
            publish_root.as_posix(),
            "-TelemetryOutputPath",
            publish_telemetry.as_posix(),
            "-MsBuildBinlogDirectory",
            publish_binlog_dir.as_posix(),
        ],
        context.repo_root,
        context.runner,
        telemetry=context.telemetry,
        phase="inno-publish-script",
        binlog_dir=publish_binlog_dir,
        output_paths=[publish_root, publish_telemetry],
    )
    installer_telemetry = context.telemetry.next_script_telemetry_path(
        "inno-installer-script"
    )
    installer_command = [
        pwsh,
        "-NoLogo",
        "-File",
        installer_script.as_posix(),
        "-Configuration",
        "Release",
        "-PublishOutputRoot",
        publish_root.as_posix(),
        "-InstallerOutputPath",
        installer_output.as_posix(),
        "-TelemetryOutputPath",
        installer_telemetry.as_posix(),
    ]
    if project_id != "image-occlusion-editor":
        installer_command.extend(
            ["-InstallerFileName", _inno_setup_filename(context.request)]
        )
    _run_checked(
        installer_command,
        context.repo_root,
        context.runner,
        telemetry=context.telemetry,
        phase="inno-installer-script",
        output_paths=[installer_output, installer_telemetry],
    )
    produced = _match_outputs(
        context.artifacts,
        {"inno-setup": tuple(installer_output.glob("*.exe"))},
    )
    _validate_windows_pe_installers(produced)
    return produced


def _restore_dotnet_tools(
    repo_root: Path,
    runner: Runner,
    telemetry: _BuildTelemetry,
) -> None:
    """Restore local .NET tools required by project build scripts."""
    tool_manifest = repo_root / ".config" / "dotnet-tools.json"
    if not tool_manifest.is_file():
        return
    _run_checked(
        [shutil.which("dotnet") or "dotnet", "tool", "restore"],
        repo_root,
        runner,
        telemetry=telemetry,
        phase="dotnet-tool-restore",
    )


def _inno_setup_filename(request: Mapping[str, object]) -> str:
    """Return the planner-frozen GitHub asset name for Inno Setup."""
    project = _mapping(request["project"], "project")
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    return (
        f"{variant['project-id']}-"
        f"{project['resolved-version']}-"
        f"{_variant_token(dimensions)}-setup.exe"
    )


def _validate_windows_pe_installers(produced: Mapping[str, Path]) -> None:
    """Reject placeholder text files masquerading as Inno Setup .exe outputs."""
    for artifact_id, path in produced.items():
        try:
            with path.open("rb") as stream:
                header = stream.read(2)
        except OSError as exc:
            msg = f"Inno Setup installer could not be read: {exc}"
            raise BuildExecutorError(
                msg,
                code="BUILD_OUTPUT_INVALID",
                phase="receipt",
                artifact_id=artifact_id,
            ) from exc
        if header != b"MZ":
            msg = "Inno Setup installer output is not a Windows executable"
            raise BuildExecutorError(
                msg,
                code="BUILD_OUTPUT_INVALID",
                phase="receipt",
                artifact_id=artifact_id,
                details={"path": path.as_posix()},
            )


def _match_outputs(
    artifacts: Sequence[_ArtifactSlot],
    paths_by_kind: Mapping[str, Sequence[Path]],
) -> dict[str, Path]:
    """Map requested artifacts to concrete produced paths."""
    produced: dict[str, Path] = {}
    slots_by_kind: dict[str, list[_ArtifactSlot]] = {}
    for slot in artifacts:
        slots_by_kind.setdefault(slot.concrete_kind, []).append(slot)
    for kind, slots in slots_by_kind.items():
        paths = tuple(
            path for path in paths_by_kind.get(kind, ()) if path.is_file()
        )
        if len(paths) != len(slots):
            msg = (
                f"expected {len(slots)} output(s) for {kind}, "
                f"found {len(paths)}"
            )
            raise BuildExecutorError(msg)
        sorted_slots = sorted(slots, key=lambda item: item.artifact_id)
        for slot, path in zip(sorted_slots, paths, strict=True):
            produced[slot.artifact_id] = path
    return produced


def _archive_executable_artifacts(
    produced: Mapping[str, Path],
    artifacts: Sequence[_ArtifactSlot],
    output: Path,
    request: Mapping[str, object],
) -> dict[str, Path | _ProducedArtifact]:
    """Package executable artifacts plus declared companions as one archive."""
    slots = {slot.artifact_id: slot for slot in artifacts}
    archived: dict[str, Path | _ProducedArtifact] = {}
    for artifact_id, primary in produced.items():
        slot = slots[artifact_id]
        companions = _declared_companion_files(output, primary, slot)
        if not slot.companions:
            archived[artifact_id] = _copy_raw_executable_output(
                primary,
                output / _raw_executable_filename(request),
            )
            continue
        archive_path = output / _executable_archive_filename(request)
        members = [
            (archive_name, path)
            for archive_name, path, _companion in companions
        ]
        members.append((primary.name, primary))
        _write_deterministic_zip(archive_path, members)
        companion_receipts = [
            _archive_member_receipt(
                member_path,
                companion_path,
                role=companion.role,
                required=companion.required,
            )
            for member_path, companion_path, companion in companions
        ]
        archived[artifact_id] = _ProducedArtifact(
            archive_path,
            {
                "archive": {
                    "format": "zip",
                    "primary-executable": _archive_member_receipt(
                        primary.name, primary, role="primary-binary"
                    ),
                    "companions": companion_receipts,
                }
            },
        )
    return archived


def _executable_archive_filename(request: Mapping[str, object]) -> str:
    """Return the planner-frozen GitHub asset name for executable archives."""
    project = _mapping(request["project"], "project")
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    return (
        f"{variant['project-id']}-"
        f"{project['resolved-version']}-"
        f"{_variant_token(dimensions)}.zip"
    )


def _raw_executable_filename(request: Mapping[str, object]) -> str:
    """Return the planner-frozen GitHub asset name for raw executables."""
    project = _mapping(request["project"], "project")
    variant = _mapping(request["variant"], "variant")
    dimensions = _mapping(variant["dimensions"], "variant.dimensions")
    token = _variant_token(dimensions)
    suffix = ".exe" if "windows" in token else ""
    return (
        f"{variant['project-id']}-{project['resolved-version']}-{token}{suffix}"
    )


def _copy_raw_executable_output(source: Path, destination: Path) -> Path:
    """Copy a no-companion executable into the build output."""
    if source.resolve() == destination.resolve():
        return source
    if destination.exists():
        msg = f"raw executable destination already exists: {destination}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={"destination": destination.as_posix()},
        )
    try:
        shutil.copy2(source, destination)
    except OSError as exc:
        msg = f"raw executable output could not be copied: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={
                "source": source.as_posix(),
                "destination": destination.as_posix(),
                "error": str(exc),
            },
        ) from exc
    return destination


def _variant_token(dimensions: Mapping[str, object]) -> str:
    """Return the planner variant token rendered from variant dimensions."""
    if not dimensions:
        return "default"
    return "-".join(str(value) for _, value in sorted(dimensions.items()))


def _declared_companion_files(
    output: Path,
    primary: Path,
    slot: _ArtifactSlot,
) -> list[tuple[str, Path, _ArtifactCompanion]]:
    """Resolve descriptor-declared executable companion files."""
    companions: list[tuple[str, Path, _ArtifactCompanion]] = []
    used_archive_paths = {primary.name}
    for companion in slot.companions:
        matches = sorted(
            path
            for path in _safe_companion_glob(output, companion.path)
            if path.is_file() and path != primary
        )
        if not matches and companion.required:
            msg = (
                f"required executable companion {companion.path!r} "
                f"for artifact {slot.artifact_id!r} was not produced"
            )
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
        if len(matches) > 1:
            msg = (
                f"executable companion {companion.path!r} "
                f"for artifact {slot.artifact_id!r} matched "
                f"{len(matches)} files"
            )
            raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
        for path in matches:
            archive_path = path.name
            if archive_path in used_archive_paths:
                msg = f"duplicate executable archive member {archive_path!r}"
                raise BuildExecutorError(msg, code="BUILD_OUTPUT_INVALID")
            used_archive_paths.add(archive_path)
            companions.append((archive_path, path, companion))
    return companions


def _safe_companion_glob(output: Path, pattern: str) -> tuple[Path, ...]:
    """Return glob matches that remain inside the publish output root."""
    root = output.resolve()
    matches: list[Path] = []
    for path in output.glob(pattern):
        resolved = path.resolve()
        try:
            resolved.relative_to(root)
        except ValueError as exc:
            msg = (
                f"executable companion pattern {pattern!r} matched "
                f"path outside output root: {path}"
            )
            raise BuildExecutorError(
                msg,
                code="BUILD_OUTPUT_INVALID",
                details={
                    "pattern": pattern,
                    "output-root": root.as_posix(),
                    "matched-path": resolved.as_posix(),
                },
            ) from exc
        matches.append(path)
    return tuple(matches)


def _write_deterministic_zip(
    archive_path: Path, members: Sequence[tuple[str, Path]]
) -> None:
    """Write a stable zip archive with root-level members."""
    try:
        with zipfile.ZipFile(
            archive_path,
            "w",
            compression=zipfile.ZIP_DEFLATED,
        ) as archive:
            for archive_name, source in sorted(members):
                info = zipfile.ZipInfo(archive_name)
                info.date_time = (1980, 1, 1, 0, 0, 0)
                mode = source.stat().st_mode & 0o777
                info.external_attr = (stat.S_IFREG | mode) << 16
                archive.writestr(info, source.read_bytes())
    except (OSError, zipfile.BadZipFile) as exc:
        msg = f"executable archive could not be written: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={"path": archive_path.as_posix(), "error": str(exc)},
        ) from exc


def _archive_member_receipt(
    archive_path: str,
    path: Path,
    *,
    role: str,
    required: bool | None = None,
) -> Json:
    """Return hash metadata for one file inside an executable archive."""
    try:
        data = path.read_bytes()
    except OSError as exc:
        msg = f"archive member {path} could not be read for receipt: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={"path": path.as_posix(), "error": str(exc)},
        ) from exc
    receipt: Json = {
        "path": archive_path,
        "sha256": hashlib.sha256(data).hexdigest(),
        "byte-size": len(data),
        "role": role,
    }
    if required is not None:
        receipt["required"] = required
    return receipt


def _artifact_slots(request: Mapping[str, object]) -> tuple[_ArtifactSlot, ...]:
    """Return artifact fulfillment slots from a validated build request."""
    artifacts = _mapping(request["artifacts"], "artifacts")
    slots: list[_ArtifactSlot] = []
    for artifact_id, artifact in artifacts.items():
        entry = _mapping(artifact, f"artifacts.{artifact_id}")
        slots.append(
            _ArtifactSlot(
                artifact_id=artifact_id,
                role=str(entry["role"]),
                kind_family=str(entry["kind-family"]),
                concrete_kind=str(entry["concrete-kind"]),
                companions=_artifact_companions(entry, artifact_id),
                projection=dict(
                    _mapping(entry.get("projection", {}), "projection")
                ),
            )
        )
    return tuple(slots)


def _artifact_companions(
    artifact: Mapping[str, object], artifact_id: str
) -> tuple[_ArtifactCompanion, ...]:
    """Return normalized executable companion declarations."""
    raw = artifact.get("companions", [])
    if not isinstance(raw, Sequence) or isinstance(raw, (str, bytes)):
        msg = f"artifact {artifact_id!r} companions must be an array"
        raise BuildExecutorError(msg, code="BUILD_INVALID_INPUT")
    companions: list[_ArtifactCompanion] = []
    for index, item in enumerate(raw):
        entry = _mapping(item, f"artifacts.{artifact_id}.companions[{index}]")
        companions.append(
            _ArtifactCompanion(
                path=str(entry["path"]),
                role=str(entry["role"]),
                required=bool(entry["required"]),
            )
        )
    return tuple(companions)


def _variant_id_from_request(request: Mapping[str, object]) -> str:
    """Return the single variant-id shared by all requested artifacts."""
    artifacts = _mapping(request["artifacts"], "artifacts")
    variant_ids = {
        str(_mapping(artifact, f"artifacts.{artifact_id}")["variant-id"])
        for artifact_id, artifact in artifacts.items()
    }
    if len(variant_ids) != 1:
        msg = "build request artifacts must resolve to exactly one variant-id"
        raise BuildExecutorError(msg)
    return next(iter(variant_ids))


def _validate_request(request: Mapping[str, object]) -> Mapping[str, object]:
    """Validate and normalize a build request object."""
    try:
        validate_contract(request)
    except ContractValidationError as exc:
        msg = "build request violates the closed contract"
        raise BuildExecutorError(
            msg,
            code="BUILD_INVALID_INPUT",
            phase="validation",
            details={"validation-error": str(exc)},
        ) from exc
    return request


def _frozen_version(request: Mapping[str, object]) -> str:
    """Return the planner-frozen project version."""
    project = _mapping(request["project"], "project")
    return str(project["resolved-version"])


def _materialize_pinned_worktree(
    request: Mapping[str, object],
    repo_root: Path,
    bundle_dir: Path,
    runner: Runner,
    telemetry: _BuildTelemetry,
) -> Path:
    """Materialize the request commit in a detached worktree."""
    result = _run_checked(
        [shutil.which("git") or "git", "rev-parse", "HEAD"],
        repo_root,
        runner,
        code="BUILD_CHECKOUT_FAILED",
        telemetry=telemetry,
        phase="git-rev-parse-head",
    )
    actual = result.stdout.strip()
    expected = str(request["commit-sha"])
    if actual != expected:
        msg = (
            f"checkout HEAD {actual!r} does not match build commit {expected!r}"
        )
        raise BuildExecutorError(
            msg,
            code="BUILD_CHECKOUT_FAILED",
            phase="materialization",
            details={"actual": actual, "expected": expected},
        )
    git_dir_result = _run_checked(
        [shutil.which("git") or "git", "rev-parse", "--git-common-dir"],
        repo_root,
        runner,
        code="BUILD_CHECKOUT_FAILED",
        telemetry=telemetry,
        phase="git-rev-parse-common-dir",
    )
    git_dir_text = git_dir_result.stdout.strip()
    git_dir = Path(git_dir_text)
    if not git_dir.is_absolute():
        git_dir = (repo_root / git_dir).resolve()
    worktree_parent = git_dir / "three-workflow-release-build-worktrees"
    worktree_name = hashlib.sha256(
        f"{bundle_dir}\n{expected}\n{request['plan-id']}".encode()
    ).hexdigest()[:24]
    worktree_path = worktree_parent / worktree_name
    if worktree_path.exists():
        try:
            _remove_tree(worktree_path)
        except OSError as exc:
            msg = f"stale build worktree could not be removed: {exc}"
            raise BuildExecutorError(
                msg,
                code="BUILD_CHECKOUT_FAILED",
                phase="materialization",
                details={"path": worktree_path.as_posix()},
            ) from exc
    try:
        worktree_parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        msg = f"build worktree parent could not be prepared: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_CHECKOUT_FAILED",
            phase="materialization",
            details={
                "path": worktree_parent.as_posix(),
                "operation": "mkdir",
                "error": str(exc),
            },
        ) from exc
    prior_skip_smudge = os.environ.get("GIT_LFS_SKIP_SMUDGE")
    os.environ["GIT_LFS_SKIP_SMUDGE"] = "1"
    try:
        _run_checked(
            [
                shutil.which("git") or "git",
                "worktree",
                "add",
                "--detach",
                "--checkout",
                str(worktree_path),
                expected,
            ],
            repo_root,
            runner,
            code="BUILD_CHECKOUT_FAILED",
            telemetry=telemetry,
            phase="git-worktree-add",
        )
    except BuildExecutorError:
        if worktree_path.exists():
            _try_remove_tree(worktree_path)
        raise
    finally:
        if prior_skip_smudge is None:
            os.environ.pop("GIT_LFS_SKIP_SMUDGE", None)
        else:
            os.environ["GIT_LFS_SKIP_SMUDGE"] = prior_skip_smudge
    return worktree_path.resolve()


def _run_checked(  # noqa: PLR0913
    args: Sequence[str],
    cwd: Path,
    runner: Runner,
    *,
    code: str = "BUILD_FAILED",
    telemetry: _BuildTelemetry | None = None,
    phase: str = "subprocess",
    binlog_path: Path | None = None,
    binlog_dir: Path | None = None,
    output_paths: Sequence[Path] = (),
) -> subprocess.CompletedProcess[str]:
    """Run a subprocess through the injectable runner and require success."""
    timing = _profile_timing_start()
    try:
        result = runner(args, cwd)
    except OSError as exc:
        if telemetry is not None:
            telemetry.record_phase(
                phase,
                timing,
                outcome="failure",
                cwd=cwd,
                argv=args,
                binlog_path=binlog_path,
                binlog_dir=binlog_dir,
                output_paths=output_paths,
                error=str(exc),
            )
        command = " ".join(args[:3])
        msg = f"{command} could not start: {exc}"
        raise BuildExecutorError(
            msg,
            code=code,
            phase=(
                "materialization"
                if code == "BUILD_CHECKOUT_FAILED"
                else "execution"
            ),
            details={
                "command": tuple(args),
                "cwd": cwd.as_posix(),
                "error": str(exc),
            },
        ) from exc
    if telemetry is not None:
        telemetry.record_phase(
            phase,
            timing,
            outcome="success" if result.returncode == 0 else "failure",
            cwd=cwd,
            argv=args,
            exit_code=result.returncode,
            binlog_path=binlog_path,
            binlog_dir=binlog_dir,
            output_paths=output_paths,
        )
    if result.returncode != 0:
        stderr = result.stderr.strip()
        stdout = result.stdout.strip()
        detail = stderr or stdout or f"exit code {result.returncode}"
        command = " ".join(args[:3])
        msg = f"{command} failed: {detail[:2000]}"
        raise BuildExecutorError(
            msg,
            code=code,
            phase="materialization"
            if code == "BUILD_CHECKOUT_FAILED"
            else "execution",
            details={"command": tuple(args), "cwd": cwd.as_posix()},
        )
    return result


def _subprocess_runner(
    args: Sequence[str],
    cwd: Path,
) -> subprocess.CompletedProcess[str]:
    """Run a text subprocess without invoking a shell."""
    return subprocess.run(  # noqa: S603
        list(args),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
    )


def _safe_repo_path(repo_root: Path, value: str) -> Path:
    """Resolve a normalized repository-relative path."""
    candidate = Path(value)
    if (
        candidate.is_absolute()
        or "\\" in value
        or "." in candidate.parts
        or ".." in candidate.parts
    ):
        msg = f"path is not normalized repo-relative: {value!r}"
        raise BuildExecutorError(msg)
    resolved = (repo_root / candidate).resolve()
    try:
        resolved.relative_to(repo_root)
    except ValueError as exc:
        msg = f"path escapes repository root: {value!r}"
        raise BuildExecutorError(msg) from exc
    return resolved


def _mapping(value: object, label: str) -> Mapping[str, object]:
    """Require a mapping after contract validation."""
    if not isinstance(value, Mapping):
        msg = f"{label} must be an object"
        raise BuildExecutorError(msg)
    return value


def _require_single_kind(
    artifacts: Sequence[_ArtifactSlot],
    concrete_kind: str,
) -> None:
    """Require exactly one artifact of *concrete_kind*."""
    if len(artifacts) != 1 or artifacts[0].concrete_kind != concrete_kind:
        msg = f"expected exactly one {concrete_kind} artifact"
        raise BuildExecutorError(msg)


def _validate_npm_pack_json(stdout: str) -> None:
    """Require npm/pnpm pack --json to emit a valid package entry."""
    _extract_npm_pack_json(stdout)


def _extract_npm_pack_json(stdout: str) -> object:
    """Extract the npm/pnpm pack JSON payload from script-noisy stdout."""
    decoder = json.JSONDecoder()
    for offset, char in enumerate(stdout):
        if char not in "[{":
            continue
        with suppress(json.JSONDecodeError):
            payload, _ = decoder.raw_decode(stdout[offset:])
            if _is_npm_pack_payload(payload):
                return payload
    excerpt = _diagnostic_excerpt(stdout)
    msg = (
        "npm pack --json emitted no valid package entries; "
        f"stdout excerpt: {excerpt}"
    )
    raise BuildExecutorError(msg)


def _is_npm_pack_payload(value: object) -> bool:
    """Return whether a decoded value has an npm/pnpm pack result shape."""
    if isinstance(value, list):
        return bool(value) and all(_is_npm_pack_entry(entry) for entry in value)
    return _is_npm_pack_entry(value)


def _is_npm_pack_entry(value: object) -> bool:
    """Return whether a decoded object is a package pack entry."""
    if not isinstance(value, Mapping):
        return False
    filename = value.get("filename")
    if not isinstance(filename, str) or not filename.endswith(".tgz"):
        return False
    return any(
        key in value
        for key in (
            "name",
            "version",
            "integrity",
            "shasum",
            "files",
            "size",
            "unpackedSize",
            "entryCount",
        )
    )


def _diagnostic_excerpt(text: str, *, limit: int = 240) -> str:
    """Return a compact excerpt suitable for error diagnostics."""
    compact = " ".join(text.split())
    if len(compact) <= limit:
        return compact or "<empty>"
    return f"{compact[:limit]}..."


def _validate_package_versions(
    produced: Mapping[str, Path],
    artifacts: Sequence[_ArtifactSlot],
    frozen_version: str,
    runner: Runner,
    cwd: Path,
) -> None:
    """Validate package artifact metadata carries the frozen version."""
    slots = {slot.artifact_id: slot for slot in artifacts}
    for artifact_id, path in produced.items():
        concrete_kind = slots[artifact_id].concrete_kind
        version = _package_artifact_version(path, concrete_kind, runner, cwd)
        if not _versions_match(
            concrete_kind, version, frozen_version, runner, cwd
        ):
            msg = (
                f"{concrete_kind} artifact {artifact_id!r} version {version!r} "
                f"does not match frozen version {frozen_version!r}"
            )
            raise BuildExecutorError(msg)


def _package_artifact_version(
    path: Path,
    concrete_kind: str,
    runner: Runner,
    cwd: Path,
) -> str:
    """Read package version metadata for one artifact kind."""
    if concrete_kind == "wheel":
        return _python_wheel_version(path)
    if concrete_kind == "sdist":
        return _python_sdist_version(path)
    if concrete_kind in _DOTNET_PACKAGE_KINDS:
        return _nuget_package_version(path)
    if concrete_kind == "rubygem":
        return _rubygem_version(path, runner, cwd)
    msg = f"version metadata validation is unsupported for {concrete_kind}"
    raise BuildExecutorError(msg)


def _python_wheel_version(path: Path) -> str:
    """Read the Version field from wheel METADATA."""
    try:
        with zipfile.ZipFile(path) as archive:
            metadata_names = [
                name
                for name in archive.namelist()
                if name.endswith(".dist-info/METADATA")
            ]
            if len(metadata_names) != 1:
                msg = "wheel must contain exactly one .dist-info/METADATA"
                raise BuildExecutorError(msg)
            metadata = archive.read(metadata_names[0]).decode("utf-8")
    except (OSError, zipfile.BadZipFile, UnicodeDecodeError) as exc:
        msg = "wheel metadata could not be inspected"
        raise BuildExecutorError(msg) from exc
    return _core_metadata_version(metadata, "wheel")


def _python_sdist_version(path: Path) -> str:
    """Read the Version field from sdist PKG-INFO."""
    try:
        with tarfile.open(path, "r:gz") as archive:
            members = [
                member
                for member in archive.getmembers()
                if member.isfile() and member.name.endswith("/PKG-INFO")
            ]
            if len(members) != 1:
                msg = "sdist must contain exactly one PKG-INFO"
                raise BuildExecutorError(msg)
            file_obj = archive.extractfile(members[0])
            if file_obj is None:
                msg = "sdist PKG-INFO could not be read"
                raise BuildExecutorError(msg)
            metadata = file_obj.read().decode("utf-8")
    except (OSError, tarfile.TarError, UnicodeDecodeError) as exc:
        msg = "sdist metadata could not be inspected"
        raise BuildExecutorError(msg) from exc
    return _core_metadata_version(metadata, "sdist")


def _core_metadata_version(metadata: str, label: str) -> str:
    """Parse a Python core metadata Version field."""
    version = Parser().parsestr(metadata).get("Version")
    if not version:
        msg = f"{label} metadata is missing Version"
        raise BuildExecutorError(msg)
    return version


def _nuget_package_version(path: Path) -> str:
    """Read the version from the embedded NuGet .nuspec."""
    try:
        with zipfile.ZipFile(path) as archive:
            nuspec_names = [
                name for name in archive.namelist() if name.endswith(".nuspec")
            ]
            if len(nuspec_names) != 1:
                msg = "NuGet package must contain exactly one .nuspec"
                raise BuildExecutorError(msg)
            nuspec = archive.read(nuspec_names[0]).decode("utf-8")
    except (OSError, zipfile.BadZipFile, UnicodeDecodeError) as exc:
        msg = "NuGet package metadata could not be inspected"
        raise BuildExecutorError(msg) from exc
    match = re.search(
        r"<(?:[A-Za-z_][\w.-]*:)?version>([^<]+)</(?:[A-Za-z_][\w.-]*:)?version>",
        nuspec,
    )
    if match is not None:
        return match.group(1).strip()
    msg = "NuGet package metadata is missing version"
    raise BuildExecutorError(msg)


def _versions_match(
    concrete_kind: str,
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare versions using package ecosystem identity rules."""
    if concrete_kind in {"wheel", "sdist"}:
        return _python_versions_match(observed, frozen)
    if concrete_kind in _DOTNET_PACKAGE_KINDS:
        return _nuget_normalized_version(observed) == _nuget_normalized_version(
            frozen
        )
    if concrete_kind == "rubygem":
        return _rubygem_versions_match(observed, frozen, runner, cwd)
    return observed == frozen


def _python_versions_match(observed: str, frozen: str) -> bool:
    """Compare Python package versions by PEP 440 identity."""
    try:
        observed_normalized = normalize_version_field(
            observed,
            field="observed",
        )
        frozen_normalized = normalize_version_field(
            frozen,
            field="frozen",
        )
        return Version(observed_normalized) == Version(frozen_normalized)
    except (InvalidVersion, NbgvVersionNormalizationError) as exc:
        msg = "Python package version is not PEP 440 compatible"
        raise BuildExecutorError(msg) from exc


def _nuget_normalized_version(version: str) -> str:
    """Normalize a NuGet package version for identity comparison."""
    public_version = version.strip().split("+", 1)[0]
    release, separator, prerelease = public_version.partition("-")
    numeric_parts = release.split(".")
    if not 1 <= len(numeric_parts) <= _NUGET_MAX_NUMERIC_PARTS:
        return public_version
    try:
        normalized_numbers = [str(int(part)) for part in numeric_parts]
    except ValueError:
        return public_version
    while len(normalized_numbers) < _NUGET_IDENTITY_NUMERIC_PARTS:
        normalized_numbers.append("0")
    if (
        len(normalized_numbers) == _NUGET_MAX_NUMERIC_PARTS
        and normalized_numbers[3] == "0"
    ):
        normalized_numbers.pop()
    normalized = ".".join(normalized_numbers)
    if not separator:
        return normalized
    prerelease_parts = []
    for part in prerelease.split("."):
        if part.isdecimal():
            prerelease_parts.append(str(int(part)))
        else:
            prerelease_parts.append(part.lower())
    return f"{normalized}-{'.'.join(prerelease_parts)}"


def _rubygem_version(path: Path, runner: Runner, cwd: Path) -> str:
    """Read the version from a built RubyGem using RubyGems."""
    result = _run_checked(
        [
            shutil.which("gem") or "gem",
            "specification",
            path.as_posix(),
            "version",
            "--yaml",
        ],
        cwd,
        runner,
    )
    version = _parse_rubygems_version_yaml(result.stdout)
    if version is None:
        msg = "RubyGem metadata is missing version"
        raise BuildExecutorError(msg)
    return version


def _rubygem_versions_match(
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare RubyGem versions through RubyGems' Gem::Version semantics."""
    script = (
        "observed = Gem::Version.new(ARGV.fetch(0)); "
        "frozen = Gem::Version.new(ARGV.fetch(1)); "
        "puts(observed == frozen ? 'true' : 'false')"
    )
    result = _run_checked(
        [
            shutil.which("ruby") or "ruby",
            "-rrubygems",
            "-e",
            script,
            observed,
            frozen,
        ],
        cwd,
        runner,
    )
    return result.stdout.strip() == "true"


def _parse_rubygems_version_yaml(stdout: str) -> str | None:
    """Parse RubyGems version YAML output."""
    for line in stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith("version:"):
            return stripped.removeprefix("version:").strip().strip("'\"")
        if stripped.startswith("---"):
            scalar = stripped.removeprefix("---").strip()
            if scalar and not scalar.startswith("!"):
                return scalar.strip("'\"")
    return None


def _write_npm_package_json(
    manifest: Path, package_json: Mapping[str, object]
) -> None:
    """Write package.json with stable formatting."""
    try:
        manifest.write_text(
            json.dumps(package_json, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
    except OSError as exc:
        msg = "npm package manifest could not be written"
        raise BuildExecutorError(msg) from exc


def _write_npm_package_json_bytes(manifest: Path, contents: bytes) -> None:
    """Restore package.json from previously captured bytes."""
    try:
        manifest.write_bytes(contents)
    except OSError as exc:
        msg = "npm package manifest could not be written"
        raise BuildExecutorError(msg) from exc


def _read_npm_package_json_bytes(manifest: Path) -> bytes:
    """Read package.json bytes for byte-identical restoration."""
    try:
        return manifest.read_bytes()
    except OSError as exc:
        msg = "npm package manifest could not be read as JSON"
        raise BuildExecutorError(msg) from exc


def _parse_npm_package_json_bytes(contents: bytes) -> Mapping[str, object]:
    """Parse captured package.json bytes."""
    try:
        payload = json.loads(contents.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = "npm package manifest could not be read as JSON"
        raise BuildExecutorError(msg) from exc
    if not isinstance(payload, Mapping):
        msg = "npm package manifest must be a JSON object"
        raise BuildExecutorError(msg)
    return payload


def _load_npm_package_json(manifest: Path) -> Mapping[str, object]:
    """Load and minimally validate the source package.json."""
    try:
        payload = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = "npm package manifest could not be read as JSON"
        raise BuildExecutorError(msg) from exc
    if not isinstance(payload, Mapping):
        msg = "npm package manifest must be a JSON object"
        raise BuildExecutorError(msg)
    return payload


def _read_effective_npm_package_name(manifest: Path) -> str:
    """Read the manifest package name that npm pack is expected to embed."""
    package_json = _load_npm_package_json(manifest)
    package_name = package_json.get("name")
    if not isinstance(package_name, str):
        msg = "npm package manifest name must be a string"
        raise BuildExecutorError(msg)
    return package_name


def _validate_npm_tarball(
    package_path: Path,
    expected_package_name: str,
    frozen_version: str,
    runner: Runner,
    cwd: Path,
) -> None:
    """Fail unless the packed tarball contains declared npm package content."""
    try:
        with tarfile.open(package_path, "r:gz") as archive:
            names = {
                member.name
                for member in archive.getmembers()
                if member.isfile()
            }
            package_member = archive.extractfile("package/package.json")
            if package_member is None:
                msg = "npm tarball is missing package/package.json"
                raise BuildExecutorError(msg)
            packaged_json = json.loads(package_member.read().decode("utf-8"))
    except (
        OSError,
        tarfile.TarError,
        UnicodeDecodeError,
        json.JSONDecodeError,
    ) as exc:
        msg = "npm tarball could not be inspected"
        raise BuildExecutorError(msg) from exc
    if not isinstance(packaged_json, Mapping):
        msg = "npm tarball package.json must be a JSON object"
        raise BuildExecutorError(msg)
    packaged_name = packaged_json.get("name")
    if not isinstance(packaged_name, str):
        msg = "npm tarball package.json name must be a string"
        raise BuildExecutorError(msg)
    if packaged_name != expected_package_name:
        msg = (
            f"npm package name {packaged_name!r} does not match "
            f"effective package name {expected_package_name!r}"
        )
        raise BuildExecutorError(msg)
    packaged_version = packaged_json.get("version")
    if not isinstance(packaged_version, str):
        msg = "npm tarball package.json version must be a string"
        raise BuildExecutorError(msg)
    if not _npm_versions_match(packaged_version, frozen_version, runner, cwd):
        msg = (
            f"npm package version {packaged_version!r} does not match "
            f"frozen version {frozen_version!r}"
        )
        raise BuildExecutorError(msg)

    required = _npm_required_tarball_paths(packaged_json)
    npm_global_root = None
    if any(_is_npm_glob_requirement(requirement) for requirement in required):
        npm_global_root = _npm_global_root(runner, cwd)
    missing = sorted(
        requirement
        for requirement in required
        if not _npm_tarball_contains(
            names, requirement, runner, cwd, npm_global_root
        )
    )
    if missing:
        msg = f"npm tarball is missing declared package content: {missing}"
        raise BuildExecutorError(msg)


def _npm_required_tarball_paths(
    packaged_json: Mapping[str, object],
) -> set[str]:
    """Return package-relative files, directories, and patterns to require."""
    required = {"package.json"}
    files = packaged_json.get("files")
    if isinstance(files, list):
        for item in files:
            if isinstance(item, str) and not item.startswith("!"):
                required.add(_normalize_npm_package_path(item))
    for field in ("main", "module", "types", "typings", "browser"):
        value = packaged_json.get(field)
        if isinstance(value, str):
            required.add(_normalize_npm_package_path(value))
    bin_value = packaged_json.get("bin")
    if isinstance(bin_value, str):
        required.add(_normalize_npm_package_path(bin_value))
    elif isinstance(bin_value, Mapping):
        for value in bin_value.values():
            if isinstance(value, str):
                required.add(_normalize_npm_package_path(value))
    _collect_npm_export_paths(packaged_json.get("exports"), required)
    return required


def _collect_npm_export_paths(value: object, output: set[str]) -> None:
    """Collect string package paths from an npm exports object."""
    if isinstance(value, str):
        output.add(_normalize_npm_package_path(value))
        return
    if isinstance(value, list):
        for item in value:
            _collect_npm_export_paths(item, output)
        return
    if isinstance(value, Mapping):
        for item in value.values():
            _collect_npm_export_paths(item, output)


def _normalize_npm_package_path(value: str) -> str:
    """Normalize an npm package-relative path or glob."""
    normalized = value.strip()
    if normalized.startswith("./"):
        normalized = normalized[2:]
    normalized = normalized.strip("/")
    if (
        normalized == ""
        or normalized.startswith(("../", "/"))
        or "\\" in normalized
    ):
        msg = f"npm package path is not package-relative: {value!r}"
        raise BuildExecutorError(msg)
    return normalized


def _npm_tarball_contains(
    names: set[str],
    requirement: str,
    runner: Runner,
    cwd: Path,
    npm_global_root: str | None,
) -> bool:
    """Return whether a tarball contains a required path or glob."""
    package_requirement = f"package/{requirement}"
    if _is_npm_glob_requirement(requirement):
        if npm_global_root is None:
            msg = "npm files matcher root was not resolved"
            raise BuildExecutorError(msg)
        return _npm_files_glob_matches(
            names, package_requirement, runner, cwd, npm_global_root
        )
    return package_requirement in names or any(
        name.startswith(f"{package_requirement}/") for name in names
    )


def _is_npm_glob_requirement(requirement: str) -> bool:
    """Return whether an npm files entry needs npm-compatible glob matching."""
    return any(char in requirement for char in ("*", "?", "[", "{", "("))


def _npm_global_root(runner: Runner, cwd: Path) -> str:
    """Resolve npm's global root for loading npm's minimatch."""
    result = _run_checked(
        [shutil.which("npm") or "npm", "root", "-g"],
        cwd,
        runner,
    )
    root = result.stdout.strip()
    if root == "":
        msg = "npm global root was empty"
        raise BuildExecutorError(msg)
    return root


def _npm_versions_match(
    observed: str,
    frozen: str,
    runner: Runner,
    cwd: Path,
) -> bool:
    """Compare npm package versions using node-semver identity semantics."""
    if observed == frozen:
        return True
    npm_global_root = _npm_global_root(runner, cwd)
    script = """
const path = require("node:path");
const payload = JSON.parse(process.argv[1]);
const candidates = [
  "semver",
  path.join(payload.npmGlobalRoot, "npm", "node_modules", "semver"),
];
let semver;
for (const candidate of candidates) {
  try {
    semver = require(candidate);
    break;
  } catch {}
}
if (!semver) {
  process.exit(2);
}
const observed = semver.clean(payload.observed);
const frozen = semver.clean(payload.frozen);
if (observed === null || frozen === null) {
  process.stdout.write("false");
} else {
  process.stdout.write(semver.eq(observed, frozen) ? "true" : "false");
}
"""
    payload = json.dumps(
        {
            "observed": observed,
            "frozen": frozen,
            "npmGlobalRoot": npm_global_root,
        },
        separators=(",", ":"),
    )
    result = _run_checked(
        [shutil.which("node") or "node", "-e", script, payload],
        cwd,
        runner,
    )
    output = result.stdout.strip()
    if output == "true":
        return True
    if output == "false":
        return False
    msg = f"npm semver matcher returned invalid output: {output!r}"
    raise BuildExecutorError(msg)


def _npm_files_glob_matches(
    names: set[str],
    pattern: str,
    runner: Runner,
    cwd: Path,
    npm_global_root: str,
) -> bool:
    """Match npm files globs with npm's bundled minimatch implementation."""
    script = """
const path = require("node:path");
const payload = JSON.parse(process.argv[1]);
const candidates = [
  "minimatch",
  path.join(payload.npmGlobalRoot, "npm", "node_modules", "minimatch"),
];
let minimatchModule;
for (const candidate of candidates) {
  try {
    minimatchModule = require(candidate);
    break;
  } catch {}
}
if (!minimatchModule) {
  process.exit(2);
}
const minimatch = minimatchModule.minimatch || minimatchModule;
if (typeof minimatch !== "function") {
  process.exit(3);
}
const options = { dot: true };
const matched = payload.names.some((name) =>
  minimatch(name, payload.pattern, options)
);
process.stdout.write(matched ? "true" : "false");
"""
    payload = json.dumps(
        {
            "names": sorted(_npm_match_candidate_names(names)),
            "pattern": pattern,
            "npmGlobalRoot": npm_global_root,
        },
        separators=(",", ":"),
    )
    result = _run_checked(
        [shutil.which("node") or "node", "-e", script, payload],
        cwd,
        runner,
    )
    output = result.stdout.strip()
    if output == "true":
        return True
    if output == "false":
        return False
    msg = f"npm files matcher returned invalid output: {output!r}"
    raise BuildExecutorError(msg)


def _npm_match_candidate_names(names: set[str]) -> set[str]:
    """Return tarball file paths plus ancestor directories for files globs."""
    candidates = set(names)
    for name in names:
        parts = name.split("/")
        for index in range(1, len(parts)):
            candidates.add("/".join(parts[:index]))
    return candidates


def _executable_candidates(
    output: Path, rid: str, companion_patterns: Sequence[str] = ()
) -> list[Path]:
    """Return candidate single-file executable outputs."""
    if rid.startswith("win-"):
        return [
            path
            for path in output.glob("*.exe")
            if path.is_file()
            and not _matches_any_companion(path.name, companion_patterns)
        ]
    candidates: list[Path] = []
    ignored_suffixes = {
        ".config",
        ".deps",
        ".dll",
        ".json",
        ".pdb",
        ".runtimeconfig",
        ".xml",
    }
    for path in output.iterdir():
        if (
            not path.is_file()
            or path.suffix in ignored_suffixes
            or _matches_any_companion(path.name, companion_patterns)
        ):
            continue
        if path.suffix == "":
            candidates.append(path)
            continue
        mode = path.stat().st_mode
        if mode & (stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH):
            candidates.append(path)
    return candidates


def _matches_any_companion(name: str, patterns: Sequence[str]) -> bool:
    """Return whether an output name matches a companion declaration."""
    return any(fnmatchcase(name, pattern) for pattern in patterns)


def _copy_to_dist(source: Path, dist_dir: Path) -> Path:
    """Copy one produced artifact into the receipted dist directory."""
    safe_name = _safe_filename(source.name)
    destination = dist_dir / safe_name
    try:
        if destination.exists():
            stem = destination.stem
            suffix = "".join(destination.suffixes)
            digest = hashlib.sha256(
                source.as_posix().encode("utf-8")
            ).hexdigest()[:8]
            destination = dist_dir / f"{stem}-{digest}{suffix}"
        shutil.copy2(source, destination)
    except OSError as exc:
        msg = f"artifact {source} could not be copied to bundle dist: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={
                "source": source.as_posix(),
                "destination": destination.as_posix(),
                "error": str(exc),
            },
        ) from exc
    return destination


def _safe_filename(name: str) -> str:
    """Return a conservative filename for bundle materialization."""
    sanitized = re.sub(r"[^A-Za-z0-9._+-]", "-", name)
    sanitized = sanitized.strip(".")
    return sanitized or "artifact"


def _artifact_receipt(
    path: Path, relative: str, extra: Mapping[str, object] | None = None
) -> Json:
    """Return the closed receipt entry for one file."""
    try:
        data = path.read_bytes()
    except OSError as exc:
        msg = f"artifact {path} could not be read for receipt: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={"path": path.as_posix(), "error": str(exc)},
        ) from exc
    receipt: Json = {
        "bundle-relative-path": relative,
        "sha256": hashlib.sha256(data).hexdigest(),
        "byte-size": len(data),
    }
    if extra:
        receipt.update(extra)
    return receipt


def _remove_tree(path: Path) -> None:
    """Remove a directory tree without following symlinked directories."""
    if path.is_symlink() or not path.is_dir():
        path.unlink()
        return
    for child in path.iterdir():
        _remove_tree(child)
    path.rmdir()


def _try_remove_tree(path: Path) -> None:
    """Best-effort directory removal for cleanup paths."""
    with suppress(OSError):
        _remove_tree(path)
