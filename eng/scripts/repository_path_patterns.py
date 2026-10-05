"""Pure repository-coordinate validation and component-aware record matching."""

from __future__ import annotations

import re


def safe_path(path: str, *, pattern: bool = False) -> bool:
    """Accept repository-relative POSIX paths and optional star globs."""
    return (
        bool(path)
        and not path.startswith("/")
        and "\\" not in path
        and ":" not in path
        and all(p not in {"", ".", ".."} for p in path.split("/"))
        and (pattern or not any(c in path for c in "*?[]"))
    )


def matches(path: str, pattern: str) -> bool:
    """Match star globs by path component, with recursive double stars."""
    regex = (
        re.escape(pattern)
        .replace(r"\*\*/", "(?:.*/)?")
        .replace(r"\*\*", ".*")
        .replace(r"\*", "[^/]*")
    )
    return re.fullmatch(regex, path) is not None
