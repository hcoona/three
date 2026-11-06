"""Text Score 2 parser, canonical writer and command-line tools.

The package parses Text Score 2 system documents and assembled scores into an
immutable syntax tree, reports lexical, grammar and semantic violations as
diagnostics, derives the carry-out state of a system document and writes the
canonical spelling of a tree. Assembly and MusicXML conversion are not
implemented yet.
"""

from sheet_music_omr.diagnostics import (
    Diagnostic,
    Location,
    RuleClass,
    TextScoreError,
)
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.parser import parse_document
from sheet_music_omr.review import ReviewMarker, review_markers
from sheet_music_omr.semantics import (
    carry_out,
    check_document,
    validate_document,
)
from sheet_music_omr.writer import write_document

__all__ = [
    "Diagnostic",
    "Location",
    "ReviewMarker",
    "RuleClass",
    "TextScoreError",
    "carry_out",
    "check_document",
    "decode_source",
    "parse_document",
    "review_markers",
    "validate_document",
    "write_document",
]
