"""Scan public text for private paths, names and gold identities.

Text bound for a public carrier is scanned before it is written. A hit is a
stop condition: private content, a private path or a gold piece identity is
about to be written where it must not go.
"""

from __future__ import annotations

import os
import re
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness.records import read_object
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Iterable

    from sheet_music_omr.harness.confine import Confinement

GOLD_MAPPING = Path("gold") / "mapping.json"
"""The run-directory record of the gold identities, which stays local."""

_ABSOLUTE = (
    re.compile(r"(?<![A-Za-z0-9])[A-Za-z]:[\\/]"),
    re.compile(r"\\\\[^\\\s]+\\"),
    re.compile(r"(?<![\w.])/(?:home|Users|tmp|var|mnt|root)/"),
)


class PrivacyError(StopConditionError):
    """Public text holds a private path, name or gold identity."""


def gold_private(confinement: Confinement) -> list[str]:
    """Return the gold identities recorded in the run directory."""
    path = confinement.run / GOLD_MAPPING
    if not path.exists():
        return []
    mapping = read_object(path)
    values: list[str] = []
    for name in ("status", "cache"):
        value = mapping.get(name)
        if isinstance(value, str):
            values.append(value)
    pieces = mapping.get("pieces")
    if isinstance(pieces, list):
        for piece in pieces:
            if isinstance(piece, dict):
                values.extend(
                    str(piece[name])
                    for name in ("workspace", "build", "path", "sha256")
                    if isinstance(piece.get(name), str)
                )
    return values


def private_strings(
    confinement: Confinement,
    extra: Iterable[str] = (),
) -> tuple[str, ...]:
    """Return every private string of a run that public text must not hold.

    These are the run and download directories, the home directory, the user
    name, the gold identities in the run directory and ``extra``.
    """
    values = [
        str(confinement.run),
        str(confinement.downloads),
        str(Path.home()),
        os.environ.get("USERNAME", ""),
        os.environ.get("USER", ""),
        *gold_private(confinement),
        *extra,
    ]
    return tuple(sorted({v for v in values if v.strip()}))


def scan(text: str, private: Iterable[str]) -> None:
    """Check public text for private strings and absolute paths.

    A private string matches case-insensitively, in either slash spelling,
    and as a whole word when it is a single word.

    Raises:
        PrivacyError: If the text holds one. The message does not repeat it.
    """
    for value in private:
        variants = {value, value.replace("\\", "/"), value.replace("/", "\\")}
        for variant in variants:
            pattern = re.escape(variant)
            if re.fullmatch(r"\w+", variant):
                pattern = rf"(?<!\w){pattern}(?!\w)"
            if re.search(pattern, text, flags=re.IGNORECASE):
                message = "public text holds a private path, name or identity"
                raise PrivacyError(message)
    for absolute in _ABSOLUTE:
        if absolute.search(text):
            message = "public text holds an absolute path"
            raise PrivacyError(message)
