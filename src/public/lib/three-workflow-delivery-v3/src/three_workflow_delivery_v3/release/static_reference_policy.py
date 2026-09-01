"""Canonical bounded static-reference policy orchestration."""

from __future__ import annotations

import hashlib
import logging
import tomllib
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
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
_AUTHORITY_PREPARATION_STAMP = Path(
    "artifacts/workflow-delivery-v3/static-reference/authority-preparation.json"
)
_AUTHORITY_DEPENDENCY_CLOSURES = (
    (
        "pnpm-lock",
        "pnpm-lock.yaml",
        "sha256:44ea8ea08134a04f079e89747de2f4b6219ff7dbc23365d66c9656e087a224ba",
    ),
    (
        "nuget-lock",
        (
            "src/private/app/workflow-delivery-v3-nuget-authority/"
            "packages.lock.json"
        ),
        "sha256:2fcd4e94b3b3be83522776536c4cae3f22aaa4bcbfe747a522627654c020cc5a",
    ),
)
_EXPECTED_IMPLEMENTATIONS: dict[str, tuple[str, ...]] = {
    "npm-manifest-v1": (
        "@npmcli/package-json@8.0.0",
        "node@24.19.0",
        "npm-package-arg@14.0.0",
    ),
    "pnpm-lock-v1": (
        "@pnpm/deps.path@1101.0.1",
        "@pnpm/lockfile.fs@1100.2.5",
        "@pnpm/lockfile.utils@1102.1.0",
        "@pnpm/resolving.npm-resolver@1104.1.0",
        "@pnpm/workspace.spec-parser@1100.0.1",
        "node@24.19.0",
    ),
    "pnpm-workspace-v1": (
        "@pnpm/resolving.npm-resolver@1104.1.0",
        "@pnpm/workspace.spec-parser@1100.0.1",
        "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
        "node@24.19.0",
        "npm-package-arg@14.0.0",
    ),
    "nuget-lock-v1": (
        "NuGet.Packaging@7.9.0",
        "NuGet.ProjectModel@7.9.0",
        "dotnet-runtime@10.0.8",
    ),
}
_LIVE_REQUIRED_GRAPH_IDS = (
    "npm-manifest-v1",
    "pnpm-lock-v1",
    "pnpm-workspace-v1",
)
_LIVE_REQUIRED_IMPLEMENTATION_IDENTITIES = frozenset(
    identity
    for graph_id in _LIVE_REQUIRED_GRAPH_IDS
    for identity in _EXPECTED_IMPLEMENTATIONS[graph_id]
)

_NODE_CHECKSUMS = {
    "linux-arm64": (
        "sha256:d28c8a5bf0a808f0ed434a1dce8c54ae98f0371c0bd86ac58abc613f73e6643f"
    ),
    "linux-arm64-musl": (
        "sha256:20824e4d35948fae5b337dccef47813b04d8995312f59df7386f2256d9f9ab7e"
    ),
    "linux-x64": (
        "sha256:f625d97cd707df4ff96254916fbc5ff014f09c09effe5a1e0ca8f6d41a8789d4"
    ),
    "linux-x64-musl": (
        "sha256:c60223786df14a5d23e220ebb8e60318f5322640a62f90e6d9e54d3a18da532e"
    ),
    "macos-arm64": (
        "sha256:8294b7aa9b03997481c06babf1e8b270c859358f27da57a11509afe537ac381d"
    ),
    "macos-x64": (
        "sha256:d1b5e999db158c62fe8f7267a4476b035d8bd93b1a605bac24a3f0dd166e3316"
    ),
    "windows-x64": (
        "sha256:57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73"
    ),
}
_PNPM_CHECKSUMS = {
    "linux-arm64": (
        "sha256:f1426231f365bdfd46c15fa3d1211c3936ee2c4e557afd304f6c66dbf1b2a8bf"
    ),
    "linux-arm64-musl": (
        "sha256:6e53557024be48e59ab8760f9117c0e5c0e0a37ab420f71f302d86216970d28f"
    ),
    "linux-x64": (
        "sha256:4c592fa410eb23b69691a9efb9bf21c87c15b3e9d88c6ec8acdd354a0eb8de71"
    ),
    "linux-x64-musl": (
        "sha256:45425b06e747cbcaff4940d7b4a55e694645f15f9339dbf7f2601cfb21400545"
    ),
    "macos-arm64": (
        "sha256:2000dcc8f0718852c2806ba4dca1edaedf18a4a39264474d5a1c8fcee250adfd"
    ),
    "windows-x64": (
        "sha256:1de83ad5100acfd2adb5c8bc6f8a428cee9ff4e365deff57c22bfc0cccaa4ddb"
    ),
}


@dataclass(frozen=True, slots=True)
class _MiseToolSpec:
    tool: str
    config_key: str
    selector: str
    lock_key: str
    backend: str
    version: str
    artifact_checksums: tuple[tuple[str, str], ...] = ()
    provenance: str | None = None


_MISE_TOOL_SPECS = (
    _MiseToolSpec(
        tool="dotnet",
        config_key="core:dotnet",
        selector="10",
        lock_key="dotnet",
        backend="core:dotnet",
        version="10.0.300",
    ),
    _MiseToolSpec(
        tool="node",
        config_key="node",
        selector="24",
        lock_key="node",
        backend="core:node",
        version="24.19.0",
        artifact_checksums=tuple(_NODE_CHECKSUMS.items()),
    ),
    _MiseToolSpec(
        tool="pnpm",
        config_key="pnpm",
        selector="11.22.0",
        lock_key="pnpm",
        backend="aqua:pnpm/pnpm",
        version="11.22.0",
        artifact_checksums=tuple(_PNPM_CHECKSUMS.items()),
        provenance="github-attestations",
    ),
)


def _mise_runtime_closure_document() -> dict[str, JsonValue]:
    selectors: list[JsonValue] = [
        {
            "tool": spec.tool,
            "config-key": spec.config_key,
            "selector": spec.selector,
            "lock-key": spec.lock_key,
        }
        for spec in _MISE_TOOL_SPECS
    ]
    tools: list[JsonValue] = []
    for spec in _MISE_TOOL_SPECS:
        tool: dict[str, JsonValue] = {
            "tool": spec.tool,
            "lock-key": spec.lock_key,
            "backend": spec.backend,
            "version": spec.version,
        }
        if spec.artifact_checksums:
            artifact_checksums: dict[str, JsonValue] = {}
            for platform, checksum in spec.artifact_checksums:
                artifact_checksums[platform] = checksum
            tool["artifact-checksums"] = artifact_checksums
        if spec.provenance is not None:
            tool["provenance"] = spec.provenance
        tools.append(tool)
    return {
        "mise-config": {
            "path": "mise.toml",
            "selectors": selectors,
        },
        "mise-lock": {
            "path": "mise.lock",
            "tools": tools,
        },
    }


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
    """Return the checked-in exact authority graph manifest."""
    return {
        "schema": "workflow-delivery/v3/static-reference-authority-manifest",
        "dependency-closures": [
            {
                "kind": kind,
                "path": path,
                "sha256": digest,
            }
            for kind, path, digest in _AUTHORITY_DEPENDENCY_CLOSURES
        ],
        "execution": {
            "preparation-command": [
                "mise",
                "run",
                "prepare:static-reference-authorities",
            ],
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
            "preparation-stamp": _AUTHORITY_PREPARATION_STAMP.as_posix(),
            "timeout-seconds": 30,
        },
        "graph-contracts": _static_reference_graph_contracts(),
        "graphs": [
            {
                "id": "npm-manifest-v1",
                "artifact": "package.json",
                "input-mode": "strict-utf8-file",
                "snapshot-inputs": ["package.json"],
                "implementations": list(
                    _EXPECTED_IMPLEMENTATIONS["npm-manifest-v1"]
                ),
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
                "implementations": list(
                    _EXPECTED_IMPLEMENTATIONS["pnpm-lock-v1"]
                ),
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
                "implementations": list(
                    _EXPECTED_IMPLEMENTATIONS["pnpm-workspace-v1"]
                ),
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
                "implementations": list(
                    _EXPECTED_IMPLEMENTATIONS["nuget-lock-v1"]
                ),
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
        "runtime-closure": _mise_runtime_closure_document(),
        "runtimes": [
            {
                "tool": "dotnet",
                "backend": "core:dotnet",
                "sdk-version": "10.0.300",
                "loaded-runtime": "dotnet-runtime@10.0.8",
            },
            {
                "tool": "node",
                "backend": "core:node",
                "version": "24.19.0",
                "loaded-runtime": "node@24.19.0",
                "artifact-checksums": dict(_NODE_CHECKSUMS),
            },
            {
                "tool": "pnpm",
                "backend": "aqua:pnpm/pnpm",
                "version": "11.22.0",
                "provenance": "github-attestations",
                "artifact-checksums": dict(_PNPM_CHECKSUMS),
            },
        ],
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


class StaticReferenceAuthorityMismatchError(RuntimeError):
    """The prepared authority closure does not match the current policy."""


def static_reference_authority_preparation_document() -> dict[str, JsonValue]:
    """Return the exact stamp bound to the current authority closure."""
    return {
        "schema": (
            "workflow-delivery/v3/static-reference-authority-preparation"
        ),
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "dependency-closures": [
            {
                "kind": kind,
                "path": path,
                "sha256": digest,
            }
            for kind, path, digest in _AUTHORITY_DEPENDENCY_CLOSURES
        ],
        "runtime-closure": _mise_runtime_closure_document(),
    }


def static_reference_authority_preparation_stamp_path(
    repository_root: Path,
) -> Path:
    """Return the exact repository-owned preparation stamp path."""
    return repository_root / _AUTHORITY_PREPARATION_STAMP


def _exact_mapping(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or any(
        type(key) is not str for key in value
    ):
        message = "static-reference authority closure does not match"
        raise StaticReferenceAuthorityMismatchError(message)
    return cast("dict[str, object]", value)


def _load_toml_document(path: Path) -> dict[str, object]:
    try:
        with path.open("rb") as stream:
            document: object = tomllib.load(stream)
    except (OSError, tomllib.TOMLDecodeError) as error:
        message = "static-reference authority closure is unavailable"
        raise StaticReferenceAuthorityMismatchError(message) from error
    return _exact_mapping(document)


def _validate_mise_runtime_closure(repository_root: Path) -> None:
    config = _load_toml_document(repository_root / "mise.toml")
    lock = _load_toml_document(repository_root / "mise.lock")
    config_tools = _exact_mapping(config.get("tools"))
    lock_tools = _exact_mapping(lock.get("tools"))
    for spec in _MISE_TOOL_SPECS:
        if config_tools.get(spec.config_key) != spec.selector:
            message = "static-reference authority closure does not match"
            raise StaticReferenceAuthorityMismatchError(message)
        entries = lock_tools.get(spec.lock_key)
        if type(entries) is not list or len(entries) != 1:
            message = "static-reference authority closure does not match"
            raise StaticReferenceAuthorityMismatchError(message)
        entry = _exact_mapping(entries[0])
        if (
            entry.get("backend") != spec.backend
            or entry.get("version") != spec.version
        ):
            message = "static-reference authority closure does not match"
            raise StaticReferenceAuthorityMismatchError(message)
        expected_checksums = dict(spec.artifact_checksums)
        if not expected_checksums:
            continue
        platforms = {
            key.removeprefix("platforms."): value
            for key, value in entry.items()
            if key.startswith("platforms.")
        }
        if set(platforms) != set(expected_checksums):
            message = "static-reference authority closure does not match"
            raise StaticReferenceAuthorityMismatchError(message)
        for platform, expected_checksum in expected_checksums.items():
            platform_entry = _exact_mapping(platforms.get(platform))
            if platform_entry.get("checksum") != expected_checksum:
                message = "static-reference authority closure does not match"
                raise StaticReferenceAuthorityMismatchError(message)
            if (
                spec.provenance is not None
                and platform_entry.get("provenance") != spec.provenance
            ):
                message = "static-reference authority closure does not match"
                raise StaticReferenceAuthorityMismatchError(message)


def validate_static_reference_dependency_closures(
    repository_root: Path,
) -> None:
    """Require the checked-in package-manager locks bound by the policy."""
    for _, relative_path, expected_digest in _AUTHORITY_DEPENDENCY_CLOSURES:
        try:
            content = (repository_root / relative_path).read_bytes()
        except OSError as error:
            message = "static-reference authority closure is unavailable"
            raise StaticReferenceAuthorityMismatchError(message) from error
        observed_digest = f"sha256:{hashlib.sha256(content).hexdigest()}"
        if observed_digest != expected_digest:
            message = "static-reference authority closure does not match"
            raise StaticReferenceAuthorityMismatchError(message)
    _validate_mise_runtime_closure(repository_root)


def validate_static_reference_authority_preparation(
    repository_root: Path,
) -> None:
    """Require current locks and the stamp written after preparation."""
    validate_static_reference_dependency_closures(repository_root)
    stamp_path = static_reference_authority_preparation_stamp_path(
        repository_root
    )
    try:
        stamp = stamp_path.read_bytes()
    except OSError as error:
        message = "static-reference authority preparation is unavailable"
        raise StaticReferenceAuthorityMismatchError(message) from error
    if stamp != canonicalize(static_reference_authority_preparation_document()):
        message = "static-reference authority preparation does not match"
        raise StaticReferenceAuthorityMismatchError(message)


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


def _implementation_mismatch(
    outcome: AuthorityGraphOutcome,
) -> bool:
    expected_values = _EXPECTED_IMPLEMENTATIONS.get(outcome.graph_id)
    if expected_values is None:
        return True
    expected = set(expected_values)
    observed = set(outcome.implementation_identities)
    if not observed <= expected:
        return True
    return outcome.error_kind is None and observed != expected


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
    if _implementation_mismatch(outcome):
        context.state.error_kind = "authority-mismatch"
        return
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
        if state.error_kind is None and authority_runner is None:
            try:
                validate_static_reference_authority_preparation(repository_root)
            except StaticReferenceAuthorityMismatchError:
                state.error_kind = "authority-mismatch"

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
    if result.error_kind is None:
        admitted_closures = {frozenset[str]()}
        for implementation_set in _EXPECTED_IMPLEMENTATIONS.values():
            graph_closure = frozenset(implementation_set)
            admitted_closures.update(
                closure | graph_closure for closure in tuple(admitted_closures)
            )
        if frozenset(result.implementation_identities) not in admitted_closures:
            message = (
                "bounded static-reference Result implementations are "
                "not complete graph closures"
            )
            raise ValueError(message)


def validate_live_static_reference_result(
    result: BoundedStaticReferenceResult,
) -> None:
    """Require the mandatory first-slice authority closure for Live clean."""
    validate_bounded_static_reference_result(result)
    if (
        result.result == "clean"
        and not frozenset(result.implementation_identities)
        >= _LIVE_REQUIRED_IMPLEMENTATION_IDENTITIES
    ):
        message = "Live static-reference Result implementations are incomplete"
        raise ValueError(message)


__all__ = [
    "STATIC_REFERENCE_POLICY_DIGEST",
    "StaticReferenceAuthorityMismatchError",
    "scan_bounded_static_references",
    "static_reference_authority_manifest",
    "static_reference_authority_preparation_document",
    "static_reference_authority_preparation_stamp_path",
    "static_reference_policy_document",
    "validate_bounded_static_reference_result",
    "validate_live_static_reference_result",
    "validate_static_reference_authority_preparation",
    "validate_static_reference_dependency_closures",
]
