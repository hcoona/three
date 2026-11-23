"""Normalize 2-part single-staff MusicXML and assemble multi-page predictions.

Follows ``docs/evaluation-protocol.md``:
- Two-part single-staff grand-staff outputs (as ``copisteria`` emits when a
  page's left brace is missed) merge into ``<part id="P1"><staves>2</staves>``
  preserving divisions, durations, offsets, clefs, keys, voices and spanners
  without number collisions, failing closed when measure counts, meters or
  barline structures conflict.
- Per-page MusicXML predictions concatenate into whole-piece ``P_piece`` with
  ``<print new-page="yes"/>`` on valid pages ``p >= 2``, unified ``lcm``
  divisions, disjoint per-page spanner numbers, and explicit ``failed_pages``
  tracking when individual pages fail validation.
"""

from __future__ import annotations

import copy
import math
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.comparison import (
    LAYOUT_DETAIL,
    AnnotatedScore,
    ProtocolDefectError,
    ScoreParseError,
    _voice_content_groups,
    annotate,
    install_coupling_fix,
)
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
)
from sheet_music_omr.musicxml_reverse import (
    _load as _load_musicxml,
)
from sheet_music_omr.supplementary import (
    _compared_fingerings,
    _cross_barline_beams,
    _cross_staff_events,
    _dashes_lines,
    _hooks,
    _metronome_parentheses,
    _other_dynamics,
    _parentheses,
    _pedal_ends,
    _ratios,
    _rest_beams,
    _Side,
    _slurs,
    _tie_stops,
    _tremolos,
    _voice_lines,
    _wavy_lines,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

_SPANNER_TAGS: tuple[tuple[str, str], ...] = (
    (".//notations/slur", "slur"),
    (".//notations/ornaments/wavy-line", "wavy-line"),
    (".//direction-type/wedge", "wedge"),
    (".//direction-type/dashes", "dashes"),
    (".//direction-type/pedal", "pedal"),
    (".//direction-type/octave-shift", "octave-shift"),
    (".//direction-type/bracket", "bracket"),
)


class CandidateNormalizationError(ValueError):
    """A candidate MusicXML violates grand-staff normalization invariants."""


@dataclass(frozen=True, slots=True)
class PageAssemblyResult:
    """Whole-piece MusicXML assembled from per-page candidate outputs."""

    xml_text: str | None
    parse_valid: bool
    failed_pages: tuple[int, ...]
    pred_measure_pages: tuple[int, ...]
    pred_measure_raw_conf: tuple[float, ...]
    error_reasons: dict[int, str]


def _staff_count_of_part(part: ET.Element) -> int:
    max_staves = 1
    for staves_el in part.findall("./measure/attributes/staves"):
        if staves_el.text and staves_el.text.strip().isdigit():
            max_staves = max(max_staves, int(staves_el.text.strip()))
    for staff_el in part.findall(".//staff"):
        if staff_el.text and staff_el.text.strip().isdigit():
            max_staves = max(max_staves, int(staff_el.text.strip()))
    return max_staves


def _scale_int_text(el: ET.Element | None, factor: int) -> None:
    if el is not None and el.text and el.text.strip().lstrip("-").isdigit():
        el.text = str(int(el.text.strip()) * factor)


def _scale_measure_divisions(
    measure: ET.Element, cur_div: int, target_div: int
) -> None:
    if target_div % cur_div != 0:
        msg = f"target divisions {target_div} not divisible by {cur_div}"
        raise CandidateNormalizationError(msg)
    factor = target_div // cur_div
    for div_el in measure.findall("./attributes/divisions"):
        div_el.text = str(target_div)
    if factor == 1:
        return
    for dur_el in measure.findall(".//duration"):
        _scale_int_text(dur_el, factor)
    for off_el in measure.findall(".//offset"):
        _scale_int_text(off_el, factor)


def _measure_final_cursor(measure: ET.Element) -> int:
    pos = 0
    for child in measure:
        if child.tag == "note":
            if (
                child.find("chord") is not None
                or child.find("grace") is not None
            ):
                continue
            dur_txt = child.findtext("duration")
            if not (dur_txt and dur_txt.strip().isdigit()):
                msg = f"invalid note duration {dur_txt!r}"
                raise CandidateNormalizationError(msg)
            pos += int(dur_txt.strip())
        elif child.tag == "forward":
            dur_txt = child.findtext("duration")
            if not (dur_txt and dur_txt.strip().isdigit()):
                msg = f"invalid forward duration {dur_txt!r}"
                raise CandidateNormalizationError(msg)
            pos += int(dur_txt.strip())
        elif child.tag == "backup":
            dur_txt = child.findtext("duration")
            if not (dur_txt and dur_txt.strip().isdigit()):
                msg = f"invalid backup duration {dur_txt!r}"
                raise CandidateNormalizationError(msg)
            pos -= int(dur_txt.strip())
            if pos < 0:
                msg = "backup moves cursor before measure start"
                raise CandidateNormalizationError(msg)
    return pos


def _renumber_spanners(part_measures: Sequence[ET.Element], offset: int) -> int:
    """Shift spanner ``number`` attributes by ``offset``; return next offset."""
    max_seen = offset
    for measure in part_measures:
        for xpath, _ in _SPANNER_TAGS:
            for el in measure.findall(xpath):
                raw_num = el.get("number")
                base = int(raw_num) if raw_num and raw_num.isdigit() else 1
                shifted = base + offset
                el.set("number", str(shifted))
                max_seen = max(max_seen, shifted)
    return max_seen


def _time_signature_tuple(
    measure: ET.Element,
) -> tuple[str, str, str] | None:
    time_el = measure.find("./attributes/time")
    if time_el is None:
        return None
    return (
        (time_el.findtext("beats") or "").strip(),
        (time_el.findtext("beat-type") or "").strip(),
        (time_el.get("symbol") or "").strip(),
    )


def _barline_signature(
    measure: ET.Element,
) -> dict[str, tuple[str, str, tuple[tuple[str, str], ...]]]:
    sigs: dict[str, tuple[str, str, tuple[tuple[str, str], ...]]] = {}
    for bl in measure.findall("./barline"):
        loc = bl.get("location", "right")
        style = (bl.findtext("bar-style") or "").strip()
        rep = bl.find("repeat")
        rep_dir = rep.get("direction", "") if rep is not None else ""
        endings = tuple(
            (end.get("number", ""), end.get("type", ""))
            for end in bl.findall("ending")
        )
        sigs[loc] = (style, rep_dir, endings)
    return sigs


def merge_two_part_grand_staff(  # noqa: C901, PLR0912, PLR0915
    source: Path | str | bytes,
) -> str:
    """Normalize a 1-part/2-staff or 2-part/1-staff piano MusicXML to 1 part."""
    try:
        raw: bytes | str = (
            source.read_bytes() if isinstance(source, Path) else source
        )
        root = copy.deepcopy(_load_musicxml(raw))
    except (MusicXmlError, ET.ParseError, OSError) as exc:
        raise CandidateNormalizationError(str(exc)) from exc

    parts = root.findall("part")
    if len(parts) == 1:
        staves = _staff_count_of_part(parts[0])
        if staves != 2:  # noqa: PLR2004
            msg = f"single-part score has {staves} staves, expected 2"
            raise CandidateNormalizationError(msg)
        return _serialize_xml(root)

    if len(parts) != 2:  # noqa: PLR2004
        msg = f"score has {len(parts)} parts, expected 1 or 2"
        raise CandidateNormalizationError(msg)

    s1 = _staff_count_of_part(parts[0])
    s2 = _staff_count_of_part(parts[1])
    if s1 != 1 or s2 != 1:
        msg = f"two-part score has ({s1}, {s2}) staves, expected (1, 1)"
        raise CandidateNormalizationError(msg)

    m1_list = parts[0].findall("measure")
    m2_list = parts[1].findall("measure")
    if not m1_list or len(m1_list) != len(m2_list):
        msg = f"part measure counts differ ({len(m1_list)} vs {len(m2_list)})"
        raise CandidateNormalizationError(msg)

    max_p1_span = _renumber_spanners(m1_list, 0)
    _renumber_spanners(m2_list, max_p1_span + 10)

    max_v1 = 4
    for v_el in parts[0].findall(".//voice"):
        if v_el.text and v_el.text.strip().isdigit():
            max_v1 = max(max_v1, int(v_el.text.strip()))

    div1 = 1
    div2 = 1
    eff_time1: tuple[str, str] | None = None
    eff_time2: tuple[str, str] | None = None
    merged_part = ET.Element("part", {"id": "P1"})

    for idx, (m1, m2) in enumerate(zip(m1_list, m2_list, strict=True)):
        o1_raw = _combine_attributes_blocks(_opening_attributes_blocks(m1))
        o2_raw = _combine_attributes_blocks(_opening_attributes_blocks(m2))
        d1_txt = o1_raw.findtext("divisions") if o1_raw is not None else None
        d2_txt = o2_raw.findtext("divisions") if o2_raw is not None else None
        if d1_txt and d1_txt.strip().isdigit():
            div1 = int(d1_txt.strip())
        if d2_txt and d2_txt.strip().isdigit():
            div2 = int(d2_txt.strip())
        if div1 <= 0 or div2 <= 0:
            msg = f"non-positive divisions ({div1}, {div2})"
            raise CandidateNormalizationError(msg)
        target_div = math.lcm(div1, div2)

        t1 = _time_signature_tuple(m1)
        t2 = _time_signature_tuple(m2)
        if t1 is not None:
            eff_time1 = t1[:2]
        if t2 is not None:
            eff_time2 = t2[:2]
        if (
            eff_time1 is not None
            and eff_time2 is not None
            and eff_time1 != eff_time2
        ):
            msg = (
                f"conflicting effective time signatures in measure {idx + 1}: "
                f"{eff_time1} vs {eff_time2}"
            )
            raise CandidateNormalizationError(msg)

        b1 = _barline_signature(m1)
        b2 = _barline_signature(m2)
        for loc in b1.keys() & b2.keys():
            if b1[loc] != b2[loc]:
                msg = f"conflicting {loc} barlines in measure {idx + 1}"
                raise CandidateNormalizationError(msg)

        cm1 = copy.deepcopy(m1)
        cm2 = copy.deepcopy(m2)
        _scale_measure_divisions(cm1, div1, target_div)
        _scale_measure_divisions(cm2, div2, target_div)

        open_a1_list = _opening_attributes_blocks(cm1)
        open_a2_list = _opening_attributes_blocks(cm2)
        open_a1_ids = {id(el) for el in open_a1_list}
        open_a2_ids = {id(el) for el in open_a2_list}
        out_m = ET.Element("measure", dict(cm1.attrib))
        merged_attrs = _merge_measure_attributes(
            _combine_attributes_blocks(open_a1_list),
            _combine_attributes_blocks(open_a2_list),
            target_div=target_div if idx == 0 or d1_txt or d2_txt else None,
            include_staves=(idx == 0),
        )
        for ch in cm1:
            if ch.tag == "print" or (
                ch.tag == "barline" and ch.get("location") == "left"
            ):
                out_m.append(ch)
        for ch in cm2:
            if (
                ch.tag == "barline"
                and ch.get("location") == "left"
                and "left" not in b1
            ):
                out_m.append(ch)
        if merged_attrs is not None:
            out_m.append(merged_attrs)

        for ch in cm1:
            if ch.tag in {"print", "barline"} or id(ch) in open_a1_ids:
                continue
            if ch.tag == "attributes":
                mid_a1 = _emit_mid_measure_attributes(ch, staff_no=1)
                if mid_a1 is not None:
                    out_m.append(mid_a1)
                continue
            _tag_staff_and_clef(ch, staff_no=1, voice_offset=0)
            out_m.append(ch)

        s1_cursor = _measure_final_cursor(cm1)
        _ = _measure_final_cursor(cm2)
        if s1_cursor > 0:
            backup = ET.Element("backup")
            dur = ET.SubElement(backup, "duration")
            dur.text = str(s1_cursor)
            out_m.append(backup)

        for ch in cm2:
            if ch.tag in {"print", "barline"} or id(ch) in open_a2_ids:
                continue
            if ch.tag == "attributes":
                mid_a2 = _emit_mid_measure_attributes(ch, staff_no=2)
                if mid_a2 is not None:
                    out_m.append(mid_a2)
                continue
            _tag_staff_and_clef(ch, staff_no=2, voice_offset=max_v1)
            out_m.append(ch)

        for ch in cm1:
            if ch.tag == "barline" and ch.get("location", "right") != "left":
                out_m.append(ch)
        for ch in cm2:
            if (
                ch.tag == "barline"
                and ch.get("location", "right") != "left"
                and "right" not in b1
            ):
                out_m.append(ch)

        merged_part.append(out_m)

    new_root = ET.Element("score-partwise", {"version": "4.0"})
    for child in root:
        if child.tag in {"part-list", "part"}:
            continue
        new_root.append(copy.deepcopy(child))
    part_list = ET.SubElement(new_root, "part-list")
    sp = ET.SubElement(part_list, "score-part", {"id": "P1"})
    part_name = ET.SubElement(sp, "part-name")
    part_name.text = "Piano"
    new_root.append(merged_part)
    return _serialize_xml(new_root)


def _tag_staff_and_clef(
    element: ET.Element, *, staff_no: int, voice_offset: int
) -> None:
    if element.tag == "note":
        if voice_offset > 0:
            v_el = element.find("voice")
            base_v = (
                int(v_el.text.strip())
                if v_el is not None
                and v_el.text
                and v_el.text.strip().isdigit()
                else 1
            )
            if v_el is None:
                v_el = ET.SubElement(element, "voice")
            v_el.text = str(base_v + voice_offset)
        st_el = element.find("staff")
        if st_el is None:
            _insert_staff_in_note(element, staff_no)
        else:
            st_el.text = str(staff_no)
    elif element.tag == "direction":
        st_el = element.find("staff")
        if st_el is None:
            st_el = ET.SubElement(element, "staff")
        st_el.text = str(staff_no)
    elif element.tag == "forward" and voice_offset > 0:
        v_el = element.find("voice")
        if v_el is not None and v_el.text and v_el.text.strip().isdigit():
            v_el.text = str(int(v_el.text.strip()) + voice_offset)
        st_el = element.find("staff")
        if st_el is not None:
            st_el.text = str(staff_no)


def _opening_attributes_blocks(measure: ET.Element) -> list[ET.Element]:
    """Return all onset-0 ``<attributes>`` blocks before any timed event."""
    blocks: list[ET.Element] = []
    for ch in measure:
        if ch.tag in {"note", "backup", "forward"}:
            break
        if ch.tag == "attributes":
            blocks.append(ch)
    return blocks


def _combine_attributes_blocks(
    blocks: Sequence[ET.Element],
) -> ET.Element | None:
    if not blocks:
        return None
    out = ET.Element("attributes")
    for b in blocks:
        for child in b:
            out.append(copy.deepcopy(child))
    return out


def _emit_mid_measure_attributes(
    attr_el: ET.Element, staff_no: int
) -> ET.Element | None:
    """Tag a mid-measure ``<attributes>`` block for ``staff_no`` inline."""
    if any(
        attr_el.find(tag) is not None for tag in ("divisions", "time", "staves")
    ):
        msg = "unsupported mid-measure divisions/time/staves in 2-part merge"
        raise CandidateNormalizationError(msg)
    out = ET.Element("attributes")
    for sub in attr_el:
        if sub.tag in {"key", "clef"}:
            sc = copy.deepcopy(sub)
            sc.set("number", str(staff_no))
            out.append(sc)
    return out if len(out) > 0 else None


def _insert_staff_in_note(note: ET.Element, staff_no: int) -> None:
    after_tags = {"beam", "notations", "lyric", "play", "listen"}
    st_el = ET.Element("staff")
    st_el.text = str(staff_no)
    for idx, child in enumerate(list(note)):
        if child.tag in after_tags:
            note.insert(idx, st_el)
            return
    note.append(st_el)


def _merge_measure_attributes(  # noqa: C901, PLR0912
    a1: ET.Element | None,
    a2: ET.Element | None,
    *,
    target_div: int | None,
    include_staves: bool,
) -> ET.Element | None:
    if a1 is None and a2 is None and target_div is None and not include_staves:
        return None
    attrs = ET.Element("attributes")
    if target_div is not None:
        div_el = ET.SubElement(attrs, "divisions")
        div_el.text = str(target_div)

    k1 = a1.find("key") if a1 is not None else None
    k2 = a2.find("key") if a2 is not None else None
    if k1 is not None and k2 is not None:
        f1 = (k1.findtext("fifths") or "").strip()
        f2 = (k2.findtext("fifths") or "").strip()
        m1_mode = (k1.findtext("mode") or "").strip()
        m2_mode = (k2.findtext("mode") or "").strip()
        if (f1, m1_mode) == (f2, m2_mode):
            kc = copy.deepcopy(k1)
            kc.attrib.pop("number", None)
            attrs.append(kc)
        else:
            kc1 = copy.deepcopy(k1)
            kc1.set("number", "1")
            kc2 = copy.deepcopy(k2)
            kc2.set("number", "2")
            attrs.extend((kc1, kc2))
    elif k1 is not None:
        kc1 = copy.deepcopy(k1)
        kc1.set("number", "1")
        attrs.append(kc1)
    elif k2 is not None:
        kc2 = copy.deepcopy(k2)
        kc2.set("number", "2")
        attrs.append(kc2)

    t1 = a1.find("time") if a1 is not None else None
    t2 = a2.find("time") if a2 is not None else None
    t_el = t1 if t1 is not None else t2
    if t_el is not None:
        attrs.append(copy.deepcopy(t_el))

    if include_staves:
        st_el = ET.SubElement(attrs, "staves")
        st_el.text = "2"

    if a1 is not None:
        for clef in a1.findall("clef"):
            cc = copy.deepcopy(clef)
            cc.set("number", "1")
            attrs.append(cc)
    if a2 is not None:
        for clef in a2.findall("clef"):
            cc = copy.deepcopy(clef)
            cc.set("number", "2")
            attrs.append(cc)

    return attrs if len(attrs) > 0 else None


def validate_candidate_score_pair(
    norm_xml: str,
) -> tuple[AnnotatedScore, AnnotatedScore]:
    """Verify ``norm_xml`` through content, layout and supplementary passes."""
    install_coupling_fix()
    ann = annotate(norm_xml)
    layout_ann = annotate(norm_xml, detail=LAYOUT_DETAIL)
    side = _Side(ann, frozenset(), {})
    for gen in (
        _compared_fingerings(side),
        _wavy_lines(side),
        _dashes_lines(side),
        _voice_lines(side),
        _parentheses(side, "notehead"),
        _parentheses(side, "accidental"),
        _metronome_parentheses(side),
        _other_dynamics(side),
        _pedal_ends(side, frozenset()),
        _tremolos(side),
        _hooks(side),
        _rest_beams(side),
        _cross_barline_beams(side),
        _ratios(side, frozenset()),
        _slurs(side),
        _tie_stops(side),
    ):
        _ = list(gen)
    _ = _cross_staff_events(ann)
    _ = _voice_content_groups(ann)
    return ann, layout_ann


def validate_candidate_score(norm_xml: str) -> AnnotatedScore:
    """Verify ``norm_xml`` parses cleanly through musicdiff & supplementary."""
    return validate_candidate_score_pair(norm_xml)[0]


def _parse_normalized_page(
    item: Path | str | bytes,
    expected_conf_len: int | None,
) -> tuple[ET.Element, list[ET.Element], int]:
    norm_str = merge_two_part_grand_staff(item)
    _ = validate_candidate_score(norm_str)
    root = _load_musicxml(norm_str)
    part = root.find("part")
    measures = [] if part is None else part.findall("measure")
    if not measures:
        msg = "normalized page has 0 measures"
        raise CandidateNormalizationError(msg)
    if expected_conf_len is not None and expected_conf_len != len(measures):
        msg = (
            f"posterior count {expected_conf_len} != "
            f"page measure count {len(measures)}"
        )
        raise CandidateNormalizationError(msg)
    page_divs = [
        int(d.text.strip())
        for d in root.findall(".//part/measure/attributes/divisions")
        if d.text and d.text.strip().isdigit() and int(d.text.strip()) > 0
    ]
    page_lcm = math.lcm(*page_divs) if page_divs else 1
    return root, measures, page_lcm


def concat_page_predictions(  # noqa: C901, PLR0912, PLR0915
    page_xmls: Sequence[Path | str | bytes | None],
    *,
    page_raw_confs: Mapping[int, Sequence[float]] | None = None,
) -> PageAssemblyResult:
    """Concatenate 1-based per-page candidate MusicXMLs into ``P_piece``."""
    valid_pages: list[tuple[int, ET.Element, list[ET.Element], int]] = []
    failed_pages: list[int] = []
    error_reasons: dict[int, str] = {}

    for page_idx, item in enumerate(page_xmls, start=1):
        if item is None:
            failed_pages.append(page_idx)
            error_reasons[page_idx] = "missing page MusicXML"
            continue
        exp_len = (
            len(page_raw_confs[page_idx])
            if (page_raw_confs is not None and page_idx in page_raw_confs)
            else None
        )
        try:
            root, measures, page_lcm = _parse_normalized_page(item, exp_len)
            valid_pages.append((page_idx, root, measures, page_lcm))
        except (
            CandidateNormalizationError,
            ScoreParseError,
            ProtocolDefectError,
            MusicXmlError,
            ET.ParseError,
        ) as exc:
            failed_pages.append(page_idx)
            error_reasons[page_idx] = str(exc)

    if not valid_pages:
        return PageAssemblyResult(
            xml_text=None,
            parse_valid=False,
            failed_pages=tuple(failed_pages),
            pred_measure_pages=(),
            pred_measure_raw_conf=(),
            error_reasons=error_reasons,
        )

    global_div = math.lcm(*(p_div for _, _, _, p_div in valid_pages))
    out_root = ET.Element("score-partwise", {"version": "4.0"})
    first_root = valid_pages[0][1]
    for child in first_root:
        if child.tag in {"part-list", "part"}:
            continue
        out_root.append(copy.deepcopy(child))
    part_list = ET.SubElement(out_root, "part-list")
    sp = ET.SubElement(part_list, "score-part", {"id": "P1"})
    part_name = ET.SubElement(sp, "part-name")
    part_name.text = "Piano"
    out_part = ET.SubElement(out_root, "part", {"id": "P1"})

    pred_pages: list[int] = []
    pred_confs: list[float] = []
    span_offset = 0
    measure_no = 0

    for valid_ord, (page_idx, _, measures, _) in enumerate(valid_pages):
        cloned = [copy.deepcopy(m) for m in measures]
        span_offset = _renumber_spanners(cloned, span_offset) + 10
        cur_div = 1
        raw_seq = (
            list(page_raw_confs.get(page_idx, ()))
            if page_raw_confs is not None
            else []
        )
        for m_i, meas in enumerate(cloned):
            d_txt = meas.findtext("./attributes/divisions")
            if d_txt and d_txt.strip().isdigit() and int(d_txt.strip()) > 0:
                cur_div = int(d_txt.strip())
            _scale_measure_divisions(meas, cur_div, global_div)
            if valid_ord == 0 and m_i == 0:
                attrs = meas.find("attributes")
                if attrs is None:
                    attrs = ET.Element("attributes")
                    meas.insert(0, attrs)
                if attrs.find("divisions") is None:
                    div_el = ET.Element("divisions")
                    div_el.text = str(global_div)
                    attrs.insert(0, div_el)
                if attrs.find("staves") is None:
                    st_el = ET.SubElement(attrs, "staves")
                    st_el.text = "2"
            if valid_ord > 0 and m_i == 0:
                pr = meas.find("print")
                if pr is None:
                    pr = ET.Element("print", {"new-page": "yes"})
                    meas.insert(0, pr)
                else:
                    pr.set("new-page", "yes")
                    pr.attrib.pop("new-system", None)
                attrs = meas.find("attributes")
                if attrs is not None:
                    for st_el in list(attrs.findall("staves")):
                        attrs.remove(st_el)
            measure_no += 1
            meas.set("number", str(measure_no))
            out_part.append(meas)
            pred_pages.append(page_idx)
            conf = float(raw_seq[m_i]) if m_i < len(raw_seq) else 0.0
            pred_confs.append(conf)

    assembled_xml = _serialize_xml(out_root)
    try:
        _ = validate_candidate_score(assembled_xml)
    except (
        CandidateNormalizationError,
        ScoreParseError,
        ProtocolDefectError,
        MusicXmlError,
        ET.ParseError,
    ) as exc:
        error_reasons[0] = f"assembled piece validation failed: {exc}"
        return PageAssemblyResult(
            xml_text=None,
            parse_valid=False,
            failed_pages=tuple(failed_pages),
            pred_measure_pages=(),
            pred_measure_raw_conf=(),
            error_reasons=error_reasons,
        )

    return PageAssemblyResult(
        xml_text=assembled_xml,
        parse_valid=True,
        failed_pages=tuple(failed_pages),
        pred_measure_pages=tuple(pred_pages),
        pred_measure_raw_conf=tuple(pred_confs),
        error_reasons=error_reasons,
    )


def _serialize_xml(root: ET.Element) -> str:
    ET.indent(root)
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        + ET.tostring(root, encoding="unicode")
        + "\n"
    )
