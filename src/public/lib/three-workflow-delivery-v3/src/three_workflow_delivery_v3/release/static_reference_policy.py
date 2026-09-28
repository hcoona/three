"""Canonical bounded static-reference policy orchestration."""

from __future__ import annotations

import logging
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
)
from three_workflow_delivery_v3.release.static_reference_authority import (
    AuthorityExecutionError,
    AuthorityGraphOutcome,
    run_authority_graph,
)
from three_workflow_delivery_v3.release.static_reference_model import (
    PRODUCER_MANIFEST,
    PRODUCER_PACKAGE,
    PRODUCER_ROOT,
    STATIC_REFERENCE_POLICY_ID,
    STATIC_REFERENCE_POLICY_SCHEMA,
    BoundedStaticReferenceResult,
    StaticReferenceErrorKind,
    StaticReferenceFinding,
    StaticReferenceSourceKind,
    utf8_sort_key,
)
from three_workflow_delivery_v3.release.static_reference_projection import (
    StaticReferenceProjectionError,
    project_static_reference_facts,
)
from three_workflow_delivery_v3.release.static_reference_session import (
    StaticReferenceCleanupError,
    StaticReferenceSession,
)
from three_workflow_delivery_v3.release.static_reference_source import (
    SourceAcquisitionError,
    StaticReferenceCandidate,
    StaticReferenceInventory,
    acquire_static_reference_inventory,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.release.static_reference_session import (
        MaterializedAuthorityInvocation,
    )

type AuthorityRunner = Callable[
    [
        Path,
        StaticReferenceCandidate,
        MaterializedAuthorityInvocation,
        StaticReferenceSession,
    ],
    AuthorityGraphOutcome,
]
type SessionFactory = Callable[[], StaticReferenceSession]

_LOGGER = logging.getLogger(__name__)


def _static_reference_graph_contracts() -> dict[str, JsonValue]:
    return {
        "npm-manifest-v1": {
            "decoding": {
                "byte-preflight": "fatal-utf8",
                "snapshot-bytes": "exact",
                "accepted-leading-utf8-bom-counts": [0, 1],
                "next-leading-utf8-bom-outcome": "authority-rejected",
            },
            "calls": [
                {
                    "api": "PackageJson.load",
                    "arguments": ["snapshotDirectory"],
                },
                {
                    "api": "npa.resolve",
                    "arguments": [
                        "packageName",
                        "*",
                        "snapshotDirectory",
                    ],
                    "purpose": "top-level-name-validation",
                },
                {
                    "api": "npa.resolve",
                    "arguments": [
                        "name",
                        "specifier",
                        "snapshotDirectory",
                    ],
                },
            ],
            "options": {
                "dependency-sections": [
                    "dependencies",
                    "devDependencies",
                    "optionalDependencies",
                    "peerDependencies",
                ],
                "npa-result-types": [
                    "alias",
                    "directory",
                    "file",
                    "git",
                    "range",
                    "remote",
                    "tag",
                    "version",
                ],
            },
        },
        "pnpm-lock-v1": {
            "decoding": {
                "byte-preflight": "fatal-utf8",
                "snapshot-bytes": "exact",
                "accepted-leading-utf8-bom-counts": [0, 1, 2],
                "next-leading-utf8-bom-outcome": "authority-rejected",
                "comparison-view-newlines": "crlf-to-lf",
                "extract-main-document": "must-equal-comparison-view",
            },
            "calls": [
                {
                    "api": "extractMainDocument",
                    "arguments": ["comparisonView"],
                },
                {
                    "api": "readWantedLockfileWithMergeInfo",
                    "arguments": ["lockfileDirectory"],
                    "options": {
                        "autofixMergeConflicts": True,
                        "ignoreIncompatible": False,
                        "mergeGitBranchLockfiles": False,
                        "useGitBranchLockfile": False,
                        "wantedVersions": ["9.0"],
                    },
                },
                {
                    "api": "nameVerFromPkgSnapshot",
                    "arguments": ["dependencyPath", "snapshot"],
                },
                {
                    "api": "pkgSnapshotToResolution",
                    "arguments": [
                        "dependencyPath",
                        "snapshot",
                        "registryContext",
                    ],
                    "registry-context": {
                        "registriesByScope": {
                            "default": "https://registry.npmjs.org/"
                        }
                    },
                },
                {
                    "api": "WorkspaceSpec.parse",
                    "arguments": ["rawSpecifier"],
                },
                {
                    "api": "workspacePrefToNpm",
                    "arguments": ["rawSpecifier"],
                },
                {
                    "api": "parseBareSpecifier",
                    "arguments": [
                        "normalizedSpecifier",
                        "dependencyKey",
                        "latest",
                        "https://registry.npmjs.org/",
                    ],
                },
                {
                    "api": "refToRelative",
                    "arguments": [
                        "resolvedReference",
                        "dependencyKey",
                    ],
                },
            ],
        },
        "pnpm-workspace-v1": {
            "decoding": {
                "byte-preflight": "fatal-utf8",
                "snapshot-bytes": "exact",
                "accepted-leading-utf8-bom-counts": [0, 1, 2],
                "next-leading-utf8-bom-outcome": "empty-facts",
            },
            "calls": [
                {
                    "api": "readWorkspaceManifest",
                    "arguments": ["snapshotDirectory"],
                },
                {
                    "api": "WorkspaceSpec.parse",
                    "arguments": ["rawSpecifier"],
                },
                {
                    "api": "workspacePrefToNpm",
                    "arguments": ["rawSpecifier"],
                },
                {
                    "api": "parseBareSpecifier",
                    "arguments": [
                        "normalizedSpecifier",
                        "dependencyKey",
                        "latest",
                        "https://registry.npmjs.org/",
                    ],
                },
                {
                    "api": "npa.resolve",
                    "arguments": [
                        "dependencyKey",
                        "sourceSpec",
                        "snapshotDirectory",
                    ],
                },
            ],
            "options": {
                "null-catalog": "absent",
                "null-catalogs": "absent",
            },
        },
        "nuget-lock-v1": {
            "decoding": [
                {
                    "artifact": "packages.lock.json",
                    "byte-preflight": "fatal-utf8",
                    "reader-input": "original-byte-stream",
                },
                {
                    "artifact": "packages.config",
                    "byte-preflight": "none",
                    "reader-input": "original-xml-byte-stream",
                },
            ],
            "calls": [
                {
                    "api": "PackagesLockFileFormat.Read",
                    "arguments": [
                        "stream",
                        "NullLogger.Instance",
                        "repositoryLogicalPath",
                    ],
                    "admitted-model-versions": [1, 2, 3],
                },
                {
                    "api": "PackagesConfigReader",
                    "arguments": ["stream"],
                    "options": {"leaveStreamOpen": False},
                },
                {
                    "api": "PackagesConfigReader.GetPackages",
                    "options": {"allowDuplicatePackageIds": False},
                },
            ],
            "projections": [
                {
                    "api": "PackageDependencyType.ToString",
                    "source": "LockFileDependency.Type",
                    "field": "dependencyType",
                },
                {
                    "api": "VersionRange.ToNormalizedString",
                    "source": "LockFileDependency.RequestedVersion",
                    "field": "requestedRange",
                },
                {
                    "api": "NuGetVersion.ToNormalizedString",
                    "source": "LockFileDependency.ResolvedVersion",
                    "field": "resolvedVersion",
                },
                {
                    "api": "VersionRange.ToNormalizedString",
                    "source": "PackageDependency.VersionRange",
                    "field": "dependencies[].requestedRange",
                },
                {
                    "api": "NuGetVersion.ToNormalizedString",
                    "source": "PackageIdentity.Version",
                    "field": "version",
                },
            ],
            "ordering": [
                {
                    "collection": "targets",
                    "key": "PackagesLockFileTarget.Name",
                    "comparers": ["StringComparer.Ordinal"],
                },
                {
                    "collection": "target.dependencies",
                    "key": "LockFileDependency.Id",
                    "comparers": [
                        "StringComparer.OrdinalIgnoreCase",
                        "StringComparer.Ordinal",
                    ],
                },
                {
                    "collection": "dependency.dependencies",
                    "key": "PackageDependency.Id",
                    "comparers": [
                        "StringComparer.OrdinalIgnoreCase",
                        "StringComparer.Ordinal",
                    ],
                },
                {
                    "collection": "packages.config",
                    "key": "PackageReference.PackageIdentity",
                    "comparers": ["PackageIdentity.Comparer"],
                },
            ],
        },
    }


def _static_reference_fact_contracts() -> dict[str, JsonValue]:
    return {
        "definitions": {
            "npm-reference": {
                "fields": {
                    "aliasTarget": {"nullable-ref": "npm-reference"},
                    "fetchSpec": "nullable-exact-string",
                    "localPath": "snapshot-relative-path-or-null",
                    "name": "nonempty-string",
                    "rawSpec": "exact-string",
                    "saveSpec": "nullable-nonempty-string",
                    "type": {
                        "enum": [
                            "alias",
                            "directory",
                            "file",
                            "git",
                            "range",
                            "remote",
                            "tag",
                            "version",
                        ]
                    },
                }
            },
            "workspace-reference": {
                "one-of": [
                    {
                        "fields": {
                            "kind": {"const": "npm"},
                            "npm": {"ref": "npm-reference"},
                        }
                    },
                    {
                        "fields": {
                            "kind": {"const": "workspace"},
                            "workspace": {"ref": "workspace-value"},
                        }
                    },
                ]
            },
            "workspace-value": {
                "fields": {
                    "fetchSpec": "nonempty-string",
                    "name": "nonempty-string",
                    "selector": "exact-string",
                    "type": "nonempty-string",
                }
            },
            "pnpm-resolution": {
                "one-of": [
                    {
                        "fields": {
                            "kind": {"enum": ["directory", "file-tarball"]},
                            "localPath": "snapshot-relative-path",
                        }
                    },
                    {
                        "fields": {
                            "commit": "nonempty-string",
                            "kind": {"const": "git"},
                            "path": "nullable-nonempty-string",
                            "repo": "nonempty-string",
                        }
                    },
                    {
                        "fields": {
                            "kind": {"const": "hosted-git"},
                            "path": "nullable-nonempty-string",
                            "tarball": "nonempty-string",
                        }
                    },
                    {"fields": {"kind": {"const": "registry"}}},
                ]
            },
            "pnpm-registry-spec": {
                "fields": {
                    "fetchSpec": "nonempty-string",
                    "name": "nonempty-string",
                    "type": "nonempty-string",
                }
            },
            "pnpm-snapshot-dependency": {
                "fields": {
                    "dependencyKey": "exact-string",
                    "reference": "exact-string",
                    "section": {
                        "enum": [
                            "dependencies",
                            "optionalDependencies",
                        ]
                    },
                }
            },
            "nuget-dependency-edge": {
                "fields": {
                    "id": "nonempty-string",
                    "requestedRange": "nullable-nonempty-string",
                }
            },
        },
        "facts": {
            "npm-package-name": {
                "fields": {
                    "context": {"const": "name"},
                    "kind": {"const": "npm-package-name"},
                    "name": "nonempty-string",
                }
            },
            "npm-reference": {
                "fields": {
                    "dependencyKey": "nonempty-string",
                    "kind": {"const": "npm-reference"},
                    "reference": {"ref": "npm-reference"},
                    "section": {
                        "enum": [
                            "dependencies",
                            "devDependencies",
                            "optionalDependencies",
                            "peerDependencies",
                        ]
                    },
                    "sourceSpec": "exact-string",
                }
            },
            "pnpm-workspace-pattern": {
                "fields": {
                    "index": "nonnegative-integer",
                    "kind": {"const": "pnpm-workspace-pattern"},
                    "pattern": "nonempty-string",
                }
            },
            "pnpm-workspace-reference": {
                "fields": {
                    "catalogKind": {"enum": ["default", "named"]},
                    "catalogName": "nullable-exact-string",
                    "dependencyKey": "nonempty-string",
                    "kind": {"const": "pnpm-workspace-reference"},
                    "reference": {"ref": "workspace-reference"},
                    "sourceSpec": "exact-string",
                },
                "invariants": ["catalogKind=default iff catalogName=null"],
            },
            "pnpm-lock-snapshot": {
                "fields": {
                    "dependencies": {
                        "array-items": {"ref": "pnpm-snapshot-dependency"}
                    },
                    "dependencyPath": "nonempty-string",
                    "kind": {"const": "pnpm-lock-snapshot"},
                    "name": "nonempty-string",
                    "nonSemverVersion": "nullable-nonempty-string",
                    "registryName": "nullable-nonempty-string",
                    "resolution": {"ref": "pnpm-resolution"},
                    "version": "nullable-nonempty-string",
                },
                "invariants": ["version or nonSemverVersion is non-null"],
            },
            "pnpm-lock-importer-reference": {
                "fields": {
                    "dependencyKey": "nonempty-string",
                    "importerId": "nonempty-string",
                    "kind": {"const": "pnpm-lock-importer-reference"},
                    "rawSpecifier": "exact-string",
                    "registrySpec": {"nullable-ref": "pnpm-registry-spec"},
                    "resolvedReference": "nonempty-string",
                    "section": {
                        "enum": [
                            "dependencies",
                            "devDependencies",
                            "optionalDependencies",
                        ]
                    },
                    "snapshotKey": "nullable-nonempty-string",
                    "workspaceSelector": "nullable-exact-string",
                }
            },
            "nuget-lock-dependency": {
                "fields": {
                    "dependencies": {
                        "array-items": {"ref": "nuget-dependency-edge"}
                    },
                    "dependencyType": "nonempty-string",
                    "id": "nonempty-string",
                    "kind": {"const": "nuget-lock-dependency"},
                    "requestedRange": "nullable-nonempty-string",
                    "resolvedVersion": "nullable-nonempty-string",
                    "target": "nonempty-string",
                }
            },
            "nuget-packages-config-entry": {
                "fields": {
                    "id": "nonempty-string",
                    "kind": {"const": "nuget-packages-config-entry"},
                    "version": "nonempty-string",
                }
            },
        },
    }


def static_reference_authority_manifest() -> dict[str, JsonValue]:
    """Return the semantic graph contract, independent of tooling versions."""
    return {
        "schema": "workflow-delivery/v3/static-reference-authority-manifest",
        "execution": {
            "node-command": [
                "node",
                "eng/scripts/workflow_delivery_v3_static_reference_node.mjs",
            ],
            "nuget-command": [
                "dotnet",
                (
                    "artifacts/workflow-delivery-v3/static-reference/"
                    "nuget-authority/WorkflowDeliveryV3NuGetAuthority.dll"
                ),
            ],
            "timeout-seconds": 30,
        },
        "graph-contracts": _static_reference_graph_contracts(),
        "graphs": [
            {
                "id": "npm-manifest-v1",
                "artifact": "package.json",
                "input-mode": "strict-utf8-file",
                "snapshot-inputs": ["package.json"],
                "packages": [
                    "@npmcli/package-json",
                    "npm-package-arg",
                ],
                "apis": [
                    "PackageJson.load(snapshotDirectory)",
                    "npa.resolve(name,spec,snapshotDirectory)",
                ],
                "fact-kinds": ["npm-package-name", "npm-reference"],
            },
            {
                "id": "pnpm-lock-v1",
                "artifact": "pnpm-lock.yaml@9.0",
                "input-mode": "strict-utf8-file",
                "snapshot-inputs": ["pnpm-lock.yaml"],
                "packages": [
                    "@pnpm/deps.path",
                    "@pnpm/lockfile.fs",
                    "@pnpm/lockfile.utils",
                    "@pnpm/resolving.npm-resolver",
                    "@pnpm/workspace.spec-parser",
                ],
                "apis": [
                    "extractMainDocument",
                    "readWantedLockfileWithMergeInfo",
                    "WorkspaceSpec.parse",
                    "workspacePrefToNpm",
                    "parseBareSpecifier",
                    "refToRelative",
                    "nameVerFromPkgSnapshot",
                    "pkgSnapshotToResolution",
                ],
                "fact-kinds": [
                    "pnpm-lock-snapshot",
                    "pnpm-lock-importer-reference",
                ],
            },
            {
                "id": "pnpm-workspace-v1",
                "artifact": "pnpm-workspace.yaml",
                "input-mode": "strict-utf8-file",
                "snapshot-inputs": ["pnpm-workspace.yaml"],
                "packages": [
                    "@pnpm/resolving.npm-resolver",
                    "@pnpm/workspace.spec-parser",
                    "@pnpm/workspace.workspace-manifest-reader",
                    "npm-package-arg",
                ],
                "apis": [
                    "readWorkspaceManifest(snapshotDirectory)",
                    "WorkspaceSpec.parse",
                    "workspacePrefToNpm",
                    "parseBareSpecifier",
                    "npa.resolve(name,spec,snapshotDirectory)",
                ],
                "fact-kinds": [
                    "pnpm-workspace-pattern",
                    "pnpm-workspace-reference",
                ],
            },
            {
                "id": "nuget-lock-v1",
                "artifacts": [
                    "packages.lock.json@1-3",
                    "packages.config",
                ],
                "input-modes": [
                    {
                        "artifact": "packages.lock.json",
                        "mode": "strict-utf8-byte-stream",
                    },
                    {
                        "artifact": "packages.config",
                        "mode": "xml-byte-stream",
                    },
                ],
                "packages": [
                    "NuGet.Packaging",
                    "NuGet.ProjectModel",
                ],
                "apis": [
                    (
                        "PackagesLockFileFormat.Read("
                        "Stream,NullLogger.Instance,repositoryLogicalPath)"
                    ),
                    "PackagesConfigReader(Stream,false).GetPackages(false)",
                ],
                "fact-kinds": [
                    "nuget-lock-dependency",
                    "nuget-packages-config-entry",
                ],
            },
        ],
        "normalized-fact-contracts": _static_reference_fact_contracts(),
    }


def static_reference_policy_document() -> dict[str, JsonValue]:
    """Return the canonical policy document bound to the authority manifest."""
    return {
        "schema": STATIC_REFERENCE_POLICY_SCHEMA,
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "producer": {
            "package": PRODUCER_PACKAGE,
            "root": PRODUCER_ROOT,
            "manifest": PRODUCER_MANIFEST,
        },
        "source-kinds": ["git-target", "index", "worktree"],
        "selectors": [
            {
                "basename": "package.json",
                "family": "npm-manifest",
                "graph": "npm-manifest-v1",
            },
            {
                "basename": "pnpm-lock.yaml",
                "exclude-descendant": ".github/workflows",
                "family": "pnpm-lock",
                "graph": "pnpm-lock-v1",
            },
            {
                "basename": "pnpm-workspace.yaml",
                "exclude-descendant": ".github/workflows",
                "family": "pnpm-workspace",
                "graph": "pnpm-workspace-v1",
            },
            {
                "basename": "packages.lock.json",
                "family": "nuget-lock",
                "graph": "nuget-lock-v1",
            },
            {
                "basename": "packages.config",
                "family": "nuget-packages-config",
                "graph": "nuget-lock-v1",
            },
        ],
        "authority-manifest": static_reference_authority_manifest(),
        "prohibited-forms": [
            "A",
            "D",
            "L",
            "V",
            "W",
            "dependency-key",
        ],
        "allowances": [
            "producer-name-in-exact-producer-manifest",
            "producer-root-outside-dependency-position",
        ],
        "traversal": {
            "inventory-order": "normalized-posix-path-utf8-bytes",
            "graph-order": "declared",
            "array-order": "index",
            "mapping-order": "declared-section-then-utf8-key",
        },
        "failure-selection": {
            "source-before-graph": True,
            "first-typed-graph-failure": True,
            "cleanup-overrides": True,
            "partial-findings-on-error": False,
        },
    }


STATIC_REFERENCE_POLICY_DIGEST = canonical_sha256(
    static_reference_policy_document()
)


@dataclass(slots=True)
class _ScanState:
    implementation_identities: set[str] = field(default_factory=set)
    findings: set[StaticReferenceFinding] = field(default_factory=set)
    error_kind: StaticReferenceErrorKind | None = None
    cleanup_overridden_error_kind: StaticReferenceErrorKind | None = None


@dataclass(frozen=True, slots=True)
class _ScanContext:
    repository_root: Path
    inventory: StaticReferenceInventory
    session: StaticReferenceSession
    authority_runner: AuthorityRunner
    state: _ScanState


def _run_materialized_candidate(
    context: _ScanContext,
    candidate: StaticReferenceCandidate,
    invocation: MaterializedAuthorityInvocation,
) -> None:
    try:
        outcome = context.authority_runner(
            context.repository_root,
            candidate,
            invocation,
            context.session,
        )
    except AuthorityExecutionError:
        context.state.error_kind = "authority-execution-failed"
        return
    context.state.implementation_identities.update(
        outcome.implementation_identities
    )
    if outcome.error_kind is not None:
        context.state.error_kind = outcome.error_kind
        return
    try:
        context.state.findings.update(
            project_static_reference_facts(candidate, outcome.facts)
        )
    except StaticReferenceProjectionError:
        context.state.error_kind = "unsupported-projection"


def _release_invocation(
    context: _ScanContext,
    invocation: MaterializedAuthorityInvocation,
) -> None:
    try:
        context.session.release(invocation)
    except StaticReferenceCleanupError:
        _record_cleanup_failure(context.state)


def _record_cleanup_failure(state: _ScanState) -> None:
    if (
        state.error_kind is not None
        and state.error_kind != "cleanup-failed"
        and state.cleanup_overridden_error_kind is None
    ):
        state.cleanup_overridden_error_kind = state.error_kind
    state.error_kind = "cleanup-failed"


def _materialize_inventory(
    context: _ScanContext,
) -> deque[tuple[StaticReferenceCandidate, MaterializedAuthorityInvocation]]:
    materialized: deque[
        tuple[StaticReferenceCandidate, MaterializedAuthorityInvocation]
    ] = deque()
    for candidate in context.inventory.candidates:
        if candidate.selection.input_mode == "strict-utf8-file":
            try:
                candidate.content.decode("utf-8", "strict")
            except UnicodeDecodeError:
                context.state.error_kind = "encoding-rejected"
                break
        try:
            invocation = context.session.materialize(
                candidate,
                source_kind=context.inventory.source_kind,
                target=context.inventory.target,
            )
        except OSError:
            context.state.error_kind = "source-acquisition-failed"
            break
        materialized.append((candidate, invocation))
    return materialized


def _run_materialized_inventory(
    context: _ScanContext,
    materialized: deque[
        tuple[StaticReferenceCandidate, MaterializedAuthorityInvocation]
    ],
) -> None:
    while materialized and context.state.error_kind is None:
        candidate, invocation = materialized.popleft()
        try:
            _run_materialized_candidate(context, candidate, invocation)
        finally:
            _release_invocation(context, invocation)


def _scan_inventory(
    repository_root: Path,
    inventory: StaticReferenceInventory,
    *,
    authority_runner: AuthorityRunner | None,
    session_factory: SessionFactory,
) -> _ScanState:
    state = _ScanState()
    try:
        session = session_factory()
    except OSError:
        state.error_kind = "source-acquisition-failed"
        return state
    context = _ScanContext(
        repository_root,
        inventory,
        session,
        run_authority_graph if authority_runner is None else authority_runner,
        state,
    )
    materialized: deque[
        tuple[StaticReferenceCandidate, MaterializedAuthorityInvocation]
    ] = deque()
    try:
        materialized = _materialize_inventory(context)
        if state.error_kind is None:
            _run_materialized_inventory(context, materialized)
    finally:
        for _, invocation in materialized:
            _release_invocation(context, invocation)
        try:
            session.close()
        except StaticReferenceCleanupError:
            _record_cleanup_failure(state)
    if state.cleanup_overridden_error_kind is not None:
        _LOGGER.warning(
            "static-reference cleanup failure overrode prior error-kind=%s",
            state.cleanup_overridden_error_kind,
        )
    return state


def _result(
    *,
    source_kind: StaticReferenceSourceKind,
    target: str | None,
    state: _ScanState,
) -> BoundedStaticReferenceResult:
    findings = (
        ()
        if state.error_kind is not None
        else tuple(
            sorted(
                state.findings,
                key=StaticReferenceFinding.sort_key,
            )
        )
    )
    return BoundedStaticReferenceResult(
        source_kind=source_kind,
        target=target,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=tuple(
            sorted(state.implementation_identities, key=utf8_sort_key)
        ),
        findings=findings,
        error_kind=state.error_kind,
    )


def scan_bounded_static_references(
    repository_root: Path,
    *,
    source_kind: StaticReferenceSourceKind,
    target: str | None = None,
    authority_runner: AuthorityRunner | None = None,
    session_factory: SessionFactory = StaticReferenceSession,
) -> BoundedStaticReferenceResult:
    """Scan one admitted exact source through the retained authority graph."""
    try:
        inventory = acquire_static_reference_inventory(
            repository_root,
            source_kind=source_kind,
            target=target,
        )
    except SourceAcquisitionError:
        return _result(
            source_kind=source_kind,
            target=target,
            state=_ScanState(error_kind="source-acquisition-failed"),
        )
    root = repository_root.resolve(strict=True)
    state = _scan_inventory(
        root,
        inventory,
        authority_runner=authority_runner,
        session_factory=session_factory,
    )
    return _result(
        source_kind=inventory.source_kind,
        target=inventory.target,
        state=state,
    )


def validate_bounded_static_reference_result(
    result: BoundedStaticReferenceResult,
) -> None:
    """Validate a Result against the exact current policy identity."""
    if type(result) is not BoundedStaticReferenceResult:
        message = "bounded static-reference Result has the wrong type"
        raise TypeError(message)
    if (
        result.policy_id != STATIC_REFERENCE_POLICY_ID
        or result.policy_digest != STATIC_REFERENCE_POLICY_DIGEST
    ):
        message = "bounded static-reference Result policy is not current"
        raise ValueError(message)
    if (
        result.error_kind == "source-acquisition-failed"
        and result.implementation_identities
    ):
        message = (
            "source-acquisition-failed Result cannot contain "
            "implementation identities"
        )
        raise ValueError(message)


__all__ = [
    "STATIC_REFERENCE_POLICY_DIGEST",
    "scan_bounded_static_references",
    "static_reference_authority_manifest",
    "static_reference_policy_document",
    "validate_bounded_static_reference_result",
]
