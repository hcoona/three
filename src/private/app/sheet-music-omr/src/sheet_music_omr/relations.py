"""Relation rules of the "Relations" and "Directions" sections.

A relation is the set of endpoints that share one label, together with the
``carry-in`` entry that carries it into a system document. The checks cover
endpoint kinds and variants, the placement rules of each kind, open relations,
duplicate relations and the numbering of relation labels.

Label numbering is a validity rule rather than a canonical respelling: labels
of carried relations are fixed by ``carry-in``, so a writer cannot renumber a
document on its own. The numbering check runs only when every other relation
rule holds, because its order needs well-formed relations.
"""

from __future__ import annotations

from dataclasses import dataclass, field, replace
from typing import TYPE_CHECKING, Final

from sheet_music_omr.diagnostics import semantic
from sheet_music_omr.model import (
    ArpeggioRelation,
    AssembledScore,
    DashesEnd,
    DashesStart,
    ExtendedTrillRelation,
    HairpinEnd,
    HairpinStart,
    Note,
    Ornament,
    OrnamentKind,
    OttavaEnd,
    OttavaSign,
    OttavaStart,
    PedalAction,
    PedalLine,
    SlurRelation,
    SystemDocument,
    TremoloRelation,
    TremoloRole,
    TrillEnd,
)

if TYPE_CHECKING:
    from collections.abc import Iterable, Iterator
    from fractions import Fraction

    from sheet_music_omr.diagnostics import Diagnostic, Location
    from sheet_music_omr.model import Document, Endpoint, OpenRelation, Pitch
    from sheet_music_omr.timeline import Placed, Timeline

type EndpointKey = tuple[int, Fraction, int, int, int, int]

TIE: Final = "tie"
ARC: Final = "arc"
SLUR: Final = "slur"
TREMOLO: Final = "tremolo"
ORNAMENT: Final = "ornament"
ARPEGGIO: Final = "arpeggio"
OTTAVA: Final = "ottava"
START: Final = "start"
END: Final = "end"
MEMBER: Final = "member"
CHANGE: Final = "change"
EXTENDED_TRILL: Final = "extended-trill"
_MIN_ARPEGGIO: Final = 2
_MAX_SLURS: Final = 2
_LOOSE_ORDER: Final = frozenset({"hairpin", OTTAVA, "pedal", "dashes"})
OTTAVA_SHIFTS: Final = {
    OttavaSign.OTTAVA_ALTA: 1,
    OttavaSign.OTTAVA_BASSA: -1,
    OttavaSign.QUINDICESIMA_ALTA: 2,
    OttavaSign.QUINDICESIMA_BASSA: -2,
}
_PEDAL_ROLES = {
    PedalAction.LINE_START: START,
    PedalAction.LINE_CHANGE: CHANGE,
    PedalAction.LINE_RELEASE: END,
}


def endpoint_key(endpoint: Endpoint) -> EndpointKey:
    """Return the ordering key of a carried endpoint."""
    return (
        endpoint.measure,
        endpoint.offset,
        endpoint.staff,
        endpoint.voice,
        endpoint.event_index,
        endpoint.member_index or 0,
    )


@dataclass(frozen=True, slots=True)
class Point:
    """One relation endpoint written in a document.

    Attributes:
        label: The relation label number.
        kind: The relation kind, such as ``tie`` or ``hairpin``.
        role: ``start``, ``end``, ``change`` or ``member``.
        variant: The written variant, if the endpoint spells one.
        placed: The event that holds the endpoint.
        member: The 1-based chord member of a pitch endpoint.
        pitch: The pitch of a pitch endpoint.
        location: Where the endpoint is written.
    """

    label: int
    kind: str
    role: str
    variant: str | None
    placed: Placed
    member: int | None
    pitch: Pitch | None
    location: Location | None

    @property
    def key(self) -> EndpointKey:
        """Return the ordering key, with an event endpoint before members."""
        placed = self.placed
        return (
            placed.frame.measure,
            placed.onset,
            placed.staff,
            placed.voice,
            placed.index,
            self.member or 0,
        )


@dataclass(slots=True)
class Relation:
    """The endpoints of one label and its ``carry-in`` entry, if any."""

    label: int
    carried: OpenRelation | None
    points: list[Point] = field(default_factory=list)

    @property
    def kind(self) -> str:
        """Return the kind of the relation, as its end decides for an arc."""
        if self.carried is None:
            return self.points[0].kind
        end = self.end
        if self.carried.kind == ARC and end is not None:
            return end.kind
        return self.carried.kind.value

    @property
    def start(self) -> Point | None:
        """Return the written start endpoint, if any."""
        return next((p for p in self.points if p.role == START), None)

    @property
    def end(self) -> Point | None:
        """Return the written end endpoint, if any."""
        return next((p for p in self.points if p.role == END), None)

    @property
    def is_open(self) -> bool:
        """Return whether the relation stays open at the document end."""
        return self.kind != ARPEGGIO and self.end is None

    @property
    def first_key(self) -> EndpointKey:
        """Return the key of the first endpoint."""
        if self.carried is not None:
            return endpoint_key(self.carried.start)
        return min(point.key for point in self.points)

    @property
    def last_key(self) -> EndpointKey:
        """Return the key of the last endpoint written in the document."""
        return max(point.key for point in self.points)

    @property
    def location(self) -> Location | None:
        """Return where the relation is first written."""
        if self.points:
            return self.points[0].location
        return None if self.carried is None else self.carried.location


@dataclass(frozen=True, slots=True)
class OttavaSpan:
    """The range of one ottava line on one staff, end exclusive."""

    staff: int
    start: Fraction
    end: Fraction
    shift: int
    location: Location | None


def _flag_point(placed: Placed, flag: object) -> Point | None:
    spec: tuple[int, str, str, str | None] | None = None
    if isinstance(flag, SlurRelation):
        spec = (flag.label, SLUR, flag.role.value, None)
    elif isinstance(flag, TremoloRelation):
        role = START if flag.role is TremoloRole.TWO_START else END
        spec = (flag.label, TREMOLO, role, str(flag.strokes))
    elif isinstance(flag, ArpeggioRelation):
        spec = (flag.label, ARPEGGIO, MEMBER, None)
    elif isinstance(flag, ExtendedTrillRelation):
        spec = (flag.label, ORNAMENT, flag.role.value, EXTENDED_TRILL)
    if spec is None:
        return None
    label, kind, role, variant = spec
    location = getattr(flag, "location", None)
    return Point(label, kind, role, variant, placed, None, None, location)


def _direction_spec(event: object) -> tuple[int, str, str, str | None] | None:
    spec: tuple[int, str, str, str | None] | None = None
    if isinstance(event, HairpinStart):
        spec = (event.label, "hairpin", START, event.kind.value)
    elif isinstance(event, HairpinEnd):
        spec = (event.label, "hairpin", END, None)
    elif isinstance(event, (OttavaStart, OttavaEnd)):
        role = START if isinstance(event, OttavaStart) else END
        spec = (event.label, OTTAVA, role, event.variant.value)
    elif isinstance(event, PedalLine):
        spec = (event.label, "pedal", _PEDAL_ROLES[event.action], None)
    elif isinstance(event, (DashesStart, DashesEnd)):
        role = START if isinstance(event, DashesStart) else END
        spec = (event.label, "dashes", role, None)
    elif isinstance(event, TrillEnd):
        spec = (event.label, ORNAMENT, END, EXTENDED_TRILL)
    return spec


def _pitch_points(placed: Placed, member: int, pitch: Pitch) -> Iterator[Point]:
    def point(label: int, kind: str, role: str) -> Point:
        return Point(
            label, kind, role, None, placed, member, pitch, pitch.location
        )

    if pitch.tie_end is not None:
        yield point(pitch.tie_end, TIE, END)
    if pitch.tie_start is not None:
        yield point(pitch.tie_start, TIE, START)
    for label in pitch.arc_starts:
        yield point(label, ARC, START)


def collect_points(timeline: Timeline) -> list[Point]:
    """Return every relation endpoint of a document in written order."""
    points: list[Point] = []
    for placed in timeline.events():
        event = placed.event
        if isinstance(event, Note):
            for flag in event.flags:
                point = _flag_point(placed, flag)
                if point is not None:
                    points.append(point)
            for member, pitch in enumerate(event.pitches, 1):
                points.extend(_pitch_points(placed, member, pitch))
            continue
        spec = _direction_spec(event)
        if spec is not None:
            label, kind, role, variant = spec
            location = event.location
            points.append(
                Point(label, kind, role, variant, placed, None, None, location),
            )
    return points


@dataclass(slots=True)
class RelationAnalysis:
    """The relations of a document and the findings about them."""

    document: Document
    timeline: Timeline
    relations: dict[int, Relation]
    findings: list[Diagnostic] = field(default_factory=list)
    spans: list[OttavaSpan] = field(default_factory=list)
    main: dict[tuple[int, int], list[Placed]] = field(default_factory=dict)

    @property
    def assembled(self) -> bool:
        """Return whether the document is an assembled score."""
        return isinstance(self.document, AssembledScore)

    def report(
        self, rule: str, message: str, location: Location | None
    ) -> None:
        """Record a semantic finding."""
        self.findings.append(semantic(rule, message, location))

    def next_main(self, placed: Placed) -> Placed | None:
        """Return the next non-grace rhythmic event of the same ``S.V``."""
        sequence = self.main.get((placed.staff, placed.voice), [])
        for position, other in enumerate(sequence):
            if other.ident == placed.ident:
                following = sequence[position + 1 : position + 2]
                return following[0] if following else None
        return None

    def first_main(self, staff: int, voice: int) -> Placed | None:
        """Return the first non-grace rhythmic event of one ``S.V``."""
        sequence = self.main.get((staff, voice), [])
        return sequence[0] if sequence else None

    def shift_at(self, placed: Placed) -> int:
        """Return the ottava octave shift that applies to an event."""
        for span in self.spans:
            if (
                span.staff == placed.displayed_staff
                and span.start <= placed.time < span.end
            ):
                return span.shift
        return 0


def analyze_relations(
    document: Document, timeline: Timeline
) -> RelationAnalysis:
    """Group the endpoints of a document by label and check them."""
    relations: dict[int, Relation] = {}
    if isinstance(document, SystemDocument):
        for entry in document.carry_in.open:
            relations.setdefault(entry.label, Relation(entry.label, entry))
    for point in collect_points(timeline):
        relation = relations.setdefault(
            point.label, Relation(point.label, None)
        )
        relation.points.append(point)
    analysis = RelationAnalysis(document, timeline, relations)
    for placed in timeline.events():
        if placed.is_main_rhythmic:
            key = (placed.staff, placed.voice)
            analysis.main.setdefault(key, []).append(placed)
    for relation in relations.values():
        if _check_structure(analysis, relation):
            _check_kind(analysis, relation)
        if analysis.assembled and relation.is_open:
            analysis.report(
                "relation_open",
                f"r{relation.label} is open at the end of an assembled score",
                relation.location,
            )
    analysis.spans = _ottava_spans(analysis)
    _check_ottava_overlap(analysis)
    _check_duplicates(analysis)
    if not analysis.findings:
        _check_numbering(analysis)
    return analysis


# Structure shared by every kind.


def _allowed_kinds(relation: Relation) -> set[str]:
    carried = relation.carried
    if carried is None:
        return {relation.points[0].kind}
    if carried.kind.value == ARC:
        return {TIE, SLUR}
    return {carried.kind.value}


def _check_structure(analysis: RelationAnalysis, relation: Relation) -> bool:
    before = len(analysis.findings)
    allowed = _allowed_kinds(relation)
    reference = None if relation.carried is None else relation.carried.variant
    for point in relation.points:
        if point.kind not in allowed:
            analysis.report(
                "relation_kind",
                f"r{relation.label} is a {'/'.join(sorted(allowed))} relation,"
                f" not {point.kind}",
                point.location,
            )
            continue
        if point.variant is None:
            continue
        if reference is None:
            reference = point.variant
        elif point.variant != reference:
            analysis.report(
                "relation_variant",
                f"r{relation.label} has the variant {reference}, not"
                f" {point.variant}",
                point.location,
            )
    _check_roles(analysis, relation)
    return len(analysis.findings) == before


def _check_roles(analysis: RelationAnalysis, relation: Relation) -> None:
    starts = [p for p in relation.points if p.role == START]
    ends = [p for p in relation.points if p.role == END]
    label = f"r{relation.label}"
    if relation.carried is not None and starts:
        analysis.report(
            "relation_reuse",
            f"{label} is carried in and shall not start again",
            starts[0].location,
        )
    for extra in (starts[1:], ends[1:]):
        for point in extra:
            analysis.report(
                "relation_reuse",
                f"{label} shall not be reused within a document",
                point.location,
            )
    if (
        relation.carried is None
        and relation.points[0].kind != ARPEGGIO
        and not starts
    ):
        analysis.report(
            "relation_start",
            f"{label} starts outside the document but carry-in does not list"
            " it as open",
            relation.points[0].location,
        )


# Rules of each kind.


def _check_kind(analysis: RelationAnalysis, relation: Relation) -> None:
    kind = relation.kind
    if kind == ARPEGGIO:
        _check_arpeggio(analysis, relation)
        return
    if relation.carried is None and relation.points[0].kind == ARC:
        if analysis.assembled:
            analysis.report(
                ARC,
                "an assembled score has no arc",
                relation.location,
            )
        return
    start = relation.start
    if start is not None:
        _check_order(analysis, relation, start)
    end = relation.end
    if end is not None:
        for point in relation.points:
            if point.role == CHANGE and end.placed.time < point.placed.time:
                analysis.report(
                    kind,
                    f"the change of r{relation.label} shall not lie after"
                    " its release",
                    point.location,
                )
    checks = {
        TIE: _check_tie,
        TREMOLO: _check_tremolo,
        ORNAMENT: _check_trill,
        OTTAVA: _check_ottava,
    }
    check = checks.get(kind)
    if check is not None:
        check(analysis, relation)


def _check_order(
    analysis: RelationAnalysis, relation: Relation, start: Point
) -> None:
    kind = relation.kind
    loose = kind in _LOOSE_ORDER
    for point in relation.points:
        if point is start:
            continue
        if loose:
            bad = point.placed.time < start.placed.time
        elif isinstance(point.placed.event, TrillEnd):
            bad = point.placed.precedes(start.placed)
        else:
            bad = not start.placed.precedes(point.placed)
        if bad:
            relation_word = "not before" if loose else "after"
            analysis.report(
                kind,
                f"the {point.role} of r{relation.label} shall lie"
                f" {relation_word} its start",
                point.location,
            )


def _tie_follows(start: Placed, end: Placed) -> bool:
    return (
        end.is_main_rhythmic
        and isinstance(end.event, Note)
        and end.staff == start.staff
        and end.time == start.end_time
    )


def _check_tie(analysis: RelationAnalysis, relation: Relation) -> None:
    start, end = relation.start, relation.end
    timeline = analysis.timeline
    label = f"r{relation.label}"
    carried = relation.carried
    if carried is not None:
        if end is None:
            if carried.kind.value == TIE:
                analysis.report(
                    TIE,
                    f"the carried tie {label} shall end at the start of the"
                    " document",
                    carried.location,
                )
            return
        if not (
            end.placed.is_main_rhythmic
            and end.placed.staff == carried.start.staff
            and end.placed.time == timeline.start_time
        ):
            analysis.report(
                TIE,
                f"the end of the carried {label} shall lie in a non-grace"
                " rhythmic event of its staff at the start of the document",
                end.location,
            )
        return
    if start is None:
        return
    if end is None:
        if start.placed.end_time != timeline.end_time:
            analysis.report(
                TIE,
                f"the tie {label} shall end in the next non-grace rhythmic"
                " event of its staff",
                start.location,
            )
    elif not _tie_follows(start.placed, end.placed):
        analysis.report(
            TIE,
            f"the end of {label} shall lie in a non-grace rhythmic event of"
            " the same staff that starts when the start event ends",
            end.location,
        )


def _same_written(first: Placed, second: Placed) -> bool:
    a, b = first.event, second.event
    return (
        isinstance(a, Note) and isinstance(b, Note) and a.duration == b.duration
    )


def _check_tremolo(analysis: RelationAnalysis, relation: Relation) -> None:
    start, end = relation.start, relation.end
    label = f"r{relation.label}"
    carried = relation.carried
    if carried is not None:
        expected = analysis.first_main(carried.start.staff, carried.start.voice)
        if end is None and expected is None:
            return
        event = None if end is None else end.placed.event
        if (
            end is None
            or expected is None
            or end.placed.ident != expected.ident
            or not isinstance(event, Note)
            or event.duration.value != carried.written
        ):
            analysis.report(
                TREMOLO,
                f"the carried tremolo {label} shall end on the first"
                " non-grace rhythmic event of its voice line, with its"
                " written duration",
                carried.location if end is None else end.location,
            )
        return
    if start is None:
        return
    if not start.placed.is_main_rhythmic:
        analysis.report(
            TREMOLO,
            f"the tremolo {label} shall start on a non-grace rhythmic event",
            start.location,
        )
        return
    following = analysis.next_main(start.placed)
    if end is None:
        if following is not None:
            analysis.report(
                TREMOLO,
                f"the tremolo {label} shall end on the next non-grace"
                " rhythmic event of its voice line",
                start.location,
            )
        return
    if (
        following is None
        or following.ident != end.placed.ident
        or not _same_written(start.placed, end.placed)
    ):
        analysis.report(
            TREMOLO,
            f"the endpoints of {label} shall be consecutive non-grace rhythmic"
            " events of one voice line with equal written durations",
            end.location,
        )


def _check_trill(analysis: RelationAnalysis, relation: Relation) -> None:
    start, end = relation.start, relation.end
    label = f"r{relation.label}"
    if start is not None:
        event = start.placed.event
        flags = event.flags if isinstance(event, Note) else ()
        if not any(
            isinstance(f, Ornament) and f.kind is OrnamentKind.TRILL
            for f in flags
        ):
            analysis.report(
                ORNAMENT,
                f"the extended trill {label} shall start on a note with"
                " orn=trill",
                start.location,
            )
    if (
        end is not None
        and isinstance(end.placed.event, TrillEnd)
        and (relation.carried is None or analysis.assembled)
    ):
        analysis.report(
            ORNAMENT,
            f"<trill)> may end only an extended trill that carry-in lists"
            f" as open, not {label}",
            end.location,
        )


def _check_arpeggio(analysis: RelationAnalysis, relation: Relation) -> None:
    members = relation.points
    times = {member.placed.time for member in members}
    events = {member.placed.ident for member in members}
    if len(events) < _MIN_ARPEGGIO or len(times) != 1:
        analysis.report(
            ARPEGGIO,
            f"the arpeggio r{relation.label} shall have at least two member"
            " events at the same onset",
            members[0].location,
        )


def _ottava_sign(point: Point) -> OttavaSign | None:
    event = point.placed.event
    if isinstance(event, (OttavaStart, OttavaEnd)):
        return event.sign
    return None


def _check_ottava(analysis: RelationAnalysis, relation: Relation) -> None:
    label = f"r{relation.label}"
    carried = relation.carried
    reference = None if carried is None else carried.variant
    staff = None if carried is None else carried.start.staff
    start = relation.start
    if start is not None:
        reference, staff = _ottava_sign(start), start.placed.staff
    for point in relation.points:
        sign = _ottava_sign(point)
        if sign is not None and (
            sign.value != point.variant or sign != reference
        ):
            analysis.report(
                OTTAVA,
                f"every direction of {label} shall have the sign and variant"
                f" {reference}",
                point.location,
            )
    end = relation.end
    if end is not None and end.placed.staff != staff:
        analysis.report(
            OTTAVA,
            f"the end of {label} shall be on the staff of its start",
            end.location,
        )


def _ottava_spans(analysis: RelationAnalysis) -> list[OttavaSpan]:
    spans: list[OttavaSpan] = []
    timeline = analysis.timeline
    for relation in sorted(analysis.relations.values(), key=lambda r: r.label):
        if relation.kind != OTTAVA:
            continue
        start, end = relation.start, relation.end
        if start is not None:
            sign, staff, begin = (
                _ottava_sign(start),
                start.placed.staff,
                start.placed.time,
            )
        elif (
            relation.carried is not None
            and relation.carried.kind.value == OTTAVA
        ):
            sign = OttavaSign(relation.carried.variant)
            staff, begin = relation.carried.start.staff, timeline.start_time
        else:
            continue
        stop = timeline.end_time if end is None else end.placed.time
        if sign is not None:
            spans.append(
                OttavaSpan(
                    staff,
                    begin,
                    stop,
                    OTTAVA_SHIFTS[sign],
                    relation.location,
                ),
            )
    return spans


def _check_ottava_overlap(analysis: RelationAnalysis) -> None:
    spans = sorted(analysis.spans, key=lambda s: (s.staff, s.start))
    for index, span in enumerate(spans):
        for other in spans[index + 1 :]:
            if (
                other.staff == span.staff
                and other.start < span.end
                and span.start < other.end
            ):
                analysis.report(
                    OTTAVA,
                    f"ottava lines on staff {span.staff} shall not overlap",
                    other.location,
                )


# Duplicates and numbering.


def _signature(relation: Relation) -> tuple[object, ...]:
    keys = [(point.role, point.key) for point in relation.points]
    if relation.carried is not None:
        keys.append((START, endpoint_key(relation.carried.start)))
    variant = next((p.variant for p in relation.points if p.variant), None)
    if relation.carried is not None:
        variant = relation.carried.variant or variant
    return (relation.kind, tuple(sorted(keys)), variant)


def _check_duplicates(analysis: RelationAnalysis) -> None:
    groups: dict[tuple[object, ...], list[Relation]] = {}
    for relation in sorted(analysis.relations.values(), key=lambda r: r.label):
        groups.setdefault(_signature(relation), []).append(relation)
    for group in groups.values():
        if len(group) < _MIN_ARPEGGIO:
            continue
        if all(relation.is_open for relation in group):
            continue
        if group[0].kind == SLUR and len(group) <= _MAX_SLURS:
            continue
        analysis.report(
            "relation_duplicate",
            f"r{group[1].label} repeats r{group[0].label} in kind, endpoints"
            " and variant",
            group[1].location,
        )


def _new_order(relation: Relation) -> tuple[object, ...]:
    ending: tuple[object, ...] = (
        (1,) if relation.is_open else (0, relation.last_key)
    )
    variant = next((p.variant for p in relation.points if p.variant), "")
    return (relation.first_key, relation.kind, ending, variant)


def _carried_groups(relations: Iterable[Relation]) -> list[list[Relation]]:
    groups: dict[OpenRelation, list[Relation]] = {}
    for relation in relations:
        if relation.carried is not None:
            anonymous = replace(relation.carried, label=0)
            groups.setdefault(anonymous, []).append(relation)
    return [group for group in groups.values() if len(group) > 1]


def _check_numbering(analysis: RelationAnalysis) -> None:
    relations = analysis.relations.values()
    for group in _carried_groups(relations):
        labels = sorted(relation.label for relation in group)
        ended = sorted(
            (r for r in group if not r.is_open),
            key=lambda r: (r.last_key, r.label),
        )
        for relation, label in zip(ended, labels, strict=False):
            if relation.label != label:
                analysis.report(
                    "relation_label",
                    f"carried relations that differ only by id take their"
                    f" labels in the order of their last endpoints; r{label}"
                    f" shall end before r{relation.label}",
                    relation.end.location if relation.end else None,
                )
    used = {r.label for r in relations if r.carried is not None}
    new = sorted((r for r in relations if r.carried is None), key=_new_order)
    for relation in new:
        expected = next(n for n in range(1, len(used) + 2) if n not in used)
        used.add(expected)
        if relation.label != expected:
            analysis.report(
                "relation_label",
                f"r{relation.label} shall be numbered r{expected}",
                relation.location,
            )
