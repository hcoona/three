"""W2 recognizer evaluation and W3 real-data alignment tools."""

from __future__ import annotations

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
