# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Validate RubyGems-style version strings for this repository.

Rules (repository policy):
- Must be MAJOR.MINOR.PATCH, with optional suffix dot segments.
- Must not contain '-' or '+'.
- If suffix segments exist:
    - Each suffix segment must be ASCII alphanumeric: [0-9A-Za-z]+
    - The suffix must contain at least one letter (A-Za-z) across all suffix
        segments.
    - Reject suffixes that are numeric-only (all suffix segments are digits),
        e.g.:
        - 1.2.3.1
        - 1.2.3.0.1

A version is considered prerelease iff it contains any suffix segment beyond
MAJOR.MINOR.PATCH.
"""

from __future__ import annotations

import argparse
import re
import sys

_BASE_RE = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(.+))?$")
_SUFFIX_SEG_RE = re.compile(r"^[0-9A-Za-z]+$")


def _fail(msg: str) -> None:
    print(msg, file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description="Validate RubyGems-style version string (repo policy)."
    )
    parser.add_argument("version", help="The version string to validate")
    args = parser.parse_args()

    version: str = args.version

    if version.startswith(("v", "V")):
        _fail(
            "Invalid RubyGems version: leading 'v' is not allowed: " + version
        )

    if "-" in version or "+" in version:
        _fail(
            "Invalid RubyGems version: must not contain '-' or '+': " + version
        )

    m = _BASE_RE.match(version)
    if not m:
        _fail(
            "Invalid RubyGems version: expected MAJOR.MINOR.PATCH[.suffix...]: "
            + version
        )

    suffix = m.group(4)
    if suffix is None:
        print(f"Version {version} is valid (prerelease=false).")
        return

    segments = suffix.split(".")
    if any(seg == "" for seg in segments):
        _fail(f"Invalid RubyGems version: empty suffix segment: {version}")

    for seg in segments:
        if not _SUFFIX_SEG_RE.fullmatch(seg):
            _fail(
                "Invalid RubyGems version: "
                "suffix segments must be [0-9A-Za-z]+: " + version
            )

    has_letter = any(
        any("A" <= ch <= "Z" or "a" <= ch <= "z" for ch in seg)
        for seg in segments
    )
    if not has_letter:
        _fail(
            "Invalid RubyGems version: suffix must contain at least one letter;"
            " numeric-only suffixes are not allowed: " + version
        )

    print(f"Version {version} is valid (prerelease=true).")


if __name__ == "__main__":
    main()
