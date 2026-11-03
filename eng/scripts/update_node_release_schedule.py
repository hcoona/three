"""Refresh the committed Node.js release schedule at its pinned commit.

Renovate updates schedule.commit in eng/node-release-schedule.json, then runs
this post-upgrade task. It replaces schedule.releases with the verbatim
nodejs/Release schedule.json at that commit and changes nothing else.
"""

from __future__ import annotations

import json
import re
import urllib.request
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Callable

ROOT = Path(__file__).resolve().parents[2]
SCHEDULE = Path("eng/node-release-schedule.json")
SOURCE = (
    "https://raw.githubusercontent.com/nodejs/Release/{commit}/schedule.json"
)
COMMIT = re.compile(r"\A[0-9a-f]{40}\Z")


def download(commit: str) -> bytes:
    """Read schedule.json of nodejs/Release at one exact commit."""
    with urllib.request.urlopen(  # noqa: S310 - Fixed HTTPS source.
        SOURCE.format(commit=commit), timeout=60
    ) as response:
        return response.read()


def _unique(pairs: list[tuple[str, object]]) -> dict[str, object]:
    """Keep upstream text verbatim: a repeated key has no single meaning."""
    keys = [key for key, _ in pairs]
    if len(set(keys)) != len(keys):
        message = "nodejs/Release schedule.json repeats a key"
        raise ValueError(message)
    return dict(pairs)


def refresh(root: Path, fetch: Callable[[str], bytes] = download) -> None:
    """Rewrite schedule.releases from the commit the file already pins."""
    path = root / SCHEDULE
    data = json.loads(path.read_text(encoding="utf-8"))
    commit = data["schedule"]["commit"]
    if not isinstance(commit, str) or not COMMIT.fullmatch(commit):
        message = f"{SCHEDULE} schedule.commit must be a 40-hex commit"
        raise ValueError(message)
    releases = json.loads(fetch(commit), object_pairs_hook=_unique)
    if not isinstance(releases, dict) or not releases:
        message = "nodejs/Release schedule.json must be a nonempty object"
        raise ValueError(message)
    data["schedule"]["releases"] = releases
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    path.write_text(text, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    refresh(ROOT)
