"""Command-line entry point for workflow-release authoring validation."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from three_workflow_release_authoring.authoring import (
    AuthoringValidationError,
    diagnostics_document,
    validate_authoring,
)


def main() -> int:
    """Run the authoring validator CLI."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("validate", nargs="?", help="validate authoring files")
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--commit-sha")
    parser.add_argument("--dotnet-metadata-input-out")
    parser.add_argument("--diagnostics-out")
    args = parser.parse_args()
    if args.validate not in {None, "validate"}:
        parser.error("the only supported command is validate")
    try:
        snapshot = validate_authoring(Path(args.repo_root))
    except AuthoringValidationError as error:
        if args.diagnostics_out:
            document = diagnostics_document(error.issues)
            Path(args.diagnostics_out).write_text(
                json.dumps(document, indent=2, sort_keys=True) + "\n",
                encoding="utf-8",
            )
        return 1
    if args.dotnet_metadata_input_out:
        if not args.commit_sha:
            parser.error("--commit-sha is required with metadata input output")
        document = snapshot.dotnet_metadata_input(args.commit_sha)
        Path(args.dotnet_metadata_input_out).write_text(
            json.dumps(document, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
