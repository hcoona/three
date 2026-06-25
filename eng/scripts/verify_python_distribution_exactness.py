# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Verify built Python distributions exactly match planner-frozen evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def _expected_sha256_by_filename(raw_json: str) -> dict[str, str]:
    """Parse and validate the expected filename-to-SHA-256 map."""
    try:
        raw = json.loads(raw_json)
    except json.JSONDecodeError as exc:
        sys.exit(f"Expected distribution map is not valid JSON: {exc}")
    if not isinstance(raw, dict):
        sys.exit("Expected distribution map must be a JSON object")
    if not raw:
        sys.exit("Expected distribution map must not be empty")

    expected: dict[str, str] = {}
    for raw_filename, raw_digest in raw.items():
        if not isinstance(raw_filename, str) or not raw_filename:
            sys.exit(
                "Expected distribution filenames must be non-empty strings"
            )
        if "/" in raw_filename or "\\" in raw_filename:
            sys.exit(
                "Expected distribution filename must be a basename: "
                f"{raw_filename}"
            )
        if raw_filename in {".", ".."}:
            sys.exit(
                "Expected distribution filename must not be a dot segment: "
                f"{raw_filename}"
            )
        if (
            not isinstance(raw_digest, str)
            or _SHA256_RE.fullmatch(raw_digest) is None
        ):
            sys.exit(
                f"Expected SHA-256 for {raw_filename} must be lowercase 64-hex"
            )
        expected[raw_filename] = raw_digest
    if len(expected) != len(raw):
        sys.exit("Expected distribution filenames must be unique")
    return expected


def _actual_sha256_by_filename(dist_dir: Path) -> dict[str, str]:
    """Hash every file in the distribution directory."""
    if not dist_dir.exists():
        sys.exit(f"Dist directory not found: {dist_dir}")
    if not dist_dir.is_dir():
        sys.exit(f"Dist path is not a directory: {dist_dir}")

    actual: dict[str, str] = {}
    for entry in sorted(dist_dir.iterdir(), key=lambda item: item.name):
        if not entry.is_file():
            sys.exit(f"Unexpected non-file distribution entry: {entry.name}")
        actual[entry.name] = hashlib.sha256(entry.read_bytes()).hexdigest()
    if not actual:
        sys.exit(f"No build artifacts found in {dist_dir}")
    return actual


def verify_distribution_exactness(
    expected_sha256_by_filename: dict[str, str],
    actual_sha256_by_filename: dict[str, str],
) -> None:
    """Fail unless actual names and SHA-256 digests exactly match expected."""
    expected_names = set(expected_sha256_by_filename)
    actual_names = set(actual_sha256_by_filename)
    missing = sorted(expected_names - actual_names)
    extra = sorted(actual_names - expected_names)
    if missing or extra:
        details: list[str] = []
        if missing:
            details.append(f"missing={missing}")
        if extra:
            details.append(f"extra={extra}")
        sys.exit("Built distribution filename mismatch: " + ", ".join(details))

    mismatched = [
        filename
        for filename, expected_digest in sorted(
            expected_sha256_by_filename.items()
        )
        if actual_sha256_by_filename[filename] != expected_digest
    ]
    if mismatched:
        sys.exit(
            "Built distribution SHA-256 mismatch for: " + ", ".join(mismatched)
        )


def main(argv: list[str] | None = None) -> None:
    """Verify built distributions exactly match planner-frozen evidence."""
    parser = argparse.ArgumentParser(
        description=(
            "Verify built Python distribution basenames and SHA-256 digests "
            "exactly match planner-frozen evidence."
        )
    )
    parser.add_argument(
        "expected_sha256_by_filename_json",
        help=(
            "JSON object mapping expected distribution basenames to SHA-256 "
            "digests"
        ),
    )
    parser.add_argument(
        "dist_dir",
        type=Path,
        help="Directory containing built Python distributions",
    )
    args = parser.parse_args(argv)

    expected = _expected_sha256_by_filename(
        str(args.expected_sha256_by_filename_json)
    )
    actual = _actual_sha256_by_filename(args.dist_dir)
    verify_distribution_exactness(expected, actual)
    print(
        f"Verified {len(expected)} Python distributions match the release plan"
    )


if __name__ == "__main__":
    main()
