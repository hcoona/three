"""Accidental resolution, sounding pitch and tie-in rules.

The resolver walks the pitches of each block in the "earlier" order of the
"Pitch and accidentals" section and resolves each alteration by the first rule
that applies: a printed accidental, tie inheritance (including a ``tie-in``
source), the accidental state of the measure and the key signature. Only
printed accidentals change the state, so a review marker, which prints nothing,
leaves it unchanged.

An alteration is an integer, ``"tie-in"`` when it is deferred to a source
outside the document, or ``None`` when a broken rule leaves it unknown.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Final, Literal

from sheet_music_omr.diagnostics import semantic
from sheet_music_omr.measures import UNKNOWN, ending_of, is_initial
from sheet_music_omr.model import (
    Barline,
    CarryMode,
    EndingKind,
    EndingOption,
    Note,
    RightOption,
    SystemDocument,
)
from sheet_music_omr.relations import TIE
from sheet_music_omr.timeline import first_option

if TYPE_CHECKING:
    from sheet_music_omr.diagnostics import Diagnostic, Location
    from sheet_music_omr.model import Document, Pitch
    from sheet_music_omr.relations import RelationAnalysis
    from sheet_music_omr.timeline import Frame, Placed, Timeline

type Alter = int | Literal["tie-in"] | None
type Scope = tuple[int, str, int | None]
type PitchKey = tuple[tuple[int, int, int], int]

DEFERRED: Final = "tie-in"
_MAX_OCTAVE: Final = 9
_SHARPS: Final = "FCGDAEB"
_FLATS: Final = "BEADGCF"
_NOT_FIRST: Final = frozenset({EndingKind.CONTINUE, EndingKind.END})


def key_alter(key: int, step: str) -> int:
    """Return the alteration that a key signature gives a step."""
    if key > 0 and step in _SHARPS[:key]:
        return 1
    if key < 0 and step in _FLATS[:-key]:
        return -1
    return 0


@dataclass(frozen=True, slots=True)
class Sounding:
    """The sounding step, octave and alteration of a pitch."""

    step: str
    octave: int
    alter: Alter


@dataclass(slots=True)
class PitchResolver:
    """Resolves every pitch of a document."""

    document: Document
    timeline: Timeline
    relations: RelationAnalysis
    findings: list[Diagnostic] = field(default_factory=list)
    resolved: dict[PitchKey, Sounding] = field(default_factory=dict)
    state: dict[Scope, int] = field(default_factory=dict)

    def report(
        self, rule: str, message: str, location: Location | None
    ) -> None:
        """Record a semantic finding."""
        self.findings.append(semantic(rule, message, location))

    @property
    def by_octave(self) -> bool:
        """Return whether accidentals stay in force per octave."""
        return self.document.carry is CarryMode.OCTAVE

    def run(self) -> None:
        """Resolve every pitch, block by block."""
        for frame in self.timeline.frames:
            self.state = self._seed(frame)
            entries = [
                (placed, member, pitch)
                for placed in self.timeline.events()
                if placed.frame.index == frame.index
                and isinstance(placed.event, Note)
                for member, pitch in enumerate(placed.event.pitches, 1)
            ]
            entries.sort(
                key=lambda e: (
                    e[0].onset,
                    e[0].voice,
                    e[0].staff,
                    e[0].position,
                    e[1],
                ),
            )
            for placed, member, pitch in entries:
                self._resolve(frame, placed, member, pitch)

    def _seed(self, frame: Frame) -> dict[Scope, int]:
        document = self.document
        if frame.index != 0 or not isinstance(document, SystemDocument):
            return {}
        return {
            (entry.staff, entry.step, entry.octave): entry.alter
            for entry in document.carry_in.accidentals
        }

    def scope(self, placed: Placed, pitch: Pitch) -> Scope:
        """Return the accidental scope of a pitch."""
        octave = pitch.octave if self.by_octave else None
        return (placed.displayed_staff, pitch.step, octave)

    def _resolve(
        self,
        frame: Frame,
        placed: Placed,
        member: int,
        pitch: Pitch,
    ) -> None:
        octave = pitch.octave + self.relations.shift_at(placed)
        if not 0 <= octave <= _MAX_OCTAVE:
            self.report(
                "sounding_octave",
                f"the sounding octave {octave} shall lie from 0 to 9",
                pitch.location,
            )
        inherited, rule, source = self._inherited(placed, pitch)
        alter: Alter
        if pitch.accidental is not None:
            alter = pitch.accidental.kind.alter
            self.state[self.scope(placed, pitch)] = alter
            if isinstance(inherited, int) and inherited != alter:
                self.report(
                    rule,
                    f"the accidental gives the alteration {alter} but the"
                    f" {rule} source has {inherited}",
                    pitch.location,
                )
        elif rule:
            alter = inherited
        else:
            alter = self.state.get(
                self.scope(placed, pitch),
                key_alter(frame.key, pitch.step),
            )
        sounding = Sounding(pitch.step, octave, alter)
        self.resolved[(placed.ident, member)] = sounding
        if source is not None and (source.step, source.octave) != (
            pitch.step,
            octave,
        ):
            self.report(
                TIE,
                "a tie end shall have the sounding step and octave of"
                " its start",
                pitch.location,
            )

    def _inherited(
        self,
        placed: Placed,
        pitch: Pitch,
    ) -> tuple[Alter, str, Sounding | None]:
        if pitch.tie_end is not None:
            source = self._tie_source(pitch.tie_end)
            return (None if source is None else source.alter, TIE, source)
        if pitch.tie_in:
            return (self._tie_in(placed, pitch), "tie_in", None)
        return (None, "", None)

    def _tie_source(self, label: int) -> Sounding | None:
        relation = self.relations.relations.get(label)
        if relation is None:
            return None
        carried = relation.carried
        if carried is not None:
            if carried.pitch is None:
                return None
            pitch = carried.pitch
            return Sounding(pitch.step, pitch.octave, pitch.alter)
        start = relation.start
        if start is None or start.member is None:
            return None
        return self.resolved.get((start.placed.ident, start.member))

    # Tie-in pitches.

    def _tie_in(self, placed: Placed, pitch: Pitch) -> Alter:
        source_frame = self._source_frame(placed, pitch)
        if source_frame is None:
            return None
        if source_frame == UNKNOWN:
            return DEFERRED
        frame = self.timeline.frames[source_frame]
        octave = pitch.octave + self.relations.shift_at(placed)
        measure_end = frame.origin + frame.length
        alters: set[Alter] = set()
        for other in self.timeline.events():
            if not (
                other.frame.index == frame.index
                and other.is_main_rhythmic
                and isinstance(other.event, Note)
                and other.staff == placed.staff
                and other.end_time == measure_end
            ):
                continue
            for member in range(1, len(other.event.pitches) + 1):
                sounding = self.resolved.get((other.ident, member))
                if sounding is not None and (
                    sounding.step,
                    sounding.octave,
                ) == (
                    pitch.step,
                    octave,
                ):
                    alters.add(sounding.alter)
        return self._source_alter(alters, pitch)

    def _source_alter(self, alters: set[Alter], pitch: Pitch) -> Alter:
        if not alters:
            self.report(
                "tie_in",
                "a tie-in pitch needs a source pitch of the same sounding step"
                " and octave that ends at the end of the source measure",
                pitch.location,
            )
            return None
        if None in alters:
            return None
        if DEFERRED in alters:
            return DEFERRED
        if len(alters) > 1:
            self.report(
                "tie_in",
                "all sources of a tie-in pitch shall have the same alteration",
                pitch.location,
            )
            return None
        return next(iter(alters))

    def _invalid_place(self, pitch: Pitch, reason: str) -> None:
        self.report("tie_in", f"a tie-in pitch {reason}", pitch.location)

    def _first_in_line(self, placed: Placed) -> bool:
        for other in self.timeline.events():
            if other.same_line(placed) and other.is_main_rhythmic:
                return other.ident == placed.ident
        return False

    def _source_frame(
        self,
        placed: Placed,
        pitch: Pitch,
    ) -> int | Literal["unknown"] | None:
        frame = placed.frame
        if not frame.owns_start or not self._first_in_line(placed):
            self._invalid_place(
                pitch,
                "shall lie in the first non-grace rhythmic event of its line"
                " in its measure",
            )
            return None
        ending = ending_of(frame)
        if isinstance(ending, EndingOption) and ending.kind in _NOT_FIRST:
            self._invalid_place(
                pitch, "shall lie in the first measure of a bracket"
            )
            return None
        if frame.index == 0:
            return self._outside(pitch)
        previous = self.timeline.frames[frame.index - 1]
        before = ending_of(previous)
        if before == UNKNOWN:
            return UNKNOWN
        repeat = first_option(previous.block, RightOption)
        if before is None or (
            ending is None
            and (repeat is None or repeat.barline is not Barline.REPEAT)
        ):
            self._invalid_place(
                pitch,
                "shall lie in a later bracket of a volta group or in the"
                " unlabeled last ending after right=repeat",
            )
            return None
        return self._walk_group(previous.index, pitch)

    def _outside(self, pitch: Pitch) -> Literal["unknown"] | None:
        if is_initial(self.document):
            self._invalid_place(
                pitch, "cannot lie in the first measure of a piece"
            )
            return None
        return UNKNOWN

    def _walk_group(
        self,
        index: int,
        pitch: Pitch,
    ) -> int | Literal["unknown"] | None:
        while index >= 0:
            ending = ending_of(self.timeline.frames[index])
            if ending == UNKNOWN:
                return UNKNOWN
            if ending is None:
                return index
            index -= 1
        return self._outside(pitch)


def resolve_pitches(
    document: Document,
    timeline: Timeline,
    relations: RelationAnalysis,
) -> PitchResolver:
    """Resolve and check every pitch of a document."""
    resolver = PitchResolver(document, timeline, relations)
    resolver.run()
    return resolver
