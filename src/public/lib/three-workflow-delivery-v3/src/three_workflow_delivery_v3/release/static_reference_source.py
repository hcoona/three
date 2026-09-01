"""Exact Git, index, and worktree source acquisition."""

from __future__ import annotations

import hashlib
import os
import re
import shutil
import stat
import subprocess
from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path, PurePath, PurePosixPath
from typing import Literal

from three_workflow_delivery_v3.release.static_reference_model import (
    StaticReferenceFamily,
    StaticReferenceSourceKind,
    native_repository_path,
    normalized_repository_path,
    utf8_sort_key,
)

type AuthorityGraphId = Literal[
    "npm-manifest-v1",
    "pnpm-lock-v1",
    "pnpm-workspace-v1",
    "nuget-lock-v1",
]
type StaticReferenceInputMode = Literal[
    "strict-utf8-file",
    "strict-utf8-byte-stream",
    "xml-byte-stream",
]

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_REGULAR_GIT_MODES = frozenset({"100644", "100755"})
_GIT_METADATA_FIELD_COUNT = 3
_UTF8_BOM = b"\xef\xbb\xbf"


class _SourceDiagnostic(StrEnum):
    GIT_BLOB_UNREADABLE = "git-blob-unreadable"
    GIT_COMMAND_FAILED = "git-command-failed"
    GIT_EXECUTABLE_UNAVAILABLE = "git-executable-unavailable"
    GIT_PATH_REJECTED = "git-path-rejected"
    GIT_TARGET_NOT_EXACT = "git-target-not-exact"
    GIT_TARGET_UNAVAILABLE = "git-target-unavailable"
    DUPLICATE_GIT_PATH = "duplicate-git-path"
    MALFORMED_GIT_ENTRY = "malformed-git-entry"
    MALFORMED_GIT_INDEX_STAGE = "malformed-git-index-stage"
    UNMERGED_INDEX_ENTRY = "unmerged-index-entry"
    UNSUPPORTED_GIT_MODE = "unsupported-git-mode"
    WORKTREE_FILE_UNREADABLE = "worktree-file-unreadable"
    WORKTREE_FILE_UNSUPPORTED = "worktree-file-unsupported"
    WORKTREE_PARENT_UNREADABLE = "worktree-parent-unreadable"
    WORKTREE_PARENT_UNSUPPORTED = "worktree-parent-unsupported"


class SourceAcquisitionError(RuntimeError):
    """An admitted source request could not yield its exact candidate bytes."""

    def __init__(
        self,
        diagnostic_code: _SourceDiagnostic,
        path: str | None = None,
    ) -> None:
        """Initialize a bounded source diagnostic."""
        self.diagnostic_code = diagnostic_code.value
        self.path = path
        message = "static-reference source acquisition failed"
        if path is not None:
            message = f"{path}: {message}"
        super().__init__(message)


class InvalidRepositoryRootError(ValueError):
    """The repository-root parameter is not one exact Git worktree root."""


@dataclass(frozen=True, slots=True)
class StaticReferenceSelection:
    """One retained disjoint selector result."""

    family: StaticReferenceFamily
    graph_id: AuthorityGraphId
    input_mode: StaticReferenceInputMode


@dataclass(frozen=True, slots=True)
class StaticReferenceCandidate:
    """One exact selected source artifact."""

    path: str
    selection: StaticReferenceSelection
    content: bytes
    source_object: str | None

    def __post_init__(self) -> None:
        """Validate the source record boundary."""
        normalized_repository_path(self.path, field="candidate.path")
        if type(self.content) is not bytes:
            message = "candidate.content must be exact bytes"
            raise TypeError(message)
        if self.source_object is not None and (
            type(self.source_object) is not str or not self.source_object
        ):
            message = "candidate.source_object must be an exact identity"
            raise TypeError(message)

    @property
    def content_sha256(self) -> str:
        """Return the exact candidate byte digest."""
        return f"sha256:{hashlib.sha256(self.content).hexdigest()}"

    @property
    def has_utf8_bom(self) -> bool:
        """Return whether the exact bytes begin with a UTF-8 BOM."""
        return self.content.startswith(_UTF8_BOM)


@dataclass(frozen=True, slots=True)
class StaticReferenceInventory:
    """One completely acquired and deterministically ordered source."""

    source_kind: StaticReferenceSourceKind
    target: str | None
    candidates: tuple[StaticReferenceCandidate, ...]

    def __post_init__(self) -> None:
        """Reject a partial or noncanonical inventory."""
        paths = tuple(candidate.path for candidate in self.candidates)
        if paths != tuple(sorted(set(paths), key=utf8_sort_key)):
            message = "source candidates must be sorted and unique"
            raise ValueError(message)


_SELECTIONS = {
    "package.json": StaticReferenceSelection(
        "npm-manifest",
        "npm-manifest-v1",
        "strict-utf8-file",
    ),
    "pnpm-lock.yaml": StaticReferenceSelection(
        "pnpm-lock",
        "pnpm-lock-v1",
        "strict-utf8-file",
    ),
    "pnpm-workspace.yaml": StaticReferenceSelection(
        "pnpm-workspace",
        "pnpm-workspace-v1",
        "strict-utf8-file",
    ),
    "packages.lock.json": StaticReferenceSelection(
        "nuget-lock",
        "nuget-lock-v1",
        "strict-utf8-byte-stream",
    ),
    "packages.config": StaticReferenceSelection(
        "nuget-packages-config",
        "nuget-lock-v1",
        "xml-byte-stream",
    ),
}
STATIC_REFERENCE_BASENAMES = frozenset(_SELECTIONS)
_RAW_STATIC_REFERENCE_BASENAMES = frozenset(
    basename.encode("ascii") for basename in STATIC_REFERENCE_BASENAMES
)
_RAW_WORKFLOW_EXCLUDED_BASENAMES = frozenset(
    {b"pnpm-lock.yaml", b"pnpm-workspace.yaml"}
)


def select_static_reference_path(
    path: str,
) -> StaticReferenceSelection | None:
    """Select exactly one retained static-reference row."""
    normalized_repository_path(path, field="static-reference path")
    candidate = PurePosixPath(path)
    if candidate.parts[:2] == (".github", "workflows") and candidate.name in {
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
    }:
        return None
    return _SELECTIONS.get(candidate.name)


def _source_failure(
    diagnostic_code: _SourceDiagnostic,
    *,
    path: str | None = None,
) -> SourceAcquisitionError:
    return SourceAcquisitionError(diagnostic_code, path)


def _git(repository_root: Path, *arguments: str) -> bytes:
    executable = shutil.which("git")
    if executable is None:
        raise _source_failure(_SourceDiagnostic.GIT_EXECUTABLE_UNAVAILABLE)
    environment = os.environ.copy()
    for name in (
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_PARAMETERS",
        "GIT_DIR",
        "GIT_INDEX_FILE",
        "GIT_NAMESPACE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_WORK_TREE",
    ):
        environment.pop(name, None)
    environment["GIT_CONFIG_GLOBAL"] = os.devnull
    environment["GIT_CONFIG_NOSYSTEM"] = "1"
    environment["GIT_NO_LAZY_FETCH"] = "1"
    try:
        return subprocess.run(  # noqa: S603
            (executable, "--no-replace-objects", *arguments),
            cwd=repository_root,
            check=True,
            capture_output=True,
            env=environment,
        ).stdout
    except (OSError, subprocess.CalledProcessError) as error:
        raise _source_failure(_SourceDiagnostic.GIT_COMMAND_FAILED) from error


def resolve_static_reference_repository_root(repository_root: Path) -> Path:
    """Resolve and require the exact Git worktree root."""
    exact_root_message = (
        "repository_root must identify the exact Git worktree root"
    )
    try:
        root = repository_root.resolve(strict=True)
    except OSError as error:
        message = "repository_root must identify an existing directory"
        raise InvalidRepositoryRootError(message) from error
    if not root.is_dir():
        message = "repository_root must identify an existing directory"
        raise InvalidRepositoryRootError(message)
    try:
        (root / ".git").lstat()
    except FileNotFoundError as error:
        raise InvalidRepositoryRootError(exact_root_message) from error
    except OSError:
        pass
    raw_top_level = _git(root, "rev-parse", "--show-toplevel")
    if not raw_top_level.endswith(b"\n"):
        raise InvalidRepositoryRootError(exact_root_message)
    try:
        top_level = Path(raw_top_level[:-1].decode("utf-8", "strict")).resolve(
            strict=True
        )
    except (
        OSError,
        UnicodeDecodeError,
        ValueError,
    ) as error:
        raise InvalidRepositoryRootError(exact_root_message) from error
    if top_level != root:
        raise InvalidRepositoryRootError(exact_root_message)
    return root


def _decode_path(raw_path: bytes, *, context: str) -> str:
    try:
        decoded = raw_path.decode("utf-8", "strict")
        return normalized_repository_path(decoded, field=context)
    except (UnicodeDecodeError, ValueError) as error:
        raise _source_failure(_SourceDiagnostic.GIT_PATH_REJECTED) from error


def _raw_path_may_select(raw_path: bytes) -> bool:
    parts = raw_path.split(b"/")
    if not parts or parts[-1] not in _RAW_STATIC_REFERENCE_BASENAMES:
        return False
    return not (
        parts[:2] == [b".github", b"workflows"]
        and parts[-1] in _RAW_WORKFLOW_EXCLUDED_BASENAMES
    )


def _raw_git_entry_path(raw_entry: bytes) -> bytes:
    _, separator, raw_path = raw_entry.partition(b"\t")
    if not separator:
        raise _source_failure(_SourceDiagnostic.MALFORMED_GIT_ENTRY)
    return raw_path


def _parse_git_entry(
    raw_entry: bytes,
    *,
    context: str,
) -> tuple[str, str, str, str]:
    metadata, _, raw_path = raw_entry.partition(b"\t")
    try:
        fields = metadata.decode("ascii", "strict").split()
    except UnicodeDecodeError as error:
        raise _source_failure(_SourceDiagnostic.MALFORMED_GIT_ENTRY) from error
    if len(fields) != _GIT_METADATA_FIELD_COUNT:
        raise _source_failure(_SourceDiagnostic.MALFORMED_GIT_ENTRY)
    mode, object_type, object_id = fields
    path = _decode_path(raw_path, context=context)
    return path, mode, object_type, object_id


def _read_blob(repository_root: Path, object_id: str, *, path: str) -> bytes:
    try:
        return _git(repository_root, "cat-file", "blob", object_id)
    except SourceAcquisitionError as error:
        raise _source_failure(
            _SourceDiagnostic.GIT_BLOB_UNREADABLE,
            path=path,
        ) from error


def _git_target_inventory(
    repository_root: Path,
    target: str,
) -> StaticReferenceInventory:
    try:
        resolved = (
            _git(
                repository_root,
                "rev-parse",
                "--verify",
                "--end-of-options",
                f"{target}^{{commit}}",
            )
            .decode("ascii", "strict")
            .strip()
        )
    except (SourceAcquisitionError, UnicodeDecodeError) as error:
        raise _source_failure(
            _SourceDiagnostic.GIT_TARGET_UNAVAILABLE
        ) from error
    if resolved != target:
        raise _source_failure(_SourceDiagnostic.GIT_TARGET_NOT_EXACT)

    entries: list[tuple[str, str, str, str, StaticReferenceSelection]] = []
    selected_paths: set[str] = set()
    for raw_entry in _git(
        repository_root,
        "ls-tree",
        "-r",
        "-z",
        "--full-tree",
        target,
    ).split(b"\0"):
        if not raw_entry:
            continue
        if not _raw_path_may_select(_raw_git_entry_path(raw_entry)):
            continue
        path, mode, object_type, object_id = _parse_git_entry(
            raw_entry,
            context="Git tree path",
        )
        selection = select_static_reference_path(path)
        if selection is not None:
            if path in selected_paths:
                raise _source_failure(
                    _SourceDiagnostic.DUPLICATE_GIT_PATH,
                    path=path,
                )
            selected_paths.add(path)
            entries.append((path, mode, object_type, object_id, selection))

    candidates: list[StaticReferenceCandidate] = []
    for path, mode, object_type, object_id, selection in sorted(
        entries,
        key=lambda entry: utf8_sort_key(entry[0]),
    ):
        if mode not in _REGULAR_GIT_MODES or object_type != "blob":
            raise _source_failure(
                _SourceDiagnostic.UNSUPPORTED_GIT_MODE,
                path=path,
            )
        candidates.append(
            StaticReferenceCandidate(
                path=path,
                selection=selection,
                content=_read_blob(repository_root, object_id, path=path),
                source_object=object_id,
            )
        )
    return StaticReferenceInventory("git-target", target, tuple(candidates))


def _index_inventory(repository_root: Path) -> StaticReferenceInventory:
    grouped: dict[str, list[tuple[str, str, int]]] = {}
    for raw_entry in _git(
        repository_root,
        "ls-files",
        "--stage",
        "-z",
    ).split(b"\0"):
        if not raw_entry:
            continue
        if not _raw_path_may_select(_raw_git_entry_path(raw_entry)):
            continue
        path, mode, object_id, raw_stage = _parse_git_entry(
            raw_entry,
            context="Git index path",
        )
        try:
            stage = int(raw_stage)
        except ValueError as error:
            raise _source_failure(
                _SourceDiagnostic.MALFORMED_GIT_INDEX_STAGE,
                path=path,
            ) from error
        grouped.setdefault(path, []).append((mode, object_id, stage))

    candidates: list[StaticReferenceCandidate] = []
    for path in sorted(grouped, key=utf8_sort_key):
        selection = select_static_reference_path(path)
        if selection is None:
            continue
        entries = grouped[path]
        if len(entries) != 1 or entries[0][2] != 0:
            raise _source_failure(
                _SourceDiagnostic.UNMERGED_INDEX_ENTRY,
                path=path,
            )
        mode, object_id, _ = entries[0]
        if mode not in _REGULAR_GIT_MODES:
            raise _source_failure(
                _SourceDiagnostic.UNSUPPORTED_GIT_MODE,
                path=path,
            )
        candidates.append(
            StaticReferenceCandidate(
                path=path,
                selection=selection,
                content=_read_blob(repository_root, object_id, path=path),
                source_object=object_id,
            )
        )
    return StaticReferenceInventory("index", None, tuple(candidates))


def _decode_paths(
    output: bytes,
    *,
    skip_directory_markers: bool = False,
) -> tuple[str, ...]:
    paths: set[str] = set()
    for raw_path in output.split(b"\0"):
        if not raw_path:
            continue
        if not _raw_path_may_select(raw_path):
            continue
        try:
            decoded = raw_path.decode("utf-8", "strict")
        except UnicodeDecodeError as error:
            raise _source_failure(
                _SourceDiagnostic.GIT_PATH_REJECTED
            ) from error
        if skip_directory_markers and decoded.endswith("/"):
            continue
        try:
            path = normalized_repository_path(decoded, field="Git path")
        except ValueError as error:
            raise _source_failure(
                _SourceDiagnostic.GIT_PATH_REJECTED
            ) from error
        paths.add(path)
    return tuple(sorted(paths, key=utf8_sort_key))


def _validate_worktree_parents(
    repository_root: Path,
    source: Path,
    *,
    path: str,
) -> bool:
    current = repository_root
    for part in source.relative_to(repository_root).parent.parts:
        current /= part
        try:
            mode = current.lstat().st_mode
        except FileNotFoundError:
            return False
        except OSError as error:
            raise _source_failure(
                _SourceDiagnostic.WORKTREE_PARENT_UNREADABLE,
                path=path,
            ) from error
        if stat.S_ISLNK(mode) or not stat.S_ISDIR(mode):
            raise _source_failure(
                _SourceDiagnostic.WORKTREE_PARENT_UNSUPPORTED,
                path=path,
            )
    return True


def _native_worktree_path[PathT: PurePath](
    repository_root: PathT,
    path: str,
) -> PathT:
    try:
        return native_repository_path(
            repository_root,
            path,
            field="worktree path",
        )
    except ValueError as error:
        raise _source_failure(
            _SourceDiagnostic.GIT_PATH_REJECTED,
            path=path,
        ) from error


def _open_worktree_candidate(source: Path, *, path: str) -> int | None:
    try:
        initial_mode = source.lstat().st_mode
    except FileNotFoundError:
        return None
    except OSError as error:
        raise _source_failure(
            _SourceDiagnostic.WORKTREE_FILE_UNREADABLE,
            path=path,
        ) from error
    if stat.S_ISLNK(initial_mode) or not stat.S_ISREG(initial_mode):
        raise _source_failure(
            _SourceDiagnostic.WORKTREE_FILE_UNSUPPORTED,
            path=path,
        )

    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0)
    flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        return os.open(source, flags)
    except FileNotFoundError:
        return None
    except OSError as error:
        raise _source_failure(
            _SourceDiagnostic.WORKTREE_FILE_UNREADABLE,
            path=path,
        ) from error


def _read_worktree_candidate(
    repository_root: Path,
    path: str,
) -> bytes | None:
    source = _native_worktree_path(repository_root, path)
    if not _validate_worktree_parents(
        repository_root,
        source,
        path=path,
    ):
        return None
    descriptor = _open_worktree_candidate(source, path=path)
    if descriptor is None:
        return None
    try:
        if not stat.S_ISREG(os.fstat(descriptor).st_mode):
            raise _source_failure(
                _SourceDiagnostic.WORKTREE_FILE_UNSUPPORTED,
                path=path,
            )
        with os.fdopen(descriptor, "rb", closefd=False) as stream:
            return stream.read()
    except OSError as error:
        raise _source_failure(
            _SourceDiagnostic.WORKTREE_FILE_UNREADABLE,
            path=path,
        ) from error
    finally:
        os.close(descriptor)


def _worktree_inventory(repository_root: Path) -> StaticReferenceInventory:
    tracked = _decode_paths(_git(repository_root, "ls-files", "--cached", "-z"))
    untracked = _decode_paths(
        _git(
            repository_root,
            "ls-files",
            "--others",
            "--exclude-standard",
            "-z",
        ),
        skip_directory_markers=True,
    )
    candidates: list[StaticReferenceCandidate] = []
    for path in sorted(set(tracked) | set(untracked), key=utf8_sort_key):
        selection = select_static_reference_path(path)
        if selection is None:
            continue
        content = _read_worktree_candidate(repository_root, path)
        if content is None:
            continue
        candidates.append(
            StaticReferenceCandidate(
                path=path,
                selection=selection,
                content=content,
                source_object=None,
            )
        )
    return StaticReferenceInventory("worktree", None, tuple(candidates))


def acquire_static_reference_inventory(
    repository_root: Path,
    *,
    source_kind: StaticReferenceSourceKind,
    target: str | None = None,
) -> StaticReferenceInventory:
    """Acquire one complete exact source before any graph can execute."""
    if source_kind not in {"git-target", "index", "worktree"}:
        message = "unsupported static-reference source kind"
        raise ValueError(message)
    if source_kind == "git-target":
        if type(target) is not str or _SHA_PATTERN.fullmatch(target) is None:
            message = "git-target requires a full lowercase commit SHA"
            raise ValueError(message)
    elif target is not None:
        message = f"{source_kind} source does not accept target"
        raise ValueError(message)

    root = resolve_static_reference_repository_root(repository_root)

    if source_kind == "git-target":
        if target is None:
            message = "git-target requires a full lowercase commit SHA"
            raise ValueError(message)
        return _git_target_inventory(root, target)
    if source_kind == "index":
        return _index_inventory(root)
    return _worktree_inventory(root)


__all__ = [
    "STATIC_REFERENCE_BASENAMES",
    "AuthorityGraphId",
    "InvalidRepositoryRootError",
    "SourceAcquisitionError",
    "StaticReferenceCandidate",
    "StaticReferenceInputMode",
    "StaticReferenceInventory",
    "StaticReferenceSelection",
    "acquire_static_reference_inventory",
    "resolve_static_reference_repository_root",
    "select_static_reference_path",
]
