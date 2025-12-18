# /// script
# requires-python = ">=3.14"
# dependencies = ["packaging>=24.0"]
# ///

"""Validate PEP 440 version string."""

from __future__ import annotations

import argparse
import sys

from packaging.version import InvalidVersion, Version


def main() -> None:
    """Validate PEP 440 version string."""
    parser = argparse.ArgumentParser(
        description="Validate PEP 440 version string."
    )
    parser.add_argument("version", help="The version string to validate")
    args = parser.parse_args()

    version_str: str = args.version

    try:
        Version(version_str)
    except InvalidVersion as exc:
        sys.exit(
            f"Invalid version (must be PEP 440): {version_str}. Error: {exc}"
        )

    print(f"Version {version_str} is valid.")


if __name__ == "__main__":
    main()
