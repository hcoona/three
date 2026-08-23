"""Canonical authority for the first-slice consumer policy."""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import PurePosixPath

from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256

CONSUMER_POLICY_ID = "release/no-smoke-package-consumers-v1"
CONSUMER_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
NODE_DEPENDENCY_FIELDS = (
    "dependencies",
    "devDependencies",
    "optionalDependencies",
    "peerDependencies",
)
POLICY_IMPLEMENTATION_PATH = (
    "eng/scripts/workflow_delivery_v3_consumer_policy.py"
)
POLICY_AUTHORITY_PATH = (
    "src/public/lib/three-workflow-delivery-v3/src/"
    "three_workflow_delivery_v3/release/consumer_policy.py"
)
JAVASCRIPT_ANALYZER_PATH = (
    "src/public/lib/three-workflow-delivery-v3/src/"
    "three_workflow_delivery_v3/release/javascript_consumer.py"
)
JAVASCRIPT_ANALYZER_PATHS = (JAVASCRIPT_ANALYZER_PATH,)
GIT_ATTRIBUTES_PATH = ".gitattributes"
OWN_DECLARATION_PATH = "src/public/lib/hcoona-release-smoke-npm/package.json"
ACCEPTANCE_FIXTURE_PATH = (
    "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
    "release/consumer-policy-acceptance.json"
)
ACCEPTANCE_NPM_MANIFEST_PATH = (
    "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
    "acceptance/npm-publish-request/package/package.json"
)

TREE_SITTER_VERSION = "0.25.2"
TREE_SITTER_JAVASCRIPT_VERSION = "0.25.0"
TREE_SITTER_TYPESCRIPT_VERSION = "0.23.2"
JAVASCRIPT_SOURCE_BYTE_LIMIT = 256 * 1024
JAVASCRIPT_AST_NODE_LIMIT = 50_000
JAVASCRIPT_AST_DEPTH_LIMIT = 128
JAVASCRIPT_COMMONJS_GLOBAL_SUFFIXES = (".cjs", ".js", ".ts")
JAVASCRIPT_ANALYSIS_SEMANTICS_ID = (
    "rc-033-tree-sitter-static-consumer-subset-v1"
)
JAVASCRIPT_UNKNOWN_ADMISSION_POLICY = "relevant-unknown-fail-closed-v1"
JAVASCRIPT_RELEVANT_UNKNOWN_ADMISSION_RULE = (
    "admit unsupported syntax only when it is unrelated to package data and "
    "sensitive process, loader, pnpm-writer, or dynamic-code relevance; "
    "otherwise fail closed"
)
JAVASCRIPT_SUPPORTED_CONSTRUCTS = (
    "pinned-tree-sitter-javascript-and-typescript-parsing",
    "decoded-exact-ecmascript-strings-and-static-string-arrays",
    "bounded-syntactic-package-and-sensitive-identity-relevance",
    "file-global-unique-immutable-constants-and-one-identifier-alias",
    "value-preserving-typescript-expression-wrappers",
    "direct-esm-commonjs-module-require-and-typescript-package-loads",
    "direct-child-process-import-require-and-static-member-apis",
    "direct-create-require-and-get-builtin-module-loaders",
    "direct-import-meta-resolve-package-resolution",
    "suffix-routed-commonjs-require-and-module-globals",
    "exact-exec-and-exec-sync-shell-command-matching",
    "exact-spawn-and-exec-file-structured-argument-matching",
    "syntactic-second-argument-options-overloads",
    "direct-pnpm-dependency-writes-updates-deletes-and-known-mutators",
    "type-only-comment-regex-and-metadata-pruning",
    "relevant-unsupported-syntactic-and-dynamic-code-barriers",
)

_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")


def _root_and_recursive(*patterns: str) -> tuple[str, ...]:
    return (*patterns, *(f"**/{pattern}" for pattern in patterns))


def _script_patterns() -> tuple[str, ...]:
    suffixes = (
        "bat",
        "bash",
        "cjs",
        "cmd",
        "js",
        "mjs",
        "ps1",
        "py",
        "sh",
        "ts",
        "zsh",
    )
    names = [
        f"{prefix}*.{suffix}"
        for prefix in ("bootstrap", "install", "setup")
        for suffix in suffixes
    ]
    names.extend(
        f"postinstall*.{suffix}" for suffix in ("cjs", "js", "mjs", "ts")
    )
    return _root_and_recursive(*names)


@dataclass(frozen=True, slots=True)
class DependencySurfaceRule:
    """One closed dependency-consumer path and syntax family."""

    category: str
    path_patterns: tuple[str, ...]
    syntax_contexts: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical policy representation."""
        patterns: list[JsonValue] = list(self.path_patterns)
        contexts: list[JsonValue] = list(self.syntax_contexts)
        return {
            "category": self.category,
            "path-patterns": patterns,
            "syntax-contexts": contexts,
        }


@dataclass(frozen=True, slots=True)
class ApprovedConsumerException:
    """One reviewed path, context, and exact byte-digest exception."""

    path: str
    category: str
    context: str
    content_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical policy representation."""
        return {
            "path": self.path,
            "category": self.category,
            "context": self.context,
            "content-digest": self.content_digest,
        }


DEPENDENCY_SURFACE_CATALOG = (
    DependencySurfaceRule(
        "dependency-manifest",
        (
            *_root_and_recursive(
                "Directory.Packages.props",
                "package.json",
                "packages.config",
                "pyproject.toml",
                "requirements*.txt",
                "setup.py",
                "*.csproj",
                "*.fsproj",
                "*.vbproj",
            ),
            ACCEPTANCE_FIXTURE_PATH,
        ),
        (
            "node-dependency",
            "python-dependency",
            "dotnet-package-reference",
        ),
    ),
    DependencySurfaceRule(
        "lockfile",
        _root_and_recursive(
            "bun.lock",
            "npm-shrinkwrap.json",
            "package-lock.json",
            "packages.lock.json",
            "pnpm-lock.yaml",
            "poetry.lock",
            "uv.lock",
            "yarn.lock",
        ),
        ("dependency-key", "package-key", "node_modules-path"),
    ),
    DependencySurfaceRule(
        "workflow",
        _root_and_recursive(
            ".github/workflows/*.yaml",
            ".github/workflows/*.yml",
        ),
        ("uses", "run", "with", "env"),
    ),
    DependencySurfaceRule(
        "composite-action",
        (".github/actions/**/action.yaml", ".github/actions/**/action.yml"),
        ("uses", "run", "with", "env"),
    ),
    DependencySurfaceRule(
        "install-bootstrap-script",
        (
            *_script_patterns(),
            POLICY_IMPLEMENTATION_PATH,
            POLICY_AUTHORITY_PATH,
            *JAVASCRIPT_ANALYZER_PATHS,
        ),
        ("package-manager-command", "module-import"),
    ),
    DependencySurfaceRule(
        "dependency-configuration",
        (
            *_root_and_recursive(
                ".github/dependabot.yaml",
                ".github/dependabot.yml",
                ".npmrc",
                ".pnpmfile.cjs",
                ".yarnrc",
                ".yarnrc.yml",
                "NuGet.config",
                "bunfig.toml",
                "nuget.config",
                "pnpm-workspace.yaml",
                "renovate.json",
            ),
            GIT_ATTRIBUTES_PATH,
        ),
        ("dependency-selection", "package-manager-command"),
    ),
)

APPROVED_CONSUMER_EXCEPTIONS = (
    ApprovedConsumerException(
        OWN_DECLARATION_PATH,
        "dependency-manifest",
        "name",
        "sha256:a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566",
    ),
    ApprovedConsumerException(
        ACCEPTANCE_FIXTURE_PATH,
        "dependency-manifest",
        f"dependencies.{CONSUMER_PACKAGE}",
        "sha256:a28d7f1e161df6948cdc2f122e78b9a38f425b481877178e29c8cd8ef30b0aa2",
    ),
    ApprovedConsumerException(
        ACCEPTANCE_NPM_MANIFEST_PATH,
        "dependency-manifest",
        "name",
        "sha256:d032b543a77820f9660a629e7deee6140664150a2c0a7de8048d37947afc957e",
    ),
)

CONSUMER_POLICY_HK_GLOBS = tuple(
    sorted(
        {
            pattern
            for rule in DEPENDENCY_SURFACE_CATALOG
            for pattern in rule.path_patterns
        }
        | {
            "hk.pkl",
            (
                "src/public/lib/three-workflow-delivery-v3/tests/ci/"
                "test_consumer_policy.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/"
                "test_hk_trigger.py"
            ),
        }
    )
)


def consumer_policy_parser_profile() -> dict[str, JsonValue]:
    """Return the exact parser engine, grammar, and limit profile."""
    commonjs_global_suffixes: list[JsonValue] = list(
        JAVASCRIPT_COMMONJS_GLOBAL_SUFFIXES
    )
    grammars: list[JsonValue] = [
        {
            "language": "javascript",
            "distribution": "tree-sitter-javascript",
            "version": TREE_SITTER_JAVASCRIPT_VERSION,
        },
        {
            "language": "typescript",
            "distribution": "tree-sitter-typescript",
            "version": TREE_SITTER_TYPESCRIPT_VERSION,
        },
    ]
    return {
        "engine": "tree-sitter",
        "analysis": {
            "semantics-id": JAVASCRIPT_ANALYSIS_SEMANTICS_ID,
            "unknown-admission-policy": (JAVASCRIPT_UNKNOWN_ADMISSION_POLICY),
            "relevant-unknown-admission-rule": (
                JAVASCRIPT_RELEVANT_UNKNOWN_ADMISSION_RULE
            ),
            "supported-constructs": list(JAVASCRIPT_SUPPORTED_CONSTRUCTS),
        },
        "runtime": {
            "distribution": "tree-sitter",
            "version": TREE_SITTER_VERSION,
        },
        "grammars": grammars,
        "limits": {
            "source-bytes": JAVASCRIPT_SOURCE_BYTE_LIMIT,
            "ast-nodes": JAVASCRIPT_AST_NODE_LIMIT,
            "ast-depth": JAVASCRIPT_AST_DEPTH_LIMIT,
        },
        "commonjs-global-suffixes": commonjs_global_suffixes,
    }


def consumer_policy_document() -> dict[str, JsonValue]:
    """Return the complete canonical consumer-policy definition."""
    dependency_fields: list[JsonValue] = list(NODE_DEPENDENCY_FIELDS)
    catalog: list[JsonValue] = [
        rule.to_document() for rule in DEPENDENCY_SURFACE_CATALOG
    ]
    exceptions: list[JsonValue] = [
        item.to_document() for item in APPROVED_CONSUMER_EXCEPTIONS
    ]
    return {
        "schema": "workflow-delivery/v3/consumer-policy",
        "policy-id": CONSUMER_POLICY_ID,
        "package": CONSUMER_PACKAGE,
        "node-dependency-fields": dependency_fields,
        "catalog": catalog,
        "approved-exceptions": exceptions,
        "parser": consumer_policy_parser_profile(),
    }


CONSUMER_POLICY_DIGEST = canonical_sha256(consumer_policy_document())


@dataclass(frozen=True, slots=True)
class SurfaceDigest:
    """One exact target dependency surface and its content digest."""

    path: str
    content_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical surface binding."""
        return {
            "path": self.path,
            "content-digest": self.content_digest,
        }


@dataclass(frozen=True, slots=True)
class ConsumerPolicyResult:
    """Permanent target-bound consumer-policy result."""

    policy_id: str
    policy_digest: str
    target: str
    scanned_surfaces: tuple[SurfaceDigest, ...]
    admitted_exceptions: tuple[SurfaceDigest, ...]
    consumers: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical consumer-policy result."""
        scanned: list[JsonValue] = [
            surface.to_document() for surface in self.scanned_surfaces
        ]
        exceptions: list[JsonValue] = [
            surface.to_document() for surface in self.admitted_exceptions
        ]
        consumers: list[JsonValue] = list(self.consumers)
        return {
            "schema": "workflow-delivery/v3/consumer-policy-result",
            "policy-id": self.policy_id,
            "policy-digest": self.policy_digest,
            "target": self.target,
            "scanned-surfaces": scanned,
            "admitted-exceptions": exceptions,
            "consumers": consumers,
        }

    @property
    def result_digest(self) -> str:
        """Return the complete target-bound result digest."""
        return canonical_sha256(self.to_document())


APPROVED_EXCEPTION_SURFACES = tuple(
    sorted(
        (
            SurfaceDigest(item.path, item.content_digest)
            for item in APPROVED_CONSUMER_EXCEPTIONS
        ),
        key=lambda item: item.path,
    )
)


def _normalized_path(value: str, *, field: str) -> str:
    candidate = PurePosixPath(value)
    if (
        not value
        or candidate.is_absolute()
        or candidate.as_posix() != value
        or "\\" in value
        or any(part in {"", ".", ".."} for part in candidate.parts)
    ):
        message = f"{field} must be a normalized repository-relative path"
        raise ValueError(message)
    return value


def _validate_surfaces(
    surfaces: tuple[SurfaceDigest, ...],
    *,
    field: str,
) -> None:
    if type(surfaces) is not tuple or any(
        type(surface) is not SurfaceDigest for surface in surfaces
    ):
        message = f"{field} must contain exact SurfaceDigest values"
        raise TypeError(message)
    expected = tuple(sorted(surfaces, key=lambda surface: surface.path))
    if surfaces != expected:
        message = f"{field} must be sorted by path"
        raise ValueError(message)
    paths: set[str] = set()
    for surface in surfaces:
        _normalized_path(surface.path, field=f"{field}.path")
        if surface.path in paths:
            message = f"{field} contains a duplicate path"
            raise ValueError(message)
        paths.add(surface.path)
        if _DIGEST_PATTERN.fullmatch(surface.content_digest) is None:
            message = f"{field}.content_digest must be SHA-256"
            raise ValueError(message)


def _validate_exception_admission(
    result: ConsumerPolicyResult,
    scanned: dict[str, str],
) -> None:
    approved = {
        surface.path: surface.content_digest
        for surface in APPROVED_EXCEPTION_SURFACES
    }
    if any(
        approved.get(surface.path) != surface.content_digest
        for surface in result.admitted_exceptions
    ):
        message = "consumer-policy exception is not an exact approved exception"
        raise ValueError(message)
    if not approved.keys() <= scanned.keys():
        message = (
            "consumer-policy approved exception paths are missing from "
            "scanned surfaces"
        )
        raise ValueError(message)
    if any(
        scanned[surface.path] != surface.content_digest
        for surface in result.admitted_exceptions
    ):
        message = (
            "consumer-policy admitted exceptions do not match scanned surfaces"
        )
        raise ValueError(message)
    admitted_paths = {surface.path for surface in result.admitted_exceptions}
    if not (approved.keys() - admitted_paths) <= set(result.consumers):
        message = (
            "consumer-policy non-admitted approved exceptions must be consumers"
        )
        raise ValueError(message)


def validate_consumer_policy_result(  # noqa: C901
    result: ConsumerPolicyResult,
) -> None:
    """Validate one exact result against the current canonical policy."""
    if type(result) is not ConsumerPolicyResult:
        message = "consumer-policy result has the wrong type"
        raise TypeError(message)
    if result.policy_id != CONSUMER_POLICY_ID:
        message = "consumer-policy ID is not the static first-slice policy"
        raise ValueError(message)
    if _DIGEST_PATTERN.fullmatch(result.policy_digest) is None:
        message = "consumer-policy digest must be SHA-256"
        raise ValueError(message)
    if result.policy_digest != CONSUMER_POLICY_DIGEST:
        message = "consumer-policy digest is not the current canonical policy"
        raise ValueError(message)
    if _SHA_PATTERN.fullmatch(result.target) is None:
        message = "consumer-policy target must be a full commit SHA"
        raise ValueError(message)
    if not result.scanned_surfaces:
        message = "scanned_surfaces must be nonempty"
        raise ValueError(message)
    _validate_surfaces(result.scanned_surfaces, field="scanned_surfaces")
    _validate_surfaces(
        result.admitted_exceptions,
        field="admitted_exceptions",
    )
    scanned = {
        surface.path: surface.content_digest
        for surface in result.scanned_surfaces
    }
    if type(result.consumers) is not tuple or any(
        type(consumer) is not str for consumer in result.consumers
    ):
        message = "consumer-policy consumers must be exact strings"
        raise TypeError(message)
    if result.consumers != tuple(sorted(result.consumers)):
        message = "consumer-policy consumers must be sorted"
        raise ValueError(message)
    if len(set(result.consumers)) != len(result.consumers):
        message = "consumer-policy consumers contain duplicates"
        raise ValueError(message)
    for consumer in result.consumers:
        _normalized_path(consumer, field="consumer-policy consumer")
        if consumer not in scanned:
            message = "consumer-policy consumer was not in scanned surfaces"
            raise ValueError(message)
    _validate_exception_admission(result, scanned)
