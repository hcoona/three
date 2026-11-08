"""Run the system-assembly test of the comparison protocol on one gold piece.

The test splits the whole-piece encoding, the canonical text of ``reverse(X)``,
at the printed system breaks of ``X``, assembles the system documents and
compares the assembled score with the whole piece: equal canonical texts pass,
and otherwise both texts are converted forward and compared with the content
pass and the supplementary checks, with the whole piece as the reference.

Where the protocol and the format record leave a choice open, this module
decides as follows, and fails closed where no reading is safe.

- A break is a ``print`` element with ``new-system="yes"`` or
  ``new-page="yes"`` in the first part. A break in the first measure starts no
  new system, because the first system starts at measure 1 anyway. A page
  break starts system 1 of the next page and a system break the next system of
  its page; the first page is page 1. A piece whose ``X`` has no other break
  is not evidence and does not pass.
- A break ``print`` that follows a ``note``, ``backup`` or ``forward`` in its
  measure starts a system inside the measure. The protocol states that gold
  breaks fall at measure boundaries, so such a piece fails the split rather
  than being split at a guessed boundary.
- System document ``k`` holds the blocks from its first measure to the measure
  before the next system. Its ``carry-in`` is the carry-out of document
  ``k - 1``, and the first one takes the initial state. Its ``key``, ``time``
  and ``clefs`` headers are the state after document ``k - 1``, the last key
  and meter and the clefs after every clef direction, as assembly checks them;
  the first one takes the headers of the whole piece.
- The heading lines before a system's first block become the document's
  heading lines. A heading line before any other block precedes no system, so
  the split fails; ``reverse`` writes heading lines only before measure 1.
- A tie or slur that crosses a system edge keeps its kind, so a carried tie
  stays a tie instead of the ``arc`` that a transcription of the printed
  system writes for a tie at the right edge. The format's carry rules allow a
  carried tie, and assembly gives an arc the kind of its end, so the assembled
  text is the same either way. The split therefore does not exercise the
  assembly of arcs.
- Labels are renumbered in each document by the format's rule: a carried
  relation keeps its ``carry-in`` label, carried relations whose entries
  differ only by label take the group's labels in the order of their last
  endpoints, and each new relation takes the smallest unused label in the
  order of ``new_order``.
- Each document is written in canonical form and parsed back, so it is the
  document that its canonical text holds, and then validated. A document that
  does not validate fails the split.
- The outcome details name rule codes, systems and measure numbers only, never
  score content.

``ProtocolDefectError`` and ``InstrumentMismatchError`` of the comparison
instrument propagate, because the protocol stops the run on them.
"""

from __future__ import annotations

from dataclasses import dataclass, fields, is_dataclass, replace
from enum import StrEnum
from fractions import Fraction
from itertools import pairwise
from typing import TYPE_CHECKING, Final

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.comparison import ScoreParseError
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.model import (
    ArpeggioRelation,
    CarryState,
    ClefChange,
    DashesEnd,
    DashesStart,
    ExtendedTrillRelation,
    HairpinEnd,
    HairpinStart,
    OttavaEnd,
    OttavaStart,
    PedalLine,
    Pitch,
    SlurRelation,
    SystemDocument,
    TremoloRelation,
    TrillEnd,
)
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
    from_musicxml,
)

# The reverse converter's safe loader (size limit, .mxl, no DTD entities);
# musicxml_events reuses it the same way.
from sheet_music_omr.musicxml_reverse import _load as _load_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.relations import new_order
from sheet_music_omr.semantics import analyze, carry_out, validate_document
from sheet_music_omr.supplementary import compare_file
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Mapping, Sequence
    from xml.etree.ElementTree import Element

    from sheet_music_omr.assembly import SourceDiagnostic
    from sheet_music_omr.diagnostics import Diagnostic
    from sheet_music_omr.model import (
        AssembledScore,
        Clef,
        Document,
        MeasureBlock,
        Meter,
    )
    from sheet_music_omr.relations import Relation

_YES: Final = "yes"
_BEFORE_BREAK: Final = frozenset({"note", "backup", "forward"})
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

type _Clefs = tuple[Clef, Clef]


class AssemblyTestStatus(StrEnum):
    """The outcome of the system-assembly test for one piece."""

    EQUAL = "equal"
    """The assembled canonical text equals the whole-piece text."""
    COMPARED_PASS = "compared-pass"  # noqa: S105 - a status, not a secret.
    """The texts differ, but the comparison counts no difference."""
    COMPARED_FAIL = "compared-fail"
    """The texts differ, and the comparison counts differences."""
    NO_BREAKS = "no-breaks"
    """``X`` has no system or page break; the piece is not evidence."""
    REVERSE_FAILED = "reverse-failed"
    """``X`` cannot be read or converted to the whole-piece encoding."""
    SPLIT_FAILED = "split-failed"
    """The whole-piece encoding cannot be split into valid documents."""
    ASSEMBLY_FAILED = "assembly-failed"
    """Assembly failed or needs a person."""
    CONVERT_FAILED = "convert-failed"
    """The forward converter rejected one of the two texts."""
    COMPARE_FAILED = "compare-failed"
    """The comparison instrument cannot parse one of the two results."""


_PASSING: Final = frozenset(
    {AssemblyTestStatus.EQUAL, AssemblyTestStatus.COMPARED_PASS}
)


@dataclass(frozen=True, slots=True)
class AssemblyTestResult:
    """The system-assembly test result of one piece.

    Attributes:
        status: The outcome.
        systems: The number of systems, 1 for a piece without breaks.
        texts_differ: Whether the assembled text differs from the whole-piece
            text, or ``None`` when no assembled text exists.
        differences: The counted differences of the comparison: counted
            content differences plus non-identical and extra elements of the
            supplementary checks. Zero unless the texts were compared.
        category_counts: The differences per content category, sorted by
            category.
        excluded_voices: The excluded reference voices of the comparison's
            voice-order check, which are neither elements nor agreement.
        detail: Rule codes, systems and measure numbers of a failure.
    """

    status: AssemblyTestStatus
    systems: int
    texts_differ: bool | None = None
    differences: int = 0
    category_counts: tuple[tuple[str, int], ...] = ()
    excluded_voices: int = 0
    detail: str = ""

    @property
    def passed(self) -> bool:
        """Return whether the piece passes the test."""
        return self.status in _PASSING


@dataclass(frozen=True, slots=True)
class SystemStart:
    """The first measure of one printed system.

    Attributes:
        ordinal: The 0-based ordinal of the measure in the part, which is the
            index of its block in the whole-piece encoding.
        page: The 1-based page number.
        system: The 1-based system number on its page.
    """

    ordinal: int
    page: int
    system: int


@dataclass(frozen=True, slots=True)
class SystemLayout:
    """The printed systems of a piece.

    Attributes:
        measures: The number of measures in the first part.
        starts: The start of each system, the first at ordinal 0.
    """

    measures: int
    starts: tuple[SystemStart, ...]

    @property
    def has_breaks(self) -> bool:
        """Return whether the piece has more than one system."""
        return len(self.starts) > 1


class SplitError(Exception):
    """Raised when the whole-piece encoding cannot be split.

    Attributes:
        detail: Rule codes, systems and measure numbers, without content.
    """

    def __init__(self, detail: str) -> None:
        """Create an error with a content-free detail."""
        super().__init__(detail)
        self.detail = detail


# Breaks.


def system_starts(x: bytes | str) -> SystemLayout:
    """Return the printed systems of a MusicXML piece.

    Args:
        x: A ``score-partwise`` document or the bytes of an ``.mxl`` file.

    Returns:
        The measure count of the first part and the start of each system.

    Raises:
        MusicXmlError: ``x`` cannot be read.
        SplitError: A break starts a system inside a measure.
    """
    part = _load_musicxml(x).find("part")
    measures = [] if part is None else part.findall("measure")
    starts = [SystemStart(0, 1, 1)]
    for ordinal, measure in enumerate(measures):
        kind = _break_kind(measure)
        if kind is None or ordinal == 0:
            continue
        last = starts[-1]
        if kind == "new-page":
            starts.append(SystemStart(ordinal, last.page + 1, 1))
        else:
            starts.append(SystemStart(ordinal, last.page, last.system + 1))
    return SystemLayout(len(measures), tuple(starts))


def _break_kind(measure: Element) -> str | None:
    kind = None
    seen_music = False
    for child in measure:
        if child.tag in _BEFORE_BREAK:
            seen_music = True
            continue
        if child.tag != "print":
            continue
        if child.get("new-page") == _YES:
            found = "new-page"
        elif child.get("new-system") == _YES:
            found = "new-system"
        else:
            continue
        if seen_music:
            number = measure.get("number", "?")
            message = f"musicxml/break_inside_measure (measure {number})"
            raise SplitError(message)
        if kind != "new-page":
            kind = found
    return kind


# Split.


def split_score(
    score: AssembledScore, starts: Sequence[SystemStart]
) -> tuple[SystemDocument, ...]:
    """Split an assembled score into valid system documents.

    Args:
        score: The whole-piece encoding.
        starts: The start of each system, in order, the first at ordinal 0.

    Returns:
        One system document per start, each parsed from its canonical text.

    Raises:
        SplitError: A document cannot be built or does not validate.
    """
    blocks = score.blocks
    ordinals = [start.ordinal for start in starts]
    if (
        not ordinals
        or ordinals[0] != 0
        or any(b <= a for a, b in pairwise(ordinals))
        or ordinals[-1] >= len(blocks)
    ):
        message = (
            f"split/layout (system starts {ordinals}, {len(blocks)} blocks)"
        )
        raise SplitError(message)
    carry_in = CarryState(1, Fraction(0), (), score.carry, (), ())
    key, meter, clefs = score.key, score.time, score.clefs
    carried: dict[int, int] = {}
    documents: list[SystemDocument] = []
    bounds = pairwise([*ordinals, len(blocks)])
    for number, (start, (first, end)) in enumerate(
        zip(starts, bounds, strict=True), 1
    ):
        headed = blocks[first:end]
        for item in headed[1:]:
            if item.headings:
                message = (
                    f"split/heading_inside_system (system {number},"
                    f" measure {item.block.number})"
                )
                raise SplitError(message)
        draft = SystemDocument(
            key,
            meter,
            clefs,
            score.carry,
            start.page,
            start.system,
            carry_in,
            headed[0].headings,
            tuple(item.block for item in headed),
        )
        document, names = _numbered(draft, carried, number)
        documents.append(document)
        carry_in = carry_out(document)
        carried = {names[entry.label]: entry.label for entry in carry_in.open}
        key, meter, clefs = _state_after(document)
    return tuple(documents)


def _numbered(
    draft: SystemDocument, carried: Mapping[int, int], number: int
) -> tuple[SystemDocument, dict[int, int]]:
    """Renumber the labels of a draft written with whole-piece labels.

    Args:
        draft: The document, with the whole-piece label of each relation.
        carried: The ``carry-in`` label of each carried whole-piece label.
        number: The 1-based system number in the piece, for details.

    Returns:
        The parsed and validated document, and the whole-piece label of each
        of its labels.

    Raises:
        SplitError: The document does not validate.
    """
    written = _labels(draft) | set(carried)
    offset = max(written | set(carried.values()), default=0)
    provisional = {
        label: carried.get(label, offset + label) for label in written
    }
    relations = analyze(_relabelled(draft, provisional)).relations.relations
    final = _final_labels(relations.values())
    labels = {label: final[provisional[label]] for label in written}
    try:
        document = parse_document(write_document(_relabelled(draft, labels)))
    except TextScoreError as error:
        raise SplitError(_describe(error.diagnostics, None, number)) from None
    if not isinstance(document, SystemDocument):
        message = f"split/document_kind (system {number})"
        raise SplitError(message)
    diagnostics = validate_document(document)
    if diagnostics:
        raise SplitError(_describe(diagnostics, document, number))
    return document, {new: old for old, new in labels.items()}


def _final_labels(relations: Iterable[Relation]) -> dict[int, int]:
    """Number relations by the format's rule, keyed by their draft label."""
    items = list(relations)
    final: dict[int, int] = {}
    groups: dict[object, list[Relation]] = {}
    for relation in items:
        if relation.carried is not None:
            final[relation.label] = relation.label
            anonymous = replace(relation.carried, label=0)
            groups.setdefault(anonymous, []).append(relation)
    for group in groups.values():
        labels = sorted(relation.label for relation in group)
        ended = sorted(
            (r for r in group if not r.is_open),
            key=lambda r: (r.last_key, r.label),
        )
        still_open = sorted(
            (r for r in group if r.is_open), key=lambda r: r.label
        )
        for relation, label in zip([*ended, *still_open], labels, strict=True):
            final[relation.label] = label
    used = set(final.values())
    new = sorted((r for r in items if r.carried is None), key=new_order)
    for relation in new:
        label = next(n for n in range(1, len(used) + 2) if n not in used)
        used.add(label)
        final[relation.label] = label
    return final


def _state_after(document: SystemDocument) -> tuple[int, Meter, _Clefs]:
    """Return the key, meter and clefs after a document, as assembly does."""
    timeline = build_timeline(document)
    last = timeline.frames[-1]
    clefs = list(document.clefs)
    changes = sorted(
        (p for p in timeline.events() if isinstance(p.event, ClefChange)),
        key=lambda placed: placed.time,
    )
    for placed in changes:
        if isinstance(placed.event, ClefChange):
            clefs[placed.staff - 1] = placed.event.clef
    return last.key, last.meter, (clefs[0], clefs[1])


# Labels in a syntax tree.


def _map_tree(node: object, visit: Callable[[object], object]) -> object:
    """Rebuild a syntax tree bottom-up, calling ``visit`` on each node."""
    if isinstance(node, tuple):
        return tuple(_map_tree(item, visit) for item in node)
    if not is_dataclass(node) or isinstance(node, type):
        return node
    changes = {
        item.name: _map_tree(getattr(node, item.name), visit)
        for item in fields(node)
    }
    return visit(replace(node, **changes))


def _labels(document: SystemDocument) -> set[int]:
    """Return every relation label written in a document's blocks."""
    found: set[int] = set()

    def collect(node: object) -> object:
        if isinstance(node, Pitch):
            found.update(
                label
                for label in (node.tie_start, node.tie_end)
                if label is not None
            )
            found.update(node.arc_starts)
        elif isinstance(node, _LABELLED):
            found.add(node.label)
        return node

    _map_tree(document.blocks, collect)
    return found


def _relabelled(
    document: SystemDocument, labels: Mapping[int, int]
) -> SystemDocument:
    """Return the document with each label of its blocks renamed."""

    def rename(node: object) -> object:
        if isinstance(node, Pitch):
            start, end = node.tie_start, node.tie_end
            return replace(
                node,
                tie_start=None if start is None else labels[start],
                tie_end=None if end is None else labels[end],
                arc_starts=tuple(labels[a] for a in node.arc_starts),
            )
        if isinstance(node, _LABELLED):
            return replace(node, label=labels[node.label])
        return node

    blocks = _map_tree(document.blocks, rename)
    if not isinstance(blocks, tuple):
        message = "the blocks of a document are a tuple"
        raise TypeError(message)
    return replace(document, blocks=blocks)


# Details.


def _measure_at(document: Document, line: int) -> int | None:
    """Return the number of the block that holds a text line, if any."""
    number = None
    for block in _blocks_of(document):
        if block.location is not None and block.location.line <= line:
            number = block.number
    return number


def _blocks_of(document: Document) -> tuple[MeasureBlock, ...]:
    if isinstance(document, SystemDocument):
        return document.blocks
    return tuple(headed.block for headed in document.blocks)


def _describe(
    diagnostics: Iterable[Diagnostic], document: Document | None, system: int
) -> str:
    """Return the rule codes and measures of diagnostics, without content.

    ``document`` is the parsed document that the diagnostic locations refer
    to, or ``None`` when they refer to text that did not parse.
    """
    items = []
    for diagnostic in diagnostics:
        location = diagnostic.location
        measure = None
        if document is not None and location is not None:
            measure = _measure_at(document, location.line)
        where = "" if measure is None else f", measure {measure}"
        items.append(f"{diagnostic.code} (system {system}{where})")
    return "; ".join(dict.fromkeys(items))


def _describe_assembly(
    diagnostics: Iterable[SourceDiagnostic],
    documents: Sequence[SystemDocument],
    names: Sequence[str],
) -> str:
    position = {name: index for index, name in enumerate(names)}
    items = []
    for item in diagnostics:
        index = position.get(item.source)
        if index is None:
            items.append(f"{item.diagnostic.code} (assembled score)")
        else:
            items.append(
                _describe((item.diagnostic,), documents[index], index + 1)
            )
    return "; ".join(dict.fromkeys(items))


def _describe_reverse(error: MusicXmlError) -> str:
    items = [
        diagnostic.code
        if diagnostic.measure is None
        else f"{diagnostic.code} (measure {diagnostic.measure})"
        for diagnostic in error.diagnostics
    ]
    return "; ".join(dict.fromkeys(items))


# The test.


def assembly_test(x: bytes | str) -> AssemblyTestResult:
    """Run the system-assembly test on one gold piece.

    Args:
        x: The gold MusicXML document, or the bytes of an ``.mxl`` file.

    Returns:
        The outcome. Only ``equal`` and ``compared-pass`` pass.

    Raises:
        ProtocolDefectError: The comparison cannot assign a difference.
        InstrumentMismatchError: The installed musicdiff is not the pinned
            instrument.
    """
    systems = 1
    try:
        layout = system_starts(x)
        systems = len(layout.starts)
        if not layout.has_breaks:
            return AssemblyTestResult(AssemblyTestStatus.NO_BREAKS, systems)
        whole = from_musicxml(x)
        documents = _split_piece(whole, layout)
    except MusicXmlError as error:
        return AssemblyTestResult(
            AssemblyTestStatus.REVERSE_FAILED,
            systems,
            detail=_describe_reverse(error),
        )
    except SplitError as error:
        return AssemblyTestResult(
            AssemblyTestStatus.SPLIT_FAILED, systems, detail=error.detail
        )
    return compare_assembly(whole, documents)


def _split_piece(
    whole: AssembledScore, layout: SystemLayout
) -> tuple[SystemDocument, ...]:
    if len(whole.blocks) != layout.measures:
        message = (
            f"split/measure_count ({layout.measures} measures,"
            f" {len(whole.blocks)} blocks)"
        )
        raise SplitError(message)
    return split_score(whole, layout.starts)


def compare_assembly(
    whole: AssembledScore, documents: Sequence[SystemDocument]
) -> AssemblyTestResult:
    """Assemble system documents and compare the result with a whole piece.

    Args:
        whole: The whole-piece encoding, which is the reference.
        documents: The system documents that split it.

    Returns:
        The outcome of assembly and of the comparison, with
        ``assembly-failed`` when assembly does not succeed.

    Raises:
        ProtocolDefectError: The comparison cannot assign a difference.
        InstrumentMismatchError: The installed musicdiff is not the pinned
            instrument.
    """
    systems = len(documents)
    names = [f"system-{n}" for n in range(1, systems + 1)]
    result = assemble(
        documents, names=names, title=whole.title, composer=whole.composer
    )
    if result.status is not AssemblyStatus.SUCCESS or result.score is None:
        detail = _describe_assembly(result.diagnostics, documents, names)
        if result.status is AssemblyStatus.NEEDS_PERSON:
            detail = "assembly/needs_person"
        return AssemblyTestResult(
            AssemblyTestStatus.ASSEMBLY_FAILED, systems, detail=detail
        )
    if result.text == write_document(whole):
        return AssemblyTestResult(
            AssemblyTestStatus.EQUAL, systems, texts_differ=False
        )
    converted: list[str] = []
    for side, score in (("assembled", result.score), ("whole", whole)):
        try:
            converted.append(to_musicxml(score).decode("utf-8"))
        except TextScoreError as error:
            codes = "; ".join(dict.fromkeys(d.code for d in error.diagnostics))
            return AssemblyTestResult(
                AssemblyTestStatus.CONVERT_FAILED,
                systems,
                texts_differ=True,
                detail=f"{side}: {codes}",
            )
        except ReviewRequiredError:
            return AssemblyTestResult(
                AssemblyTestStatus.CONVERT_FAILED,
                systems,
                texts_differ=True,
                detail=f"{side}: review_required",
            )
    try:
        comparison = compare_file(converted[0], converted[1])
    except ScoreParseError:
        return AssemblyTestResult(
            AssemblyTestStatus.COMPARE_FAILED,
            systems,
            texts_differ=True,
            detail="comparison/score_parse",
        )
    checks = comparison.check_differences().values()
    differences = len(comparison.content.counted) + sum(
        non_identical + extras for non_identical, extras in checks
    )
    status = (
        AssemblyTestStatus.COMPARED_FAIL
        if differences
        else AssemblyTestStatus.COMPARED_PASS
    )
    return AssemblyTestResult(
        status,
        systems,
        texts_differ=True,
        differences=differences,
        category_counts=tuple(sorted(comparison.category_counts().items())),
        excluded_voices=comparison.excluded_voices.total(),
    )
