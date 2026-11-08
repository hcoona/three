"""The harness's conversion and comparison steps, as worker functions.

Each step is a top-level function that reads its input files and returns
plain data with a ``status``. A step catches the failures that the protocol
names and reports them by status; any other exception reaches the caller,
which classifies it by the step. A stop condition or protocol defect is never
caught here.
"""

from __future__ import annotations

from fractions import Fraction
from typing import TYPE_CHECKING

from sheet_music_omr.comparison import (
    CONTENT_DETAIL,
    ElementTally,
    Location,
    OperationRecord,
    PassRecord,
    ScoreParseError,
    annotate,
    compare_layout,
    failure_layout_element_count,
)
from sheet_music_omr.diagnostics import RuleClass, TextScoreError
from sheet_music_omr.harness.systems import assembly_test
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
    from_musicxml,
    from_musicxml_text,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.review import review_markers
from sheet_music_omr.supplementary import (
    compare_file,
    failure_element_count,
    text_space_consistent,
)
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping
    from pathlib import Path

type Data = dict[str, object]

OK = "ok"
UNPARSABLE = "unparsable"
UNSUPPORTED = "unsupported"
REVIEW_MARKER = "review-marker"
REVERSE_FAILED = "reverse-failed"
FORWARD_FAILED = "forward-failed"


def _detail(error: BaseException) -> str:
    return str(error)[:2000]


def tally_data(tally: ElementTally) -> Data:
    """Return an element tally and its fidelity as plain data."""
    return {
        "elements": tally.elements,
        "identical": tally.identical,
        "extras": tally.extras,
        "fidelity": tally.fidelity,
    }


def _onset(value: float | Fraction | None) -> str | float | None:
    return str(value) if isinstance(value, Fraction) else value


def _location(location: Location | None) -> Data | None:
    if location is None:
        return None
    return {
        "part_index": location.part_index,
        "staff": location.staff,
        "measure_index": location.measure_index,
        "measure_number": location.measure_number,
        "voice": location.voice,
        "voice_id": location.voice_id,
        "onset": _onset(location.onset),
        "voice_position": location.voice_position,
        "grace_position": location.grace_position,
    }


def _operation(operation: OperationRecord) -> Data:
    return {
        "name": operation.name,
        "kind_t": operation.kind1,
        "kind_y": operation.kind2,
        "cost": operation.cost,
        "location_t": _location(operation.location1),
        "location_y": _location(operation.location2),
    }


def record_data(record: PassRecord) -> tuple[Data, Data]:
    """Split a pass record into its figures and its raw operations.

    Returns:
        The edit costs and whole-score diagnostic figures, and the raw
        operations of the measure pairs, the global-only comparison and the
        whole-score diagnostic.
    """
    whole = record.whole_score
    figures: Data = {
        "measure_edit_cost": record.measure_edit_cost,
        "global_edit_cost": record.global_edit_cost,
        "whole_score_omr_edit_distance": whole.omr_edit_distance,
        "whole_score_symbols_t": whole.symbols_t,
        "whole_score_symbols_y": whole.symbols_y,
        "whole_score_normalized_distance": whole.normalized_distance,
    }
    operations: Data = {
        "measure": [_operation(o) for o in record.measure_operations],
        "global": [_operation(o) for o in record.global_operations],
        "whole_score": [_operation(o) for o in whole.operations],
    }
    return figures, operations


def count(y: Path) -> Data:
    """Count a failed file's content and layout elements on one score.

    Returns:
        ``ok`` with ``content`` and ``layout`` counts, or ``unparsable``
        when musicdiff cannot parse the score.
    """
    try:
        content = failure_element_count(y)
        layout = failure_layout_element_count(y)
    except ScoreParseError as error:
        return {"status": UNPARSABLE, "detail": _detail(error)}
    return {"status": OK, "content": content, "layout": layout}


def reverse(y: Path) -> Data:
    """Convert a score to canonical text.

    Returns:
        ``ok`` with ``text``; ``review-marker`` with ``text`` and
        ``markers``; ``unsupported`` or ``reverse-failed`` with ``codes``.
    """
    try:
        score = from_musicxml(y.read_bytes())
    except MusicXmlError as error:
        diagnostics = error.diagnostics
        unsupported = any(
            d.rule_class == RuleClass.UNSUPPORTED for d in diagnostics
        )
        return {
            "status": UNSUPPORTED if unsupported else REVERSE_FAILED,
            "codes": [d.code for d in diagnostics],
            "detail": _detail(error),
        }
    text = write_document(score)
    markers = review_markers(score)
    if markers:
        return {"status": REVIEW_MARKER, "text": text, "markers": len(markers)}
    return {"status": OK, "text": text}


def forward(text: str) -> Data:
    """Convert canonical text to MusicXML.

    Returns:
        ``ok`` with ``musicxml`` bytes, or ``forward-failed`` with ``codes``.
    """
    try:
        document = parse_document(text)
        if not isinstance(document, AssembledScore):
            return {
                "status": FORWARD_FAILED,
                "codes": [],
                "detail": "the text is not an assembled score",
            }
        data = to_musicxml(document)
    except TextScoreError as error:
        return {
            "status": FORWARD_FAILED,
            "codes": [d.code for d in error.diagnostics],
            "detail": _detail(error),
        }
    except ReviewRequiredError as error:
        return {
            "status": FORWARD_FAILED,
            "codes": ["review-required"],
            "detail": _detail(error),
        }
    return {"status": OK, "musicxml": data}


def content(t: Path, y: Path) -> Data:
    """Run the content pass and the supplementary checks of one file.

    Returns:
        ``ok`` with the pooled ``tally``, the musicdiff part's tally, each
        check's tally and differences, the excluded reference voices by
        reason, the category counts, the figures and the raw
        ``operations``; or ``unparsable`` when musicdiff cannot parse one of
        the scores.
    """
    try:
        comparison = compare_file(t, y)
    except ScoreParseError as error:
        return {"status": UNPARSABLE, "detail": _detail(error)}
    figures, operations = record_data(comparison.content.record)
    return {
        "status": OK,
        "tally": tally_data(comparison.tally),
        "musicdiff_tally": tally_data(comparison.content.tally),
        "checks": {
            name: tally_data(tally) for name, tally in comparison.checks.items()
        },
        "check_differences": {
            name: list(pair)
            for name, pair in comparison.check_differences().items()
        },
        "excluded_voices": {
            str(reason): count
            for reason, count in sorted(comparison.excluded_voices.items())
        },
        "category_counts": dict(comparison.category_counts()),
        "counted_differences": len(comparison.content.counted),
        "figures": figures,
        "operations": operations,
    }


def layout(t: Path, y: Path) -> Data:
    """Run the layout pass of one file.

    Returns:
        ``ok`` with the layout ``tally``, figures and raw ``operations``, or
        ``unparsable``.
    """
    try:
        result = compare_layout(t, y)
    except ScoreParseError as error:
        return {"status": UNPARSABLE, "detail": _detail(error)}
    figures, operations = record_data(result.record)
    return {
        "status": OK,
        "tally": tally_data(result.tally),
        "figures": figures,
        "operations": operations,
    }


def text_space(t: Path, y: Path) -> Data:
    """Check that ``Y`` and ``T(Y)`` reverse to the same canonical text.

    A ``T(Y)`` that the reverse converter rejects is a mismatch.

    Returns:
        ``ok`` with ``consistent`` and the canonical text of ``T(Y)``, or
        ``None`` for it when the reverse converter rejects ``T(Y)``.
    """
    texts: dict[str, str] = {}

    def remember(source: bytes | str) -> str:
        text = from_musicxml_text(source)
        texts["t" if "y" in texts else "y"] = text
        return text

    try:
        consistent = text_space_consistent(t, y, reverse=remember)
    except MusicXmlError as error:
        return {
            "status": OK,
            "consistent": False,
            "t_text": None,
            "detail": _detail(error),
        }
    return {"status": OK, "consistent": consistent, "t_text": texts.get("t")}


def assembly(x: Path) -> Data:
    """Run the system-assembly test on one gold piece."""
    result = assembly_test(x.read_bytes())
    return {
        "status": str(result.status),
        "passed": result.passed,
        "systems": result.systems,
        "texts_differ": result.texts_differ,
        "differences": result.differences,
        "category_counts": dict(result.category_counts),
        "excluded_voices": result.excluded_voices,
        "detail": result.detail[:2000],
    }


def baseline(rx: Path, x: Path) -> Data:
    """Compare ``R(X)`` with ``X`` as the MuseScore-only baseline.

    Returns:
        The content and layout results with ``R(X)`` as the round trip.
    """
    return {"status": OK, "content": content(rx, x), "layout": layout(rx, x)}


def parse_check(x: Path) -> Data:
    """Run the screen's musicdiff parse of a candidate in the content pass.

    Returns:
        ``ok``, or ``unparsable`` when musicdiff cannot parse it.
    """
    try:
        annotate(x, CONTENT_DETAIL)
    except ScoreParseError as error:
        return {"status": UNPARSABLE, "detail": _detail(error)}
    return {"status": OK}


STEPS: Mapping[str, Callable[..., Data]] = {
    "count": count,
    "reverse": reverse,
    "forward": forward,
    "content": content,
    "layout": layout,
    "text_space": text_space,
    "assembly": assembly,
    "baseline": baseline,
    "parse_check": parse_check,
}
"""The default step table, by step name."""
