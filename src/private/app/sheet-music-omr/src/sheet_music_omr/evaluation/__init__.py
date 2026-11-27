"""W2 recognizer evaluation and W3 real-data alignment tools."""

from __future__ import annotations

from importlib import import_module
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from sheet_music_omr.evaluation.scorer import (
        PredictionScore,
        score_prediction,
    )
    from sheet_music_omr.evaluation.xml_merge import (
        PageAssemblyResult,
        concat_page_predictions,
        merge_two_part_grand_staff,
    )

__all__ = [
    "PageAssemblyResult",
    "PredictionScore",
    "concat_page_predictions",
    "merge_two_part_grand_staff",
    "score_prediction",
]

_SCORER_EXPORTS = frozenset({"PredictionScore", "score_prediction"})
_XML_MERGE_EXPORTS = frozenset(
    {
        "PageAssemblyResult",
        "concat_page_predictions",
        "merge_two_part_grand_staff",
    }
)


def __getattr__(name: str) -> Any:  # noqa: ANN401
    if name in _SCORER_EXPORTS:
        return getattr(import_module("sheet_music_omr.evaluation.scorer"), name)
    if name in _XML_MERGE_EXPORTS:
        return getattr(
            import_module("sheet_music_omr.evaluation.xml_merge"), name
        )
    msg = f"module {__name__!r} has no attribute {name!r}"
    raise AttributeError(msg)
