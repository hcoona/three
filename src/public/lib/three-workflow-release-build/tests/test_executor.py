"""Tests for workflow-release build executors."""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import zipfile
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

import pytest
from three_workflow_release_build import (
    BuildExecutorError,
    build_diagnostics_document,
    execute_build,
)
from three_workflow_release_build import executor as executor_module
from three_workflow_release_build.cli import main as cli_main
from three_workflow_release_contracts import validate_contract

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

REPO_ROOT = Path(__file__).parents[5]
SHA = "a" * 40
TIMING_DURATION_TOLERANCE_MS = 1000
POWERSHELL_TIMING_MIN_DURATION_MS = 25


def _request(  # noqa: PLR0913
    scratch: Path,
    *,
    ecosystem: str,
    artifacts: Mapping[str, tuple[str, str, str]],
    dimensions: Mapping[str, str] | None = None,
    project_id: str = "example",
    resolved_version: str = "1.2.3",
    companions: Mapping[str, Sequence[Mapping[str, object]]] | None = None,
    projections: Mapping[str, Mapping[str, object]] | None = None,
) -> dict[str, Any]:
    """Create one closed build request rooted under scratch."""
    release_root = scratch / project_id
    release_root.mkdir(parents=True, exist_ok=True)
    manifest_name = {
        "dotnet": "Example.csproj",
        "python": "pyproject.toml",
        "node": "package.json",
        "ruby": "example.gemspec",
    }[ecosystem]
    manifest = release_root / manifest_name
    manifest.write_text("", encoding="utf-8")
    artifact_ids = list(artifacts)
    request = {
        "api-version": "three.release.build-request/v1alpha1",
        "kind": "build-request",
        "plan-id": "plan/example",
        "profile": "buddy",
        "commit-sha": SHA,
        "project": {
            "display-name": "Example",
            "ecosystem": ecosystem,
            "release-kind": "lib",
            "descriptor-path": (release_root / "three.release.yml")
            .relative_to(REPO_ROOT)
            .as_posix(),
            "release-root": release_root.relative_to(REPO_ROOT).as_posix(),
            "resolved-version": resolved_version,
            "source": {
                "primary-manifest-path": manifest.relative_to(
                    REPO_ROOT
                ).as_posix(),
                "auxiliary-input-paths": [],
                "version-authority-kind": "build-system-nbgv",
            },
            "variant-ids": ["variant/package"],
            "publish-node-ids": ["publish-node/example"],
        },
        "variant": {
            "project-id": project_id,
            "descriptor-handle": "package",
            "dimensions": dict(dimensions or {}),
            "artifact-ids": artifact_ids,
        },
        "artifacts": {
            artifact_id: _artifact_request_entry(
                artifact_id,
                project_id,
                role,
                kind_family,
                concrete_kind,
                companions,
                projections,
            )
            for artifact_id, (
                role,
                kind_family,
                concrete_kind,
            ) in artifacts.items()
        },
    }
    validate_contract(request)
    return request


def _artifact_request_entry(  # noqa: PLR0913
    artifact_id: str,
    project_id: str,
    role: str,
    kind_family: str,
    concrete_kind: str,
    companions: Mapping[str, Sequence[Mapping[str, object]]] | None,
    projections: Mapping[str, Mapping[str, object]] | None,
) -> dict[str, Any]:
    """Create one build-request artifact entry."""
    entry: dict[str, Any] = {
        "project-id": project_id,
        "variant-id": "variant/package",
        "descriptor-handle": artifact_id.rsplit("/", 1)[-1],
        "role": role,
        "kind-family": kind_family,
        "concrete-kind": concrete_kind,
        "produced-from-artifact-ids": [],
    }
    if companions and artifact_id in companions:
        entry["companions"] = [dict(item) for item in companions[artifact_id]]
    if projections and artifact_id in projections:
        entry["projection"] = dict(projections[artifact_id])
    return entry


def test_python_executor_receipts_wheel_and_sdist() -> None:
    """Build Python distributions and emit a closed build-result."""
    scratch = REPO_ROOT / ".build-executor-python-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
                "artifact/sdist": ("primary-package", "package", "sdist"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            _write_python_sdist(out_dir / "example-1.2.3.tar.gz", "1.2.3")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipts = _result_artifacts(result)
        assert set(receipts) == {"artifact/wheel", "artifact/sdist"}
        assert {
            Path(receipt["bundle-relative-path"]).name
            for receipt in receipts.values()
            if isinstance(receipt, dict)
        } == {"example-1.2.3-py3-none-any.whl", "example-1.2.3.tar.gz"}
    finally:
        _remove_tree_scratch(scratch)


def test_receipt_copy_os_error_becomes_output_invalid(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Wrap artifact copy filesystem errors as receipt diagnostics errors."""
    scratch = REPO_ROOT / ".build-executor-copy-oserror-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        def fail_copy2(
            _source: Path,
            _destination: Path,
        ) -> None:
            message = "copy failed"
            raise OSError(message)

        monkeypatch.setattr(
            "three_workflow_release_build.executor.shutil.copy2",
            fail_copy2,
        )

        with pytest.raises(
            BuildExecutorError, match="could not be copied"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
        assert error.value.phase == "receipt"
        diagnostics = cast(
            "Mapping[str, Any]",
            build_diagnostics_document(error.value, request=request),
        )
        validate_contract(diagnostics)
    finally:
        _remove_tree_scratch(scratch)


def test_receipt_read_os_error_becomes_output_invalid(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Wrap artifact receipt read errors as receipt diagnostics errors."""
    scratch = REPO_ROOT / ".build-executor-receipt-read-oserror-test"
    _remove_tree_scratch(scratch)
    original_read_bytes = Path.read_bytes
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        def fail_receipt_read(path: Path) -> bytes:
            if (
                path.name == "example-1.2.3-py3-none-any.whl"
                and path.parent.name == "dist"
            ):
                message = "read failed"
                raise OSError(message)
            return original_read_bytes(path)

        monkeypatch.setattr(Path, "read_bytes", fail_receipt_read)

        with pytest.raises(
            BuildExecutorError, match="could not be read"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
        assert error.value.phase == "receipt"
        diagnostics = cast(
            "Mapping[str, Any]",
            build_diagnostics_document(error.value, request=request),
        )
        validate_contract(diagnostics)
    finally:
        _remove_tree_scratch(scratch)


def test_cli_writes_diagnostics_when_bundle_mkdir_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Bundle preparation filesystem errors emit closed diagnostics."""
    scratch = REPO_ROOT / ".build-executor-cli-bundle-mkdir-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        blocking_parent = scratch / "bundle-parent"
        blocking_parent.write_text("not a directory", encoding="utf-8")
        request_path.write_text(json.dumps(request), encoding="utf-8")
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(blocking_parent / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["code"] == "BUILD_OUTPUT_INVALID"
        assert diagnostic["phase"] == "receipt"
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


@pytest.mark.parametrize(
    ("failing_name", "message"),
    [
        ("_executor-work", "work mkdir failed"),
        ("dist", "dist mkdir failed"),
    ],
)
def test_bundle_work_dir_mkdir_os_error_becomes_output_invalid(
    monkeypatch: pytest.MonkeyPatch,
    failing_name: str,
    message: str,
) -> None:
    """Work/dist mkdir failures are diagnostic-safe output errors."""
    scratch = REPO_ROOT / ".build-executor-work-dir-mkdir-failure-test"
    _remove_tree_scratch(scratch)
    original_mkdir = Path.mkdir
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        failing_path = scratch / "bundle" / failing_name

        def fail_target_mkdir(
            path: Path,
            mode: int = 0o777,
            *,
            parents: bool = False,
            exist_ok: bool = False,
        ) -> None:
            if path == failing_path:
                raise OSError(message)
            original_mkdir(path, mode=mode, parents=parents, exist_ok=exist_ok)

        monkeypatch.setattr(Path, "mkdir", fail_target_mkdir)

        with pytest.raises(BuildExecutorError, match="directory") as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=lambda args, _cwd: subprocess.CompletedProcess(
                    args, 0, "", ""
                ),
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
        assert error.value.phase == "receipt"
    finally:
        _remove_tree_scratch(scratch)


@pytest.mark.parametrize(
    "case",
    [
        {
            "ecosystem": "python",
            "artifacts": {
                "artifact/wheel": ("primary-package", "package", "wheel")
            },
            "output_name": "python",
            "project_id": "example",
        },
        {
            "ecosystem": "node",
            "artifacts": {
                "artifact/npm": ("primary-package", "package", "npm-package")
            },
            "output_name": "node",
            "project_id": "example",
        },
        {
            "ecosystem": "ruby",
            "artifacts": {
                "artifact/gem": ("primary-package", "package", "rubygem")
            },
            "output_name": "ruby",
            "project_id": "example",
        },
        {
            "ecosystem": "dotnet",
            "artifacts": {
                "artifact/nuget": ("primary-package", "package", "nuget")
            },
            "output_name": "dotnet-pack",
            "project_id": "example",
        },
        {
            "ecosystem": "dotnet",
            "artifacts": {
                "artifact/exe": ("primary-binary", "binary", "executable")
            },
            "output_name": "dotnet-publish",
            "dimensions": {"rid": "linux-x64"},
            "project_id": "example",
        },
        {
            "ecosystem": "dotnet",
            "artifacts": {
                "artifact/installer": (
                    "installer",
                    "installer",
                    "inno-setup",
                )
            },
            "output_name": "image-occlusion-installer",
            "dimensions": {"rid": "win-x64"},
            "project_id": "image-occlusion-editor",
        },
    ],
)
def test_ecosystem_output_dir_mkdir_os_error_becomes_output_invalid(
    monkeypatch: pytest.MonkeyPatch,
    case: Mapping[str, Any],
) -> None:
    """Ecosystem-specific output mkdir failures are closed diagnostics."""
    ecosystem = cast("str", case["ecosystem"])
    artifacts = cast("Mapping[str, tuple[str, str, str]]", case["artifacts"])
    output_name = cast("str", case["output_name"])
    dimensions = cast("Mapping[str, str] | None", case.get("dimensions"))
    project_id = cast("str", case["project_id"])
    scratch = REPO_ROOT / f".build-executor-{output_name}-mkdir-failure-test"
    _remove_tree_scratch(scratch)
    original_mkdir = Path.mkdir
    try:
        request = _request(
            scratch,
            ecosystem=ecosystem,
            artifacts=artifacts,
            dimensions=dimensions,
            project_id=project_id,
        )
        project_root = scratch / project_id
        if ecosystem == "node":
            (project_root / "package.json").write_text(
                json.dumps(
                    {
                        "name": "example",
                        "version": "1.2.3",
                        "scripts": {"build": "node build.mjs"},
                    }
                ),
                encoding="utf-8",
            )
        if output_name == "image-occlusion-installer":
            script_dir = project_root / "script"
            script_dir.mkdir()
            (script_dir / "Publish-ImageOcclusionEditor.ps1").write_text(
                "", encoding="utf-8"
            )
            (script_dir / "Build-InnoInstaller.ps1").write_text(
                "", encoding="utf-8"
            )
        failing_path = scratch / "bundle" / "_executor-work" / output_name

        def fail_ecosystem_output_mkdir(
            path: Path,
            mode: int = 0o777,
            *,
            parents: bool = False,
            exist_ok: bool = False,
        ) -> None:
            if path == failing_path:
                message = "ecosystem output mkdir failed"
                raise OSError(message)
            original_mkdir(path, mode=mode, parents=parents, exist_ok=exist_ok)

        monkeypatch.setattr(Path, "mkdir", fail_ecosystem_output_mkdir)

        with pytest.raises(
            BuildExecutorError, match="output directory"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=lambda args, _cwd: subprocess.CompletedProcess(
                    args, 0, "", ""
                ),
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
        assert error.value.phase == "receipt"
        assert error.value.details["operation"] == "mkdir"
        assert error.value.details["path"] == failing_path.as_posix()
        diagnostics = cast(
            "Mapping[str, Any]",
            build_diagnostics_document(error.value, request=request),
        )
        validate_contract(diagnostics)
    finally:
        _remove_tree_scratch(scratch)


def test_bundle_work_dir_remove_os_error_becomes_output_invalid(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Stale work directory cleanup failures are diagnostic-safe."""
    scratch = REPO_ROOT / ".build-executor-work-dir-remove-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        output_root = scratch / "bundle" / "_executor-work"
        output_root.mkdir(parents=True)

        def fail_remove(path: Path) -> None:
            if path == output_root:
                message = "remove failed"
                raise OSError(message)
            _remove_tree(path)

        monkeypatch.setattr(
            "three_workflow_release_build.executor._remove_tree",
            fail_remove,
        )

        with pytest.raises(BuildExecutorError, match="directory") as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=lambda args, _cwd: subprocess.CompletedProcess(
                    args, 0, "", ""
                ),
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
        assert error.value.phase == "receipt"
    finally:
        _remove_tree_scratch(scratch)


def test_worktree_parent_mkdir_os_error_becomes_checkout_failed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Pinned worktree parent mkdir failures are materialization errors."""
    scratch = REPO_ROOT / ".build-executor-worktree-parent-mkdir-failure-test"
    _remove_tree_scratch(scratch)
    original_mkdir = Path.mkdir
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        worktree_parent = (
            scratch / "git-common" / "three-workflow-release-build-worktrees"
        )

        def fail_worktree_parent_mkdir(
            path: Path,
            mode: int = 0o777,
            *,
            parents: bool = False,
            exist_ok: bool = False,
        ) -> None:
            if path == worktree_parent:
                message = "worktree parent mkdir failed"
                raise OSError(message)
            original_mkdir(path, mode=mode, parents=parents, exist_ok=exist_ok)

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        monkeypatch.setattr(Path, "mkdir", fail_worktree_parent_mkdir)

        with pytest.raises(
            BuildExecutorError, match="worktree parent"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
            )
        assert error.value.code == "BUILD_CHECKOUT_FAILED"
        assert error.value.phase == "materialization"
    finally:
        _remove_tree_scratch(scratch)


def test_execution_runner_os_error_becomes_build_failed() -> None:
    """Wrap non-materialization runner startup failures as build failures."""
    scratch = REPO_ROOT / ".build-executor-oserror-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            raise FileNotFoundError(args[0], cwd)

        with pytest.raises(BuildExecutorError) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert error.value.code == "BUILD_FAILED"
        assert error.value.phase == "execution"
        assert error.value.details["cwd"]
    finally:
        _remove_tree_scratch(scratch)


def test_build_worktree_materialization_skips_lfs_smudge(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Pinned build worktrees must not download unrelated LFS payloads."""
    scratch = REPO_ROOT / ".build-executor-lfs-smudge-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        monkeypatch.delenv("GIT_LFS_SKIP_SMUDGE", raising=False)
        observed_skip_values: list[str | None] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                observed_skip_values.append(
                    os.environ.get("GIT_LFS_SKIP_SMUDGE")
                )
                return subprocess.CompletedProcess(
                    args, 1, "", "checkout failed"
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
            )

        assert observed_skip_values == ["1"]
        assert os.environ.get("GIT_LFS_SKIP_SMUDGE") is None
    finally:
        _remove_tree_scratch(scratch)


def test_python_executor_fails_on_version_mismatch() -> None:
    """Reject Python packages whose metadata version differs."""
    scratch = REPO_ROOT / ".build-executor-python-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-9.9.9-py3-none-any.whl", "9.9.9"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="frozen version"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_python_executor_accepts_pep440_equivalent_versions() -> None:
    """Accept PEP 440-equivalent Python package versions."""
    scratch = REPO_ROOT / ".build-executor-python-normalized-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
                "artifact/sdist": ("primary-package", "package", "sdist"),
            },
        )
        request["project"]["resolved-version"] = "v1.0"

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.0.0-py3-none-any.whl", "1.0.0"
            )
            _write_python_sdist(out_dir / "example-1.0.0.tar.gz", "1.0.0")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {
            "artifact/wheel",
            "artifact/sdist",
        }
    finally:
        _remove_tree_scratch(scratch)


def test_python_executor_accepts_normalized_nbgv_semver_version() -> None:
    """Accept Python PEP 440 metadata matching an NBGV SemVer2 version."""
    scratch = REPO_ROOT / ".build-executor-python-nbgv-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request["project"]["resolved-version"] = "1.0.0-beta.256.gc482c26"

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.0.0b256+gc482c26-py3-none-any.whl",
                "1.0.0b256+gc482c26",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/wheel"}
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_pack_uses_frozen_version_and_symbols() -> None:
    """Run dotnet pack with frozen version and symbol package output."""
    scratch = REPO_ROOT / ".build-executor-dotnet-pack-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
                "artifact/snupkg": ("symbols", "package", "snupkg"),
            },
        )
        calls: list[tuple[str, ...]] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append(tuple(args))
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(out_dir / "Example.1.2.3.nupkg", "1.2.3")
            _write_nuget_package(out_dir / "Example.1.2.3.snupkg", "1.2.3")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls[0][1] == "pack"
        assert "-p:IncludeSymbols=true" in calls[0]
        assert "-p:SymbolPackageFormat=snupkg" in calls[0]
        assert "-p:PackageVersion=1.2.3" in calls[0]
        assert "-p:WorkflowReleaseFrozenPackageVersion=1.2.3" in calls[0]
        binlog_args = [arg for arg in calls[0] if arg.startswith("/bl:")]
        assert len(binlog_args) == 1
        assert "/_profile/runs/" in binlog_args[0]
        assert binlog_args[0].endswith("/binlogs/0001-dotnet-pack.binlog")
        assert set(_result_artifacts(result)) == {
            "artifact/nuget",
            "artifact/snupkg",
        }
        telemetry = json.loads(
            (scratch / "bundle/release-build-profile-telemetry.json").read_text(
                encoding="utf-8",
            ),
        )
        dotnet_pack = [
            phase
            for phase in telemetry["phases"]
            if phase["phase"] == "dotnet-pack"
        ]
        assert len(dotnet_pack) == 1
        assert dotnet_pack[0]["argv"] == list(calls[0])
        assert "/_profile/runs/" in dotnet_pack[0]["binlog-path"]
        assert dotnet_pack[0]["binlog-path"].endswith(
            "/binlogs/0001-dotnet-pack.binlog",
        )
        assert "/_profile/runs/" in telemetry["profile-root"]
    finally:
        _remove_tree_scratch(scratch)


def test_profile_telemetry_uses_unique_run_roots_for_repeated_builds() -> None:
    """Repeated bundle executions use unique roots."""
    scratch = REPO_ROOT / ".build-executor-profile-run-root-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(out_dir / "Example.1.2.3.nupkg", "1.2.3")
            return subprocess.CompletedProcess(args, 0, "", "")

        roots: list[str] = []
        for _ in range(2):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
            telemetry_path = (
                scratch / "bundle/release-build-profile-telemetry.json"
            )
            telemetry = json.loads(
                telemetry_path.read_text(
                    encoding="utf-8",
                ),
            )
            roots.append(str(telemetry["profile-root"]))
            dotnet_pack = next(
                phase
                for phase in telemetry["phases"]
                if phase["phase"] == "dotnet-pack"
            )
            assert dotnet_pack["binlog-path"].endswith(
                "/binlogs/0001-dotnet-pack.binlog",
            )
            assert str(dotnet_pack["binlog-path"]).startswith(roots[-1])

        assert roots[0] != roots[1]
        assert Path(roots[0]).is_dir()
        assert Path(roots[1]).is_dir()
    finally:
        _remove_tree_scratch(scratch)


def test_profile_timing_corrects_contract_invalid_clock_drift(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Profile phase timing cannot emit 1-5s wall/monotonic drift."""
    started_at = datetime(2026, 6, 26, 5, 0, 0, tzinfo=UTC)
    wall_completed_at = started_at + timedelta(milliseconds=4500)

    class DriftedDatetime(datetime):
        @classmethod
        def now(cls, tz: Any = None) -> datetime:
            del cls, tz
            return wall_completed_at

    monkeypatch.setattr(executor_module, "datetime", DriftedDatetime)
    monkeypatch.setattr(
        executor_module.time,
        "perf_counter_ns",
        lambda: 1_500_000_000,
    )

    timing = executor_module._profile_timing_finish((started_at, 0))  # noqa: SLF001

    assert timing == {
        "started-at": "2026-06-26T05:00:00.000Z",
        "completed-at": "2026-06-26T05:00:01.500Z",
        "duration-ms": 1500,
    }


def test_dotnet_pack_excludes_legacy_symbols_nupkg_from_primary_package() -> (
    None
):
    """Do not classify legacy .symbols.nupkg files as primary NuGet packages."""
    scratch = REPO_ROOT / ".build-executor-dotnet-legacy-symbols-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(
                out_dir / "Example.1.2.3.symbols.nupkg", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(
            BuildExecutorError, match=r"expected 1 output.*found 0"
        ):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_pack_fails_on_version_mismatch() -> None:
    """Reject NuGet packages whose nuspec version differs."""
    scratch = REPO_ROOT / ".build-executor-dotnet-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(out_dir / "Example.9.9.9.nupkg", "9.9.9")
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="frozen version"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_pack_accepts_nuget_normalized_version_match() -> None:
    """Accept NuGet-normalized package versions for frozen version checks."""
    scratch = REPO_ROOT / ".build-executor-dotnet-normalized-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )
        project = cast("dict[str, Any]", request["project"])
        project["resolved-version"] = "01.02.03.0+build.7"

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(out_dir / "Example.1.2.3.nupkg", "1.2.3")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/nuget"}
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_pack_passes_planner_semver2_prerelease_version() -> None:
    """Pass the frozen planner SemVer2 identity as NuGet PackageVersion."""
    scratch = REPO_ROOT / ".build-executor-dotnet-nbgv-semver2-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
            resolved_version="1.0.0-beta.253.gac1659d",
        )
        calls: list[tuple[str, ...]] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append(tuple(args))
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(
                out_dir / "Example.1.0.0-beta.253.gac1659d.nupkg",
                "1.0.0-beta.253.gac1659d",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert "-p:PackageVersion=1.0.0-beta.253.gac1659d" in calls[0]
        assert (
            "-p:WorkflowReleaseFrozenPackageVersion=1.0.0-beta.253.gac1659d"
            in calls[0]
        )
        assert "--version" not in calls[0]
        assert set(_result_artifacts(result)) == {"artifact/nuget"}
    finally:
        _remove_tree_scratch(scratch)


@pytest.mark.parametrize(
    ("frozen_version", "package_version"),
    [
        ("1", "1.0.0"),
        ("1.0", "1.0.0.0"),
        ("1.0.0", "1"),
        ("1.0.0.0", "1.0"),
        ("1.0-alpha.01+build.7", "1.0.0-alpha.1"),
    ],
)
def test_dotnet_pack_accepts_nuget_equivalent_versions(
    frozen_version: str,
    package_version: str,
) -> None:
    """Accept NuGet-equivalent version identities."""
    scratch = REPO_ROOT / ".build-executor-dotnet-equivalent-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )
        project = cast("dict[str, Any]", request["project"])
        project["resolved-version"] = frozen_version

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(
                out_dir / "Example.1.0.0.nupkg", package_version
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/nuget"}
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_pack_rejects_different_nuget_prerelease_version() -> None:
    """Reject genuinely different NuGet prerelease identities."""
    scratch = REPO_ROOT / ".build-executor-dotnet-different-prerelease-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            artifacts={
                "artifact/nuget": ("primary-package", "package", "nuget"),
            },
        )
        project = cast("dict[str, Any]", request["project"])
        project["resolved-version"] = "1.0.0-alpha.1"

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            _write_nuget_package(
                out_dir / "Example.1.0.0.nupkg", "1.0.0-beta.1"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="frozen version"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_requires_one_single_file_candidate() -> None:
    """Run dotnet publish and receipt exactly one executable file."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
        )
        calls: list[tuple[str, ...]] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append(tuple(args))
            out_dir = Path(args[args.index("--output") + 1])
            exe = out_dir / "example"
            exe.write_bytes(b"binary")
            exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
            (out_dir / "example.dll").write_bytes(b"dll")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert "-p:PublishTrimmed=false" not in calls[0]
        assert any(
            "/_profile/runs/" in arg
            and arg.endswith("/binlogs/0001-dotnet-publish.binlog")
            for arg in calls[0]
            if arg.startswith("/bl:")
        )
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        assert receipt["bundle-relative-path"] == "dist/example-1.2.3-linux-x64"
        assert not (scratch / "bundle/dist/example").exists()
        staged = scratch / "bundle/dist/example-1.2.3-linux-x64"
        assert staged.read_bytes() == b"binary"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_accepts_extensionless_non_windows_candidate() -> (
    None
):
    """Receipt cross-RID extensionless executables without Unix mode bits."""
    scratch = REPO_ROOT / ".build-executor-dotnet-cross-rid-exe-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            (out_dir / "example").write_bytes(b"binary")
            (out_dir / "example.dll").write_bytes(b"dll")
            (out_dir / "example.deps.json").write_bytes(b"deps")
            (out_dir / "example.runtimeconfig.json").write_bytes(b"config")
            (out_dir / "example.pdb").write_bytes(b"pdb")
            (out_dir / "example.xml").write_bytes(b"xml")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        assert receipt["bundle-relative-path"] == "dist/example-1.2.3-linux-x64"
        assert not (scratch / "bundle/dist/example").exists()
        staged = scratch / "bundle/dist/example-1.2.3-linux-x64"
        assert staged.read_bytes() == b"binary"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_windows_raw_asset_uses_planner_name() -> None:
    """Receipt Windows no-companion executables under frozen asset names."""
    scratch = REPO_ROOT / ".build-executor-dotnet-windows-exe-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"os": "windows", "rid": "win-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            (out_dir / "RawExecutable.exe").write_bytes(b"MZbinary")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        assert receipt["bundle-relative-path"] == (
            "dist/example-1.2.3-windows-win-x64.exe"
        )
        assert not (scratch / "bundle/dist/RawExecutable.exe").exists()
        assert (
            scratch / "bundle/dist/example-1.2.3-windows-win-x64.exe"
        ).read_bytes() == b"MZbinary"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_archives_declared_companions() -> None:
    """Exclude declared companions from candidates and receipt an archive."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-companion-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
            companions={
                "artifact/exe": [
                    {
                        "path": "*.dbg",
                        "role": "debug-symbol",
                        "required": False,
                    },
                    {
                        "path": "playwright.ps1",
                        "role": "runtime-helper",
                        "required": True,
                    },
                ]
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            exe = out_dir / "example"
            exe.write_bytes(b"binary")
            exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
            (out_dir / "example.dbg").write_bytes(b"debug")
            helper = out_dir / "playwright.ps1"
            helper.write_bytes(b"helper")
            helper.chmod(helper.stat().st_mode | stat.S_IXUSR)
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        assert (
            receipt["bundle-relative-path"]
            == "dist/example-1.2.3-linux-x64.zip"
        )
        archive = receipt["archive"]
        assert isinstance(archive, dict)
        primary = archive["primary-executable"]
        assert isinstance(primary, dict)
        assert primary["path"] == "example"
        assert primary["sha256"] == hashlib.sha256(b"binary").hexdigest()
        companions = archive["companions"]
        assert isinstance(companions, list)
        assert companions == [
            {
                "path": "example.dbg",
                "sha256": hashlib.sha256(b"debug").hexdigest(),
                "byte-size": 5,
                "role": "debug-symbol",
                "required": False,
            },
            {
                "path": "playwright.ps1",
                "sha256": hashlib.sha256(b"helper").hexdigest(),
                "byte-size": 6,
                "role": "runtime-helper",
                "required": True,
            },
        ]
        with zipfile.ZipFile(
            scratch / "bundle" / "dist/example-1.2.3-linux-x64.zip"
        ) as zf:
            assert zf.namelist() == [
                "example",
                "example.dbg",
                "playwright.ps1",
            ]
            assert zf.read("example") == b"binary"
            assert zf.read("example.dbg") == b"debug"
            assert zf.read("playwright.ps1") == b"helper"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_archive_name_uses_planner_convention() -> None:
    """Name companion archives from project id, version, and variant token."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-asset-name-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            project_id="release-tool",
            resolved_version="4.5.6",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
            companions={
                "artifact/exe": [
                    {
                        "path": "*.dbg",
                        "role": "debug-symbol",
                        "required": False,
                    }
                ]
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            exe = out_dir / "renamed-host"
            exe.write_bytes(b"binary")
            exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
            (out_dir / "renamed-host.dbg").write_bytes(b"debug")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        expected = "dist/release-tool-4.5.6-linux-x64.zip"
        assert receipt["bundle-relative-path"] == expected
        archive = receipt["archive"]
        assert isinstance(archive, dict)
        primary = archive["primary-executable"]
        assert isinstance(primary, dict)
        assert primary["path"] == "renamed-host"
        companions = archive["companions"]
        assert isinstance(companions, list)
        assert companions[0]["path"] == "renamed-host.dbg"
        assert not (
            scratch / "bundle" / "dist/renamed-host-linux-x64.zip"
        ).exists()
        with zipfile.ZipFile(scratch / "bundle" / expected) as zf:
            assert zf.namelist() == ["renamed-host", "renamed-host.dbg"]
            assert zf.read("renamed-host") == b"binary"
            assert zf.read("renamed-host.dbg") == b"debug"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_archives_when_optional_companion_absent() -> None:
    """Archive executables when optional declared companions match no files."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-empty-companion-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
            companions={
                "artifact/exe": [
                    {
                        "path": "*.dbg",
                        "role": "debug-symbol",
                        "required": False,
                    }
                ]
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            exe = out_dir / "example"
            exe.write_bytes(b"binary")
            exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        receipt = _result_artifacts(result)["artifact/exe"]
        assert isinstance(receipt, dict)
        assert (
            receipt["bundle-relative-path"]
            == "dist/example-1.2.3-linux-x64.zip"
        )
        archive = receipt["archive"]
        assert isinstance(archive, dict)
        primary = archive["primary-executable"]
        assert isinstance(primary, dict)
        assert primary["path"] == "example"
        assert primary["sha256"] == hashlib.sha256(b"binary").hexdigest()
        assert archive["companions"] == []
        with zipfile.ZipFile(
            scratch / "bundle" / "dist/example-1.2.3-linux-x64.zip"
        ) as zf:
            assert zf.namelist() == ["example"]
            assert zf.read("example") == b"binary"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_rejects_escaped_companion_match(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Do not archive companion matches outside the publish output root."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-escaped-companion-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
        )
        artifact = request["artifacts"]["artifact/exe"]
        assert isinstance(artifact, dict)
        artifact["companions"] = [
            {
                "path": "../escaped.dbg",
                "role": "debug-symbol",
                "required": False,
            }
        ]
        monkeypatch.setattr(
            executor_module, "validate_contract", lambda _: None
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            exe = out_dir / "example"
            exe.write_bytes(b"binary")
            exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
            (out_dir.parent / "escaped.dbg").write_bytes(b"debug")
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(
            BuildExecutorError, match="outside output root"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert error.value.code == "BUILD_OUTPUT_INVALID"
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_missing_required_companion_fails() -> None:
    """Fail when a required descriptor-declared companion is absent."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-missing-companion-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
            companions={
                "artifact/exe": [
                    {
                        "path": "playwright.sh",
                        "role": "runtime-helper",
                        "required": True,
                    }
                ]
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            (out_dir / "example").write_bytes(b"binary")
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(
            BuildExecutorError, match="required executable companion"
        ):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_dotnet_executable_undeclared_companion_remains_unexpected() -> None:
    """Do not hide companion-like files unless the descriptor declares them."""
    scratch = REPO_ROOT / ".build-executor-dotnet-exe-unexpected-companion-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            dimensions={"rid": "linux-x64"},
            artifacts={
                "artifact/exe": ("primary-binary", "binary", "executable"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            out_dir = Path(args[args.index("--output") + 1])
            (out_dir / "example").write_bytes(b"binary")
            dbg = out_dir / "example.dbg"
            dbg.write_bytes(b"debug")
            dbg.chmod(dbg.stat().st_mode | stat.S_IXUSR)
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="found 2"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_requires_npm_json_and_one_tarball() -> None:
    """Run npm build and pack, then validate tarball content."""
    scratch = REPO_ROOT / ".build-executor-node-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/index.cjs",
                    "types": "./dist/index.d.cts",
                    "files": ["dist", "README.md"],
                    "scripts": {"build": "node build.mjs"},
                    "exports": {".": "./dist/index.cjs"},
                }
            ),
            encoding="utf-8",
        )
        calls: list[tuple[tuple[str, ...], Path]] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append((tuple(args), cwd))
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/index.cjs",
                            "types": "./dist/index.d.cts",
                            "files": ["dist", "README.md"],
                            "exports": {".": "./dist/index.cjs"},
                        }
                    ).encode(),
                    "package/dist/index.cjs": b"module.exports = {};",
                    "package/dist/index.d.cts": b"export {};",
                    "package/README.md": b"# Example\n",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [
                        {
                            "filename": "example-1.2.3.tgz",
                            "name": "example",
                            "version": "1.2.3",
                        }
                    ]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls[0][0][1:3] == ("run", "build")
        assert calls[0][1] == project_root
        assert calls[1][0][1:3] == ("pack", "--json")
        assert calls[1][1] == project_root
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


@pytest.mark.parametrize(
    ("browser", "artifact_id"),
    [
        ("chrome", "artifact/chrome"),
        ("firefox", "artifact/firefox"),
        ("edge", "artifact/edge"),
    ],
)
def test_node_browser_zip_executor_receipts_wxt_browser_package(
    browser: str, artifact_id: str
) -> None:
    """Run a WXT build and receipt the requested browser zip asset name."""
    scratch = REPO_ROOT / f".build-executor-node-browser-{browser}-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            project_id="hcoona-release-smoke-wxt",
            dimensions={"browser": browser},
            artifacts={
                artifact_id: ("primary-package", "package", "browser-zip"),
            },
        )
        project_root = scratch / "hcoona-release-smoke-wxt"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "hcoona-release-smoke-wxt",
                    "version": "0.0.0",
                    "scripts": {"build": "wxt zip"},
                }
            ),
            encoding="utf-8",
        )
        calls: list[tuple[tuple[str, ...], Path]] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append((tuple(args), cwd))
            output = project_root / ".output"
            output.mkdir(exist_ok=True)
            package_json = json.loads(
                (project_root / "package.json").read_text(encoding="utf-8")
            )
            assert package_json["version"] == "1.2.3"
            _write_browser_zip(
                output / f"hcoona-release-smoke-wxt-1.2.3-{browser}.zip",
                "1.2.3",
            )
            _write_browser_zip(
                output
                / f"hcoona-release-smoke-wxt-1.2.3-{browser}-sources.zip",
                "1.2.3",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls[0][0][1:3] == ("run", "build")
        receipt = _result_artifacts(result)[artifact_id]
        assert isinstance(receipt, dict)
        assert receipt["bundle-relative-path"] == (
            f"dist/hcoona-release-smoke-wxt-1.2.3-{browser}.zip"
        )
        restored_package_json = json.loads(
            (project_root / "package.json").read_text(encoding="utf-8")
        )
        assert restored_package_json["version"] == "0.0.0"
    finally:
        _remove_tree_scratch(scratch)


def test_node_browser_zip_executor_receipts_firefox_sources_zip() -> None:
    """Run a WXT Firefox build and receipt browser plus sources zips."""
    scratch = REPO_ROOT / ".build-executor-node-browser-sources-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            project_id="hcoona-release-smoke-wxt",
            dimensions={"browser": "firefox"},
            artifacts={
                "artifact/firefox": (
                    "primary-package",
                    "package",
                    "browser-zip",
                ),
                "artifact/firefox-sources": (
                    "sources",
                    "archive",
                    "sources-zip",
                ),
            },
        )
        project_root = scratch / "hcoona-release-smoke-wxt"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "hcoona-release-smoke-wxt",
                    "version": "0.0.0",
                    "scripts": {
                        "build": "wxt zip",
                        "workflow-release:zip:firefox": "wxt zip -b firefox",
                    },
                }
            ),
            encoding="utf-8",
        )
        calls: list[tuple[tuple[str, ...], Path]] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append((tuple(args), cwd))
            output = project_root / ".output"
            output.mkdir(exist_ok=True)
            _write_browser_zip(
                output / "hcoona-release-smoke-wxt-1.2.3-firefox.zip",
                "1.2.3",
            )
            _write_browser_zip(
                output / "hcoona-release-smoke-wxt-1.2.3-sources.zip",
                "1.2.3",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls[0][0][1:3] == ("run", "workflow-release:zip:firefox")
        artifacts = _result_artifacts(result)
        assert artifacts["artifact/firefox"]["bundle-relative-path"] == (
            "dist/hcoona-release-smoke-wxt-1.2.3-firefox.zip"
        )
        assert artifacts["artifact/firefox-sources"][
            "bundle-relative-path"
        ] == ("dist/hcoona-release-smoke-wxt-1.2.3-sources.zip")
    finally:
        _remove_tree_scratch(scratch)


def test_node_browser_zip_executor_rejects_non_firefox_sources_zip() -> None:
    """Reject WXT source zip artifacts outside the Firefox variant."""
    scratch = REPO_ROOT / ".build-executor-node-browser-source-browser-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            project_id="hcoona-release-smoke-wxt",
            dimensions={"browser": "chrome"},
            artifacts={
                "artifact/chrome": (
                    "primary-package",
                    "package",
                    "browser-zip",
                ),
                "artifact/chrome-sources": (
                    "sources",
                    "archive",
                    "sources-zip",
                ),
            },
        )
        project_root = scratch / "hcoona-release-smoke-wxt"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "hcoona-release-smoke-wxt",
                    "version": "0.0.0",
                    "scripts": {"build": "wxt zip"},
                }
            ),
            encoding="utf-8",
        )

        with pytest.raises(BuildExecutorError, match="firefox variants"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=lambda args, _cwd: subprocess.CompletedProcess(
                    args, 0, "", ""
                ),
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_browser_zip_executor_validates_manifest_version() -> None:
    """Reject browser zips whose manifest version is not the frozen release."""
    scratch = REPO_ROOT / ".build-executor-node-browser-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            project_id="hcoona-release-smoke-wxt",
            dimensions={"browser": "chrome"},
            resolved_version="1.2.3-beta.4",
            artifacts={
                "artifact/chrome": (
                    "primary-package",
                    "package",
                    "browser-zip",
                ),
            },
        )
        project_root = scratch / "hcoona-release-smoke-wxt"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "hcoona-release-smoke-wxt",
                    "version": "0.0.0",
                    "scripts": {"build": "wxt zip"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            output = project_root / ".output"
            output.mkdir(exist_ok=True)
            _write_browser_zip(
                output / "hcoona-release-smoke-wxt-1.2.3-chrome.zip",
                "0.0.0",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(
            BuildExecutorError, match="manifest version"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )

        assert error.value.code == "BUILD_OUTPUT_INVALID"
    finally:
        _remove_tree_scratch(scratch)


def test_node_browser_zip_executor_rejects_fake_zip_text() -> None:
    """Reject placeholder text files masquerading as browser zip output."""
    scratch = REPO_ROOT / ".build-executor-node-browser-fake-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            project_id="hcoona-release-smoke-wxt",
            dimensions={"browser": "chrome"},
            artifacts={
                "artifact/chrome": (
                    "primary-package",
                    "package",
                    "browser-zip",
                ),
            },
        )
        project_root = scratch / "hcoona-release-smoke-wxt"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "hcoona-release-smoke-wxt",
                    "version": "0.0.0",
                    "scripts": {"build": "wxt zip"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            output = project_root / ".output"
            output.mkdir(exist_ok=True)
            (output / "hcoona-release-smoke-wxt-1.2.3-chrome.zip").write_text(
                "not a zip",
                encoding="utf-8",
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(
            BuildExecutorError, match=r"manifest\.json"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )

        assert error.value.code == "BUILD_OUTPUT_INVALID"
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_packs_each_projected_npm_artifact() -> None:
    """Pack one built npm project once per artifact-level package projection."""
    scratch = REPO_ROOT / ".build-executor-node-projection-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
                "artifact/npm-github": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
            },
            projections={
                "artifact/npm": {"package-name": "example"},
                "artifact/npm-github": {"package-name": "@hcoona/example"},
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        original_package_json = (
            b'{"scripts":{"build":"node build.mjs"},'
            b'"main":"./dist/index.cjs","version":"1.2.3",'
            b'"name":"example"}\n'
        )
        built_package_json = (
            b'{\n  "main": "./dist/index.cjs",\n'
            b'  "name": "example",\n'
            b'  "scripts": {\n    "build": "node build.mjs"\n  },\n'
            b'  "version": "1.2.3+build"\n}\n'
        )
        manifest.write_bytes(original_package_json)
        packed_names: list[str] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                manifest.write_bytes(built_package_json)
                return subprocess.CompletedProcess(args, 0, "", "")
            if not packed_names:
                assert manifest.read_bytes() == built_package_json
            current = json.loads(manifest.read_text(encoding="utf-8"))
            name = current["name"]
            filename = f"{name.removeprefix('@').replace('/', '-')}-1.2.3.tgz"
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / filename,
                {
                    "package/package.json": json.dumps(
                        {
                            "name": name,
                            "version": "1.2.3",
                            "main": "./dist/index.cjs",
                        }
                    ).encode(),
                    "package/dist/index.cjs": b"module.exports = {};",
                },
            )
            packed_names.append(name)
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": filename, "name": name, "version": "1.2.3"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert packed_names == ["example", "@hcoona/example"]
        assert manifest.read_bytes() == original_package_json
        receipts = _result_artifacts(result)
        assert {
            Path(
                cast(
                    "str",
                    cast("Mapping[str, object]", receipt)[
                        "bundle-relative-path"
                    ],
                )
            ).name
            for receipt in receipts.values()
        } == {"example-1.2.3.tgz", "hcoona-example-1.2.3.tgz"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_projected_tarball_with_rewritten_name() -> None:
    """Reject tarballs whose lifecycle scripts undo package-name projection."""
    scratch = REPO_ROOT / ".build-executor-node-projection-name-mismatch-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm-github": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
            },
            projections={
                "artifact/npm-github": {"package-name": "@hcoona/example"},
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        manifest.write_bytes(
            b'{"scripts":{"build":"node build.mjs"},'
            b'"version":"1.2.3","name":"example"}\n'
        )

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            assert (
                json.loads(manifest.read_text(encoding="utf-8"))["name"]
                == "@hcoona/example"
            )
            manifest.write_text(
                json.dumps(
                    {
                        "name": "example",
                        "scripts": {"build": "node build.mjs"},
                        "version": "1.2.3",
                    }
                ),
                encoding="utf-8",
            )
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                        }
                    ).encode(),
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [
                        {
                            "filename": "example-1.2.3.tgz",
                            "name": "example",
                            "version": "1.2.3",
                        }
                    ]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="package name"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_unprojected_tarball_with_rewritten_name() -> (
    None
):
    """Reject tarballs with lifecycle-rewritten manifest-fallback names."""
    scratch = REPO_ROOT / ".build-executor-node-manifest-name-mismatch-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        manifest.write_bytes(
            b'{"scripts":{"build":"node build.mjs"},'
            b'"version":"1.2.3","name":"example"}\n'
        )

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            manifest.write_text(
                json.dumps(
                    {
                        "name": "renamed",
                        "scripts": {"build": "node build.mjs"},
                        "version": "1.2.3",
                    }
                ),
                encoding="utf-8",
            )
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "renamed-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "renamed",
                            "version": "1.2.3",
                        }
                    ).encode(),
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [
                        {
                            "filename": "renamed-1.2.3.tgz",
                            "name": "renamed",
                            "version": "1.2.3",
                        }
                    ]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="package name"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_isolates_colliding_npm_pack_filenames() -> None:
    """Pack distinct npm package identities whose tarball filenames collide."""
    scratch = REPO_ROOT / ".build-executor-node-pack-collision-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/hcoona-example": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
                "artifact/scoped-hcoona-example": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
            },
            projections={
                "artifact/hcoona-example": {"package-name": "hcoona-example"},
                "artifact/scoped-hcoona-example": {
                    "package-name": "@hcoona/example"
                },
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        original_package_json = (
            b'{"scripts":{"build":"node build.mjs"},'
            b'"main":"./dist/index.cjs","version":"1.2.3",'
            b'"name":"hcoona-example"}\n'
        )
        manifest.write_bytes(original_package_json)
        pack_outputs: list[Path] = []
        packed_names: list[str] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            current = json.loads(manifest.read_text(encoding="utf-8"))
            name = current["name"]
            filename = "hcoona-example-1.2.3.tgz"
            out_dir = Path(args[args.index("--pack-destination") + 1])
            assert out_dir not in pack_outputs
            pack_outputs.append(out_dir)
            packed_names.append(name)
            _write_npm_tarball(
                out_dir / filename,
                {
                    "package/package.json": json.dumps(
                        {
                            "name": name,
                            "version": "1.2.3",
                            "main": "./dist/index.cjs",
                        }
                    ).encode(),
                    "package/dist/index.cjs": (
                        f"module.exports = {name!r};".encode()
                    ),
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": filename, "name": name, "version": "1.2.3"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert packed_names == ["hcoona-example", "@hcoona/example"]
        assert manifest.read_bytes() == original_package_json
        receipts = _result_artifacts(result)
        assert set(receipts) == {
            "artifact/hcoona-example",
            "artifact/scoped-hcoona-example",
        }
        relative_paths = [
            cast("Mapping[str, str]", receipt)["bundle-relative-path"]
            for receipt in receipts.values()
        ]
        assert len(set(relative_paths)) == len(relative_paths)
        packaged_names = set()
        for relative_path in relative_paths:
            with tarfile.open(scratch / "bundle" / relative_path) as archive:
                package_json = archive.extractfile("package/package.json")
                assert package_json is not None
                packaged_names.add(
                    json.loads(package_json.read().decode())["name"]
                )
        assert packaged_names == {"hcoona-example", "@hcoona/example"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_restores_manifest_bytes_after_pack_failure() -> None:
    """Restore the exact npm manifest bytes when projected npm pack fails."""
    scratch = REPO_ROOT / ".build-executor-node-projection-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm-github": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
            },
            projections={
                "artifact/npm-github": {"package-name": "@hcoona/example"},
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        original_package_json = (
            b'{\n  "version": "1.2.3",\n'
            b'  "scripts": {"build": "node build.mjs"},\n'
            b'  "name": "example"\n}\n'
        )
        built_package_json = (
            b'{\n  "name": "example",\n'
            b'  "scripts": {"build": "node build.mjs"},\n'
            b'  "version": "1.2.3+build"\n}\n'
        )
        failed_pack_package_json = (
            b'{\n  "name": "failed-pack-attempt",\n'
            b'  "scripts": {"build": "node build.mjs"},\n'
            b'  "version": "1.2.3+pack"\n}\n'
        )
        manifest.write_bytes(original_package_json)

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                manifest.write_bytes(built_package_json)
                return subprocess.CompletedProcess(args, 0, "", "")
            assert (
                json.loads(manifest.read_text(encoding="utf-8"))["name"]
                == "@hcoona/example"
            )
            manifest.write_bytes(failed_pack_package_json)
            return subprocess.CompletedProcess(
                args,
                1,
                "",
                "simulated pack failure",
            )

        with pytest.raises(BuildExecutorError, match="simulated pack failure"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert manifest.read_bytes() == original_package_json
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_restores_noop_projection_after_pack_failure() -> None:
    """Restore no-op projected manifests after failed lifecycle mutation."""
    scratch = REPO_ROOT / ".build-executor-node-noop-projection-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": (
                    "primary-package",
                    "package",
                    "npm-package",
                ),
            },
            projections={
                "artifact/npm": {"package-name": "example"},
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        original_package_json = (
            b'{\n  "name": "example",\n'
            b'  "scripts": {"build": "node build.mjs"},\n'
            b'  "version": "1.2.3"\n}\n'
        )
        mutated_package_json = (
            b'{\n  "name": "example",\n'
            b'  "scripts": {"build": "node build.mjs"},\n'
            b'  "version": "1.2.3+prepack"\n}\n'
        )
        manifest.write_bytes(original_package_json)

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == project_root
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            assert manifest.read_bytes() == original_package_json
            manifest.write_bytes(mutated_package_json)
            return subprocess.CompletedProcess(
                args,
                1,
                "",
                "simulated prepack failure",
            )

        with pytest.raises(
            BuildExecutorError, match="simulated prepack failure"
        ):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
        assert manifest.read_bytes() == original_package_json
    finally:
        _remove_tree_scratch(scratch)


def test_node_pack_json_accepts_script_noisy_stdout() -> None:
    """Accept pnpm/npm script logs around the pack JSON payload."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    validate_pack_json(
        "\n> example@1.2.3 prepack\n> node prepack.mjs\n"
        '[{"filename":"example-1.2.3.tgz","name":"example"}]\n'
        "> example@1.2.3 postpack\n> node postpack.mjs\n"
    )


def test_node_pack_json_skips_unrelated_json_array() -> None:
    """Skip decodable arrays that are not npm pack entries."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    validate_pack_json(
        '["debug"]\n'
        "> example@1.2.3 prepack\n"
        '[{"filename":"example-1.2.3.tgz","version":"1.2.3"}]\n'
    )


def test_node_pack_json_rejects_unrelated_array_without_valid_payload() -> None:
    """Reject lifecycle log arrays when the npm pack payload is absent."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    with pytest.raises(BuildExecutorError, match="no valid package entries"):
        validate_pack_json('["debug"]\n[{"name":"example-1.2.3.tgz"}')


def test_node_pack_json_rejects_filename_only_array() -> None:
    """Reject filename-only arrays that are not npm pack entries."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    with pytest.raises(BuildExecutorError, match="no valid package entries"):
        validate_pack_json('[{"filename":"example-1.2.3.tgz"}]\n')


def test_node_pack_json_accepts_pnpm_object_with_noisy_stdout() -> None:
    """Accept pnpm 10 object-shaped pack JSON among lifecycle logs."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    validate_pack_json(
        "> example@1.2.3 prepack\n"
        '{"name":"example","version":"1.2.3","filename":"example-1.2.3.tgz",'
        '"files":[{"path":"package.json"}]}\n'
        "> example@1.2.3 postpack\n"
    )


def test_node_pack_json_failure_includes_stdout_excerpt() -> None:
    """Report a compact stdout excerpt when pack JSON cannot be found."""
    validate_pack_json = vars(executor_module)["_validate_npm_pack_json"]
    with pytest.raises(BuildExecutorError, match="stdout excerpt:"):
        validate_pack_json("> example@1.2.3 prepack\nnot json\n")


def test_node_workspace_runner_installs_pnpm_dependencies() -> None:
    """Use corepack and pnpm for projects covered by a root pnpm workspace."""
    scratch = REPO_ROOT / ".build-executor-node-pnpm-workspace-test"
    _remove_tree_scratch(scratch)
    try:
        project_root = scratch / "packages" / "example"
        project_root.mkdir(parents=True)
        (scratch / "pnpm-lock.yaml").write_text(
            "lockfileVersion: '9.0'\n",
            encoding="utf-8",
        )
        (scratch / "pnpm-workspace.yaml").write_text(
            "packages:\n  - packages/*\n",
            encoding="utf-8",
        )
        calls: list[tuple[str, ...]] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append(tuple(args))
            return subprocess.CompletedProcess(args, 0, "", "")

        build_context = vars(executor_module)["_BuildContext"]
        build_telemetry = vars(executor_module)["_BuildTelemetry"]
        prepare_runner = vars(executor_module)["_prepare_node_package_runner"]
        context = build_context(
            request={},
            artifacts=(),
            project_root=project_root,
            manifest=project_root / "package.json",
            output_root=scratch / "out",
            runner=runner,
            repo_root=scratch,
            telemetry=build_telemetry(scratch / "bundle"),
        )

        package_runner = prepare_runner(context)

        assert [call[1:] for call in calls] == [
            ("enable", "pnpm"),
            ("install", "--frozen-lockfile"),
        ]
        assert Path(package_runner.args[0]).name == "pnpm"
        assert package_runner.args[1:] == (
            "--dir",
            project_root.as_posix(),
        )
        assert package_runner.cwd == scratch
    finally:
        _remove_tree_scratch(scratch)


def test_node_pnpm_workspace_globs_match_path_segments() -> None:
    """Keep pnpm '*' matching to one path segment while '**' crosses them."""
    scratch = REPO_ROOT / ".build-executor-node-pnpm-glob-test"
    _remove_tree_scratch(scratch)
    try:
        (scratch / "poc" / "foo").mkdir(parents=True)
        (scratch / "poc" / "foo" / "bar").mkdir()
        (scratch / "deep" / "foo" / "bar").mkdir(parents=True)
        (scratch / "pnpm-lock.yaml").write_text(
            "lockfileVersion: '9.0'\n",
            encoding="utf-8",
        )
        (scratch / "pnpm-workspace.yaml").write_text(
            "packages:\n  - poc/*\n  - deep/**\n",
            encoding="utf-8",
        )
        is_workspace_project = vars(executor_module)[
            "_is_pnpm_workspace_project"
        ]

        assert is_workspace_project(scratch, scratch / "poc" / "foo")
        assert not is_workspace_project(
            scratch, scratch / "poc" / "foo" / "bar"
        )
        assert is_workspace_project(scratch, scratch / "deep" / "foo" / "bar")
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_fails_on_missing_tarball_entrypoint() -> None:
    """Reject npm tarballs missing declared package entrypoints."""
    scratch = REPO_ROOT / ".build-executor-node-missing-content-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/index.cjs",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/index.cjs",
                            "files": ["dist"],
                        }
                    ).encode(),
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_uses_post_build_files_manifest() -> None:
    """Validate npm files entries from the manifest after the build script."""
    scratch = REPO_ROOT / ".build-executor-node-post-build-files-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        manifest.write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                manifest.write_text(
                    json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                            "scripts": {"build": "node build.mjs"},
                        }
                    ),
                    encoding="utf-8",
                )
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                        }
                    ).encode(),
                    "package/lib/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_post_build_files_manifest() -> None:
    """Reject tarballs missing files from the post-build npm manifest."""
    scratch = REPO_ROOT / ".build-executor-node-missing-post-build-files-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        manifest = project_root / "package.json"
        manifest.write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                manifest.write_text(
                    json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                            "scripts": {"build": "node build.mjs"},
                        }
                    ),
                    encoding="utf-8",
                )
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                        }
                    ).encode(),
                    "package/dist/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_uses_packaged_files_manifest_after_lifecycle() -> None:
    """Validate npm files entries from the manifest captured in the tarball."""
    scratch = REPO_ROOT / ".build-executor-node-lifecycle-files-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                        }
                    ).encode(),
                    "package/lib/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_packaged_files_manifest() -> None:
    """Reject tarballs missing files from the packed npm manifest."""
    scratch = REPO_ROOT / ".build-executor-node-missing-lifecycle-files-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["lib"],
                        }
                    ).encode(),
                    "package/dist/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_files_double_star_glob() -> None:
    """Accept npm files globs that span nested directories."""
    scratch = REPO_ROOT / ".build-executor-node-files-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/nested/index.js",
                    "files": ["dist/**/*.js"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/nested/index.js",
                            "files": ["dist/**/*.js"],
                        }
                    ).encode(),
                    "package/dist/nested/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_files_double_star_glob() -> None:
    """Reject npm files globs that match no packed files."""
    scratch = REPO_ROOT / ".build-executor-node-missing-files-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/nested/index.js",
                    "files": ["dist/**/*.js"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/nested/index.js",
                            "files": ["dist/**/*.js"],
                        }
                    ).encode(),
                    "package/dist/nested/index.css": b"",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_files_brace_glob() -> None:
    """Accept npm files globs that require npm-compatible brace expansion."""
    scratch = REPO_ROOT / ".build-executor-node-files-brace-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/index.js",
                    "files": ["dist/*.{js,d.ts}"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/index.js",
                            "files": ["dist/*.{js,d.ts}"],
                        }
                    ).encode(),
                    "package/dist/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_files_directory_glob() -> None:
    """Accept files globs that match ancestor directories of packed files."""
    scratch = REPO_ROOT / ".build-executor-node-files-directory-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/cjs/index.js",
                    "files": ["dist/{cjs,esm}"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/cjs/index.js",
                            "files": ["dist/{cjs,esm}"],
                        }
                    ).encode(),
                    "package/dist/cjs/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_files_directory_glob() -> None:
    """Reject directory-matching files globs with no matching descendants."""
    scratch = REPO_ROOT / ".build-executor-node-missing-directory-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/umd/index.js",
                    "files": ["dist/{cjs,esm}"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/umd/index.js",
                            "files": ["dist/{cjs,esm}"],
                        }
                    ).encode(),
                    "package/dist/umd/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_files_posix_character_class_glob() -> None:
    """Accept npm files globs using minimatch POSIX character classes."""
    scratch = REPO_ROOT / ".build-executor-node-files-posix-class-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/a.js",
                    "files": ["dist/[[:alpha:]].js"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/a.js",
                            "files": ["dist/[[:alpha:]].js"],
                        }
                    ).encode(),
                    "package/dist/a.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_files_posix_class_glob() -> None:
    """Reject POSIX character-class files globs that match no packed files."""
    scratch = REPO_ROOT / ".build-executor-node-missing-posix-class-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist/[[:alpha:]].js"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["dist/[[:alpha:]].js"],
                        }
                    ).encode(),
                    "package/dist/1.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_files_trailing_double_star() -> None:
    """Accept npm files trailing /** globs for recursive package contents."""
    scratch = REPO_ROOT / ".build-executor-node-files-trailing-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/nested/index.js",
                    "files": ["dist/**"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "main": "./dist/nested/index.js",
                            "files": ["dist/**"],
                        }
                    ).encode(),
                    "package/dist/nested/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_rejects_missing_files_trailing_double_star() -> None:
    """Reject npm trailing /** files globs that match no packed files."""
    scratch = REPO_ROOT / ".build-executor-node-missing-trailing-glob-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "files": ["dist/**"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3",
                            "files": ["dist/**"],
                        }
                    ).encode(),
                    "package/src/index.js": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="missing declared"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_fails_on_version_mismatch() -> None:
    """Reject npm tarballs whose package.json version differs."""
    scratch = REPO_ROOT / ".build-executor-node-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/index.cjs",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-9.9.9.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "9.9.9",
                            "main": "./dist/index.cjs",
                            "files": ["dist"],
                        }
                    ).encode(),
                    "package/dist/index.cjs": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-9.9.9.tgz", "name": "example"}]
                ),
                "",
            )

        with pytest.raises(BuildExecutorError, match="frozen version"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_accepts_semver_equivalent_versions() -> None:
    """Accept node-semver-equivalent npm package versions."""
    scratch = REPO_ROOT / ".build-executor-node-normalized-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        request["project"]["resolved-version"] = "v1.2.3"
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps(
                {
                    "name": "example",
                    "version": "1.2.3",
                    "main": "./dist/index.cjs",
                    "files": ["dist"],
                    "scripts": {"build": "node build.mjs"},
                }
            ),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["run", "build"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            helper_result = _handle_npm_matcher_command(args, _cwd)
            if helper_result is not None:
                return helper_result
            out_dir = Path(args[args.index("--pack-destination") + 1])
            _write_npm_tarball(
                out_dir / "example-1.2.3.tgz",
                {
                    "package/package.json": json.dumps(
                        {
                            "name": "example",
                            "version": "1.2.3+build.7",
                            "main": "./dist/index.cjs",
                            "files": ["dist"],
                        }
                    ).encode(),
                    "package/dist/index.cjs": b"module.exports = {};",
                },
            )
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    [{"filename": "example-1.2.3.tgz", "name": "example"}]
                ),
                "",
            )

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/npm"}
    finally:
        _remove_tree_scratch(scratch)


def test_node_executor_requires_build_script() -> None:
    """Reject npm packages without an explicit build script."""
    scratch = REPO_ROOT / ".build-executor-node-no-build-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        project_root = scratch / "example"
        (project_root / "package.json").write_text(
            json.dumps({"name": "example", "version": "1.2.3"}),
            encoding="utf-8",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="build script"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_ruby_executor_builds_requested_gemspec() -> None:
    """Run gem build with an explicit output file."""
    scratch = REPO_ROOT / ".build-executor-ruby-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="ruby",
            artifacts={
                "artifact/gem": ("primary-package", "package", "rubygem"),
            },
        )
        calls: list[tuple[tuple[str, ...], Path]] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append((tuple(args), cwd))
            if args[1] == "specification":
                return subprocess.CompletedProcess(
                    args,
                    0,
                    "--- !ruby/object:Gem::Version\nversion: 1.2.3\n",
                    "",
                )
            if args[1] == "-rrubygems":
                return subprocess.CompletedProcess(args, 0, "true\n", "")
            Path(args[args.index("--output") + 1]).write_bytes(b"gem")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        build_args, build_cwd = calls[0]
        assert build_args[1:3] == ("build", "example.gemspec")
        assert "-C" not in build_args
        assert build_cwd == scratch / "example"
        assert set(_result_artifacts(result)) == {"artifact/gem"}
    finally:
        _remove_tree_scratch(scratch)


def test_ruby_executor_fails_on_version_mismatch() -> None:
    """Reject RubyGems whose metadata version differs."""
    scratch = REPO_ROOT / ".build-executor-ruby-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="ruby",
            artifacts={
                "artifact/gem": ("primary-package", "package", "rubygem"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1] == "specification":
                return subprocess.CompletedProcess(
                    args,
                    0,
                    "--- !ruby/object:Gem::Version\nversion: 9.9.9\n",
                    "",
                )
            if args[1] == "-rrubygems":
                return subprocess.CompletedProcess(args, 0, "false\n", "")
            Path(args[args.index("--output") + 1]).write_bytes(b"gem")
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="frozen version"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_ruby_executor_accepts_gem_version_equality() -> None:
    """Accept RubyGem versions according to Gem::Version equality."""
    scratch = REPO_ROOT / ".build-executor-ruby-equivalent-version-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="ruby",
            artifacts={
                "artifact/gem": ("primary-package", "package", "rubygem"),
            },
        )
        project = cast("dict[str, Any]", request["project"])
        project["resolved-version"] = "1.2"

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1] == "specification":
                return subprocess.CompletedProcess(
                    args,
                    0,
                    "--- !ruby/object:Gem::Version\nversion: 1.2.0\n",
                    "",
                )
            if args[1] == "-rrubygems":
                assert args[-2:] == ["1.2.0", "1.2"]
                return subprocess.CompletedProcess(args, 0, "true\n", "")
            Path(args[args.index("--output") + 1]).write_bytes(b"gem")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/gem"}
    finally:
        _remove_tree_scratch(scratch)


def test_inno_setup_executor_runs_project_specific_scripts() -> None:
    """Run ImageOcclusionEditor publish and installer scripts."""
    scratch = REPO_ROOT / ".build-executor-inno-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            project_id="image-occlusion-editor",
            dimensions={"rid": "win-x64"},
            artifacts={
                "artifact/installer": ("installer", "installer", "inno-setup"),
            },
        )
        project_root = scratch / "image-occlusion-editor"
        script_dir = project_root / "script"
        script_dir.mkdir(exist_ok=True)
        (script_dir / "Publish-ImageOcclusionEditor.ps1").write_text(
            "", encoding="utf-8"
        )
        (script_dir / "Build-InnoInstaller.ps1").write_text(
            "", encoding="utf-8"
        )
        calls: list[tuple[tuple[str, ...], Path]] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append((tuple(args), cwd))
            if "-InstallerOutputPath" in args:
                assert "-InstallerFileName" not in args
                out_dir = Path(args[args.index("-InstallerOutputPath") + 1])
                (out_dir / "ImageOcclusionEditorWinUI3_Setup.exe").write_bytes(
                    b"MZinstaller"
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert Path(calls[0][0][0]).name == "dotnet"
        assert calls[0][0][1:] == ("tool", "restore")
        assert calls[0][1] == REPO_ROOT
        assert calls[1][0][3].endswith(
            "/script/Publish-ImageOcclusionEditor.ps1"
        )
        assert "-TelemetryOutputPath" in calls[1][0]
        assert "-MsBuildBinlogDirectory" in calls[1][0]
        assert calls[1][0][
            calls[1][0].index("-TelemetryOutputPath") + 1
        ].endswith("/powershell/0001-inno-publish-script.json")
        assert (
            "/_profile/runs/"
            in calls[1][0][calls[1][0].index("-TelemetryOutputPath") + 1]
        )
        assert calls[1][0][
            calls[1][0].index("-MsBuildBinlogDirectory") + 1
        ].endswith("/binlogs/0002-inno-publish-script")
        assert (
            "/_profile/runs/"
            in calls[1][0][calls[1][0].index("-MsBuildBinlogDirectory") + 1]
        )
        assert calls[2][0][3].endswith("/script/Build-InnoInstaller.ps1")
        assert "-TelemetryOutputPath" in calls[2][0]
        assert calls[2][0][
            calls[2][0].index("-TelemetryOutputPath") + 1
        ].endswith("/powershell/0003-inno-installer-script.json")
        assert (
            "/_profile/runs/"
            in calls[2][0][calls[2][0].index("-TelemetryOutputPath") + 1]
        )
        assert set(_result_artifacts(result)) == {"artifact/installer"}
        telemetry = json.loads(
            (scratch / "bundle/release-build-profile-telemetry.json").read_text(
                encoding="utf-8",
            ),
        )
        phases = {phase["phase"] for phase in telemetry["phases"]}
        assert {
            "dotnet-tool-restore",
            "inno-publish-script",
            "inno-installer-script",
        } <= phases
    finally:
        _remove_tree_scratch(scratch)


def test_inno_setup_executor_runs_generic_smoke_scripts() -> None:
    """Run generic Inno Setup publish and installer scripts for smoke apps."""
    scratch = REPO_ROOT / ".build-executor-inno-generic-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            project_id="hcoona-release-smoke-inno",
            dimensions={"os": "windows", "rid": "win-x64"},
            artifacts={
                "artifact/installer": ("installer", "installer", "inno-setup"),
            },
        )
        project_root = scratch / "hcoona-release-smoke-inno"
        script_dir = project_root / "script"
        script_dir.mkdir(exist_ok=True)
        (script_dir / "Publish.ps1").write_text("", encoding="utf-8")
        (script_dir / "Build-InnoInstaller.ps1").write_text(
            "", encoding="utf-8"
        )
        calls: list[tuple[str, ...]] = []

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            calls.append(tuple(args))
            if "-InstallerOutputPath" in args:
                out_dir = Path(args[args.index("-InstallerOutputPath") + 1])
                file_name = args[args.index("-InstallerFileName") + 1]
                (out_dir / file_name).write_bytes(b"MZinstaller")
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
            check_commit=False,
        )

        validate_contract(result)
        assert Path(calls[0][0]).name == "dotnet"
        assert calls[0][1:] == ("tool", "restore")
        assert calls[1][3].endswith("/script/Publish.ps1")
        assert "-TelemetryOutputPath" in calls[1]
        assert "-MsBuildBinlogDirectory" in calls[1]
        assert calls[1][calls[1].index("-TelemetryOutputPath") + 1].endswith(
            "/powershell/0001-inno-publish-script.json",
        )
        assert (
            "/_profile/runs/"
            in calls[1][calls[1].index("-TelemetryOutputPath") + 1]
        )
        assert calls[1][calls[1].index("-MsBuildBinlogDirectory") + 1].endswith(
            "/binlogs/0002-inno-publish-script",
        )
        assert (
            "/_profile/runs/"
            in calls[1][calls[1].index("-MsBuildBinlogDirectory") + 1]
        )
        assert calls[2][3].endswith("/script/Build-InnoInstaller.ps1")
        assert "-TelemetryOutputPath" in calls[2]
        assert calls[2][calls[2].index("-TelemetryOutputPath") + 1].endswith(
            "/powershell/0003-inno-installer-script.json",
        )
        assert (
            "/_profile/runs/"
            in calls[2][calls[2].index("-TelemetryOutputPath") + 1]
        )
        receipt = _result_artifacts(result)["artifact/installer"]
        assert isinstance(receipt, dict)
        assert receipt["bundle-relative-path"] == (
            "dist/hcoona-release-smoke-inno-1.2.3-windows-win-x64-setup.exe"
        )
    finally:
        _remove_tree_scratch(scratch)


def test_inno_setup_executor_rejects_fake_text_exe() -> None:
    """Reject placeholder text files masquerading as Inno Setup installers."""
    scratch = REPO_ROOT / ".build-executor-inno-fake-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="dotnet",
            project_id="hcoona-release-smoke-inno",
            dimensions={"os": "windows", "rid": "win-x64"},
            artifacts={
                "artifact/installer": ("installer", "installer", "inno-setup"),
            },
        )
        project_root = scratch / "hcoona-release-smoke-inno"
        script_dir = project_root / "script"
        script_dir.mkdir(exist_ok=True)
        (script_dir / "Publish.ps1").write_text("", encoding="utf-8")
        (script_dir / "Build-InnoInstaller.ps1").write_text(
            "", encoding="utf-8"
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if "-InstallerOutputPath" in args:
                out_dir = Path(args[args.index("-InstallerOutputPath") + 1])
                file_name = args[args.index("-InstallerFileName") + 1]
                (out_dir / file_name).write_text(
                    "not a real installer",
                    encoding="utf-8",
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="Windows executable"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_smoke_inno_publish_records_dotnet_failure_with_native_error_preference(
    tmp_path: Path,
) -> None:
    """Smoke publish sidecar records nonzero dotnet under native throw mode."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno publish tests")
    fake_bin = tmp_path / "fake-bin"
    fake_bin.mkdir()
    expected_exit_code = 23
    if os.name == "nt":
        fake_dotnet = fake_bin / "dotnet.cmd"
        fake_dotnet.write_text(
            "\r\n".join(
                [
                    "@echo off",
                    "echo fake dotnet publish failed 1>&2",
                    f"exit /b {expected_exit_code}",
                ],
            ),
            encoding="utf-8",
        )
    else:
        fake_dotnet = fake_bin / "dotnet"
        fake_dotnet.write_text(
            "\n".join(
                [
                    "#!/usr/bin/env bash",
                    "echo 'fake dotnet publish failed' >&2",
                    f"exit {expected_exit_code}",
                ],
            ),
            encoding="utf-8",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)
    telemetry_path = tmp_path / "profile" / "publish.json"
    binlog_dir = tmp_path / "binlogs"
    output_root = tmp_path / "publish"
    env = os.environ.copy()
    env["PATH"] = f"{fake_bin}{os.pathsep}{env.get('PATH', '')}"
    command = (
        "$ErrorActionPreference = 'Stop'; "
        "$PSNativeCommandUseErrorActionPreference = $true; "
        f"& {_ps_single_quote(_smoke_inno_publish_script())} "
        f"-OutputRoot {_ps_single_quote(output_root)} "
        f"-TelemetryOutputPath {_ps_single_quote(telemetry_path)} "
        f"-MsBuildBinlogDirectory {_ps_single_quote(binlog_dir)}"
    )

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-Command",
            command,
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert f"exit code: {expected_exit_code}" in result.stderr
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == ["dotnet-publish"]
    assert phases[0]["outcome"] == "failure"
    assert phases[0]["exit-code"] == expected_exit_code
    assert phases[0]["binlog-path"].endswith("dotnet-publish.binlog")
    assert phases[0]["binlog-exists"] is False


def test_smoke_inno_publish_records_missing_dotnet_start_failure(
    tmp_path: Path,
) -> None:
    """Smoke publish sidecar records telemetry when dotnet cannot start."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno publish tests")
    empty_bin = tmp_path / "empty-bin"
    empty_bin.mkdir()
    telemetry_path = tmp_path / "profile" / "publish.json"
    binlog_dir = tmp_path / "binlogs"
    output_root = tmp_path / "publish"
    env = os.environ.copy()
    env["PATH"] = str(empty_bin)
    if os.name == "nt":
        env["Path"] = str(empty_bin)
    command = (
        "$ErrorActionPreference = 'Stop'; "
        "$PSNativeCommandUseErrorActionPreference = $true; "
        f"& {_ps_single_quote(_smoke_inno_publish_script())} "
        f"-OutputRoot {_ps_single_quote(output_root)} "
        f"-TelemetryOutputPath {_ps_single_quote(telemetry_path)} "
        f"-MsBuildBinlogDirectory {_ps_single_quote(binlog_dir)}"
    )

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-Command",
            command,
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == ["dotnet-publish"]
    phase = phases[0]
    assert phase["outcome"] == "failure"
    assert phase["argv"][0] == "dotnet"
    assert "publish" in phase["argv"]
    assert phase["cwd"]
    assert "exit-code" not in phase
    assert phase["error"]
    assert "dotnet" in phase["error"].lower()
    assert phase["binlog-path"].endswith("dotnet-publish.binlog")
    assert phase["binlog-exists"] is False


def test_image_occlusion_publish_records_missing_dotnet_start_failure(
    tmp_path: Path,
) -> None:
    """ImageOcclusion publish records telemetry when dotnet cannot start."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion publish tests")
    empty_bin = tmp_path / "empty-bin"
    empty_bin.mkdir()
    telemetry_path = tmp_path / "profile" / "publish.json"
    binlog_dir = tmp_path / "binlogs"
    output_root = tmp_path / "publish"
    env = os.environ.copy()
    env["PATH"] = str(empty_bin)
    if os.name == "nt":
        env["Path"] = str(empty_bin)
    command = (
        "$ErrorActionPreference = 'Stop'; "
        "$PSNativeCommandUseErrorActionPreference = $true; "
        f"& {_ps_single_quote(_image_occlusion_publish_script())} "
        f"-OutputRoot {_ps_single_quote(output_root)} "
        f"-TelemetryOutputPath {_ps_single_quote(telemetry_path)} "
        f"-MsBuildBinlogDirectory {_ps_single_quote(binlog_dir)}"
    )

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-Command",
            command,
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == ["dotnet-publish"]
    phase = phases[0]
    assert phase["outcome"] == "failure"
    assert phase["argv"][0] == "dotnet"
    assert "publish" in phase["argv"]
    assert phase["cwd"]
    assert "exit-code" not in phase
    assert phase["error"]
    assert "dotnet" in phase["error"].lower()
    assert phase["binlog-path"].endswith("dotnet-publish.binlog")
    assert phase["binlog-exists"] is False


def test_image_occlusion_publish_records_cyclonedx_command_argv(
    tmp_path: Path,
) -> None:
    """ImageOcclusion publish records the actual CycloneDX command argv."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion publish tests")
    if os.name == "nt":
        pytest.skip("POSIX fake dotnet is required for this argv probe")
    fake_bin = tmp_path / "fake-bin"
    fake_bin.mkdir()
    _write_fake_image_occlusion_dotnet(
        fake_bin / "dotnet",
        cyclone_success=True,
    )
    output_root = tmp_path / "publish-root"
    telemetry_path = tmp_path / "profile" / "publish.json"
    target_framework, runtime_identifier, _assembly_name = (
        _image_occlusion_project_metadata()
    )
    manifest_path = (
        output_root
        / "ImageOcclusionEditor"
        / "Release"
        / target_framework
        / runtime_identifier
        / "_manifest"
    )
    csproj_path = (
        REPO_ROOT
        / "src/public/app/ImageOcclusionEditor"
        / "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj"
    )
    env = os.environ.copy()
    env["PATH"] = f"{fake_bin}{os.pathsep}{env.get('PATH', '')}"

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_publish_script()),
            "-OutputRoot",
            str(output_root),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0, result.stderr
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = {phase["phase"]: phase for phase in telemetry["phases"]}
    cyclone_phase = phases["cyclonedx-sbom"]
    assert cyclone_phase["outcome"] == "success"
    assert cyclone_phase["exit-code"] == 0
    cyclone_argv = cyclone_phase["argv"]
    assert cyclone_argv[:5] == [
        "dotnet",
        "tool",
        "run",
        "dotnet-CycloneDX",
        "--",
    ]
    assert str(csproj_path) in cyclone_argv
    assert cyclone_argv[cyclone_argv.index("-o") + 1] == str(manifest_path)
    assert "--exclude-dev" in cyclone_argv
    assert "--exclude-test-projects" in cyclone_argv
    assert cyclone_argv[cyclone_argv.index("--output-format") + 1] == "Json"
    assert "--disable-package-restore" in cyclone_argv


def test_image_occlusion_publish_records_failed_cyclonedx_command_argv(
    tmp_path: Path,
) -> None:
    """ImageOcclusion publish records CycloneDX failure argv with path args."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion publish tests")
    if os.name == "nt":
        pytest.skip("POSIX fake dotnet is required for this argv probe")
    fake_bin = tmp_path / "fake-bin"
    fake_bin.mkdir()
    _write_fake_image_occlusion_dotnet(
        fake_bin / "dotnet",
        cyclone_success=False,
        cyclone_failure_exit_codes=(41, 43, 44),
    )
    _write_failing_executable(fake_bin / "dotnet-CycloneDX", exit_code=42)
    expected_exit_code = 44
    output_root = tmp_path / "publish-root"
    telemetry_path = tmp_path / "profile" / "publish.json"
    argv_log_path = tmp_path / "cyclonedx-argv.jsonl"
    target_framework, runtime_identifier, _assembly_name = (
        _image_occlusion_project_metadata()
    )
    manifest_path = (
        output_root
        / "ImageOcclusionEditor"
        / "Release"
        / target_framework
        / runtime_identifier
        / "_manifest"
    )
    csproj_path = (
        REPO_ROOT
        / "src/public/app/ImageOcclusionEditor"
        / "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj"
    )
    env = os.environ.copy()
    env["PATH"] = f"{fake_bin}{os.pathsep}{env.get('PATH', '')}"
    env["CYCLONEDX_FAKE_ARGV_LOG"] = str(argv_log_path)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_publish_script()),
            "-OutputRoot",
            str(output_root),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = {phase["phase"]: phase for phase in telemetry["phases"]}
    cyclone_phase = phases["cyclonedx-sbom"]
    assert cyclone_phase["outcome"] == "failure"
    cyclone_argv = cyclone_phase["argv"]
    expected_common_args = [
        str(csproj_path),
        "-o",
        str(manifest_path),
        "--exclude-dev",
        "--exclude-test-projects",
        "--output-format",
        "Json",
        "--disable-package-restore",
    ]
    expected_initial_argv = [
        "dotnet",
        "tool",
        "run",
        "dotnet-CycloneDX",
        "--",
        *expected_common_args,
    ]
    expected_direct_argv = ["dotnet-CycloneDX", *expected_common_args]
    expected_dotnet_tool_argv = [
        "dotnet",
        "dotnet-CycloneDX",
        *expected_common_args,
    ]
    expected_final_argv = ["dotnet", "CycloneDX", *expected_common_args]
    attempted_argvs = [
        [Path(argv[0]).name, *argv[1:]]
        for argv in (
            json.loads(line)
            for line in argv_log_path.read_text(encoding="utf-8").splitlines()
        )
        if len(argv) > 1 and argv[1] != "publish"
    ]
    assert attempted_argvs == [
        expected_initial_argv,
        expected_direct_argv,
        expected_dotnet_tool_argv,
        expected_final_argv,
    ]
    assert cyclone_argv == expected_final_argv
    assert cyclone_argv != expected_initial_argv
    assert cyclone_phase["exit-code"] == expected_exit_code
    assert cyclone_phase["output-paths"] == [str(manifest_path)]
    assert cyclone_phase["error"]


def test_image_occlusion_publish_omits_cyclonedx_exit_code_without_process_code(
    tmp_path: Path,
) -> None:
    """ImageOcclusion publish omits CycloneDX exit-code when no CLI starts."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion publish tests")
    if os.name == "nt":
        pytest.skip("POSIX fake dotnet is required for this argv probe")
    fake_bin = tmp_path / "fake-bin"
    fake_bin.mkdir()
    fake_dotnet = fake_bin / "dotnet"
    fake_dotnet.write_text(
        "\n".join(
            [
                "#!/bin/bash",
                "set -euo pipefail",
                "if [ \"${1:-}\" = 'publish' ]; then",
                '    /bin/rm -- "$0"',
                "    exit 0",
                "fi",
                "exit 42",
            ],
        ),
        encoding="utf-8",
    )
    fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)
    output_root = tmp_path / "publish-root"
    telemetry_path = tmp_path / "profile" / "publish.json"
    target_framework, runtime_identifier, _assembly_name = (
        _image_occlusion_project_metadata()
    )
    manifest_path = (
        output_root
        / "ImageOcclusionEditor"
        / "Release"
        / target_framework
        / runtime_identifier
        / "_manifest"
    )
    csproj_path = (
        REPO_ROOT
        / "src/public/app/ImageOcclusionEditor"
        / "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj"
    )
    env = os.environ.copy()
    env["PATH"] = str(fake_bin)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_publish_script()),
            "-OutputRoot",
            str(output_root),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = {phase["phase"]: phase for phase in telemetry["phases"]}
    cyclone_phase = phases["cyclonedx-sbom"]
    assert cyclone_phase["outcome"] == "failure"
    assert cyclone_phase["argv"] == [
        "dotnet",
        "CycloneDX",
        str(csproj_path),
        "-o",
        str(manifest_path),
        "--exclude-dev",
        "--exclude-test-projects",
        "--output-format",
        "Json",
        "--disable-package-restore",
    ]
    assert "exit-code" not in cyclone_phase
    assert cyclone_phase["output-paths"] == [str(manifest_path)]
    assert cyclone_phase["error"]


def test_smoke_inno_script_includes_iscc_failure_diagnostics(
    tmp_path: Path,
) -> None:
    """Smoke installer script records sanitized ISCC failure output."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "telemetry.json"
    publish_root.mkdir()
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")
    fake_iscc = tmp_path / "fake-iscc"
    fake_iscc.write_text(
        "\n".join(
            [
                "#!/usr/bin/env bash",
                "echo 'ISCC compiler started' >&2",
                "echo 'token=super-secret-token' >&2",
                "echo 'error: invalid Setup directive' >&2",
                "exit 42",
            ],
        ),
        encoding="utf-8",
    )
    fake_iscc.chmod(fake_iscc.stat().st_mode | stat.S_IXUSR)
    expected_exit_code = 42

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_smoke_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "super-secret-token" not in result.stdout
    assert "super-secret-token" not in result.stderr
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phase = telemetry["phases"][0]
    assert phase["phase"] == "iscc-compile"
    assert phase["outcome"] == "failure"
    assert phase["exit-code"] == expected_exit_code
    assert phase["error"] == os.linesep.join(
        [
            "Inno Setup failed, exit code: 42",
            "ISCC output:",
            "ISCC compiler started",
            "token=<redacted>",
            "error: invalid Setup directive",
        ],
    )


def test_smoke_inno_script_records_missing_iscc_resolution_failure(
    tmp_path: Path,
) -> None:
    """Smoke installer sidecar records telemetry when ISCC cannot resolve."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "telemetry.json"
    missing_iscc = tmp_path / "missing-iscc"
    publish_root.mkdir()
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_smoke_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(missing_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == [
        "iscc-compiler-resolution",
    ]
    phase = phases[0]
    assert phase["outcome"] == "failure"
    assert "exit-code" not in phase
    assert str(missing_iscc) in phase["error"]


def test_smoke_inno_script_no_hint_iscc_resolution_failure_uses_empty_argv(
    tmp_path: Path,
) -> None:
    """Smoke installer sidecar emits empty argv without an ISCC hint."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    empty_bin = tmp_path / "empty-bin"
    empty_bin.mkdir()
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "telemetry.json"
    publish_root.mkdir()
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")
    env = os.environ.copy()
    env["PATH"] = str(empty_bin)
    if os.name == "nt":
        env["Path"] = str(empty_bin)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_smoke_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phase = telemetry["phases"][0]
    assert phase["phase"] == "iscc-compiler-resolution"
    assert phase["outcome"] == "failure"
    assert phase.get("argv", []) == []
    assert "exit-code" not in phase
    assert "Inno Setup compiler" in phase["error"]


def test_smoke_inno_script_records_iscc_start_failure_without_exit_code(
    tmp_path: Path,
) -> None:
    """Smoke installer sidecar omits exit-code when ISCC never starts."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    if os.name == "nt":
        pytest.skip("POSIX shebang start-failure probe is not portable")
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "telemetry.json"
    publish_root.mkdir()
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")
    fake_iscc = tmp_path / "fake-iscc"
    fake_iscc.write_text(
        "\n".join(
            [
                "#!/definitely/missing/iscc-interpreter",
                "echo 'unreachable'",
            ],
        ),
        encoding="utf-8",
    )
    fake_iscc.chmod(fake_iscc.stat().st_mode | stat.S_IXUSR)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_smoke_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == ["iscc-compile"]
    phase = phases[0]
    assert phase["outcome"] == "failure"
    assert "exit-code" not in phase
    assert "ISCC launch failed before producing an exit code" in phase["error"]
    assert "unreachable" not in phase["error"]


def test_smoke_inno_telemetry_write_failure_preserves_iscc_failure(
    tmp_path: Path,
) -> None:
    """Best-effort telemetry warnings do not mask the ISCC failure."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_parent = tmp_path / "telemetry-parent-file"
    telemetry_path = telemetry_parent / "telemetry.json"
    publish_root.mkdir()
    telemetry_parent.write_text("not a directory", encoding="utf-8")
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")
    fake_iscc = tmp_path / "fake-iscc"
    fake_iscc.write_text(
        "\n".join(
            [
                "#!/usr/bin/env bash",
                "echo 'error: invalid Setup directive' >&2",
                "exit 42",
            ],
        ),
        encoding="utf-8",
    )
    fake_iscc.chmod(fake_iscc.stat().st_mode | stat.S_IXUSR)
    command = (
        "$WarningPreference = 'Stop'; "
        f"& {_ps_single_quote(_smoke_inno_installer_script())} "
        f"-PublishOutputRoot {_ps_single_quote(publish_root)} "
        f"-InstallerOutputPath {_ps_single_quote(installer_root)} "
        f"-InnoSetupCompiler {_ps_single_quote(fake_iscc)} "
        f"-TelemetryOutputPath {_ps_single_quote(telemetry_path)}"
    )

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-Command",
            command,
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "Inno Setup failed, exit code: 42" in result.stderr
    assert "directive" in result.stderr
    assert (
        "Profile telemetry could not be written"
        in f"{result.stdout}\n{result.stderr}"
    )


def test_powershell_profile_telemetry_warnings_ignore_warning_preference() -> (
    None
):
    """All PowerShell telemetry write warnings force non-terminating output."""
    for script in _powershell_telemetry_scripts():
        warning_lines = [
            line
            for line in script.read_text(encoding="utf-8").splitlines()
            if "Profile telemetry could not be written" in line
        ]
        assert warning_lines, script
        assert all("-WarningAction Continue" in line for line in warning_lines)


def test_powershell_profile_phase_timing_corrects_clock_drift(
    tmp_path: Path,
) -> None:
    """PowerShell sidecars keep completed-at aligned to stopwatch duration."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for profile timing tests")
    scripts = (
        _smoke_inno_publish_script(),
        _smoke_inno_installer_script(),
        _image_occlusion_publish_script(),
        _image_occlusion_inno_installer_script(),
    )
    for script in scripts:
        script_text = script.read_text(encoding="utf-8")
        functions = []
        if "function Convert-ProfileTelemetryText" in script_text:
            functions.append(
                _extract_powershell_function(
                    script_text,
                    "Convert-ProfileTelemetryText",
                )
            )
        functions.append(
            _extract_powershell_function(script_text, "Add-ProfilePhase")
        )
        harness = tmp_path / f"{script.parent.parent.name}-timing.ps1"
        harness.write_text(
            "\n".join(
                [
                    "$ErrorActionPreference = 'Stop'",
                    "Set-StrictMode -Version Latest",
                    (
                        "$profilePhases = "
                        "[System.Collections.Generic.List[object]]::new()"
                    ),
                    *functions,
                    (
                        "function Get-Date { "
                        "[datetime]::Parse("
                        "'2026-06-26T05:00:04.500Z', "
                        "[System.Globalization.CultureInfo]::InvariantCulture, "
                        "[System.Globalization.DateTimeStyles]::AdjustToUniversal"
                        ") }"
                    ),
                    (
                        "$startedAt = [datetime]::Parse("
                        "'2026-06-26T05:00:00.000Z', "
                        "[System.Globalization.CultureInfo]::InvariantCulture, "
                        "[System.Globalization.DateTimeStyles]::AdjustToUniversal"
                        ")"
                    ),
                    "$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()",
                    (
                        "while ($stopwatch.ElapsedMilliseconds -lt "
                        f"{POWERSHELL_TIMING_MIN_DURATION_MS}) {{ "
                        "Start-Sleep -Milliseconds 1 }"
                    ),
                    "$stopwatch.Stop()",
                    (
                        "Add-ProfilePhase -Phase 'drift-test' "
                        "-StartedAt $startedAt -Stopwatch $stopwatch "
                        "-Outcome 'success'"
                    ),
                    "$profilePhases[0] | ConvertTo-Json -Depth 8",
                ]
            ),
            encoding="utf-8",
        )

        result = subprocess.run(  # noqa: S603
            [
                pwsh,
                "-NoLogo",
                "-NoProfile",
                "-File",
                str(harness),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        assert result.returncode == 0, result.stderr
        record = json.loads(result.stdout)
        started_at = datetime.fromisoformat(
            record["started-at"].replace("Z", "+00:00")
        )
        completed_at = datetime.fromisoformat(
            record["completed-at"].replace("Z", "+00:00")
        )
        expected_completed_at = started_at + timedelta(
            milliseconds=record["duration-ms"],
        )
        elapsed_ms = int(
            (completed_at - started_at).total_seconds()
            * TIMING_DURATION_TOLERANCE_MS
        )
        assert record["duration-ms"] >= POWERSHELL_TIMING_MIN_DURATION_MS
        assert record["completed-at"] != "2026-06-26T05:00:04.500Z"
        assert (
            abs(
                (completed_at - expected_completed_at).total_seconds()
                * TIMING_DURATION_TOLERANCE_MS
            )
            <= 1
        )
        assert (
            abs(elapsed_ms - record["duration-ms"])
            <= TIMING_DURATION_TOLERANCE_MS
        )


def test_image_occlusion_inno_telemetry_omits_deleted_temp_paths(
    tmp_path: Path,
) -> None:
    """ImageOcclusion Inno sidecar aliases deleted staging paths."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    fake_iscc = _write_fake_image_occlusion_iscc(tmp_path / "fake-iscc")
    env = _image_occlusion_inno_env(runner_temp)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0, result.stderr
    telemetry_text = telemetry_path.read_text(encoding="utf-8-sig")
    assert str(runner_temp) not in telemetry_text
    assert "inno-work:" in telemetry_text
    telemetry = json.loads(telemetry_text)
    phase_names = [phase["phase"] for phase in telemetry["phases"]]
    assert "inno-staging-copy" in phase_names
    assert "iscc-compile" in phase_names
    assert "inno-temp-cleanup" in phase_names


def test_image_occlusion_inno_required_input_failure_writes_telemetry(
    tmp_path: Path,
) -> None:
    """Missing required Inno inputs emit staging telemetry before rethrowing."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    project_root = _copy_image_occlusion_inno_test_project(
        tmp_path,
        missing_required_input="LICENSE.MIT.txt",
    )
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    fake_iscc = tmp_path / "fake-iscc.ps1"
    fake_iscc.write_text("exit 0", encoding="utf-8")
    env = _image_occlusion_inno_env(runner_temp)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(project_root / "script/Build-InnoInstaller.ps1"),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "Required Inno Setup input not found" in result.stderr
    assert "LICENSE.MIT.txt" in result.stderr
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    phase_names = [phase["phase"] for phase in phases]
    assert phase_names == ["inno-staging-copy"]
    staging_phase = phases[0]
    assert staging_phase["outcome"] == "failure"
    assert "Required Inno Setup input not found" in staging_phase["error"]
    assert "LICENSE.MIT.txt" in staging_phase["error"]
    assert list(runner_temp.glob("image-occlusion-inno-*")) == []


def test_image_occlusion_inno_copy_back_failure_writes_telemetry(
    tmp_path: Path,
) -> None:
    """Copy-back failures record installer-copy-back before cleanup."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    fake_iscc = _write_fake_image_occlusion_iscc(tmp_path / "fake-iscc")
    env = _image_occlusion_inno_env(runner_temp)
    env["TEST_INSTALLER_OUTPUT_PATH"] = str(installer_root)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry_text = telemetry_path.read_text(encoding="utf-8-sig")
    assert str(runner_temp) not in telemetry_text
    telemetry = json.loads(telemetry_text)
    phases = telemetry["phases"]
    phase_names = [phase["phase"] for phase in phases]
    copy_back_index = phase_names.index("installer-copy-back")
    cleanup_index = phase_names.index("inno-temp-cleanup")
    assert copy_back_index < cleanup_index
    copy_back = [
        phase for phase in phases if phase["phase"] == "installer-copy-back"
    ]
    assert len(copy_back) == 1
    assert copy_back[0]["outcome"] == "failure"
    cleanup = phases[cleanup_index]
    assert cleanup["outcome"] == "success"
    assert list(runner_temp.glob("image-occlusion-inno-*")) == []


def test_image_occlusion_inno_nonzero_failure_records_diagnostics(
    tmp_path: Path,
) -> None:
    """ImageOcclusion installer sidecar records diagnostics on ISCC nonzero."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    fake_iscc = _write_failing_image_occlusion_iscc(tmp_path / "fake-iscc")
    expected_exit_code = 42
    env = _image_occlusion_inno_env(runner_temp)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    iscc_phase = next(
        phase for phase in phases if phase["phase"] == "iscc-compile"
    )
    cleanup_phase = next(
        phase for phase in phases if phase["phase"] == "inno-temp-cleanup"
    )
    assert phases.index(iscc_phase) < phases.index(cleanup_phase)
    assert iscc_phase["outcome"] == "failure"
    assert iscc_phase["exit-code"] == expected_exit_code
    assert "ISCC output:" in iscc_phase["error"]
    assert "invalid Setup directive" in iscc_phase["error"]
    assert "super-secret-token" not in iscc_phase["error"]
    assert "token=<redacted>" in iscc_phase["error"]
    assert str(runner_temp) not in iscc_phase["error"]
    assert "inno-work:" in iscc_phase["error"]
    assert cleanup_phase["outcome"] == "success"
    assert list(runner_temp.glob("image-occlusion-inno-*")) == []


def test_image_occlusion_inno_records_missing_iscc_resolution_failure(
    tmp_path: Path,
) -> None:
    """ImageOcclusion installer records telemetry when ISCC cannot resolve."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    missing_iscc = tmp_path / "missing-iscc"
    env = _image_occlusion_inno_env(runner_temp)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(missing_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == [
        "iscc-compiler-resolution",
    ]
    phase = phases[0]
    assert phase["outcome"] == "failure"
    assert "exit-code" not in phase
    assert str(missing_iscc) in phase["error"]


def test_image_occlusion_inno_no_hint_iscc_resolution_failure_uses_empty_argv(
    tmp_path: Path,
) -> None:
    """ImageOcclusion installer emits empty argv without an ISCC hint."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    empty_bin = tmp_path / "empty-bin"
    empty_bin.mkdir()
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    env = _image_occlusion_inno_env(runner_temp)
    env["PATH"] = str(empty_bin)
    if os.name == "nt":
        env["Path"] = str(empty_bin)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phase = telemetry["phases"][0]
    assert phase["phase"] == "iscc-compiler-resolution"
    assert phase["outcome"] == "failure"
    assert phase.get("argv", []) == []
    assert "exit-code" not in phase
    assert "Inno Setup compiler" in phase["error"]


def test_image_occlusion_inno_records_iscc_start_failure_without_exit_code(
    tmp_path: Path,
) -> None:
    """ImageOcclusion installer omits exit-code when ISCC never starts."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for ImageOcclusion Inno tests")
    if os.name == "nt":
        pytest.skip("POSIX shebang start-failure probe is not portable")
    publish_root = _prepare_image_occlusion_publish_output(tmp_path)
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "profile" / "powershell" / "installer.json"
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    fake_iscc = tmp_path / "fake-iscc"
    fake_iscc.write_text(
        "\n".join(
            [
                "#!/definitely/missing/iscc-interpreter",
                "echo 'unreachable'",
            ],
        ),
        encoding="utf-8",
    )
    fake_iscc.chmod(fake_iscc.stat().st_mode | stat.S_IXUSR)
    env = _image_occlusion_inno_env(runner_temp)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_image_occlusion_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        env=env,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    iscc_phase = next(
        phase for phase in phases if phase["phase"] == "iscc-compile"
    )
    assert iscc_phase["outcome"] == "failure"
    assert "exit-code" not in iscc_phase
    assert (
        "ISCC launch failed before producing an exit code"
        in iscc_phase["error"]
    )
    assert "unreachable" not in iscc_phase["error"]


def test_smoke_inno_script_records_missing_installer_validation_failure(
    tmp_path: Path,
) -> None:
    """Smoke installer sidecar records absent output as failure."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is required for smoke Inno script tests")
    publish_root = tmp_path / "publish"
    installer_root = tmp_path / "installer"
    telemetry_path = tmp_path / "telemetry.json"
    publish_root.mkdir()
    (publish_root / "hcoona-release-smoke-inno.exe").write_bytes(b"MZapp")
    fake_iscc = tmp_path / "fake-iscc"
    fake_iscc.write_text(
        "\n".join(
            [
                "#!/usr/bin/env bash",
                "echo 'ISCC compiler completed without producing an installer'",
                "exit 0",
            ],
        ),
        encoding="utf-8",
    )
    fake_iscc.chmod(fake_iscc.stat().st_mode | stat.S_IXUSR)

    result = subprocess.run(  # noqa: S603
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-File",
            str(_smoke_inno_installer_script()),
            "-PublishOutputRoot",
            str(publish_root),
            "-InstallerOutputPath",
            str(installer_root),
            "-InnoSetupCompiler",
            str(fake_iscc),
            "-TelemetryOutputPath",
            str(telemetry_path),
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    telemetry = json.loads(telemetry_path.read_text(encoding="utf-8-sig"))
    phases = telemetry["phases"]
    assert [phase["phase"] for phase in phases] == [
        "iscc-compile",
        "installer-output-validation",
    ]
    assert phases[0]["outcome"] == "success"
    assert phases[1]["outcome"] == "failure"
    assert "expected installer was not found" in phases[1]["error"]


def _smoke_inno_installer_script() -> Path:
    script = "src/public/lib/hcoona-release-smoke-inno/script"
    return REPO_ROOT / script / "Build-InnoInstaller.ps1"


def _smoke_inno_publish_script() -> Path:
    return (
        REPO_ROOT
        / "src/public/lib/hcoona-release-smoke-inno/script"
        / "Publish.ps1"
    )


def _image_occlusion_publish_script() -> Path:
    return (
        REPO_ROOT
        / "src/public/app/ImageOcclusionEditor/script"
        / "Publish-ImageOcclusionEditor.ps1"
    )


def _powershell_telemetry_scripts() -> tuple[Path, ...]:
    smoke_script_dir = (
        REPO_ROOT / "src/public/lib/hcoona-release-smoke-inno/script"
    )
    image_script_dir = REPO_ROOT / "src/public/app/ImageOcclusionEditor/script"
    return (
        smoke_script_dir / "Publish.ps1",
        smoke_script_dir / "Build-InnoInstaller.ps1",
        image_script_dir / "Publish-ImageOcclusionEditor.ps1",
        image_script_dir / "Build-InnoInstaller.ps1",
    )


def _image_occlusion_inno_installer_script() -> Path:
    return (
        REPO_ROOT
        / "src/public/app/ImageOcclusionEditor/script/Build-InnoInstaller.ps1"
    )


def _image_occlusion_project_metadata() -> tuple[str, str, str]:
    app_path = "src/public/app/ImageOcclusionEditor"
    csproj = (
        REPO_ROOT
        / app_path
        / ("ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj")
    )
    project_text = csproj.read_text(encoding="utf-8")

    def property_text(name: str) -> str | None:
        match = re.search(rf"<{name}>(.*?)</{name}>", project_text)
        if match is None:
            return None
        value = match.group(1).strip()
        return value or None

    target_framework = property_text("TargetFramework")
    assembly_name = property_text("AssemblyName") or "ImageOcclusionEditor"
    runtime_identifier = property_text("RuntimeIdentifier")
    if runtime_identifier is None:
        runtime_identifiers = property_text("RuntimeIdentifiers")
        if runtime_identifiers is not None:
            runtime_identifier = next(
                (
                    item.strip()
                    for item in runtime_identifiers.split(";")
                    if item.strip()
                ),
                None,
            )
    assert target_framework is not None
    assert runtime_identifier is not None
    return target_framework, runtime_identifier, assembly_name


def _prepare_image_occlusion_publish_output(tmp_path: Path) -> Path:
    target_framework, runtime_identifier, assembly_name = (
        _image_occlusion_project_metadata()
    )
    publish_root = tmp_path / "publish-root"
    publish_dir = (
        publish_root
        / "ImageOcclusionEditor"
        / "Release"
        / target_framework
        / runtime_identifier
    )
    publish_dir.mkdir(parents=True)
    _write_versioned_dotnet_assembly_as_exe(
        publish_dir / f"{assembly_name}.exe",
        tmp_path,
        assembly_name,
    )
    return publish_root


def _write_versioned_dotnet_assembly_as_exe(
    target: Path,
    tmp_path: Path,
    assembly_name: str,
) -> None:
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        pytest.skip("dotnet is required to create a versioned Windows exe")
    source_dir = tmp_path / "versioned-exe-src"
    create_result = subprocess.run(  # noqa: S603
        [dotnet, "new", "console", "--no-restore", "-o", str(source_dir)],
        check=False,
        capture_output=True,
        text=True,
    )
    if create_result.returncode != 0:
        pytest.skip(f"dotnet new failed: {create_result.stderr}")
    publish_result = subprocess.run(  # noqa: S603
        # The binlog keeps this test helper compliant when targeted tests
        # exercise ImageOcclusion PowerShell behavior through a real assembly.
        [
            dotnet,
            "publish",
            str(source_dir),
            "-c",
            "Release",
            (
                "/bl:"
                + (
                    tmp_path / f"{assembly_name}-versioned-exe-publish.binlog"
                ).as_posix()
            ),
            f"/p:AssemblyName={assembly_name}",
            "/p:FileVersion=1.2.3.4",
            "/p:AssemblyVersion=1.2.3.4",
        ],
        check=False,
        capture_output=True,
        text=True,
    )
    if publish_result.returncode != 0:
        pytest.skip(f"dotnet publish failed: {publish_result.stderr}")
    candidates = sorted(
        (source_dir / "bin").glob(
            f"Release/*/publish/{assembly_name}.dll",
        ),
    )
    assert candidates
    shutil.copy2(candidates[0], target)


def _write_fake_image_occlusion_dotnet(
    path: Path,
    *,
    cyclone_success: bool,
    cyclone_failure_exit_codes: tuple[int, int, int] = (42, 42, 42),
) -> Path:
    (
        cyclone_tool_run_failure_exit_code,
        cyclone_dotnet_tool_failure_exit_code,
        cyclone_dotnet_global_failure_exit_code,
    ) = cyclone_failure_exit_codes
    cyclone_lines = (
        [
            "    out=''",
            "    previous=''",
            '    for arg in "$@"; do',
            '        if [ "$previous" = \'-o\' ]; then out="$arg"; fi',
            '        previous="$arg"',
            "    done",
            '    mkdir -p "$out"',
            (
                "    printf '%s' '{\"bomFormat\":\"CycloneDX\"}' "
                '> "$out/bom.json"'
            ),
            "    exit 0",
        ]
        if cyclone_success
        else [
            "    echo 'fake CycloneDX failed' >&2",
            f"    exit {cyclone_tool_run_failure_exit_code}",
        ]
    )
    cyclone_fallback_lines = (
        []
        if cyclone_success
        else [
            "if [ \"${1:-}\" = 'dotnet-CycloneDX' ]; then",
            f"    exit {cyclone_dotnet_tool_failure_exit_code}",
            "fi",
            "if [ \"${1:-}\" = 'CycloneDX' ]; then",
            f"    exit {cyclone_dotnet_global_failure_exit_code}",
            "fi",
        ]
    )
    path.write_text(
        "\n".join(
            [
                "#!/usr/bin/env bash",
                "set -euo pipefail",
                *_fake_argv_log_lines(),
                "if [ \"${1:-}\" = 'publish' ]; then exit 0; fi",
                "if [ \"${1:-}\" = 'tool' ] "
                "&& [ \"${2:-}\" = 'run' ] "
                "&& [ \"${3:-}\" = 'dotnet-CycloneDX' ]; then",
                *cyclone_lines,
                "fi",
                *cyclone_fallback_lines,
                "exit 42",
            ],
        ),
        encoding="utf-8",
    )
    path.chmod(path.stat().st_mode | stat.S_IXUSR)
    return path


def _fake_argv_log_lines() -> list[str]:
    return [
        'if [ -n "${CYCLONEDX_FAKE_ARGV_LOG:-}" ]; then',
        '    python3 - "$CYCLONEDX_FAKE_ARGV_LOG" "$0" "$@" <<\'PY\'',
        "import json",
        "import sys",
        "with open(sys.argv[1], 'a', encoding='utf-8') as f:",
        "    f.write(json.dumps(sys.argv[2:]) + '\\n')",
        "PY",
        "fi",
    ]


def _write_failing_executable(path: Path, *, exit_code: int = 42) -> Path:
    path.write_text(
        "\n".join(
            [
                "#!/usr/bin/env bash",
                *_fake_argv_log_lines(),
                "echo 'fake command failed' >&2",
                f"exit {exit_code}",
            ],
        ),
        encoding="utf-8",
    )
    path.chmod(path.stat().st_mode | stat.S_IXUSR)
    return path


def _write_fake_image_occlusion_iscc(path: Path) -> Path:
    if path.suffix.lower() != ".ps1":
        path = path.with_suffix(".ps1")
    path.write_text(
        "\n".join(
            [
                "$ErrorActionPreference = 'Stop'",
                "$out = ''",
                "foreach ($arg in $args) {",
                "    if ($arg.StartsWith('/O')) {",
                "        $out = $arg.Substring(2)",
                "    }",
                "}",
                "if ([string]::IsNullOrWhiteSpace($out)) {",
                "    Write-Error 'Missing /O output argument.'",
                "    exit 2",
                "}",
                "New-Item -ItemType Directory -Force -Path $out | Out-Null",
                "$name = 'ImageOcclusionEditorWinUI3_Setup.exe'",
                "$installer = Join-Path $out $name",
                "$text = 'MZinstaller'",
                "$bytes = [System.Text.Encoding]::ASCII.GetBytes($text)",
                "[System.IO.File]::WriteAllBytes($installer, $bytes)",
                "$conflict = $env:TEST_INSTALLER_OUTPUT_PATH",
                "if (-not [string]::IsNullOrWhiteSpace($conflict)) {",
                "    if (Test-Path -LiteralPath $conflict) {",
                "        Remove-Item -LiteralPath $conflict -Recurse -Force",
                "    }",
                "    Set-Content -LiteralPath $conflict "
                "-Value 'copy-back path conflict' -Encoding UTF8",
                "}",
                "exit 0",
            ],
        ),
        encoding="utf-8",
    )
    return path


def _write_failing_image_occlusion_iscc(path: Path) -> Path:
    if path.suffix.lower() != ".ps1":
        path = path.with_suffix(".ps1")
    path.write_text(
        "\n".join(
            [
                "$ErrorActionPreference = 'Continue'",
                "Write-Output 'ISCC compiler started'",
                "Write-Output 'token=super-secret-token'",
                "Write-Error 'error: invalid Setup directive'",
                "exit 42",
            ],
        ),
        encoding="utf-8",
    )
    return path


def _image_occlusion_inno_env(runner_temp: Path) -> dict[str, str]:
    env = os.environ.copy()
    env.pop("IMAGE_OCCLUSION_EDITOR_KEEP_INNO_TEMP", None)
    env.pop("TEST_INSTALLER_OUTPUT_PATH", None)
    temp_path = str(runner_temp)
    env["RUNNER_TEMP"] = temp_path
    env["TEMP"] = temp_path
    env["TMP"] = temp_path
    env["TMPDIR"] = temp_path
    return env


def _ps_single_quote(value: Path) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def _copy_image_occlusion_inno_test_project(
    tmp_path: Path,
    *,
    missing_required_input: str | None = None,
) -> Path:
    target_framework, runtime_identifier, assembly_name = (
        _image_occlusion_project_metadata()
    )
    project_root = tmp_path / "image-occlusion-project"
    script_dir = project_root / "script"
    script_dir.mkdir(parents=True)
    source_script_dir = REPO_ROOT / "src/public/app/ImageOcclusionEditor/script"
    for name in ("Build-InnoInstaller.ps1", "Helpers.ps1", "Setup.iss"):
        shutil.copy2(source_script_dir / name, script_dir / name)
    csproj_dir = project_root / "ImageOcclusionEditorWinUI3"
    csproj_dir.mkdir()
    (csproj_dir / "ImageOcclusionEditorWinUI3.csproj").write_text(
        "\n".join(
            [
                '<Project Sdk="Microsoft.NET.Sdk">',
                "  <PropertyGroup>",
                f"    <TargetFramework>{target_framework}</TargetFramework>",
                f"    <AssemblyName>{assembly_name}</AssemblyName>",
                (
                    f"    <RuntimeIdentifier>{runtime_identifier}"
                    "</RuntimeIdentifier>"
                ),
                "  </PropertyGroup>",
                "</Project>",
            ]
        ),
        encoding="utf-8",
    )
    for relative_path in (
        "imageocclusioneditor.ico",
        "README.md",
        "LICENSE",
        "LICENSE.GPL3.txt",
        "LICENSE.MIT.txt",
        "THIRD-PARTY-NOTICES.TXT",
    ):
        if relative_path == missing_required_input:
            continue
        (project_root / relative_path).write_text(
            f"test fixture for {relative_path}",
            encoding="utf-8",
        )
    return project_root


def _extract_powershell_function(script_text: str, function_name: str) -> str:
    match = re.search(
        rf"^function\s+{re.escape(function_name)}\s*\{{",
        script_text,
        flags=re.MULTILINE,
    )
    assert match is not None, function_name
    index = script_text.index("{", match.start())
    depth = 0
    for position in range(index, len(script_text)):
        char = script_text[position]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return script_text[match.start() : position + 1]
    message = f"unterminated PowerShell function: {function_name}"
    raise AssertionError(message)


def test_executor_fails_closed_on_missing_output() -> None:
    """Reject a build command that does not produce requested artifacts."""
    scratch = REPO_ROOT / ".build-executor-missing-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            return subprocess.CompletedProcess(args, 0, "", "")

        with pytest.raises(BuildExecutorError, match="expected 1 output"):
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
                check_commit=False,
            )
    finally:
        _remove_tree_scratch(scratch)


def test_executor_fails_closed_on_commit_mismatch() -> None:
    """Reject a checkout that is not pinned to build-request commit-sha."""
    scratch = REPO_ROOT / ".build-executor-commit-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            return subprocess.CompletedProcess(args, 0, "b" * 40 + "\n", "")

        with pytest.raises(BuildExecutorError, match="does not match"):
            execute_build(request, REPO_ROOT, scratch / "bundle", runner=runner)
    finally:
        _remove_tree_scratch(scratch)


def test_executor_builds_from_detached_pinned_worktree() -> None:
    """Build commands use a materialized commit, not the ambient checkout."""
    scratch = REPO_ROOT / ".build-executor-worktree-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        build_cwds: list[Path] = []

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                Path(args[-2]).mkdir(parents=True)
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "remove"]:
                _remove_tree(Path(args[-1]))
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "prune"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            build_cwds.append(cwd)
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
        )

        validate_contract(result)
        assert build_cwds
        assert scratch / "example" not in build_cwds[0].parents
        assert "git-common" in build_cwds[0].as_posix()
        assert not (
            scratch / "git-common" / "three-workflow-release-build-worktrees"
        ).exists()
    finally:
        _remove_tree_scratch(scratch)


def test_executor_ignores_cleanup_oserror_after_success() -> None:
    """Best-effort worktree cleanup must not fail a completed build."""
    scratch = REPO_ROOT / ".build-executor-cleanup-success-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                Path(args[-2]).mkdir(parents=True)
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "remove"]:
                message = "cleanup runner failed"
                raise OSError(message)
            if args[1:3] == ["worktree", "prune"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
        )

        validate_contract(result)
        assert set(_result_artifacts(result)) == {"artifact/wheel"}
    finally:
        _remove_tree_scratch(scratch)


def test_executor_cleanup_does_not_follow_symlinked_directories() -> None:
    """Fallback worktree cleanup must not remove symlink targets."""
    scratch = REPO_ROOT / ".build-executor-cleanup-symlink-test"
    _remove_tree_scratch(scratch)
    try:
        external = scratch / "external-target"
        external.mkdir(parents=True)
        external_file = external / "keep.txt"
        external_file.write_text("keep", encoding="utf-8")
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                worktree = Path(args[-2])
                worktree.mkdir(parents=True)
                (worktree / "external-link").symlink_to(
                    external, target_is_directory=True
                )
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "remove"]:
                return subprocess.CompletedProcess(args, 1, "", "remove failed")
            if args[1:3] == ["worktree", "prune"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            out_dir = Path(args[args.index("--out-dir") + 1])
            _write_python_wheel(
                out_dir / "example-1.2.3-py3-none-any.whl", "1.2.3"
            )
            return subprocess.CompletedProcess(args, 0, "", "")

        result = execute_build(
            request,
            REPO_ROOT,
            scratch / "bundle",
            runner=runner,
        )

        validate_contract(result)
        assert external_file.read_text(encoding="utf-8") == "keep"
        assert external.is_dir()
    finally:
        _remove_tree_scratch(scratch)


def test_executor_preserves_build_error_when_cleanup_raises_oserror() -> None:
    """Cleanup OSError must not mask the original closed build failure."""
    scratch = REPO_ROOT / ".build-executor-cleanup-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                Path(args[-2]).mkdir(parents=True)
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "remove"]:
                message = "cleanup runner failed"
                raise OSError(message)
            if args[1:3] == ["worktree", "prune"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            return subprocess.CompletedProcess(
                args, 1, "", "build command failed"
            )

        with pytest.raises(
            BuildExecutorError, match="build command failed"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
            )
        assert error.value.code == "BUILD_FAILED"
        assert error.value.phase == "execution"
    finally:
        _remove_tree_scratch(scratch)


def test_executor_wraps_stale_worktree_cleanup_oserror(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Stale materialization cleanup failures remain diagnostic-safe errors."""
    scratch = REPO_ROOT / ".build-executor-stale-cleanup-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        bundle_dir = scratch / "bundle"
        git_dir = scratch / "git-common"
        worktree_name = hashlib.sha256(
            f"{bundle_dir}\n{SHA}\n{request['plan-id']}".encode()
        ).hexdigest()[:24]
        stale_worktree = (
            git_dir / "three-workflow-release-build-worktrees" / worktree_name
        )
        stale_worktree.mkdir(parents=True)

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(args, 0, str(git_dir), "")
            return subprocess.CompletedProcess(args, 0, "", "")

        def fail_remove(path: Path) -> None:
            if path == stale_worktree:
                message = "stale cleanup failed"
                raise OSError(message)
            _remove_tree(path)

        monkeypatch.setattr(
            "three_workflow_release_build.executor._remove_tree",
            fail_remove,
        )

        with pytest.raises(
            BuildExecutorError, match="stale build worktree"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                bundle_dir,
                runner=runner,
            )
        assert error.value.code == "BUILD_CHECKOUT_FAILED"
        assert error.value.phase == "materialization"
    finally:
        _remove_tree_scratch(scratch)


def test_executor_preserves_checkout_error_when_cleanup_raises_oserror(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Checkout cleanup failures must not mask the original checkout failure."""
    scratch = REPO_ROOT / ".build-executor-checkout-cleanup-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                Path(args[-2]).mkdir(parents=True)
                return subprocess.CompletedProcess(
                    args, 1, "", "checkout failed"
                )
            return subprocess.CompletedProcess(args, 0, "", "")

        def fail_remove(_path: Path) -> None:
            message = "cleanup failed"
            raise OSError(message)

        monkeypatch.setattr(
            "three_workflow_release_build.executor._remove_tree",
            fail_remove,
        )

        with pytest.raises(
            BuildExecutorError, match="checkout failed"
        ) as error:
            execute_build(
                request,
                REPO_ROOT,
                scratch / "bundle",
                runner=runner,
            )
        assert error.value.code == "BUILD_CHECKOUT_FAILED"
        assert error.value.phase == "materialization"
    finally:
        _remove_tree_scratch(scratch)


def test_cli_writes_build_result(monkeypatch: pytest.MonkeyPatch) -> None:
    """Exercise the build executor CLI."""
    scratch = REPO_ROOT / ".build-executor-cli-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        def fake_execute_build(
            loaded_request: Mapping[str, object],
            _repo_root: Path,
            _bundle_dir: Path,
            *,
            check_commit: bool = True,
        ) -> dict[str, Any]:
            assert loaded_request["kind"] == "build-request"
            assert check_commit is True
            return {
                "api-version": "three.release.build-result/v1alpha1",
                "kind": "build-result",
                "plan-id": "plan/example",
                "project-id": "example",
                "variant-id": "variant/package",
                "artifacts": {},
            }

        monkeypatch.setattr(
            "three_workflow_release_build.cli.execute_build",
            fake_execute_build,
        )
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
            ],
        )

        assert cli_main() == 0
        validate_contract(json.loads(result_path.read_text(encoding="utf-8")))
    finally:
        _remove_tree_scratch(scratch)


def test_cli_writes_diagnostics_when_result_output_fails(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Result write failures are reported through build diagnostics."""
    scratch = REPO_ROOT / ".build-executor-cli-result-output-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "missing" / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        def fake_execute_build(
            _loaded_request: Mapping[str, object],
            _repo_root: Path,
            _bundle_dir: Path,
        ) -> dict[str, Any]:
            return {
                "api-version": "three.release.build-result/v1alpha1",
                "kind": "build-result",
                "plan-id": "plan/example",
                "project-id": "example",
                "variant-id": "variant/package",
                "artifacts": {},
            }

        monkeypatch.setattr(
            "three_workflow_release_build.cli.execute_build",
            fake_execute_build,
        )
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["code"] == "BUILD_OUTPUT_INVALID"
        assert diagnostic["phase"] == "receipt"
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def test_cli_writes_closed_diagnostics_on_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Failure diagnostics use a closed build-diagnostics contract."""
    scratch = REPO_ROOT / ".build-executor-cli-diagnostics-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        def fake_execute_build(
            _loaded_request: Mapping[str, object],
            _repo_root: Path,
            _bundle_dir: Path,
        ) -> dict[str, Any]:
            msg = "checkout HEAD does not match build commit"
            raise BuildExecutorError(
                msg,
                code="BUILD_CHECKOUT_FAILED",
                phase="materialization",
                details={"actual": "b" * 40, "expected": SHA},
            )

        monkeypatch.setattr(
            "three_workflow_release_build.cli.execute_build",
            fake_execute_build,
        )
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        assert diagnostics["diagnostics"][0]["code"] == "BUILD_CHECKOUT_FAILED"
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def test_cli_diagnostics_write_failure_preserves_original_error(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Diagnostics write failures do not mask the original build failure."""
    scratch = REPO_ROOT / ".build-executor-cli-diagnostics-write-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        blocking_parent = scratch / "diagnostics-parent"
        diagnostics_path = blocking_parent / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")
        blocking_parent.write_text("not a directory", encoding="utf-8")

        def fake_execute_build(
            _loaded_request: Mapping[str, object],
            _repo_root: Path,
            _bundle_dir: Path,
        ) -> dict[str, Any]:
            msg = "original controlled build failure"
            raise BuildExecutorError(
                msg,
                code="BUILD_FAILED",
                phase="execution",
            )

        monkeypatch.setattr(
            "three_workflow_release_build.cli.execute_build",
            fake_execute_build,
        )
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        stderr = capsys.readouterr().err
        assert "could not be written as build diagnostics JSON" in stderr
        assert "original controlled build failure" in stderr
        assert not diagnostics_path.exists()
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def test_cli_diagnostics_drop_invalid_empty_plan_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Invalid request plan IDs must not prevent diagnostic emission."""
    scratch = REPO_ROOT / ".build-executor-cli-empty-plan-diagnostics-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request["plan-id"] = ""
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["code"] == "BUILD_INVALID_INPUT"
        assert "plan-id" not in diagnostic
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def test_diagnostics_include_variant_identity_with_multiple_variants() -> None:
    """Variant diagnostics remain valid when a project has many variants."""
    scratch = REPO_ROOT / ".build-executor-diagnostics-variant-id-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        project = cast("dict[str, Any]", request["project"])
        project["variant-ids"] = ["variant/package", "variant/other"]
        error = BuildExecutorError(
            "variant build failed",
            scope_kind="variant",
            project_id="example",
            variant_id="variant/package",
        )

        diagnostics = cast(
            "dict[str, Any]",
            build_diagnostics_document(error, request=request),
        )

        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["project-id"] == "example"
        assert diagnostic["variant-id"] == "variant/package"
        assert "artifact-id" not in diagnostic
    finally:
        _remove_tree_scratch(scratch)


def test_diagnostics_fall_back_for_empty_request_scope_ids() -> None:
    """Empty request-derived scope IDs must not invalidate diagnostics."""
    scratch = REPO_ROOT / ".build-executor-empty-request-scope-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        variant = cast("dict[str, Any]", request["variant"])
        variant["project-id"] = ""
        error = BuildExecutorError("project build failed", scope_kind="project")

        diagnostics = cast(
            "dict[str, Any]",
            build_diagnostics_document(error, request=request),
        )

        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["scope-kind"] == "request"
        assert "project-id" not in diagnostic
        assert "variant-id" not in diagnostic
        assert "artifact-id" not in diagnostic
    finally:
        _remove_tree_scratch(scratch)


def test_diagnostics_fall_back_for_empty_error_scope_ids() -> None:
    """Empty error-derived scope IDs must not invalidate diagnostics."""
    error = BuildExecutorError(
        "artifact build failed",
        scope_kind="artifact",
        project_id="example",
        variant_id="variant/package",
        artifact_id="",
    )

    diagnostics = cast(
        "dict[str, Any]",
        build_diagnostics_document(error),
    )

    validate_contract(diagnostics)
    diagnostic = diagnostics["diagnostics"][0]
    assert diagnostic["scope-kind"] == "request"
    assert "project-id" not in diagnostic
    assert "variant-id" not in diagnostic
    assert "artifact-id" not in diagnostic


def test_diagnostics_include_artifact_identity() -> None:
    """Artifact diagnostics include every required scope identity."""
    scratch = REPO_ROOT / ".build-executor-diagnostics-artifact-id-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="node",
            artifacts={
                "artifact/npm": ("primary-package", "package", "npm-package"),
            },
        )
        error = BuildExecutorError(
            "artifact receipt failed",
            phase="receipt",
            scope_kind="artifact",
            project_id="example",
            variant_id="variant/package",
            artifact_id="artifact/npm",
        )

        diagnostics = cast(
            "dict[str, Any]",
            build_diagnostics_document(error, request=request),
        )

        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["project-id"] == "example"
        assert diagnostic["variant-id"] == "variant/package"
        assert diagnostic["artifact-id"] == "artifact/npm"
    finally:
        _remove_tree_scratch(scratch)


def test_diagnostics_fall_back_to_request_when_scope_identity_missing() -> None:
    """Diagnostics never emit contract-invalid partial scope identities."""
    error = BuildExecutorError(
        "artifact build failed",
        scope_kind="artifact",
    )

    diagnostics = cast(
        "dict[str, Any]",
        build_diagnostics_document(error),
    )

    validate_contract(diagnostics)
    diagnostic = diagnostics["diagnostics"][0]
    assert diagnostic["scope-kind"] == "request"
    assert "project-id" not in diagnostic
    assert "variant-id" not in diagnostic
    assert "artifact-id" not in diagnostic


@pytest.mark.parametrize(
    ("repo_root_arg", "failure_kind", "expected_code"),
    [
        ("missing-cwd", "missing-cwd", "BUILD_CHECKOUT_FAILED"),
        ("existing-cwd", "missing-executable", "BUILD_CHECKOUT_FAILED"),
    ],
)
def test_cli_writes_diagnostics_for_runner_os_errors(
    monkeypatch: pytest.MonkeyPatch,
    repo_root_arg: str,
    failure_kind: str,
    expected_code: str,
) -> None:
    """Missing cwd/executable failures still emit build diagnostics."""
    scratch = REPO_ROOT / f".build-executor-cli-oserror-{repo_root_arg}"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")
        repo_root = (
            scratch / "missing" if repo_root_arg == "missing-cwd" else scratch
        )
        if failure_kind == "missing-executable":
            monkeypatch.setenv("PATH", "")
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(repo_root),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        assert diagnostics["diagnostics"][0]["code"] == expected_code
        assert diagnostics["diagnostics"][0]["phase"] == "materialization"
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def test_cli_diagnostics_preserve_build_error_when_cleanup_raises_oserror(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """CLI diagnostics report the original failure if cleanup also fails."""
    scratch = REPO_ROOT / ".build-executor-cli-cleanup-failure-test"
    _remove_tree_scratch(scratch)
    try:
        request = _request(
            scratch,
            ecosystem="python",
            artifacts={
                "artifact/wheel": ("primary-package", "package", "wheel"),
            },
        )
        request_path = scratch / "request.json"
        result_path = scratch / "result.json"
        diagnostics_path = scratch / "diagnostics.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if args[1:3] == ["rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(args, 0, f"{SHA}\n", "")
            if args[1:3] == ["rev-parse", "--git-common-dir"]:
                return subprocess.CompletedProcess(
                    args, 0, str(scratch / "git-common"), ""
                )
            if args[1:3] == ["worktree", "add"]:
                Path(args[-2]).mkdir(parents=True)
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[1:3] == ["worktree", "remove"]:
                message = "cleanup runner failed"
                raise OSError(message)
            if args[1:3] == ["worktree", "prune"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            return subprocess.CompletedProcess(
                args, 1, "", "build command failed"
            )

        monkeypatch.setattr(
            "three_workflow_release_build.executor._subprocess_runner",
            runner,
        )
        monkeypatch.setattr(
            sys,
            "argv",
            [
                "three-workflow-release-build",
                "build",
                "--repo-root",
                str(REPO_ROOT),
                "--request",
                str(request_path),
                "--bundle-dir",
                str(scratch / "bundle"),
                "--result-out",
                str(result_path),
                "--diagnostics-out",
                str(diagnostics_path),
            ],
        )

        assert cli_main() == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["code"] == "BUILD_FAILED"
        assert diagnostic["phase"] == "execution"
        assert "build command failed" in diagnostic["message"]
        assert not result_path.exists()
    finally:
        _remove_tree_scratch(scratch)


def _remove_tree_scratch(path: Path) -> None:
    """Remove a repository-local scratch tree."""
    if path.exists():
        _remove_tree(path)


def _remove_tree(path: Path) -> None:
    """Remove a directory tree without following symlinked directories."""
    if path.is_symlink() or not path.is_dir():
        path.unlink()
        return
    for child in path.iterdir():
        _remove_tree(child)
    path.rmdir()


def _result_artifacts(result: Mapping[str, object]) -> Mapping[str, Any]:
    """Return the artifact receipt map from a build result."""
    return cast("Mapping[str, Any]", result["artifacts"])


def _write_npm_tarball(path: Path, entries: Mapping[str, bytes]) -> None:
    """Write a gzip tarball with deterministic test entries."""
    with tarfile.open(path, "w:gz") as archive:
        for name, data in entries.items():
            info = tarfile.TarInfo(name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))


def _handle_npm_matcher_command(
    args: Sequence[str],
    cwd: Path,
) -> subprocess.CompletedProcess[str] | None:
    """Let focused npm tests use the same Node-side matcher as production."""
    if args[1:3] == ["root", "-g"] or args[1] == "-e":
        return subprocess.run(  # noqa: S603
            args,
            cwd=cwd,
            check=False,
            capture_output=True,
            text=True,
        )
    return None


def _write_python_wheel(path: Path, version: str) -> None:
    """Write a minimal wheel containing core metadata."""
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(
            "example-1.2.3.dist-info/METADATA",
            f"Metadata-Version: 2.1\nName: example\nVersion: {version}\n",
        )


def _write_python_sdist(path: Path, version: str) -> None:
    """Write a minimal sdist containing PKG-INFO."""
    data = (
        f"Metadata-Version: 2.1\nName: example\nVersion: {version}\n"
    ).encode()
    with tarfile.open(path, "w:gz") as archive:
        info = tarfile.TarInfo("example-1.2.3/PKG-INFO")
        info.size = len(data)
        archive.addfile(info, io.BytesIO(data))


def _write_nuget_package(path: Path, version: str) -> None:
    """Write a minimal NuGet package containing nuspec metadata."""
    nuspec = (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
        f"<metadata><id>Example</id><version>{version}</version></metadata>"
        "</package>"
    )
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("Example.nuspec", nuspec)


def _write_browser_zip(path: Path, manifest_version: str) -> None:
    """Write a minimal browser extension zip containing manifest.json."""
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(
            "manifest.json",
            json.dumps(
                {
                    "manifest_version": 3,
                    "name": "hcoona-release-smoke-wxt",
                    "version": manifest_version,
                }
            ),
        )
