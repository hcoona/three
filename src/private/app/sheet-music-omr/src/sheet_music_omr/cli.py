"""Command-line interface for Text Score 2 documents.

``validate`` checks the lexical and grammar rules of each file and lists the
review markers of each valid file. ``canonicalize`` writes the canonical text
of one valid file. The semantic rules are not checked yet.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.lexical import decode_source, format_rational
from sheet_music_omr.model import TimedReview
from sheet_music_omr.parser import parse_document
from sheet_music_omr.review import review_markers
from sheet_music_omr.writer import format_candidates, write_document

if TYPE_CHECKING:
    from collections.abc import Sequence

    from sheet_music_omr.model import Document
    from sheet_music_omr.review import ReviewMarker


def _load(path: str) -> Document:
    return parse_document(decode_source(Path(path).read_bytes()))


def _describe(path: str, marker: ReviewMarker) -> str:
    if isinstance(marker.marker, TimedReview):
        duration = format_rational(marker.marker.duration)
        kind = f"timed review span ?({duration})"
    else:
        kind = "untimed review marker <?>"
    candidates = format_candidates(marker.marker.candidates)
    detail = f" candidates {candidates}" if candidates else " no candidates"
    place = f"measure {marker.measure}, {marker.staff}.{marker.voice}"
    return f"{path}:{marker.location}: review: {kind} in {place};{detail}\n"


def _validate(paths: Sequence[str]) -> int:
    status = 0
    for path in paths:
        try:
            document = _load(path)
        except TextScoreError as error:
            status = 1
            for diagnostic in error.diagnostics:
                sys.stdout.write(diagnostic.format(path) + "\n")
            continue
        for marker in review_markers(document):
            sys.stdout.write(_describe(path, marker))
    return status


def _canonicalize(path: str) -> int:
    try:
        document = _load(path)
    except TextScoreError as error:
        for diagnostic in error.diagnostics:
            sys.stderr.write(diagnostic.format(path) + "\n")
        return 1
    sys.stdout.write(write_document(document))
    return 0


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sheet-music-omr",
        description=(
            "Check and canonicalize Text Score 2 documents. Only the lexical"
            " and grammar rules are checked."
        ),
    )
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser(
        "validate",
        help="check documents and list their review markers",
    )
    validate.add_argument("files", nargs="+", metavar="FILE")
    canonicalize = commands.add_parser(
        "canonicalize",
        help="write the canonical text of a document to standard output",
    )
    canonicalize.add_argument("file", metavar="FILE")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command line and return the exit status.

    Args:
        argv: The arguments without the program name. Defaults to
            ``sys.argv[1:]``.

    Returns:
        0 when every document is valid, otherwise 1.
    """
    arguments = _parser().parse_args(argv)
    if arguments.command == "validate":
        return _validate(arguments.files)
    return _canonicalize(arguments.file)
