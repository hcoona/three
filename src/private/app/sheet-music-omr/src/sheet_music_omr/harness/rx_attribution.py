"""Deterministic attribution of known upstream MuseScore 4 re-export defects.

The W1 comparison protocol keeps every failed ``R(X)`` file's reference
elements counted as non-identical in the reported raw ``R(X)`` score. When
``X`` passed the structural scope screen and converted without failure, and
``R(X)`` failed whole-file conversion or ``musicdiff`` parsing solely because
of one of the three minimal-reproduced, upstream-reported MuseScore 4 MusicXML
export defects recorded in the Delivery Wave, the report also lists those
files, their element counts and their upstream issue URLs alongside the
fidelity pooled across the remaining ``R(X)`` files.

Detection uses structural predicates on ``X`` and ``R(X)`` rather than corpus
file identifiers, so the attributed list drops to zero automatically once a
MuseScore 4 release fixes an upstream defect.
"""

from __future__ import annotations

import copy
import xml.etree.ElementTree as ET
from collections import deque
from typing import TYPE_CHECKING, Final

from sheet_music_omr.comparison import (
    CONTENT_DETAIL,
    ScoreParseError,
    annotate,
)
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.harness.selection import inputs_dir
from sheet_music_omr.musicxml_events import ProtocolDefectError
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.musicxml_reverse import MusicXmlError, from_musicxml
from sheet_music_omr.musicxml_reverse import _load as _load_musicxml
from sheet_music_omr.review import review_markers

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement

type Data = dict[str, object]

ISSUE_EMPTY_BEAT_UNIT: Final = (
    "https://github.com/musescore/MuseScore/issues/22974"
)
"""Upstream issue for empty ``<beat-unit/>`` export after non-ASCII words."""

ISSUE_DUPLICATE_DASHES: Final = (
    "https://github.com/musescore/MuseScore/issues/35740"
)
"""Upstream issue for concurrent ``<dashes>`` exported with ``number="1"``."""

ISSUE_DROPPED_KEY_AFTER_REPEAT: Final = (
    "https://github.com/musescore/MuseScore/issues/35741"
)
"""Upstream issue for dropped ``<key>`` after backward ``<repeat/>``."""


def _load_xml(path: Path) -> ET.Element | None:
    if not path.is_file():
        return None
    try:
        return _load_musicxml(path.read_bytes())
    except MusicXmlError, OSError:
        return None


def _has_empty_beat_unit(root: ET.Element) -> bool:
    for metro in root.findall(".//metronome"):
        units = metro.findall("beat-unit")
        if units and any(not (u.text or "").strip() for u in units):
            return True
    return False


def _has_overlapping_dashes_number(root: ET.Element) -> bool:
    open_numbers: set[str] = set()
    for dashes in root.findall(".//direction/direction-type/dashes"):
        dtype = (dashes.get("type") or "").strip()
        num = (dashes.get("number") or "1").strip()
        if dtype == "start":
            if num in open_numbers:
                return True
            open_numbers.add(num)
        elif dtype == "stop":
            open_numbers.discard(num)
    return False


def _find_dropped_keys_after_backward_repeat(
    x_root: ET.Element, rx_root: ET.Element
) -> list[tuple[int, ET.Element]]:
    x_measures = x_root.findall("part/measure")
    rx_measures = rx_root.findall("part/measure")
    if len(x_measures) != len(rx_measures):
        return []
    active_fifths: str | None = None
    dropped: list[tuple[int, ET.Element]] = []
    for idx, x_m in enumerate(x_measures):
        x_key = x_m.find("attributes/key")
        new_fifths = (
            None if x_key is None else (x_key.findtext("fifths") or "").strip()
        )
        if idx > 0 and x_key is not None and new_fifths != active_fifths:
            prev_back_rep = any(
                rep.get("direction") == "backward"
                for rep in x_measures[idx - 1].findall("barline/repeat")
            )
            rx_has_key = rx_measures[idx].find("attributes/key") is not None
            if prev_back_repeat := prev_back_rep and not rx_has_key:
                del prev_back_repeat
                dropped.append((idx, x_key))
        if new_fifths:
            active_fifths = new_fifths
    return dropped


def _counterfactual_passes(repaired_root: ET.Element) -> bool:
    try:
        repaired_xml = ET.tostring(
            repaired_root, encoding="utf-8", xml_declaration=True
        )
        score = from_musicxml(repaired_xml)
        if review_markers(score):
            return False
        t_bytes = to_musicxml(score)
        annotate(repaired_xml.decode("utf-8"), CONTENT_DETAIL)
        annotate(t_bytes.decode("utf-8"), CONTENT_DETAIL)
    except (
        MusicXmlError,
        TextScoreError,
        ReviewRequiredError,
        ScoreParseError,
        ProtocolDefectError,
    ):
        return False
    return True


def _repair_empty_beat_units(rx_root: ET.Element) -> ET.Element:
    repaired = copy.deepcopy(rx_root)
    for metro in repaired.findall(".//metronome"):
        units = metro.findall("beat-unit")
        if not units or not any(not (u.text or "").strip() for u in units):
            continue
        for u in units:
            if not (u.text or "").strip():
                u.text = "quarter"
        if metro.find("per-minute") is None and len(units) == 1:
            ET.SubElement(metro, "per-minute").text = "60"
    return repaired


def _repair_overlapping_dashes(rx_root: ET.Element) -> ET.Element | None:
    repaired = copy.deepcopy(rx_root)
    active_by_orig: dict[str, deque[str]] = {}
    in_use: set[str] = set()
    for dashes in repaired.findall(".//direction/direction-type/dashes"):
        dtype = (dashes.get("type") or "").strip()
        orig = (dashes.get("number") or "1").strip()
        queue = active_by_orig.setdefault(orig, deque())
        if dtype == "start":
            if orig != "1" and (queue or orig in in_use):
                return None
            chosen = (
                orig
                if orig not in in_use
                else next(str(i) for i in range(1, 33) if str(i) not in in_use)
            )
            dashes.set("number", chosen)
            queue.append(chosen)
            in_use.add(chosen)
        elif dtype == "stop":
            if not queue:
                return None
            closed = queue.popleft()
            dashes.set("number", closed)
            in_use.discard(closed)
    if any(queue for queue in active_by_orig.values()):
        return None
    return repaired


def _restore_dropped_keys(
    rx_root: ET.Element, dropped: Sequence[tuple[int, ET.Element]]
) -> ET.Element:
    repaired = copy.deepcopy(rx_root)
    rx_measures = repaired.findall("part/measure")
    for m_idx, x_key in dropped:
        target_m = rx_measures[m_idx]
        attrs = target_m.find("attributes")
        if attrs is None:
            attrs = ET.Element("attributes")
            insert_at = 1 if len(target_m) and target_m[0].tag == "print" else 0
            target_m.insert(insert_at, attrs)
        attrs.insert(0, copy.deepcopy(x_key))
    return repaired


def _reverse_info(rx_result: Mapping[str, object]) -> tuple[set[str], str]:
    steps = rx_result.get("steps")
    if not isinstance(steps, dict):
        return set(), ""
    rev = steps.get("reverse")
    if not isinstance(rev, dict):
        return set(), ""
    codes = rev.get("codes")
    code_set = {str(c) for c in codes} if isinstance(codes, list) else set()
    return code_set, str(rev.get("detail") or "")


def _match_defect_rule(
    x_root: ET.Element,
    rx_root: ET.Element,
    failures: set[str],
    codes: set[str],
    detail: str,
) -> str | None:
    if (
        "rx-unparsable" in failures
        and failures <= {"unsupported", "rx-unparsable"}
        and codes <= {"unsupported/metronome"}
        and _has_empty_beat_unit(rx_root)
        and not _has_empty_beat_unit(x_root)
        and _counterfactual_passes(_repair_empty_beat_units(rx_root))
    ):
        return ISSUE_EMPTY_BEAT_UNIT
    if (
        failures <= {"unsupported", "rx-unparsable"}
        and codes == {"unsupported/spanner_number"}
        and "dashes" in detail
        and _has_overlapping_dashes_number(rx_root)
        and not _has_overlapping_dashes_number(x_root)
        and (repaired_dashes := _repair_overlapping_dashes(rx_root)) is not None
        and _counterfactual_passes(repaired_dashes)
    ):
        return ISSUE_DUPLICATE_DASHES
    dropped_keys = _find_dropped_keys_after_backward_repeat(x_root, rx_root)
    if (
        failures == {"unsupported"}
        and codes == {"unsupported/pitch_alteration"}
        and dropped_keys
        and _counterfactual_passes(_restore_dropped_keys(rx_root, dropped_keys))
    ):
        return ISSUE_DROPPED_KEY_AFTER_REPEAT
    return None


def match_upstream_export_defect(
    x_path: Path,
    rx_path: Path,
    x_result: Mapping[str, object] | None,
    rx_result: Mapping[str, object],
) -> str | None:
    """Return the upstream issue URL when ``R(X)`` failed solely from a bug."""
    if (
        not rx_result.get("failed")
        or x_result is None
        or x_result.get("failed")
    ):
        return None
    x_root = _load_xml(x_path)
    rx_root = _load_xml(rx_path)
    failures_raw = rx_result.get("failures")
    failures = (
        {str(f) for f in failures_raw}
        if isinstance(failures_raw, list)
        else set()
    )
    if x_root is None or rx_root is None or not failures:
        return None
    codes, detail = _reverse_info(rx_result)
    return _match_defect_rule(x_root, rx_root, failures, codes, detail)


def collect_rx_attribution(
    confinement: Confinement,
    corpus: str,
    keys: Mapping[str, str],
    x_results: Sequence[Mapping[str, object]],
    rx_results: Sequence[Mapping[str, object]],
) -> tuple[list[Data], list[Mapping[str, object]]]:
    """Split ``rx_results`` into attributed upstream defects and remaining.

    Args:
        confinement: The run's directories.
        corpus: ``"pdmx"`` or ``"lieder"``.
        keys: Map from public file id (e.g. ``"p0080"``) to corpus key.
        x_results: Results of the corresponding ``*-x`` arm.
        rx_results: Results of the ``*-rx`` arm.

    Returns:
        ``(attributed_entries, remaining_rx_results)``.
    """
    x_by_id = {str(r.get("id")): r for r in x_results}
    base = inputs_dir(confinement, corpus)
    attributed: list[Data] = []
    remaining: list[Mapping[str, object]] = []
    for rx_res in rx_results:
        fid = str(rx_res.get("id"))
        issue = match_upstream_export_defect(
            base / fid / "x.musicxml",
            base / fid / "rx.musicxml",
            x_by_id.get(fid),
            rx_res,
        )
        if issue is None:
            remaining.append(rx_res)
            continue
        content = rx_res.get("content")
        elements = (
            content.get("elements")
            if isinstance(content, dict)
            and isinstance(content.get("elements"), int)
            else 0
        )
        attributed.append(
            {
                "id": fid,
                "key": keys.get(fid, ""),
                "elements": elements,
                "failure": str(rx_res.get("failure")),
                "issue": issue,
            }
        )
    return attributed, remaining
