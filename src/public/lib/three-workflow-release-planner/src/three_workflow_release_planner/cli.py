"""Command-line entry point for workflow-release planning."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Literal, cast

from three_workflow_release_planner import (
    CiValidationPlannerInputs,
    CiValidationPlanningError,
    PlannerError,
    PlannerInputs,
    plan_ci_validation_from_repo,
    plan_from_repo,
)

RemoteObservation = Literal[
    "absent",
    "exact-satisfied",
    "partial",
    "partial-authoritative",
    "conflicting",
]

_REMOTE_OBSERVATIONS: set[RemoteObservation] = {
    "absent",
    "exact-satisfied",
    "partial",
    "partial-authoritative",
    "conflicting",
}


def main() -> int:
    """Run the planner CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["plan", "ci-plan"])
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--request", required=True)
    parser.add_argument("--expected-run-id")
    parser.add_argument("--expected-run-attempt")
    parser.add_argument("--created-at")
    parser.add_argument("--ci-plan-id")
    parser.add_argument("--observed-commit-sha")
    parser.add_argument("--tracked-files")
    parser.add_argument("--policy-version")
    parser.add_argument("--dotnet-metadata")
    parser.add_argument("--remote-observations")
    parser.add_argument("--official-frozen-versions")
    parser.add_argument("--plan-out", required=True)
    parser.add_argument("--execution-sets-out")
    parser.add_argument("--changed-files-out")
    parser.add_argument("--fact-snapshot-out")
    parser.add_argument("--diagnostics-out")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--validation-build", action="store_true")
    parser.add_argument(
        "--deactivate-buddy-github-release",
        action="store_true",
    )
    args = parser.parse_args()

    request = _load_json(Path(args.request))
    if args.command == "ci-plan":
        return _run_ci_plan(args, request, parser)
    if not args.execution_sets_out:
        parser.error("--execution-sets-out is required for plan")
    metadata = (
        _load_json(Path(args.dotnet_metadata)) if args.dotnet_metadata else None
    )
    remote_observations = (
        _load_remote_observations(Path(args.remote_observations))
        if args.remote_observations
        else None
    )
    official_frozen_versions = (
        _load_official_frozen_versions(Path(args.official_frozen_versions))
        if args.official_frozen_versions
        else None
    )
    try:
        result = plan_from_repo(
            PlannerInputs(
                request=request,
                repo_root=Path(args.repo_root),
                dry_run=args.dry_run,
                validation_build=args.validation_build,
                dotnet_metadata=metadata,
                remote_observations=remote_observations,
                official_frozen_versions=official_frozen_versions,
                deactivate_buddy_github_release=(
                    args.deactivate_buddy_github_release
                ),
            )
        )
    except PlannerError as exc:
        if args.diagnostics_out:
            _write_json(Path(args.diagnostics_out), exc.document())
        return 1
    _write_json(Path(args.plan_out), result.plan)
    _write_json(Path(args.execution_sets_out), result.execution_sets)
    return 0


def _run_ci_plan(
    args: argparse.Namespace,
    request: dict[str, Any],
    parser: argparse.ArgumentParser,
) -> int:
    """Run CI validation planning from CLI arguments."""
    missing = [
        option
        for option in ("expected_run_id", "expected_run_attempt")
        if getattr(args, option) is None
    ]
    if missing:
        missing_args = ", ".join(
            f"--{item.replace('_', '-')}" for item in missing
        )
        parser.error(f"ci-plan requires {missing_args}")
    tracked_files = (
        _load_string_array(Path(args.tracked_files))
        if args.tracked_files
        else None
    )
    try:
        result = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=Path(args.repo_root),
                expected_run_id=args.expected_run_id,
                expected_run_attempt=args.expected_run_attempt,
                created_at=args.created_at,
                plan_id=args.ci_plan_id,
                observed_commit_sha=args.observed_commit_sha,
                tracked_files=tracked_files,
                policy_version=args.policy_version,
            )
        )
    except CiValidationPlanningError as exc:
        if args.diagnostics_out:
            _write_json(
                Path(args.diagnostics_out),
                _diagnostics_document(exc.diagnostics),
            )
        return 1
    _write_json(Path(args.plan_out), result.plan)
    if args.changed_files_out and result.changed_files_snapshot is not None:
        _write_json(Path(args.changed_files_out), result.changed_files_snapshot)
    if args.fact_snapshot_out and result.fact_snapshot is not None:
        _write_json(Path(args.fact_snapshot_out), result.fact_snapshot)
    return 0


def _load_json(path: Path) -> dict[str, Any]:
    """Load one JSON object file."""
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        msg = f"{path} must contain a JSON object"
        raise TypeError(msg)
    return document


def _load_string_array(path: Path) -> tuple[str, ...]:
    """Load one JSON string array file."""
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, list) or not all(
        isinstance(item, str) for item in document
    ):
        msg = f"{path} must contain a JSON string array"
        raise TypeError(msg)
    return tuple(document)


def _load_remote_observations(path: Path) -> dict[str, RemoteObservation]:
    """Load and validate publish-node remote observations."""
    document = _load_json(path)
    for key, value in document.items():
        if not isinstance(key, str) or not isinstance(value, str):
            msg = f"{path} must map strings to remote observation strings"
            raise TypeError(msg)
        if value not in _REMOTE_OBSERVATIONS:
            msg = f"{path} contains invalid remote observation {value!r}"
            raise ValueError(msg)
    return cast("dict[str, RemoteObservation]", document)


def _load_official_frozen_versions(path: Path) -> dict[str, tuple[str, ...]]:
    """Load and validate official frozen versions by project id."""
    document = _load_json(path)
    frozen: dict[str, tuple[str, ...]] = {}
    for key, value in document.items():
        if not isinstance(key, str) or not isinstance(value, list):
            msg = f"{path} must map strings to arrays of version strings"
            raise TypeError(msg)
        if not all(isinstance(item, str) for item in value):
            msg = f"{path} must map strings to arrays of version strings"
            raise TypeError(msg)
        frozen[key] = tuple(value)
    return frozen


def _write_json(path: Path, document: object) -> None:
    """Write deterministic UTF-8 JSON with LF ending."""
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False)
        + "\n",
        encoding="utf-8",
    )


def _diagnostics_document(diagnostics: object) -> dict[str, object]:
    """Wrap planner diagnostics in the release diagnostics envelope."""
    return {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": list(_release_diagnostics(diagnostics)),
    }


def _release_diagnostics(diagnostics: object) -> tuple[dict[str, object], ...]:
    """Convert CI validation diagnostics to planner diagnostics."""
    if not isinstance(diagnostics, tuple | list):
        return (_release_diagnostic("CI_VALIDATION_PLANNING_FAILED", None),)
    result: list[dict[str, object]] = []
    for diagnostic in diagnostics:
        code = None
        message = None
        if isinstance(diagnostic, dict):
            code_value = diagnostic.get("code")
            message_value = diagnostic.get("message")
            code = code_value if isinstance(code_value, str) else None
            message = message_value if isinstance(message_value, str) else None
        result.append(_release_diagnostic(_release_code(code), message))
    return tuple(result) or (
        _release_diagnostic("CI_VALIDATION_PLANNING_FAILED", None),
    )


def _release_diagnostic(
    code: str | None,
    message: str | None,
) -> dict[str, object]:
    """Build one schema-valid planner diagnostic for CI planning failure."""
    return {
        "api-version": "three.release.planner-diagnostic/v1alpha1",
        "kind": "planner-diagnostic",
        "code": code or "CI_VALIDATION_PLANNING_FAILED",
        "message": message or "CI validation planning failed closed",
        "phase": "validation",
        "scope-kind": "request",
        "blocking": True,
        "details": {},
    }


def _release_code(code: str | None) -> str:
    """Map CI diagnostic codes into the release diagnostics vocabulary."""
    if code == "request-invalid":
        return "REQ_INVALID_INPUT"
    return "PLAN_INTERNAL_INVARIANT"


if __name__ == "__main__":
    raise SystemExit(main())
