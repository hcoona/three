"""Session-owned authority snapshots and controlled process environment."""

from __future__ import annotations

import os
import shutil
import tempfile
from dataclasses import dataclass
from pathlib import Path, PurePath
from typing import TYPE_CHECKING, Self

from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.release.static_reference_model import (
    native_repository_path,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping

    from three_workflow_delivery_v3.release.static_reference_model import (
        StaticReferenceSourceKind,
    )
    from three_workflow_delivery_v3.release.static_reference_source import (
        StaticReferenceCandidate,
    )


class StaticReferenceCleanupError(RuntimeError):
    """A required exact Session-owned root could not be removed."""


def _materialized_candidate_path[PathT: PurePath](
    snapshot_root: PathT,
    logical_path: str,
) -> PathT:
    try:
        return native_repository_path(
            snapshot_root,
            logical_path,
            field="candidate.path",
        )
    except ValueError as error:
        message = "candidate path is not representable below the snapshot root"
        raise OSError(message) from error


@dataclass(frozen=True, slots=True)
class MaterializedAuthorityInvocation:
    """One candidate-scoped authority invocation."""

    root: Path
    snapshot_root: Path
    candidate_path: Path | None
    manifest_path: Path
    home: Path
    scratch: Path


def _authority_environment(
    *,
    home: Path,
    scratch: Path,
) -> dict[str, str]:
    environment: dict[str, str] = {}
    for name in (
        "COMSPEC",
        "DOTNET_ROOT",
        "DOTNET_ROOT_X64",
        "DYLD_LIBRARY_PATH",
        "LD_LIBRARY_PATH",
        "PATH",
        "PATHEXT",
        "SYSTEMROOT",
        "WINDIR",
    ):
        value = os.environ.get(name)
        if value:
            environment[name] = value

    home_value = str(home)
    scratch_value = str(scratch)
    environment.update(
        {
            "CI": "1",
            "DOTNET_CLI_HOME": home_value,
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
            "GIT_CONFIG_GLOBAL": os.devnull,
            "GIT_CONFIG_NOSYSTEM": "1",
            "HOME": home_value,
            "NPM_CONFIG_USERCONFIG": os.devnull,
            "NUGET_PACKAGES": str(scratch / "nuget-packages"),
            "PNPM_HOME": str(home / "pnpm"),
            "TEMP": scratch_value,
            "TMP": scratch_value,
            "TMPDIR": scratch_value,
            "USERPROFILE": home_value,
            "XDG_CACHE_HOME": str(scratch / "xdg-cache"),
            "XDG_CONFIG_HOME": str(home / "xdg-config"),
            "XDG_DATA_HOME": str(home / "xdg-data"),
        }
    )
    drive, tail = os.path.splitdrive(home_value)
    if drive:
        environment["HOMEDRIVE"] = drive
        environment["HOMEPATH"] = tail or os.sep
    return environment


class StaticReferenceSession:
    """Own exact temporary roots for one bounded policy invocation."""

    def __init__(
        self,
        *,
        parent: Path | None = None,
        cleanup: Callable[[Path], None] | None = None,
    ) -> None:
        """Create one exact process-owned Session root."""
        parent_value: str | None = None
        if parent is not None:
            parent_path = parent.resolve(strict=True)
            if not parent_path.is_dir():
                message = "session parent must be an existing directory"
                raise ValueError(message)
            parent_value = str(parent_path)
        self._root = Path(
            tempfile.mkdtemp(
                prefix="wdv3-static-reference-",
                dir=parent_value,
            )
        ).resolve(strict=True)
        self._cleanup = cleanup or self._remove_tree
        self._closed = False
        self._next_invocation = 0
        self._invocation_roots: set[Path] = set()

    @staticmethod
    def _remove_tree(path: Path) -> None:
        shutil.rmtree(path)

    @property
    def root(self) -> Path:
        """Return the exact owned root."""
        return self._root

    def materialize(
        self,
        candidate: StaticReferenceCandidate,
        *,
        source_kind: StaticReferenceSourceKind,
        target: str | None,
    ) -> MaterializedAuthorityInvocation:
        """Materialize one candidate and no undeclared companion."""
        if self._closed:
            message = "static-reference Session is already closed"
            raise RuntimeError(message)
        invocation_root = self._root / (
            f"invocation-{self._next_invocation:04d}"
        )
        self._next_invocation += 1
        snapshot_root = invocation_root / "snapshot"
        home = invocation_root / "home"
        scratch = invocation_root / "scratch"
        manifest_path = invocation_root / "materialization.json"
        for directory in (snapshot_root, home, scratch):
            directory.mkdir(parents=True, exist_ok=False)
        self._invocation_roots.add(invocation_root)

        candidate_path: Path | None = None
        if candidate.selection.input_mode == "strict-utf8-file":
            candidate_path = _materialized_candidate_path(
                snapshot_root,
                candidate.path,
            )
            candidate_path.parent.mkdir(parents=True, exist_ok=True)
            with candidate_path.open("xb") as stream:
                stream.write(candidate.content)

        source_document: dict[str, JsonValue] = {
            "source-kind": source_kind,
            "logical-path": candidate.path,
            "byte-length": len(candidate.content),
            "content-sha256": candidate.content_sha256,
            "utf8-bom": candidate.has_utf8_bom,
            "input-mode": candidate.selection.input_mode,
            "authority-graph": candidate.selection.graph_id,
        }
        if target is not None:
            source_document["target"] = target
        if candidate.source_object is not None:
            source_document["source-object"] = candidate.source_object
        with manifest_path.open("xb") as stream:
            stream.write(canonicalize(source_document))

        return MaterializedAuthorityInvocation(
            root=invocation_root,
            snapshot_root=snapshot_root,
            candidate_path=candidate_path,
            manifest_path=manifest_path,
            home=home,
            scratch=scratch,
        )

    def environment_for(
        self,
        invocation: MaterializedAuthorityInvocation,
    ) -> Mapping[str, str]:
        """Return the closed environment for one authority process."""
        if invocation.root.parent != self._root:
            message = "authority invocation is not owned by this Session"
            raise ValueError(message)
        if invocation.root not in self._invocation_roots:
            message = "authority invocation is no longer active"
            raise ValueError(message)
        return _authority_environment(
            home=invocation.home,
            scratch=invocation.scratch,
        )

    def release(self, invocation: MaterializedAuthorityInvocation) -> None:
        """Remove one exact candidate invocation root."""
        if invocation.root not in self._invocation_roots:
            message = "authority invocation is not active in this Session"
            raise ValueError(message)
        try:
            self._cleanup(invocation.root)
        except OSError as error:
            message = "static-reference invocation cleanup failed"
            raise StaticReferenceCleanupError(message) from error
        self._invocation_roots.remove(invocation.root)

    def close(self) -> None:
        """Remove the exact owned root or raise one bounded cleanup failure."""
        if self._closed:
            return
        self._closed = True
        try:
            self._cleanup(self._root)
        except OSError as error:
            message = "static-reference Session cleanup failed"
            raise StaticReferenceCleanupError(message) from error
        self._invocation_roots.clear()

    def __enter__(self) -> Self:
        """Return this open Session."""
        return self

    def __exit__(
        self,
        exc_type: object,
        exc_value: object,
        traceback: object,
    ) -> None:
        """Always run required exact-root cleanup."""
        del exc_type, exc_value, traceback
        self.close()


__all__ = [
    "MaterializedAuthorityInvocation",
    "StaticReferenceCleanupError",
    "StaticReferenceSession",
]
