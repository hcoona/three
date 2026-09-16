"""Select readable-tool operands without changing HK's impact paths."""

from __future__ import annotations

import os
from pathlib import Path


def existing_operands(paths: list[str]) -> list[str]:
    """Omit absent paths only when a file-linter step explicitly opts in."""
    if os.environ.get("HK_SKIP_MISSING_FILES") != "1":
        return paths
    result = []
    for path in paths:
        try:
            Path(path).lstat()
        except FileNotFoundError:
            continue
        result.append(path)
    return result
