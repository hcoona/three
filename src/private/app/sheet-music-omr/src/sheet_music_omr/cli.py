"""Command-line interface for Text Score 2 documents.

``validate`` checks the lexical, grammar and semantic rules of each file and
lists the review markers of each valid file. ``canonicalize`` writes the
canonical text of one valid file. ``assemble`` joins system documents into
an assembled score; its exit status tells success, failure and results that
need a person apart.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.lexical import decode_source, format_rational
from sheet_music_omr.model import TimedReview
from sheet_music_omr.parser import parse_document
from sheet_music_omr.review import review_markers
from sheet_music_omr.semantics import check_document
from sheet_music_omr.writer import format_candidates, write_document

if TYPE_CHECKING:
    from collections.abc import Sequence

    from sheet_music_omr.assembly import AssemblyResult
    from sheet_music_omr.model import Document
    from sheet_music_omr.review import ReviewMarker


def _load(path: str) -> Document:
    document = parse_document(decode_source(Path(path).read_bytes()))
    check_document(document)
    return document


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
    _write_text(write_document(document), None)
    return 0


_EXIT: dict[AssemblyStatus, int] = {
    AssemblyStatus.SUCCESS: 0,
    AssemblyStatus.FAILED: 1,
    AssemblyStatus.NEEDS_PERSON: 3,
}


def _write_text(text: str, output: str | None) -> None:
    data = text.encode("utf-8")
    if output is not None:
        Path(output).write_bytes(data)
        return
    # Write bytes so that text-mode newline translation, such as CRLF on
    # Windows, cannot break the LF-only canonical form.
    sys.stdout.flush()
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()


def _report(result: AssemblyResult) -> None:
    for diagnostic in result.diagnostics:
        sys.stderr.write(diagnostic.format() + "\n")
    for trill in result.trills:
        sys.stderr.write(
            f"{trill.end}: review: extended trill r{trill.label} ends on"
            f" <trill)>; it starts at {trill.start}\n"
        )
    for review in result.markers:
        sys.stderr.write(_describe(review.source, review.marker))


def _assemble(
    paths: Sequence[str],
    output: str | None,
    title: str | None,
    composer: str | None,
) -> int:
    documents: list[Document] = []
    status = 0
    for path in paths:
        try:
            text = decode_source(Path(path).read_bytes())
            documents.append(parse_document(text))
        except TextScoreError as error:
            status = 1
            for diagnostic in error.diagnostics:
                sys.stderr.write(diagnostic.format(path) + "\n")
    if status:
        return status
    result = assemble(documents, names=paths, title=title, composer=composer)
    _report(result)
    if result.text is not None:
        _write_text(result.text, output)
    return _EXIT[result.status]


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sheet-music-omr",
        description=(
            "Check and canonicalize Text Score 2 documents against the"
            " lexical, grammar and semantic rules."
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
    assembler = commands.add_parser(
        "assemble",
        help="join system documents into an assembled score",
        description=(
            "Exit status 0 means success, 1 failure and 3 a score whose"
            " review markers or extended trills need a person."
        ),
    )
    assembler.add_argument("files", nargs="+", metavar="FILE")
    assembler.add_argument(
        "-o", "--output", help="write the score to this file"
    )
    assembler.add_argument("--title", help="the title of the score")
    assembler.add_argument("--composer", help="the composer of the score")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command line and return the exit status.

    Args:
        argv: The arguments without the program name. Defaults to
            ``sys.argv[1:]``.

    Returns:
        0 on success and 1 on failure. ``assemble`` returns 3 when the
        result needs a person.
    """
    arguments = _parser().parse_args(argv)
    if arguments.command == "validate":
        return _validate(arguments.files)
    if arguments.command == "assemble":
        return _assemble(
            arguments.files,
            arguments.output,
            arguments.title,
            arguments.composer,
        )
    return _canonicalize(arguments.file)
