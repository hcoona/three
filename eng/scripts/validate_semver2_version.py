# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Validate SemVer 2.0.0 version string.

This validator is intentionally strict:
- Requires MAJOR.MINOR.PATCH
- Allows optional prerelease (-...) and build metadata (+...)
- Does NOT allow a leading "v" prefix

Reference: https://semver.org/
"""

from __future__ import annotations

import argparse
import re
import sys

# SemVer 2.0.0 (strict) regex from semver.org (slightly formatted)
_SEMVER2_RE = re.compile(
    r"^(0|[1-9]\d*)\."
    r"(0|[1-9]\d*)\."
    r"(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)"
    r"(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)


def main() -> None:
    """Validate that the provided version matches SemVer 2.0.0."""
    parser = argparse.ArgumentParser(
        description="Validate SemVer 2.0.0 version string."
    )
    parser.add_argument("version", help="The version string to validate")
    args = parser.parse_args()

    version: str = args.version

    if version.startswith(("v", "V")):
        msg = (
            "Invalid version (must be SemVer 2.0.0 without leading 'v'): "
            f"{version}"
        )
        sys.exit(msg)

    if not _SEMVER2_RE.match(version):
        sys.exit(f"Invalid version (must be SemVer 2.0.0): {version}")

    print(f"Version {version} is valid.")


if __name__ == "__main__":
    main()
