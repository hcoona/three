"""Load the private gold set, read-only, under anonymized identifiers.

The gold inputs are the MusicXML files of every accepted private gold piece.
The set is fixed when a run starts; only aggregates and anonymized batch
numbers leave the run directory.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import TYPE_CHECKING

from sheet_music_omr.harness.musescore import sha256_file
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from pathlib import Path

SCORE_NAME = "score.musicxml"
"""The name of a gold piece's MusicXML file in its build directory."""


class GoldError(StopConditionError):
    """The gold set cannot be read as the protocol fixes it."""


@dataclass(frozen=True, slots=True)
class GoldPiece:
    """One accepted gold piece.

    Attributes:
        id: The anonymized identifier, ``gold-NN`` from the batch number.
        number: The batch number.
        workspace: The private workspace name, kept in the run directory.
        path: The private MusicXML file ``X``.
        sha256: The SHA-256 of ``X`` when the set was loaded.
    """

    id: str
    number: int
    workspace: str
    path: Path
    sha256: str


def gold_id(number: int) -> str:
    """Return the anonymized identifier of a batch number."""
    return f"gold-{number:02d}"


def _records(data: object) -> list[object]:
    if isinstance(data, list):
        return data
    if isinstance(data, dict):
        for name in ("batches", "pieces"):
            value = data.get(name)
            if isinstance(value, list):
                return value
    message = "the gold status file has no list of pieces"
    raise GoldError(message)


def _accepted(record: object, index: int) -> tuple[int, str, str] | None:
    if not isinstance(record, dict):
        message = f"gold status record {index} is not an object"
        raise GoldError(message)
    accepted = record.get("accepted")
    if not isinstance(accepted, bool):
        message = f"gold status record {index} has no boolean accepted"
        raise GoldError(message)
    if not accepted:
        return None
    number = record.get("number")
    workspace = record.get("workspace")
    build = record.get("latest_build")
    if (
        not isinstance(number, int)
        or isinstance(number, bool)
        or number < 0
        or not isinstance(workspace, str)
        or not isinstance(build, str)
    ):
        message = (
            f"accepted gold status record {index} lacks an integer number, "
            "a workspace or a latest build"
        )
        raise GoldError(message)
    for part in (workspace, build):
        if not part or part in {".", ".."} or any(c in part for c in "/\\:"):
            message = f"accepted gold status record {index} has an unsafe name"
            raise GoldError(message)
    return number, workspace, build


def load_gold(status: Path, cache: Path) -> tuple[GoldPiece, ...]:
    """Read every accepted gold piece, in batch-number order.

    Args:
        status: The private batch status file.
        cache: The private build cache, holding
            ``<workspace>/<latest_build>/score.musicxml`` for each piece.

    Returns:
        The accepted pieces, each with the hash of its ``X``.

    Raises:
        GoldError: If the status file is unreadable or malformed, a batch
            number repeats, there is no accepted piece, or a piece's ``X``
            is missing or unreadable.
    """
    try:
        data = json.loads(status.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        message = f"cannot read the gold status file: {type(error).__name__}"
        raise GoldError(message) from error
    pieces: dict[int, GoldPiece] = {}
    for index, record in enumerate(_records(data)):
        accepted = _accepted(record, index)
        if accepted is None:
            continue
        number, workspace, build = accepted
        if number in pieces:
            message = f"gold batch number {number} repeats"
            raise GoldError(message)
        path = cache / workspace / build / SCORE_NAME
        try:
            digest = sha256_file(path)
        except OSError as error:
            message = f"the X of {gold_id(number)} is missing or unreadable"
            raise GoldError(message) from error
        pieces[number] = GoldPiece(
            gold_id(number),
            number,
            workspace,
            path,
            digest,
        )
    if not pieces:
        message = "the gold status file has no accepted piece"
        raise GoldError(message)
    return tuple(pieces[number] for number in sorted(pieces))


def check_unchanged(piece: GoldPiece) -> None:
    """Check that a gold piece's ``X`` still has its frozen hash.

    Raises:
        GoldError: If it is missing, unreadable or changed.
    """
    try:
        digest = sha256_file(piece.path)
    except OSError as error:
        message = f"the X of {piece.id} is missing or unreadable"
        raise GoldError(message) from error
    if digest != piece.sha256:
        message = f"the X of {piece.id} changed during the run"
        raise GoldError(message)
