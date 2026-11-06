"""Assembly of Text Score 2 system documents into an assembled score.

``assemble`` follows the steps of the "Assembly" section of the format record.
It validates each system document, orders the documents by ``page`` and
``system``, checks the initial carry-in, the open relations at the end, each
carry link and each state link, joins split measures line by line, takes the
block options of each measure from its fragments, places the heading lines,
closes volta brackets left open at a document end, gives each arc the kind of
its end, renames relation labels globally and validates the assembled score,
which checks volta adjacency, tie-ins and deferred alterations across the
whole piece. Extended trills that end on ``<trill)>`` and review markers are
returned for a person to resolve, never as success.

Where the format record leaves a choice open, this module decides as follows.
A bracket that the last document leaves open is closed as at any other
document end, because no measure follows it. The identity of ``len`` across
the fragments of a split measure compares effective lengths, so a fragment
that writes a ``len`` equal to the meter matches one that omits it. A voice
line that is absent from a fragment leaves that fragment's range unfilled,
even when the voice starts later in the measure. An assembled score cannot
hold ``<trill)>``, so an extended trill that ends on it yields no score;
review markers stay in the score that is returned. Failures that the
assembled-score validator finds, such as a broken tie-in across two
documents, keep the validator's rule class and rule name and are located in
the source document that holds them.
"""

from __future__ import annotations

from dataclasses import dataclass, field, fields, is_dataclass, replace
from enum import StrEnum
from fractions import Fraction
from itertools import pairwise
from typing import TYPE_CHECKING, Final

from sheet_music_omr.carry import derive_carry_out
from sheet_music_omr.diagnostics import Diagnostic, Location, assembly
from sheet_music_omr.lexical import format_rational
from sheet_music_omr.model import (
    ArpeggioRelation,
    AssembledScore,
    CarryState,
    ClefChange,
    DashesEnd,
    DashesStart,
    EndingKind,
    EndingOption,
    EndpointRole,
    ExtendedTrillRelation,
    FromOption,
    HairpinEnd,
    HairpinStart,
    HeadedBlock,
    LenOption,
    MeasureBlock,
    Note,
    OttavaEnd,
    OttavaStart,
    PedalLine,
    Pitch,
    RightOption,
    SlurRelation,
    SystemDocument,
    ToOption,
    TremoloRelation,
    TrillEnd,
    VoiceLine,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.relations import ARC, TIE, analyze_relations, new_order
from sheet_music_omr.review import review_markers
from sheet_music_omr.semantics import analyze, validate_document
from sheet_music_omr.timeline import build_timeline, first_option
from sheet_music_omr.writer import write_carry_state, write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Iterator, Mapping, Sequence

    from sheet_music_omr.model import (
        Clef,
        Document,
        Heading,
        MeasureOption,
        Meter,
    )
    from sheet_music_omr.relations import Point
    from sheet_music_omr.review import ReviewMarker
    from sheet_music_omr.semantics import Analysis
    from sheet_music_omr.timeline import Frame

# Each source document's locations are moved by this many lines times its
# 1-based position, so that a location in the joined score names its source.
_STRIDE: Final = 1_000_000
_DOCUMENT_START: Final = Location(1, 1)
_SCORE: Final = "assembled score"
_OPENING: Final = frozenset({EndingKind.START, EndingKind.CONTINUE})
_LABELLED: Final = (
    SlurRelation,
    TremoloRelation,
    ArpeggioRelation,
    ExtendedTrillRelation,
    HairpinStart,
    HairpinEnd,
    OttavaStart,
    OttavaEnd,
    PedalLine,
    DashesStart,
    DashesEnd,
    TrillEnd,
)


class AssemblyStatus(StrEnum):
    """The outcome of an assembly."""

    SUCCESS = "success"
    NEEDS_PERSON = "needs-person"
    FAILED = "failed"


@dataclass(frozen=True, slots=True)
class SourceLocation:
    """A location in one source document."""

    source: str
    location: Location

    def __str__(self) -> str:
        """Return the location as ``source:line:column``."""
        return f"{self.source}:{self.location}"


@dataclass(frozen=True, slots=True)
class SourceDiagnostic:
    """A diagnostic located in one source document."""

    source: str
    diagnostic: Diagnostic

    def format(self) -> str:
        """Return the ``source:line:col: class/rule: message`` spelling."""
        return self.diagnostic.format(self.source)


@dataclass(frozen=True, slots=True)
class TrillReview:
    """An extended trill that ends on ``<trill)>``.

    Attributes:
        label: The label of the trill in the document that ends it.
        start: Where the trill starts.
        end: Where the ``<trill)>`` direction is written.
    """

    label: int
    start: SourceLocation
    end: SourceLocation


@dataclass(frozen=True, slots=True)
class MarkerReview:
    """A review marker of one source document."""

    source: str
    marker: ReviewMarker


@dataclass(frozen=True, slots=True)
class AssemblyResult:
    """The result of an assembly.

    Attributes:
        score: The assembled score, parsed from ``text``, when one exists.
        text: The canonical text of the assembled score.
        diagnostics: The failures, each located in its source document.
        trills: The extended trills that end on ``<trill)>``.
        markers: The review markers of the source documents.
    """

    score: AssembledScore | None
    text: str | None
    diagnostics: tuple[SourceDiagnostic, ...] = ()
    trills: tuple[TrillReview, ...] = ()
    markers: tuple[MarkerReview, ...] = ()

    @property
    def status(self) -> AssemblyStatus:
        """Return whether assembly succeeded, failed or needs a person."""
        if self.diagnostics:
            return AssemblyStatus.FAILED
        if self.trills or self.markers:
            return AssemblyStatus.NEEDS_PERSON
        return AssemblyStatus.SUCCESS


def _failed(diagnostics: Sequence[SourceDiagnostic]) -> AssemblyResult:
    return AssemblyResult(None, None, tuple(diagnostics))


@dataclass(frozen=True, slots=True)
class _Source:
    """A valid system document in assembly order."""

    position: int
    name: str
    document: SystemDocument
    analysis: Analysis

    @property
    def shift(self) -> int:
        return (self.position + 1) * _STRIDE

    def at(
        self, rule: str, message: str, location: Location | None
    ) -> SourceDiagnostic:
        return SourceDiagnostic(self.name, assembly(rule, message, location))

    def where(self, location: Location | None) -> SourceLocation:
        return SourceLocation(self.name, location or _DOCUMENT_START)


def assemble(
    documents: Sequence[Document],
    *,
    names: Sequence[str] | None = None,
    title: str | None = None,
    composer: str | None = None,
) -> AssemblyResult:
    """Assemble system documents into an assembled score.

    Args:
        documents: The parsed system documents, in any order.
        names: The name of each document in diagnostics, such as its path.
            Defaults to ``#1``, ``#2``, ... in input order.
        title: The optional title of the assembled score.
        composer: The optional composer of the assembled score.

    Returns:
        The result. Its status is ``failed`` when any rule is broken,
        ``needs-person`` when a trill or a review marker needs a person, and
        ``success`` otherwise.

    Raises:
        ValueError: No document is given, or the names do not match.
    """
    if not documents:
        message = "assembly needs at least one system document"
        raise ValueError(message)
    labels = list(names or (f"#{n}" for n in range(1, len(documents) + 1)))
    if len(labels) != len(documents):
        message = "give one name for each document"
        raise ValueError(message)
    found = list(_check_inputs(labels, documents))
    if found:
        return _failed(found)
    sources, found = _order(labels, documents)
    if not found:
        found = list(_check_links(sources))
    if found:
        return _failed(found)
    return _Assembler(sources, title, composer).run()


# Input checks and order.


def _check_inputs(
    names: Sequence[str], documents: Sequence[Document]
) -> Iterator[SourceDiagnostic]:
    for name, document in zip(names, documents, strict=True):
        if not isinstance(document, SystemDocument):
            diagnostic = assembly(
                "document_kind",
                "assembly takes system documents, not an assembled score",
                document.location,
            )
            yield SourceDiagnostic(name, diagnostic)
            continue
        for diagnostic in validate_document(document):
            yield SourceDiagnostic(name, diagnostic)


def _order(
    names: Sequence[str], documents: Sequence[Document]
) -> tuple[list[_Source], list[SourceDiagnostic]]:
    pairs = [
        (name, document)
        for name, document in zip(names, documents, strict=True)
        if isinstance(document, SystemDocument)
    ]
    pairs.sort(key=lambda pair: (pair[1].page, pair[1].system))
    sources = [
        _Source(position, name, document, analyze(document))
        for position, (name, document) in enumerate(pairs)
    ]
    found: list[SourceDiagnostic] = []
    for previous, current in pairwise(sources):
        before, after = previous.document, current.document
        if (before.page, before.system) == (after.page, after.system):
            found.append(
                current.at(
                    "order",
                    f"page {after.page} system {after.system} is also the"
                    f" place of {previous.name}",
                    after.page_location,
                ),
            )
    return sources, found


# Steps 1 to 3: carry links and state links.


def _carry_out(source: _Source) -> CarryState:
    analysis = source.analysis
    return derive_carry_out(
        source.document,
        analysis.timeline,
        analysis.relations,
        analysis.pitches,
    )


def _check_links(sources: Sequence[_Source]) -> Iterator[SourceDiagnostic]:
    yield from _check_initial(sources[0])
    for previous, current in pairwise(sources):
        yield from _check_carry(previous, current)
        yield from _check_state(previous, current)
    last = sources[-1]
    for relation in last.analysis.relations.relations.values():
        if relation.is_open:
            yield last.at(
                "relation_open",
                f"r{relation.label} is still open at the end of the last"
                " system document",
                relation.location,
            )


def _check_initial(first: _Source) -> Iterator[SourceDiagnostic]:
    document = first.document
    initial = CarryState(1, Fraction(0), (), document.carry, (), ())
    if document.carry_in != initial:
        yield first.at(
            "initial_carry",
            "the first system document shall start with the initial carry-in:"
            ' measure 1, offset "0" and nothing carried',
            document.carry_in.location,
        )


def _check_carry(
    previous: _Source, current: _Source
) -> Iterator[SourceDiagnostic]:
    expected = _carry_out(previous)
    actual = current.document.carry_in
    if actual == expected:
        return
    differing = next(
        item.name
        for item in fields(CarryState)
        if item.compare
        and getattr(actual, item.name) != getattr(expected, item.name)
    )
    yield current.at(
        "carry_in",
        f"carry-in.{differing} differs from the carry-out of {previous.name},"
        f" which is {write_carry_state(expected)}",
        actual.location,
    )


def _state_after(source: _Source) -> tuple[int, Meter, tuple[Clef, Clef]]:
    timeline = source.analysis.timeline
    last = timeline.frames[-1]
    clefs = list(source.document.clefs)
    changes = sorted(
        (p for p in timeline.events() if isinstance(p.event, ClefChange)),
        key=lambda placed: placed.time,
    )
    for placed in changes:
        if isinstance(placed.event, ClefChange):
            clefs[placed.staff - 1] = placed.event.clef
    return last.key, last.meter, (clefs[0], clefs[1])


def _check_state(
    previous: _Source, current: _Source
) -> Iterator[SourceDiagnostic]:
    # carry needs no check here: each carry-in scope equals its document's
    # carry, and the carry link compares the scopes.
    key, meter, clefs = _state_after(previous)
    header = current.document
    after = f"the state after {previous.name}"
    if header.key != key:
        yield current.at(
            "state",
            f"key {header.key} differs from {after}, key {key}",
            header.key_location,
        )
    if header.time != meter:
        yield current.at(
            "state",
            f"time {header.time.numerator}/{header.time.denominator} differs"
            f" from {after}, time {meter.numerator}/{meter.denominator}",
            header.time.location,
        )
    for staff, (written, expected) in enumerate(
        zip(header.clefs, clefs, strict=True), 1
    ):
        if written != expected:
            yield current.at(
                "state",
                f"the clef of staff {staff} differs from {after}",
                written.location,
            )


# Relation chains across documents.


@dataclass(slots=True)
class _Chains:
    """Global relation ids that follow carry links."""

    ids: list[dict[int, int]] = field(default_factory=list)
    starts: dict[int, tuple[_Source, Point]] = field(default_factory=dict)
    ends: dict[int, tuple[_Source, Point]] = field(default_factory=dict)
    count: int = 0

    @classmethod
    def build(cls, sources: Sequence[_Source]) -> _Chains:
        chains = cls()
        for source in sources:
            mapping: dict[int, int] = {}
            for label, relation in source.analysis.relations.relations.items():
                if relation.carried is None:
                    chains.count += 1
                    mapping[label] = chains.count
                else:
                    mapping[label] = chains.ids[-1][label]
                chain = mapping[label]
                if relation.carried is None and relation.points:
                    start = relation.start or relation.points[0]
                    chains.starts[chain] = (source, start)
                if relation.end is not None:
                    chains.ends[chain] = (source, relation.end)
            chains.ids.append(mapping)
        return chains

    def arcs(self) -> Iterator[tuple[int, Point, Point]]:
        """Yield each arc with its start and its end in a later document."""
        for chain, (_, start) in self.starts.items():
            if start.kind == ARC:
                yield chain, start, self.ends[chain][1]

    def trills(self) -> Iterator[TrillReview]:
        """Yield each extended trill that ends on ``<trill)>``."""
        for chain, (source, end) in self.ends.items():
            if isinstance(end.placed.event, TrillEnd):
                origin, start = self.starts[chain]
                yield TrillReview(
                    end.label,
                    origin.where(start.location),
                    source.where(end.location),
                )


# Tree rewriting.


type _Visit = Callable[[object, object], object]


def _map_tree(node: object, visit: _Visit) -> object:
    """Rebuild a syntax tree bottom-up, calling ``visit(old, rebuilt)``."""
    if isinstance(node, tuple):
        return tuple(_map_tree(item, visit) for item in node)
    if not is_dataclass(node) or isinstance(node, type):
        return node
    changes = {
        item.name: _map_tree(getattr(node, item.name), visit)
        for item in fields(node)
    }
    return visit(node, replace(node, **changes))


def _moved(location: Location | None, shift: int) -> Location:
    where = location or _DOCUMENT_START
    return Location(where.line + shift, where.column)


def _typed[T](value: object, kind: type[T]) -> T:
    if not isinstance(value, kind):
        message = f"expected {kind.__name__}, got {type(value).__name__}"
        raise TypeError(message)
    return value


@dataclass(frozen=True, slots=True)
class _Rewrite:
    """Moves locations, renames labels and resolves arcs."""

    shift: int
    labels: Mapping[int, int]
    ties: Mapping[int, int]
    slurs: Mapping[int, tuple[SlurRelation, ...]]

    def __call__(self, original: object, node: object) -> object:
        if isinstance(node, Location):
            return Location(node.line + self.shift, node.column)
        if isinstance(node, Pitch):
            return self._pitch(original, node)
        if isinstance(node, _LABELLED):
            return replace(node, label=self.labels[node.label])
        if isinstance(node, Note):
            added = self.slurs.get(id(original), ())
            return replace(node, flags=(*node.flags, *added))
        return node

    def _pitch(self, original: object, pitch: Pitch) -> Pitch:
        tie = self.ties.get(id(original))
        if tie is None and pitch.tie_start is not None:
            tie = self.labels[pitch.tie_start]
        end = pitch.tie_end
        return replace(
            pitch,
            tie_end=None if end is None else self.labels[end],
            tie_start=tie,
            arc_starts=(),
        )

    def blocks(
        self, blocks: Sequence[MeasureBlock]
    ) -> tuple[MeasureBlock, ...]:
        return tuple(
            _typed(_map_tree(block, self), MeasureBlock) for block in blocks
        )

    def headings(self, headings: tuple[Heading, ...]) -> tuple[Heading, ...]:
        return _typed(_map_tree(headings, self), tuple)


# Steps 4 to 6: measures.


@dataclass(frozen=True, slots=True)
class _Fragment:
    """A block of a source document, before and after rewriting."""

    source: _Source
    frame: Frame
    block: MeasureBlock

    @property
    def keys(self) -> set[tuple[int, int]]:
        return {(line.staff, line.voice) for line in self.block.voices}


def _groups(fragments: Sequence[_Fragment]) -> list[list[_Fragment]]:
    groups: list[list[_Fragment]] = []
    for fragment in fragments:
        if groups and groups[-1][-1].frame.measure == fragment.frame.measure:
            groups[-1].append(fragment)
        else:
            groups.append([fragment])
    return groups


def _check_len(group: Sequence[_Fragment]) -> Iterator[SourceDiagnostic]:
    first = group[0]
    for fragment in group[1:]:
        if fragment.frame.length == first.frame.length:
            continue
        option = first_option(fragment.frame.block, LenOption)
        location = fragment.frame.block.location
        yield fragment.source.at(
            "split_len",
            f"measure {fragment.frame.measure} has the length"
            f" {format_rational(fragment.frame.length)} here but"
            f" {format_rational(first.frame.length)} in {first.source.name};"
            " len is repeated identically on every fragment",
            location if option is None else option.location,
        )


def _gaps(
    group: Sequence[_Fragment], key: tuple[int, int]
) -> list[tuple[Fraction, Fraction, _Fragment]]:
    gaps: list[tuple[Fraction, Fraction, _Fragment]] = []
    for fragment in group:
        if key in fragment.keys:
            continue
        frame = fragment.frame
        if gaps and gaps[-1][1] == frame.start:
            gaps[-1] = (gaps[-1][0], frame.end, gaps[-1][2])
        else:
            gaps.append((frame.start, frame.end, fragment))
    last = group[-1]
    end, length = last.frame.end, last.frame.length
    if end < length:
        if gaps and gaps[-1][1] == end:
            gaps[-1] = (gaps[-1][0], length, gaps[-1][2])
        else:
            gaps.append((end, length, last))
    return gaps


def _check_fill(group: Sequence[_Fragment]) -> Iterator[SourceDiagnostic]:
    keys = sorted(set().union(*(fragment.keys for fragment in group)))
    for staff, voice in keys:
        for start, end, fragment in _gaps(group, (staff, voice)):
            yield fragment.source.at(
                "measure_fill",
                f"voice line {staff}.{voice} of measure"
                f" {fragment.frame.measure} leaves {format_rational(start)}"
                f" to {format_rational(end)} unfilled",
                fragment.frame.block.location,
            )


def _join(group: Sequence[_Fragment]) -> MeasureBlock:
    first, last = group[0].block, group[-1].block
    options: list[MeasureOption] = [
        option
        for option in first.options
        if not isinstance(option, (FromOption, ToOption, RightOption))
    ]
    options.extend(o for o in last.options if isinstance(o, RightOption))
    voices: list[VoiceLine] = []
    for staff, voice in sorted(set().union(*(f.keys for f in group))):
        lines = [
            line
            for fragment in group
            for line in fragment.block.voices
            if (line.staff, line.voice) == (staff, voice)
        ]
        events = tuple(event for line in lines for event in line.events)
        voices.append(
            VoiceLine(staff, voice, events, location=lines[0].location),
        )
    return MeasureBlock(
        first.number,
        tuple(options),
        tuple(voices),
        location=first.location,
    )


def _close_bracket(
    block: MeasureBlock, following: MeasureBlock | None
) -> MeasureBlock:
    ending = first_option(block, EndingOption)
    if ending is None or ending.kind not in _OPENING:
        return block
    if following is not None and first_option(following, EndingOption):
        return block
    closed = (
        replace(ending, kind=EndingKind.SINGLE)
        if ending.kind is EndingKind.START
        else replace(ending, kind=EndingKind.END)
    )
    options = tuple(closed if o is ending else o for o in block.options)
    return replace(block, options=options)


# The assembler.


class _Assembler:
    """Runs steps 4 to 8 on documents whose links are valid."""

    def __init__(
        self,
        sources: Sequence[_Source],
        title: str | None,
        composer: str | None,
    ) -> None:
        self.sources = sources
        self.title = title
        self.composer = composer
        self.chains = _Chains.build(sources)
        self.found: list[SourceDiagnostic] = []

    def run(self) -> AssemblyResult:
        rewrites = self._rewrites()
        fragments = [
            _Fragment(source, frame, block)
            for source in self.sources
            for frame, block in zip(
                source.analysis.timeline.frames,
                rewrites[source.position].blocks(source.document.blocks),
                strict=True,
            )
        ]
        groups = _groups(fragments)
        for group in groups:
            self.found.extend(_check_len(group))
            self.found.extend(_check_fill(group))
        if self.found:
            return _failed(self.found)
        score = self._renumber(self._score(groups, rewrites))
        return self._finish(score)

    def _rewrites(self) -> list[_Rewrite]:
        ties: dict[int, int] = {}
        slurs: dict[int, list[SlurRelation]] = {}
        for chain, start, end in self.chains.arcs():
            source = self.chains.starts[chain][0]
            if end.kind != TIE:
                location = _moved(start.location, source.shift)
                relation = SlurRelation(
                    chain, EndpointRole.START, location=location
                )
                slurs.setdefault(id(start.placed.event), []).append(relation)
            elif id(start.pitch) in ties:
                self.found.append(
                    source.at(
                        "arc",
                        "two arcs that start on one pitch both end as ties,"
                        " and a pitch can start only one tie",
                        start.location,
                    ),
                )
            else:
                ties[id(start.pitch)] = chain
        frozen = {key: tuple(value) for key, value in slurs.items()}
        return [
            _Rewrite(
                source.shift, self.chains.ids[source.position], ties, frozen
            )
            for source in self.sources
        ]

    def _score(
        self,
        groups: Sequence[Sequence[_Fragment]],
        rewrites: Sequence[_Rewrite],
    ) -> AssembledScore:
        ends = {source.document.blocks[-1].number for source in self.sources}
        blocks = [_join(group) for group in groups]
        headed: list[HeadedBlock] = []
        for index, (group, block) in enumerate(
            zip(groups, blocks, strict=True)
        ):
            following = blocks[index + 1] if index + 1 < len(blocks) else None
            closed = (
                _close_bracket(block, following)
                if block.number in ends
                else block
            )
            headings: tuple[Heading, ...] = ()
            first = group[0]
            if first.frame.index == 0:
                rewrite = rewrites[first.source.position]
                headings = rewrite.headings(first.source.document.headings)
            headed.append(HeadedBlock(headings, closed))
        first = self.sources[0].document
        return AssembledScore(
            self.title,
            self.composer,
            first.key,
            first.time,
            first.clefs,
            first.carry,
            tuple(headed),
            location=first.location,
        )

    @staticmethod
    def _renumber(score: AssembledScore) -> AssembledScore:
        relations = analyze_relations(score, build_timeline(score)).relations
        ordered = sorted(relations.values(), key=new_order)
        labels = {r.label: n for n, r in enumerate(ordered, 1)}
        rewrite = _Rewrite(0, labels, {}, {})
        renamed = rewrite.blocks([item.block for item in score.blocks])
        blocks = tuple(
            HeadedBlock(item.headings, block)
            for item, block in zip(score.blocks, renamed, strict=True)
        )
        return replace(score, blocks=blocks)

    def _finish(self, score: AssembledScore) -> AssemblyResult:
        trills = tuple(self.chains.trills())
        trill_ends = {
            _moved(end.location, source.shift)
            for source, end in self.chains.ends.values()
            if isinstance(end.placed.event, TrillEnd)
        }
        for diagnostic in validate_document(score):
            if diagnostic.code == "semantic/ornament" and (
                diagnostic.location in trill_ends
            ):
                continue
            self.found.append(self._locate(diagnostic))
        markers = tuple(
            MarkerReview(source.name, marker)
            for source in self.sources
            for marker in review_markers(source.document)
        )
        if self.found:
            return _failed(self.found)
        if trills:
            return AssemblyResult(None, None, (), trills, markers)
        text = write_document(score)
        parsed = _typed(parse_document(text), AssembledScore)
        return AssemblyResult(parsed, text, (), (), markers)

    def _locate(self, diagnostic: Diagnostic) -> SourceDiagnostic:
        position, line = divmod(diagnostic.location.line, _STRIDE)
        if position == 0:
            return SourceDiagnostic(_SCORE, diagnostic)
        source = self.sources[position - 1]
        location = Location(line, diagnostic.location.column)
        return SourceDiagnostic(
            source.name, replace(diagnostic, location=location)
        )
