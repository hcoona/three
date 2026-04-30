"""Command-line entry point for workflow-release build executors."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from three_workflow_release_build import (
    BuildExecutorError,
    build_diagnostics_document,
    execute_build,
)


def main() -> int:
    """Run the build executor CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["build"])
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--request", required=True)
    parser.add_argument("--bundle-dir", required=True)
    parser.add_argument("--result-out", required=True)
    parser.add_argument("--diagnostics-out")
    args = parser.parse_args()

    request: dict[str, Any] | None = None
    try:
        request = _load_json(Path(args.request))
        result = execute_build(
            request,
            Path(args.repo_root),
            Path(args.bundle_dir),
        )
        _write_result_json(Path(args.result_out), result)
    except BuildExecutorError as exc:
        if args.diagnostics_out:
            diagnostics = build_diagnostics_document(exc, request=request)
            try:
                _write_json(Path(args.diagnostics_out), diagnostics)
            except OSError as diagnostics_exc:
                sys.stderr.write(
                    f"{args.diagnostics_out} could not be written as build "
                    f"diagnostics JSON: {diagnostics_exc}\n"
                )
        sys.stderr.write(f"{exc}\n")
        return 1
    return 0


def _load_json(path: Path) -> dict[str, Any]:
    """Load one JSON object file."""
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = f"{path} could not be read as a UTF-8 JSON object: {exc}"
        raise BuildExecutorError(
            msg, code="BUILD_INVALID_INPUT", phase="validation"
        ) from exc
    if not isinstance(document, dict):
        msg = f"{path} must contain a JSON object"
        raise BuildExecutorError(
            msg, code="BUILD_INVALID_INPUT", phase="validation"
        )
    return document


def _write_json(path: Path, document: object) -> None:
    """Write deterministic UTF-8 JSON with LF ending."""
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False)
        + "\n",
        encoding="utf-8",
    )


def _write_result_json(path: Path, document: object) -> None:
    """Write a build result and wrap filesystem failures as diagnostics."""
    try:
        _write_json(path, document)
    except OSError as exc:
        msg = f"{path} could not be written as build result JSON: {exc}"
        raise BuildExecutorError(
            msg,
            code="BUILD_OUTPUT_INVALID",
            phase="receipt",
            details={"path": path.as_posix(), "error": str(exc)},
        ) from exc


if __name__ == "__main__":
    raise SystemExit(main())
