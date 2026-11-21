"""Beam run rules of the "Beams" section.

Beam runs are checked per voice line of one block. Directions are ignored,
and grace notes and other events form separate sequences, so that they never
mix in a run. A run that is open at the end of its line, or that continues a
run from before its line, crosses a barline or a fragment boundary, which the
format lists as unsupported content.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Final

from sheet_music_omr.diagnostics import semantic, unsupported
from sheet_music_omr.model import Beam, BeamState, Note, Rest, Spacer

if TYPE_CHECKING:
    from collections.abc import Iterator

    from sheet_music_omr.diagnostics import Diagnostic, Location
    from sheet_music_omr.timeline import Line, Placed

_EIGHTH: Final = 8
_RUNNING = frozenset({BeamState.START, BeamState.CONTINUE})
_JOINING = frozenset({BeamState.CONTINUE, BeamState.END})
_HOOKS = frozenset({BeamState.HOOK_FORWARD, BeamState.HOOK_BACKWARD})
_HOOK_NEEDS = {
    BeamState.HOOK_FORWARD: _RUNNING,
    BeamState.HOOK_BACKWARD: _JOINING,
}


def flag_count(base: int) -> int:
    """Return the number of flags of a written value, 0 for a quarter."""
    count = 0
    while base >= _EIGHTH:
        count += 1
        base //= 2
    return count


def _beams(placed: Placed) -> dict[int, Beam]:
    event = placed.event
    flags = event.beams if isinstance(event, Spacer) else ()
    if isinstance(event, (Note, Rest)):
        flags = event.flags
    beams: dict[int, Beam] = {}
    for flag in flags:
        if isinstance(flag, Beam):
            beams.setdefault(flag.level, flag)
    return beams


def _sequences(line: Line) -> list[list[Placed]]:
    main: list[Placed] = []
    sequences: list[list[Placed]] = [main]
    grace: list[Placed] | None = None
    for placed in line.events:
        if placed.is_direction:
            continue
        if placed.is_grace:
            if grace is None:
                grace = []
                sequences.append(grace)
            grace.append(placed)
        else:
            grace = None
            main.append(placed)
    return sequences


def _check_levels(
    placed: Placed, beams: dict[int, Beam]
) -> Iterator[Diagnostic]:
    if not beams:
        return
    event = placed.event
    location = next(iter(beams.values())).location
    for level, beam in beams.items():
        if level > 1 and level - 1 not in beams:
            yield semantic(
                "beam",
                f"beam{level} requires beam{level - 1} on the same event",
                beam.location,
            )
    count = 0
    if isinstance(event, (Note, Rest, Spacer)):
        count = flag_count(event.duration.base)
    top = max(beams)
    if isinstance(event, Note) and top != count:
        yield semantic(
            "beam",
            f"the highest beam level of a note shall be its flag count {count}",
            location,
        )
    elif top > count:
        yield semantic(
            "beam",
            f"a rest or spacer carries at most its flag count {count} of beams",
            location,
        )


class _RunChecker:
    """Follows the open runs of one sequence of events."""

    def __init__(self) -> None:
        self.open: dict[int, Location | None] = {}
        self.seen: set[int] = set()

    def step(self, beams: dict[int, Beam]) -> Iterator[Diagnostic]:
        levels = set(beams) | set(self.open)
        for level in sorted(levels):
            beam = beams.get(level)
            if beam is None:
                yield semantic(
                    "beam",
                    f"the beam{level} run ends without an end state",
                    self.open.pop(level),
                )
                continue
            yield from self._state(level, beam, beams)
            self.seen.add(level)

    def _state(
        self,
        level: int,
        beam: Beam,
        beams: dict[int, Beam],
    ) -> Iterator[Diagnostic]:
        state = beam.state
        is_open = level in self.open
        if state in _HOOKS:
            yield from self._hook(level, beam, beams, is_open=is_open)
        elif state is BeamState.START:
            if is_open:
                yield semantic(
                    "beam",
                    f"beam{level}=start inside an open run",
                    beam.location,
                )
            self.open[level] = beam.location
        elif not is_open:
            yield self._orphan(level, beam)
        elif state is BeamState.END:
            del self.open[level]
        parent = beams.get(level - 1)
        if (
            level > 1
            and state in _RUNNING
            and parent is not None
            and parent.state not in _RUNNING
        ):
            yield semantic(
                "beam",
                f"the beam{level} run outlives its beam{level - 1} run",
                beam.location,
            )

    def _hook(
        self,
        level: int,
        beam: Beam,
        beams: dict[int, Beam],
        *,
        is_open: bool,
    ) -> Iterator[Diagnostic]:
        primary = beams.get(1)
        needs = _HOOK_NEEDS[beam.state]
        if level == 1 or primary is None or primary.state not in needs:
            yield semantic(
                "beam",
                f"beam{level}={beam.state} needs a level-1 run with an event"
                " on its side",
                beam.location,
            )
        if is_open:
            yield semantic(
                "beam",
                f"beam{level}={beam.state} lies inside a run of its level",
                beam.location,
            )

    def _orphan(self, level: int, beam: Beam) -> Diagnostic:
        message = f"beam{level}={beam.state} without an open run"
        if level in self.seen:
            return semantic("beam", message, beam.location)
        return unsupported(
            "beam_boundary",
            f"{message}; a beam run across a system or fragment boundary"
            " is unsupported",
            beam.location,
        )

    def finish(self) -> Iterator[Diagnostic]:
        for level, location in sorted(self.open.items()):
            yield unsupported(
                "beam_boundary",
                f"the beam{level} run is open at the end of its run; a beam"
                " run across a system or fragment boundary is unsupported",
                location,
            )


def _continues_across_barline(
    previous: tuple[Line, Placed, _RunChecker] | None,
    line: Line,
    main: list[Placed],
) -> bool:
    if previous is None or not previous[2].open or not main:
        return False
    prev_line, prev_last, _ = previous
    return (
        prev_line.frame.index + 1 == line.frame.index
        and prev_line.frame.owns_end
        and line.frame.owns_start
        and line.frame.measure == prev_line.frame.measure + 1
        and prev_last.onset + prev_last.duration == prev_line.frame.length
        and main[0].onset == 0
        and not any(
            e.is_grace and e.position < main[0].position for e in line.events
        )
    )


def _check_sequence(sequence: list[Placed]) -> Iterator[Diagnostic]:
    checker = _RunChecker()
    for placed in sequence:
        beams = _beams(placed)
        yield from _check_levels(placed, beams)
        yield from checker.step(beams)
    yield from checker.finish()


def _check_voice_lines(voice_lines: list[Line]) -> Iterator[Diagnostic]:
    previous: tuple[Line, Placed, _RunChecker] | None = None
    for line in voice_lines:
        main, *graces = _sequences(line)
        if _continues_across_barline(previous, line, main):
            assert previous is not None  # noqa: S101
            checker = previous[2]
        else:
            if previous is not None:
                yield from previous[2].finish()
            checker = _RunChecker()
        for placed in main:
            beams = _beams(placed)
            yield from _check_levels(placed, beams)
            yield from checker.step(beams)
        previous = (line, main[-1], checker) if main else None
        for grace_seq in graces:
            yield from _check_sequence(grace_seq)
    if previous is not None:
        yield from previous[2].finish()


def check_beams(lines: tuple[Line, ...]) -> Iterator[Diagnostic]:
    """Check the beam runs of every voice line, including across barlines."""
    by_voice: dict[tuple[int, int], list[Line]] = {}
    for line in lines:
        by_voice.setdefault((line.line.staff, line.line.voice), []).append(line)
    for voice_lines in by_voice.values():
        yield from _check_voice_lines(voice_lines)
