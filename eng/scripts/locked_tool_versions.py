"""Print committed tool versions as GitHub Actions output lines.

Root mise.lock selects every tool except the .NET SDK, which global.json
selects. Workflows read versions once through this script instead of
repeating them as literals.
"""

from __future__ import annotations

import argparse
import json
import re
import tomllib
from pathlib import Path

EXACT = re.compile(r"\A\d+(?:\.\d+)+\Z")


def locked_version(root: Path, tool: str) -> str:
    """Return the single exact committed version of one tool."""
    if tool == "dotnet":
        source = "global.json sdk.version"
        sdk = json.loads((root / "global.json").read_text(encoding="utf-8"))
        versions = {sdk.get("sdk", {}).get("version")}
    else:
        source = f"mise.lock [[tools.{tool}]]"
        lock = tomllib.loads((root / "mise.lock").read_text(encoding="utf-8"))
        entries = lock.get("tools", {}).get(tool, [])
        versions = {entry.get("version") for entry in entries}
    if len(versions) != 1:
        message = f"{tool}: {source} must select exactly one version"
        raise ValueError(message)
    (version,) = versions
    if not isinstance(version, str) or not EXACT.fullmatch(version):
        message = f"{tool}: {source} is not an exact version: {version!r}"
        raise ValueError(message)
    return version


def main() -> int:
    """Print one name=version line per requested tool."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tools", nargs="+")
    parser.add_argument("--root", type=Path, default=Path())
    options = parser.parse_args()
    try:
        lines = [
            f"{tool}={locked_version(options.root, tool)}"
            for tool in options.tools
        ]
    except (OSError, ValueError) as error:
        raise SystemExit(str(error)) from error
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
