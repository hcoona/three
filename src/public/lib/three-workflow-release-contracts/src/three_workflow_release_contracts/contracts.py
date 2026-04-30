"""Closed JSON contract validators for workflow-release handoffs."""

from __future__ import annotations

import re
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from typing import Any

type JsonObject = Mapping[str, object]

_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_WINDOWS_DRIVE_ABSOLUTE_RE = re.compile(r"^[A-Za-z]:($|/)")
_SIGNER_WORKFLOW_RE = re.compile(
    r"^(?:[A-Za-z0-9.-]+/)?[A-Za-z0-9_.-]+/"
    r"[A-Za-z0-9_.-]+/\.github/workflows/[^/]+\.ya?ml$",
)
_REGISTERED_CODES = {
    "REQ_INVALID_INPUT",
    "REQ_FORCE_FOR_OFFICIAL",
    "REQ_PROJECT_NOT_FOUND",
    "DESC_SCHEMA_INVALID",
    "DESC_STATIC_INVALID",
    "CATALOG_SCHEMA_INVALID",
    "CATALOG_REF_NOT_FOUND",
    "VERSION_AUTHORITY_FAILED",
    "DOTNET_METADATA_FAILED",
    "PUBLISH_IDENTITY_CONFLICT",
    "PYPI_FILENAME_COMPUTE_FAILED",
    "REMOTE_QUERY_FAILED",
    "REMOTE_NORMALIZATION_FAILED",
    "REMOTE_CLASSIFICATION_FAILED",
    "IMMUTABLE_PROOF_UNAVAILABLE",
    "IMMUTABLE_PARTIAL_UNSUPPORTED",
    "REMOTE_CONFLICTING",
    "OFFICIAL_FROZEN_VERSION",
    "REQ_ACTOR_UNAUTHORIZED",
    "REQ_UNTRUSTED_WORKFLOW_REF",
    "REQ_EXTERNAL_TARGET_DISABLED",
    "REQ_EXTERNAL_TOPOLOGY_BLOCKED",
    "PLAN_INTERNAL_INVARIANT",
}
REGISTERED_DIAGNOSTIC_CODES = frozenset(_REGISTERED_CODES)
"""Registered planner diagnostic code vocabulary for v1alpha1 contracts."""
_BUILD_DIAGNOSTIC_CODES = {
    "BUILD_FAILED",
    "BUILD_INVALID_INPUT",
    "BUILD_CHECKOUT_FAILED",
    "BUILD_OUTPUT_INVALID",
}
REGISTERED_BUILD_DIAGNOSTIC_CODES = frozenset(_BUILD_DIAGNOSTIC_CODES)
"""Registered build diagnostic code vocabulary for v1alpha1 contracts."""
_PHASES = {"validation", "query", "normalization", "classification"}
_SCOPE_KINDS = {"request", "project", "publish-node"}
_PROFILES = {"buddy", "official"}
_TOPOLOGIES = {
    "github-token",
    "external-oidc-entry-workflow",
    "external-oidc-caller-workflow",
    "external-oidc-reusable-workflow",
}
_CONTRACT_IDS_BY_FAMILY = {
    "github-release": "github-release-assets",
    "nuget": "nuget-publish",
    "pypi": "pypi-publish",
    "npm": "npm-publish",
    "rubygems": "rubygems-publish",
}
_EXPECTED_CONTRACTS = {
    "github-release-assets": {
        "allowed-artifact-tuples": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "nuget",
            },
            {
                "role": "symbols",
                "kind-family": "package",
                "concrete-kind": "snupkg",
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "wheel",
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "sdist",
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "npm-package",
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "rubygem",
            },
            {
                "role": "primary-binary",
                "kind-family": "binary",
                "concrete-kind": "executable",
            },
            {
                "role": "installer",
                "kind-family": "installer",
                "concrete-kind": "inno-setup",
            },
        ],
        "aggregate-rules": {
            "min-artifact-count": 1,
            "max-artifact-count": None,
            "cross-variant-policy": "allow",
            "tuple-rules": [
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "nuget",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "symbols",
                    "kind-family": "package",
                    "concrete-kind": "snupkg",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "wheel",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "sdist",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "npm-package",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "rubygem",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "primary-binary",
                    "kind-family": "binary",
                    "concrete-kind": "executable",
                    "min-count": 0,
                    "max-count": None,
                },
                {
                    "role": "installer",
                    "kind-family": "installer",
                    "concrete-kind": "inno-setup",
                    "min-count": 0,
                    "max-count": None,
                },
            ],
        },
    },
    "nuget-publish": {
        "allowed-artifact-tuples": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "nuget",
            },
            {
                "role": "symbols",
                "kind-family": "package",
                "concrete-kind": "snupkg",
            },
        ],
        "aggregate-rules": {
            "min-artifact-count": 1,
            "max-artifact-count": 2,
            "cross-variant-policy": "forbid",
            "tuple-rules": [
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "nuget",
                    "min-count": 1,
                    "max-count": 1,
                },
                {
                    "role": "symbols",
                    "kind-family": "package",
                    "concrete-kind": "snupkg",
                    "min-count": 0,
                    "max-count": 1,
                },
            ],
        },
    },
    "pypi-publish": {
        "allowed-artifact-tuples": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "wheel",
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "sdist",
            },
        ],
        "aggregate-rules": {
            "min-artifact-count": 1,
            "max-artifact-count": 2,
            "cross-variant-policy": "forbid",
            "tuple-rules": [
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "wheel",
                    "min-count": 1,
                    "max-count": 1,
                },
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "sdist",
                    "min-count": 0,
                    "max-count": 1,
                },
            ],
        },
    },
    "npm-publish": {
        "allowed-artifact-tuples": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "npm-package",
            },
        ],
        "aggregate-rules": {
            "min-artifact-count": 1,
            "max-artifact-count": 1,
            "cross-variant-policy": "forbid",
            "tuple-rules": [
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "npm-package",
                    "min-count": 1,
                    "max-count": 1,
                },
            ],
        },
    },
    "rubygems-publish": {
        "allowed-artifact-tuples": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "rubygem",
            },
        ],
        "aggregate-rules": {
            "min-artifact-count": 1,
            "max-artifact-count": 1,
            "cross-variant-policy": "forbid",
            "tuple-rules": [
                {
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": "rubygem",
                    "min-count": 1,
                    "max-count": 1,
                },
            ],
        },
    },
}
_CAPABILITY_VALUES = {
    "mutability": {"immutable", "mutable-prerelease", "replaceable"},
    "name-uniqueness-scope": {
        "release-tag",
        "package-name",
        "package-name-with-owner",
    },
    "version-uniqueness-rule": {
        "tag",
        "version",
        "package-name-plus-version",
    },
    "profile-coexistence-rule": {
        "same-name-allowed",
        "requires-distinct-name",
        "not-applicable",
    },
    "credential-posture": {"oidc", "github-token"},
    "publish-topology": _TOPOLOGIES,
}
_GITHUB_SLUG_RE = re.compile(r"^[A-Za-z0-9_.-]+$")


@dataclass(frozen=True, slots=True)
class ValidationIssue:
    """One contract validation failure."""

    path: str
    message: str


class ContractValidationError(ValueError):
    """Raised when a JSON object violates a frozen contract."""

    def __init__(self, issues: Sequence[ValidationIssue]) -> None:
        """Initialize the error from one or more validation issues."""
        self.issues = tuple(issues)
        joined = "; ".join(f"{issue.path}: {issue.message}" for issue in issues)
        super().__init__(joined)


class _Validator:
    """Mutable collector for validation issues."""

    def __init__(self) -> None:
        """Create an empty validator."""
        self.issues: list[ValidationIssue] = []

    def error(self, path: str, message: str) -> None:
        """Record a validation failure."""
        self.issues.append(ValidationIssue(path, message))

    def mapping(
        self,
        value: object,
        path: str,
        required: set[str],
        optional: set[str] | None = None,
        *,
        allow_extra: bool = False,
    ) -> JsonObject | None:
        """Validate a mapping with required and optional keys."""
        if not isinstance(value, Mapping):
            self.error(path, "must be an object")
            return None
        keys = set(value)
        for key in sorted(required - keys):
            self.error(f"{path}.{key}", "is required")
        allowed = required | (optional or set())
        if not allow_extra:
            for key in sorted(keys - allowed):
                self.error(f"{path}.{key}", "is not allowed")
        return value

    def string(
        self, value: object, path: str, *, non_empty: bool = True
    ) -> bool:
        """Validate a string value."""
        if not isinstance(value, str):
            self.error(path, "must be a string")
            return False
        if non_empty and value == "":
            self.error(path, "must not be empty")
            return False
        return True

    def integer(
        self, value: object, path: str, *, minimum: int | None = None
    ) -> bool:
        """Validate an integer value."""
        if not isinstance(value, int) or isinstance(value, bool):
            self.error(path, "must be an integer")
            return False
        if minimum is not None and value < minimum:
            self.error(path, f"must be >= {minimum}")
            return False
        return True

    def boolean(self, value: object, path: str) -> bool:
        """Validate a boolean value."""
        if not isinstance(value, bool):
            self.error(path, "must be a boolean")
            return False
        return True

    def enum(self, value: object, path: str, allowed: set[str]) -> bool:
        """Validate a string enum value."""
        if not self.string(value, path):
            return False
        if value not in allowed:
            self.error(path, f"must be one of {sorted(allowed)}")
            return False
        return True

    def array(self, value: object, path: str) -> Sequence[Any] | None:
        """Validate an array value."""
        if not isinstance(value, list):
            self.error(path, "must be an array")
            return None
        return value

    def string_array(self, value: object, path: str) -> list[str] | None:
        """Validate an array containing only strings."""
        items = self.array(value, path)
        if items is None:
            return None
        for index, item in enumerate(items):
            self.string(item, f"{path}[{index}]")
        return (
            list(items)
            if all(isinstance(item, str) for item in items)
            else None
        )


@dataclass(frozen=True, slots=True)
class _ExecutionSetValues:
    """Normalized execution-set routing arrays for invariant checks."""

    publish_intent_nodes: set[str]
    active_variant_ids: list[str] | None
    active_nodes: set[str]
    skip_nodes: set[str]
    selected_gh_nodes: set[str]
    active_gh_nodes: set[str]
    selected_selector_nodes: list[str]


def validate_contract(
    document: JsonObject,
    *,
    metadata_input: JsonObject | None = None,
) -> None:
    """Validate *document* using its top-level `kind` discriminator."""
    validator = _Validator()
    if not isinstance(document, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")]
        )
    kind = document.get("kind")
    dispatch: dict[str, Callable[[_Validator, JsonObject], None]] = {
        "planner-request": _planner_request,
        "planner-diagnostics": _planner_diagnostics,
        "release-plan": _release_plan,
        "dotnet-planner-metadata-input": _dotnet_metadata_input,
        "dotnet-planner-metadata": lambda v, d: _dotnet_metadata(
            v,
            d,
            metadata_input,
        ),
        "execution-sets": _execution_sets,
        "entry-publish-handoff": _entry_handoff,
        "build-request": _build_request,
        "build-result": _build_result,
        "build-diagnostics": _build_diagnostics,
        "tag-result": _tag_result,
        "publish-request": _publish_request,
        "publish-result": _publish_result,
        "skip-result": _skip_result,
        "immutable-proof": _immutable_proof,
        "github-release-asset-proof": _github_release_asset_proof,
        "release-report": _release_report,
    }
    if not isinstance(kind, str) or kind not in dispatch:
        validator.error("$.kind", "is not a registered contract kind")
    else:
        dispatch[kind](validator, document)
    if validator.issues:
        raise ContractValidationError(validator.issues)


def _header(  # noqa: PLR0913
    validator: _Validator,
    document: JsonObject,
    path: str,
    *,
    api_version: str,
    kind: str,
    required: set[str],
    optional: set[str] | None = None,
    allow_extra: bool = False,
) -> JsonObject | None:
    """Validate top-level fields common to every contract."""
    obj = validator.mapping(
        document,
        path,
        required | {"api-version", "kind"},
        optional,
        allow_extra=allow_extra,
    )
    if obj is None:
        return None
    if obj.get("api-version") != api_version:
        validator.error(f"{path}.api-version", f"must be {api_version}")
    if obj.get("kind") != kind:
        validator.error(f"{path}.kind", f"must be {kind}")
    return obj


def _request_flags(validator: _Validator, value: object, path: str) -> None:
    """Validate current-scope request flags."""
    obj = validator.mapping(value, path, {"force"})
    if obj is not None:
        validator.boolean(obj.get("force"), f"{path}.force")


def _planner_request(validator: _Validator, document: JsonObject) -> None:
    """Validate `planner-request.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.planner-request/v1alpha1",
        kind="planner-request",
        required={
            "profile",
            "commit-sha",
            "requested-project-ids",
            "request-flags",
        },
    )
    if obj is None:
        return
    validator.enum(obj.get("profile"), "$.profile", _PROFILES)
    validator.string(obj.get("commit-sha"), "$.commit-sha")
    _unique_sorted_strings(
        validator, obj.get("requested-project-ids"), "$.requested-project-ids"
    )
    _request_flags(validator, obj.get("request-flags"), "$.request-flags")
    flags = obj.get("request-flags")
    if (
        isinstance(flags, Mapping)
        and obj.get("profile") == "official"
        and flags.get("force") is True
    ):
        validator.error(
            "$.request-flags.force", "is invalid for official profile"
        )


def _planner_diagnostics(validator: _Validator, document: JsonObject) -> None:
    """Validate `planner-diagnostics.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.planner-diagnostics/v1alpha1",
        kind="planner-diagnostics",
        required={"diagnostics"},
    )
    if obj is None:
        return
    diagnostics = validator.array(obj.get("diagnostics"), "$.diagnostics")
    if diagnostics is None:
        return
    if len(diagnostics) == 0:
        validator.error("$.diagnostics", "must be non-empty")
    for index, diagnostic in enumerate(diagnostics):
        _planner_diagnostic(validator, diagnostic, f"$.diagnostics[{index}]")


def _planner_diagnostic(
    validator: _Validator, value: object, path: str
) -> None:
    """Validate one planner diagnostic."""
    required = {
        "api-version",
        "kind",
        "code",
        "message",
        "phase",
        "scope-kind",
        "blocking",
        "details",
    }
    optional = {
        "project-id",
        "publish-node-id",
        "target-instance-snapshot-id",
        "resolved-publish-identity",
    }
    obj = validator.mapping(value, path, required, optional)
    if obj is None:
        return
    if obj.get("api-version") != "three.release.planner-diagnostic/v1alpha1":
        validator.error(
            f"{path}.api-version",
            "must be three.release.planner-diagnostic/v1alpha1",
        )
    if obj.get("kind") != "planner-diagnostic":
        validator.error(f"{path}.kind", "must be planner-diagnostic")
    validator.enum(obj.get("code"), f"{path}.code", _REGISTERED_CODES)
    validator.string(obj.get("message"), f"{path}.message")
    validator.enum(obj.get("phase"), f"{path}.phase", _PHASES)
    validator.enum(obj.get("scope-kind"), f"{path}.scope-kind", _SCOPE_KINDS)
    validator.boolean(obj.get("blocking"), f"{path}.blocking")
    if not isinstance(obj.get("details"), Mapping):
        validator.error(f"{path}.details", "must be an object")
    scope = obj.get("scope-kind")
    if scope in {"project", "publish-node"} and "project-id" not in obj:
        validator.error(f"{path}.project-id", "is required for this scope-kind")
    if scope == "publish-node" and "publish-node-id" not in obj:
        validator.error(
            f"{path}.publish-node-id", "is required for publish-node scope"
        )
    if scope == "request":
        for key in ("project-id", "publish-node-id"):
            if key in obj:
                validator.error(
                    f"{path}.{key}", "must be omitted for request scope"
                )


def _dotnet_metadata_input(validator: _Validator, document: JsonObject) -> None:
    """Validate `dotnet-planner-metadata-input.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.dotnet-planner-metadata-input/v1alpha1",
        kind="dotnet-planner-metadata-input",
        required={"commit-sha", "projects"},
    )
    if obj is None:
        return
    validator.string(obj.get("commit-sha"), "$.commit-sha")
    projects = _map(validator, obj.get("projects"), "$.projects")
    if projects is None:
        return
    for project_id, project in projects.items():
        path = f"$.projects.{project_id}"
        entry = validator.mapping(
            project,
            path,
            {"descriptor-path", "primary-manifest-path", "requires-package-id"},
        )
        if entry is None:
            continue
        validator.string(
            entry.get("descriptor-path"), f"{path}.descriptor-path"
        )
        validator.string(
            entry.get("primary-manifest-path"), f"{path}.primary-manifest-path"
        )
        validator.boolean(
            entry.get("requires-package-id"), f"{path}.requires-package-id"
        )


def _dotnet_metadata(
    validator: _Validator,
    document: JsonObject,
    metadata_input: JsonObject | None,
) -> None:
    """Validate `dotnet-planner-metadata.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.dotnet-planner-metadata/v1alpha1",
        kind="dotnet-planner-metadata",
        required={"commit-sha", "projects"},
    )
    if obj is None:
        return
    validator.string(obj.get("commit-sha"), "$.commit-sha")
    input_projects = _dotnet_metadata_input_projects(validator, metadata_input)
    if isinstance(metadata_input, Mapping):
        input_sha = metadata_input.get("commit-sha")
        if obj.get("commit-sha") != input_sha:
            validator.error(
                "$.commit-sha",
                "must match dotnet-planner-metadata-input commit-sha",
            )
    requirements = _dotnet_package_requirements(metadata_input)
    projects = _map(validator, obj.get("projects"), "$.projects")
    if projects is None:
        return
    if input_projects is not None:
        _require_exact_keys(
            validator, projects, set(input_projects), "$.projects"
        )
    for project_id, project in projects.items():
        _dotnet_metadata_project(
            validator,
            project,
            project_id,
            input_projects,
            requirements,
        )


def _dotnet_metadata_project(
    validator: _Validator,
    project: object,
    project_id: str,
    input_projects: JsonObject | None,
    requirements: Mapping[str, bool],
) -> None:
    """Validate one .NET metadata output project against input context."""
    path = f"$.projects.{project_id}"
    entry = validator.mapping(
        project,
        path,
        {"descriptor-path", "primary-manifest-path", "resolved-version"},
        {"package-id"},
    )
    if entry is None:
        return
    for field in (
        "descriptor-path",
        "primary-manifest-path",
        "resolved-version",
    ):
        validator.string(entry.get(field), f"{path}.{field}")
    input_entry = (
        input_projects.get(project_id) if input_projects is not None else None
    )
    if isinstance(input_entry, Mapping):
        _dotnet_metadata_paths_match(validator, entry, input_entry, path)
    requires = requirements.get(project_id)
    if requires is True and "package-id" not in entry:
        validator.error(
            f"{path}.package-id",
            "is required when requires-package-id is true",
        )
    if requires is False and "package-id" in entry:
        validator.error(
            f"{path}.package-id",
            "must be omitted when requires-package-id is false",
        )
    if "package-id" in entry:
        validator.string(entry.get("package-id"), f"{path}.package-id")


def _dotnet_metadata_paths_match(
    validator: _Validator,
    entry: JsonObject,
    input_entry: Mapping[str, object],
    path: str,
) -> None:
    """Validate copied .NET metadata paths match the input manifest."""
    for field in ("descriptor-path", "primary-manifest-path"):
        if entry.get(field) != input_entry.get(field):
            validator.error(
                f"{path}.{field}",
                "must match dotnet-planner-metadata-input",
            )


def _dotnet_metadata_input_projects(
    validator: _Validator,
    metadata_input: JsonObject | None,
) -> JsonObject | None:
    """Return validated project input map for .NET metadata output checks."""
    if metadata_input is None:
        validator.error(
            "$",
            "metadata_input is required to validate dotnet-planner-metadata",
        )
        return None
    input_validator = _Validator()
    _dotnet_metadata_input(input_validator, metadata_input)
    for issue in input_validator.issues:
        validator.error(
            f"metadata_input{issue.path.removeprefix('$')}",
            issue.message,
        )
    projects = metadata_input.get("projects")
    return projects if isinstance(projects, Mapping) else None


def _dotnet_package_requirements(
    metadata_input: JsonObject | None,
) -> dict[str, bool]:
    """Return project package-id requirements from metadata input."""
    if not isinstance(metadata_input, Mapping):
        return {}
    projects = metadata_input.get("projects")
    if not isinstance(projects, Mapping):
        return {}
    return {
        str(key): bool(value.get("requires-package-id"))
        for key, value in projects.items()
        if isinstance(value, Mapping)
        and isinstance(value.get("requires-package-id"), bool)
    }


def _release_plan(validator: _Validator, document: JsonObject) -> None:
    """Validate `release-plan.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.plan/v1alpha1",
        kind="release-plan",
        required={"envelope", "graph"},
    )
    if obj is None:
        return
    _plan_envelope(validator, obj.get("envelope"), "$.envelope")
    _plan_graph(validator, obj.get("graph"), "$.graph")


def _plan_envelope(validator: _Validator, value: object, path: str) -> None:
    """Validate the plan envelope."""
    obj = validator.mapping(
        value,
        path,
        {
            "plan-id",
            "profile",
            "commit-sha",
            "request-flags",
            "requested-project-ids",
            "selected-project-ids",
            "authoring-inputs",
            "projects",
        },
    )
    if obj is None:
        return
    validator.string(obj.get("plan-id"), f"{path}.plan-id")
    validator.enum(obj.get("profile"), f"{path}.profile", _PROFILES)
    validator.string(obj.get("commit-sha"), f"{path}.commit-sha")
    _request_flags(validator, obj.get("request-flags"), f"{path}.request-flags")
    _unique_sorted_strings(
        validator,
        obj.get("requested-project-ids"),
        f"{path}.requested-project-ids",
    )
    _unique_sorted_strings(
        validator,
        obj.get("selected-project-ids"),
        f"{path}.selected-project-ids",
    )
    authoring = validator.mapping(
        obj.get("authoring-inputs"),
        f"{path}.authoring-inputs",
        {"descriptor-api-version", "catalog-path"},
    )
    if authoring is not None:
        if authoring.get("descriptor-api-version") != "three.release/v1alpha1":
            validator.error(
                f"{path}.authoring-inputs.descriptor-api-version",
                "must be three.release/v1alpha1",
            )
        validator.string(
            authoring.get("catalog-path"),
            f"{path}.authoring-inputs.catalog-path",
        )
    projects = _map(validator, obj.get("projects"), f"{path}.projects")
    if projects is not None:
        for project_id, project in projects.items():
            _project_snapshot(
                validator, project, f"{path}.projects.{project_id}"
            )


def _project_snapshot(validator: _Validator, value: object, path: str) -> None:
    """Validate a planner-frozen project snapshot."""
    obj = validator.mapping(
        value,
        path,
        {
            "display-name",
            "ecosystem",
            "release-kind",
            "descriptor-path",
            "release-root",
            "source",
            "resolved-version",
            "variant-ids",
            "publish-node-ids",
        },
    )
    if obj is None:
        return
    for field in (
        "display-name",
        "ecosystem",
        "release-kind",
        "descriptor-path",
        "release-root",
        "resolved-version",
    ):
        validator.string(obj.get(field), f"{path}.{field}")
    source = validator.mapping(
        obj.get("source"),
        f"{path}.source",
        {
            "primary-manifest-path",
            "auxiliary-input-paths",
            "version-authority-kind",
        },
    )
    if source is not None:
        validator.string(
            source.get("primary-manifest-path"),
            f"{path}.source.primary-manifest-path",
        )
        _unique_sorted_strings(
            validator,
            source.get("auxiliary-input-paths"),
            f"{path}.source.auxiliary-input-paths",
        )
        validator.string(
            source.get("version-authority-kind"),
            f"{path}.source.version-authority-kind",
        )
    validator.string_array(obj.get("variant-ids"), f"{path}.variant-ids")
    validator.string_array(
        obj.get("publish-node-ids"), f"{path}.publish-node-ids"
    )


def _plan_graph(  # noqa: C901
    validator: _Validator,
    value: object,
    path: str,
) -> None:
    """Validate the normalized graph."""
    obj = validator.mapping(
        value,
        path,
        {"variants", "artifacts", "publish-nodes", "target-instance-snapshots"},
    )
    if obj is None:
        return
    variants = _map(validator, obj.get("variants"), f"{path}.variants")
    if variants is not None:
        for variant_id, variant in variants.items():
            _variant(validator, variant, f"{path}.variants.{variant_id}")
    artifacts = _map(validator, obj.get("artifacts"), f"{path}.artifacts")
    if artifacts is not None:
        for artifact_id, artifact in artifacts.items():
            _artifact(validator, artifact, f"{path}.artifacts.{artifact_id}")
    snapshots = _map(
        validator,
        obj.get("target-instance-snapshots"),
        f"{path}.target-instance-snapshots",
    )
    families: dict[str, str] = {}
    if snapshots is not None:
        for snapshot_id, snapshot in snapshots.items():
            family = _target_snapshot(
                validator,
                snapshot,
                f"{path}.target-instance-snapshots.{snapshot_id}",
                snapshot_id=snapshot_id,
            )
            if family is not None:
                families[snapshot_id] = family
    nodes = _map(validator, obj.get("publish-nodes"), f"{path}.publish-nodes")
    if nodes is not None:
        for node_id, node in nodes.items():
            _publish_node(
                validator,
                node,
                f"{path}.publish-nodes.{node_id}",
                families,
                expected_node_id=node_id,
            )


def _variant(validator: _Validator, value: object, path: str) -> None:
    """Validate one plan variant."""
    obj = validator.mapping(
        value,
        path,
        {"project-id", "descriptor-handle", "dimensions", "artifact-ids"},
    )
    if obj is None:
        return
    validator.string(obj.get("project-id"), f"{path}.project-id")
    validator.string(obj.get("descriptor-handle"), f"{path}.descriptor-handle")
    _string_map(validator, obj.get("dimensions"), f"{path}.dimensions")
    validator.string_array(obj.get("artifact-ids"), f"{path}.artifact-ids")


def _artifact(validator: _Validator, value: object, path: str) -> None:
    """Validate one plan artifact."""
    obj = validator.mapping(
        value,
        path,
        {
            "project-id",
            "variant-id",
            "descriptor-handle",
            "role",
            "kind-family",
            "concrete-kind",
            "produced-from-artifact-ids",
        },
    )
    if obj is None:
        return
    for field in (
        "project-id",
        "variant-id",
        "descriptor-handle",
        "role",
        "kind-family",
        "concrete-kind",
    ):
        validator.string(obj.get(field), f"{path}.{field}")
    validator.string_array(
        obj.get("produced-from-artifact-ids"),
        f"{path}.produced-from-artifact-ids",
    )


def _target_snapshot(
    validator: _Validator,
    value: object,
    path: str,
    *,
    snapshot_id: str | None = None,
) -> str | None:
    """Validate a target-instance snapshot and return its family."""
    obj = validator.mapping(
        value,
        path,
        {
            "family",
            "instance-id",
            "catalog-ref",
            "contract",
            "destination",
            "capabilities",
        },
    )
    if obj is None:
        return None
    for field in ("family", "instance-id", "catalog-ref"):
        validator.string(obj.get(field), f"{path}.{field}")
    raw_family = obj.get("family")
    raw_instance_id = obj.get("instance-id")
    family: str = raw_family if isinstance(raw_family, str) else ""
    instance_id: str = (
        raw_instance_id if isinstance(raw_instance_id, str) else ""
    )
    catalog_ref = f"{family}/{instance_id}"
    if obj.get("catalog-ref") != catalog_ref:
        validator.error(f"{path}.catalog-ref", "must equal family/instance-id")
    if snapshot_id is not None and snapshot_id != catalog_ref:
        validator.error(path, "snapshot key must equal family/instance-id")
    _contract(validator, obj.get("contract"), f"{path}.contract", family=family)
    destination = _target_destination(
        validator,
        obj.get("destination"),
        f"{path}.destination",
        family,
    )
    capabilities = validator.mapping(
        obj.get("capabilities"),
        f"{path}.capabilities",
        {
            "mutability",
            "name-uniqueness-scope",
            "version-uniqueness-rule",
            "profile-coexistence-rule",
            "credential-posture",
            "publish-topology",
        },
    )
    if capabilities is not None:
        _target_capabilities(
            validator,
            capabilities,
            f"{path}.capabilities",
            family,
            destination,
        )
    return family or None


def _target_destination(
    validator: _Validator,
    value: object,
    path: str,
    family: str,
) -> JsonObject | None:
    """Validate a family-specific target destination object."""
    if family == "github-release":
        obj = validator.mapping(value, path, {"host", "owner", "repo"})
        if obj is not None:
            if obj.get("host") != "github":
                validator.error(f"{path}.host", "must be github")
            _github_slug(validator, obj.get("owner"), f"{path}.owner")
            _github_slug(validator, obj.get("repo"), f"{path}.repo")
            if obj.get("owner") != "hcoona" or obj.get("repo") != "three":
                validator.error(path, "must target hcoona/three")
        return obj
    if family == "nuget":
        return _owner_by_host_destination(
            validator,
            value,
            path,
            public_host="nuget.org",
            github_host="nuget.pkg.github.com",
        )
    if family == "pypi":
        obj = validator.mapping(value, path, {"host"})
        if obj is not None and obj.get("host") != "pypi.org":
            validator.error(f"{path}.host", "must be pypi.org")
        return obj
    if family == "npm":
        return _owner_by_host_destination(
            validator,
            value,
            path,
            public_host="registry.npmjs.org",
            github_host="npm.pkg.github.com",
        )
    if family == "rubygems":
        return _owner_by_host_destination(
            validator,
            value,
            path,
            public_host="rubygems.org",
            github_host="rubygems.pkg.github.com",
        )
    validator.error(path, f"unsupported target family: {family}")
    validator.mapping(value, path, set(), allow_extra=True)
    return None


def _owner_by_host_destination(
    validator: _Validator,
    value: object,
    path: str,
    *,
    public_host: str,
    github_host: str,
) -> JsonObject | None:
    """Validate registry destinations with host-dependent owner fields."""
    obj = validator.mapping(value, path, {"host"}, {"owner"})
    if obj is None:
        return None
    host = obj.get("host")
    if host not in {public_host, github_host}:
        validator.error(
            f"{path}.host", f"must be {public_host} or {github_host}"
        )
    if host == public_host and "owner" in obj:
        validator.error(f"{path}.owner", f"must be omitted for {public_host}")
    if host == github_host and "owner" not in obj:
        validator.error(f"{path}.owner", f"is required for {github_host}")
    if "owner" in obj:
        validator.string(obj.get("owner"), f"{path}.owner")
    return obj


def _github_slug(validator: _Validator, value: object, path: str) -> None:
    """Validate a non-empty GitHub slug string."""
    if validator.string(value, path) and not _GITHUB_SLUG_RE.match(str(value)):
        validator.error(path, "must be a GitHub slug")


def _target_capabilities(
    validator: _Validator,
    capabilities: JsonObject,
    path: str,
    family: str,
    destination: JsonObject | None,
) -> None:
    """Validate capability vocabularies and required family/host tuple."""
    for field, values in _CAPABILITY_VALUES.items():
        validator.enum(capabilities.get(field), f"{path}.{field}", values)
    expected = _expected_capabilities(family, destination)
    if expected is None:
        return
    for field, value in expected.items():
        if capabilities.get(field) != value:
            validator.error(f"{path}.{field}", f"must be {value}")


def _expected_capabilities(
    family: str,
    destination: JsonObject | None,
) -> dict[str, str] | None:
    """Return the required capability tuple for a family and host."""
    if destination is None:
        return None
    host = destination.get("host")
    if family == "github-release" and host == "github":
        return _capability_tuple(
            "mutable-prerelease",
            "release-tag",
            "tag",
            "not-applicable",
            "github-token",
            "github-token",
        )
    if family in {"nuget", "pypi", "npm", "rubygems"}:
        return _registry_capabilities(family, str(host))
    return None


def _registry_capabilities(family: str, host: str) -> dict[str, str] | None:
    """Return expected capabilities for current-scope registry hosts."""
    public_hosts = {
        "nuget": "nuget.org",
        "pypi": "pypi.org",
        "npm": "registry.npmjs.org",
        "rubygems": "rubygems.org",
    }
    github_hosts = {
        "nuget": "nuget.pkg.github.com",
        "npm": "npm.pkg.github.com",
        "rubygems": "rubygems.pkg.github.com",
    }
    if public_hosts.get(family) == host:
        topology = (
            "external-oidc-reusable-workflow"
            if family == "rubygems"
            else "external-oidc-entry-workflow"
        )
        return _capability_tuple(
            "immutable",
            "package-name",
            "package-name-plus-version",
            "requires-distinct-name",
            "oidc",
            topology,
        )
    if github_hosts.get(family) == host:
        return _capability_tuple(
            "immutable",
            "package-name-with-owner",
            "package-name-plus-version",
            "requires-distinct-name",
            "github-token",
            "github-token",
        )
    return None


def _capability_tuple(  # noqa: PLR0913
    mutability: str,
    name_scope: str,
    version_rule: str,
    coexistence_rule: str,
    credential_posture: str,
    topology: str,
) -> dict[str, str]:
    """Create a capability tuple keyed by serialized contract fields."""
    return {
        "mutability": mutability,
        "name-uniqueness-scope": name_scope,
        "version-uniqueness-rule": version_rule,
        "profile-coexistence-rule": coexistence_rule,
        "credential-posture": credential_posture,
        "publish-topology": topology,
    }


def _contract(  # noqa: C901
    validator: _Validator,
    value: object,
    path: str,
    *,
    family: str = "",
) -> None:
    """Validate a frozen destination contract object."""
    obj = validator.mapping(
        value,
        path,
        {"id", "allowed-artifact-tuples", "aggregate-rules"},
    )
    if obj is None:
        return
    validator.string(obj.get("id"), f"{path}.id")
    expected_id = _CONTRACT_IDS_BY_FAMILY.get(family)
    if expected_id is None:
        validator.error(f"{path}.id", f"unsupported target family: {family}")
    elif obj.get("id") != expected_id:
        validator.error(f"{path}.id", f"must be {expected_id}")
    tuples = validator.array(
        obj.get("allowed-artifact-tuples"), f"{path}.allowed-artifact-tuples"
    )
    if tuples is not None:
        for index, item in enumerate(tuples):
            _artifact_tuple(
                validator, item, f"{path}.allowed-artifact-tuples[{index}]"
            )
    rules = validator.mapping(
        obj.get("aggregate-rules"),
        f"{path}.aggregate-rules",
        {
            "min-artifact-count",
            "max-artifact-count",
            "cross-variant-policy",
            "tuple-rules",
        },
    )
    if rules is not None:
        validator.integer(
            rules.get("min-artifact-count"),
            f"{path}.aggregate-rules.min-artifact-count",
            minimum=0,
        )
        _nullable_nonnegative_int(
            validator,
            rules.get("max-artifact-count"),
            f"{path}.aggregate-rules.max-artifact-count",
        )
        validator.enum(
            rules.get("cross-variant-policy"),
            f"{path}.aggregate-rules.cross-variant-policy",
            {"allow", "forbid"},
        )
        tuple_rules = validator.array(
            rules.get("tuple-rules"), f"{path}.aggregate-rules.tuple-rules"
        )
        if tuple_rules is not None:
            for index, rule in enumerate(tuple_rules):
                rule_obj = validator.mapping(
                    rule,
                    f"{path}.aggregate-rules.tuple-rules[{index}]",
                    {
                        "role",
                        "kind-family",
                        "concrete-kind",
                        "min-count",
                        "max-count",
                    },
                )
                if rule_obj is not None:
                    _artifact_tuple_fields(
                        validator,
                        rule_obj,
                        f"{path}.aggregate-rules.tuple-rules[{index}]",
                    )
                    validator.integer(
                        rule_obj.get("min-count"),
                        f"{path}.aggregate-rules.tuple-rules[{index}].min-count",
                        minimum=0,
                    )
                    _nullable_nonnegative_int(
                        validator,
                        rule_obj.get("max-count"),
                        f"{path}.aggregate-rules.tuple-rules[{index}].max-count",
                    )
    if isinstance(obj.get("id"), str):
        _contract_matches_expected(validator, obj, path, str(obj.get("id")))


def _contract_matches_expected(
    validator: _Validator,
    obj: JsonObject,
    path: str,
    contract_id: str,
) -> None:
    """Validate a frozen contract against the closed compatibility table."""
    expected = _EXPECTED_CONTRACTS.get(contract_id)
    if expected is None:
        return
    for field in ("allowed-artifact-tuples", "aggregate-rules"):
        if obj.get(field) != expected[field]:
            validator.error(
                f"{path}.{field}",
                "must match the closed contract compatibility table",
            )


def _artifact_tuple(validator: _Validator, value: object, path: str) -> None:
    """Validate a role/family/kind tuple."""
    obj = validator.mapping(
        value, path, {"role", "kind-family", "concrete-kind"}
    )
    if obj is not None:
        _artifact_tuple_fields(validator, obj, path)


def _artifact_tuple_fields(
    validator: _Validator, obj: JsonObject, path: str
) -> None:
    """Validate fields common to contract tuple records."""
    for field in ("role", "kind-family", "concrete-kind"):
        validator.string(obj.get(field), f"{path}.{field}")


def _publish_node(
    validator: _Validator,
    value: object,
    path: str,
    target_families: Mapping[str, str],
    expected_node_id: str | None = None,
) -> None:
    """Validate a normalized publish node."""
    obj = validator.mapping(
        value,
        path,
        {
            "publish-node-id",
            "project-id",
            "profile",
            "descriptor-target-index",
            "target-instance-snapshot-id",
            "artifact-ids",
            "publish-disposition",
            "resolved-publish-identity",
            "projection",
        },
        {"publish-mode", "desired-publish-state", "attestation"},
    )
    if obj is None:
        return
    validator.string(obj.get("publish-node-id"), f"{path}.publish-node-id")
    if (
        expected_node_id is not None
        and isinstance(obj.get("publish-node-id"), str)
        and obj["publish-node-id"] != expected_node_id
    ):
        validator.error(
            f"{path}.publish-node-id",
            "must match the containing publish node id",
        )
    validator.string(obj.get("project-id"), f"{path}.project-id")
    validator.enum(obj.get("profile"), f"{path}.profile", _PROFILES)
    validator.integer(
        obj.get("descriptor-target-index"),
        f"{path}.descriptor-target-index",
        minimum=0,
    )
    snapshot_id = obj.get("target-instance-snapshot-id")
    validator.string(snapshot_id, f"{path}.target-instance-snapshot-id")
    artifact_ids = (
        validator.string_array(obj.get("artifact-ids"), f"{path}.artifact-ids")
        or []
    )
    disposition = obj.get("publish-disposition")
    validator.enum(
        disposition,
        f"{path}.publish-disposition",
        {"publish", "skip-satisfied"},
    )
    if disposition == "publish":
        if "publish-mode" not in obj:
            validator.error(
                f"{path}.publish-mode",
                "is required when publish-disposition is publish",
            )
        else:
            validator.enum(
                obj.get("publish-mode"),
                f"{path}.publish-mode",
                {"create-only", "overwrite-mutable", "replace-authoritative"},
            )
    elif "publish-mode" in obj:
        validator.error(
            f"{path}.publish-mode",
            "must be omitted when publish-disposition is skip-satisfied",
        )
    family = target_families.get(str(snapshot_id), "")
    _resolved_identity(
        validator,
        obj.get("resolved-publish-identity"),
        f"{path}.resolved-publish-identity",
        family,
    )
    _desired_state(validator, obj, path, family)
    _projection(
        validator,
        obj.get("projection"),
        f"{path}.projection",
        family,
        artifact_ids,
    )
    _attestation(validator, obj, path, family)


def _resolved_identity(
    validator: _Validator, value: object, path: str, family: str
) -> None:
    """Validate a family-specific resolved publish identity."""
    if family == "github-release":
        obj = validator.mapping(value, path, {"release-tag"})
        if obj is not None:
            validator.string(obj.get("release-tag"), f"{path}.release-tag")
        return
    obj = validator.mapping(value, path, {"package-name", "version"})
    if obj is not None:
        validator.string(obj.get("package-name"), f"{path}.package-name")
        validator.string(obj.get("version"), f"{path}.version")


def _desired_state(
    validator: _Validator, obj: JsonObject, path: str, family: str
) -> None:
    """Validate conditional desired publish state."""
    if family == "github-release":
        state = validator.mapping(
            obj.get("desired-publish-state"),
            f"{path}.desired-publish-state",
            {"release-state"},
        )
        if state is not None:
            validator.enum(
                state.get("release-state"),
                f"{path}.desired-publish-state.release-state",
                {"prerelease", "release"},
            )
    elif "desired-publish-state" in obj:
        validator.error(
            f"{path}.desired-publish-state",
            "must be omitted for package registries",
        )


def _projection(
    validator: _Validator,
    value: object,
    path: str,
    family: str,
    artifact_ids: Sequence[str],
) -> None:
    """Validate a family-specific projection object."""
    if family == "github-release":
        obj = validator.mapping(
            value,
            path,
            {"asset-names-by-artifact-id", "asset-labels-by-artifact-id"},
        )
        if obj is None:
            return
        names = _string_map(
            validator,
            obj.get("asset-names-by-artifact-id"),
            f"{path}.asset-names-by-artifact-id",
        )
        _string_map(
            validator,
            obj.get("asset-labels-by-artifact-id"),
            f"{path}.asset-labels-by-artifact-id",
        )
        _require_exact_keys(
            validator,
            names,
            set(artifact_ids),
            f"{path}.asset-names-by-artifact-id",
        )
        if names is not None and len(set(names.values())) != len(names):
            validator.error(
                f"{path}.asset-names-by-artifact-id",
                "asset names must be unique",
            )
        return
    if family in {"nuget", "pypi", "rubygems"}:
        obj = validator.mapping(
            value, path, {"final-distribution-filenames-by-artifact-id"}
        )
        if obj is not None:
            filenames = _string_map(
                validator,
                obj.get("final-distribution-filenames-by-artifact-id"),
                f"{path}.final-distribution-filenames-by-artifact-id",
            )
            _require_exact_keys(
                validator,
                filenames,
                set(artifact_ids),
                f"{path}.final-distribution-filenames-by-artifact-id",
            )
        return
    if family == "npm":
        obj = validator.mapping(
            value,
            path,
            {"final-distribution-filenames-by-artifact-id"},
            {"package-name"},
        )
        if obj is not None:
            filenames = _string_map(
                validator,
                obj.get("final-distribution-filenames-by-artifact-id"),
                f"{path}.final-distribution-filenames-by-artifact-id",
            )
            _require_exact_keys(
                validator,
                filenames,
                set(artifact_ids),
                f"{path}.final-distribution-filenames-by-artifact-id",
            )
            if "package-name" in obj:
                validator.string(
                    obj.get("package-name"), f"{path}.package-name"
                )
        return
    validator.mapping(value, path, set())


def _attestation(
    validator: _Validator, obj: JsonObject, path: str, family: str
) -> None:
    """Validate conditional GitHub Release attestation fields."""
    if family == "github-release":
        attestation = validator.mapping(
            obj.get("attestation"), f"{path}.attestation", {"signer-workflow"}
        )
        if attestation is None:
            return
        value = attestation.get("signer-workflow")
        if validator.string(
            value, f"{path}.attestation.signer-workflow"
        ) and not _SIGNER_WORKFLOW_RE.match(str(value)):
            validator.error(
                f"{path}.attestation.signer-workflow",
                "must be a full signer workflow identity",
            )
    elif "attestation" in obj:
        validator.error(
            f"{path}.attestation", "must be omitted outside github-release"
        )


def _execution_sets(validator: _Validator, document: JsonObject) -> None:
    """Validate `execution-sets.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.execution-sets/v1alpha1",
        kind="execution-sets",
        required={
            "plan-id",
            "dry-run",
            "validation-build",
            "publish-intent-node-ids",
            "active-variant-ids",
            "active-publish-node-ids",
            "active-publish-selectors",
            "skip-satisfied-publish-node-ids",
            "selected-github-release-publish-node-ids",
            "active-github-release-publish-node-ids",
        },
    )
    if obj is None:
        return
    validator.string(obj.get("plan-id"), "$.plan-id")
    validator.boolean(obj.get("dry-run"), "$.dry-run")
    validator.boolean(obj.get("validation-build"), "$.validation-build")
    values = _execution_set_values(validator, obj)
    _execution_set_invariants(validator, obj, values)


def _execution_set_values(
    validator: _Validator,
    obj: JsonObject,
) -> _ExecutionSetValues:
    """Validate execution-set arrays and return normalized values."""
    publish_intent_node_ids = _unique_string_array(
        validator,
        obj.get("publish-intent-node-ids"),
        "$.publish-intent-node-ids",
    )
    active_variant_ids = _unique_string_array(
        validator, obj.get("active-variant-ids"), "$.active-variant-ids"
    )
    active_publish_node_ids = _unique_string_array(
        validator,
        obj.get("active-publish-node-ids"),
        "$.active-publish-node-ids",
    )
    skip_satisfied_node_ids = _unique_string_array(
        validator,
        obj.get("skip-satisfied-publish-node-ids"),
        "$.skip-satisfied-publish-node-ids",
    )
    selected_gh_node_ids = _unique_string_array(
        validator,
        obj.get("selected-github-release-publish-node-ids"),
        "$.selected-github-release-publish-node-ids",
    )
    active_gh_node_ids = _unique_string_array(
        validator,
        obj.get("active-github-release-publish-node-ids"),
        "$.active-github-release-publish-node-ids",
    )
    selected = _execution_set_selector_nodes(validator, obj)
    return _ExecutionSetValues(
        publish_intent_nodes=set(publish_intent_node_ids or []),
        active_variant_ids=active_variant_ids,
        active_nodes=set(active_publish_node_ids or []),
        skip_nodes=set(skip_satisfied_node_ids or []),
        selected_gh_nodes=set(selected_gh_node_ids or []),
        active_gh_nodes=set(active_gh_node_ids or []),
        selected_selector_nodes=selected,
    )


def _execution_set_selector_nodes(
    validator: _Validator,
    obj: JsonObject,
) -> list[str]:
    """Validate active publish selector partitions and return all members."""
    selectors = validator.mapping(
        obj.get("active-publish-selectors"),
        "$.active-publish-selectors",
        _TOPOLOGIES,
    )
    selected: list[str] = []
    if selectors is None:
        return selected
    for topology, node_ids in selectors.items():
        ids = _unique_string_array(
            validator,
            node_ids,
            f"$.active-publish-selectors.{topology}",
        )
        selected.extend(ids or [])
    return selected


def _execution_set_invariants(
    validator: _Validator,
    obj: JsonObject,
    values: _ExecutionSetValues,
) -> None:
    """Validate cross-field execution-set routing invariants."""
    selected_set = set(values.selected_selector_nodes)
    if len(values.selected_selector_nodes) != len(selected_set):
        validator.error(
            "$.active-publish-selectors",
            "each active publish node must appear in exactly one selector",
        )
    if selected_set != values.active_nodes:
        validator.error(
            "$.active-publish-selectors",
            "selector union must equal active-publish-node-ids",
        )
    if values.publish_intent_nodes & values.skip_nodes:
        validator.error(
            "$.skip-satisfied-publish-node-ids",
            "must not overlap publish-intent-node-ids",
        )
    if obj.get("validation-build") is True and obj.get("dry-run") is False:
        validator.error(
            "$.validation-build",
            "must be false when dry-run is false",
        )
    if obj.get("dry-run") is True and values.active_nodes:
        validator.error(
            "$.active-publish-node-ids",
            "must be empty when dry-run is true",
        )
    if (
        obj.get("dry-run") is True
        and obj.get("validation-build") is False
        and values.active_variant_ids
    ):
        validator.error(
            "$.active-variant-ids",
            "must be empty for ordinary dry-runs",
        )
    if not values.publish_intent_nodes and values.active_variant_ids:
        validator.error(
            "$.active-variant-ids",
            "must be empty when publish-intent-node-ids is empty",
        )
    if (
        obj.get("dry-run") is False
        and values.active_nodes != values.publish_intent_nodes
    ):
        validator.error(
            "$.active-publish-node-ids",
            "must equal publish-intent-node-ids when dry-run is false",
        )
    _execution_set_github_release_invariants(validator, values)


def _execution_set_github_release_invariants(
    validator: _Validator,
    values: _ExecutionSetValues,
) -> None:
    """Validate GitHub Release execution selector subsets."""
    if values.active_gh_nodes - values.active_nodes:
        validator.error(
            "$.active-github-release-publish-node-ids",
            "must be a subset of active-publish-node-ids",
        )
    if values.active_gh_nodes - values.selected_gh_nodes:
        validator.error(
            "$.active-github-release-publish-node-ids",
            "must be a subset of selected-github-release-publish-node-ids",
        )
    expected_active_gh_nodes = values.selected_gh_nodes & values.active_nodes
    if values.active_gh_nodes != expected_active_gh_nodes:
        validator.error(
            "$.active-github-release-publish-node-ids",
            "must equal the active selected GitHub Release publish-node subset",
        )
    selected_publish_nodes = values.publish_intent_nodes | values.skip_nodes
    if values.selected_gh_nodes - selected_publish_nodes:
        validator.error(
            "$.selected-github-release-publish-node-ids",
            "must be a subset of selected publish node ids",
        )


def _entry_handoff(validator: _Validator, document: JsonObject) -> None:
    """Validate `entry-publish-handoff.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.entry-publish-handoff/v1alpha1",
        kind="entry-publish-handoff",
        required={
            "plan-id",
            "commit-sha",
            "plan-artifact-name",
            "execution-sets-artifact-name",
            "entry-publish-node-ids",
            "publish-inputs-by-node-id",
        },
    )
    if obj is None:
        return
    for field in (
        "plan-id",
        "commit-sha",
        "plan-artifact-name",
        "execution-sets-artifact-name",
    ):
        validator.string(obj.get(field), f"$.{field}")
    node_ids = (
        validator.string_array(
            obj.get("entry-publish-node-ids"), "$.entry-publish-node-ids"
        )
        or []
    )
    inputs = _map(
        validator,
        obj.get("publish-inputs-by-node-id"),
        "$.publish-inputs-by-node-id",
    )
    if inputs is None:
        return
    _require_exact_keys(
        validator, inputs, set(node_ids), "$.publish-inputs-by-node-id"
    )
    for node_id, payload in inputs.items():
        path = f"$.publish-inputs-by-node-id.{node_id}"
        entry = validator.mapping(
            payload,
            path,
            {
                "target-instance-snapshot-id",
                "build-result-artifact-names",
                "build-bundle-artifact-names",
            },
        )
        if entry is not None:
            validator.string(
                entry.get("target-instance-snapshot-id"),
                f"{path}.target-instance-snapshot-id",
            )
            validator.string_array(
                entry.get("build-result-artifact-names"),
                f"{path}.build-result-artifact-names",
            )
            validator.string_array(
                entry.get("build-bundle-artifact-names"),
                f"{path}.build-bundle-artifact-names",
            )


def _build_request(validator: _Validator, document: JsonObject) -> None:
    """Validate `build-request.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.build-request/v1alpha1",
        kind="build-request",
        required={
            "plan-id",
            "profile",
            "commit-sha",
            "project",
            "variant",
            "artifacts",
        },
    )
    if obj is None:
        return
    _common_plan_fields(validator, obj, "$", publish=False)
    _project_snapshot(validator, obj.get("project"), "$.project")
    _variant(validator, obj.get("variant"), "$.variant")
    artifacts = _map(validator, obj.get("artifacts"), "$.artifacts")
    if artifacts is not None:
        for artifact_id, artifact in artifacts.items():
            _artifact(validator, artifact, f"$.artifacts.{artifact_id}")
    variant = obj.get("variant")
    if isinstance(variant, Mapping):
        _require_exact_keys(
            validator,
            artifacts,
            set(variant.get("artifact-ids", [])),
            "$.artifacts",
        )


def _build_result(validator: _Validator, document: JsonObject) -> None:
    """Validate `build-result.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.build-result/v1alpha1",
        kind="build-result",
        required={"plan-id", "project-id", "variant-id", "artifacts"},
    )
    if obj is None:
        return
    for field in ("plan-id", "project-id", "variant-id"):
        validator.string(obj.get(field), f"$.{field}")
    artifacts = _map(validator, obj.get("artifacts"), "$.artifacts")
    if artifacts is not None:
        for artifact_id, artifact in artifacts.items():
            _artifact_receipt(validator, artifact, f"$.artifacts.{artifact_id}")


def _build_diagnostics(validator: _Validator, document: JsonObject) -> None:
    """Validate `build-diagnostics.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.build-diagnostics/v1alpha1",
        kind="build-diagnostics",
        required={"diagnostics"},
    )
    if obj is None:
        return
    diagnostics = validator.array(obj.get("diagnostics"), "$.diagnostics")
    if diagnostics is None:
        return
    if len(diagnostics) == 0:
        validator.error("$.diagnostics", "must be non-empty")
    for index, diagnostic in enumerate(diagnostics):
        _build_diagnostic(validator, diagnostic, f"$.diagnostics[{index}]")


def _build_diagnostic(
    validator: _Validator,
    value: object,
    path: str,
) -> None:
    """Validate one build diagnostic."""
    required = {
        "api-version",
        "kind",
        "code",
        "message",
        "phase",
        "scope-kind",
        "blocking",
        "details",
    }
    optional = {"plan-id", "project-id", "variant-id", "artifact-id"}
    obj = validator.mapping(value, path, required, optional)
    if obj is None:
        return
    if obj.get("api-version") != "three.release.build-diagnostic/v1alpha1":
        validator.error(
            f"{path}.api-version",
            "must be three.release.build-diagnostic/v1alpha1",
        )
    if obj.get("kind") != "build-diagnostic":
        validator.error(f"{path}.kind", "must be build-diagnostic")
    validator.enum(obj.get("code"), f"{path}.code", _BUILD_DIAGNOSTIC_CODES)
    validator.string(obj.get("message"), f"{path}.message")
    validator.enum(
        obj.get("phase"),
        f"{path}.phase",
        {"validation", "materialization", "execution", "receipt"},
    )
    validator.enum(
        obj.get("scope-kind"),
        f"{path}.scope-kind",
        {"request", "project", "variant", "artifact"},
    )
    validator.boolean(obj.get("blocking"), f"{path}.blocking")
    if not isinstance(obj.get("details"), Mapping):
        validator.error(f"{path}.details", "must be an object")
    if "plan-id" in obj:
        validator.string(obj.get("plan-id"), f"{path}.plan-id")
    _build_diagnostic_scope(validator, obj, path)


def _build_diagnostic_scope(
    validator: _Validator,
    obj: JsonObject,
    path: str,
) -> None:
    """Validate build diagnostic scope identity fields."""
    scope = obj.get("scope-kind")
    for key in ("project-id", "variant-id", "artifact-id"):
        if key in obj:
            validator.string(obj.get(key), f"{path}.{key}")
    if scope in {"project", "variant", "artifact"} and "project-id" not in obj:
        validator.error(f"{path}.project-id", "is required for this scope-kind")
    if scope in {"variant", "artifact"} and "variant-id" not in obj:
        validator.error(f"{path}.variant-id", "is required for this scope-kind")
    if scope == "artifact" and "artifact-id" not in obj:
        validator.error(f"{path}.artifact-id", "is required for artifact scope")
    forbidden_by_scope = {
        "request": ("project-id", "variant-id", "artifact-id"),
        "project": ("variant-id", "artifact-id"),
        "variant": ("artifact-id",),
    }
    for key in forbidden_by_scope.get(str(scope), ()):
        if key in obj:
            validator.error(
                f"{path}.{key}", "must be omitted for this scope-kind"
            )


def _tag_result(validator: _Validator, document: JsonObject) -> None:
    """Validate `tag-result.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.tag-result/v1alpha1",
        kind="tag-result",
        required={"plan-id", "commit-sha", "tags"},
    )
    if obj is None:
        return
    validator.string(obj.get("plan-id"), "$.plan-id")
    validator.string(obj.get("commit-sha"), "$.commit-sha")
    tags = validator.array(obj.get("tags"), "$.tags")
    if tags is not None:
        for index, tag in enumerate(tags):
            path = f"$.tags[{index}]"
            entry = validator.mapping(
                tag,
                path,
                {
                    "release-tag",
                    "outcome",
                    "expected-commit-sha",
                    "peeled-commit-sha",
                },
            )
            if entry is not None:
                validator.string(
                    entry.get("release-tag"), f"{path}.release-tag"
                )
                validator.enum(
                    entry.get("outcome"),
                    f"{path}.outcome",
                    {"verified", "created"},
                )
                validator.string(
                    entry.get("expected-commit-sha"),
                    f"{path}.expected-commit-sha",
                )
                validator.string(
                    entry.get("peeled-commit-sha"), f"{path}.peeled-commit-sha"
                )


def _publish_request(validator: _Validator, document: JsonObject) -> None:
    """Validate `publish-request.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.publish-request/v1alpha1",
        kind="publish-request",
        required={
            "plan-id",
            "profile",
            "commit-sha",
            "publish-node-id",
            "project",
            "publish-node",
            "target-instance-snapshot",
            "artifacts",
        },
        optional={"github-release-asset-attestations"},
    )
    if obj is None:
        return
    _common_plan_fields(validator, obj, "$", publish=True)
    validator.string(obj.get("publish-node-id"), "$.publish-node-id")
    _project_snapshot(validator, obj.get("project"), "$.project")
    project = obj.get("project")
    if isinstance(project, Mapping):
        publish_node_ids = validator.string_array(
            project.get("publish-node-ids"),
            "$.project.publish-node-ids",
        )
        if (
            isinstance(obj.get("publish-node-id"), str)
            and publish_node_ids is not None
            and obj["publish-node-id"] not in publish_node_ids
        ):
            validator.error(
                "$.publish-node-id",
                "must be listed in $.project.publish-node-ids",
            )
    snapshot = obj.get("target-instance-snapshot")
    family = (
        _target_snapshot(validator, snapshot, "$.target-instance-snapshot")
        or ""
    )
    snapshot_id = _snapshot_id(snapshot)
    node = obj.get("publish-node")
    _publish_node(
        validator,
        node,
        "$.publish-node",
        {str(snapshot_id): family},
        expected_node_id=(
            str(obj["publish-node-id"])
            if isinstance(obj.get("publish-node-id"), str)
            else None
        ),
    )
    if (
        isinstance(node, Mapping)
        and isinstance(node.get("target-instance-snapshot-id"), str)
        and node.get("target-instance-snapshot-id") != snapshot_id
    ):
        validator.error(
            "$.publish-node.target-instance-snapshot-id",
            "must match target-instance-snapshot family/instance-id",
        )
    artifacts = _map(validator, obj.get("artifacts"), "$.artifacts")
    if artifacts is not None:
        for artifact_id, payload in artifacts.items():
            path = f"$.artifacts.{artifact_id}"
            entry = validator.mapping(
                payload,
                path,
                {
                    "artifact",
                    "input-path",
                    "bundle-relative-path",
                    "sha256",
                    "byte-size",
                },
            )
            if entry is not None:
                _artifact(validator, entry.get("artifact"), f"{path}.artifact")
                input_path = entry.get("input-path")
                if validator.string(input_path, f"{path}.input-path"):
                    _relative_path(
                        validator, str(input_path), f"{path}.input-path"
                    )
                _artifact_receipt_fields(validator, entry, path)
    node = obj.get("publish-node")
    if isinstance(node, Mapping):
        _require_exact_keys(
            validator,
            artifacts,
            set(node.get("artifact-ids", [])),
            "$.artifacts",
        )
        _github_release_asset_attestations(
            validator,
            obj,
            "$.github-release-asset-attestations",
            family,
            set(node.get("artifact-ids", [])),
        )


def _publish_result(validator: _Validator, document: JsonObject) -> None:
    """Validate `publish-result.json`."""
    obj = _result_common(
        validator,
        document,
        api_version="three.release.publish-result/v1alpha1",
        kind="publish-result",
        required={"outcome"},
    )
    if obj is not None:
        validator.enum(obj.get("outcome"), "$.outcome", {"published"})


def _github_release_asset_attestations(
    validator: _Validator,
    request: JsonObject,
    path: str,
    family: str,
    artifact_ids: set[object],
) -> None:
    """Validate actions/attest outputs carried by a GitHub Release request."""
    value = request.get("github-release-asset-attestations")
    if family != "github-release":
        if value is not None:
            validator.error(path, "must be omitted outside github-release")
        return
    outputs = _map(validator, value, path)
    if outputs is None:
        return
    _require_exact_keys(validator, outputs, artifact_ids, path)
    for artifact_id, payload in outputs.items():
        item_path = f"{path}.{artifact_id}"
        item = validator.mapping(
            payload,
            item_path,
            {"attestation-id", "attestation-url", "bundle-path"},
            {"storage-record-ids"},
        )
        if item is None:
            continue
        for field in ("attestation-id", "attestation-url", "bundle-path"):
            validator.string(item.get(field), f"{item_path}.{field}")
        if "storage-record-ids" in item:
            validator.string(
                item.get("storage-record-ids"),
                f"{item_path}.storage-record-ids",
            )


def _skip_result(validator: _Validator, document: JsonObject) -> None:
    """Validate `skip-result.json`."""
    obj = _result_common(
        validator,
        document,
        api_version="three.release.skip-result/v1alpha1",
        kind="skip-result",
        required={"outcome", "reason-source"},
    )
    if obj is not None:
        validator.enum(obj.get("outcome"), "$.outcome", {"skip-satisfied"})
        validator.enum(obj.get("reason-source"), "$.reason-source", {"planner"})


def _result_common(
    validator: _Validator,
    document: JsonObject,
    *,
    api_version: str,
    kind: str,
    required: set[str],
) -> JsonObject | None:
    """Validate fields common to publish and skip receipts."""
    obj = _header(
        validator,
        document,
        "$",
        api_version=api_version,
        kind=kind,
        required={
            "plan-id",
            "project-id",
            "publish-node-id",
            "target-instance-snapshot-id",
            "resolved-publish-identity",
            "evidence",
        }
        | required,
    )
    if obj is None:
        return None
    for field in (
        "plan-id",
        "project-id",
        "publish-node-id",
        "target-instance-snapshot-id",
    ):
        validator.string(obj.get(field), f"$.{field}")
    if not isinstance(obj.get("resolved-publish-identity"), Mapping):
        validator.error("$.resolved-publish-identity", "must be an object")
    if not isinstance(obj.get("evidence"), Mapping):
        validator.error("$.evidence", "must be an object")
    return obj


def _immutable_proof(validator: _Validator, document: JsonObject) -> None:
    """Validate `immutable-proof.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.immutable-proof/v1alpha1",
        kind="immutable-proof",
        required={
            "binding",
            "plan-id",
            "project-id",
            "variant-id",
            "build-result-artifact-name",
            "build-result-artifact-id",
            "bundle-artifact-name",
            "run",
            "artifact",
        },
    )
    if obj is None:
        return
    _immutable_binding(validator, obj.get("binding"), "$.binding")
    for field in (
        "plan-id",
        "project-id",
        "variant-id",
        "build-result-artifact-name",
        "bundle-artifact-name",
    ):
        validator.string(obj.get(field), f"$.{field}")
    validator.integer(
        obj.get("build-result-artifact-id"),
        "$.build-result-artifact-id",
        minimum=0,
    )
    _proof_run(validator, obj.get("run"), "$.run")
    _artifact_receipt(validator, obj.get("artifact"), "$.artifact")


def _github_release_asset_proof(
    validator: _Validator, document: JsonObject
) -> None:
    """Validate `github-release-asset-proof.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.github-release-asset-proof/v1alpha1",
        kind="github-release-asset-proof",
        required={
            "binding",
            "plan-id",
            "project-id",
            "variant-id",
            "run",
            "artifact",
            "attestation",
        },
    )
    if obj is None:
        return
    _github_asset_binding(validator, obj.get("binding"), "$.binding")
    for field in ("plan-id", "project-id", "variant-id"):
        validator.string(obj.get(field), f"$.{field}")
    _proof_run(validator, obj.get("run"), "$.run")
    _artifact_receipt(validator, obj.get("artifact"), "$.artifact")
    attestation = validator.mapping(
        obj.get("attestation"),
        "$.attestation",
        {
            "predicate-type",
            "subject-name",
            "subject-digest",
            "signer-workflow",
            "source-repository",
            "source-digest",
            "attestation-id",
            "attestation-url",
        },
    )
    if attestation is not None:
        if (
            attestation.get("predicate-type")
            != "https://slsa.dev/provenance/v1"
        ):
            validator.error(
                "$.attestation.predicate-type",
                "must be https://slsa.dev/provenance/v1",
            )
        for field in (
            "subject-name",
            "source-repository",
            "source-digest",
            "attestation-id",
            "attestation-url",
        ):
            validator.string(attestation.get(field), f"$.attestation.{field}")
        digest = attestation.get("subject-digest")
        if validator.string(digest, "$.attestation.subject-digest") and not str(
            digest
        ).startswith("sha256:"):
            validator.error(
                "$.attestation.subject-digest", "must start with sha256:"
            )
        signer = attestation.get("signer-workflow")
        if validator.string(
            signer, "$.attestation.signer-workflow"
        ) and not _SIGNER_WORKFLOW_RE.match(str(signer)):
            validator.error(
                "$.attestation.signer-workflow",
                "must be a full signer workflow identity",
            )


def _release_report(validator: _Validator, document: JsonObject) -> None:
    """Validate `release-report.json`."""
    obj = _header(
        validator,
        document,
        "$",
        api_version="three.release.report/v1alpha1",
        kind="release-report",
        required={"run", "plan", "artifacts", "jobs", "counts"},
    )
    if obj is None:
        return
    _report_run(validator, obj.get("run"), "$.run")
    _report_plan(validator, obj.get("plan"), "$.plan")
    _report_artifacts(validator, obj.get("artifacts"), "$.artifacts")
    jobs = _report_jobs(validator, obj.get("jobs"), "$.jobs")
    _report_counts(validator, obj.get("counts"), "$.counts")
    artifacts = obj.get("artifacts")
    plan_job = jobs.get("plan") if jobs is not None else None
    if (
        isinstance(plan_job, Mapping)
        and plan_job.get("conclusion") == "success"
        and isinstance(artifacts, Mapping)
    ):
        for field in (
            "execution-sets-artifact-name",
            "entry-publish-handoff-artifact-name",
        ):
            if artifacts.get(field) is None:
                validator.error(
                    f"$.artifacts.{field}",
                    "must be non-null after selector serialization",
                )


def _report_run(validator: _Validator, value: object, path: str) -> None:
    """Validate report run metadata."""
    obj = validator.mapping(
        value,
        path,
        {
            "repository",
            "workflow",
            "run-id",
            "run-attempt",
            "head-sha",
            "profile",
            "dry-run",
            "validation-build",
            "conclusion",
        },
    )
    if obj is None:
        return
    for field in ("repository", "workflow", "head-sha", "conclusion"):
        validator.string(obj.get(field), f"{path}.{field}")
    validator.enum(obj.get("profile"), f"{path}.profile", _PROFILES)
    validator.integer(obj.get("run-id"), f"{path}.run-id", minimum=0)
    validator.integer(obj.get("run-attempt"), f"{path}.run-attempt", minimum=0)
    validator.boolean(obj.get("dry-run"), f"{path}.dry-run")
    validator.boolean(obj.get("validation-build"), f"{path}.validation-build")


def _report_plan(validator: _Validator, value: object, path: str) -> None:
    """Validate report plan summary."""
    obj = validator.mapping(value, path, {"plan-id", "selected-project-ids"})
    if obj is None:
        return
    _nullable_string(validator, obj.get("plan-id"), f"{path}.plan-id")
    if obj.get("selected-project-ids") is not None:
        validator.string_array(
            obj.get("selected-project-ids"), f"{path}.selected-project-ids"
        )


def _report_artifacts(validator: _Validator, value: object, path: str) -> None:
    """Validate report artifact-name summary."""
    scalar = {
        "plan-artifact-name",
        "planner-diagnostics-artifact-name",
        "dotnet-planner-metadata-input-artifact-name",
        "dotnet-planner-metadata-artifact-name",
        "execution-sets-artifact-name",
        "entry-publish-handoff-artifact-name",
        "tag-result-artifact-name",
    }
    arrays = {
        "build-result-artifact-names",
        "publish-result-artifact-names",
        "skip-result-artifact-names",
    }
    obj = validator.mapping(value, path, scalar | arrays)
    if obj is None:
        return
    for field in scalar:
        _nullable_string(validator, obj.get(field), f"{path}.{field}")
    for field in arrays:
        validator.string_array(obj.get(field), f"{path}.{field}")


def _report_jobs(
    validator: _Validator, value: object, path: str
) -> JsonObject | None:
    """Validate report job conclusion objects."""
    required = {
        "authorize-entry",
        "validate-authoring",
        "dotnet-metadata",
        "plan",
        "build",
        "ensure-tag",
        "publish",
    }
    obj = validator.mapping(value, path, required)
    if obj is None:
        return None
    for field in (
        "authorize-entry",
        "validate-authoring",
        "dotnet-metadata",
        "plan",
        "ensure-tag",
    ):
        _conclusion_object(validator, obj.get(field), f"{path}.{field}")
    build = validator.mapping(
        obj.get("build"), f"{path}.build", {"conclusion", "failed-variant-ids"}
    )
    if build is not None:
        validator.string(build.get("conclusion"), f"{path}.build.conclusion")
        validator.string_array(
            build.get("failed-variant-ids"), f"{path}.build.failed-variant-ids"
        )
    publish = validator.mapping(
        obj.get("publish"),
        f"{path}.publish",
        {"conclusion", "failed-publish-node-ids"},
    )
    if publish is not None:
        validator.string(
            publish.get("conclusion"), f"{path}.publish.conclusion"
        )
        validator.string_array(
            publish.get("failed-publish-node-ids"),
            f"{path}.publish.failed-publish-node-ids",
        )
    return obj


def _report_counts(validator: _Validator, value: object, path: str) -> None:
    """Validate report count summary."""
    fields = {
        "selected-projects",
        "active-variants",
        "active-publish-nodes",
        "published-nodes",
        "skipped-publish-nodes",
    }
    obj = validator.mapping(value, path, fields)
    if obj is not None:
        for field in fields:
            validator.integer(obj.get(field), f"{path}.{field}", minimum=0)


def _conclusion_object(validator: _Validator, value: object, path: str) -> None:
    """Validate a simple report job conclusion object."""
    obj = validator.mapping(value, path, {"conclusion"})
    if obj is not None:
        validator.string(obj.get("conclusion"), f"{path}.conclusion")


def _common_plan_fields(
    validator: _Validator, obj: JsonObject, path: str, *, publish: bool
) -> None:
    """Validate common request fields copied from the plan envelope."""
    validator.string(obj.get("plan-id"), f"{path}.plan-id")
    commit_sha = obj.get("commit-sha")
    if validator.string(commit_sha, f"{path}.commit-sha") and not _SHA_RE.match(
        str(commit_sha)
    ):
        validator.error(
            f"{path}.commit-sha", "must be a 40-char lowercase hex SHA"
        )
    validator.enum(obj.get("profile"), f"{path}.profile", _PROFILES)
    if publish and obj.get("profile") not in _PROFILES:
        validator.error(f"{path}.profile", "is invalid")


def _artifact_receipt(validator: _Validator, value: object, path: str) -> None:
    """Validate a build-result artifact receipt entry."""
    obj = validator.mapping(
        value, path, {"bundle-relative-path", "sha256", "byte-size"}
    )
    if obj is not None:
        _artifact_receipt_fields(validator, obj, path)


def _artifact_receipt_fields(
    validator: _Validator, obj: JsonObject, path: str
) -> None:
    """Validate fields in an artifact receipt-like object."""
    bundle_path = obj.get("bundle-relative-path")
    if validator.string(bundle_path, f"{path}.bundle-relative-path"):
        _relative_path(
            validator, str(bundle_path), f"{path}.bundle-relative-path"
        )
    digest = obj.get("sha256")
    if validator.string(digest, f"{path}.sha256") and not _DIGEST_RE.match(
        str(digest)
    ):
        validator.error(f"{path}.sha256", "must be a lowercase 64-hex digest")
    validator.integer(obj.get("byte-size"), f"{path}.byte-size", minimum=0)


def _proof_run(validator: _Validator, value: object, path: str) -> None:
    """Validate proof run provenance."""
    obj = validator.mapping(
        value,
        path,
        {
            "repository",
            "workflow",
            "run-id",
            "run-attempt",
            "head-sha",
            "live",
            "dry-run",
            "validation-only",
        },
    )
    if obj is None:
        return
    for field in ("repository", "workflow"):
        validator.string(obj.get(field), f"{path}.{field}")
    head = obj.get("head-sha")
    if validator.string(head, f"{path}.head-sha") and not _SHA_RE.match(
        str(head)
    ):
        validator.error(
            f"{path}.head-sha", "must be a 40-char lowercase hex SHA"
        )
    validator.integer(obj.get("run-id"), f"{path}.run-id", minimum=0)
    validator.integer(obj.get("run-attempt"), f"{path}.run-attempt", minimum=0)
    for field in ("live", "dry-run", "validation-only"):
        validator.boolean(obj.get(field), f"{path}.{field}")


def _immutable_binding(validator: _Validator, value: object, path: str) -> None:
    """Validate an immutable proof binding."""
    obj = validator.mapping(
        value,
        path,
        {"publish-node-id", "artifact-id", "package-name", "version"},
    )
    if obj is not None:
        for field in (
            "publish-node-id",
            "artifact-id",
            "package-name",
            "version",
        ):
            validator.string(obj.get(field), f"{path}.{field}")


def _github_asset_binding(
    validator: _Validator, value: object, path: str
) -> None:
    """Validate a GitHub Release asset proof binding."""
    obj = validator.mapping(
        value,
        path,
        {"publish-node-id", "artifact-id", "release-tag", "asset-name"},
    )
    if obj is not None:
        for field in (
            "publish-node-id",
            "artifact-id",
            "release-tag",
            "asset-name",
        ):
            validator.string(obj.get(field), f"{path}.{field}")


def _map(validator: _Validator, value: object, path: str) -> JsonObject | None:
    """Validate a mapping value."""
    if not isinstance(value, Mapping):
        validator.error(path, "must be an object")
        return None
    for key in value:
        if not isinstance(key, str) or key == "":
            validator.error(path, "object keys must be non-empty strings")
    return value


def _string_map(
    validator: _Validator, value: object, path: str
) -> dict[str, str] | None:
    """Validate a string-to-string map."""
    obj = _map(validator, value, path)
    if obj is None:
        return None
    result: dict[str, str] = {}
    for key, item in obj.items():
        if validator.string(item, f"{path}.{key}"):
            result[str(key)] = str(item)
    return result


def _unique_sorted_strings(
    validator: _Validator, value: object, path: str
) -> None:
    """Validate a unique lexicographically sorted string array."""
    items = validator.string_array(value, path)
    if items is None:
        return
    if items != sorted(set(items)):
        validator.error(path, "must be unique and lexicographically sorted")


def _unique_string_array(
    validator: _Validator,
    value: object,
    path: str,
) -> list[str] | None:
    """Validate a string array with no duplicate values."""
    items = validator.string_array(value, path)
    if items is None:
        return None
    if len(items) != len(set(items)):
        validator.error(path, "must not contain duplicate values")
    return items


def _nullable_nonnegative_int(
    validator: _Validator, value: object, path: str
) -> None:
    """Validate a nullable non-negative integer."""
    if value is None:
        return
    validator.integer(value, path, minimum=0)


def _nullable_string(validator: _Validator, value: object, path: str) -> None:
    """Validate a nullable string."""
    if value is None:
        return
    validator.string(value, path)


def _require_exact_keys(
    validator: _Validator,
    mapping: Mapping[str, object] | None,
    expected: set[Any],
    path: str,
) -> None:
    """Require *mapping* keys to equal *expected*."""
    if mapping is None:
        return
    actual = set(mapping)
    expected_strings = {str(item) for item in expected}
    if actual != expected_strings:
        validator.error(path, "keys must exactly match the referenced id set")


def _relative_path(validator: _Validator, value: str, path: str) -> None:
    """Validate a normalized relative path."""
    if (
        value.startswith("/")
        or "\\" in value
        or _WINDOWS_DRIVE_ABSOLUTE_RE.match(value)
    ):
        validator.error(path, "must be a normalized relative path")
        return
    if any(part in {"", ".", ".."} for part in value.split("/")):
        validator.error(
            path, "must not contain empty, dot, or dot-dot segments"
        )


def _snapshot_id(value: object) -> str:
    """Return the family/instance-id identifier for a target snapshot."""
    if not isinstance(value, Mapping):
        return ""
    family = value.get("family")
    instance_id = value.get("instance-id")
    if isinstance(family, str) and isinstance(instance_id, str):
        return f"{family}/{instance_id}"
    return ""
