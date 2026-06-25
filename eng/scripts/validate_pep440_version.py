# /// script
# requires-python = ">=3.14"
# dependencies = ["packaging>=24.0"]
# ///

"""Validate canonical PEP 440 version string."""

from __future__ import annotations

import argparse
import sys

from packaging.version import InvalidVersion, Version


def main() -> None:
    """Validate canonical PEP 440 version string."""
    parser = argparse.ArgumentParser(
        description="Validate PEP 440 version string."
    )
    parser.add_argument("version", help="The version string to validate")
    args = parser.parse_args()

    version_str: str = args.version

    try:
        parsed_version = Version(version_str)
    except InvalidVersion as exc:
        sys.exit(
            f"Invalid version (must be PEP 440): {version_str}. Error: {exc}"
        )

    canonical_version = str(parsed_version)
    if canonical_version != version_str:
        sys.exit(
            "Invalid version (must be canonical PEP 440): "
            f"{version_str}. Canonical form: {canonical_version}"
        )

    print(f"Version {version_str} is valid.")


if __name__ == "__main__":
    main()
