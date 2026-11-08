"""Show one file's measure differences for the owner's local spot-check.

For each measure ordinal with an operation, the view lists the raw
operations of the content and layout passes and the canonical text of that
measure in ``Y`` and in ``T(Y)``. It only reads the run directory.
"""

from __future__ import annotations

import re
from typing import TYPE_CHECKING

from sheet_music_omr.harness.records import read_object

if TYPE_CHECKING:
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement

_MEASURE = re.compile(r"^m \d+")


def measure_blocks(text: str) -> list[list[str]]:
    """Split canonical text into its measure blocks, in order."""
    blocks: list[list[str]] = []
    for line in text.splitlines():
        if _MEASURE.match(line):
            blocks.append([line])
        elif blocks and line.strip():
            blocks[-1].append(line)
    return blocks


def _ordinal(operation: dict[str, object]) -> int | None:
    for side in ("location_y", "location_t"):
        location = operation.get(side)
        if isinstance(location, dict):
            index = location.get("measure_index")
            if isinstance(index, int):
                return index
    return None


def _describe(operation: dict[str, object]) -> str:
    return (
        f"{operation.get('name')}: T(Y) {operation.get('kind_t')}, "
        f"Y {operation.get('kind_y')}, cost {operation.get('cost')}"
    )


def _text(path: Path) -> list[list[str]]:
    if not path.exists():
        return []
    return measure_blocks(path.read_text(encoding="utf-8"))


def view_file(
    confinement: Confinement,
    name: str,
    *,
    arm: str | None = None,
    measure: int | None = None,
) -> str:
    """Return the measure view of one file in each arm that has it.

    Args:
        confinement: The run's directories.
        name: The file identifier, such as ``p0001`` or ``gold-03``.
        arm: Only this arm, if given.
        measure: Only this zero-based measure ordinal, if given.

    Returns:
        The view text, or a note that the file has no results.
    """
    root = confinement.run / "files"
    directories = sorted(
        path.parent
        for path in root.glob(f"*/{name}/result.json")
        if path.parent.parent.name not in {"baseline", "assembly"}
        and (arm is None or path.parent.parent.name == arm)
    )
    if not directories:
        return f"no results for {name}\n"
    lines: list[str] = []
    for directory in directories:
        lines += _view_one(directory, measure)
    return "\n".join(lines) + "\n"


def _view_one(directory: Path, measure: int | None) -> list[str]:
    result = read_object(directory / "result.json")
    lines = [
        (
            f"# {directory.parent.name} {directory.name}: failure "
            f"{result.get('failure')}"
        ),
    ]
    operations_path = directory / "operations.json"
    operations = (
        read_object(operations_path) if operations_path.exists() else {}
    )
    y_blocks = _text(directory / "reverse.txt")
    t_blocks = _text(directory / "t-reverse.txt")
    by_measure: dict[int, list[str]] = {}
    unplaced: list[str] = []
    for pass_name in ("content", "layout"):
        records = operations.get(pass_name)
        if not isinstance(records, dict):
            continue
        for group in ("measure", "global"):
            entries = records.get(group)
            for entry in entries if isinstance(entries, list) else []:
                if not isinstance(entry, dict):
                    continue
                text = f"  {pass_name} {group}: {_describe(entry)}"
                ordinal = _ordinal(entry) if group == "measure" else None
                if ordinal is None:
                    unplaced.append(text)
                else:
                    by_measure.setdefault(ordinal, []).append(text)
    ordinals = sorted(by_measure) if measure is None else [measure]
    for ordinal in ordinals:
        lines += ["", f"## measure ordinal {ordinal}"]
        lines += by_measure.get(ordinal, ["  no operation"])
        for label, blocks in (("Y", y_blocks), ("T(Y)", t_blocks)):
            lines.append(f"{label}:")
            block = blocks[ordinal] if 0 <= ordinal < len(blocks) else []
            lines += [f"  {line}" for line in block] or ["  (no measure)"]
    if measure is None and unplaced:
        lines += ["", "## global-only operations", *unplaced]
    return lines
