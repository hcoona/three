"""Blind Text Score 2 decoding, nested calibration and prediction sidecars."""

from __future__ import annotations

import codecs
import math
import re
from dataclasses import dataclass, replace
from importlib import import_module
from inspect import signature
from itertools import pairwise
from typing import TYPE_CHECKING, Literal, TypeGuard, cast

import numpy as np

from sheet_music_omr.assembly import (
    AssemblyStatus,
    header_state_after,
    validate_system_prefix,
)
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.model import SystemDocument
from sheet_music_omr.parser import parse_document
from sheet_music_omr.semantics import carry_out, validate_document
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.training.models import TextScore2DomainTokenizer
from sheet_music_omr.writer import write_carry_state

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Mapping, Sequence

    from sheet_music_omr.model import Clef
    from sheet_music_omr.training.models import Box

MAX_MEASURE_TOKENS = 192
MAX_SYSTEM_TOKENS = 1536
BEAM_SIZE = 3

type Token = str | int
type TokenStream = Callable[[int, Callable[[Token], bool]], Iterable[Token]]
type AbstentionReason = Literal[
    "parse_invalid",
    "duration_invalid",
    "assembly_prefix_invalid",
    "length_cap",
    "upstream_carry_gap",
]
type CalibrationMethod = Literal["temperature", "platt"]
_ABSTENTION_REASONS = (
    "parse_invalid",
    "duration_invalid",
    "assembly_prefix_invalid",
    "length_cap",
    "upstream_carry_gap",
)


@dataclass(frozen=True, slots=True)
class SystemPromptState:
    """Caller layout and prediction-derived state supplied to a generator."""

    page_system_tuple: tuple[int, int]
    carry_in_text: str
    incoming_header_state_text: str


def _format_clef(clef: Clef) -> str:
    change = {1: "+8", -1: "-8"}.get(clef.octave_change, "")
    return f"{clef.sign}{clef.line}{change}"


def build_system_prompt_state(
    page_system_tuple: tuple[int, int],
    previous_system: SystemDocument | None = None,
) -> SystemPromptState:
    """Format the initial prompt or derive both states from the last system."""
    if min(page_system_tuple) <= 0:
        msg = "page and system coordinates are 1-based"
        raise ValueError(msg)
    if previous_system is None:
        return SystemPromptState(page_system_tuple, "", "")
    key, meter, clefs = header_state_after(previous_system)
    clef_text = " ".join(map(_format_clef, clefs))
    return SystemPromptState(
        page_system_tuple,
        write_carry_state(carry_out(previous_system)),
        f"key: {key}\ntime: {meter.numerator}/{meter.denominator}\n"
        f"clefs: {clef_text}",
    )


@dataclass(frozen=True, slots=True)
class BeamCandidate:
    """A scored beam from token fixtures or an incremental generation loop.

    A stream factory receives the total step budget and a validator. Calling
    the validator before yielding the same token aborts generation immediately
    on overflow; a plain iterator can instead rely on validation on yield.
    Online search marks a truncated overflow prefix with ``length_capped``.
    """

    raw_logprob: float
    tokens: Iterable[Token] | TokenStream
    length_capped: bool = False


@dataclass(frozen=True, slots=True)
class BeamRejection:
    """A rejected candidate and the validation phase that rejected it."""

    raw_logprob: float
    reason: AbstentionReason
    diagnostic_codes: tuple[str, ...] = ()


@dataclass(frozen=True, slots=True)
class SystemDecodeResult:
    """Accepted source text/document or a fail-closed system abstention."""

    valid: bool
    document: SystemDocument | None = None
    text: str | None = None
    raw_logprob: float | None = None
    abstention_reason: AbstentionReason | None = None
    needs_person: bool = False
    carry_out_text: str | None = None
    carry_measure_tokens: int = 0
    rejections: tuple[BeamRejection, ...] = ()


class _LengthCapError(Exception):
    """A candidate exceeded a generated-token budget."""


class _TokenBudget:
    """Recognize measure starts across token boundaries without reparsing."""

    def __init__(self, carry_measure_tokens: int) -> None:
        self.seed = carry_measure_tokens
        self.count = 0
        self.steps = 0
        self.in_measure = False
        self.line_start = True
        self.pending_m: int | None = None
        self.last_charged = 0
        self.exceeded = False

    def _overflow(self) -> None:
        self.exceeded = True
        raise _LengthCapError

    def _charge(self, step: int) -> None:
        if self.last_charged == step:
            return
        self.last_charged = step
        self.count += 1
        if self.count > MAX_MEASURE_TOKENS:
            self._overflow()

    def _char(self, char: str) -> None:
        if self.pending_m is not None:
            mark = self.pending_m
            self.pending_m = None
            if char == " ":
                # The first fragment inherits tokens from the previous system.
                self.count = 0 if self.in_measure else self.seed
                self.in_measure = True
                self.last_charged = 0
                self._charge(mark)
            elif self.in_measure:
                self._charge(mark)
        elif self.line_start and char == "m":
            self.pending_m = self.steps
            self.line_start = False
            return
        if self.in_measure:
            self._charge(self.steps)
        self.line_start = char == "\n"

    def feed(self, piece: str, *, control_token: bool = False) -> None:
        self.steps += 1
        if self.steps > MAX_SYSTEM_TOKENS or self.exceeded:
            self._overflow()
        if control_token:
            return
        if not piece and self.in_measure and self.pending_m is None:
            self._charge(self.steps)
        for char in piece:
            self._char(char)

    def finish(self) -> None:
        if self.exceeded:
            raise _LengthCapError
        if self.in_measure and self.pending_m is not None:
            self._charge(self.pending_m)


class _CandidateReader:
    def __init__(self, carry_measure_tokens: int) -> None:
        self.budget = _TokenBudget(carry_measure_tokens)
        self.decoder = codecs.getincrementaldecoder("utf-8")()
        self.tokenizer = TextScore2DomainTokenizer()
        self.pieces: list[str] = []
        self.checked: list[Token] = []

    def validate(self, token: Token) -> bool:
        control_token = False
        if isinstance(token, int):
            raw = self.tokenizer.token_bytes(token)
            control_token = not raw
            piece = self.decoder.decode(raw)
        else:
            piece = self.decoder.decode(b"", final=True) + token
        self.budget.feed(piece, control_token=control_token)
        self.pieces.append(piece)
        self.checked.append(token)
        return True

    def consume(self, tokens: Iterable[Token] | TokenStream) -> str:
        stream = (
            tokens(MAX_SYSTEM_TOKENS, self.validate)
            if callable(tokens)
            else tokens
        )
        yielded = 0
        for i, token in enumerate(stream):
            if i == len(self.checked):
                self.validate(token)
            elif self.checked[i] != token:
                msg = "stream did not yield its validated token sequence"
                raise ValueError(msg)
            yielded += 1
        if yielded != len(self.checked):
            msg = "stream did not yield its validated token sequence"
            raise ValueError(msg)
        self.budget.finish()
        self.decoder.decode(b"", final=True)
        return "".join(self.pieces)


def _parse_rejection(
    candidate: BeamCandidate,
    codes: tuple[str, ...] = (),
) -> BeamRejection:
    reason = (
        "duration_invalid"
        if any(
            c.rsplit("/", 1)[-1] in {"measure_fill", "measure_range"}
            for c in codes
        )
        else "parse_invalid"
    )
    return BeamRejection(candidate.raw_logprob, reason, codes)


def _evaluate_beam(  # noqa: PLR0911
    candidate: BeamCandidate,
    prior_systems: Sequence[SystemDocument],
    *,
    expected_page_system: tuple[int, int],
    is_final_system: bool,
    carry_measure_tokens: int,
) -> SystemDecodeResult | BeamRejection:
    if candidate.length_capped:
        return BeamRejection(candidate.raw_logprob, "length_cap")
    reader = _CandidateReader(carry_measure_tokens)
    try:
        text = reader.consume(candidate.tokens)
        document = parse_document(text)
    except _LengthCapError:
        return BeamRejection(candidate.raw_logprob, "length_cap")
    except TextScoreError as exc:
        return _parse_rejection(
            candidate, tuple(d.code for d in exc.diagnostics)
        )
    except UnicodeError, ValueError:
        return _parse_rejection(candidate)
    if not isinstance(document, SystemDocument):
        return _parse_rejection(candidate)
    if findings := validate_document(document):
        return _parse_rejection(candidate, tuple(d.code for d in findings))
    prefix = validate_system_prefix(
        prior_systems,
        document,
        expected_page_system=expected_page_system,
        is_final_system=is_final_system,
    )
    if prefix.status not in {
        AssemblyStatus.SUCCESS,
        AssemblyStatus.NEEDS_PERSON,
    }:
        return BeamRejection(
            candidate.raw_logprob,
            "assembly_prefix_invalid",
            tuple(d.diagnostic.code for d in prefix.diagnostics),
        )
    return SystemDecodeResult(
        valid=True,
        document=document,
        text=text,
        raw_logprob=candidate.raw_logprob,
        needs_person=prefix.status == AssemblyStatus.NEEDS_PERSON,
        carry_out_text=write_carry_state(carry_out(document)),
        carry_measure_tokens=(
            0
            if build_timeline(document).frames[-1].owns_end
            else reader.budget.count
        ),
    )


def decode_system_constrained(
    candidates: Iterable[BeamCandidate],
    prior_systems: Sequence[SystemDocument] = (),
    *,
    expected_page_system: tuple[int, int],
    is_final_system: bool = False,
    carry_measure_tokens: int = 0,
) -> SystemDecodeResult:
    """Try three beams in score order and accept the first valid one."""
    if not 0 <= carry_measure_tokens <= MAX_MEASURE_TOKENS:
        msg = "invalid incoming measure token count"
        raise ValueError(msg)
    ranked = list(candidates)
    if any(not math.isfinite(c.raw_logprob) for c in ranked):
        msg = "candidate log probabilities must be finite"
        raise ValueError(msg)
    rejections: list[BeamRejection] = []
    for candidate in sorted(ranked, key=lambda c: -c.raw_logprob)[:BEAM_SIZE]:
        result = _evaluate_beam(
            candidate,
            prior_systems,
            expected_page_system=expected_page_system,
            is_final_system=is_final_system,
            carry_measure_tokens=carry_measure_tokens,
        )
        if isinstance(result, BeamRejection):
            rejections.append(result)
        else:
            return replace(result, rejections=tuple(rejections))
    first = rejections[0] if rejections else None
    return SystemDecodeResult(
        valid=False,
        raw_logprob=first.raw_logprob if first else None,
        abstention_reason=first.reason if first else "parse_invalid",
        rejections=tuple(rejections),
    )


@dataclass(frozen=True, slots=True)
class PieceDecodeResult:
    """Results in caller order, with oracle conditioning excluded from gates."""

    systems: tuple[SystemDecodeResult, ...]
    conditioning: Literal["blind", "oracle_carry_conditioned"]
    gate_eligible: bool


type _PromptGenerator = Callable[[SystemPromptState], Iterable[BeamCandidate]]
type _IndexedGenerator = Callable[
    [int, SystemPromptState], Iterable[BeamCandidate]
]
type _BudgetedGenerator = Callable[
    [int, SystemPromptState, int], Iterable[BeamCandidate]
]


def _budgeted_generator(
    generator: _PromptGenerator | _IndexedGenerator | _BudgetedGenerator,
) -> _BudgetedGenerator:
    try:
        parameters = signature(generator)
    except TypeError, ValueError:
        parameters = None
    if parameters is not None:
        budgeted_arity = 3
        for arity in (budgeted_arity, 2):
            try:
                parameters.bind(*([None] * arity))
            except TypeError:
                continue
            if arity == budgeted_arity:
                return cast("_BudgetedGenerator", generator)
            indexed = cast("_IndexedGenerator", generator)
            return lambda index, prompt, _: indexed(index, prompt)
    prompt_only = cast("_PromptGenerator", generator)
    return lambda _index, prompt, _carry: prompt_only(prompt)


def decode_piece_systems_blind(
    page_systems: Sequence[tuple[int, int]],
    generate_candidates: _PromptGenerator
    | _IndexedGenerator
    | _BudgetedGenerator,
    *,
    expected_systems: int | None = None,
    use_oracle_carry: bool = False,
    oracle_prompts: Sequence[SystemPromptState] | None = None,
) -> PieceDecodeResult:
    """Decode in order, passing the zero-based index and open-measure budget.

    Generators may also accept just the index and prompt, or just the prompt.
    No generator is called after an upstream gap.
    """
    if (
        not page_systems
        or (
            expected_systems is not None
            and len(page_systems) != expected_systems
        )
        or any(min(coords) <= 0 for coords in page_systems)
        or any(a >= b for a, b in pairwise(page_systems))
    ):
        msg = "piece system coordinates must be complete and in reading order"
        raise ValueError(msg)
    if oracle_prompts is not None and (
        not use_oracle_carry
        or tuple(p.page_system_tuple for p in oracle_prompts)
        != tuple(page_systems)
    ):
        msg = (
            "oracle prompts require diagnostic conditioning and matching layout"
        )
        raise ValueError(msg)
    results: list[SystemDecodeResult] = []
    accepted: list[SystemDocument] = []
    carry_tokens = 0
    generate = _budgeted_generator(generate_candidates)
    for i, coords in enumerate(page_systems):
        if results and not results[-1].valid:
            results.append(
                SystemDecodeResult(
                    valid=False, abstention_reason="upstream_carry_gap"
                )
            )
            continue
        prompt = (
            oracle_prompts[i]
            if oracle_prompts is not None
            else build_system_prompt_state(
                coords, accepted[-1] if accepted else None
            )
        )
        result = decode_system_constrained(
            generate(i, prompt, carry_tokens),
            accepted,
            expected_page_system=coords,
            is_final_system=i == len(page_systems) - 1,
            carry_measure_tokens=carry_tokens,
        )
        results.append(result)
        if result.document is not None:
            accepted.append(result.document)
            carry_tokens = result.carry_measure_tokens
    return PieceDecodeResult(
        tuple(results),
        "oracle_carry_conditioned" if use_oracle_carry else "blind",
        not use_oracle_carry,
    )


@dataclass(frozen=True, slots=True)
class TrainDevFold:
    """Preregistered piece-level REAL_VAL with its REAL_TRAIN complement."""

    fold: int
    plate_groups: tuple[str, ...]
    piece_ids: tuple[str, ...]
    pages: int
    systems: int
    measures: int
    real_train_systems: int

    @property
    def pieces(self) -> int:
        """Return the number of held-out pieces."""
        return len(self.piece_ids)


TRAINDEV_FOLDS_4 = (
    TrainDevFold(
        0,
        ("G5",),
        ("gold-07", "gold-08", "gold-09", "gold-11", "gold-12"),
        17,
        79,
        339,
        244,
    ),
    TrainDevFold(
        1,
        ("G6",),
        ("gold-16", "gold-17", "gold-19", "gold-20", "gold-21"),
        21,
        82,
        294,
        241,
    ),
    TrainDevFold(
        2, ("G1", "G2"), ("gold-10", "gold-14", "gold-15"), 17, 87, 487, 236
    ),
    TrainDevFold(
        3, ("G4", "G7"), ("gold-13", "gold-22", "gold-23"), 14, 75, 481, 248
    ),
)
_FOLDS = frozenset(f.fold for f in TRAINDEV_FOLDS_4)
_MIN_ERRORS = 10


@dataclass(frozen=True, slots=True)
class CalibrationPrediction:
    """An uncalibrated logit or ranking score with its held-out error label."""

    score: float
    has_error: bool
    category: str = "overall"
    supervised: bool = True


@dataclass(frozen=True, slots=True)
class InnerValidationPredictions:
    """Predictions from a replay model known to exclude an outer/inner pair."""

    trained_folds: frozenset[int]
    predictions: Sequence[CalibrationPrediction]


def _sigmoid(z: float) -> float:
    exp = math.exp(-abs(z))
    return 1 / (1 + exp) if z >= 0 else exp / (1 + exp)


@dataclass(frozen=True, slots=True)
class Calibrator:
    """Non-decreasing confidence map, or an explicit unsupported category."""

    method: CalibrationMethod
    status: Literal["fit", "insufficient_support"]
    error_instances: int
    fit_instances: int
    slope: float | None = None
    intercept: float = 0.0

    @property
    def temperature(self) -> float | None:
        """Return T for a supported temperature fit."""
        return (
            1 / self.slope
            if self.method == "temperature" and self.slope
            else None
        )

    def predict(self, score: float) -> float | None:
        """Return calibrated correctness confidence, if supported."""
        return (
            None
            if self.slope is None
            else _sigmoid(self.slope * score + self.intercept)
        )


@dataclass(frozen=True, slots=True)
class NestedCalibration:
    """Fold-specific fits and held-out-only correctness metrics."""

    by_fold: Mapping[int, Mapping[str, Calibrator]]
    out_of_fold: Mapping[int, tuple[float | None, ...]]
    metrics_by_category: Mapping[str, Mapping[str, float | None]]
    selection_conditional: bool
    mode: Literal["synthetic_only", "replay"]


def _root(fn: Callable[[float], float], lo: float, hi: float) -> float:
    """Bisect a nondecreasing derivative, including boundary optima."""
    for _ in range(56):
        mid = (lo + hi) / 2
        if fn(mid) < 0:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def fit_score_calibrator(
    rows: Sequence[CalibrationPrediction],
    method: CalibrationMethod = "platt",
) -> Calibrator:
    """Fit a monotone logit-domain calibrator under the support rule."""
    return _fit_score_map(rows, method)


def _fit_score_map(
    rows: Sequence[CalibrationPrediction], method: CalibrationMethod
) -> Calibrator:
    supervised = [p for p in rows if p.supervised]
    n = len(supervised)
    errors = sum(p.has_error for p in supervised)
    if errors < _MIN_ERRORS or errors == n:
        return Calibrator(method, "insufficient_support", errors, n)
    scores = np.asarray([p.score for p in supervised], dtype=float)
    scale = max(1.0, float(np.max(np.abs(scores))))
    x = scores / scale
    y = np.asarray([not p.has_error for p in supervised], dtype=float)

    def residual(a: float, b: float) -> np.ndarray:
        return np.exp(-np.logaddexp(0.0, -(a * x + b))) - y

    if method == "temperature":
        slope = _root(lambda a: float(x @ residual(a, 0)), 1e-6, 1e6)
        return Calibrator(method, "fit", errors, n, slope / scale)

    # Profile out the intercept, then solve the convex, monotone Platt fit.
    reg = 1e-3

    def intercept(a: float) -> float:
        return _root(
            lambda b: float(residual(a, b).sum()) + reg * b, -a - 40, a + 40
        )

    def gradient(a: float) -> float:
        return float(x @ residual(a, intercept(a))) + reg * a

    slope = 0.0 if gradient(0.0) >= 0 else _root(gradient, 0.0, n / reg)
    return Calibrator(method, "fit", errors, n, slope / scale, intercept(slope))


def _nested_fit_rows(
    outer: Mapping[int, Sequence[CalibrationPrediction]],
    inner: Mapping[tuple[int, int], InnerValidationPredictions] | None,
) -> dict[int, list[CalibrationPrediction]]:
    if inner is None:
        if outer.keys() != _FOLDS:
            msg = "synthetic-only calibration requires all four held-out folds"
            raise ValueError(msg)
        return {
            f: [p for g in sorted(_FOLDS - {f}) for p in outer[g]]
            for f in sorted(_FOLDS)
        }
    expected = {(f, g) for f in _FOLDS for g in _FOLDS - {f}}
    if inner.keys() != expected:
        msg = "replay requires exactly all ordered outer/inner held-out pairs"
        raise ValueError(msg)
    fit: dict[int, list[CalibrationPrediction]] = {f: [] for f in _FOLDS}
    for (f, g), batch in sorted(inner.items()):
        if batch.trained_folds != _FOLDS - {f, g}:
            msg = f"replay fold pair {(f, g)} has leaked or incomplete training"
            raise ValueError(msg)
        fit[f].extend(batch.predictions)
    return fit


def fit_nested_calibrators(
    predictions_by_fold: Mapping[int, Sequence[CalibrationPrediction]]
    | None = None,
    *,
    method: CalibrationMethod = "platt",
    replay_predictions: Mapping[tuple[int, int], InnerValidationPredictions]
    | None = None,
) -> NestedCalibration:
    """Fit on proper fold complements and report only held-out predictions."""
    calibration_metrics = import_module(
        "sheet_music_omr.evaluation.stats"
    ).calibration_metrics
    outer = predictions_by_fold if predictions_by_fold is not None else {}
    if method not in {"platt", "temperature"} or (
        outer and outer.keys() != _FOLDS
    ):
        msg = "unknown calibration method or incomplete outer folds"
        raise ValueError(msg)
    fit = _nested_fit_rows(outer, replay_predictions)
    groups = sorted(
        {
            p.category
            for fold_rows in (*fit.values(), *outer.values())
            for p in fold_rows
        }
    )
    if any(
        not math.isfinite(p.score)
        for rows in (*fit.values(), *outer.values())
        for p in rows
        if p.supervised
    ):
        msg = "supervised calibration scores must be finite"
        raise ValueError(msg)
    by_fold: dict[int, dict[str, Calibrator]] = {}
    out_of_fold: dict[int, tuple[float | None, ...]] = {}
    scores: dict[str, list[float]] = {group: [] for group in groups}
    errors: dict[str, list[bool]] = {group: [] for group in groups}
    for f, fitting_rows in fit.items():
        by_fold[f] = calibrators = {
            group: _fit_score_map(
                [p for p in fitting_rows if p.category == group], method
            )
            for group in groups
        }
        fold_scores = []
        for p in outer.get(f, ()):
            prob = (
                calibrators[p.category].predict(p.score)
                if p.supervised
                else None
            )
            fold_scores.append(prob)
            if prob is not None:
                scores[p.category].append(prob)
                errors[p.category].append(p.has_error)
        out_of_fold[f] = tuple(fold_scores)
    return NestedCalibration(
        by_fold,
        out_of_fold,
        {
            group: calibration_metrics(scores[group], errors[group])
            for group in groups
        },
        selection_conditional=True,
        mode="synthetic_only" if replay_predictions is None else "replay",
    )


_REAL_SPLITS = {"TRAIN_DEV", "TEST"}
_GOLD_ID = re.compile(r"gold-(?:0[1-9]|1[0-9]|2[0-3])")
_SIDECAR_SCHEMA = "omr-w7-sidecar-v1"
_BBOX_COORDS = 4
_SIDECAR_KEYS = frozenset(
    {
        "schema",
        "run_id",
        "sample_or_piece_id",
        "page_index",
        "system_index",
        "pred_measure_ordinal",
        "staff",
        "text_score_v2_slice",
        "page_system_bbox_px",
        "page_measure_bbox_px",
        "carry_out_text",
        "raw_logprob",
        "calibrated_confidence",
        "category_confidences",
        "symbol_posteriors",
        "coverage_mask",
        "mandatory_abstention_review",
        "flagged_at_20pct",
        "abstention_reason",
        "aligned_gold_measure_ordinal",
    }
)


@dataclass(frozen=True, slots=True)
class SidecarConfidence:
    """Optional model and calibrator evidence for one measure and staff."""

    calibrated_confidence: float | None = None
    category_confidences: Mapping[str, float | None] | None = None
    symbol_posteriors: Sequence[Mapping[str, object]] | None = None
    coverage_mask: Mapping[str, bool] | None = None


def _positive_int(value: object) -> bool:
    return type(value) is int and value > 0


def _nonnegative_int(value: object) -> bool:
    return type(value) is int and value >= 0


def _number(value: object) -> TypeGuard[float | int]:
    return (
        not isinstance(value, bool)
        and isinstance(value, (float, int))
        and math.isfinite(value)
    )


def _probability(value: object) -> bool:
    return _number(value) and 0 <= value <= 1


def _bbox(value: object) -> bool:
    if not isinstance(value, (tuple, list)) or len(value) != _BBOX_COORDS:
        return False
    x0, y0, x1, y1 = value
    return all(map(_number, value)) and 0 <= x0 < x1 and 0 <= y0 < y1


def _string_map(value: object, check: Callable[[object], bool]) -> bool:
    return isinstance(value, dict) and all(
        isinstance(k, str) and k and check(v) for k, v in value.items()
    )


def _valid_sidecar_payload(row: Mapping[str, object]) -> bool:
    reason = row["abstention_reason"]
    if reason is not None:
        empty = (
            "pred_measure_ordinal",
            "staff",
            "text_score_v2_slice",
            "page_measure_bbox_px",
            "carry_out_text",
            "flagged_at_20pct",
            "aligned_gold_measure_ordinal",
            "symbol_posteriors",
        )
        return (
            reason in _ABSTENTION_REASONS
            and row["mandatory_abstention_review"] is True
            and _number(row["calibrated_confidence"])
            and row["calibrated_confidence"] == 0.0
            and all(row[k] is None for k in empty)
            and row["category_confidences"] == {}
            and row["coverage_mask"] == {}
        )
    ordinal = row["aligned_gold_measure_ordinal"]
    text = row["text_score_v2_slice"]
    confidence = row["calibrated_confidence"]
    flag = row["flagged_at_20pct"]
    measure_box = row["page_measure_bbox_px"]
    symbols = row["symbol_posteriors"]
    return (
        row["mandatory_abstention_review"] is False
        and _positive_int(row["pred_measure_ordinal"])
        and _positive_int(row["staff"])
        and row["staff"] in (1, 2)
        and isinstance(text, str)
        and bool(text)
        and isinstance(row["carry_out_text"], str)
        and (ordinal is None or _positive_int(ordinal))
        and (confidence is None or _probability(confidence))
        and (flag is None or isinstance(flag, bool))
        and (measure_box is None or _bbox(measure_box))
        and (
            symbols is None
            or (
                isinstance(symbols, list)
                and all(isinstance(p, dict) for p in symbols)
            )
        )
        and _string_map(
            row["category_confidences"], lambda p: p is None or _probability(p)
        )
        and _string_map(row["coverage_mask"], lambda v: isinstance(v, bool))
    )


def validate_w7_sidecar_row(row: Mapping[str, object]) -> None:
    """Validate a parsed W7 row or the single mandatory sentinel form."""
    if row.keys() != _SIDECAR_KEYS or row["schema"] != _SIDECAR_SCHEMA:
        msg = "invalid omr-w7-sidecar-v1 row keys or schema tag"
        raise ValueError(msg)
    if (
        not all(
            isinstance(row[k], str) and row[k]
            for k in ("run_id", "sample_or_piece_id")
        )
        or not _nonnegative_int(row["page_index"])
        or not _nonnegative_int(row["system_index"])
        or not _bbox(row["page_system_bbox_px"])
        or not (row["raw_logprob"] is None or _number(row["raw_logprob"]))
        or not _valid_sidecar_payload(row)
    ):
        msg = "invalid omr-w7-sidecar-v1 prediction or abstention payload"
        raise ValueError(msg)


def _sidecar_alignment(  # noqa: PLR0913
    ordinal: int,
    *,
    split: str,
    sample_or_piece_id: str,
    pred_to_draft_ordinal: Mapping[int, int] | None,
    pred_to_gold_ordinal: Mapping[int, int] | None,
    budgeted_draft_flags: Mapping[tuple[str, int], bool] | None,
) -> tuple[bool | None, int | None]:
    if split not in _REAL_SPLITS:
        return None, None
    draft = (pred_to_draft_ordinal or {}).get(ordinal)
    flag = None
    if draft is not None:
        if not _positive_int(draft):
            msg = "aligned draft ordinals must be positive integers"
            raise ValueError(msg)
        if budgeted_draft_flags is not None:
            flag = budgeted_draft_flags.get((sample_or_piece_id, draft), False)
    return flag, (pred_to_gold_ordinal or {}).get(ordinal)


def build_w7_sidecar_rows(  # noqa: PLR0913
    result: SystemDecodeResult,
    *,
    run_id: str,
    sample_or_piece_id: str,
    split: Literal["synth_val", "TRAIN_DEV", "TEST"],
    page_index: int,
    system_index: int,
    page_system_bbox_px: Box,
    page_measure_boxes_px: Mapping[int, Box] | None = None,
    confidences: Mapping[tuple[int, int], SidecarConfidence] | None = None,
    pred_to_draft_ordinal: Mapping[int, int] | None = None,
    pred_to_gold_ordinal: Mapping[int, int] | None = None,
    budgeted_draft_flags: Mapping[tuple[str, int], bool] | None = None,
) -> list[dict[str, object]]:
    """Create one row per parsed measure/staff or one abstention sentinel.

    Prediction ordinals are 1-based within this parsed system. Alignment
    maps supply piece-global draft and gold ordinals where available.
    """
    if split not in {"synth_val", *_REAL_SPLITS} or (
        split in _REAL_SPLITS and _GOLD_ID.fullmatch(sample_or_piece_id) is None
    ):
        msg = "invalid sidecar split or real piece ID"
        raise ValueError(msg)
    base: dict[str, object] = {
        "schema": _SIDECAR_SCHEMA,
        "run_id": run_id,
        "sample_or_piece_id": sample_or_piece_id,
        "page_index": page_index,
        "system_index": system_index,
        "page_system_bbox_px": list(page_system_bbox_px),
        "raw_logprob": result.raw_logprob,
    }
    if result.valid:
        rows = _parsed_sidecar_rows(
            result,
            base,
            page_measure_boxes_px=page_measure_boxes_px or {},
            confidences=confidences or {},
            alignment=lambda ordinal: _sidecar_alignment(
                ordinal,
                split=split,
                sample_or_piece_id=sample_or_piece_id,
                pred_to_draft_ordinal=pred_to_draft_ordinal,
                pred_to_gold_ordinal=pred_to_gold_ordinal,
                budgeted_draft_flags=budgeted_draft_flags,
            ),
        )
    else:
        rows = [
            {
                **base,
                "pred_measure_ordinal": None,
                "staff": None,
                "text_score_v2_slice": None,
                "page_measure_bbox_px": None,
                "carry_out_text": None,
                "calibrated_confidence": 0.0,
                "category_confidences": {},
                "symbol_posteriors": None,
                "coverage_mask": {},
                "mandatory_abstention_review": True,
                "flagged_at_20pct": None,
                "abstention_reason": result.abstention_reason,
                "aligned_gold_measure_ordinal": None,
            }
        ]
    for row in rows:
        validate_w7_sidecar_row(row)
    return rows


def _parsed_sidecar_rows(
    result: SystemDecodeResult,
    base: Mapping[str, object],
    *,
    page_measure_boxes_px: Mapping[int, Box],
    confidences: Mapping[tuple[int, int], SidecarConfidence],
    alignment: Callable[[int], tuple[bool | None, int | None]],
) -> list[dict[str, object]]:
    document = result.document
    if document is None or result.text is None or result.abstention_reason:
        msg = "a parsed sidecar needs accepted text and its parsed document"
        raise ValueError(msg)
    source_lines = result.text.split("\n")
    rows = []
    for ordinal, block in enumerate(document.blocks, 1):
        assert block.location is not None  # noqa: S101
        header = source_lines[block.location.line - 1]
        staff_lines: dict[int, list[str]] = {}
        for voice in block.voices:
            assert voice.location is not None  # noqa: S101
            staff_lines.setdefault(voice.staff, []).append(
                source_lines[voice.location.line - 1]
            )
        flag, gold = alignment(ordinal)
        box = page_measure_boxes_px.get(ordinal)
        for staff, lines in sorted(staff_lines.items()):
            evidence = confidences.get((ordinal, staff), SidecarConfidence())
            rows.append(
                {
                    **base,
                    "pred_measure_ordinal": ordinal,
                    "staff": staff,
                    "text_score_v2_slice": "\n".join((header, *lines, "")),
                    "page_measure_bbox_px": list(box)
                    if box is not None
                    else None,
                    "carry_out_text": result.carry_out_text,
                    "calibrated_confidence": evidence.calibrated_confidence,
                    "category_confidences": dict(
                        evidence.category_confidences or {}
                    ),
                    "symbol_posteriors": (
                        [dict(p) for p in evidence.symbol_posteriors]
                        if evidence.symbol_posteriors is not None
                        else None
                    ),
                    "coverage_mask": dict(evidence.coverage_mask or {}),
                    "mandatory_abstention_review": False,
                    "flagged_at_20pct": flag,
                    "abstention_reason": None,
                    "aligned_gold_measure_ordinal": gold,
                }
            )
    return rows


def emit_aligned_discrepancy_rows(
    rows: Iterable[Mapping[str, object]],
    *,
    split: str,
    sample_or_piece_id: str,
) -> list[dict[str, object]]:
    """Validate W2 scorer rows without changing gold-aligned budget semantics.

    This API accepts complete scorer rows; it does not infer a comparison or
    synthesize gold-aligned discrepancies from prediction-side slices.
    """
    if (
        split not in _REAL_SPLITS
        or _GOLD_ID.fullmatch(sample_or_piece_id) is None
    ):
        msg = "discrepancy rows require an aligned real gold piece"
        raise ValueError(msg)
    from sheet_music_omr.evaluation.cli import (  # noqa: PLC0415
        _validate_discrepancy_row,
    )
    from sheet_music_omr.harness.stops import (  # noqa: PLC0415
        StopConditionError,
    )

    validated = []
    for item in rows:
        row = dict(item)
        if (
            row.get("split") != split
            or row.get("gold_id") != sample_or_piece_id
        ):
            msg = "discrepancy row belongs to another split or piece"
            raise ValueError(msg)
        try:
            _validate_discrepancy_row(row)
        except StopConditionError as exc:
            raise ValueError(str(exc)) from exc
        validated.append(row)
    return validated
