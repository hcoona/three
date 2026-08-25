"""Candidate coordinator for the approved Platform-Orphan exception."""

# ruff: noqa: EM101, PLR0913, TRY003

from __future__ import annotations

import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast
from uuid import UUID

from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.governance.platform_orphan import (
    PlatformOrphanObservationData,
    QueryOnlyPlatformOrphanTransport,
    observe_platform_orphan_32809578776,
)
from three_workflow_delivery_v3.records.platform_orphan import (
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_REF,
    PLATFORM_ORPHAN_REPOSITORY,
    PLATFORM_ORPHAN_RESULT_SCHEMA,
    PlatformOrphanReconciliationResult,
    admit_platform_orphan_reconciliation_result,
)

if TYPE_CHECKING:
    from datetime import datetime

    from three_workflow_delivery_v3.canonical import JsonValue


type CanonicalCandidateOutput = Callable[[bytes], None]
type GitOutput = Callable[[tuple[str, ...], Path], str]

_ENTRY_POINT_ROUTE = (
    "three-workflow-delivery-v3 governance "
    "reconcile-platform-orphan-32809578776"
)
_PROJECT_CLI_PATH = Path(
    "src/public/lib/three-workflow-delivery-v3/src/"
    "three_workflow_delivery_v3/cli.py"
)


@dataclass(frozen=True, slots=True)
class LocalControlProvenance:
    """Validated case-specific local control facts."""

    commit: str
    project_root: Path


def inspect_local_control_provenance(
    *,
    cli_module_path: Path,
    entry_point_route: str,
    git_output: GitOutput,
) -> LocalControlProvenance:
    """Fail closed unless the loaded CLI is clean tracked worktree control."""
    if entry_point_route != _ENTRY_POINT_ROUTE:
        raise ValueError("local control entry point is unexpected")
    resolved_module = cli_module_path.resolve()
    root_text = git_output(
        ("git", "rev-parse", "--show-toplevel"),
        resolved_module.parent,
    )
    project_root = Path(root_text.strip()).resolve()
    expected_module = (project_root / _PROJECT_CLI_PATH).resolve()
    if resolved_module != expected_module:
        raise ValueError("loaded CLI module source is unexpected")
    tracked = git_output(
        ("git", "ls-files", "--error-unmatch", _PROJECT_CLI_PATH.as_posix()),
        project_root,
    )
    if tracked.strip() != _PROJECT_CLI_PATH.as_posix():
        raise ValueError("loaded CLI module is not tracked")
    commit = git_output(("git", "rev-parse", "HEAD"), project_root).strip()
    if re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        raise ValueError("local control HEAD is malformed")
    status = git_output(
        ("git", "status", "--porcelain=v1", "--untracked-files=all"),
        project_root,
    )
    if status != "":
        raise ValueError("local control worktree is dirty")
    return LocalControlProvenance(commit=commit, project_root=project_root)


def _candidate_document(
    observation: PlatformOrphanObservationData,
    *,
    invocation_id: str,
) -> dict[str, JsonValue]:
    classification = observation.destination_observations[0].state[
        "classification"
    ]
    if type(classification) is not str:
        raise ValueError("observed package classification is malformed")
    diagnostics = ["platform-orphan-admitted"]
    if classification != "exact":
        diagnostics.append(f"package-{classification}")
    document: dict[str, JsonValue] = {
        "schema": PLATFORM_ORPHAN_RESULT_SCHEMA,
        "version": 1,
        "producer": {
            "id": "three-workflow-delivery-v3/platform-orphan-reconcile",
            "entry_point": (_ENTRY_POINT_ROUTE),
            "repository": PLATFORM_ORPHAN_REPOSITORY,
            "ref": PLATFORM_ORPHAN_REF,
            "control_commit": observation.control_commit,
        },
        "invocation": {
            "id": invocation_id,
            "started_at": observation.started_at,
            "completed_at": observation.completed_at,
        },
        "authority": {
            "repository": PLATFORM_ORPHAN_REPOSITORY,
            "ref": PLATFORM_ORPHAN_REF,
            "path": PLATFORM_ORPHAN_AUTHORITY_PATH,
            "initial_commit": observation.initial_source.commit,
            "initial_blob_oid": observation.initial_source.blob_oid,
            "initial_content_sha256": (
                observation.initial_source.content_sha256
            ),
            "final_commit": observation.final_source.commit,
            "final_blob_oid": observation.final_source.blob_oid,
            "final_content_sha256": observation.final_source.content_sha256,
            "parent_main_commit": observation.initial_source.commit,
        },
        "acceptance": observation.acceptance.to_document(),
        "requests": cast(
            "list[JsonValue]",
            observation.request_documents(),
        ),
        "platform_observations": cast(
            "list[JsonValue]",
            observation.platform_documents(),
        ),
        "destination_observations": cast(
            "list[JsonValue]",
            observation.destination_documents(),
        ),
        "result": {
            "terminalization_blocker_exclusion": "admitted:run:32809578776",
            "reconciliation_authority": "not-granted-by-exception",
            "acceptance_result": "unsuccessful",
            "platform_cleanup": "incomplete-with-admitted-orphan",
            "run_terminal": False,
            "release_lineage": "none",
            "package_classification": classification,
            "package_mutation": "prohibited",
            "live_activation": "prohibited",
            "diagnostics": cast("list[JsonValue]", sorted(diagnostics)),
        },
    }
    document["result_digest"] = canonical_sha256(document)
    return document


def reconcile_platform_orphan_32809578776(
    *,
    transport: QueryOnlyPlatformOrphanTransport,
    clock: Callable[[], datetime],
    invocation_id_factory: Callable[[], UUID],
    output: CanonicalCandidateOutput,
    token: str,
    review_artifact: Path,
    probe_artifact: Path,
    governance_artifact: Path,
    local_control_commit: str,
) -> PlatformOrphanReconciliationResult:
    """Observe, strictly admit, and emit one non-authoritative candidate."""
    if re.fullmatch(r"[0-9a-f]{40}", local_control_commit) is None:
        raise ValueError("local control commit is malformed")

    def validate_initial_source(source: object) -> None:
        commit = getattr(source, "commit", None)
        if commit != local_control_commit:
            raise ValueError(
                "initial remote source does not match local control commit"
            )

    observation = observe_platform_orphan_32809578776(
        transport=transport,
        clock=clock,
        token=token,
        review_artifact=review_artifact,
        probe_artifact=probe_artifact,
        governance_artifact=governance_artifact,
        initial_source_validator=validate_initial_source,
    )
    invocation_id = invocation_id_factory()
    if type(invocation_id) is not UUID:
        raise ValueError("invocation UUID source returned a malformed value")
    candidate = _candidate_document(
        observation,
        invocation_id=str(invocation_id),
    )
    content = canonicalize(candidate)
    admitted = admit_platform_orphan_reconciliation_result(content)
    output(content)
    return admitted


__all__ = [
    "CanonicalCandidateOutput",
    "GitOutput",
    "LocalControlProvenance",
    "inspect_local_control_provenance",
    "reconcile_platform_orphan_32809578776",
]
