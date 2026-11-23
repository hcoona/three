"""Statistical estimators, confidence calibration and second-reader metrics."""

from __future__ import annotations

import math
import random
from dataclasses import dataclass
from typing import TYPE_CHECKING

import numpy as np

from sheet_music_omr.comparison import CONTENT_CATEGORIES

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

BOOTSTRAP_REPLICATES = 10_000
BOOTSTRAP_SEED = 20261123

ASPECT_NAMES: tuple[str, ...] = (
    "pitch",
    "duration",
    "accidental-display",
    "articulations-lines",
    "annotations",
)

W1_TO_ASPECT: dict[str, str] = {
    "pitch": "pitch",
    "chord_membership": "pitch",
    "clef": "pitch",
    "ottava": "pitch",
    "key_signature": "pitch",
    "rhythm_rest": "duration",
    "tuplet": "duration",
    "voice": "duration",
    "cross_staff": "duration",
    "grace": "duration",
    "time_signature": "duration",
    "tremolo": "duration",
    "accidental": "accidental-display",
    "tie": "articulations-lines",
    "slur": "articulations-lines",
    "articulation_fermata": "articulations-lines",
    "arpeggio": "articulations-lines",
    "ornament": "articulations-lines",
    "beam": "articulations-lines",
    "dynamic": "annotations",
    "hairpin": "annotations",
    "text": "annotations",
    "tempo_rehearsal": "annotations",
    "pedal": "annotations",
    "fingering": "annotations",
    "repeat_ending": "annotations",
    "heading": "annotations",
}

T01_G2_GROUPS: dict[str, tuple[str, ...]] = {
    "P1": ("pitch", "chord_membership"),
    "D3": ("voice",),
    "L2": ("slur",),
    "X1": ("accidental",),
    "L3": ("articulation_fermata",),
    "L1": ("tie",),
    "N2": ("hairpin",),
    "N6": ("fingering",),
}

LAYER1_CATEGORIES: tuple[str, ...] = tuple(
    c for c in CONTENT_CATEGORIES if c != "beam"
)
COPISTA_EXCLUDED_CATEGORIES: frozenset[str] = frozenset(
    {"fingering", "text", "tempo_rehearsal", "heading"}
)
LAYER2_CATEGORIES: tuple[str, ...] = tuple(
    c for c in LAYER1_CATEGORIES if c not in COPISTA_EXCLUDED_CATEGORIES
)


@dataclass(frozen=True, slots=True)
class PieceMeasureTable:
    """Measure-level error and masking vectors for one piece."""

    gold_id: str
    book_cohort: str
    measure_count: int
    layer1_errors: tuple[frozenset[str], ...]
    masked_categories: tuple[frozenset[str], ...]
    present_categories: tuple[frozenset[str], ...]
    element_totals: dict[str, int]
    element_identical: dict[str, int]


def overall_rate(
    pieces: Sequence[PieceMeasureTable],
    *,
    active_categories: frozenset[str] | None = None,
) -> tuple[float | None, int, int]:
    """Return ``(e, erred_measures, M_unmasked)``."""
    erred = 0
    denom = 0
    active = (
        frozenset(LAYER1_CATEGORIES)
        if active_categories is None
        else active_categories
    )
    for piece in pieces:
        for errs, masks in zip(
            piece.layer1_errors, piece.masked_categories, strict=True
        ):
            unmasked = active - masks
            if not unmasked:
                continue
            denom += 1
            if errs & unmasked:
                erred += 1
    return (erred / denom if denom > 0 else None), erred, denom


def category_rate(
    pieces: Sequence[PieceMeasureTable], category: str
) -> tuple[float | None, int, int, int]:
    """Return ``(e_c, erred, M_c, present_measures)`` for one W1 category."""
    erred = 0
    denom = 0
    present = 0
    for piece in pieces:
        for errs, masks, pres in zip(
            piece.layer1_errors,
            piece.masked_categories,
            piece.present_categories,
            strict=True,
        ):
            if category in masks:
                continue
            denom += 1
            if category in pres:
                present += 1
            if category in errs:
                erred += 1
    return (erred / denom if denom > 0 else None), erred, denom, present


def aspect_rate(
    pieces: Sequence[PieceMeasureTable], aspect: str
) -> tuple[float | None, int, int]:
    """Return ``(e_a, erred, M_a)`` for one 5-aspect review group."""
    members = frozenset(
        c for c in LAYER1_CATEGORIES if W1_TO_ASPECT[c] == aspect
    )
    erred = 0
    denom = 0
    for piece in pieces:
        for errs, masks in zip(
            piece.layer1_errors, piece.masked_categories, strict=True
        ):
            unmasked = members - masks
            if not unmasked:
                continue
            denom += 1
            if errs & unmasked:
                erred += 1
    return (erred / denom if denom > 0 else None), erred, denom


def cluster_bootstrap_paired(
    cand_pieces: Sequence[PieceMeasureTable],
    base_pieces: Sequence[PieceMeasureTable],
    eval_fn: Callable[[Sequence[PieceMeasureTable]], tuple[float | None, ...]],
    *,
    replicates: int = BOOTSTRAP_REPLICATES,
    seed: int = BOOTSTRAP_SEED,
) -> dict[str, object]:
    """Compute point estimates, paired ratio, bootstrap CI and jackknife."""
    n_pieces = len(cand_pieces)
    e_cand = eval_fn(cand_pieces)[0]
    e_base = eval_fn(base_pieces)[0]
    ratio = (
        e_cand / e_base
        if (e_cand is not None and e_base is not None and e_base > 0)
        else None
    )
    if n_pieces < 2:  # noqa: PLR2004
        return {
            "e_candidate": _r6(e_cand),
            "e_baseline": _r6(e_base),
            "ratio": _r6(ratio),
            "e_candidate_ci95": None,
            "e_baseline_ci95": None,
            "ratio_ci95": None,
            "ratio_jackknife_range": None,
            "status": "unavailable",
        }

    rng = random.Random(seed)  # noqa: S311 - deterministic statistical bootstrap
    boot_ec: list[float] = []
    boot_eb: list[float] = []
    boot_r: list[float] = []
    for _ in range(replicates):
        idxs = [rng.randrange(n_pieces) for _ in range(n_pieces)]
        ec = eval_fn([cand_pieces[i] for i in idxs])[0]
        eb = eval_fn([base_pieces[i] for i in idxs])[0]
        if ec is not None:
            boot_ec.append(ec)
        if eb is not None:
            boot_eb.append(eb)
        if ec is not None and eb is not None and eb > 0:
            boot_r.append(ec / eb)

    ec_ci = _pct_ci(boot_ec) if len(boot_ec) >= replicates // 2 else None
    eb_ci = _pct_ci(boot_eb) if len(boot_eb) >= replicates // 2 else None
    r_ci = (
        _pct_ci(boot_r)
        if (ratio is not None and len(boot_r) >= replicates // 2)
        else None
    )

    jack_r: list[float] = []
    if ratio is not None:
        for drop in range(n_pieces):
            c_sub = [p for i, p in enumerate(cand_pieces) if i != drop]
            b_sub = [p for i, p in enumerate(base_pieces) if i != drop]
            ec = eval_fn(c_sub)[0]
            eb = eval_fn(b_sub)[0]
            if ec is not None and eb is not None and eb > 0:
                jack_r.append(ec / eb)
    jack_range = [_r6(min(jack_r)), _r6(max(jack_r))] if jack_r else None
    return {
        "e_candidate": _r6(e_cand),
        "e_baseline": _r6(e_base),
        "ratio": _r6(ratio),
        "e_candidate_ci95": ec_ci,
        "e_baseline_ci95": eb_ci,
        "ratio_ci95": r_ci,
        "ratio_jackknife_range": jack_range,
        "status": "ok" if ratio is not None else "unavailable_zero_baseline",
    }


def _pct_ci(values: Sequence[float]) -> list[float]:
    arr = np.quantile(np.asarray(values, dtype=float), [0.025, 0.975])
    return [round(float(arr[0]), 6), round(float(arr[1]), 6)]


def one_way_icc(pieces: Sequence[PieceMeasureTable]) -> float | None:
    """One-way random-effects ANOVA ICC over binary measure Layer-1 error."""
    active = frozenset(LAYER1_CATEGORIES)
    groups: list[list[float]] = []
    for p in pieces:
        vals = [
            1.0 if (errs & (active - masks)) else 0.0
            for errs, masks in zip(
                p.layer1_errors, p.masked_categories, strict=True
            )
            if (active - masks)
        ]
        if vals:
            groups.append(vals)
    k = len(groups)
    n_total = sum(len(g) for g in groups)
    if k < 2 or n_total <= k:  # noqa: PLR2004
        return None
    grand_mean = sum(sum(g) for g in groups) / n_total
    ssb = sum(len(g) * ((sum(g) / len(g)) - grand_mean) ** 2 for g in groups)
    ssw = sum(sum((x - (sum(g) / len(g))) ** 2 for x in g) for g in groups)
    if ssb + ssw <= 0.0:
        return None
    msb = ssb / (k - 1)
    msw = ssw / (n_total - k)
    n0 = (n_total - sum(len(g) ** 2 for g in groups) / n_total) / (k - 1)
    denom = msb + (n0 - 1.0) * msw
    if denom <= 0.0:
        return None
    return round(float((msb - msw) / denom), 6)


def g2_recall_table(
    cand_pieces: Sequence[PieceMeasureTable],
    base_pieces: Sequence[PieceMeasureTable],
) -> dict[str, dict[str, object]]:
    """Compute per-category element recall and G2 recall drop."""
    out: dict[str, dict[str, object]] = {}
    for g2_id, cats in T01_G2_GROUPS.items():
        el_b = sum(
            sum(p.element_totals.get(c, 0) for c in cats) for p in base_pieces
        )
        id_b = sum(
            sum(p.element_identical.get(c, 0) for c in cats)
            for p in base_pieces
        )
        el_c = sum(
            sum(p.element_totals.get(c, 0) for c in cats) for p in cand_pieces
        )
        id_c = sum(
            sum(p.element_identical.get(c, 0) for c in cats)
            for p in cand_pieces
        )
        if el_b == 0 or el_c == 0:
            out[g2_id] = {
                "elements": el_b,
                "recall_baseline": None,
                "recall_candidate": None,
                "recall_drop": None,
                "passes_g2": None,
                "status": "unavailable",
            }
            continue
        rec_b = id_b / el_b
        rec_c = id_c / el_c
        drop = rec_b - rec_c
        out[g2_id] = {
            "elements": el_b,
            "recall_baseline": _r6(rec_b),
            "recall_candidate": _r6(rec_c),
            "recall_drop": _r6(drop),
            "passes_g2": drop <= 0.05 + 1e-12,
            "status": "ok",
        }
    return out


def _logit(p: float) -> float:
    clipped = min(max(float(p), 1e-4), 1.0 - 1e-4)
    return math.log(clipped / (1.0 - clipped))


def _sigmoid(z: float) -> float:
    if z >= 0:
        ez = math.exp(-min(z, 500.0))
        return 1.0 / (1.0 + ez)
    ez = math.exp(max(z, -500.0))
    return ez / (1.0 + ez)


def fit_monotone_platt(  # noqa: C901
    raw_confs: Sequence[float],
    is_correct: Sequence[bool],
    *,
    l2_reg: float = 1e-3,
) -> tuple[float, float, float | None]:
    """Fit ``cal = sigmoid(a * logit(clip(raw)) + b)`` with ``a >= 0``."""
    n = len(raw_confs)
    if n == 0:
        return 0.0, 0.0, 0.5
    ys = np.asarray([1.0 if y else 0.0 for y in is_correct], dtype=float)
    pos = float(ys.sum())
    if pos == 0.0 or pos == float(n):
        return 0.0, 0.0, pos / n

    xs = np.asarray([_logit(c) for c in raw_confs], dtype=float)

    def nll(a_val: float, b_val: float) -> float:
        z_v = np.clip(a_val * xs + b_val, -40.0, 40.0)
        loss = float(np.sum(np.logaddexp(0.0, z_v) - ys * z_v))
        return loss + 0.5 * l2_reg * (a_val * a_val + b_val * b_val)

    # Start from the exact intercept-only logistic solution (a=0, b=logit(p))
    a = 0.0
    b = _logit(pos / n)
    cur_obj = nll(a, b)

    for _ in range(80):
        z = np.clip(a * xs + b, -40.0, 40.0)
        p = 1.0 / (1.0 + np.exp(-z))
        diff = p - ys
        ga = float(np.dot(diff, xs)) + l2_reg * a
        gb = float(diff.sum()) + l2_reg * b
        if a == 0.0 and ga >= 0.0 and abs(gb) < 1e-9:  # noqa: PLR2004
            break
        w = p * (1.0 - p)
        h_aa = float(np.dot(w, xs * xs)) + l2_reg
        h_ab = float(np.dot(w, xs))
        h_bb = float(w.sum()) + l2_reg
        if a == 0.0 and ga >= 0.0:
            da = 0.0
            db = gb / h_bb
        else:
            det = h_aa * h_bb - h_ab * h_ab
            if det <= 1e-15:  # noqa: PLR2004
                break
            da = (h_bb * ga - h_ab * gb) / det
            db = (-h_ab * ga + h_aa * gb) / det

        step = 1.0
        improved = False
        for _ in range(25):
            a_cand = max(0.0, a - step * da)
            b_cand = b - step * db
            cand_obj = nll(a_cand, b_cand)
            if cand_obj <= cur_obj - 1e-12:
                a, b, cur_obj = a_cand, b_cand, cand_obj
                improved = True
                break
            step *= 0.5
        if not improved:
            break
    return float(a), float(b), None


def apply_platt(
    raw_conf: float, params: tuple[float, float, float | None]
) -> float:
    """Map raw confidence through fitted monotone Platt parameters."""
    a, b, const = params
    if const is not None:
        return float(const)
    return _sigmoid(a * _logit(raw_conf) + b)


def calibration_metrics(
    confs: Sequence[float], has_error: Sequence[bool], *, n_bins: int = 10
) -> dict[str, float | None]:
    """Compute AUROC (error from ``1 - conf``), 10-bin ECE and AURC."""
    n = len(confs)
    if n == 0:
        return {"auroc": None, "ece": None, "aurc": None}
    errs = np.asarray([1 if e else 0 for e in has_error], dtype=int)
    probs = np.asarray(confs, dtype=float)
    acc = 1.0 - errs.astype(float)

    ece = 0.0
    for b_idx in range(n_bins):
        lo = b_idx / n_bins
        hi = (b_idx + 1) / n_bins
        mask = (
            (probs >= lo) & (probs <= hi)
            if b_idx == n_bins - 1
            else (probs >= lo) & (probs < hi)
        )
        cnt = int(mask.sum())
        if cnt > 0:
            ece += (cnt / n) * abs(float(acc[mask].mean() - probs[mask].mean()))

    order = np.argsort(-probs, kind="mergesort")
    sorted_errs = errs[order].astype(float)
    cum_risks = np.cumsum(sorted_errs) / np.arange(1, n + 1, dtype=float)
    aurc = float(cum_risks.mean())

    n_pos = int(errs.sum())
    n_neg = n - n_pos
    auroc = (
        None
        if (n_pos == 0 or n_neg == 0)
        else _mann_whitney_auroc(1.0 - probs, errs)
    )
    return {
        "auroc": _r6(auroc),
        "ece": _r6(ece),
        "aurc": _r6(aurc),
    }


def _mann_whitney_auroc(scores: np.ndarray, labels: np.ndarray) -> float:
    order = np.argsort(scores, kind="mergesort")
    sorted_s = scores[order]
    sorted_y = labels[order]
    n = len(scores)
    ranks = np.zeros(n, dtype=float)
    i = 0
    while i < n:
        j = i + 1
        while j < n and sorted_s[j] == sorted_s[i]:
            j += 1
        ranks[i:j] = 0.5 * ((i + 1) + j)
        i = j
    n_pos = float(sorted_y.sum())
    n_neg = float(n - n_pos)
    pos_rank_sum = float(ranks[sorted_y == 1].sum())
    return (pos_rank_sum - n_pos * (n_pos + 1.0) / 2.0) / (n_pos * n_neg)


def expected_review_passes(  # noqa: PLR0913
    e: float,
    *,
    n_measures: int = 88,
    d: float = 0.6,
    phi: float = 0.5,
    r_reg: float = 0.10,
    occupancy: bool = False,
) -> float:
    """Compute expected review passes ``E[passes]`` under ``T09#3``."""
    e_clamped = max(0.0, min(float(e), 0.999999))
    lam0 = (
        -n_measures * math.log(1.0 - e_clamped)
        if occupancy
        else n_measures * float(e)
    )
    q = 1.0 - d * (1.0 - r_reg)
    total = 1.0
    surv = 1.0
    for t in range(1, 501):
        mu_t = lam0 * d * (q ** (t - 1))
        p_clean = math.exp(-mu_t - phi)
        surv *= 1.0 - p_clean
        total += surv
        if surv < 1e-12:  # noqa: PLR2004
            break
    return round(total, 4)


def fit_pass_model_from_t01(
    round_a_counts: Mapping[int, int],
    late_non_a_per_piece_round: Sequence[int],
    *,
    r_reg: float = 0.10,
) -> dict[str, float | None]:
    """Estimate ``(q_hat, d_hat, phi_hat)`` from ``T01`` whole-round counts."""
    denom = sum(v for r, v in round_a_counts.items() if r >= 1)
    num_sum = sum(v for r, v in round_a_counts.items() if r >= 2)  # noqa: PLR2004
    if denom <= 0:
        q_hat = None
        d_hat = None
    else:
        q_hat = num_sum / denom
        d_hat = min(1.0, max(0.0, (1.0 - q_hat) / (1.0 - r_reg)))
    phi_hat = (
        sum(late_non_a_per_piece_round) / len(late_non_a_per_piece_round)
        if late_non_a_per_piece_round
        else 0.0
    )
    return {
        "q_hat": _r6(q_hat),
        "d_hat": _r6(d_hat),
        "phi_hat": _r6(phi_hat),
    }


@dataclass(frozen=True, slots=True)
class BlindDraftMeasure:
    """One draft measure ranked in blind second-reader mode."""

    gold_id: str
    draft_ordinal: int
    delta: int
    uncertainty: float

    @property
    def score(self) -> float:
        """Combined disagreement-uncertainty priority score."""
        return float(self.delta) * (0.5 + 0.5 * self.uncertainty)

    @property
    def sort_key(self) -> tuple[int, float, float, str, int]:
        """Deterministic total-order sort key for second-reader ranking."""
        return (
            -(1 if self.delta > 0 else 0),
            -self.score,
            -self.uncertainty,
            self.gold_id,
            self.draft_ordinal,
        )


def rank_second_reader_flags(
    measures: Sequence[BlindDraftMeasure],
) -> dict[str, set[tuple[str, int]]]:
    """Return flagged ``(gold_id, draft_ordinal)`` sets by budget."""
    ordered = sorted(measures, key=lambda m: m.sort_key)
    n = len(ordered)
    k10 = math.floor(0.10 * n)
    k20 = math.floor(0.20 * n)
    return {
        "at_10pct": {(m.gold_id, m.draft_ordinal) for m in ordered[:k10]},
        "at_20pct": {(m.gold_id, m.draft_ordinal) for m in ordered[:k20]},
        "unthresholded": {
            (m.gold_id, m.draft_ordinal) for m in ordered if m.delta > 0
        },
    }


def _r6(val: float | None) -> float | None:
    return None if val is None else round(float(val), 6)
