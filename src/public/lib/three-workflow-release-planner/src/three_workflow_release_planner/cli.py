"""Command-line entry point for workflow-release planning."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Literal, cast

from three_workflow_release_planner import (
    PlannerError,
    PlannerInputs,
    plan_from_repo,
)

RemoteObservation = Literal[
    "absent", "exact-satisfied", "partial", "conflicting"
]

_REMOTE_OBSERVATIONS: set[RemoteObservation] = {
    "absent",
    "exact-satisfied",
    "partial",
    "conflicting",
}


def main() -> int:
    """Run the planner CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["plan"])
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--request", required=True)
    parser.add_argument("--dotnet-metadata")
    parser.add_argument("--remote-observations")
    parser.add_argument("--official-frozen-versions")
    parser.add_argument("--plan-out", required=True)
    parser.add_argument("--execution-sets-out", required=True)
    parser.add_argument("--diagnostics-out")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--validation-build", action="store_true")
    args = parser.parse_args()

    request = _load_json(Path(args.request))
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
            )
        )
    except PlannerError as exc:
        if args.diagnostics_out:
            _write_json(Path(args.diagnostics_out), exc.document())
        return 1
    _write_json(Path(args.plan_out), result.plan)
    _write_json(Path(args.execution_sets_out), result.execution_sets)
    return 0


def _load_json(path: Path) -> dict[str, Any]:
    """Load one JSON object file."""
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        msg = f"{path} must contain a JSON object"
        raise TypeError(msg)
    return document


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


if __name__ == "__main__":
    raise SystemExit(main())
