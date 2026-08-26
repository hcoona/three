"""Merge-time history protection for the Platform-Orphan singleton."""

# ruff: noqa: C901, E501, EM101, EM102, PLR2004, S607, TC003, TRY003

from __future__ import annotations

import hashlib
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.governance.platform_orphan import _git_blob_oid
from three_workflow_delivery_v3.records import (
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_RESULT_PATH,
    admit_platform_orphan_active_authority,
    admit_platform_orphan_consumed_audit,
    admit_platform_orphan_reconciliation_result,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

_OBJECT_ID = re.compile(r"[0-9a-f]{40}")
_REGULAR_FILE_MODE = "100644"


class PlatformOrphanHistoryError(ValueError):
    """Raised when a fixed-path history transition is not admissible."""


@dataclass(frozen=True, slots=True)
class _TreeBlob:
    mode: str
    oid: str
    content: bytes


def _git(
    repository: Path,
    *arguments: str,
    text: bool = False,
) -> subprocess.CompletedProcess[bytes] | subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603
        ("git", *arguments),
        cwd=repository,
        check=True,
        capture_output=True,
        text=text,
    )


def _tree_blob(
    repository: Path, commit_oid: str, path: str
) -> _TreeBlob | None:
    result = cast(
        "subprocess.CompletedProcess[bytes]",
        _git(
            repository,
            "ls-tree",
            "-z",
            "--full-tree",
            commit_oid,
            "--",
            path,
        ),
    )
    if not result.stdout:
        return None
    records = result.stdout.removesuffix(b"\0").split(b"\0")
    if len(records) != 1:
        raise PlatformOrphanHistoryError(f"{path} is not one tree entry")
    metadata, separator, encoded_path = records[0].partition(b"\t")
    if not separator or encoded_path.decode("utf-8", "strict") != path:
        raise PlatformOrphanHistoryError(f"{path} has an invalid tree entry")
    fields = metadata.decode("ascii", "strict").split()
    if len(fields) != 3 or fields[1] != "blob":
        raise PlatformOrphanHistoryError(f"{path} is not a Git blob")
    mode, _, oid = fields
    if mode != _REGULAR_FILE_MODE or _OBJECT_ID.fullmatch(oid) is None:
        raise PlatformOrphanHistoryError(f"{path} has invalid Git metadata")
    content = cast(
        "subprocess.CompletedProcess[bytes]",
        _git(repository, "cat-file", "blob", oid),
    ).stdout
    if _git_blob_oid(content) != oid:
        raise PlatformOrphanHistoryError(f"{path} Git blob OID does not match")
    return _TreeBlob(mode=mode, oid=oid, content=content)


def _required_blob(value: _TreeBlob | None, *, path: str) -> _TreeBlob:
    if value is None:
        raise PlatformOrphanHistoryError(f"{path} is absent")
    return value


def _fixed_statuses(
    repository: Path,
    base_oid: str,
    head_oid: str,
) -> dict[str, list[str]]:
    result = cast(
        "subprocess.CompletedProcess[bytes]",
        _git(
            repository,
            "diff",
            "--name-status",
            "--find-renames",
            "-z",
            "--end-of-options",
            base_oid,
            head_oid,
            "--",
        ),
    )
    fields = result.stdout.removesuffix(b"\0").split(b"\0")
    if fields == [b""]:
        return {}
    statuses: dict[str, list[str]] = {}
    index = 0
    while index < len(fields):
        status = fields[index].decode("ascii", "strict")
        index += 1
        path_count = 2 if status.startswith(("R", "C")) else 1
        if not status or index + path_count > len(fields):
            raise PlatformOrphanHistoryError(
                "Git returned malformed name-status history",
            )
        for encoded_path in fields[index : index + path_count]:
            path = encoded_path.decode("utf-8", "strict")
            if path in {
                PLATFORM_ORPHAN_AUTHORITY_PATH,
                PLATFORM_ORPHAN_RESULT_PATH,
            }:
                statuses.setdefault(path, []).append(status)
        index += path_count
    return statuses


def _sha256(content: bytes) -> str:
    return f"sha256:{hashlib.sha256(content).hexdigest()}"


def _object(value: object, *, field: str) -> dict[str, JsonValue]:
    if type(value) is not dict:
        raise PlatformOrphanHistoryError(f"{field} is not an object")
    return cast("dict[str, JsonValue]", value)


def _validate_atomic_consumption(
    *,
    base_oid: str,
    base_authority: _TreeBlob,
    head_authority: _TreeBlob | None,
    head_result: _TreeBlob | None,
) -> None:
    result_blob = _required_blob(
        head_result,
        path=PLATFORM_ORPHAN_RESULT_PATH,
    )
    audit_blob = _required_blob(
        head_authority,
        path=PLATFORM_ORPHAN_AUTHORITY_PATH,
    )
    result = admit_platform_orphan_reconciliation_result(result_blob.content)
    result_document = result.to_document()
    authority = _object(result_document["authority"], field="authority")
    if authority["parent_main_commit"] != base_oid:
        raise PlatformOrphanHistoryError(
            "candidate parent_main_commit does not equal the base commit",
        )
    if authority["initial_blob_oid"] != base_authority.oid:
        raise PlatformOrphanHistoryError(
            "candidate initial_blob_oid does not equal the base authority",
        )
    if authority["initial_content_sha256"] != _sha256(
        base_authority.content,
    ):
        raise PlatformOrphanHistoryError(
            "candidate initial_content_sha256 does not equal the base authority",
        )
    admit_platform_orphan_consumed_audit(audit_blob.content, result=result)


def validate_platform_orphan_history(
    repository: Path,
    base_oid: str,
    head_oid: str,
) -> None:
    """Validate one explicit base/head fixed-path tree transition."""
    if _OBJECT_ID.fullmatch(base_oid) is None:
        raise PlatformOrphanHistoryError("base commit OID is invalid")
    if _OBJECT_ID.fullmatch(head_oid) is None:
        raise PlatformOrphanHistoryError("head commit OID is invalid")

    base_authority = _tree_blob(
        repository,
        base_oid,
        PLATFORM_ORPHAN_AUTHORITY_PATH,
    )
    base_result = _tree_blob(repository, base_oid, PLATFORM_ORPHAN_RESULT_PATH)
    head_authority = _tree_blob(
        repository,
        head_oid,
        PLATFORM_ORPHAN_AUTHORITY_PATH,
    )
    head_result = _tree_blob(repository, head_oid, PLATFORM_ORPHAN_RESULT_PATH)
    statuses = _fixed_statuses(
        repository,
        base_oid,
        head_oid,
    )

    if base_authority is None:
        raise PlatformOrphanHistoryError("base active authority is absent")

    try:
        admit_platform_orphan_active_authority(base_authority.content)
    except (TypeError, ValueError):
        if base_result is None:
            raise PlatformOrphanHistoryError(
                "base is neither active nor consumed",
            ) from None
        try:
            result = admit_platform_orphan_reconciliation_result(
                base_result.content,
            )
            admit_platform_orphan_consumed_audit(
                base_authority.content,
                result=result,
            )
        except (TypeError, ValueError) as error:
            raise PlatformOrphanHistoryError(
                "base consumed authority/result pair is invalid",
            ) from error
        if head_authority != base_authority or head_result != base_result:
            raise PlatformOrphanHistoryError(
                "consumed authority and result must remain byte-for-byte fixed",
            ) from None
        return

    if base_result is not None:
        raise PlatformOrphanHistoryError(
            "active base authority must not have a result",
        )
    if head_authority == base_authority and head_result is None:
        return
    if statuses != {
        PLATFORM_ORPHAN_AUTHORITY_PATH: ["M"],
        PLATFORM_ORPHAN_RESULT_PATH: ["A"],
    }:
        raise PlatformOrphanHistoryError(
            "consumption must modify authority and add result without rename",
        )
    try:
        _validate_atomic_consumption(
            base_oid=base_oid,
            base_authority=base_authority,
            head_authority=head_authority,
            head_result=head_result,
        )
    except PlatformOrphanHistoryError:
        raise
    except (TypeError, ValueError) as error:
        raise PlatformOrphanHistoryError(
            "head does not contain an admissible atomic consumption",
        ) from error
