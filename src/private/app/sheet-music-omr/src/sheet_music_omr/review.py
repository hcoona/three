"""Listing of the review markers of a parsed document.

A review marker is a timed ``?(D)`` span or an untimed ``<?>`` direction. A
valid document can contain markers, and a tool lists each one with its location
and candidates so that a person can resolve it.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from sheet_music_omr.model import (
    AssembledScore,
    TimedReview,
    Tuplet,
    UntimedReview,
)

if TYPE_CHECKING:
    from collections.abc import Iterable, Iterator

    from sheet_music_omr.diagnostics import Location
    from sheet_music_omr.model import Document, Event, MeasureBlock


@dataclass(frozen=True, slots=True)
class ReviewMarker:
    """One review marker in a document.

    Attributes:
        marker: The timed or untimed marker.
        measure: The number of the measure block that contains the marker.
        staff: The staff of the voice line that contains the marker.
        voice: The voice of the voice line that contains the marker.
    """

    marker: TimedReview | UntimedReview
    measure: int
    staff: int
    voice: int

    @property
    def location(self) -> Location | None:
        """Return the location of the marker in the source text."""
        return self.marker.location


def _markers_in(
    events: Iterable[Event],
) -> Iterator[TimedReview | UntimedReview]:
    for event in events:
        if isinstance(event, TimedReview | UntimedReview):
            yield event
        elif isinstance(event, Tuplet):
            yield from _markers_in(event.items)


def _blocks(document: Document) -> Iterator[MeasureBlock]:
    if isinstance(document, AssembledScore):
        for headed in document.blocks:
            yield headed.block
    else:
        yield from document.blocks


def review_markers(document: Document) -> tuple[ReviewMarker, ...]:
    """Return the review markers of a document in text order."""
    return tuple(
        ReviewMarker(marker, block.number, voice.staff, voice.voice)
        for block in _blocks(document)
        for voice in block.voices
        for marker in _markers_in(voice.events)
    )
