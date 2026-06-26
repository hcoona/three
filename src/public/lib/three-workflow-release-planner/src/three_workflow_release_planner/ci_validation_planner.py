"""Planner-side CI affected-validation scope realization."""

from __future__ import annotations

import fnmatch
import json
import re
import shutil
import subprocess
import tomllib
import xml.etree.ElementTree as ET
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import Literal, cast

import yaml
from three_workflow_release_authoring import (
    AuthoringSnapshot,
    AuthoringValidationError,
    ProjectDescriptor,
    Variant,
    validate_authoring,
)
from three_workflow_release_contracts import (
    PLANNED_CAPABILITY_ORDER,
    CiValidationPlanSnapshot,
    CiValidationRequestNormalization,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    NormalizedCiValidationRequest,
    canonical_json_bytes,
    canonical_json_digest,
    ci_validation_diagnostic,
    freeze_ci_validation_plan,
    normalize_ci_validation_request,
)

Json = dict[str, object]
ImpactCategory = Literal[
    "project-scoped",
    "ecosystem-scoped",
    "workflow-release-infrastructure",
    "global",
    "known-non-impacting",
    "unknown",
]

_SUPPORTED_ECOSYSTEMS = frozenset(
    {"dotnet", "python", "javascript", "typescript", "ruby"}
)
_PROVIDER_BY_ECOSYSTEM = {
    "dotnet": "dotnet",
    "python": "python",
    "javascript": "javascript-typescript",
    "typescript": "javascript-typescript",
    "ruby": "ruby",
}
_RUNNER_BY_ECOSYSTEM = {
    "dotnet": "windows",
    "python": "ubuntu",
    "javascript": "ubuntu",
    "typescript": "ubuntu",
    "ruby": "ubuntu",
}
_TOOLING_SURFACES = (
    "authoring-validation",
    "build-execution",
    "classifier",
    "descriptor-contract",
    "descriptor-schema-documentation",
    "fact-provider",
    "planner",
    "publish-execution",
    "smoke-validation",
    "target-catalog",
    "workflow-orchestration",
    "workflow-release-contract",
)
_SCHEDULED_FULL_EQUIVALENT_SURFACES = frozenset(
    {
        "planner",
        "classifier",
        "workflow-release-contract",
        "workflow-orchestration",
    },
)
_ALL_DESCRIPTOR_SURFACES = frozenset(
    {
        "fact-provider",
        "descriptor-contract",
        "authoring-validation",
        "build-execution",
        "publish-execution",
        "smoke-validation",
    },
)
_ARTIFACT_SURFACES = frozenset(
    {
        "descriptor-contract",
        "target-catalog",
        "build-execution",
        "publish-execution",
        "smoke-validation",
    },
)
_GLOBAL_PATHS = frozenset(
    {
        ".config/dotnet-tools.json",
        ".pre-commit-config.yaml",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "dirs.proj",
        "global.json",
        "hk.pkl",
        "mise.toml",
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "pyproject.toml",
        "uv.lock",
        "version.json",
    },
)
_MAX_READABLE_TOKEN_LENGTH = 72
_KNOWN_NON_IMPACTING_GLOBS = (
    ".github/hooks/**",
    ".gitignore",
    "README.md",
    "AGENTS.md",
    "docs/**",
    "*.md",
)
_WORKFLOW_RELEASE_SURFACE_GLOBS: tuple[tuple[str, str], ...] = (
    ("src/public/lib/three-workflow-release-planner/**", "planner"),
    (
        "src/public/lib/three-workflow-release-contracts/**",
        "workflow-release-contract",
    ),
    (
        "src/public/lib/three-workflow-release-authoring/**",
        "authoring-validation",
    ),
    ("src/public/lib/three-workflow-release-build/**", "build-execution"),
    ("src/public/lib/three-workflow-release-publish/**", "publish-execution"),
    ("src/public/lib/three-workflow-release-metadata/**", "fact-provider"),
    (
        "src/public/lib/three-workflow-release-proof/**",
        "workflow-release-contract",
    ),
    ("eng/release/**", "target-catalog"),
    (".github/CODEOWNERS", "workflow-orchestration"),
    (".github/actionlint.yaml", "workflow-orchestration"),
    ("eng/scripts/find_project_path.py", "workflow-orchestration"),
    ("eng/scripts/validate_pep440_version.py", "workflow-orchestration"),
    ("eng/scripts/validate_semver2_version.py", "workflow-orchestration"),
    ("eng/scripts/validate_rubygems_version.py", "workflow-orchestration"),
    ("eng/scripts/release_orchestrate_*.sh", "workflow-orchestration"),
    ("eng/scripts/prepare_npm_publish.py", "publish-execution"),
    ("eng/scripts/publish_*_idempotent.sh", "publish-execution"),
    ("eng/scripts/validate_pypi_remote_digests.sh", "publish-execution"),
    (
        "eng/scripts/verify_python_distribution_exactness.py",
        "build-execution",
    ),
    (
        "eng/scripts/verify_python_artifact_version.py",
        "build-execution",
    ),
    (
        "tests/test_verify_python_distribution_exactness.py",
        "build-execution",
    ),
    ("eng/scripts/workflow_release_*.py", "workflow-orchestration"),
    (".github/workflows/docs/DESIGN.v2.md", "workflow-orchestration"),
    (".github/workflows/*.yml", "workflow-orchestration"),
    (".github/workflows/*.yaml", "workflow-orchestration"),
    (".github/workflows/release-*.yml", "workflow-orchestration"),
    (".github/workflows/release-*.yaml", "workflow-orchestration"),
    ("tests/test_workflow_release_control.py", "workflow-orchestration"),
    ("tests/fixtures/workflow-release-*.json", "smoke-validation"),
    (
        "docs/wiki/analyses/workflow-release-*.md",
        "descriptor-schema-documentation",
    ),
)
_ECOSYSTEM_PATH_GLOBS: tuple[tuple[str, str, str], ...] = (
    ("**/*.sln", "dotnet", "dotnet-solution"),
    ("**/*.slnx", "dotnet", "dotnet-solution"),
    ("**/Directory.Build.props", "dotnet", "dotnet-build-props"),
    ("**/Directory.Build.targets", "dotnet", "dotnet-build-targets"),
    ("**/Directory.Packages.props", "dotnet", "dotnet-cpm"),
    ("**/packages.lock.json", "dotnet", "dotnet-lock"),
    ("**/uv.lock", "python", "uv-lock"),
    ("**/pyproject.toml", "python", "python-project-metadata"),
    ("**/pnpm-lock.yaml", "javascript", "pnpm-lock"),
    ("**/pnpm-workspace.yaml", "javascript", "pnpm-workspace"),
    ("**/package.json", "javascript", "package-metadata"),
    ("**/tsconfig*.json", "typescript", "typescript-config"),
)


@dataclass(frozen=True, slots=True)
class CiValidationPlannerInputs:
    """Inputs for deterministic CI affected-validation planning."""

    request: Mapping[str, object]
    repo_root: Path
    expected_run_id: str
    expected_run_attempt: str
    created_at: str | None = None
    plan_id: str | None = None
    observed_commit_sha: str | None = None
    tracked_files: Sequence[str] | None = None
    policy_version: str | None = None


class CiValidationPlanningError(ValueError):
    """Raised when a CI validation plan cannot be emitted safely."""

    def __init__(self, diagnostics: Sequence[Mapping[str, object]]) -> None:
        """Initialize the planning error with fail-closed diagnostics."""
        self.diagnostics = tuple(dict(item) for item in diagnostics)
        message = "; ".join(str(item.get("message")) for item in diagnostics)
        super().__init__(message)


class _FactDiscoveryError(ValueError):
    """Raised when authoring fact discovery cannot be trusted."""

    def __init__(self, diagnostics: Sequence[Mapping[str, object]]) -> None:
        self.diagnostics = tuple(dict(item) for item in diagnostics)
        message = "; ".join(str(item.get("message")) for item in diagnostics)
        super().__init__(message)


@dataclass(frozen=True, slots=True)
class _SubjectFacts:
    subject: Json
    provider: str | None


@dataclass(frozen=True, slots=True)
class _Impact:
    impact_id: str
    category: ImpactCategory
    paths: tuple[str, ...]
    source_rule: str
    rationale: str
    coverage_target: Json
    descriptor_validation: bool
    downstream_expansion: bool
    broad_expansion: bool
    diagnostic: str | None = None


@dataclass(frozen=True, slots=True)
class _PlanningFacts:
    subjects: Mapping[str, _SubjectFacts]
    providers: tuple[Json, ...]
    dependency_edges: tuple[Json, ...]
    dependency_failures: tuple[Json, ...]


@dataclass(slots=True)
class _ResolvedScope:
    selected_subject_ids: set[str]
    tooling_surfaces: set[str]
    descriptor_subject_ids: set[str]
    artifact_subject_ids: set[str]
    source_impacts_by_subject: dict[str, set[str]]
    source_impacts_by_tooling_surface: dict[str, set[str]]
    broad_expansions: list[Json]
    provenance: list[Json]
    diagnostics: list[Json]
    lightweight_source_impacts: set[str]
    lightweight_only: bool = False
    full_scope_descriptor_coverage: bool = False


@dataclass(slots=True)
class _PlanRecords:
    descriptor_obligations: list[Json]
    validation_obligations: list[Json]
    artifact_obligations: list[Json]
    work_groups: list[Json]
    evidence_expectations: list[Json]
    detail_profiles: list[Json]


def plan_ci_validation_from_repo(
    inputs: CiValidationPlannerInputs,
) -> CiValidationPlanSnapshot:
    """Plan CI affected validation from a repository checkout."""
    tracked_files = tuple(inputs.tracked_files or _git_files(inputs.repo_root))
    try:
        snapshot = validate_authoring(
            inputs.repo_root,
            tracked_files=tracked_files,
        )
    except AuthoringValidationError as error:
        normalization = _normalize_or_raise(inputs)
        diagnostic = _diagnostic(
            code=DiagnosticFamily.DESCRIPTOR_INVALID.value,
            detail=None,
            message="workflow-release authoring facts could not be validated",
            source_type="descriptor",
            source_id=None,
            ordinal=1,
            extra={"issues": [issue.message for issue in error.issues]},
        )
        return _fail_closed_plan(inputs, normalization, [diagnostic])
    return plan_ci_validation(snapshot, inputs, tracked_files=tracked_files)


def plan_ci_validation(
    snapshot: AuthoringSnapshot,
    inputs: CiValidationPlannerInputs,
    *,
    tracked_files: Sequence[str] | None = None,
) -> CiValidationPlanSnapshot:
    """Realize a deterministic CI validation plan from normalized facts."""
    normalization = _normalize_or_raise(inputs)
    request = normalization.request
    if request is None:
        message = "normalization unexpectedly produced no request"
        raise AssertionError(message)
    tracked = tuple(
        tracked_files or inputs.tracked_files or _git_files(inputs.repo_root)
    )
    created_at = inputs.created_at or _now_rfc3339()
    observed_commit_sha = inputs.observed_commit_sha or _validation_tree_sha(
        request
    )
    plan_id = inputs.plan_id or _plan_id(request)
    try:
        facts = _discover_facts(snapshot, inputs.repo_root, tracked)
    except _FactDiscoveryError as error:
        return _freeze(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            diagnostics=error.diagnostics,
            classification=_classification(
                _fact_discovery_failure_impacts(request),
                [],
                [],
                lightweight_only=False,
            ),
            facts=None,
            policy_version=inputs.policy_version,
        )
    if (
        request.mode != "scheduled_full"
        and _affected_range(request).get("status") == "unavailable"
    ):
        detail = _affected_range(request).get("diagnostic-detail")
        diagnostic = _diagnostic(
            code=DiagnosticFamily.RANGE_UNCONFIRMED.value,
            detail=str(detail)
            if isinstance(detail, str)
            else DiagnosticDetail.MISSING.value,
            message="affected range was not confirmed",
            source_type="request",
            source_id=None,
            ordinal=1,
        )
        return _freeze(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            diagnostics=[diagnostic],
            classification=_classification(
                [],
                [],
                [],
                lightweight_only=False,
            ),
            facts=None,
            policy_version=inputs.policy_version,
        )
    impacts = _classify_request(request, facts)
    diagnostics = _impact_diagnostics(impacts)
    if diagnostics:
        diagnostic_code = str(
            diagnostics[0].get("code", DiagnosticFamily.UNKNOWN_CHANGE.value)
        )
        return _freeze(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            diagnostics=diagnostics,
            classification=_classification(
                _fail_closed_impacts_from(impacts, diagnostic_code),
                [],
                [],
                lightweight_only=False,
            ),
            facts=None,
            policy_version=inputs.policy_version,
        )
    scope = _resolve_scope(request, impacts, facts)
    if scope.diagnostics:
        diagnostic_code = str(
            scope.diagnostics[0].get(
                "code", DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
            )
        )
        fail_closed_impacts = _fail_closed_impacts_from(
            impacts,
            diagnostic_code,
        )
        return _freeze(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            diagnostics=scope.diagnostics,
            classification=_classification(
                fail_closed_impacts,
                [],
                [],
                lightweight_only=scope.lightweight_only,
            ),
            facts=None,
            policy_version=inputs.policy_version,
        )
    subjects = _subjects_with_selection(
        facts.subjects, scope.selected_subject_ids
    )
    records = _build_plan_records(subjects, facts, scope)
    _sort_plan_records(records)
    diagnostics = _capability_diagnostics(
        subjects, records.validation_obligations
    )
    if diagnostics:
        diagnostic_code = str(
            diagnostics[0].get(
                "code", DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
            )
        )
        return _freeze(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            diagnostics=diagnostics,
            classification=_classification(
                _fail_closed_impacts_from(impacts, diagnostic_code),
                [],
                [],
                lightweight_only=scope.lightweight_only,
            ),
            facts=None,
            policy_version=inputs.policy_version,
        )
    return _freeze(
        request=request,
        plan_id=plan_id,
        created_at=created_at,
        observed_commit_sha=observed_commit_sha,
        verdict_intent="executable",
        diagnostics=[],
        classification=_classification(
            impacts,
            scope.broad_expansions,
            scope.provenance,
            lightweight_only=scope.lightweight_only,
        ),
        facts=facts,
        subjects=subjects,
        records=records,
        policy_version=inputs.policy_version,
    )


def _normalize_or_raise(
    inputs: CiValidationPlannerInputs,
) -> CiValidationRequestNormalization:
    normalization = normalize_ci_validation_request(
        inputs.request,
        expected_run_id=inputs.expected_run_id,
        expected_run_attempt=inputs.expected_run_attempt,
    )
    if normalization.request is None:
        raise CiValidationPlanningError(normalization.diagnostics)
    return normalization


def _fail_closed_plan(
    inputs: CiValidationPlannerInputs,
    normalization: object,
    diagnostics: Sequence[Mapping[str, object]],
) -> CiValidationPlanSnapshot:
    request = getattr(normalization, "request", None)
    if not isinstance(request, NormalizedCiValidationRequest):
        raise CiValidationPlanningError(diagnostics)
    return _freeze(
        request=request,
        plan_id=inputs.plan_id or _plan_id(request),
        created_at=inputs.created_at or _now_rfc3339(),
        observed_commit_sha=inputs.observed_commit_sha
        or _validation_tree_sha(request),
        verdict_intent="fail-closed",
        diagnostics=diagnostics,
        classification=_classification(
            _authoring_validation_failure_impacts(request),
            [],
            [],
            lightweight_only=False,
        ),
        facts=None,
        policy_version=inputs.policy_version,
    )


def _authoring_validation_failure_impacts(
    request: NormalizedCiValidationRequest,
) -> list[_Impact]:
    if request.mode == "scheduled_full":
        return []
    return _coalesce_impacts(
        [
            _impact(
                category="unknown",
                paths=[path],
                source_rule="authoring-validation-fail-closed",
                rationale=(
                    "Changed path requires fail-closed planning because "
                    "workflow-release authoring facts could not be validated."
                ),
                coverage_target={"type": "none", "id": None},
                descriptor_validation=False,
                downstream_expansion=False,
                broad_expansion=False,
                diagnostic=DiagnosticFamily.DESCRIPTOR_INVALID.value,
            )
            for path in _changed_files(request)
        ],
    )


def _freeze(  # noqa: PLR0913
    *,
    request: NormalizedCiValidationRequest,
    plan_id: str,
    created_at: str,
    observed_commit_sha: str,
    verdict_intent: Literal["executable", "fail-closed"],
    diagnostics: Sequence[Mapping[str, object]],
    classification: Mapping[str, object],
    facts: _PlanningFacts | None,
    subjects: Sequence[Mapping[str, object]] = (),
    records: _PlanRecords | None = None,
    policy_version: str | None = None,
) -> CiValidationPlanSnapshot:
    records = records or _PlanRecords([], [], [], [], [], [])
    providers = facts.providers if facts is not None else None
    try:
        return freeze_ci_validation_plan(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent=verdict_intent,
            classification=classification,
            subjects=subjects,
            descriptor_obligations=records.descriptor_obligations,
            validation_obligations=records.validation_obligations,
            artifact_obligations=records.artifact_obligations,
            work_groups=records.work_groups,
            evidence_expectations=records.evidence_expectations,
            detail_profiles=records.detail_profiles,
            diagnostics=_sorted_diagnostics(diagnostics),
            fact_snapshot_providers=providers,
            policy_version=policy_version,
        )
    except ContractValidationError as error:
        if verdict_intent == "fail-closed":
            raise
        diagnostic = _diagnostic(
            code=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
            detail=None,
            message="planned CI validation scope was structurally ambiguous",
            source_type="fact-provider",
            source_id=None,
            ordinal=1,
            extra={"issues": [issue.message for issue in error.issues]},
        )
        safe_classification = _classification(
            impacts=_fact_discovery_failure_impacts(request),
            broad_expansions=[],
            provenance=[],
            lightweight_only=False,
        )
        return freeze_ci_validation_plan(
            request=request,
            plan_id=plan_id,
            created_at=created_at,
            observed_commit_sha=observed_commit_sha,
            verdict_intent="fail-closed",
            classification=safe_classification,
            diagnostics=[diagnostic],
            fact_snapshot_providers=None,
            policy_version=policy_version,
        )


def _sorted_diagnostics(
    diagnostics: Sequence[Mapping[str, object]],
) -> list[Mapping[str, object]]:
    return sorted(
        (dict(diagnostic) for diagnostic in diagnostics),
        key=lambda diagnostic: str(diagnostic["diagnostic-id"]),
    )


def _discover_facts(
    snapshot: AuthoringSnapshot,
    repo_root: Path,
    tracked_files: Sequence[str],
) -> _PlanningFacts:
    subjects: dict[str, _SubjectFacts] = {}
    for project in snapshot.projects.values():
        ecosystem = _normalized_project_ecosystem(project, repo_root)
        subject_id = _subject_id(ecosystem, project.release_root)
        supported = ecosystem in _SUPPORTED_ECOSYSTEMS
        subject = _subject_record(
            subject_id=subject_id,
            ecosystem=ecosystem,
            root=project.release_root,
            active=supported,
            selected=False,
            capability_class="descriptor-backed"
            if supported
            else "validation-only",
            descriptor_path=project.descriptor_path if supported else None,
            descriptor_identity=project.project_id if supported else None,
            capabilities=_descriptor_capabilities(project)
            if supported
            else _empty_capabilities(),
            inclusion_source="descriptor",
            inclusion_reason="workflow-release descriptor",
            exclusion_reason=None if supported else "unsupported-ecosystem",
        )
        subjects[subject_id] = _SubjectFacts(
            subject=subject,
            provider=_PROVIDER_BY_ECOSYSTEM.get(ecosystem),
        )
    for root in _uv_workspace_roots(repo_root):
        _add_validation_only_subject(
            subjects,
            repo_root,
            "python",
            root,
            "uv workspace",
        )
    for root in _pnpm_workspace_roots(repo_root):
        ecosystem = (
            "typescript"
            if _has_typescript_config(repo_root / root, root)
            else "javascript"
        )
        _add_validation_only_subject(
            subjects,
            repo_root,
            ecosystem,
            root,
            "pnpm workspace",
        )
    for root in _dotnet_project_roots(tracked_files):
        _add_validation_only_subject(
            subjects,
            repo_root,
            "dotnet",
            root,
            ".NET project",
        )
    dependency_failures: list[Json] = []
    dependency_edges = _dependency_edges(
        repo_root, subjects, dependency_failures
    )
    providers = _fact_providers(snapshot, subjects, dependency_edges)
    dependency_edges = tuple(
        edge
        for provider in providers
        for edge in cast("Sequence[Json]", provider["dependency-edges"])
    )
    return _PlanningFacts(
        subjects=dict(sorted(subjects.items())),
        providers=providers,
        dependency_edges=dependency_edges,
        dependency_failures=tuple(dependency_failures),
    )


def _fact_discovery_failure_impacts(
    request: NormalizedCiValidationRequest,
) -> list[_Impact]:
    if request.mode == "scheduled_full":
        return []
    return _coalesce_impacts(
        [
            _impact(
                category="unknown",
                paths=[path],
                source_rule="fact-discovery-fail-closed",
                rationale=(
                    "Changed path requires fail-closed planning because "
                    "authoring facts could not be discovered completely."
                ),
                coverage_target={"type": "none", "id": None},
                descriptor_validation=False,
                downstream_expansion=False,
                broad_expansion=False,
                diagnostic=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
            )
            for path in _changed_files(request)
        ],
    )


def _add_validation_only_subject(
    subjects: dict[str, _SubjectFacts],
    repo_root: Path,
    ecosystem: str,
    root: str,
    reason: str,
) -> None:
    root = _clean_path(root)
    if any(
        _path_is_under(root, str(item.subject["root"]))
        for item in subjects.values()
    ):
        return
    subject_id = _subject_id(ecosystem, root)
    subjects.setdefault(
        subject_id,
        _SubjectFacts(
            subject=_subject_record(
                subject_id=subject_id,
                ecosystem=ecosystem,
                root=root,
                active=True,
                selected=False,
                capability_class="validation-only",
                descriptor_path=None,
                descriptor_identity=None,
                capabilities=_validation_capabilities(
                    repo_root,
                    ecosystem,
                    root,
                ),
                inclusion_source="workspace"
                if ecosystem != "dotnet"
                else "solution",
                inclusion_reason=reason,
                exclusion_reason=None,
            ),
            provider=_PROVIDER_BY_ECOSYSTEM[ecosystem],
        ),
    )


def _subject_record(  # noqa: PLR0913
    *,
    subject_id: str,
    ecosystem: str,
    root: str,
    active: bool,
    selected: bool,
    capability_class: str,
    descriptor_path: str | None,
    descriptor_identity: str | None,
    capabilities: Mapping[str, object],
    inclusion_source: str,
    inclusion_reason: str,
    exclusion_reason: str | None,
) -> Json:
    return {
        "subject-id": subject_id,
        "ecosystem": ecosystem,
        "root": root,
        "activity-status": "active" if active else "inactive",
        "selection-status": "selected" if selected else "not-selected",
        "capability-class": capability_class,
        "descriptor": {
            "path": descriptor_path,
            "identity": descriptor_identity,
        },
        "capabilities": dict(sorted(capabilities.items())),
        "inclusion": {"source": inclusion_source, "reason": inclusion_reason},
        "exclusion": {"reason": exclusion_reason},
    }


def _descriptor_capabilities(project: ProjectDescriptor) -> dict[str, bool]:
    has_artifacts = any(
        target.artifacts
        for targets in project.profiles.values()
        for target in targets
    )
    return {
        "build": True,
        "format": False,
        "lint": False,
        "release-shaped-artifacts": has_artifacts,
        "test": False,
        "type-check": False,
    }


def _validation_capabilities(
    repo_root: Path,
    ecosystem: str,
    root: str,
) -> dict[str, bool]:
    if ecosystem == "python":
        return _python_validation_capabilities(repo_root, root)
    if ecosystem in {"javascript", "typescript"}:
        return _javascript_validation_capabilities(repo_root, root)
    if ecosystem == "dotnet":
        return _dotnet_validation_capabilities(repo_root, root)
    if ecosystem == "ruby":
        return _ruby_validation_capabilities(repo_root, root)
    return _empty_capabilities()


def _python_validation_capabilities(
    repo_root: Path,
    root: str,
) -> dict[str, bool]:
    project_root = repo_root / root
    return {
        "build": (project_root / "pyproject.toml").is_file(),
        "format": False,
        "lint": False,
        "release-shaped-artifacts": False,
        "test": _python_project_has_tests(repo_root, root),
        "type-check": _root_pyrefly_project_includes_root(repo_root, root),
    }


def _python_project_has_tests(repo_root: Path, root: str) -> bool:
    project_root = repo_root / root
    if _has_files_matching(project_root, ("test_*.py", "*_test.py")):
        return True
    tests_root = repo_root / _project_tests_root(root)
    return _has_files_matching(tests_root, ("test_*.py", "*_test.py"))


def _project_tests_root(root: str) -> str:
    if root.startswith("src/"):
        return f"tests/{root[4:]}"
    return f"tests/{root}"


def _has_files_matching(root: Path, patterns: Sequence[str]) -> bool:
    if not root.is_dir():
        return False
    return any(
        path.is_file() for pattern in patterns for path in root.rglob(pattern)
    )


def _root_pyrefly_project_includes_root(repo_root: Path, root: str) -> bool:
    pyproject_path = repo_root / "pyproject.toml"
    if not pyproject_path.is_file():
        return False
    try:
        pyproject = tomllib.loads(pyproject_path.read_text(encoding="utf-8"))
    except (OSError, tomllib.TOMLDecodeError):
        return False
    tool = pyproject.get("tool")
    if not isinstance(tool, Mapping):
        return False
    pyrefly = tool.get("pyrefly")
    if not isinstance(pyrefly, Mapping):
        return False
    includes = pyrefly.get("project-includes")
    if not isinstance(includes, Sequence) or isinstance(includes, str | bytes):
        return False
    root_prefix = f"{root.rstrip('/')}/"
    return any(
        isinstance(include, str)
        and (include == root or include.startswith(root_prefix))
        for include in includes
    )


def _javascript_validation_capabilities(
    repo_root: Path,
    root: str,
) -> dict[str, bool]:
    scripts = _package_json_scripts(repo_root / root / "package.json")
    return {
        "build": "build" in scripts,
        "format": "format:check" in scripts,
        "lint": "lint" in scripts,
        "release-shaped-artifacts": False,
        "test": "test" in scripts,
        "type-check": "typecheck" in scripts,
    }


def _package_json_scripts(package_json_path: Path) -> Mapping[str, object]:
    if not package_json_path.is_file():
        return {}
    try:
        package_json = json.loads(package_json_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    scripts = package_json.get("scripts")
    if isinstance(scripts, Mapping):
        return scripts
    return {}


def _dotnet_validation_capabilities(
    repo_root: Path,
    root: str,
) -> dict[str, bool]:
    project_root = repo_root / root
    project_path = _single_dotnet_project_path(project_root)
    is_test_project = _dotnet_project_is_test_project(project_path)
    return {
        "build": project_path is not None,
        "format": False,
        "lint": False,
        "release-shaped-artifacts": False,
        "test": is_test_project,
        "type-check": False,
    }


def _single_dotnet_project_path(project_root: Path) -> Path | None:
    projects = sorted(
        path
        for extension in ("*.csproj", "*.fsproj", "*.vbproj")
        for path in project_root.glob(extension)
    )
    if len(projects) != 1:
        return None
    return projects[0]


def _dotnet_project_is_test_project(project_path: Path | None) -> bool:
    if project_path is None:
        return False
    if "/tests/" in project_path.as_posix():
        return True
    try:
        root = ET.parse(project_path).getroot()  # noqa: S314
    except (OSError, ET.ParseError):
        return False
    for property_group in root.findall("PropertyGroup"):
        is_test_project = property_group.findtext("IsTestProject")
        if (
            isinstance(is_test_project, str)
            and is_test_project.lower() == "true"
        ):
            return True
    return False


def _ruby_validation_capabilities(
    repo_root: Path,
    root: str,
) -> dict[str, bool]:
    project_root = repo_root / root
    return {
        "build": any(project_root.glob("*.gemspec")),
        "format": False,
        "lint": False,
        "release-shaped-artifacts": False,
        "test": False,
        "type-check": False,
    }


def _empty_capabilities() -> dict[str, bool]:
    return {
        "build": False,
        "format": False,
        "lint": False,
        "release-shaped-artifacts": False,
        "test": False,
        "type-check": False,
    }


def _fact_providers(
    snapshot: AuthoringSnapshot,
    subjects: Mapping[str, _SubjectFacts],
    dependency_edges: Sequence[Mapping[str, object]],
) -> tuple[Json, ...]:
    providers: list[Json] = []
    for provider_id in ("dotnet", "javascript-typescript", "python", "ruby"):
        provider_subjects = [
            item.subject
            for item in subjects.values()
            if item.provider == provider_id
        ]
        providers.append(
            {
                "provider": provider_id,
                "provider-version": "planner-discovery/v1",
                "status": "available",
                "roots": sorted(
                    str(item["root"]) for item in provider_subjects
                ),
                "subjects": sorted(
                    str(item["subject-id"]) for item in provider_subjects
                ),
                "dependency-edges": [
                    dict(edge)
                    for edge in dependency_edges
                    if _edge_provider(edge, subjects) == provider_id
                ],
                "tooling-surfaces": [],
                "descriptors": [],
                "target-catalog": _empty_target_catalog(),
                "diagnostics": [],
            },
        )
    all_subjects = [item.subject for item in subjects.values()]
    providers.append(
        {
            "provider": "workflow-release",
            "provider-version": "planner-discovery/v1",
            "status": "available",
            "roots": [],
            "subjects": [],
            "dependency-edges": [],
            "tooling-surfaces": list(_TOOLING_SURFACES),
            "descriptors": _provider_descriptors(all_subjects),
            "target-catalog": _provider_target_catalog(
                "workflow-release",
                snapshot,
                all_subjects,
            ),
            "diagnostics": [],
        },
    )
    return tuple(providers)


def _edge_provider(
    edge: Mapping[str, object],
    subjects: Mapping[str, _SubjectFacts],
) -> str | None:
    from_subject = subjects.get(str(edge["from-subject-id"]))
    to_subject = subjects.get(str(edge["to-subject-id"]))
    if from_subject is None or to_subject is None:
        return None
    if from_subject.provider != to_subject.provider:
        return None
    return from_subject.provider


def _dependency_edges(
    repo_root: Path,
    subjects: Mapping[str, _SubjectFacts],
    dependency_failures: list[Json],
) -> tuple[Json, ...]:
    edges = [
        *_dotnet_dependency_edges(repo_root, subjects, dependency_failures),
        *_javascript_dependency_edges(repo_root, subjects, dependency_failures),
        *_python_dependency_edges(repo_root, subjects, dependency_failures),
    ]
    return tuple(
        sorted(
            _unique_dependency_edges(edges),
            key=lambda item: (
                str(item["from-subject-id"]),
                str(item["to-subject-id"]),
                str(item["relation"]),
            ),
        )
    )


def _unique_dependency_edges(edges: Sequence[Json]) -> list[Json]:
    result: list[Json] = []
    seen: set[tuple[str, str, str]] = set()
    for edge in edges:
        key = (
            str(edge["from-subject-id"]),
            str(edge["to-subject-id"]),
            str(edge["relation"]),
        )
        if key in seen:
            continue
        seen.add(key)
        result.append(edge)
    return result


def _dotnet_dependency_edges(
    repo_root: Path,
    subjects: Mapping[str, _SubjectFacts],
    dependency_failures: list[Json],
) -> list[Json]:
    subjects_by_root = {
        str(facts.subject["root"]): subject_id
        for subject_id, facts in subjects.items()
        if facts.provider == "dotnet"
    }
    edges: list[Json] = []
    for root, from_subject_id in subjects_by_root.items():
        project = _dotnet_project_file(repo_root / root)
        if project is None:
            continue
        for referenced_project in _dotnet_project_references(
            project,
            from_subject_id,
            _clean_path(str(Path(root) / project.name)),
            dependency_failures,
        ):
            referenced_root = _relative_parent(repo_root, referenced_project)
            if referenced_root is None:
                continue
            to_subject_id = subjects_by_root.get(referenced_root)
            if to_subject_id is None or to_subject_id == from_subject_id:
                continue
            edges.append(
                {
                    "from-subject-id": from_subject_id,
                    "to-subject-id": to_subject_id,
                    "relation": "project-reference",
                }
            )
    return edges


def _dotnet_project_file(root: Path) -> Path | None:
    projects = sorted(root.glob("*.csproj"))
    if not projects:
        return None
    return projects[0]


def _dotnet_project_references(
    project: Path,
    subject_id: str,
    metadata_path: str,
    dependency_failures: list[Json],
) -> list[Path]:
    try:
        tree = ET.parse(project)  # noqa: S314
    except ET.ParseError as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "dotnet", subject_id, metadata_path, "parse-error", error
            ),
        )
        return []
    except OSError as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "dotnet", subject_id, metadata_path, "read-error", error
            ),
        )
        return []
    references: list[Path] = []
    for item in tree.iter():
        if _xml_local_name(item.tag) != "ProjectReference":
            continue
        include = item.attrib.get("Include")
        if not include:
            continue
        references.append((project.parent / include).resolve())
    return references


def _relative_parent(repo_root: Path, path: Path) -> str | None:
    try:
        return _clean_path(str(path.parent.relative_to(repo_root.resolve())))
    except ValueError:
        return None


def _xml_local_name(tag: str) -> str:
    return tag.rsplit("}", maxsplit=1)[-1]


def _javascript_dependency_edges(
    repo_root: Path,
    subjects: Mapping[str, _SubjectFacts],
    dependency_failures: list[Json],
) -> list[Json]:
    packages: dict[str, str] = {}
    dependencies: dict[str, list[str]] = {}
    for subject_id, facts in subjects.items():
        if facts.provider != "javascript-typescript":
            continue
        package_json = _read_package_json(
            repo_root / str(facts.subject["root"]) / "package.json",
            subject_id,
            _clean_path(str(Path(str(facts.subject["root"])) / "package.json")),
            dependency_failures,
        )
        package_name = package_json.get("name")
        if isinstance(package_name, str):
            packages[package_name] = subject_id
        dependencies[subject_id] = _package_dependencies(package_json)

    edges: list[Json] = []
    for from_subject_id, deps in dependencies.items():
        for dependency in deps:
            to_subject_id = packages.get(dependency)
            if to_subject_id is None or to_subject_id == from_subject_id:
                continue
            edges.append(
                {
                    "from-subject-id": from_subject_id,
                    "to-subject-id": to_subject_id,
                    "relation": "package-reference",
                }
            )
    return edges


def _read_package_json(
    package_json: Path,
    subject_id: str,
    metadata_path: str,
    dependency_failures: list[Json],
) -> Mapping[str, object]:
    try:
        data = json.loads(package_json.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "javascript-typescript",
                subject_id,
                metadata_path,
                "read-error"
                if isinstance(error, UnicodeDecodeError)
                else "parse-error",
                error,
            ),
        )
        return {}
    except OSError as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "javascript-typescript",
                subject_id,
                metadata_path,
                "read-error",
                error,
            ),
        )
        return {}
    if not isinstance(data, Mapping):
        return {}
    return data


def _package_dependencies(package_json: Mapping[str, object]) -> list[str]:
    result: set[str] = set()
    for key in (
        "dependencies",
        "devDependencies",
        "optionalDependencies",
        "peerDependencies",
    ):
        section = package_json.get(key)
        if isinstance(section, Mapping):
            result.update(
                str(name) for name in section if isinstance(name, str)
            )
    return sorted(result)


def _python_dependency_edges(
    repo_root: Path,
    subjects: Mapping[str, _SubjectFacts],
    dependency_failures: list[Json],
) -> list[Json]:
    packages, dependencies = _python_packages_and_dependencies(
        repo_root, subjects, dependency_failures
    )
    return _python_dependency_edge_records(packages, dependencies)


def _python_packages_and_dependencies(
    repo_root: Path,
    subjects: Mapping[str, _SubjectFacts],
    dependency_failures: list[Json],
) -> tuple[dict[str, str], dict[str, list[str]]]:
    packages: dict[str, str] = {}
    dependencies: dict[str, list[str]] = {}
    for subject_id, facts in subjects.items():
        if facts.provider != "python":
            continue
        metadata = _python_project_metadata(
            repo_root, subject_id, facts, dependency_failures
        )
        if metadata is None:
            continue
        project = metadata.get("project")
        if not isinstance(project, Mapping):
            continue
        package_name = project.get("name")
        if isinstance(package_name, str):
            packages[_normalize_package_name(package_name)] = subject_id
        dependencies[subject_id] = _python_dependencies(metadata)
    return packages, dependencies


def _python_dependency_edge_records(
    packages: Mapping[str, str],
    dependencies: Mapping[str, Sequence[str]],
) -> list[Json]:
    edges: list[Json] = []
    seen: set[tuple[str, str, str]] = set()
    for from_subject_id, deps in dependencies.items():
        for dependency in deps:
            dependency_name = _dependency_name(dependency)
            if dependency_name is None:
                continue
            to_subject_id = packages.get(dependency_name)
            if to_subject_id is None or to_subject_id == from_subject_id:
                continue
            key = (from_subject_id, to_subject_id, "package-reference")
            if key in seen:
                continue
            seen.add(key)
            edges.append(
                {
                    "from-subject-id": from_subject_id,
                    "to-subject-id": to_subject_id,
                    "relation": "package-reference",
                }
            )
    return edges


def _python_project_metadata(
    repo_root: Path,
    subject_id: str,
    facts: _SubjectFacts,
    dependency_failures: list[Json],
) -> Mapping[str, object] | None:
    root = str(facts.subject["root"])
    metadata = _read_pyproject(
        repo_root / root / "pyproject.toml",
        subject_id,
        _clean_path(str(Path(root) / "pyproject.toml")),
        dependency_failures,
    )
    project = metadata.get("project")
    if not isinstance(project, Mapping):
        return None
    return metadata


def _python_dependencies(metadata: Mapping[str, object]) -> list[str]:
    project = metadata.get("project")
    build_system = metadata.get("build-system")
    dependencies: list[str] = []
    if isinstance(project, Mapping):
        dependencies.extend(_string_sequence(project.get("dependencies", [])))
    if isinstance(build_system, Mapping):
        dependencies.extend(_string_sequence(build_system.get("requires", [])))
    return sorted(set(dependencies))


def _string_sequence(value: object) -> list[str]:
    if not isinstance(value, Sequence) or isinstance(value, str):
        return []
    return [item for item in value if isinstance(item, str)]


def _read_pyproject(
    pyproject: Path,
    subject_id: str,
    metadata_path: str,
    dependency_failures: list[Json],
) -> Mapping[str, object]:
    try:
        return tomllib.loads(pyproject.read_text(encoding="utf-8"))
    except (tomllib.TOMLDecodeError, UnicodeDecodeError) as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "python",
                subject_id,
                metadata_path,
                "read-error"
                if isinstance(error, UnicodeDecodeError)
                else "parse-error",
                error,
            ),
        )
        return {}
    except OSError as error:
        _record_dependency_failure(
            dependency_failures,
            _dependency_failure(
                "python", subject_id, metadata_path, "read-error", error
            ),
        )
        return {}


def _dependency_failure(
    provider: str,
    subject_id: str,
    path: str,
    reason: str,
    error: Exception,
) -> Json:
    return {
        "provider": provider,
        "subject-id": subject_id,
        "path": _clean_path(path),
        "reason": reason,
        "message": str(error),
    }


def _record_dependency_failure(
    dependency_failures: list[Json],
    failure: Mapping[str, object],
) -> None:
    dependency_failures.append(dict(failure))


def _dependency_name(dependency: str) -> str | None:
    match = re.match(r"\s*([A-Za-z0-9][A-Za-z0-9_.-]*)", dependency)
    if match is None:
        return None
    return _normalize_package_name(match.group(1))


def _normalize_package_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def _provider_descriptors(
    provider_subjects: Sequence[Mapping[str, object]],
) -> list[Json]:
    descriptors: list[Json] = []
    for subject in provider_subjects:
        descriptor = cast("Mapping[str, object]", subject["descriptor"])
        descriptor_path = descriptor.get("path")
        if not isinstance(descriptor_path, str):
            continue
        descriptors.append(
            {
                "descriptor-path": descriptor_path,
                "descriptor-identity": descriptor.get("identity"),
                "owner-subject-id": subject["subject-id"],
                "source": "ecosystem-provider",
            },
        )
    return sorted(descriptors, key=lambda item: str(item["descriptor-path"]))


def _empty_target_catalog() -> Json:
    return {"catalog-id": None, "descriptor-paths": [], "entries": []}


def _provider_target_catalog(
    provider_id: str,
    snapshot: AuthoringSnapshot,
    provider_subjects: Sequence[Mapping[str, object]],
) -> Json:
    descriptor_paths = [
        cast("Mapping[str, object]", subject["descriptor"]).get("path")
        for subject in provider_subjects
    ]
    descriptor_paths = [
        path for path in descriptor_paths if isinstance(path, str)
    ]
    entries: list[Json] = []
    projects_by_descriptor = {
        project.descriptor_path: project
        for project in snapshot.projects.values()
    }
    for descriptor_path in sorted(descriptor_paths):
        project = projects_by_descriptor.get(descriptor_path)
        if project is None:
            continue
        for profile in sorted(project.profiles):
            entries.extend(_target_catalog_entries(project, profile))
    if not entries:
        return _empty_target_catalog()
    return {
        "catalog-id": f"catalog-{provider_id}",
        "descriptor-paths": sorted(
            {str(entry["descriptor-path"]) for entry in entries}
        ),
        "entries": sorted(
            entries,
            key=_catalog_entry_sort_key,
        ),
    }


def _target_catalog_entries(
    project: ProjectDescriptor, profile: str
) -> list[Json]:
    descriptor_artifact_ids = _profile_artifact_ids(project, profile)
    entries: list[Json] = []
    for descriptor_artifact_id in descriptor_artifact_ids:
        artifact = project.artifacts_by_id.get(descriptor_artifact_id)
        if artifact is None:
            continue
        variant = _variant_for_artifact(project, artifact.variant_id)
        variant_dimensions = dict(variant.dimensions) if variant else {}
        entries.append(
            {
                "descriptor-path": project.descriptor_path,
                "profile": profile,
                "artifact": {
                    "kind-family": artifact.kind_family,
                    "concrete-kind": artifact.concrete_kind,
                    "logical-artifact-role": artifact.role,
                    "variant-dimensions": variant_dimensions,
                    "expected-artifact-refs": [
                        _artifact_ref(
                            project.project_id,
                            profile,
                            descriptor_artifact_id,
                        ),
                    ],
                },
                "release-receipt": {
                    "expected-family": project.ecosystem,
                    "logical-receipt-role": "release-plan",
                    "variant-dimensions": variant_dimensions,
                },
            },
        )
    return sorted(entries, key=_catalog_entry_sort_key)


def _catalog_entry_sort_key(entry: Mapping[str, object]) -> tuple[object, ...]:
    artifact = entry.get("artifact")
    release_receipt = entry.get("release-receipt")
    if not isinstance(artifact, Mapping):
        artifact = {}
    if not isinstance(release_receipt, Mapping):
        release_receipt = {}
    refs = artifact.get("expected-artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        refs = ()
    return (
        str(entry.get("descriptor-path")),
        str(entry.get("profile")),
        str(artifact.get("kind-family")),
        str(artifact.get("concrete-kind")),
        str(artifact.get("logical-artifact-role")),
        tuple(str(ref) for ref in refs),
        str(release_receipt.get("expected-family")),
        str(release_receipt.get("logical-receipt-role")),
        canonical_json_bytes(artifact.get("variant-dimensions", {})),
        canonical_json_bytes(release_receipt.get("variant-dimensions", {})),
    )


def _profile_artifact_ids(
    project: ProjectDescriptor,
    profile: str,
) -> list[str]:
    artifact_ids = [
        artifact_id
        for target in project.profiles.get(profile, ())
        for artifact_id in target.artifacts
    ]
    return sorted(set(artifact_ids))


def _variant_for_artifact(
    project: ProjectDescriptor,
    variant_id: str,
) -> Variant | None:
    return next(
        (variant for variant in project.variants if variant.id == variant_id),
        None,
    )


def _classify_request(
    request: NormalizedCiValidationRequest,
    facts: _PlanningFacts,
) -> list[_Impact]:
    if request.mode == "scheduled_full":
        return []
    changed_files = _changed_files(request)
    impacts: list[_Impact] = []
    for path in changed_files:
        impacts.append(_classify_path(path, facts))
    return _coalesce_impacts(impacts)


def _classify_path(path: str, facts: _PlanningFacts) -> _Impact:
    path = _clean_path(path)
    for pattern, surface in _WORKFLOW_RELEASE_SURFACE_GLOBS:
        if fnmatch.fnmatchcase(path, pattern):
            return _impact(
                category="workflow-release-infrastructure",
                paths=[path],
                source_rule=f"workflow-release-{surface}",
                rationale=(
                    f"Changed path affects the {surface} "
                    "workflow-release surface."
                ),
                coverage_target={"type": "tooling-surface", "id": surface},
                descriptor_validation=surface in _ALL_DESCRIPTOR_SURFACES,
                downstream_expansion=False,
                broad_expansion=True,
            )
    subject = _owning_subject(path, facts.subjects)
    if subject is not None:
        subject_id = str(subject.subject["subject-id"])
        return _impact(
            category="project-scoped",
            paths=[path],
            source_rule="subject-root",
            rationale=(
                "Changed path is owned by a discovered validation subject."
            ),
            coverage_target={"type": "subject", "id": subject_id},
            descriptor_validation=True,
            downstream_expansion=True,
            broad_expansion=False,
        )
    if path in _GLOBAL_PATHS:
        return _impact(
            category="global",
            paths=[path],
            source_rule="root-global-configuration",
            rationale=(
                "Changed root configuration can affect multiple ecosystems."
            ),
            coverage_target={"type": "global", "id": None},
            descriptor_validation=True,
            downstream_expansion=False,
            broad_expansion=True,
        )
    for pattern, ecosystem, rule in _ECOSYSTEM_PATH_GLOBS:
        if fnmatch.fnmatchcase(path, pattern):
            return _impact(
                category="ecosystem-scoped",
                paths=[path],
                source_rule=rule,
                rationale=(
                    f"Changed path affects {ecosystem} workspace configuration."
                ),
                coverage_target={"type": "ecosystem", "id": ecosystem},
                descriptor_validation=True,
                downstream_expansion=False,
                broad_expansion=True,
            )
    if _is_known_non_impacting(path):
        return _impact(
            category="known-non-impacting",
            paths=[path],
            source_rule="known-non-impacting-docs",
            rationale=(
                "Changed path is documented as non-impacting for "
                "validation scope."
            ),
            coverage_target={"type": "none", "id": None},
            descriptor_validation=False,
            downstream_expansion=False,
            broad_expansion=False,
            diagnostic=DiagnosticFamily.KNOWN_NON_IMPACTING.value,
        )
    return _impact(
        category="unknown",
        paths=[path],
        source_rule="unknown-path",
        rationale=(
            "Changed path does not match a deterministic CI validation "
            "classifier rule."
        ),
        coverage_target={"type": "none", "id": None},
        descriptor_validation=False,
        downstream_expansion=False,
        broad_expansion=False,
        diagnostic=DiagnosticFamily.UNKNOWN_CHANGE.value,
    )


def _impact(  # noqa: PLR0913
    *,
    category: ImpactCategory,
    paths: Sequence[str],
    source_rule: str,
    rationale: str,
    coverage_target: Json,
    descriptor_validation: bool,
    downstream_expansion: bool,
    broad_expansion: bool,
    diagnostic: str | None = None,
) -> _Impact:
    impact_id = _stable_id(
        "impact",
        {
            "category": category,
            "coverage-target": coverage_target,
            "matched-paths": sorted(paths),
            "source-rule": source_rule,
        },
    )
    return _Impact(
        impact_id=impact_id,
        category=category,
        paths=tuple(sorted(paths)),
        source_rule=source_rule,
        rationale=rationale,
        coverage_target=coverage_target,
        descriptor_validation=descriptor_validation,
        downstream_expansion=downstream_expansion,
        broad_expansion=broad_expansion,
        diagnostic=diagnostic,
    )


def _coalesce_impacts(impacts: Sequence[_Impact]) -> list[_Impact]:
    result: list[_Impact] = []
    for index, impact in enumerate(sorted(impacts, key=_impact_sort_key)):
        result.append(_ordered_impact(impact, index))
    return result


def _ordered_impact(impact: _Impact, index: int) -> _Impact:
    return _Impact(
        impact_id=f"impact-{index:04d}-{impact.impact_id.removeprefix('impact-')}",
        category=impact.category,
        paths=impact.paths,
        source_rule=impact.source_rule,
        rationale=impact.rationale,
        coverage_target=impact.coverage_target,
        descriptor_validation=impact.descriptor_validation,
        downstream_expansion=impact.downstream_expansion,
        broad_expansion=impact.broad_expansion,
        diagnostic=impact.diagnostic,
    )


def _impact_sort_key(impact: _Impact) -> tuple[tuple[str, ...], str]:
    return (impact.paths, impact.impact_id)


def _fail_closed_impacts_from(
    impacts: Sequence[_Impact],
    diagnostic_code: str,
) -> list[_Impact]:
    return [
        _Impact(
            impact_id=impact.impact_id,
            category="unknown",
            paths=impact.paths,
            source_rule=f"{impact.source_rule}-fail-closed",
            rationale=(
                "Changed path requires fail-closed planning because "
                "supporting facts were incomplete."
            ),
            coverage_target={"type": "none", "id": None},
            descriptor_validation=False,
            downstream_expansion=False,
            broad_expansion=False,
            diagnostic=diagnostic_code,
        )
        for impact in impacts
    ]


def _resolve_scope(
    request: NormalizedCiValidationRequest,
    impacts: Sequence[_Impact],
    facts: _PlanningFacts,
) -> _ResolvedScope:
    scope = _ResolvedScope(
        set(), set(), set(), set(), {}, {}, [], [], [], set()
    )
    if request.mode == "scheduled_full":
        _select_full_scope(scope, facts, scheduled=True)
        return _scope_with_dependency_failure_diagnostics(scope, facts)
    if not impacts:
        scope.lightweight_only = True
        return scope
    if all(impact.category == "known-non-impacting" for impact in impacts):
        scope.lightweight_only = True
        scope.lightweight_source_impacts.update(
            impact.impact_id for impact in impacts
        )
        return scope
    for impact in impacts:
        if impact.category == "project-scoped":
            _resolve_project_scope(scope, impact, facts)
        elif impact.category == "known-non-impacting":
            scope.lightweight_source_impacts.add(impact.impact_id)
        elif impact.category == "ecosystem-scoped":
            ecosystem = cast("str", impact.coverage_target["id"])
            _select_ecosystem(scope, facts, ecosystem, [impact.impact_id])
            _add_broad_expansion(scope, impact, [ecosystem])
        elif impact.category == "global":
            _select_full_scope(scope, facts, scheduled=False, impact=impact)
        elif impact.category == "workflow-release-infrastructure":
            _resolve_infrastructure_scope(scope, impact, facts)
    return _scope_with_dependency_failure_diagnostics(scope, facts)


def _scope_with_dependency_failure_diagnostics(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
) -> _ResolvedScope:
    if not scope.diagnostics:
        scope.diagnostics.extend(
            _selected_dependency_failure_diagnostics(scope, facts)
        )
    return scope


def _resolve_project_scope(
    scope: _ResolvedScope,
    impact: _Impact,
    facts: _PlanningFacts,
) -> None:
    subject_id = cast("str", impact.coverage_target["id"])
    subject = facts.subjects.get(subject_id)
    if subject is None or subject.subject["activity-status"] != "active":
        scope.diagnostics.append(
            _diagnostic(
                code=DiagnosticFamily.SUBJECT_UNRESOLVED.value,
                detail=None,
                message=(
                    "changed path could not resolve to an active "
                    "validation subject"
                ),
                source_type="impact",
                source_id=impact.impact_id,
                ordinal=len(scope.diagnostics) + 1,
            ),
        )
        return
    scope.selected_subject_ids.add(subject_id)
    scope.source_impacts_by_subject.setdefault(subject_id, set()).add(
        impact.impact_id,
    )
    scope.provenance.append(
        _provenance("direct", subject_id, [impact.impact_id])
    )
    provider = subject.provider
    if provider is not None:
        scope.diagnostics.extend(
            _dependency_failure_diagnostics(
                facts, provider, impact.impact_id, len(scope.diagnostics)
            )
        )
        if scope.diagnostics:
            return
    for downstream_id, edge_basis in _downstream_subjects(subject_id, facts):
        scope.selected_subject_ids.add(downstream_id)
        scope.source_impacts_by_subject.setdefault(downstream_id, set()).add(
            impact.impact_id,
        )
        scope.provenance.append(
            _provenance(
                "downstream",
                downstream_id,
                [impact.impact_id],
                direct_subject_id=subject_id,
                dependency_edge_basis=edge_basis,
            ),
        )


def _resolve_infrastructure_scope(
    scope: _ResolvedScope,
    impact: _Impact,
    facts: _PlanningFacts,
) -> None:
    surface = cast("str", impact.coverage_target["id"])
    scope.tooling_surfaces.add(surface)
    scope.source_impacts_by_tooling_surface.setdefault(surface, set()).add(
        impact.impact_id
    )
    if surface in _SCHEDULED_FULL_EQUIVALENT_SURFACES:
        _select_full_scope(scope, facts, scheduled=False, impact=impact)
        return
    if surface == "fact-provider":
        for ecosystem in sorted(_SUPPORTED_ECOSYSTEMS):
            _select_ecosystem(scope, facts, ecosystem, [impact.impact_id])
        _add_broad_expansion(scope, impact, sorted(_SUPPORTED_ECOSYSTEMS))
        return
    if surface in _ALL_DESCRIPTOR_SURFACES:
        _select_all_descriptor_subjects(scope, facts, [impact.impact_id])
        scope.full_scope_descriptor_coverage = True
    if surface in _ARTIFACT_SURFACES:
        _select_all_artifact_subjects(scope, facts, [impact.impact_id])
    if surface == "build-execution":
        _select_all_build_capable_subjects(scope, facts, [impact.impact_id])
    _add_broad_expansion(scope, impact, sorted(_SUPPORTED_ECOSYSTEMS))


def _select_full_scope(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
    *,
    scheduled: bool,
    impact: _Impact | None = None,
) -> None:
    source_impact_ids = [] if impact is None else [impact.impact_id]
    for subject_id, subject in facts.subjects.items():
        if subject.subject["activity-status"] != "active":
            continue
        scope.selected_subject_ids.add(subject_id)
        scope.source_impacts_by_subject.setdefault(subject_id, set()).update(
            source_impact_ids,
        )
        scope.provenance.append(
            _provenance(
                "scheduled-full" if scheduled else "broad-expansion",
                subject_id,
                source_impact_ids,
                broad_expansion_id=None if scheduled else _expansion_id(impact),
                scheduled_full_source=scheduled,
            ),
        )
    scope.tooling_surfaces.update(_TOOLING_SURFACES)
    if impact is not None:
        for surface in _TOOLING_SURFACES:
            scope.source_impacts_by_tooling_surface.setdefault(
                surface, set()
            ).add(impact.impact_id)
    _select_all_descriptor_subjects(scope, facts, source_impact_ids)
    _select_all_artifact_subjects(scope, facts, source_impact_ids)
    scope.full_scope_descriptor_coverage = True
    if impact is not None:
        _add_broad_expansion(scope, impact, sorted(_SUPPORTED_ECOSYSTEMS))


def _select_ecosystem(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
    ecosystem: str,
    source_impact_ids: Sequence[str],
) -> None:
    for subject_id, subject in facts.subjects.items():
        if subject.subject["ecosystem"] != ecosystem:
            continue
        if subject.subject["activity-status"] != "active":
            continue
        scope.selected_subject_ids.add(subject_id)
        scope.source_impacts_by_subject.setdefault(subject_id, set()).update(
            source_impact_ids,
        )
        scope.provenance.append(
            _provenance("broad-expansion", subject_id, source_impact_ids),
        )


def _select_all_descriptor_subjects(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
    source_impact_ids: Sequence[str],
) -> None:
    for subject_id, subject in facts.subjects.items():
        descriptor = cast("Mapping[str, object]", subject.subject["descriptor"])
        if (
            subject.subject["activity-status"] == "active"
            and subject.subject["capability-class"] == "descriptor-backed"
            and isinstance(descriptor.get("path"), str)
        ):
            scope.selected_subject_ids.add(subject_id)
            scope.descriptor_subject_ids.add(subject_id)
            scope.source_impacts_by_subject.setdefault(
                subject_id, set()
            ).update(
                source_impact_ids,
            )
            if source_impact_ids:
                scope.provenance.append(
                    _provenance(
                        "broad-expansion", subject_id, source_impact_ids
                    ),
                )


def _select_all_artifact_subjects(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
    source_impact_ids: Sequence[str],
) -> None:
    for subject_id, subject in facts.subjects.items():
        caps = cast("Mapping[str, object]", subject.subject["capabilities"])
        if (
            subject.subject["activity-status"] == "active"
            and subject.subject["capability-class"] == "descriptor-backed"
            and caps.get("release-shaped-artifacts") is True
        ):
            scope.selected_subject_ids.add(subject_id)
            scope.artifact_subject_ids.add(subject_id)
            scope.source_impacts_by_subject.setdefault(
                subject_id, set()
            ).update(
                source_impact_ids,
            )
            if source_impact_ids:
                scope.provenance.append(
                    _provenance(
                        "broad-expansion", subject_id, source_impact_ids
                    ),
                )


def _select_all_build_capable_subjects(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
    source_impact_ids: Sequence[str],
) -> None:
    for subject_id, subject in facts.subjects.items():
        caps = cast("Mapping[str, object]", subject.subject["capabilities"])
        if subject.subject["activity-status"] == "active" and (
            caps.get("build") is True
            or caps.get("release-shaped-artifacts") is True
        ):
            scope.selected_subject_ids.add(subject_id)
            scope.source_impacts_by_subject.setdefault(
                subject_id, set()
            ).update(
                source_impact_ids,
            )
            if source_impact_ids:
                scope.provenance.append(
                    _provenance(
                        "broad-expansion", subject_id, source_impact_ids
                    ),
                )


def _add_broad_expansion(
    scope: _ResolvedScope,
    impact: _Impact,
    ecosystems: Sequence[str],
) -> None:
    scope.broad_expansions.append(
        {
            "expansion-id": _expansion_id(impact),
            "source-impact-id": impact.impact_id,
            "category": "global"
            if impact.category == "global"
            else (
                "workflow-release-infrastructure"
                if impact.category == "workflow-release-infrastructure"
                else "ecosystem"
            ),
            "reason": impact.rationale,
            "resulting-scope": {
                "ecosystems": sorted(set(ecosystems)),
                "subjects": sorted(scope.selected_subject_ids),
                "descriptors": "all-discovered"
                if impact.category
                in {"global", "workflow-release-infrastructure"}
                else "selected",
            },
        },
    )
    expansion_id = _expansion_id(impact)
    for item in scope.provenance:
        if (
            item.get("selection-kind") == "broad-expansion"
            and item.get("broad-expansion-id") is None
        ):
            item["broad-expansion-id"] = expansion_id


def _build_plan_records(
    subjects: Sequence[Mapping[str, object]],
    facts: _PlanningFacts,
    scope: _ResolvedScope,
) -> _PlanRecords:
    records = _PlanRecords([], [], [], [], [], [])
    if scope.lightweight_only:
        if scope.lightweight_source_impacts:
            _add_lightweight_records(
                records, sorted(scope.lightweight_source_impacts)
            )
        return records
    if scope.lightweight_source_impacts:
        _add_lightweight_records(
            records, sorted(scope.lightweight_source_impacts)
        )
    selected = [
        subject
        for subject in subjects
        if subject["selection-status"] == "selected"
    ]
    for subject in selected:
        source_impacts = sorted(
            scope.source_impacts_by_subject.get(
                str(subject["subject-id"]), set()
            ),
        )
        _add_subject_gate(records, subject, source_impacts)
        if subject["capability-class"] == "descriptor-backed":
            descriptor_scope = (
                "all-discovered"
                if scope.full_scope_descriptor_coverage
                else "selected"
            )
            _add_descriptor_records(
                records,
                subject,
                source_impacts,
                descriptor_scope=descriptor_scope,
            )
            caps = cast("Mapping[str, object]", subject["capabilities"])
            if caps.get("release-shaped-artifacts") is True:
                _add_artifact_records(records, subject, facts, source_impacts)
    for surface in sorted(scope.tooling_surfaces):
        _add_tooling_records(
            records,
            surface,
            sorted(scope.source_impacts_by_tooling_surface.get(surface, set())),
        )
    _assign_work_group_dependencies(records)
    return records


def _assign_work_group_dependencies(records: _PlanRecords) -> None:
    """Order executable work groups only by runtime execution prerequisites."""
    gate_group_by_subject = {
        str(cast("Mapping[str, object]", item["coverage-target"])["id"]): str(
            item["work-group-id"]
        )
        for item in records.validation_obligations
        if item.get("kind") == "ecosystem-gate"
    }
    dependencies_by_group: dict[str, set[str]] = {}
    for obligation in records.artifact_obligations:
        work_group_id = str(obligation["work-group-id"])
        dependencies = dependencies_by_group.setdefault(work_group_id, set())
        gate_group_id = gate_group_by_subject.get(str(obligation["subject-id"]))
        if gate_group_id is not None:
            dependencies.add(gate_group_id)
    for group in records.work_groups:
        work_group_id = str(group["work-group-id"])
        if work_group_id in dependencies_by_group:
            group["depends-on"] = sorted(dependencies_by_group[work_group_id])


def _sort_plan_records(records: _PlanRecords) -> None:
    """Sort digest-bearing planner records before contract freezing."""
    records.descriptor_obligations.sort(
        key=lambda item: str(item["descriptor-obligation-id"]),
    )
    records.validation_obligations.sort(
        key=lambda item: str(item["validation-obligation-id"]),
    )
    records.artifact_obligations.sort(
        key=lambda item: str(item["artifact-obligation-id"]),
    )
    records.work_groups.sort(key=lambda item: str(item["work-group-id"]))
    records.evidence_expectations.sort(
        key=lambda item: str(item["evidence-expectation-id"]),
    )
    records.detail_profiles.sort(
        key=lambda item: str(item["detail-profile-id"])
    )


def _add_subject_gate(
    records: _PlanRecords,
    subject: Mapping[str, object],
    source_impacts: Sequence[str],
) -> None:
    capabilities = _planned_capabilities(subject)
    if not capabilities:
        return
    subject_id = str(subject["subject-id"])
    ecosystem = str(subject["ecosystem"])
    target = {"type": "subject", "id": subject_id}
    work_group_id = _work_group_id("ecosystem-gate", target)
    evidence_id = _stable_id("evidence", {"work-group-id": work_group_id})
    obligation_id = _stable_id(
        "validation", {"kind": "ecosystem-gate", "target": target}
    )
    expected = {
        "category": "ecosystem-gate",
        "planned-capabilities": capabilities,
        "detail-profile": None,
        "required": True,
    }
    records.work_groups.append(
        {
            "work-group-id": work_group_id,
            "kind": "ecosystem-gate",
            "coverage-target": target,
            "ecosystem": ecosystem,
            "runner-family": _RUNNER_BY_ECOSYSTEM[ecosystem],
            "selector-variant": None,
            "depends-on": [],
            "expected-evidence": expected,
        },
    )
    records.evidence_expectations.append(
        {
            "evidence-expectation-id": evidence_id,
            "work-group-id": work_group_id,
            "coverage-target": target,
            "category": "ecosystem-gate",
            "planned-capabilities": capabilities,
            "detail-profile": None,
            "required": True,
            "blocking-if-missing": True,
        },
    )
    records.validation_obligations.append(
        {
            "validation-obligation-id": obligation_id,
            "source-impact-ids": sorted(source_impacts),
            "kind": "ecosystem-gate",
            "coverage-target": target,
            "required": True,
            "blocking": True,
            "work-group-id": work_group_id,
            "expected-evidence-id": evidence_id,
        },
    )


def _add_descriptor_records(
    records: _PlanRecords,
    subject: Mapping[str, object],
    source_impacts: Sequence[str],
    *,
    descriptor_scope: str,
) -> None:
    descriptor = cast("Mapping[str, object]", subject["descriptor"])
    descriptor_path = descriptor.get("path")
    if not isinstance(descriptor_path, str):
        return
    target = {"type": "descriptor", "id": descriptor_path}
    work_group_id = _work_group_id("descriptor-validation", target)
    evidence_id = _stable_id("evidence", {"work-group-id": work_group_id})
    obligation_id = _stable_id("descriptor", {"target": target})
    records.work_groups.append(
        {
            "work-group-id": work_group_id,
            "kind": "descriptor-validation",
            "coverage-target": target,
            "ecosystem": None,
            "runner-family": "ubuntu",
            "selector-variant": None,
            "depends-on": [],
            "expected-evidence": {
                "category": "descriptor-validation",
                "planned-capabilities": None,
                "detail-profile": None,
                "required": True,
            },
        },
    )
    records.evidence_expectations.append(
        {
            "evidence-expectation-id": evidence_id,
            "work-group-id": work_group_id,
            "coverage-target": target,
            "category": "descriptor-validation",
            "planned-capabilities": None,
            "detail-profile": None,
            "required": True,
            "blocking-if-missing": True,
        },
    )
    records.descriptor_obligations.append(
        {
            "descriptor-obligation-id": obligation_id,
            "source-impact-ids": sorted(source_impacts),
            "descriptor-scope": descriptor_scope,
            "coverage-target": target,
            "required": True,
            "blocking": True,
            "work-group-id": work_group_id,
            "expected-evidence-id": evidence_id,
        },
    )


def _add_artifact_records(
    records: _PlanRecords,
    subject: Mapping[str, object],
    facts: _PlanningFacts,
    source_impacts: Sequence[str],
) -> None:
    descriptor = cast("Mapping[str, object]", subject["descriptor"])
    descriptor_path = descriptor.get("path")
    if not isinstance(descriptor_path, str):
        return
    entries = _catalog_entries_for_descriptor(facts.providers, descriptor_path)
    if not entries:
        return
    for entry in entries:
        profile = str(entry["profile"])
        artifact = dict(cast("Mapping[str, object]", entry["artifact"]))
        release_receipt = dict(
            cast("Mapping[str, object]", entry["release-receipt"])
        )
        artifact_obligation_id = _stable_id(
            "artifact",
            {
                "descriptor": descriptor_path,
                "profile": profile,
                "artifact": artifact,
                "release-receipt": release_receipt,
            },
        )
        target = {"type": "artifact-obligation", "id": artifact_obligation_id}
        work_group_id = _work_group_id("release-shaped-artifact", target)
        evidence_id = _stable_id("evidence", {"work-group-id": work_group_id})
        validation_id = _stable_id(
            "validation", {"kind": "release-shaped-artifact", "target": target}
        )
        records.work_groups.append(
            {
                "work-group-id": work_group_id,
                "kind": "release-shaped-artifact",
                "coverage-target": target,
                "ecosystem": subject["ecosystem"],
                "runner-family": _release_shaped_runner_family(
                    str(subject["ecosystem"]),
                    artifact,
                ),
                "selector-variant": None,
                "depends-on": [],
                "expected-evidence": {
                    "category": "release-shaped-artifact",
                    "planned-capabilities": None,
                    "detail-profile": None,
                    "required": True,
                },
            },
        )
        records.evidence_expectations.append(
            {
                "evidence-expectation-id": evidence_id,
                "work-group-id": work_group_id,
                "coverage-target": target,
                "category": "release-shaped-artifact",
                "planned-capabilities": None,
                "detail-profile": None,
                "required": True,
                "blocking-if-missing": True,
            },
        )
        records.validation_obligations.append(
            {
                "validation-obligation-id": validation_id,
                "source-impact-ids": sorted(source_impacts),
                "kind": "release-shaped-artifact",
                "coverage-target": target,
                "required": True,
                "blocking": True,
                "work-group-id": work_group_id,
                "expected-evidence-id": evidence_id,
            },
        )
        records.artifact_obligations.append(
            {
                "artifact-obligation-id": artifact_obligation_id,
                "source-impact-ids": sorted(source_impacts),
                "subject-id": subject["subject-id"],
                "descriptor-path": descriptor_path,
                "profile-coverage": [profile],
                "artifact": artifact,
                "release-receipt": release_receipt,
                "credential-posture": "credential-free",
                "expected-evidence-category": "release-shaped-artifact",
                "required": True,
                "blocking": True,
                "validation-obligation-id": validation_id,
                "work-group-id": work_group_id,
                "expected-evidence-id": evidence_id,
            },
        )


def _release_shaped_runner_family(
    ecosystem: str,
    artifact: Mapping[str, object],
) -> str:
    """Return the runner family that can materialize one release artifact."""
    if ecosystem == "dotnet":
        dimensions = artifact.get("variant-dimensions", {})
        if isinstance(dimensions, Mapping):
            os_dimension = dimensions.get("os")
            rid = dimensions.get("rid")
            if os_dimension == "windows" or (
                isinstance(rid, str) and rid.startswith("win-")
            ):
                return "windows"
            if os_dimension == "linux" or (
                isinstance(rid, str) and rid.startswith("linux-")
            ):
                return "ubuntu"
            if os_dimension == "macos" or (
                isinstance(rid, str) and rid.startswith("osx-")
            ):
                return "macos"
    return _RUNNER_BY_ECOSYSTEM[ecosystem]


def _add_tooling_records(
    records: _PlanRecords,
    surface: str,
    source_impacts: Sequence[str],
) -> None:
    target = {"type": "tooling-surface", "id": surface}
    work_group_id = _work_group_id("workflow-release-tooling", target)
    evidence_id = _stable_id("evidence", {"work-group-id": work_group_id})
    profile_id = _stable_id(
        "profile", {"category": "workflow-release-tooling", "target": target}
    )
    obligation_id = _stable_id(
        "validation", {"kind": "workflow-release-tooling", "target": target}
    )
    records.work_groups.append(
        {
            "work-group-id": work_group_id,
            "kind": "workflow-release-tooling",
            "coverage-target": target,
            "ecosystem": None,
            "runner-family": "ubuntu",
            "selector-variant": None,
            "depends-on": [],
            "expected-evidence": {
                "category": "workflow-release-tooling",
                "planned-capabilities": None,
                "detail-profile": profile_id,
                "required": True,
            },
        },
    )
    records.evidence_expectations.append(
        {
            "evidence-expectation-id": evidence_id,
            "work-group-id": work_group_id,
            "coverage-target": target,
            "category": "workflow-release-tooling",
            "planned-capabilities": None,
            "detail-profile": profile_id,
            "required": True,
            "blocking-if-missing": True,
        },
    )
    records.detail_profiles.append(
        {
            "detail-profile-id": profile_id,
            "category": "workflow-release-tooling",
            "coverage-target": target,
            "required-subchecks": [
                {
                    "subcheck-id": "tooling-contract",
                    "check-kind": "contract",
                    "blocking": True,
                    "description": (
                        f"Validate workflow-release {surface} tooling contract."
                    ),
                },
            ],
        },
    )
    records.validation_obligations.append(
        {
            "validation-obligation-id": obligation_id,
            "source-impact-ids": sorted(source_impacts),
            "kind": "workflow-release-tooling",
            "coverage-target": target,
            "required": True,
            "blocking": True,
            "work-group-id": work_group_id,
            "expected-evidence-id": evidence_id,
        },
    )


def _add_lightweight_records(
    records: _PlanRecords,
    source_impacts: Sequence[str],
) -> None:
    target = {"type": "lightweight-policy", "id": "known-non-impacting"}
    work_group_id = _work_group_id("lightweight-preflight", target)
    evidence_id = _stable_id("evidence", {"work-group-id": work_group_id})
    profile_id = _stable_id(
        "profile", {"category": "lightweight-preflight", "target": target}
    )
    records.work_groups.append(
        {
            "work-group-id": work_group_id,
            "kind": "lightweight-preflight",
            "coverage-target": target,
            "ecosystem": None,
            "runner-family": "ubuntu",
            "selector-variant": None,
            "depends-on": [],
            "expected-evidence": {
                "category": "lightweight-preflight",
                "planned-capabilities": None,
                "detail-profile": profile_id,
                "required": True,
            },
        },
    )
    records.evidence_expectations.append(
        {
            "evidence-expectation-id": evidence_id,
            "work-group-id": work_group_id,
            "coverage-target": target,
            "category": "lightweight-preflight",
            "planned-capabilities": None,
            "detail-profile": profile_id,
            "required": True,
            "blocking-if-missing": True,
        },
    )
    records.detail_profiles.append(
        {
            "detail-profile-id": profile_id,
            "category": "lightweight-preflight",
            "coverage-target": target,
            "required-subchecks": [
                {
                    "subcheck-id": "known-non-impacting-policy",
                    "check-kind": "policy",
                    "blocking": True,
                    "description": (
                        "Verify changed paths remain within known "
                        "non-impacting policy."
                    ),
                },
            ],
        },
    )
    records.validation_obligations.append(
        {
            "validation-obligation-id": _stable_id(
                "validation",
                {"kind": "lightweight-preflight", "target": target},
            ),
            "source-impact-ids": sorted(source_impacts),
            "kind": "lightweight-preflight",
            "coverage-target": target,
            "required": True,
            "blocking": True,
            "work-group-id": work_group_id,
            "expected-evidence-id": evidence_id,
        },
    )


def _classification(
    impacts: Sequence[_Impact],
    broad_expansions: Sequence[Mapping[str, object]],
    provenance: Sequence[Mapping[str, object]],
    *,
    lightweight_only: bool,
) -> Json:
    return {
        "impacts": [_impact_record(impact) for impact in impacts],
        "broad-expansions": _dedupe_records(broad_expansions, "expansion-id"),
        "subject-selection-provenance": _dedupe_records(
            provenance, "provenance-id"
        ),
        "subsumptions": [],
        "lightweight-only": lightweight_only,
    }


def _impact_record(impact: _Impact) -> Json:
    return {
        "impact-id": impact.impact_id,
        "category": impact.category,
        "matched-paths": list(impact.paths),
        "source-rule": impact.source_rule,
        "rationale": impact.rationale,
        "coverage-target": impact.coverage_target,
        "requires": {
            "descriptor-validation": impact.descriptor_validation,
            "downstream-expansion": impact.downstream_expansion,
            "broad-expansion": impact.broad_expansion,
            "diagnostic": impact.diagnostic,
        },
    }


def _impact_diagnostics(impacts: Sequence[_Impact]) -> list[Json]:
    diagnostics: list[Json] = []
    for impact in impacts:
        if impact.category == "unknown":
            diagnostics.append(
                _diagnostic(
                    code=DiagnosticFamily.UNKNOWN_CHANGE.value,
                    detail=None,
                    message=(
                        "changed path is not covered by CI validation "
                        "classification rules"
                    ),
                    source_type="impact",
                    source_id=impact.impact_id,
                    ordinal=len(diagnostics) + 1,
                ),
            )
    return diagnostics


def _dependency_failure_diagnostics(
    facts: _PlanningFacts,
    provider: str,
    source_impact_id: str | None,
    existing_diagnostic_count: int,
) -> list[Json]:
    failures = [
        dict(item)
        for item in facts.dependency_failures
        if item.get("provider") == provider
    ]
    if not failures:
        return []
    return [
        _diagnostic(
            code=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
            detail=None,
            message=(
                "dependency fact provider could not produce complete "
                "dependency closure"
            ),
            source_type="impact"
            if source_impact_id is not None
            else "fact-provider",
            source_id=source_impact_id
            if source_impact_id is not None
            else provider,
            ordinal=existing_diagnostic_count + index + 1,
            extra={
                "provider": provider,
                "dependency-failures": failures,
            },
        )
        for index, _failure in enumerate(failures[:1])
    ]


def _selected_dependency_failure_diagnostics(
    scope: _ResolvedScope,
    facts: _PlanningFacts,
) -> list[Json]:
    source_impacts_by_provider: dict[str, set[str]] = {}
    for subject_id in sorted(scope.selected_subject_ids):
        subject = facts.subjects.get(subject_id)
        if subject is None or subject.provider is None:
            continue
        source_impacts_by_provider.setdefault(subject.provider, set()).update(
            scope.source_impacts_by_subject.get(subject_id, set())
        )
    diagnostics: list[Json] = []
    for provider, source_impacts in sorted(source_impacts_by_provider.items()):
        source_impact_id = next(iter(sorted(source_impacts)), None)
        diagnostics.extend(
            _dependency_failure_diagnostics(
                facts,
                provider,
                source_impact_id,
                len(diagnostics),
            )
        )
    return diagnostics


def _capability_diagnostics(
    subjects: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
) -> list[Json]:
    gate_targets = {
        cast("Mapping[str, object]", item["coverage-target"]).get("id")
        for item in validation_obligations
        if item.get("kind") == "ecosystem-gate"
    }
    diagnostics: list[Json] = []
    for subject in subjects:
        if subject.get("selection-status") != "selected":
            continue
        if subject.get("capability-class") != "validation-only":
            continue
        capabilities = _planned_capabilities(subject)
        if not capabilities:
            diagnostics.append(
                _diagnostic(
                    code=DiagnosticFamily.NO_VALIDATION_CAPABILITY.value,
                    detail=None,
                    message=(
                        "selected validation-only subject has no executable "
                        "validation capability"
                    ),
                    source_type="subject",
                    source_id=str(subject["subject-id"]),
                    ordinal=len(diagnostics) + 1,
                ),
            )
        elif subject["subject-id"] not in gate_targets:
            diagnostics.append(
                _diagnostic(
                    code=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
                    detail=None,
                    message=(
                        "selected validation-only subject lacks a planned "
                        "ecosystem gate"
                    ),
                    source_type="subject",
                    source_id=str(subject["subject-id"]),
                    ordinal=len(diagnostics) + 1,
                ),
            )
    return diagnostics


def _diagnostic(  # noqa: PLR0913
    *,
    code: str,
    detail: str | None,
    message: str,
    source_type: str,
    source_id: str | None,
    ordinal: int,
    extra: Mapping[str, object] | None = None,
) -> Json:
    severity = (
        DiagnosticSeverity.INFO.value
        if code == DiagnosticFamily.KNOWN_NON_IMPACTING.value
        else DiagnosticSeverity.FAIL_CLOSED.value
    )
    effect = (
        DiagnosticVerdictEffect.NONE.value
        if code == DiagnosticFamily.KNOWN_NON_IMPACTING.value
        else DiagnosticVerdictEffect.FAIL_CLOSED.value
    )
    diagnostic = dict(
        ci_validation_diagnostic(
            diagnostic_id=_stable_id(
                "diag",
                {
                    "code": code,
                    "detail": detail,
                    "source-type": source_type,
                    "source-id": source_id,
                    "ordinal": ordinal,
                },
            ),
            code=code,
            detail=detail,
            message=message,
            source_type=source_type,
            source_id=source_id,
            severity=severity,
            verdict_effect=effect,
        ),
    )
    if extra:
        diagnostic["message"] = (
            f"{message}: {json.dumps(dict(extra), sort_keys=True)}"
        )
    return diagnostic


def _subjects_with_selection(
    subjects: Mapping[str, _SubjectFacts],
    selected_subject_ids: set[str],
) -> list[Json]:
    result: list[Json] = []
    for subject_id, facts in subjects.items():
        subject = dict(facts.subject)
        if subject_id in selected_subject_ids:
            subject["selection-status"] = "selected"
        result.append(subject)
    return sorted(result, key=lambda item: str(item["subject-id"]))


def _downstream_subjects(
    subject_id: str,
    facts: _PlanningFacts,
) -> list[tuple[str, list[Json]]]:
    reverse: dict[str, list[Json]] = {}
    for edge in facts.dependency_edges:
        reverse.setdefault(str(edge["to-subject-id"]), []).append(edge)
    result: list[tuple[str, list[Json]]] = []
    visited = {subject_id}
    queue: list[tuple[str, list[Json]]] = [(subject_id, [])]
    while queue:
        current, path_basis = queue.pop(0)
        for edge in sorted(
            reverse.get(current, []),
            key=lambda item: (
                str(item["from-subject-id"]),
                str(item["to-subject-id"]),
                str(item.get("relation", "")),
            ),
        ):
            downstream = str(edge["from-subject-id"])
            if downstream in visited:
                continue
            visited.add(downstream)
            downstream_basis = [*path_basis, dict(edge)]
            queue.append((downstream, downstream_basis))
            result.append((downstream, downstream_basis))
    return result


def _provenance(  # noqa: PLR0913
    selection_kind: str,
    subject_id: str,
    source_impact_ids: Sequence[str],
    *,
    direct_subject_id: str | None = None,
    dependency_edge_basis: Sequence[Mapping[str, object]] = (),
    broad_expansion_id: str | None = None,
    scheduled_full_source: bool = False,
) -> Json:
    payload = {
        "subject-id": subject_id,
        "selection-kind": selection_kind,
        "source-impact-ids": sorted(source_impact_ids),
        "direct-subject-id": direct_subject_id,
        "dependency-edge-basis": [dict(item) for item in dependency_edge_basis],
        "broad-expansion-id": broad_expansion_id,
        "scheduled-full-source": scheduled_full_source,
    }
    return {"provenance-id": _stable_id("prov", payload), **payload}


def _planned_capabilities(subject: Mapping[str, object]) -> list[str]:
    capabilities = cast("Mapping[str, object]", subject["capabilities"])
    return [
        capability
        for capability in PLANNED_CAPABILITY_ORDER
        if capabilities.get(capability) is True
    ]


def _catalog_entries_for_descriptor(
    providers: Sequence[Mapping[str, object]],
    descriptor_path: str,
) -> list[Mapping[str, object]]:
    entries: list[Mapping[str, object]] = []
    for provider in providers:
        catalog = provider.get("target-catalog")
        if not isinstance(catalog, Mapping):
            continue
        for entry in cast(
            "Sequence[Mapping[str, object]]", catalog.get("entries", [])
        ):
            if entry.get("descriptor-path") == descriptor_path:
                entries.append(entry)
    return sorted(entries, key=lambda item: str(item["profile"]))


def _owning_subject(
    path: str, subjects: Mapping[str, _SubjectFacts]
) -> _SubjectFacts | None:
    candidates = [
        subject
        for subject in subjects.values()
        if _path_is_under(path, str(subject.subject["root"]))
    ]
    if not candidates:
        return None
    return max(candidates, key=lambda item: len(str(item.subject["root"])))


def _path_is_under(path: str, root: str) -> bool:
    path = _clean_path(path)
    root = _clean_path(root)
    return path == root or path.startswith(f"{root}/")


def _is_known_non_impacting(path: str) -> bool:
    return any(
        fnmatch.fnmatchcase(path, pattern)
        for pattern in _KNOWN_NON_IMPACTING_GLOBS
    )


def _changed_files(request: NormalizedCiValidationRequest) -> list[str]:
    changed = _affected_range(request).get("changed-files")
    if not isinstance(changed, Sequence) or isinstance(changed, str | bytes):
        return []
    return [_clean_path(str(item)) for item in changed]


def _affected_range(
    request: NormalizedCiValidationRequest,
) -> Mapping[str, object]:
    value = request.projection.get("affected-range")
    return (
        cast("Mapping[str, object]", value)
        if isinstance(value, Mapping)
        else {}
    )


def _validation_tree_sha(request: NormalizedCiValidationRequest) -> str:
    tree = request.projection["validation-tree"]
    if not isinstance(tree, Mapping):
        message = "validation-tree must be an object"
        raise TypeError(message)
    return str(tree["commit-sha"])


def _plan_id(request: NormalizedCiValidationRequest) -> str:
    return f"plan-{request.run_id}-{request.run_attempt}"


def _work_group_id(kind: str, target: Mapping[str, object]) -> str:
    return _stable_id(
        "wg",
        {
            "kind": kind,
            "coverage-target": dict(target),
            "selector-variant": None,
        },
    )


def _expansion_id(impact: _Impact | None) -> str | None:
    if impact is None:
        return None
    return _stable_id("expansion", {"impact-id": impact.impact_id})


def _stable_id(prefix: str, payload: Mapping[str, object]) -> str:
    digest = canonical_json_digest(
        {
            "api-version": "three.ci.validation.planner-id/v1alpha1",
            "prefix": prefix,
            "payload": dict(payload),
        },
    )
    return f"{prefix}-{digest[:24]}"


def _subject_id(ecosystem: str, root: str) -> str:
    return f"{ecosystem}.{_safe_token(root)}"


def _safe_token(value: str) -> str:
    token = "".join(ch if ch.isalnum() else "-" for ch in value.lower())
    token = "-".join(part for part in token.split("-") if part)
    if len(token) <= _MAX_READABLE_TOKEN_LENGTH:
        return token or "root"
    digest = canonical_json_digest({"value": value})[:16]
    return f"{token[:55].rstrip('-')}-{digest}"


def _artifact_ref(project_id: str, profile: str, artifact_id: str) -> str:
    return "ci-validation/artifacts/" + "/".join(
        [
            _safe_token(project_id),
            _safe_token(profile),
            f"{_safe_token(artifact_id)}.artifact",
        ],
    )


def _dedupe_records(
    records: Sequence[Mapping[str, object]], key: str
) -> list[Json]:
    deduped: dict[str, Json] = {}
    for record in records:
        deduped[str(record[key])] = dict(record)
    return [deduped[item] for item in sorted(deduped)]


def _normalized_project_ecosystem(
    project: ProjectDescriptor, repo_root: Path
) -> str:
    if project.ecosystem == "node":
        return (
            "typescript"
            if _has_typescript_config(
                repo_root / project.release_root,
                project.release_root,
            )
            else "javascript"
        )
    if project.ecosystem in _SUPPORTED_ECOSYSTEMS:
        return project.ecosystem
    return "ruby" if project.ecosystem == "ruby" else "other"


def _uv_workspace_roots(repo_root: Path) -> list[str]:
    pyproject = repo_root / "pyproject.toml"
    if not pyproject.exists():
        return []
    try:
        data = tomllib.loads(pyproject.read_text(encoding="utf-8"))
    except (OSError, tomllib.TOMLDecodeError, UnicodeDecodeError) as error:
        raise _fact_discovery_error(
            provider="python",
            path="pyproject.toml",
            message="UV workspace facts could not be discovered",
            reason=type(error).__name__,
        ) from error
    members = (
        data.get("tool", {})
        .get("uv", {})
        .get("workspace", {})
        .get("members", [])
    )
    if not isinstance(members, list):
        return []
    return sorted(
        _clean_path(str(item)) for item in members if isinstance(item, str)
    )


def _pnpm_workspace_roots(repo_root: Path) -> list[str]:
    workspace = repo_root / "pnpm-workspace.yaml"
    if not workspace.exists():
        return []
    try:
        data = yaml.safe_load(workspace.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, yaml.YAMLError) as error:
        raise _fact_discovery_error(
            provider="javascript-typescript",
            path="pnpm-workspace.yaml",
            message="PNPM workspace facts could not be discovered",
            reason=type(error).__name__,
        ) from error
    if not isinstance(data, Mapping):
        return []
    packages = data.get("packages")
    if not isinstance(packages, Sequence) or isinstance(packages, str | bytes):
        return []
    roots: set[str] = set()
    for pattern in packages:
        if not isinstance(pattern, str):
            continue
        cleaned = _clean_path(pattern)
        if "*" in cleaned or "?" in cleaned or "[" in cleaned:
            matches = sorted(
                path
                for path in repo_root.glob(cleaned)
                if path.is_dir() and (path / "package.json").exists()
            )
            roots.update(
                _clean_path(path.relative_to(repo_root).as_posix())
                for path in matches
            )
            continue
        if (repo_root / cleaned / "package.json").exists():
            roots.add(cleaned)
    return sorted(roots)


def _dotnet_project_roots(tracked_files: Sequence[str]) -> list[str]:
    roots = {
        str(PurePosixPath(path).parent)
        for path in tracked_files
        if path.endswith((".csproj", ".fsproj", ".vbproj"))
        and path.startswith(("src/", "tests/"))
    }
    return sorted(root for root in roots if root != ".")


def _has_typescript_config(root: Path, display_root: str | None = None) -> bool:
    if (root / "tsconfig.json").exists():
        return True
    package_json = root / "package.json"
    if not package_json.exists():
        return False
    try:
        data = json.loads(package_json.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise _fact_discovery_error(
            provider="javascript-typescript",
            path=f"{_clean_path(display_root or root.as_posix())}/package.json",
            message="package.json facts could not be discovered",
            reason=type(error).__name__,
        ) from error
    if not isinstance(data, Mapping):
        raise _fact_discovery_error(
            provider="javascript-typescript",
            path=f"{_clean_path(display_root or root.as_posix())}/package.json",
            message="package.json facts could not be discovered",
            reason="JSONTypeError",
        )
    text = json.dumps(
        {
            "dependencies": data.get("dependencies", {}),
            "devDependencies": data.get("devDependencies", {}),
        },
        sort_keys=True,
    )
    return "typescript" in text


def _fact_discovery_error(
    *,
    provider: str,
    path: str,
    message: str,
    reason: str,
) -> _FactDiscoveryError:
    return _FactDiscoveryError(
        [
            _diagnostic(
                code=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
                detail=None,
                message=message,
                source_type="fact-provider",
                source_id=provider,
                ordinal=1,
                extra={
                    "provider": provider,
                    "path": _clean_path(path),
                    "reason": reason,
                },
            ),
        ],
    )


def _git_files(repo_root: Path) -> tuple[str, ...]:
    git = shutil.which("git") or "git"
    result = subprocess.run(  # noqa: S603
        [git, "ls-files"],
        cwd=repo_root,
        check=True,
        capture_output=True,
        text=True,
    )
    return tuple(
        _clean_path(line) for line in result.stdout.splitlines() if line
    )


def _clean_path(path: str) -> str:
    return str(PurePosixPath(path.strip().replace("\\", "/")))


def _now_rfc3339() -> str:
    return (
        datetime.now(UTC)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )
