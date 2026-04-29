"""Command-line entry point for workflow-release metadata helpers."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from three_workflow_release_metadata import (
    DotnetMetadataError,
    collect_dotnet_metadata,
)


def main() -> int:
    """Run the metadata helper CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    dotnet = subparsers.add_parser(
        "dotnet", help="collect dotnet-planner-metadata.json"
    )
    dotnet.add_argument("--repo-root", default=".")
    dotnet.add_argument("--input", required=True)
    dotnet.add_argument("--output", required=True)
    dotnet.add_argument("--diagnostics-out")
    args = parser.parse_args()

    if args.command == "dotnet":
        return _dotnet(args)
    msg = f"unsupported command {args.command!r}"
    raise ValueError(msg)


def _dotnet(args: argparse.Namespace) -> int:
    """Collect .NET planner metadata from a closed input manifest."""
    try:
        metadata_input = _load_json(Path(args.input))
        document = collect_dotnet_metadata(
            metadata_input,
            Path(args.repo_root),
        )
    except DotnetMetadataError as exc:
        if args.diagnostics_out:
            _write_json(Path(args.diagnostics_out), exc.document())
        return 1
    _write_json(Path(args.output), document)
    return 0


def _load_json(path: Path) -> object:
    """Load one JSON object file."""
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        message = "input file could not be read"
        raise _input_error(
            message,
            {"path": str(path), "error": str(exc)},
        ) from exc
    except UnicodeDecodeError as exc:
        message = "input file must be valid UTF-8"
        raise _input_error(
            message,
            {"path": str(path), "error": str(exc)},
        ) from exc
    try:
        document = json.loads(content)
    except json.JSONDecodeError as exc:
        message = "input file must contain valid JSON"
        raise _input_error(
            message,
            {"path": str(path), "error": str(exc)},
        ) from exc
    if not isinstance(document, dict):
        message = "input file must contain a JSON object"
        raise _input_error(
            message,
            {"path": str(path), "actual-type": type(document).__name__},
        )
    return document


def _input_error(
    message: str,
    details: dict[str, Any],
) -> DotnetMetadataError:
    """Create a request-scoped metadata input diagnostic."""
    return DotnetMetadataError(
        [
            {
                "api-version": "three.release.planner-diagnostic/v1alpha1",
                "kind": "planner-diagnostic",
                "code": "DOTNET_METADATA_FAILED",
                "message": message,
                "phase": "normalization",
                "scope-kind": "request",
                "blocking": True,
                "details": details,
            }
        ]
    )


def _write_json(path: Path, document: object) -> None:
    """Write deterministic UTF-8 JSON with LF ending."""
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False)
        + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    raise SystemExit(main())
