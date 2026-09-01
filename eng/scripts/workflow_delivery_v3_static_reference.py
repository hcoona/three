"""Run the bounded static-reference policy for one explicit source kind."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.static_reference_policy import (
    scan_bounded_static_references,
    validate_bounded_static_reference_result,
)
from three_workflow_delivery_v3.release.static_reference_source import (
    InvalidRepositoryRootError,
)

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")


def _full_commit_sha(value: str) -> str:
    if _SHA_PATTERN.fullmatch(value) is None:
        message = "target must be a full lowercase commit SHA"
        raise argparse.ArgumentTypeError(message)
    return value


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository-root", type=Path, default=Path())
    parser.add_argument(
        "--source-kind",
        required=True,
        choices=("git-target", "index", "worktree"),
    )
    parser.add_argument("--target", type=_full_commit_sha)
    return parser


def main() -> int:
    """Run one admitted source scan and emit its canonical Result."""
    parser = _parser()
    arguments = parser.parse_args()
    if arguments.source_kind == "git-target":
        if arguments.target is None:
            parser.error("--target is required for git-target")
    elif arguments.target is not None:
        parser.error("--target is accepted only for git-target")
    try:
        repository_root = arguments.repository_root.resolve(strict=True)
    except OSError:
        parser.error("--repository-root must identify an existing directory")
    if not repository_root.is_dir():
        parser.error("--repository-root must identify an existing directory")
    try:
        result = scan_bounded_static_references(
            repository_root,
            source_kind=arguments.source_kind,
            target=arguments.target,
        )
    except InvalidRepositoryRootError:
        parser.error(
            "--repository-root must identify the exact Git worktree root"
        )

    validate_bounded_static_reference_result(result)
    sys.stdout.buffer.write(canonicalize(result.to_document()))
    if result.result == "clean":
        return 0
    if result.result == "findings":
        return 1
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
