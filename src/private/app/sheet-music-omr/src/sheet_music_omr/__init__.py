"""Text Score 2 parser, canonical writer and command-line tools.

The package parses Text Score 2 system documents and assembled scores into an
immutable syntax tree, reports lexical, grammar and semantic violations as
diagnostics, derives the carry-out state of a system document, assembles
system documents into an assembled score and writes the canonical spelling of
a tree. It converts a valid assembled score to MusicXML 4.0 and a MusicXML
piano score back to an assembled score.
"""

from sheet_music_omr.assembly import (
    AssemblyResult,
    AssemblyStatus,
    MarkerReview,
    SourceDiagnostic,
    SourceLocation,
    TrillReview,
    assemble,
)
from sheet_music_omr.diagnostics import (
    Diagnostic,
    Location,
    RuleClass,
    TextScoreError,
)
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.musicxml_forward import (
    ReviewRequiredError,
    to_musicxml,
    to_musicxml_text,
)
from sheet_music_omr.musicxml_reverse import (
    MusicXmlDiagnostic,
    MusicXmlError,
    from_musicxml,
    from_musicxml_text,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.review import ReviewMarker, review_markers
from sheet_music_omr.semantics import (
    carry_out,
    check_document,
    validate_document,
)
from sheet_music_omr.writer import write_document

__all__ = [
    "AssemblyResult",
    "AssemblyStatus",
    "Diagnostic",
    "Location",
    "MarkerReview",
    "MusicXmlDiagnostic",
    "MusicXmlError",
    "ReviewMarker",
    "ReviewRequiredError",
    "RuleClass",
    "SourceDiagnostic",
    "SourceLocation",
    "TextScoreError",
    "TrillReview",
    "assemble",
    "carry_out",
    "check_document",
    "decode_source",
    "from_musicxml",
    "from_musicxml_text",
    "parse_document",
    "review_markers",
    "to_musicxml",
    "to_musicxml_text",
    "validate_document",
    "write_document",
]
