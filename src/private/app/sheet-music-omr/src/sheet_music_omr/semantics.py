"""Semantic validation of Text Score 2 documents.

``validate_document`` checks every rule of the "Semantics" section and the
validity rules of the "Carry state" and "Canonical form" sections on a parsed
document and returns the diagnostics in location order. ``carry_out`` derives
the carry-out state of a valid system document, which assembly compares with
the ``carry-in`` of the next document.

Rules that link several system documents, such as the identity of ``len``
across the fragments of a split measure, are checked by
``sheet_music_omr.assembly``.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from sheet_music_omr.beams import check_beams
from sheet_music_omr.carry import check_carry_in, derive_carry_out
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.measures import (
    check_blocks,
    check_events,
    check_headers,
    check_lines,
)
from sheet_music_omr.model import SystemDocument
from sheet_music_omr.pitches import resolve_pitches
from sheet_music_omr.relations import analyze_relations
from sheet_music_omr.timeline import build_timeline

if TYPE_CHECKING:
    from sheet_music_omr.diagnostics import Diagnostic
    from sheet_music_omr.model import CarryState, Document
    from sheet_music_omr.pitches import PitchResolver
    from sheet_music_omr.relations import RelationAnalysis
    from sheet_music_omr.timeline import Timeline


@dataclass(frozen=True, slots=True)
class Analysis:
    """The timeline, relations and pitch state of a document.

    Attributes:
        timeline: The placed events.
        relations: The relations and their findings.
        pitches: The resolved pitches.
        diagnostics: Every semantic finding in location order.
    """

    timeline: Timeline
    relations: RelationAnalysis
    pitches: PitchResolver
    diagnostics: tuple[Diagnostic, ...]


def analyze(document: Document) -> Analysis:
    """Analyze a parsed document and collect its semantic findings."""
    timeline = build_timeline(document)
    found: list[Diagnostic] = []
    if isinstance(document, SystemDocument):
        found.extend(check_carry_in(document))
    found.extend(check_headers(document, timeline))
    found.extend(check_blocks(document, timeline))
    found.extend(check_lines(timeline))
    found.extend(check_events(timeline))
    found.extend(check_beams(timeline.lines))
    relations = analyze_relations(document, timeline)
    found.extend(relations.findings)
    pitches = resolve_pitches(document, timeline, relations)
    found.extend(pitches.findings)
    unique = dict.fromkeys(found)
    ordered = sorted(unique, key=lambda d: (d.location, d.code, d.message))
    return Analysis(timeline, relations, pitches, tuple(ordered))


def validate_document(document: Document) -> tuple[Diagnostic, ...]:
    """Return the semantic diagnostics of a parsed document.

    Args:
        document: A document that the parser accepted.

    Returns:
        The semantic and unsupported-content diagnostics in location order;
        empty when the document is valid.
    """
    return analyze(document).diagnostics


def check_document(document: Document) -> None:
    """Raise when a parsed document breaks a semantic rule.

    Raises:
        TextScoreError: The document is not valid.
    """
    diagnostics = validate_document(document)
    if diagnostics:
        raise TextScoreError(diagnostics)


def carry_out(document: SystemDocument) -> CarryState:
    """Return the carry-out state of a valid system document.

    Raises:
        TextScoreError: The document is not valid.
    """
    analysis = analyze(document)
    if analysis.diagnostics:
        raise TextScoreError(analysis.diagnostics)
    return derive_carry_out(
        document,
        analysis.timeline,
        analysis.relations,
        analysis.pitches,
    )
