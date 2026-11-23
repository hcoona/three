"""Fail-closed recognizer comparison, DP measure alignment and B-normalization.

Implements ``score_prediction`` from ``docs/evaluation-protocol.md``:
1. Pre-validates ``reference_xml`` outside the candidate fail-closed boundary.
2. Normalizes 2-part single-staff grand-staff candidates into
   ``<staves>2</staves>``.
3. Aligns candidate and reference measures jointly across both staves via
   dynamic programming, respecting any forbidden 1-based reference ordinals
   from failed ``copista-28m`` pages so failed-page measures become ``insbar``
   charged once without double-counting.
4. Scores Layer 0 (`raw_content`, 27 W1 categories), Layer 1 (`b_normalized`,
   26 non-`beam` W1 categories), Layer 2 (`in_vocabulary`, 22 W1 categories),
   `layout` (`C`-class) counts, per-category `ElementTally` for `G2` recall,
   and `musicdiff` 6.0 symbol-level normalized edit distance `d_norm`.
"""

from __future__ import annotations

import copy
from collections import Counter
from dataclasses import dataclass, replace
from typing import TYPE_CHECKING

from musicdiff.annotation import AnnExtra, AnnMeasure
from musicdiff.comparison import Comparison, DiffOperation

from sheet_music_omr.comparison import (
    CONTENT_CATEGORIES,
    LAYOUT,
    LAYOUT_DETAIL,
    AnnotatedScore,
    ContentResult,
    Difference,
    Location,
    NotePair,
    PassDiagnostic,
    PassRecord,
    ProtocolDefectError,
    ScoreParseError,
    UndeterminedReason,
    _apply_pairing,
    _assign,
    _elements,
    _global_score_slices,
    _indexed_bars,
    _locate,
    _musicxml_part_staff_indices,
    _part_slice,
    _signature,
    _single_part_score,
    _voice_content_groups,
    _whole_symbols,
    annotate,
    heading_tally,
    install_coupling_fix,
    record_of,
)
from sheet_music_omr.evaluation.stats import (
    LAYER1_CATEGORIES,
    LAYER2_CATEGORIES,
)
from sheet_music_omr.evaluation.xml_merge import (
    CandidateNormalizationError,
    merge_two_part_grand_staff,
    validate_candidate_score_pair,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.musicxml_events import (
    XmlDirection,
    XmlEvent,
    XmlOttava,
    XmlScore,
    XmlSpan,
)
from sheet_music_omr.musicxml_reverse import MusicXmlError
from sheet_music_omr.supplementary import (
    CHECKS,
    _compared_fingerings,
    _cross_barline_beams,
    _cross_staff_events,
    _cross_staff_key,
    _dashes_lines,
    _hooks,
    _metronome_parentheses,
    _music21_notes,
    _other_dynamics,
    _parentheses,
    _pedal_ends,
    _pedal_touches,
    _ratios,
    _rest_beams,
    _Side,
    _slur_duration,
    _slur_identical,
    _slur_matches,
    _slur_site,
    _slur_touches,
    _slurs,
    _tie_stops,
    _touched,
    _tremolos,
    _voice_lines,
    _wavy_lines,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Mapping
    from fractions import Fraction
    from pathlib import Path

    from musicdiff.annotation import AnnPart, AnnScore

type BarAlignment = tuple[tuple[int | None, int | None], ...]


@dataclass(frozen=True, slots=True)
class MeasureScore:
    """Per-reference-measure discrepancy breakdown (1-based ordinal)."""

    measure_ordinal: int
    aligned_cand_ordinal: int | None
    insbar_anchor_cand_ordinal: int | None
    delbar_cand_ordinals: tuple[int, ...]
    present_categories: frozenset[str]
    layer0_counts: dict[str, int]
    layer1_counts: dict[str, int]
    layer2_counts: dict[str, int]
    layout_c_count: int
    element_totals: dict[str, int]
    element_identical_layer1: dict[str, int]


@dataclass(frozen=True, slots=True)
class UnalignedCandBar:
    """An extra candidate measure (``delbar``) without matched reference."""

    cand_ordinal: int
    attributed_ref_ordinal: int
    layer0_counts: dict[str, int]
    layer1_counts: dict[str, int]
    layer2_counts: dict[str, int]


@dataclass(frozen=True, slots=True)
class PredictionScore:
    """Complete recognizer evaluation output for one piece."""

    parse_valid: bool
    error_reason: str
    ref_measure_count: int
    cand_measure_count: int
    alignment: BarAlignment
    measures: tuple[MeasureScore, ...]
    extra_cand_bars: tuple[UnalignedCandBar, ...]
    element_totals: dict[str, int]
    element_identical_layer1: dict[str, int]
    omr_ed_aligned: int
    omr_ed_whole: int
    symbols_pred: int
    symbols_gt: int
    d_norm_aligned: float
    d_norm_whole: float
    category_edit_distance: dict[str, int]


def score_prediction(
    candidate_xml: Path | str | None,
    reference_xml: Path | str,
    *,
    forbidden_ref_ordinals: frozenset[int] = frozenset(),
) -> PredictionScore:
    """Score ``candidate_xml`` against ``reference_xml`` under W2 rules.

    Raises:
        StopConditionError: If ``reference_xml`` or the comparison instrument
            itself fails validation.
    """
    install_coupling_fix()
    try:
        y_score = annotate(reference_xml)
        y_layout_score = annotate(reference_xml, detail=LAYOUT_DETAIL)
        ref_m_count = _score_measure_count(y_score)
        y_present, y_el_by_m, y_el_totals = _reference_inventory(
            reference_xml, y_score, ref_m_count
        )
        _validate_score_slur_durations(y_score)
        symbols_gt = y_score.annotated.notation_size()
    except (ScoreParseError, ProtocolDefectError, MusicXmlError) as exc:
        msg = f"reference score defect: {exc}"
        raise StopConditionError(msg) from exc

    if candidate_xml is None:
        return _failed_prediction_score(
            reason="candidate XML is None",
            ref_m_count=ref_m_count,
            y_present=y_present,
            y_el_by_m=y_el_by_m,
            y_el_totals=y_el_totals,
            symbols_gt=symbols_gt,
        )

    try:
        norm_cand_xml = merge_two_part_grand_staff(candidate_xml)
        t_raw, t_layout_raw = validate_candidate_score_pair(norm_cand_xml)
        _validate_score_slur_durations(t_raw)
        _ = heading_tally(norm_cand_xml, reference_xml)
    except (
        CandidateNormalizationError,
        ScoreParseError,
        ProtocolDefectError,
        MusicXmlError,
    ) as exc:
        return _failed_prediction_score(
            reason=str(exc),
            ref_m_count=ref_m_count,
            y_present=y_present,
            y_el_by_m=y_el_by_m,
            y_el_totals=y_el_totals,
            symbols_gt=symbols_gt,
        )

    cand_m_count = _score_measure_count(t_raw)
    forbidden_0 = frozenset(
        m - 1 for m in forbidden_ref_ordinals if 1 <= m <= ref_m_count
    )
    alignment = _align_grand_staff_bars(
        t_raw, y_score, cand_m_count, ref_m_count, forbidden_0
    )

    try:
        return _score_aligned(
            norm_cand_xml=norm_cand_xml,
            reference_xml=reference_xml,
            t_raw=t_raw,
            y_score=y_score,
            t_layout_raw=t_layout_raw,
            y_layout_score=y_layout_score,
            cand_m_count=cand_m_count,
            ref_m_count=ref_m_count,
            alignment=alignment,
            y_present=y_present,
            y_el_by_m=y_el_by_m,
            y_el_totals=y_el_totals,
            symbols_gt=symbols_gt,
        )
    except (ScoreParseError, ProtocolDefectError) as exc:
        msg = f"comparison instrument defect: {exc}"
        raise StopConditionError(msg) from exc


def _validate_score_slur_durations(score: AnnotatedScore) -> None:
    side = _Side(score, frozenset(), {})
    notes = _music21_notes(score)
    for slur in _slurs(side):
        _ = _slur_duration(slur, notes, score.score)


def _score_measure_count(score: AnnotatedScore) -> int:
    if score.xml.measure_lengths:
        return len(score.xml.measure_lengths)
    max_idx = -1
    for part in score.annotated.part_list:
        bars = _indexed_bars(part)
        if bars:
            max_idx = max(max_idx, *bars)
    return max_idx + 1


def _reference_inventory(  # noqa: C901
    reference_xml: Path | str,
    y_score: AnnotatedScore,
    ref_m_count: int,
) -> tuple[
    tuple[frozenset[str], ...], tuple[dict[str, int], ...], dict[str, int]
]:
    """Collect per-measure categories and per-category element totals on Y."""
    locs = _locate(y_score)
    per_m: list[set[str]] = [set() for _ in range(ref_m_count)]
    el_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]
    totals: Counter[str] = Counter()

    for elem in _elements(y_score.annotated):
        loc = locs.get(id(elem))
        m_idx = (
            loc.measure_index if loc and loc.measure_index is not None else 0
        )
        m_idx = min(max(m_idx, 0), max(0, ref_m_count - 1))
        for sym in _whole_symbols(elem):
            if sym in CONTENT_CATEGORIES:
                totals[sym] += 1
                if ref_m_count > 0:
                    per_m[m_idx].add(sym)
                    el_by_m[m_idx][sym] += 1

    side = _Side(y_score, frozenset(), {})
    supp_meas = _extract_supplementary_measure_items(
        reference_xml, side, ref_m_count
    )
    for check_name, items in supp_meas.items():
        cat = CHECKS[check_name]
        for m_idx in items:
            totals[cat] += 1
            if 0 <= m_idx < ref_m_count:
                per_m[m_idx].add(cat)
                el_by_m[m_idx][cat] += 1

    yg = _voice_content_groups(y_score)
    for (_, m_idx, _), yv in yg.items():
        v_cnt = yv.known.total()
        if v_cnt > 0:
            totals["voice"] += v_cnt
            if 0 <= m_idx < ref_m_count:
                per_m[m_idx].add("voice")
                el_by_m[m_idx]["voice"] += v_cnt

    return (
        tuple(frozenset(s) for s in per_m),
        tuple(dict(c) for c in el_by_m),
        dict(totals),
    )


def _extract_supplementary_measure_items(
    xml_source: Path | str, side: _Side, m_count: int
) -> dict[str, list[int]]:
    def clamp(m: object) -> int:
        return min(max(int(str(m)), 0), max(0, m_count - 1))

    h_cnt = heading_tally(xml_source, xml_source).elements
    return {
        "fingering": [clamp(k[0]) for k in _compared_fingerings(side)],
        "cross_staff": [
            clamp(ev.measure) for ev in _cross_staff_events(side.score)
        ],
        "heading": [0] * h_cnt,
        "wavy_line": [clamp(k[0][0]) for k in _wavy_lines(side)],  # type: ignore[index]
        "dashes": [clamp(k[0][0]) for k in _dashes_lines(side)],  # type: ignore[index]
        "voice_line": [clamp(k[0][0]) for k in _voice_lines(side)],  # type: ignore[index]
        "notehead_parentheses": [
            clamp(k[0]) for k in _parentheses(side, "notehead")
        ],
        "accidental_parentheses": [
            clamp(k[0]) for k in _parentheses(side, "accidental")
        ],
        "metronome_parentheses": [
            clamp(k[0]) for k in _metronome_parentheses(side)
        ],
        "other_dynamics": [clamp(k[0]) for k, _ in _other_dynamics(side)],
        "pedal_ends": [
            clamp(t[0][0])  # type: ignore[index]
            for t in (k for k, _ in _pedal_ends(side, frozenset()))
        ],
        "tremolo_strokes": [clamp(k[0]) for k, _ in _tremolos(side)],
        "beam_hooks": [clamp(k[0]) for k, _ in _hooks(side)],
        "rest_beams": [clamp(k[0]) for k, _ in _rest_beams(side)],
        "cross_barline_beams": [
            clamp(k[0]) for k, _ in _cross_barline_beams(side)
        ],
        "tuplet_ratio": [clamp(k[0]) for k, _ in _ratios(side, frozenset())],
        "slur_endpoints": [clamp(s.start.measure) for s in _slurs(side)],
        "tie_ends": [clamp(k[0]) for k, _ in _tie_stops(side)],
    }


def _failed_prediction_score(  # noqa: PLR0913
    *,
    reason: str,
    ref_m_count: int,
    y_present: tuple[frozenset[str], ...],
    y_el_by_m: tuple[dict[str, int], ...],
    y_el_totals: dict[str, int],
    symbols_gt: int,
) -> PredictionScore:
    measures: list[MeasureScore] = []
    for m_idx in range(ref_m_count):
        pres = y_present[m_idx]
        charged = pres | {"repeat_ending"}
        el_m = dict(y_el_by_m[m_idx])
        measures.append(
            MeasureScore(
                measure_ordinal=m_idx + 1,
                aligned_cand_ordinal=None,
                insbar_anchor_cand_ordinal=None,
                delbar_cand_ordinals=(),
                present_categories=pres,
                layer0_counts=dict.fromkeys(charged, 1),
                layer1_counts={c: 1 for c in charged if c in LAYER1_CATEGORIES},
                layer2_counts={c: 1 for c in charged if c in LAYER2_CATEGORIES},
                layout_c_count=0,
                element_totals=el_m,
                element_identical_layer1=dict.fromkeys(el_m, 0),
            )
        )
    alignment = tuple((None, j) for j in range(ref_m_count))
    return PredictionScore(
        parse_valid=False,
        error_reason=reason,
        ref_measure_count=ref_m_count,
        cand_measure_count=0,
        alignment=alignment,
        measures=tuple(measures),
        extra_cand_bars=(),
        element_totals=dict(y_el_totals),
        element_identical_layer1=dict.fromkeys(y_el_totals, 0),
        omr_ed_aligned=symbols_gt,
        omr_ed_whole=symbols_gt,
        symbols_pred=0,
        symbols_gt=symbols_gt,
        d_norm_aligned=1.0 if symbols_gt > 0 else 0.0,
        d_norm_whole=1.0 if symbols_gt > 0 else 0.0,
        category_edit_distance={},
    )


def _bar_size(bar: AnnMeasure | None) -> int:
    return 0 if bar is None else int(bar.notation_size())


def _pair_bar_cost(  # noqa: PLR0913, PLR0917
    t_ann: AnnScore,
    t_part: AnnPart,
    t_bar: AnnMeasure | None,
    y_ann: AnnScore,
    y_part: AnnPart,
    y_bar: AnnMeasure | None,
    memo: dict[tuple[int, int], int],
) -> int:
    if t_bar is None and y_bar is None:
        return 0
    if t_bar is None:
        return _bar_size(y_bar)
    if y_bar is None:
        return _bar_size(t_bar)
    if t_bar == y_bar:
        return 0
    key = (id(t_bar), id(y_bar))
    cached = memo.get(key)
    if cached is not None:
        return cached
    t_frag = _single_part_score(t_ann, _part_slice(t_part, [t_bar]))
    y_frag = _single_part_score(y_ann, _part_slice(y_part, [y_bar]))
    _, cost = Comparison.annotated_scores_diff(t_frag, y_frag)
    memo[key] = int(cost)
    return int(cost)


def _align_grand_staff_bars(  # noqa: C901, PLR0912
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
    m_cand: int,
    m_ref: int,
    forbidden_ref_0: frozenset[int],
) -> BarAlignment:
    """Joint 2-staff Needleman-Wunsch DP alignment over measures."""
    t_bars = tuple(_indexed_bars(p) for p in t_score.annotated.part_list)
    y_bars = tuple(_indexed_bars(p) for p in y_score.annotated.part_list)
    t_skeys = {
        k: s for s, k in _musicxml_part_staff_indices(t_score.annotated).items()
    }
    y_skeys = {
        k: s for s, k in _musicxml_part_staff_indices(y_score.annotated).items()
    }
    common_keys = sorted(t_skeys.keys() & y_skeys.keys())

    del_costs = [
        sum(_bar_size(t_bars[t_skeys[k]].get(i)) for k in common_keys)
        for i in range(m_cand)
    ]
    ins_costs = [
        sum(_bar_size(y_bars[y_skeys[k]].get(j)) for k in common_keys)
        for j in range(m_ref)
    ]

    memo: dict[tuple[int, int], int] = {}
    inf = 10**12
    dp = [[0] * (m_ref + 1) for _ in range(m_cand + 1)]
    bt = [
        [0] * (m_ref + 1) for _ in range(m_cand + 1)
    ]  # 1: match, 2: del(t), 3: ins(y)

    for i in range(1, m_cand + 1):
        dp[i][0] = dp[i - 1][0] + del_costs[i - 1]
        bt[i][0] = 2
    for j in range(1, m_ref + 1):
        dp[0][j] = dp[0][j - 1] + ins_costs[j - 1]
        bt[0][j] = 3

    for i in range(1, m_cand + 1):
        ti = i - 1
        for j in range(1, m_ref + 1):
            yj = j - 1
            c_del = dp[i - 1][j] + del_costs[ti]
            c_ins = dp[i][j - 1] + ins_costs[yj]
            if yj in forbidden_ref_0:
                c_sub = inf
            else:
                c_sub = dp[i - 1][j - 1] + sum(
                    _pair_bar_cost(
                        t_score.annotated,
                        t_score.annotated.part_list[t_skeys[k]],
                        t_bars[t_skeys[k]].get(ti),
                        y_score.annotated,
                        y_score.annotated.part_list[y_skeys[k]],
                        y_bars[y_skeys[k]].get(yj),
                        memo,
                    )
                    for k in common_keys
                )
            # Tie-break: prefer diagonal match (1) > insbar (3) > delbar (2)
            if c_sub <= c_ins and c_sub <= c_del:
                dp[i][j] = c_sub
                bt[i][j] = 1
            elif c_ins <= c_del:
                dp[i][j] = c_ins
                bt[i][j] = 3
            else:
                dp[i][j] = c_del
                bt[i][j] = 2

    steps: list[tuple[int | None, int | None]] = []
    i, j = m_cand, m_ref
    while i > 0 or j > 0:
        move = bt[i][j]
        if move == 1:
            steps.append((i - 1, j - 1))
            i -= 1
            j -= 1
        elif move == 2:  # noqa: PLR2004
            steps.append((i - 1, None))
            i -= 1
        else:
            steps.append((None, j - 1))
            j -= 1
    steps.reverse()
    return tuple(steps)


def _remap_t_score_measures(  # noqa: C901
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
    t_to_y: dict[int, int],
) -> AnnotatedScore:
    """Clone ``t_score`` with matched measures and absolute times on ``Y``."""
    y_m_starts = y_score.xml.measure_starts
    y_m_lens = y_score.xml.measure_lengths

    new_events_list: list[XmlEvent] = []
    event_map: dict[int, XmlEvent] = {}
    for ev in t_score.xml.events:
        mapped = t_to_y.get(ev.measure)
        if mapped is None:
            continue
        cloned_ev = copy.copy(ev)
        cloned_ev.measure = mapped
        if mapped < len(y_m_starts):
            cloned_ev.start = y_m_starts[mapped] + cloned_ev.onset
        event_map[id(ev)] = cloned_ev
        new_events_list.append(cloned_ev)

    new_dirs: list[XmlDirection] = []
    dir_map: dict[int, XmlDirection] = {}
    for dr in t_score.xml.directions:
        mapped = t_to_y.get(dr.measure)
        if mapped is None:
            continue
        cloned_dr = copy.copy(dr)
        cloned_dr.measure = mapped
        if mapped < len(y_m_starts):
            cloned_dr.start = y_m_starts[mapped] + cloned_dr.onset
        dir_map[id(dr)] = cloned_dr
        new_dirs.append(cloned_dr)

    new_spans: list[XmlSpan] = []
    for sp in t_score.xml.spans:
        s_start = (
            event_map.get(id(sp.start))
            if isinstance(sp.start, XmlEvent)
            else dir_map.get(id(sp.start))
        )
        s_stop = (
            event_map.get(id(sp.stop))
            if isinstance(sp.stop, XmlEvent)
            else dir_map.get(id(sp.stop))
        )
        if s_start is not None and s_stop is not None:
            new_spans.append(
                XmlSpan(
                    sp.family,
                    s_start,
                    s_stop,
                    sp.start_note,
                    sp.stop_note,
                )
            )

    new_ottavas: list[XmlOttava] = []
    for ott in t_score.xml.ottavas:
        s_dr = dir_map.get(id(ott.direction))
        if s_dr is not None:
            span_dur = ott.end - ott.start
            new_ottavas.append(
                XmlOttava(
                    place=ott.place,
                    start=s_dr.start,
                    end=s_dr.start + span_dur,
                    shift=ott.shift,
                    direction=s_dr,
                    element=ott.element,
                )
            )

    new_note_events: dict[int, XmlEvent] = {}
    for note_id, old_ev in t_score.events.items():
        mapped_ev = event_map.get(id(old_ev))
        if mapped_ev is not None:
            new_note_events[note_id] = mapped_ev

    new_xml = XmlScore(
        events=tuple(new_events_list),
        directions=tuple(new_dirs),
        spans=tuple(new_spans),
        ottavas=tuple(new_ottavas),
        system_starts=y_score.xml.system_starts,
        measure_starts=y_m_starts,
        measure_lengths=y_m_lens,
        tie_in_sources=t_score.xml.tie_in_sources,
    )
    return AnnotatedScore(
        score=t_score.score,
        annotated=t_score.annotated,
        xml=new_xml,
        events=new_note_events,
    )


def _remap_locations(
    t_locs: dict[int, Location], t_to_y: dict[int, int]
) -> dict[int, Location]:
    remapped: dict[int, Location] = {}
    for obj_id, loc in t_locs.items():
        if loc.measure_index is None:
            remapped[obj_id] = loc
        elif loc.measure_index in t_to_y:
            remapped[obj_id] = replace(
                loc, measure_index=t_to_y[loc.measure_index]
            )
        else:
            remapped[obj_id] = replace(
                loc, measure_index=-1000 - loc.measure_index
            )
    return remapped


def _run_aligned_pass(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
    alignment: BarAlignment,
    locations: dict[int, Location],
) -> tuple[
    PassRecord,
    list[DiffOperation],
    list[tuple[int | None, int | None, DiffOperation]],
]:
    t_bars = tuple(_indexed_bars(p) for p in t_score.annotated.part_list)
    y_bars = tuple(_indexed_bars(p) for p in y_score.annotated.part_list)
    t_staff_keys = _musicxml_part_staff_indices(t_score.annotated)
    y_staff_keys = _musicxml_part_staff_indices(y_score.annotated)
    t_staff_idx = {key: staff for staff, key in t_staff_keys.items()}
    y_staff_idx = {key: staff for staff, key in y_staff_keys.items()}
    matched_keys = t_staff_idx.keys() & y_staff_idx.keys()

    measure_ops: list[DiffOperation] = []
    tagged_ops: list[tuple[int | None, int | None, DiffOperation]] = []
    measure_cost = 0

    for part_key in sorted(matched_keys):
        tp = t_staff_idx[part_key]
        yp = y_staff_idx[part_key]
        for t_i, y_j in alignment:
            tb = None if t_i is None else t_bars[tp].get(t_i)
            yb = None if y_j is None else y_bars[yp].get(y_j)
            if tb is None and yb is None:
                continue
            tf = _single_part_score(
                t_score.annotated,
                _part_slice(
                    t_score.annotated.part_list[tp],
                    [] if tb is None else [tb],
                ),
            )
            yf = _single_part_score(
                y_score.annotated,
                _part_slice(
                    y_score.annotated.part_list[yp],
                    [] if yb is None else [yb],
                ),
            )
            ops, cost = Comparison.annotated_scores_diff(tf, yf)
            measure_ops.extend(ops)
            tagged_ops.extend((t_i, y_j, op) for op in ops)
            measure_cost += int(cost)

    t_glob, y_glob = _global_score_slices(
        t_score.annotated,
        y_score.annotated,
        t_staff_keys,
        y_staff_keys,
        matched_keys,
    )
    global_ops, global_cost = Comparison.annotated_scores_diff(t_glob, y_glob)
    tagged_ops.extend((None, 0, op) for op in global_ops)

    whole_ops, whole_cost = Comparison.annotated_scores_diff(
        t_score.annotated, y_score.annotated
    )
    symbols_t = t_score.annotated.notation_size()
    symbols_y = y_score.annotated.notation_size()
    whole_rec = PassDiagnostic(
        tuple(record_of(op, locations) for op in whole_ops),
        int(whole_cost),
        symbols_t,
        symbols_y,
    )
    rec = PassRecord(
        tuple(record_of(op, locations) for op in measure_ops),
        measure_cost,
        tuple(record_of(op, locations) for op in global_ops),
        int(global_cost),
        whole_rec,
    )
    return rec, measure_ops + global_ops, tagged_ops


def _is_symbol_only_timesig_diff(diff: Difference) -> bool:
    if diff.categories != ("time_signature",):
        return False
    if not isinstance(diff.y_object, AnnExtra) or not isinstance(
        diff.t_object, AnnExtra
    ):
        return False
    if diff.y_object.kind != "timesig" or diff.t_object.kind != "timesig":
        return False
    return _signature(diff.t_object) == _signature(diff.y_object)


def _score_aligned(  # noqa: C901, PLR0912, PLR0913, PLR0915
    *,
    norm_cand_xml: str,
    reference_xml: Path | str,
    t_raw: AnnotatedScore,
    y_score: AnnotatedScore,
    t_layout_raw: AnnotatedScore,
    y_layout_score: AnnotatedScore,
    cand_m_count: int,
    ref_m_count: int,
    alignment: BarAlignment,
    y_present: tuple[frozenset[str], ...],
    y_el_by_m: tuple[dict[str, int], ...],
    y_el_totals: dict[str, int],
    symbols_gt: int,
) -> PredictionScore:
    t_to_y = {
        t_i: y_j
        for t_i, y_j in alignment
        if t_i is not None and y_j is not None
    }
    y_to_t = {y_j: t_i for t_i, y_j in t_to_y.items()}

    # Anchor preceding matched candidate bar (1-based) for each insbar, and
    # preceding matched reference bar (1-based) for each delbar.
    insbar_anchor_cand: dict[int, int] = {}
    delbar_attr_ref: dict[int, int] = {}
    delbars_for_ref: dict[int, list[int]] = {j: [] for j in range(ref_m_count)}
    last_t_1 = 1
    last_y_0 = 0
    for t_i, y_j in alignment:
        if t_i is not None and y_j is not None:
            last_t_1 = t_i + 1
            last_y_0 = y_j
        elif t_i is None and y_j is not None:
            insbar_anchor_cand[y_j] = max(
                1, min(last_t_1, max(1, cand_m_count))
            )
        elif t_i is not None and y_j is None:
            target_y = min(max(last_y_0, 0), max(0, ref_m_count - 1))
            delbar_attr_ref[t_i] = target_y + 1
            if ref_m_count > 0:
                delbars_for_ref[target_y].append(t_i + 1)

    t_remapped = _remap_t_score_measures(t_raw, y_score, t_to_y)
    raw_t_locs = _locate(t_raw)
    y_locs = _locate(y_score)
    locations = _remap_locations(raw_t_locs, t_to_y) | y_locs

    t_cross = _cross_staff_events(t_remapped)
    y_cross = _cross_staff_events(y_score)
    owned_t = {id(ev) for ev in t_cross}
    owned_y = {id(ev) for ev in y_cross}

    def cross_staff_owned(pair: NotePair) -> bool:
        return (
            id(y_score.events.get(id(pair.y_note))) in owned_y
            or id(t_remapped.events.get(id(pair.t_note))) in owned_t
        )

    record, _operations, tagged_ops = _run_aligned_pass(
        t_remapped, y_score, alignment, locations
    )
    differences: list[Difference] = []
    pending: dict[tuple[int, int, str], list[object]] = {}
    op_to_step: dict[int, tuple[int | None, int | None]] = {}
    cat_edit_dist: Counter[str] = Counter()

    for t_i, y_j, op in tagged_ops:
        op_rec = record_of(op, locations)
        op_to_step[id(op_rec)] = (t_i, y_j)
        assigned = _assign(op, op_rec, pending)  # type: ignore[arg-type]
        differences.extend(assigned)
        cats = [
            c for d in assigned for c in d.categories if c in CONTENT_CATEGORIES
        ]
        if cats:
            cat_edit_dist[cats[0]] += int(op.edit_distance)

    differences, pairings = _apply_pairing(
        differences, locations, cross_staff_owned
    )
    elements = tuple(_elements(y_score.annotated))
    content = ContentResult(
        record, tuple(differences), elements, t_remapped, y_score, pairings
    )

    # Per-measure Layer 0 and Layer 1 counters on 0..ref_m_count-1
    l0_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]
    l1_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]
    delbar_l0: dict[int, Counter[str]] = {
        t_i: Counter() for t_i in delbar_attr_ref
    }
    delbar_l1: dict[int, Counter[str]] = {
        t_i: Counter() for t_i in delbar_attr_ref
    }

    touched_y_by_m_cat: dict[tuple[int, str], set[int]] = {}

    # 1. Attribute insbar and delbar structural markers explicitly
    for t_i, y_j in alignment:
        if t_i is None and y_j is not None and 0 <= y_j < ref_m_count:
            for c in y_present[y_j] | {"repeat_ending"}:
                l0_by_m[y_j][c] += 1
                if c in LAYER1_CATEGORIES:
                    l1_by_m[y_j][c] += 1
        elif t_i is not None and y_j is None and ref_m_count > 0:
            target_y = delbar_attr_ref[t_i] - 1
            for c in ("repeat_ending", "rhythm_rest"):
                delbar_l0[t_i][c] += 1
                delbar_l1[t_i][c] += 1
                l0_by_m[target_y][c] += 1
                l1_by_m[target_y][c] += 1

    # 2. Attribute musicdiff content differences
    for diff in content.counted:
        is_sym_ts = _is_symbol_only_timesig_diff(diff)
        y_loc = (
            locations.get(id(diff.y_object))
            if diff.y_object is not None
            else None
        )
        raw_t_loc = (
            raw_t_locs.get(id(diff.t_object))
            if diff.t_object is not None
            else None
        )
        # Check if this belongs to an unaligned delbar candidate measure
        if (
            y_loc is None
            and raw_t_loc is not None
            and raw_t_loc.measure_index is not None
            and raw_t_loc.measure_index in delbar_attr_ref
        ):
            t_idx = raw_t_loc.measure_index
            target_y = delbar_attr_ref[t_idx] - 1
            for cat in diff.categories:
                delbar_l0[t_idx][cat] += 1
                l0_by_m[target_y][cat] += 1
                if cat in LAYER1_CATEGORIES and not is_sym_ts:
                    delbar_l1[t_idx][cat] += 1
                    l1_by_m[target_y][cat] += 1
            continue

        m_idx = (
            y_loc.measure_index
            if (y_loc is not None and y_loc.measure_index is not None)
            else (
                t_to_y.get(raw_t_loc.measure_index, 0)
                if (
                    raw_t_loc is not None
                    and raw_t_loc.measure_index is not None
                )
                else 0
            )
        )
        m_idx = min(max(m_idx, 0), max(0, ref_m_count - 1))
        for cat in diff.categories:
            if ref_m_count > 0:
                l0_by_m[m_idx][cat] += 1
            if cat in LAYER1_CATEGORIES and not is_sym_ts:
                if ref_m_count > 0:
                    l1_by_m[m_idx][cat] += 1
                if diff.y_object is not None and ref_m_count > 0:
                    touched_y_by_m_cat.setdefault((m_idx, cat), set()).add(
                        id(diff.y_object)
                    )

    # Track per-measure musicdiff element identical counts for G2 recall
    el_ident_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]
    md_el_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]
    for elem in elements:
        loc = y_locs.get(id(elem))
        em_idx = (
            loc.measure_index if loc and loc.measure_index is not None else 0
        )
        em_idx = min(max(em_idx, 0), max(0, ref_m_count - 1))
        for sym in _whole_symbols(elem):
            if sym in CONTENT_CATEGORIES and ref_m_count > 0:
                md_el_by_m[em_idx][sym] += 1
    for m_idx in range(ref_m_count):
        for cat, tot_m in md_el_by_m[m_idx].items():
            non_id = len(touched_y_by_m_cat.get((m_idx, cat), set()))
            el_ident_by_m[m_idx][cat] = max(0, tot_m - non_id)

    # 3. Per-measure supplementary checks (Layer 0 & Layer 1)
    supp_ident_by_m = _score_aligned_supplementary(
        norm_cand_xml=norm_cand_xml,
        reference_xml=reference_xml,
        content=content,
        t_raw=t_raw,
        t_to_y=t_to_y,
        ref_m_count=ref_m_count,
        l0_by_m=l0_by_m,
        l1_by_m=l1_by_m,
    )
    for m_idx in range(ref_m_count):
        for cat, ident_cnt in supp_ident_by_m[m_idx].items():
            el_ident_by_m[m_idx][cat] += ident_cnt

    # 4. Layout (C-class) pass per measure
    layout_by_m = _score_aligned_layout_counts(
        t_layout_raw, y_layout_score, alignment, t_to_y, ref_m_count
    )

    measures_out: list[MeasureScore] = []
    pooled_ident: Counter[str] = Counter()
    l2_set = frozenset(LAYER2_CATEGORIES)
    for m_idx in range(ref_m_count):
        l0_d = {k: v for k, v in sorted(l0_by_m[m_idx].items()) if v > 0}
        l1_d = {k: v for k, v in sorted(l1_by_m[m_idx].items()) if v > 0}
        l2_d = {k: v for k, v in l1_d.items() if k in l2_set}
        aligned_c = y_to_t.get(m_idx)
        tot_m_d = dict(y_el_by_m[m_idx])
        # Unmatched insbar measures have 0 identical reference elements
        if aligned_c is None:
            ident_m_d = dict.fromkeys(tot_m_d, 0)
        else:
            ident_m_d = {
                c: min(t_c, max(0, el_ident_by_m[m_idx].get(c, 0)))
                for c, t_c in tot_m_d.items()
            }
        for c, v in ident_m_d.items():
            pooled_ident[c] += v
        measures_out.append(
            MeasureScore(
                measure_ordinal=m_idx + 1,
                aligned_cand_ordinal=(aligned_c + 1)
                if aligned_c is not None
                else None,
                insbar_anchor_cand_ordinal=insbar_anchor_cand.get(m_idx),
                delbar_cand_ordinals=tuple(delbars_for_ref.get(m_idx, ())),
                present_categories=y_present[m_idx],
                layer0_counts=l0_d,
                layer1_counts=l1_d,
                layer2_counts=l2_d,
                layout_c_count=layout_by_m[m_idx],
                element_totals=tot_m_d,
                element_identical_layer1=ident_m_d,
            )
        )

    extra_bars_out: list[UnalignedCandBar] = []
    for t_i in sorted(delbar_attr_ref):
        l0_d = {k: v for k, v in sorted(delbar_l0[t_i].items()) if v > 0}
        l1_d = {k: v for k, v in sorted(delbar_l1[t_i].items()) if v > 0}
        l2_d = {k: v for k, v in l1_d.items() if k in l2_set}
        extra_bars_out.append(
            UnalignedCandBar(
                cand_ordinal=t_i + 1,
                attributed_ref_ordinal=delbar_attr_ref[t_i],
                layer0_counts=l0_d,
                layer1_counts=l1_d,
                layer2_counts=l2_d,
            )
        )

    symbols_pred = t_raw.annotated.notation_size()
    omr_ed_aligned = int(record.measure_edit_cost + record.global_edit_cost)
    omr_ed_whole = int(record.whole_score.omr_edit_distance)
    denom = symbols_pred + symbols_gt
    d_aligned = (omr_ed_aligned / denom) if denom > 0 else 0.0
    d_whole = (omr_ed_whole / denom) if denom > 0 else 0.0

    clamped_ident = {
        c: min(tot, max(0, pooled_ident.get(c, 0)))
        for c, tot in y_el_totals.items()
    }

    return PredictionScore(
        parse_valid=True,
        error_reason="",
        ref_measure_count=ref_m_count,
        cand_measure_count=cand_m_count,
        alignment=alignment,
        measures=tuple(measures_out),
        extra_cand_bars=tuple(extra_bars_out),
        element_totals=dict(y_el_totals),
        element_identical_layer1=clamped_ident,
        omr_ed_aligned=omr_ed_aligned,
        omr_ed_whole=omr_ed_whole,
        symbols_pred=symbols_pred,
        symbols_gt=symbols_gt,
        d_norm_aligned=round(d_aligned, 6),
        d_norm_whole=round(d_whole, 6),
        category_edit_distance=dict(cat_edit_dist),
    )


def _score_aligned_layout_counts(
    t_layout_raw: AnnotatedScore,
    y_layout_score: AnnotatedScore,
    alignment: BarAlignment,
    t_to_y: dict[int, int],
    ref_m_count: int,
) -> list[int]:
    if ref_m_count == 0:
        return []
    t_rem = _remap_t_score_measures(t_layout_raw, y_layout_score, t_to_y)
    locs = _remap_locations(_locate(t_layout_raw), t_to_y) | _locate(
        y_layout_score
    )
    _, ops, _ = _run_aligned_pass(t_rem, y_layout_score, alignment, locs)
    diffs: list[Difference] = []
    pending: dict[tuple[int, int, str], list[object]] = {}
    for op in ops:
        diffs.extend(_assign(op, record_of(op, locs), pending))  # type: ignore[arg-type]
    diffs, _ = _apply_pairing(diffs, locs, None)
    counts = [0] * ref_m_count
    for d in diffs:
        if d.symbols != (LAYOUT,):
            continue
        loc = (
            locs.get(id(d.y_object))
            if d.y_object is not None
            else locs.get(id(d.t_object))
        )
        m_idx = (
            loc.measure_index
            if (
                loc is not None
                and loc.measure_index is not None
                and loc.measure_index >= 0
            )
            else 0
        )
        counts[min(max(m_idx, 0), ref_m_count - 1)] += 1
    return counts


def _score_aligned_supplementary(  # noqa: C901, PLR0913, PLR0915
    *,
    norm_cand_xml: str,
    reference_xml: Path | str,
    content: ContentResult,
    t_raw: AnnotatedScore,
    t_to_y: dict[int, int],
    ref_m_count: int,
    l0_by_m: list[Counter[str]],
    l1_by_m: list[Counter[str]],
) -> list[Counter[str]]:
    """Evaluate all 19 supplementary checks per aligned measure."""
    touched_y, touched_t = _touched(content, None)
    y_events = content.y_score.events
    t_events = content.t_score.events
    places = {
        id(t_events[id(pair.t_note)]): y_events[id(pair.y_note)].place
        for pair in content.pairings
        if pair.rule == "cross_staff"
        and id(pair.t_note) in t_events
        and id(pair.y_note) in y_events
    }
    y_side = _Side(content.y_score, touched_y, {})
    t_side = _Side(content.t_score, touched_t, places)
    ident_by_m: list[Counter[str]] = [Counter() for _ in range(ref_m_count)]

    def clamp_m(m: int) -> int:
        return min(max(int(m), 0), max(0, ref_m_count - 1))

    def add_ident(m_idx: int, cat: str, count: int) -> None:
        if count > 0 and ref_m_count > 0:
            ident_by_m[clamp_m(m_idx)][cat] += count

    def add_diff(m_idx: int, cat: str, count: int, *, l1: bool = True) -> None:
        if count <= 0 or ref_m_count == 0:
            return
        cm = clamp_m(m_idx)
        l0_by_m[cm][cat] += count
        if l1 and cat in LAYER1_CATEGORIES:
            l1_by_m[cm][cat] += count

    def multiset_by_m[KT](
        cat: str,
        y_items: Iterable[KT],
        t_items: Iterable[KT],
        meas_of: Callable[[KT], int],
    ) -> None:
        yc = Counter(y_items)
        tc = Counter(t_items)
        matched = yc & tc
        for k, cnt in matched.items():
            add_ident(meas_of(k), cat, cnt)
        for k, cnt in (yc - matched).items():
            add_diff(meas_of(k), cat, cnt)
        for k, cnt in (tc - matched).items():
            add_diff(meas_of(k), cat, cnt)

    def exempt_by_m[KT](
        cat: str,
        y_els: Iterable[tuple[KT, bool]],
        t_els: Iterable[tuple[KT, bool]],
        meas_of: Callable[[KT], int],
    ) -> None:
        ya: Counter[KT] = Counter()
        ye: Counter[KT] = Counter()
        for k, ex in y_els:
            ya[k] += 1
            if ex:
                ye[k] += 1
        ta: Counter[KT] = Counter()
        te: Counter[KT] = Counter()
        for k, ex in t_els:
            ta[k] += 1
            if ex:
                te[k] += 1
        for k in ya.keys() | ta.keys():
            m = min(ya[k], ta[k])
            add_ident(meas_of(k), cat, m)
            yf = min(ye[k], ya[k] - m)
            tf = min(te[k], ta[k] - m)
            non_id = (ya[k] - yf) - m
            ext = ta[k] - m - tf
            add_diff(meas_of(k), cat, non_id + ext)

    def paired_by_m[KT](
        cat: str,
        y_els: Iterable[tuple[KT, object]],
        t_els: Iterable[tuple[KT, object]],
        meas_of: Callable[[KT], int],
    ) -> None:
        yv: dict[KT, Counter[object]] = {}
        for k, v in y_els:
            yv.setdefault(k, Counter())[v] += 1
        tv: dict[KT, Counter[object]] = {}
        for k, v in t_els:
            tv.setdefault(k, Counter())[v] += 1
        for k, vals in yv.items():
            cparts = tv.get(k)
            if cparts is None:
                continue
            matched = min(vals.total(), cparts.total())
            ident = min(matched, (vals & cparts).total())
            add_ident(meas_of(k), cat, ident)
            add_diff(meas_of(k), cat, matched - ident)

    # 1. Fingering: exact match + 2-digit pair <-> substitution equivalence
    y_marks = list(_compared_fingerings(y_side))
    t_marks = list(_compared_fingerings(t_side))
    yc_f0, tc_f0 = Counter(y_marks), Counter(t_marks)
    m_f0 = yc_f0 & tc_f0
    for k, cnt in m_f0.items():
        add_ident(int(str(k[0])), "fingering", cnt)
    rem_y = Counter(yc_f0 - m_f0)
    rem_t = Counter(tc_f0 - m_f0)
    for k, cnt in rem_y.items():
        add_diff(int(str(k[0])), "fingering", cnt, l1=False)
    for k, cnt in rem_t.items():
        add_diff(int(str(k[0])), "fingering", cnt, l1=False)

    _exchange_fingering_sub_pairs(rem_y, rem_t, add_ident)
    if ref_m_count > 0:
        for k, cnt in rem_y.items():
            l1_by_m[clamp_m(int(str(k[0])))]["fingering"] += cnt
        for k, cnt in rem_t.items():
            l1_by_m[clamp_m(int(str(k[0])))]["fingering"] += cnt

    def k0(k: tuple[object, ...]) -> int:
        return int(str(k[0]))

    def k00(k: tuple[object, ...]) -> int:
        return int(str(k[0][0]))  # type: ignore[index]

    # 2. Cross-staff
    multiset_by_m(
        "cross_staff",
        map(_cross_staff_key, _cross_staff_events(y_side.score)),
        map(_cross_staff_key, _cross_staff_events(t_side.score)),
        k0,
    )

    # 3. Voice order (remapping both t_m and pairing-key embedded measure_index)
    yg_raw = _voice_content_groups(y_side.score)
    tg_raw = _voice_content_groups(t_raw)
    tg_mapped: dict[tuple[tuple[int, int], int, tuple[object, ...]], object] = {
        (
            part_idx,
            t_to_y[t_m],
            tuple((t_to_y[t_m], *pk[1:]) for pk in ckey),  # type: ignore[index]
        ): tv
        for (part_idx, t_m, ckey), tv in tg_raw.items()
        if t_m in t_to_y
    }
    for key, yv in yg_raw.items():
        tv = tg_mapped.get(key)
        if tv is None:
            continue
        _, m_idx, _ = key
        t_total = tv.known.total() + tv.undetermined.total()  # type: ignore[attr-defined]
        matched_v = min(yv.known.total(), t_total)
        ident_v0 = sum(
            min(cnt, tv.known[seq])  # type: ignore[attr-defined]
            for seq, cnt in yv.known.items()
        )
        non_id_0 = matched_v - ident_v0
        stemless_t = tv.undetermined[UndeterminedReason.NO_STEMMED_EVENT]  # type: ignore[attr-defined]
        non_id_1 = max(0, non_id_0 - stemless_t)
        add_ident(m_idx, "voice", matched_v - non_id_1)
        add_diff(m_idx, "voice", non_id_0, l1=False)
        if ref_m_count > 0 and non_id_1 > 0:
            l1_by_m[clamp_m(m_idx)]["voice"] += non_id_1

    # 4. Heading (anchored at measure 0 -> ordinal 1)
    ht = heading_tally(norm_cand_xml, reference_xml)
    add_ident(0, "heading", ht.identical)
    add_diff(0, "heading", (ht.elements - ht.identical) + ht.extras)

    # 5. Line & mark checks
    multiset_by_m("ornament", _wavy_lines(y_side), _wavy_lines(t_side), k00)
    multiset_by_m("text", _dashes_lines(y_side), _dashes_lines(t_side), k00)
    multiset_by_m("voice", _voice_lines(y_side), _voice_lines(t_side), k00)
    multiset_by_m(
        "pitch",
        _parentheses(y_side, "notehead"),
        _parentheses(t_side, "notehead"),
        k0,
    )
    multiset_by_m(
        "accidental",
        _parentheses(y_side, "accidental"),
        _parentheses(t_side, "accidental"),
        k0,
    )
    multiset_by_m(
        "tempo_rehearsal",
        _metronome_parentheses(y_side),
        _metronome_parentheses(t_side),
        k0,
    )
    paired_by_m(
        "dynamic",
        _other_dynamics(y_side),
        _other_dynamics(t_side),
        k0,
    )
    paired_by_m(
        "tremolo",
        _tremolos(y_side),
        _tremolos(t_side),
        k0,
    )
    paired_by_m(
        "beam",
        _hooks(y_side),
        _hooks(t_side),
        k0,
    )
    exempt_by_m(
        "beam",
        _rest_beams(y_side),
        _rest_beams(t_side),
        k0,
    )
    exempt_by_m(
        "beam",
        _cross_barline_beams(y_side),
        _cross_barline_beams(t_side),
        k0,
    )
    paired_by_m(
        "pedal",
        _pedal_ends(y_side, _pedal_touches(content)),
        _pedal_ends(t_side, frozenset()),
        k00,
    )
    paired_by_m(
        "tuplet",
        _ratios(y_side, _touched(content, "tuplet")[0]),
        _ratios(t_side, frozenset()),
        k0,
    )
    exempt_by_m(
        "tie",
        _tie_stops(y_side),
        _tie_stops(t_side),
        k0,
    )

    # Slur endpoints per start measure
    slur_ident_by_m, slur_diffs = _slur_by_measure(
        y_side, t_side, _slur_touches(content)
    )
    for m_idx, cnt in slur_ident_by_m.items():
        add_ident(m_idx, "slur", cnt)
    for m_idx, cnt in slur_diffs.items():
        add_diff(m_idx, "slur", cnt)

    return ident_by_m


def _exchange_fingering_sub_pairs(
    rem_y: Counter[tuple[object, ...]],
    rem_t: Counter[tuple[object, ...]],
    add_ident: Callable[[int, str, int], None],
) -> None:
    """Exchange ``(*note, 'sub', d1, d2)`` with complete ``(1, d1)+(2, d2)``."""
    # Direction 1: Y has 'sub', T has (1, d1) + (2, d2)
    for k in list(rem_y):
        if len(k) >= 3 and k[-3] == "sub" and rem_y[k] > 0:  # noqa: PLR2004
            base = k[:-3]
            k1 = (*base, 1, k[-2])
            k2 = (*base, 2, k[-1])
            while rem_y[k] > 0 and rem_t[k1] > 0 and rem_t[k2] > 0:
                rem_y[k] -= 1
                rem_t[k1] -= 1
                rem_t[k2] -= 1
                add_ident(int(str(k[0])), "fingering", 1)
    # Direction 2: T has 'sub', Y has (1, d1) + (2, d2) -> 2 ref elements!
    for k in list(rem_t):
        if len(k) >= 3 and k[-3] == "sub" and rem_t[k] > 0:  # noqa: PLR2004
            base = k[:-3]
            k1 = (*base, 1, k[-2])
            k2 = (*base, 2, k[-1])
            while rem_t[k] > 0 and rem_y[k1] > 0 and rem_y[k2] > 0:
                rem_t[k] -= 1
                rem_y[k1] -= 1
                rem_y[k2] -= 1
                add_ident(int(str(k[0])), "fingering", 2)
    for ctr in (rem_y, rem_t):
        for k in [key for key, v in ctr.items() if v <= 0]:
            del ctr[k]


def _slur_by_measure(
    y_side: _Side,
    t_side: _Side,
    touches: Mapping[
        tuple[tuple[int, int], int, Fraction | None], frozenset[Fraction | None]
    ],
) -> tuple[dict[int, int], dict[int, int]]:
    candidates = _slurs(t_side)
    notes = _music21_notes(y_side.score)
    y_slurs = [
        (slur, _slur_duration(slur, notes, y_side.score.score))
        for slur in _slurs(y_side)
    ]
    durations: dict[object, set[object]] = {}
    for slur, dur in y_slurs:
        durations.setdefault(_slur_site(slur), set()).add(dur)
    scored = [
        slur
        for slur, dur in y_slurs
        if not (
            (
                (touched := touches.get(_slur_site(slur))) is not None
                and (
                    dur is None
                    or dur in touched
                    or touched.isdisjoint(durations[_slur_site(slur)])
                )
            )
            or id(slur.start) in y_side.touched
            or id(slur.stop) in y_side.touched
        )
    ]
    sides = (y_side, t_side)
    used: set[int] = set()
    unpaired: list[object] = []
    ident_by_m: Counter[int] = Counter()
    for slur in scored:
        partner = next(
            (
                idx
                for idx, other in enumerate(candidates)
                if idx not in used
                and _slur_matches(slur, other)
                and _slur_identical(slur, other, sides)
            ),
            None,
        )
        if partner is None:
            unpaired.append(slur)
        else:
            used.add(partner)
            ident_by_m[slur.start.measure] += 1

    diffs_by_m: Counter[int] = Counter()
    for slur_obj in unpaired:
        slur = slur_obj  # _Slur
        partner = next(
            (
                idx
                for idx, other in enumerate(candidates)
                if idx not in used and _slur_matches(slur, other)  # type: ignore[arg-type]
            ),
            None,
        )
        if partner is not None:
            used.add(partner)
            diffs_by_m[slur.start.measure] += 1  # type: ignore[attr-defined]
    return dict(ident_by_m), dict(diffs_by_m)
