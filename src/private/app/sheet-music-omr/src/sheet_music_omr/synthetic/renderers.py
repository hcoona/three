"""W4c rendering, pixel-frame reconciliation and symbolic label gates."""

from __future__ import annotations

import contextlib
import io
import json
import logging
import math
import os
import random
import re
import signal
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from dataclasses import dataclass, field, replace
from importlib.metadata import version
from itertools import pairwise
from pathlib import Path
from statistics import median
from typing import TYPE_CHECKING
from uuid import uuid4

import converter21
import numpy as np
from music21 import converter
from music21.stream import Score
from musicdiff.annotation import AnnExtra, AnnScore
from musicdiff.comparison import Comparison
from PIL import Image

from sheet_music_omr.comparison import (
    _EXTRA_KINDS,
    CONTENT_DETAIL,
    MUSICDIFF_VERSION,
    InstrumentMismatchError,
    ScoreParseError,
    annotate,
)
from sheet_music_omr.harness.musescore import (
    LOCK_NAME,
    STEP_SECONDS,
    _windows_path_for_wsl,
    has_source_verified_defaults,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import (
    SplitError,
    SystemLayout,
    SystemStart,
    compare_assembly,
    split_score,
    system_starts,
)
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_events import (
    ProtocolDefectError,
    read_score,
    voice_staves,
)
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
    _parse_xml,
    from_musicxml_text,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.supplementary import compare_file
from sheet_music_omr.synthetic.degrade import estimate_staff_space
from sheet_music_omr.synthetic.ledgers import (
    RESVG_VERSION,
    VEROVIO_VERSION,
    WORKER_TIMEOUT_SECONDS,
)
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence
    from fractions import Fraction

    from sheet_music_omr.harness.musescore import MuseScore
    from sheet_music_omr.synthetic.degrade import Box


_HOST_FONTS = (
    "Leland",
    "Bravura",
    "Gootville",
    "Petaluma",
    "Finale Maestro",
    "Finale Broadway",
)
_VEROVIO_FONTS = ("Leipzig", "Bravura", "Gootville", "Leland", "Petaluma")
_DENSITIES = ((2, 3), (3, 5), (4, 4), (4, 6), (5, 5), (6, 4), (7, 6))
_SCALE_MPOS = 12.0
_RASTER_INK_THRESHOLD = 180
_VIEWBOX_FIELDS = 4
_XML_ID = "{http://www.w3.org/XML/1998/namespace}id"
_MEI_DIGIT_TAG = "f" + "ing"


class RenderRejected(ValueError):  # noqa: N818
    """A sample cannot be emitted with a provable label-to-raster link."""


@dataclass(frozen=True, slots=True)
class LayoutStyle:
    """One reproducible density draw and its two engraving styles."""

    plate_group: str
    measures_per_system: int
    systems_per_page: int
    layout: SystemLayout
    musescore_style: str
    verovio_options: Mapping[str, str | int | float | bool]


def sample_layout_and_style(seed: int, num_measures: int) -> LayoutStyle:
    """Draw a group, its intended system starts, and both engraving styles."""
    if num_measures < 1:
        msg = "layout sampling requires at least one measure"
        raise ValueError(msg)
    rng = random.Random(seed)  # noqa: S311 - reproducible style sampling
    group = rng.randrange(len(_DENSITIES))
    per_system, per_page = _DENSITIES[group]
    starts = tuple(
        SystemStart(ordinal, i // per_page + 1, i % per_page + 1)
        for i, ordinal in enumerate(range(0, num_measures, per_system))
    )
    root = ET.Element("museScore", version="4.0")
    style = ET.SubElement(root, "Style")
    for key, value in {
        "Spatium": rng.uniform(1.40, 1.85),
        "staffLineWidth": rng.uniform(0.07, 0.13),
        "stemWidth": rng.uniform(0.085, 0.135),
        "barWidth": rng.uniform(0.13, 0.20),
        "musicalSymbolFont": rng.choice(_HOST_FONTS),
        "createMultiMeasureRests": 0,
        "pageWidth": 8.5,
        "pageHeight": 11.0,
        "pagePrintableWidth": 7.5,
    }.items():
        ET.SubElement(style, key).text = str(value)
    staff_width = rng.uniform(0.10, 0.17)
    vrv_page_w = rng.randint(2260, 2340)
    vrv_page_h = max(3185, per_page * 680 + rng.randint(220, 340))
    return LayoutStyle(
        f"G{group + 1}",
        per_system,
        per_page,
        SystemLayout(num_measures, starts),
        ET.tostring(root, encoding="unicode"),
        {
            "font": rng.choice(_VEROVIO_FONTS),
            "pageWidth": vrv_page_w,
            "pageHeight": vrv_page_h,
            "scale": rng.randint(96, 104),
            "breaks": "encoded",
            "svgBoundingBoxes": True,
            "svgContentBoundingBoxes": True,
            "svgViewBox": True,
            "pedalStyle": "auto",
            "staffLineWidth": staff_width,
            "stemWidth": rng.uniform(0.14, 0.22),
            "barLineWidth": rng.uniform(0.18, 0.29),
        },
    )


def _parts(root: ET.Element) -> list[ET.Element]:
    parts = root.findall("part")
    if root.tag != "score-partwise" or len(parts) != 1:
        msg = "expected one MusicXML piano part"
        raise RenderRejected(msg)
    return parts[0].findall("measure")


def encode_layout_breaks(musicxml: str, layout: SystemLayout) -> str:
    """Replace page/system breaks without removing unrelated print data."""
    root = _parse_xml(musicxml)
    measures = _parts(root)
    if len(measures) != layout.measures:
        msg = "layout count does not match MusicXML measures"
        raise RenderRejected(msg)
    breaks = {
        start.ordinal: "new-page" if start.page != prev.page else "new-system"
        for prev, start in pairwise(layout.starts)
    }
    for ordinal, measure in enumerate(measures):
        for node in measure.findall("print"):
            for key in ("new-page", "new-system", "page-number"):
                node.attrib.pop(key, None)
            if not node.attrib and not len(node):
                measure.remove(node)
        if ordinal in breaks:
            measure.insert(0, ET.Element("print", {breaks[ordinal]: "yes"}))
    encoded = ET.tostring(root, encoding="unicode")
    if system_starts(encoded) != layout:
        msg = "encoded breaks do not reproduce the requested layout"
        raise RenderRejected(msg)
    return encoded


@dataclass(frozen=True, slots=True)
class RenderedSystem:
    """A system in the raster frame linked to inclusive canonical ordinals."""

    page: int
    system: int
    first_ordinal: int
    last_ordinal: int
    box: Box
    staff_space_px: float


@dataclass(frozen=True, slots=True)
class RenderedMeasure:
    """The horizontal measure span and its system's vertical crop bounds."""

    ordinal: int
    box: Box


@dataclass(frozen=True, slots=True)
class PageGeometry:
    """One rendered page's untrimmed raster geometry."""

    width: int
    height: int
    systems: tuple[RenderedSystem, ...]
    measures: tuple[RenderedMeasure, ...]


@dataclass(frozen=True, slots=True)
class RenderedPage:
    """Lossless raster bytes and verified geometry from one renderer."""

    png: bytes
    geometry: PageGeometry


def _box_union(boxes: Sequence[Box]) -> Box:
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def _ordered_spans(
    layout: SystemLayout,
) -> tuple[tuple[int, int, int, int], ...]:
    ends = [*(s.ordinal for s in layout.starts[1:]), layout.measures]
    return tuple(
        (s.page, s.system, s.ordinal + 1, end)
        for s, end in zip(layout.starts, ends, strict=True)
    )


def _rendered_spans(
    geometries: Sequence[PageGeometry],
) -> tuple[tuple[int, int, int, int], ...]:
    return tuple(
        (s.page, s.system, s.first_ordinal, s.last_ordinal)
        for page in geometries
        for s in page.systems
    )


def _clip_box(box: Box, size: tuple[int, int]) -> Box:
    w, h = size
    x0, y0, x1, y1 = box
    clipped = max(0.0, x0), max(0.0, y0), min(w, x1), min(h, y1)
    if not all(map(math.isfinite, box)) or not (
        clipped[0] < clipped[2] and clipped[1] < clipped[3]
    ):
        msg = "degenerate or off-page renderer rectangle"
        raise RenderRejected(msg)
    return clipped


def _mpos_rows(
    mpos_xml: str, number_of_measures: int
) -> dict[tuple[int, float], list[RenderedMeasure]]:
    root = _parse_xml(mpos_xml)
    rows = root.findall("elements/element")
    ids = [int(el.attrib["id"]) for el in rows]
    if root.tag != "score" or sorted(ids) != list(range(number_of_measures)):
        msg = "MuseScore position IDs are not a canonical measure bijection"
        raise RenderRejected(msg)
    grouped: dict[tuple[int, float], list[RenderedMeasure]] = defaultdict(list)
    for el, ordinal in zip(rows, ids, strict=True):
        page = int(el.attrib["page"]) + 1
        x, y, w, h = (
            float(el.attrib[k]) / _SCALE_MPOS for k in ("x", "y", "sx", "sy")
        )
        grouped[page, y].append(
            RenderedMeasure(ordinal + 1, (x, y, x + w, y + h))
        )
    return grouped


def _gray(image: Image.Image) -> Image.Image:
    if "A" in image.getbands() or "transparency" in image.info:
        rgba = image.convert("RGBA")
        bg = Image.new("RGBA", image.size, "white")
        return Image.alpha_composite(bg, rgba).convert("L")
    return image.convert("L")


def _ink_rows(gray: np.ndarray, boxes: Sequence[Box]) -> np.ndarray:
    x0, _, x1, _ = _box_union(boxes)
    left = max(0, math.floor(x0))
    right = min(gray.shape[1], math.ceil(x1))
    return (gray[:, left:right] < _RASTER_INK_THRESHOLD).sum(axis=1)


def _gap(row_ink: np.ndarray, lo: int, hi: int, max_gutter_ink: int) -> int:
    if hi < lo:
        msg = "rendered system staff bounds overlap"
        raise RenderRejected(msg)
    rows = np.arange(lo, hi + 1)
    middle = (hi + lo) / 2
    slice_ink = row_ink[lo : hi + 1]
    clear = rows[slice_ink == 0]
    if len(clear):
        return int(clear[np.argmin(np.abs(clear - middle))])
    min_ink = int(slice_ink.min())
    if min_ink > max_gutter_ink:
        msg = "no clear mid-gap separates adjacent rendered systems"
        raise RenderRejected(msg)
    near = rows[slice_ink <= min_ink + 2]
    return int(near[np.argmin(np.abs(near - middle))])


def _scan_outer_system_edge(
    row_ink: np.ndarray,
    bounds: tuple[int, int],
    step: int,
    blank_run: int,
    pad: int,
) -> int:
    start, limit = bounds
    cur = start
    consecutive_blank = 0
    last_ink = start
    while (cur >= limit) if step < 0 else (cur <= limit):
        if int(row_ink[cur]) == 0:
            consecutive_blank += 1
            if consecutive_blank >= blank_run:
                break
        else:
            consecutive_blank = 0
            last_ink = cur
        cur += step
    target = last_ink + step * pad
    return max(limit, target) if step < 0 else min(limit, target)


def _expand_vertical(
    geometry: PageGeometry, image: Image.Image
) -> PageGeometry:
    systems, measures = geometry.systems, geometry.measures
    gray = np.asarray(_gray(image))
    seeds = [s.box for s in systems]
    if not seeds:
        msg = "blank rendered page has no systems"
        raise RenderRejected(msg)
    max_gutter_ink = max(16, round(0.015 * geometry.width))
    edges = [0]
    for a, b in pairwise(seeds):
        edges.append(
            _gap(
                _ink_rows(gray, (a, b)),
                max(0, math.ceil(a[3])),
                min(geometry.height - 1, math.floor(b[1])),
                max_gutter_ink,
            )
        )
    edges.append(geometry.height)
    expanded: list[RenderedSystem] = []
    expanded_measures: list[RenderedMeasure] = []
    for system, (lo, hi) in zip(systems, pairwise(edges), strict=True):
        x0, sy0, x1, sy1 = system.box
        sp = max(6.0, float(system.staff_space_px))
        overhang_x = math.ceil(3.0 * sp)
        scan_box = (
            max(0.0, x0 - overhang_x),
            sy0,
            min(float(geometry.width), x1 + overhang_x),
            sy1,
        )
        full_rows = _ink_rows(gray, (scan_box,))
        owned = full_rows[lo:hi]
        ink = np.flatnonzero(owned)
        if not len(ink):
            msg = "rendered system rectangle has no raster ink"
            raise RenderRejected(msg)
        pad = math.ceil(1.8 * sp)
        blank_run = max(18, math.ceil(3.8 * sp))
        if lo == 0:
            top_lim = max(0, round(sy0 - 8.5 * sp))
            y0 = _scan_outer_system_edge(
                full_rows,
                (max(top_lim, math.floor(sy0)), top_lim),
                -1,
                blank_run,
                pad,
            )
        else:
            y0 = lo
        if hi == geometry.height:
            bot_lim = min(geometry.height - 1, round(sy1 + 8.5 * sp))
            y1 = (
                _scan_outer_system_edge(
                    full_rows,
                    (min(bot_lim, math.ceil(sy1)), bot_lim),
                    1,
                    blank_run,
                    pad,
                )
                + 1
            )
        else:
            y1 = hi
        col_ink = (gray[y0:y1, :] < _RASTER_INK_THRESHOLD).sum(axis=0)
        pad_x = math.ceil(0.6 * sp)
        blank_x = max(10, math.ceil(1.6 * sp))
        left_lim = max(0, math.floor(x0 - overhang_x))
        right_lim = min(geometry.width - 1, math.ceil(x1 + overhang_x))
        nx0 = float(
            _scan_outer_system_edge(
                col_ink,
                (max(left_lim, math.floor(x0)), left_lim),
                -1,
                blank_x,
                pad_x,
            )
        )
        nx1 = float(
            _scan_outer_system_edge(
                col_ink,
                (min(right_lim, math.ceil(x1) - 1), right_lim),
                1,
                blank_x,
                pad_x,
            )
            + 1
        )
        expanded.append(
            replace(system, box=(min(x0, nx0), y0, max(x1, nx1), y1))
        )
        expanded_measures.extend(
            replace(m, box=(m.box[0], y0, m.box[2], y1))
            for m in measures[
                system.first_ordinal
                - systems[0].first_ordinal : system.last_ordinal
                - systems[0].first_ordinal
                + 1
            ]
        )
    return replace(
        geometry, systems=tuple(expanded), measures=tuple(expanded_measures)
    )


def parse_musescore_geometry(
    mpos_xml: str, rendered_musicxml: str, page_images: Sequence[Image.Image]
) -> tuple[PageGeometry, ...]:
    """Verify the two sidecar checks and map 12-unit coordinates into pixels."""
    layout = system_starts(rendered_musicxml)
    root = _parse_xml(rendered_musicxml)
    if any(int(r.text or "0") > 1 for r in root.iter("multiple-rest")):
        msg = "MuseScore collapsed a multimeasure rest"
        raise RenderRejected(msg)
    grouped = _mpos_rows(mpos_xml, layout.measures)
    groups: dict[int, list[list[RenderedMeasure]]] = defaultdict(list)
    for (page, _), measures in sorted(grouped.items()):
        group = sorted(measures, key=lambda m: m.ordinal)
        groups[page].append(group)
    spans = tuple(
        (page, index, group[0].ordinal, group[-1].ordinal)
        for page, page_groups in groups.items()
        for index, group in enumerate(page_groups, 1)
    )
    if spans != _ordered_spans(layout) or any(
        [m.ordinal for m in group]
        != list(range(group[0].ordinal, group[-1].ordinal + 1))
        or any(a.box[0] >= b.box[0] for a, b in pairwise(group))
        for page_groups in groups.values()
        for group in page_groups
    ):
        msg = "MuseScore position-system spans disagree with re-export breaks"
        raise RenderRejected(msg)
    if list(groups) != list(range(1, len(page_images) + 1)):
        msg = "MuseScore page inventory disagrees with raster output"
        raise RenderRejected(msg)
    return tuple(
        _mpos_page(page, groups[page], image)
        for page, image in enumerate(page_images, 1)
    )


def _mpos_page(
    page: int, groups: Sequence[Sequence[RenderedMeasure]], image: Image.Image
) -> PageGeometry:
    systems: list[RenderedSystem] = []
    measures: list[RenderedMeasure] = []
    gray = _gray(image)
    for index, group in enumerate(groups, 1):
        clipped = [replace(m, box=_clip_box(m.box, image.size)) for m in group]
        box = _box_union([m.box for m in clipped])
        spacing = estimate_staff_space(gray.crop(box))
        systems.append(
            RenderedSystem(
                page, index, group[0].ordinal, group[-1].ordinal, box, spacing
            )
        )
        measures.extend(clipped)
    return _expand_vertical(
        PageGeometry(*image.size, tuple(systems), tuple(measures)), image
    )


# SVG maps use column vectors; parent transforms left-multiply child transforms.
type _Affine = tuple[float, float, float, float, float, float]
_IDENTITY: _Affine = (1, 0, 0, 1, 0, 0)
_NUMBER = r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?"
_NUMBERS = re.compile(_NUMBER)
_TRANSFORM = re.compile(r"([a-zA-Z]+)\s*\(([^()]*)\)")
_HORIZONTAL = re.compile(
    rf"\s*M\s*({_NUMBER})[ ,]+({_NUMBER})\s*"
    rf"(?:L\s*({_NUMBER})[ ,]+({_NUMBER})|H\s*({_NUMBER}))\s*"
)


def _multiply(parent: _Affine, child: _Affine) -> _Affine:
    a, b, c, d, e, f = parent
    g, h, i, j, k, shift = child
    return (
        a * g + c * h,
        b * g + d * h,
        a * i + c * j,
        b * i + d * j,
        a * k + c * shift + e,
        b * k + d * shift + f,
    )


def _translate(x: float, y: float) -> _Affine:
    return (1, 0, 0, 1, x, y)


def _scale(x: float, y: float) -> _Affine:
    return (x, 0, 0, y, 0, 0)


def _transform_op(name: str, params: list[float]) -> _Affine:  # noqa: PLR0911
    match (name, params):
        case ("translate", [x]):
            return _translate(x, 0)
        case ("translate", [x, y]):
            return _translate(x, y)
        case ("scale", [x]):
            return _scale(x, x)
        case ("scale", [x, y]):
            return _scale(x, y)
        case ("matrix", [a, b, c, d, e, f]):
            return a, b, c, d, e, f
        case ("rotate", [angle, *center]) if len(center) in {0, 2}:
            cos, sin = (
                math.cos(math.radians(angle)),
                math.sin(math.radians(angle)),
            )
            turn: _Affine = (cos, sin, -sin, cos, 0, 0)
            if center:
                x, y = center
                return _multiply(
                    _translate(x, y), _multiply(turn, _translate(-x, -y))
                )
            return turn
        case ("skewX", [angle]):
            return 1, 0, math.tan(math.radians(angle)), 1, 0, 0
        case ("skewY", [angle]):
            return 1, math.tan(math.radians(angle)), 0, 1, 0, 0
        case _:
            msg = "unsupported SVG transform"
            raise RenderRejected(msg)


def _read_transform(raw: str) -> _Affine:
    result = _IDENTITY
    end = 0
    for match in _TRANSFORM.finditer(raw):
        params = match[2]
        if raw[end : match.start()].strip(" ,\t\n") or _NUMBERS.sub(
            "", params
        ).strip(" ,\t\n"):
            msg = "invalid SVG transform syntax"
            raise RenderRejected(msg)
        op = _transform_op(
            match[1], [float(n) for n in _NUMBERS.findall(params)]
        )
        result = _multiply(result, op)
        end = match.end()
    if raw[end:].strip():
        msg = "unparsed SVG transform"
        raise RenderRejected(msg)
    return result


def _length(raw: str | None, default: float) -> float:
    if raw is None:
        return default
    match = re.fullmatch(rf"\s*({_NUMBER})(px|%|in|mm|cm|pt)?\s*", raw)
    if match is None:
        msg = "unsupported SVG geometry length"
        raise RenderRejected(msg)
    units = {
        "in": 96,
        "mm": 96 / 25.4,
        "cm": 96 / 2.54,
        "pt": 96 / 72,
        "%": default / 100,
    }
    return float(match[1]) * units.get(match[2], 1)


def _viewport(
    node: ET.Element, size: tuple[float, float]
) -> tuple[_Affine, tuple[float, float]]:
    raw = node.get("viewBox")
    if raw is None:
        return _IDENTITY, size
    x, y, vw, vh = (float(n) for n in _NUMBERS.findall(raw))
    if min(vw, vh, *size) <= 0:
        msg = "non-positive SVG viewport"
        raise RenderRejected(msg)
    sx, sy = size[0] / vw, size[1] / vh
    align, *mode = node.get("preserveAspectRatio", "xMidYMid meet").split()
    if align == "none":
        return _multiply(_scale(sx, sy), _translate(-x, -y)), (vw, vh)
    zoom = max(sx, sy) if mode == ["slice"] else min(sx, sy)
    offsets = {"Min": 0, "Mid": 0.5, "Max": 1}
    dx = (size[0] - vw * zoom) * offsets[align[1:4]]
    dy = (size[1] - vh * zoom) * offsets[align[5:8]]
    return (zoom, 0, 0, zoom, dx - x * zoom, dy - y * zoom), (vw, vh)


def _local(node: ET.Element) -> str:
    return node.tag.rsplit("}", 1)[-1]


def _classes(node: ET.Element) -> set[str]:
    return set(node.get("class", "").split())


def _map_point(m: _Affine, x: float, y: float) -> tuple[float, float]:
    a, b, c, d, e, f = m
    return a * x + c * y + e, b * x + d * y + f


def _rect_box(node: ET.Element, m: _Affine) -> Box:
    x, y, w, h = (
        _length(node.get(k), 0) for k in ("x", "y", "width", "height")
    )
    points = [_map_point(m, xx, yy) for xx in (x, x + w) for yy in (y, y + h)]
    xs, ys = zip(*points, strict=True)
    return min(xs), min(ys), max(xs), max(ys)


@dataclass(slots=True)
class _SvgIndex:
    matrices: dict[ET.Element, _Affine] = field(default_factory=dict)
    boxes: dict[str, list[Box]] = field(
        default_factory=lambda: defaultdict(list)
    )
    systems: list[tuple[ET.Element, list[ET.Element]]] = field(
        default_factory=list
    )

    def visit(  # noqa: C901, PLR0913
        self,
        node: ET.Element,
        parent: _Affine,
        size: tuple[float, float],
        *,
        root: bool = False,
        owner: str = "",
        measures: list[ET.Element] | None = None,
    ) -> None:
        tag = _local(node)
        if tag in {"defs", "style", "metadata", "symbol", "clipPath"}:
            return
        m = _multiply(parent, _read_transform(node.get("transform", "")))
        if tag == "svg":
            dims = (
                size
                if root
                else (
                    _length(node.get("width"), size[0]),
                    _length(node.get("height"), size[1]),
                )
            )
            offset = _translate(
                _length(node.get("x", "0"), size[0]),
                _length(node.get("y", "0"), size[1]),
            )
            frame, size = _viewport(node, dims)
            m = _multiply(m, _multiply(offset, frame))
        self.matrices[node] = m
        classes = _classes(node)
        node_id = node.get("id", node.get("data-id", ""))
        if classes & {"bounding-box", "content-bounding-box"}:
            owner = (
                node_id.removeprefix("cbbox-").removeprefix("bbox-") or owner
            )
            for child in node:
                if _local(child) == "rect":
                    rect = _rect_box(
                        child,
                        _multiply(
                            m, _read_transform(child.get("transform", ""))
                        ),
                    )
                    if rect[0] < rect[2] and rect[1] < rect[3]:
                        self.boxes[owner].append(rect)
            return
        if "system" in classes:
            measures = []
            self.systems.append((node, measures))
        if "measure" in classes:
            if measures is None:
                msg = "SVG measure sits outside a printed system"
                raise RenderRejected(msg)
            measures.append(node)
        if node_id:
            owner = node_id
        for child in node:
            self.visit(child, m, size, owner=owner, measures=measures)


def _staff_path(
    node: ET.Element, transform: _Affine
) -> tuple[tuple[float, float], tuple[float, float]] | None:
    match = _HORIZONTAL.fullmatch(node.get("d", ""))
    if not match:
        return None
    x0, y0 = float(match[1]), float(match[2])
    x1, y1 = (
        (float(match[3]), float(match[4]))
        if match[3]
        else (float(match[5]), y0)
    )
    if abs(y0 - y1) > 0.001:  # noqa: PLR2004
        return None
    return _map_point(transform, x0, y0), _map_point(transform, x1, y1)


def _staff_geometry(
    index: _SvgIndex, measure: ET.Element
) -> tuple[list[Box], list[float]]:
    boxes, spacings = [], []
    for staff in measure:
        if "staff" not in _classes(staff):
            continue
        lines = [
            line
            for child in staff
            if _local(child) == "path"
            and child in index.matrices
            and (line := _staff_path(child, index.matrices[child])) is not None
        ]
        if len(lines) != 5:  # noqa: PLR2004
            continue
        points = [p for line in lines for p in line]
        xs, ys = zip(*points, strict=True)
        mids = sorted((a[1] + b[1]) / 2 for a, b in lines)
        spacings.extend(b - a for a, b in pairwise(mids) if b > a)
        boxes.append((min(xs), min(ys), max(xs), max(ys)))
    return boxes, spacings


def _svg_measure(
    index: _SvgIndex, node: ET.Element, size: tuple[int, int]
) -> tuple[Box, list[float], list[Box]]:
    node_id = node.get("id", node.get("data-id", ""))
    staves, spacing = _staff_geometry(index, node)
    bboxes = index.boxes.get(node_id, [])
    if not bboxes and not staves:
        msg = "no renderer bounding box or staff paths for SVG measure"
        raise RenderRejected(msg)
    # Staff paths span barline-to-barline; prefer their horizontal endpoints to
    # content bounding boxes which can project beyond the measure (e.g. slurs).
    return _clip_box(_box_union(staves or bboxes), size), spacing, staves


def parse_verovio_svg_geometry(
    svg_text: str,
    *,
    page: int = 1,
    first_ordinal: int = 1,
    raster_size: tuple[int, int] | None = None,
    expected_measure_ids: Sequence[str] | None = None,
) -> PageGeometry:
    """Reconcile Verovio SVG groups in the cumulative root-to-raster frame."""
    root = _parse_xml(svg_text)
    if _local(root) != "svg":
        msg = "Verovio output does not have an SVG viewport"
        raise RenderRejected(msg)
    viewbox = _NUMBERS.findall(root.get("viewBox", ""))
    intrinsic_w, intrinsic_h = (
        (float(viewbox[2]), float(viewbox[3]))
        if len(viewbox) == _VIEWBOX_FIELDS
        else (0, 0)
    )
    nominal = (
        _length(root.get("width"), intrinsic_w),
        _length(root.get("height"), intrinsic_h),
    )
    size = raster_size or (round(nominal[0]), round(nominal[1]))
    if min(size) <= 0:
        msg = "SVG has no positive raster dimensions"
        raise RenderRejected(msg)
    index = _SvgIndex()
    index.visit(root, _IDENTITY, size, root=True)
    systems: list[RenderedSystem] = []
    measures: list[RenderedMeasure] = []
    ids: list[str] = []
    for system_index, (node, children) in enumerate(index.systems, 1):
        starts = first_ordinal + len(measures)
        system_boxes, ink_boxes, spaces = [], [], []
        for child in children:
            ids.append(child.get("id", child.get("data-id", "")))
            box, sample, staff_boxes = _svg_measure(index, child, size)
            measures.append(RenderedMeasure(first_ordinal + len(measures), box))
            system_boxes.append(box)
            ink_boxes.extend(staff_boxes)
            spaces.extend(sample)
        if not system_boxes or not spaces:
            msg = "Verovio system has no measurable five-line staff paths"
            raise RenderRejected(msg)
        identity = node.get("id", node.get("data-id", ""))
        declared = index.boxes.get(identity, [])
        seed = _box_union(ink_boxes)
        horizontal = _box_union([*system_boxes, *declared])
        box = _clip_box((horizontal[0], seed[1], horizontal[2], seed[3]), size)
        systems.append(
            RenderedSystem(
                page,
                system_index,
                starts,
                measures[-1].ordinal,
                box,
                median(spaces),
            )
        )
    if not systems or (
        expected_measure_ids is not None and ids != list(expected_measure_ids)
    ):
        msg = "Verovio SVG measure order does not match canonical ordinals"
        raise RenderRejected(msg)
    if len(ids) != len(set(ids)) or not all(ids):
        msg = "Verovio SVG measure IDs are missing or duplicated"
        raise RenderRejected(msg)
    return PageGeometry(*size, tuple(systems), tuple(measures))


_DROP_THRESHOLD = 0.05
_BASIC_CATEGORIES = {
    "pitch",
    "rhythm_rest",
    "voice",
    "time_signature",
    "key_signature",
}
_CATEGORY_TAGS = {
    "chord": "chord_membership",
    "accidental": "accidental",
    "alter": "accidental",
    "beam": "beam",
    "time-modification": "tuplet",
    "tie": "tie",
    "tied": "tie",
    "slur": "slur",
    "grace": "grace",
    "articulations": "articulation_fermata",
    "fermata": "articulation_fermata",
    "ornaments": "ornament",
    "tremolo": "tremolo",
    "fingering": "fingering",
    "dynamics": "dynamic",
    "wedge": "hairpin",
    "octave-shift": "ottava",
    "pedal": "pedal",
    "arpeggiate": "arpeggio",
    "repeat": "repeat_ending",
    "ending": "repeat_ending",
    "words": "text",
    "metronome": "tempo_rehearsal",
    "rehearsal": "tempo_rehearsal",
    "credit-words": "heading",
}


_KNOWN_CATEGORIES = (
    _BASIC_CATEGORIES | set(_CATEGORY_TAGS.values()) | {"clef", "cross_staff"}
)


@dataclass(frozen=True, slots=True)
class RenderGate:
    """Content-free disposition of one sample, with per-category differences."""

    status: str
    category_differences: Mapping[str, int] = field(default_factory=dict)
    reason: str | None = None


@dataclass(slots=True)
class RenderGateLedger:
    """Per-engine score counts and per-category round-trip drop rates."""

    engine: str
    min_cohort_before_disable: int = 1
    outcomes: Counter[str] = field(default_factory=Counter)
    category_attempts: Counter[str] = field(default_factory=Counter)
    category_drops: Counter[str] = field(default_factory=Counter)
    disabled_categories: set[str] = field(default_factory=set)

    def drop_rates(self) -> dict[str, float]:
        """Return the observed drop fraction for each checked category."""
        return {
            name: self.category_drops[name] / total
            for name, total in self.category_attempts.items()
        }

    def record(self, gate: RenderGate, categories: set[str]) -> None:
        """Record one decision and disable unsafe categories for Verovio."""
        self.outcomes[gate.status] += 1
        if gate.status == "excluded":
            return
        known_diffs = set(gate.category_differences) & _KNOWN_CATEGORIES
        self.category_attempts.update(
            (categories & _KNOWN_CATEGORIES) | known_diffs
        )
        self.category_drops.update(known_diffs)
        evaluated = self.outcomes["passed"] + self.outcomes["dropped"]
        if (
            self.engine == "verovio"
            and evaluated >= self.min_cohort_before_disable
        ):
            self.disabled_categories = {
                name
                for name, rate in self.drop_rates().items()
                if rate > _DROP_THRESHOLD
            }


@dataclass(frozen=True, slots=True)
class RenderResult:
    """Only samples passing every gate retain rasters and system labels."""

    engine: str
    gate: RenderGate
    pages: tuple[RenderedPage, ...] = ()
    system_texts: tuple[str, ...] = ()
    categories: frozenset[str] = frozenset()


def _result(  # noqa: PLR0913
    engine: str,
    ledger: RenderGateLedger,
    categories: set[str],
    gate: RenderGate,
    *,
    pages: tuple[RenderedPage, ...] = (),
    texts: tuple[str, ...] = (),
) -> RenderResult:
    ledger.record(gate, categories)
    return RenderResult(engine, gate, pages, texts, frozenset(categories))


def _require_ledger(ledger: RenderGateLedger, engine: str) -> None:
    if ledger.engine != engine:
        msg = "render gate ledger belongs to a different engine"
        raise ValueError(msg)


def _clef_sig(clef: ET.Element) -> tuple[str, str, str]:
    return (
        (clef.findtext("sign") or "").strip(),
        (clef.findtext("line") or "").strip(),
        (clef.findtext("clef-octave-change") or "0").strip(),
    )


def _has_midscore_clef(root: ET.Element) -> bool:
    initial: dict[str, tuple[str, str, str]] = {}
    for m_idx, measure in enumerate(_parts(root), 1):
        seen_motion = False
        for child in measure:
            if child.tag in {"note", "backup", "forward"}:
                seen_motion = True
            elif child.tag == "attributes":
                for clef in child.findall("clef"):
                    st_num = clef.get("number", "1")
                    sig = _clef_sig(clef)
                    if m_idx == 1 and not seen_motion and st_num not in initial:
                        initial[st_num] = sig
                    elif initial.get(st_num) != sig:
                        return True
    return False


def _categories(root: ET.Element, xml: str) -> set[str]:
    categories = _BASIC_CATEGORIES | {
        _CATEGORY_TAGS[node.tag]
        for node in root.iter()
        if node.tag in _CATEGORY_TAGS
    }
    if _has_midscore_clef(root):
        categories.add("clef")
    events = read_score(xml).events
    owners = voice_staves(events)
    if any(owners[id(event)][1] != event.staff for event in events):
        categories.add("cross_staff")
    if "text" in categories or root.find(".//sound[@tempo]") is not None:
        # Plain words such as "Allegro" may carry tempo rather than prose.
        categories.add("tempo_rehearsal")
    return categories


def _reject_unchecked_facets(root: ET.Element) -> set[str]:  # noqa: C901, PLR0912
    unchecked = {
        "wavy-line": "ornament",
        "dashes": "text",
        "bracket": "voice",
        "other-dynamics": "dynamic",
        "tremolo": "tremolo",
        "non-arpeggiate": "arpeggio",
        "ending": "repeat_ending",
    }
    categories = {
        unchecked[node.tag] for node in root.iter() if node.tag in unchecked
    }
    for tag in ("notehead", "accidental", "metronome"):
        if root.find(f".//{tag}[@parentheses='yes']") is not None:
            category = {
                "notehead": "pitch",
                "accidental": "accidental",
                "metronome": "tempo_rehearsal",
            }[tag]
            categories.add(category)
    if root.find(".//pedal[@type='discontinue']") is not None:
        categories.add("pedal")
    if root.findall(".//ending") and (
        root.findall(".//tie") or root.findall(".//tied")
    ):
        categories.add("tie")
    for measure in _parts(root):
        opened: set[tuple[str, str, str]] = set()
        for note in measure.findall("note"):
            for beam in note.findall("beam"):
                key = (
                    note.findtext("staff", "1"),
                    note.findtext("voice", "1"),
                    beam.get("number", "1"),
                )
                if note.find("rest") is not None or beam.text in {
                    "forward hook",
                    "backward hook",
                }:
                    categories.add("beam")
                if beam.text == "begin":
                    opened.add(key)
                elif beam.text == "end":
                    if key not in opened:
                        categories.add("beam")
                    opened.discard(key)
                elif beam.text == "continue" and key not in opened:
                    categories.add("beam")
        if opened:
            categories.add("beam")
    return categories


def _labeled_xml(xml: str) -> tuple[str, tuple[str, ...]]:
    root = _parse_xml(xml)
    ids = []
    for ordinal, measure in enumerate(_parts(root), 1):
        ident = f"omr-measure-{ordinal}"
        measure.set("id", ident)
        ids.append(ident)
        for index, note in enumerate(measure.findall("note"), 1):
            note.set("id", f"omr-event-{ordinal}-{index}")
    return ET.tostring(root, encoding="unicode"), tuple(ids)


def _system_texts(xml: str, layout: SystemLayout) -> tuple[str, ...]:
    canonical = parse_document(from_musicxml_text(xml))
    if (
        not isinstance(canonical, AssembledScore)
        or len(canonical.blocks) != layout.measures
    ):
        msg = "renderer measures do not map one-to-one to canonical blocks"
        raise RenderRejected(msg)
    docs = split_score(canonical, layout.starts)
    if not compare_assembly(canonical, docs).passed:
        msg = "system slice assembly did not round-trip"
        raise RenderRejected(msg)
    return tuple(map(write_document, docs))


def _from_png(png: bytes) -> Image.Image:
    with Image.open(io.BytesIO(png), formats=["PNG"]) as image:
        image.load()
        return image.copy()


def _batch_path(path: Path, binary: Path) -> str:
    if sys.platform != "win32" and binary.suffix.lower() == ".exe":
        return _windows_path_for_wsl(path)
    return str(path.resolve())


SOURCE_VERIFIED_MUSESCORE_BUILDS: frozenset[tuple[str, str]] = frozenset(
    {
        (
            "4.7.7.261109195",
            "89d9145261eb2bec6aedc208b5234b92c01fe6bc5485fec7e2ce28ec05819f31",
        ),
    }
)
MUSESCORE_CALIBRATION_CACHE_NAME = ".musescore-calibrated.json"


def _musescore_batch_jobs(
    ms: MuseScore,
    batch_dir: Path,
    items: Sequence[tuple[Path, str]],
    musescore_style: str,
) -> None:
    owned = ms.confinement.check(batch_dir)
    owned.mkdir(parents=True, exist_ok=True)
    mss = ms.confinement.write_text(owned / "style.mss", musescore_style)
    exe = ms.executable
    job = []
    for item_dir, xml in items:
        item_owned = ms.confinement.check(item_dir)
        item_owned.mkdir(parents=True, exist_ok=False)
        source = ms.confinement.write_text(item_owned / "input.musicxml", xml)
        job.append(
            {
                "in": _batch_path(source, exe),
                "out": [
                    _batch_path(item_owned / f"score.{ext}", exe)
                    for ext in ("png", "musicxml", "mpos", "spos")
                ],
            }
        )
    job_path = ms.confinement.write_text(owned / "job.json", json.dumps(job))
    cmd = [
        str(exe),
        "-j",
        _batch_path(job_path, exe),
        "-S",
        _batch_path(mss, exe),
        "-r",
        "300",
    ]
    with ms.confinement.lock(ms.confinement.downloads / LOCK_NAME):
        ms.check_settings()
        launched_ok = False
        try:
            code, _ = ms.launcher(cmd, STEP_SECONDS * max(1, len(items)))
            launched_ok = True
        finally:
            if launched_ok:
                ms.check_settings()
            else:
                with contextlib.suppress(OSError, StopConditionError):
                    ms.check_settings()
    if code is None:
        msg = "MuseScore batch job timed out"
        raise StopConditionError(msg)
    if code != 0:
        msg = f"MuseScore W4 job did not complete successfully ({code})"
        raise RenderRejected(msg)


def _musescore_job(
    ms: MuseScore,
    directory: Path,
    xml: str,
    style: LayoutStyle,
) -> None:
    _musescore_batch_jobs(
        ms, directory, ((directory / "item-0", xml),), style.musescore_style
    )
    item_dir = directory / "item-0"
    for child in item_dir.iterdir():
        child.replace(directory / child.name)
    item_dir.rmdir()


def _numbered_pngs(directory: Path) -> tuple[bytes, ...]:
    files = sorted(
        (int(match[1]), file)
        for file in directory.glob("score-*.png")
        if (match := re.fullmatch(r"score-(\d+)\.png", file.name))
    )
    if not files or [n for n, _ in files] != list(range(1, len(files) + 1)):
        msg = "missing or non-consecutive renderer page output"
        raise RenderRejected(msg)
    return tuple(file.read_bytes() for _, file in files)


_SAMPLE_ERRORS = (
    ValueError,
    KeyError,
    ET.ParseError,
    OSError,
    MusicXmlError,
    SplitError,
    ScoreParseError,
)


_CALIBRATION_XML = """<?xml version="1.0" encoding="UTF-8"?>
<score-partwise version="4.0">
  <part-list><score-part id="P1">
    <part-name>Piano</part-name></score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes><divisions>1</divisions><key><fifths>0</fifths></key>
        <time><beats>4</beats><beat-type>4</beat-type></time><staves>2</staves>
        <clef number="1"><sign>G</sign><line>2</line></clef>
        <clef number="2"><sign>F</sign><line>4</line></clef></attributes>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration>
        <voice>1</voice><type>whole</type><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration>
        <voice>5</voice><type>whole</type><staff>2</staff></note>
    </measure>
    <measure number="2">
      <note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration>
        <voice>1</voice><type>whole</type><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>G</step><octave>2</octave></pitch><duration>4</duration>
        <voice>5</voice><type>whole</type><staff>2</staff></note>
    </measure>
  </part>
</score-partwise>
"""


def calibrate_musescore_pixel_scale(
    musescore: MuseScore,
    output_dir: Path,
    *,
    count_rasters: Callable[[int], None] | None = None,
) -> tuple[str, str]:
    """Verify .mpos/12.0 pixel alignment within <=2.0 px on synthetic score."""
    style = sample_layout_and_style(1, 2)
    try:
        _musescore_job(musescore, output_dir, _CALIBRATION_XML, style)
    finally:
        if count_rasters is not None:
            count_rasters(_rendered_raster_count(output_dir))
    pngs = _numbered_pngs(output_dir)
    if len(pngs) != 1:
        msg = "MuseScore pixel calibration expected one rendered page"
        raise StopConditionError(msg)
    mpos_text = (output_dir / "score.mpos").read_text(encoding="utf-8")
    grouped = _mpos_rows(mpos_text, 2)
    page1_groups = [g for (p, _), g in sorted(grouped.items()) if p == 1]
    if len(page1_groups) != 1 or len(page1_groups[0]) != 2:  # noqa: PLR2004
        msg = "MuseScore pixel calibration sidecar missing 2-measure system"
        raise StopConditionError(msg)
    mx0, my0, mx1, my1 = _box_union([m.box for m in page1_groups[0]])
    gray = np.asarray(_gray(_from_png(pngs[0])))
    rx0 = max(0, round(mx0))
    rx1 = min(gray.shape[1], round(mx1))
    if rx1 <= rx0:
        msg = "MuseScore pixel calibration produced degenerate horizontal span"
        raise StopConditionError(msg)
    row_cov = (gray[:, rx0:rx1] < _RASTER_INK_THRESHOLD).mean(axis=1)
    staff_ys = np.flatnonzero(row_cov > 0.60)  # noqa: PLR2004
    if not len(staff_ys):
        msg = "MuseScore pixel calibration found no rendered staff lines"
        raise StopConditionError(msg)
    sy_min, sy_max = float(staff_ys.min()), float(staff_ys.max())
    ry0 = max(0, round(sy_min))
    ry1 = min(gray.shape[0], round(sy_max) + 1)
    col_cov = (gray[ry0:ry1, :] < _RASTER_INK_THRESHOLD).mean(axis=0)
    bar_xs = np.flatnonzero(col_cov > 0.85)  # noqa: PLR2004
    if not len(bar_xs):
        msg = "MuseScore pixel calibration found no full-height barlines"
        raise StopConditionError(msg)
    bx_min, bx_max = float(bar_xs.min()), float(bar_xs.max())
    max_err = max(
        abs(bx_min - mx0),
        abs(bx_max - mx1),
        abs(sy_min - my0),
        abs(sy_max - my1),
    )
    if max_err > 2.0:  # noqa: PLR2004
        msg = (
            "MuseScore .mpos/12.0 pixel calibration exceeded <=2.0 px "
            f"four-side tolerance (max_err={max_err:.2f}px)"
        )
        raise StopConditionError(msg)
    return (musescore.version, musescore.executable_sha256)


def ensure_musescore_calibrated(
    musescore: MuseScore,
    scratch_dir: Path,
    *,
    cache_path: Path | None = None,
    count_rasters: Callable[[int], None] | None = None,
) -> tuple[str, str]:
    """Skip pixel calibration for source-verified or cached 4.x builds."""
    identity = (musescore.version, musescore.executable_sha256)
    if identity in SOURCE_VERIFIED_MUSESCORE_BUILDS:
        return identity
    cached: list[list[str]] = []
    if cache_path is not None and cache_path.is_file():
        with contextlib.suppress(OSError, ValueError, TypeError):
            raw = json.loads(cache_path.read_text(encoding="utf-8"))
            if isinstance(raw, list):
                cached = [
                    [str(row[0]), str(row[1])]
                    for row in raw
                    if isinstance(row, list) and len(row) == 2  # noqa: PLR2004
                ]
        if [identity[0], identity[1]] in cached:
            return identity
    verified = calibrate_musescore_pixel_scale(
        musescore, scratch_dir, count_rasters=count_rasters
    )
    if cache_path is not None:
        entry = [verified[0], verified[1]]
        if entry not in cached:
            cached.append(entry)
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        tmp = cache_path.with_name(f".{cache_path.name}.{uuid4().hex}")
        tmp.write_text(json.dumps(cached, indent=2) + "\n", encoding="utf-8")
        tmp.replace(cache_path)
    return verified


def _safe_count_rasters(
    output_dir: Path,
    count_rasters: Callable[[int], None] | None,
    *,
    suppress_errors: bool,
) -> int:
    if not suppress_errors:
        observed = _rendered_raster_count(output_dir)
        if count_rasters is not None:
            count_rasters(observed)
        return observed
    observed = 0
    with contextlib.suppress(OSError, StopConditionError):
        observed = _rendered_raster_count(output_dir)
        if count_rasters is not None:
            count_rasters(observed)
    return observed


def render_musescore(  # noqa: PLR0913
    x_inj: str,
    style: LayoutStyle,
    musescore: MuseScore,
    output_dir: Path,
    ledger: RenderGateLedger,
    *,
    rights_variant: str,
    calibrated_identity: tuple[str, str] | None = None,
    max_pages: int = 5_000,
    count_rasters: Callable[[int], None] | None = None,
) -> RenderResult:
    """Render a public sample through the settings-guarded host job driver."""
    engine = "musescore"
    _require_ledger(ledger, engine)
    if rights_variant not in {"permissive", "nc_sa"}:
        return _result(
            engine,
            ledger,
            set(),
            RenderGate("excluded", reason="host_public_only"),
        )
    identity = (musescore.version, musescore.executable_sha256)
    if (
        identity not in SOURCE_VERIFIED_MUSESCORE_BUILDS
        and not has_source_verified_defaults(musescore.version)
        and calibrated_identity != identity
    ):
        msg = "MuseScore version requires an identity-bound pixel calibration"
        raise StopConditionError(msg)
    categories: set[str] = set()
    try:
        encoded = encode_layout_breaks(x_inj, style.layout)
        categories = _categories(_parse_xml(encoded), encoded)
        annotate(encoded)
        stopped = False
        observed = 0
        try:
            _musescore_job(musescore, output_dir, encoded, style)
        except StopConditionError:
            stopped = True
            raise
        finally:
            observed = _safe_count_rasters(
                output_dir, count_rasters, suppress_errors=stopped
            )
        if observed > max_pages:
            msg = "MuseScore exceeded its per-job page ceiling"
            raise StopConditionError(msg)
        round_trip = (output_dir / "score.musicxml").read_text(encoding="utf-8")
        if not (output_dir / "score.spos").is_file():
            msg = "MuseScore did not write the segment-position sidecar"
            raise RenderRejected(msg)
        pngs = _numbered_pngs(output_dir)
        images = [_from_png(png) for png in pngs]
        geometry = parse_musescore_geometry(
            (output_dir / "score.mpos").read_text(encoding="utf-8"),
            round_trip,
            images,
        )
        if system_starts(round_trip).measures != style.layout.measures:
            msg = "MuseScore re-export changed the canonical measure count"
            raise RenderRejected(msg)
        try:
            annotate(round_trip)
        except ProtocolDefectError as exc:
            msg = "MuseScore re-export produced invalid spanner endpoints"
            raise RenderRejected(msg) from exc
        differences = compare_file(round_trip, encoded).category_counts()
        if differences:
            return _result(
                engine,
                ledger,
                categories,
                RenderGate("dropped", differences, "musicxml_roundtrip"),
            )
        texts = _system_texts(round_trip, system_starts(round_trip))
    except _SAMPLE_ERRORS:
        return _result(
            engine,
            ledger,
            categories,
            RenderGate("dropped", reason="render_structure_or_conversion"),
        )
    pages = tuple(
        RenderedPage(png, g) for png, g in zip(pngs, geometry, strict=True)
    )
    return _result(
        engine,
        ledger,
        categories,
        RenderGate("passed"),
        pages=pages,
        texts=texts,
    )


def _normalized_mei(mei: str) -> ET.Element:
    root = _parse_xml(mei)
    for node in root.iter():
        node.tag = _local(node)
    if root.tag != "mei":
        msg = "secondary renderer did not export MEI"
        raise RenderRejected(msg)
    return root


type _FacetCounts = dict[str, Counter[tuple[object, ...]]]
type _NotePositions = dict[str, tuple[int, int, float]]


def _mei_id(node: ET.Element) -> str:
    return node.get(_XML_ID, node.get("id", ""))


def _fragment(raw: str) -> str:
    return raw.removeprefix("#")


def _beat_units(x_root: ET.Element) -> dict[tuple[int, int], int]:
    unit = {1: 4, 2: 4}
    result = {}
    for m, measure in enumerate(_parts(x_root), 1):
        for meter in measure.findall("attributes/time"):
            denom = int(meter.findtext("beat-type", "4"))
            if "number" in meter.attrib:
                unit[int(meter.attrib["number"])] = denom
            else:
                unit = {1: denom, 2: denom}
        result.update({(m, staff): denom for staff, denom in unit.items()})
    return result


def _stamp(onset: Fraction, beat_unit: int) -> float:
    return round(float(1 + onset * beat_unit / 4), 4)


def _value(node: ET.Element) -> str:
    return " ".join("".join(node.itertext()).split())


def _add_note_refs(  # noqa: C901, PLR0912
    score: str, facets: _FacetCounts, units: Mapping[tuple[int, int], int]
) -> _NotePositions:
    xml_score = read_score(score)
    owners = voice_staves(xml_score.events)
    note_positions: _NotePositions = {}
    layers: dict[tuple[int, int], dict[object, list[str]]] = defaultdict(dict)
    chords: dict[tuple[int, Fraction, str, str], list[str]] = defaultdict(list)
    tied: dict[tuple[object, ...], str] = {}
    for event in xml_score.events:
        measure = event.measure + 1
        staff = event.staff + 1
        voice, layer_staff = owners[id(event)]
        layer = layers[measure, layer_staff + 1].setdefault(voice, [])
        onset = _stamp(event.onset, units[measure, staff])
        for note in event.notes:
            ident = note.attrib["id"]
            layer.append(ident)
            note_positions[ident] = (measure, staff, onset)
            if staff != layer_staff + 1:
                facets["cross_staff"][
                    ident, measure, layer_staff + 1, staff
                ] += 1
            if (tm := note.find("time-modification")) is not None:
                facets["tuplet"][
                    measure,
                    staff,
                    ident,
                    int(tm.findtext("actual-notes", "0")),
                    int(tm.findtext("normal-notes", "0")),
                ] += 1
            for mark in note.findall("notations/technical/fingering"):
                facets["fingering"][measure, staff, ident, _value(mark)] += 1
            for mark in note.findall("notations/arpeggiate"):
                key = (
                    measure,
                    event.onset,
                    mark.get("number", "1"),
                    mark.get("direction", ""),
                )
                chords[key].append(ident)
            types = {
                mark.get("type") for mark in note.findall("notations/tied")
            }
            if not types:
                types = {mark.get("type") for mark in note.findall("tie")}
            tie_key = (staff, voice, event.written_pitch(note))
            fallback_key = (staff, event.written_pitch(note))
            if "stop" in types or "continue" in types:
                prev_id = tied.pop(tie_key, "") or tied.pop(fallback_key, "")
                facets["tie"][prev_id, ident] += 1
            if "start" in types or "continue" in types:
                tied[tie_key] = ident
                tied[fallback_key] = ident
    chord_rep: dict[str, str] = {}
    for event in xml_score.events:
        first_id = event.notes[0].attrib["id"]
        for note in event.notes:
            chord_rep[note.attrib["id"]] = first_id
    for (measure, staff), voices in layers.items():
        # Numeric identifiers need not survive; relative layer order does.
        facets["voice"][
            measure, staff, tuple(tuple(v) for v in voices.values())
        ] += 1
    for (measure, _, _, direction), members in chords.items():
        facets["arpeggio"][measure, tuple(sorted(members)), direction] += 1
    for span in xml_score.spans:
        if (
            span.family == "slur"
            and span.start_note is not None
            and span.stop_note is not None
        ):
            start = chord_rep.get(
                span.start_note.attrib["id"], span.start_note.attrib["id"]
            )
            end = chord_rep.get(
                span.stop_note.attrib["id"], span.stop_note.attrib["id"]
            )
            m, staff, _ = note_positions[start]
            facets["slur"][m, staff, start, end] += 1
    for ottava in xml_score.ottavas:
        notes = [
            event.notes[0].attrib["id"]
            for event in xml_score.events
            if event.place == ottava.place
            and ottava.start <= event.start < ottava.end
            and not event.rest
        ]
        if not notes:
            msg = "MusicXML octave span has no verifiable note endpoints"
            raise RenderRejected(msg)
        facets["ottava"][
            ottava.direction.measure + 1,
            ottava.place[1] + 1,
            notes[0],
            notes[-1],
            abs(ottava.shift) * 7 + 1,
            "above" if ottava.shift > 0 else "below",
        ] += 1
    return note_positions


_XML_UNIT_TO_MEI = {
    "whole": "1",
    "half": "2",
    "quarter": "4",
    "eighth": "8",
    "16th": "16",
    "32nd": "32",
}
_SMUFL_RE = re.compile(r"[\ue000-\uf8ff].*$")


def _expected_directions(  # noqa: C901, PLR0912
    xml: str, units: Mapping[tuple[int, int], int], facets: _FacetCounts
) -> None:
    wedges: dict[tuple[int, str], tuple[int, float, str]] = {}
    for direction in read_score(xml).directions:
        m, staff = direction.measure + 1, direction.staff + 1
        at = _stamp(direction.onset, units[m, staff])
        dtypes = direction.element.findall("direction-type/*")
        words_el = next((c for c in dtypes if c.tag == "words"), None)
        metro_el = next((c for c in dtypes if c.tag == "metronome"), None)
        if metro_el is not None:
            w_txt = _value(words_el) if words_el is not None else ""
            b_unit = _XML_UNIT_TO_MEI.get(
                metro_el.findtext("beat-unit", ""), ""
            )
            dots = len(metro_el.findall("beat-unit-dot"))
            pm = (metro_el.findtext("per-minute") or "").strip()
            facets["tempo_rehearsal"][
                m, staff, at, "tempo", w_txt, b_unit, dots, pm
            ] += 1
        elif words_el is not None:
            w_txt = _value(words_el)
            if w_txt:
                facets["text"][m, staff, at, w_txt] += 1
        for mark in dtypes:
            if mark.tag == "rehearsal":
                facets["tempo_rehearsal"][
                    m, staff, at, "reh", _value(mark)
                ] += 1
            if mark.tag == "dynamics":
                for child in mark:
                    facets["dynamic"][m, staff, at, child.tag] += 1
            if mark.tag == "pedal":
                kind = mark.get("type", "")
                if kind != "continue":
                    facets["pedal"][
                        m,
                        staff,
                        at,
                        _PEDAL_DIRECTIONS.get(kind, kind),
                        "line" if mark.get("line") == "yes" else "sign",
                    ] += 1
            if mark.tag != "wedge":
                continue
            key = staff, mark.get("number", "1")
            kind = mark.get("type", "")
            if kind in {"crescendo", "diminuendo"}:
                wedges[key] = m, at, "cres" if kind == "crescendo" else "dim"
            elif kind == "stop" and key in wedges:
                start_m, start, form = wedges.pop(key)
                facets["hairpin"][start_m, staff, start, m, at, form] += 1
            else:
                facets["hairpin"][m, staff, at, kind] += 1
    for (staff, _), (measure, timestamp, form) in wedges.items():
        facets["hairpin"][measure, staff, timestamp, form] += 1


_PEDAL_DIRECTIONS = {
    "start": "down",
    "stop": "up",
    "change": "bounce",
    "sostenuto": "down",
}
_LAYER_EVENTS = {"note", "rest", "mRest", "space", "mSpace"}


def _mei_staff(
    mark: ET.Element, positions: _NotePositions, start: str = ""
) -> int:
    if staff := mark.get("staff"):
        return int(staff)
    return positions.get(start, (0, 0, 0.0))[1]


def _mei_onset(
    mark: ET.Element, positions: _NotePositions, start: str
) -> float:
    raw = mark.get("tstamp")
    return round(float(raw), 4) if raw is not None else positions[start][2]


def _mei_span_end(
    mark: ET.Element, measure: int, positions: _NotePositions, end: str
) -> tuple[int, float]:
    if raw := mark.get("tstamp2"):
        displacement, sep, timestamp = raw.partition("m+")
        if sep:
            return measure + int(displacement), round(float(timestamp), 4)
        return measure, round(float(displacement), 4)
    m, _, onset = positions[end]
    return m, onset


def _mei_tempo_facet(
    mark: ET.Element,
    ordinal: int,
    staff: int,
    at_t: float,
    facets: _FacetCounts,
) -> None:
    mm = mark.get("mm", "").strip()
    mm_unit = mark.get("mm.unit", "").strip()
    mm_dots = int(mark.get("mm.dots", "0"))
    raw_t = _SMUFL_RE.sub("", "".join(mark.itertext())).strip()
    clean_t = " ".join(raw_t.split())
    if mm:
        facets["tempo_rehearsal"][
            ordinal, staff, at_t, "tempo", clean_t, mm_unit, mm_dots, mm
        ] += 1
    elif clean_t:
        facets["text"][ordinal, staff, at_t, clean_t] += 1


def _mei_controls(  # noqa: C901, PLR0912
    measure: ET.Element,
    ordinal: int,
    default_pedal: str,
    lookup: tuple[
        _NotePositions, Mapping[str, tuple[str, ...]], Mapping[str, str]
    ],
    facets: _FacetCounts,
) -> None:
    positions, chord_map, chord_rep = lookup
    for mark in measure:
        tag = mark.tag
        raw_start = _fragment(mark.get("startid", ""))
        raw_end = _fragment(mark.get("endid", ""))
        if tag in {_MEI_DIGIT_TAG, "tie"}:
            start = (
                chord_map[raw_start][0] if raw_start in chord_map else raw_start
            )
            end = chord_map[raw_end][0] if raw_end in chord_map else raw_end
        else:
            start = chord_rep.get(raw_start, raw_start)
            end = chord_rep.get(raw_end, raw_end)
        staff = (
            _mei_staff(mark, positions, start)
            if tag
            in {
                _MEI_DIGIT_TAG,
                "pedal",
                "octave",
                "slur",
                "hairpin",
                "dynam",
                "dir",
                "tempo",
                "reh",
            }
            else 0
        )
        if tag == _MEI_DIGIT_TAG:
            facets["fingering"][ordinal, staff, start, _value(mark)] += 1
        elif tag == "dir":
            d_txt = _value(mark)
            if d_txt:
                facets["text"][
                    ordinal, staff, _mei_onset(mark, positions, start), d_txt
                ] += 1
        elif tag == "reh":
            facets["tempo_rehearsal"][
                ordinal,
                staff,
                _mei_onset(mark, positions, start),
                "reh",
                _value(mark),
            ] += 1
        elif tag == "tempo":
            _mei_tempo_facet(
                mark,
                ordinal,
                staff,
                _mei_onset(mark, positions, start),
                facets,
            )
        elif tag == "pedal":
            kind = mark.get("dir", "")
            form = mark.get("form", default_pedal)
            if form not in {"line", "pedstar", "altpedstar"}:
                msg = "unrecognized effective MEI pedal form"
                raise RenderRejected(msg)
            onset = _mei_onset(mark, positions, start)
            # The pinned importer shifts stop timestamps 0.1 beat earlier.
            if kind == "up" and not start:
                onset = round(onset + 0.1, 4)
            facets["pedal"][
                ordinal,
                staff,
                onset,
                kind,
                "line" if form == "line" else "sign",
            ] += 1
        elif tag == "octave":
            facets["ottava"][
                ordinal,
                staff,
                start,
                end,
                int(mark.get("dis", "8")),
                mark.get("dis.place", ""),
            ] += 1
        elif tag == "arpeg":
            expanded: list[str] = []
            for tok in mark.get("plist", "").split():
                frag = _fragment(tok)
                expanded.extend(chord_map.get(frag, (frag,)))
            members = tuple(sorted(expanded))
            order = mark.get("order", "")
            facets["arpeggio"][
                ordinal, members, order if order != "nonarp" else "unsupported"
            ] += 1
        elif tag == "slur":
            facets["slur"][ordinal, staff, start, end] += 1
        elif tag == "hairpin":
            onset = _mei_onset(mark, positions, start)
            end_measure, end_stamp = _mei_span_end(
                mark, ordinal, positions, end
            )
            facets["hairpin"][
                ordinal,
                staff,
                onset,
                end_measure,
                end_stamp,
                mark.get("form", ""),
            ] += 1
        elif tag == "dynam":
            facets["dynamic"][
                ordinal, staff, _mei_onset(mark, positions, start), _value(mark)
            ] += 1
        elif tag == "tie":
            facets["tie"][start, end] += 1


def _mei_walk_tuplet_tree(
    node: ET.Element,
    positions: _NotePositions,
    facets: _FacetCounts,
    ratio: tuple[int, int] = (1, 1),
) -> None:
    cur_a, cur_b = ratio
    if node.tag == "tuplet":
        cur_a *= int(node.get("num", "1"))
        cur_b *= int(node.get("numbase", "1"))
    elif (cur_a, cur_b) != (1, 1) and node.tag in {"note", "rest"}:
        ident = _mei_id(node)
        if ident in positions:
            m_ord, ev_staff, _ = positions[ident]
            facets["tuplet"][m_ord, ev_staff, ident, cur_a, cur_b] += 1
    for child in node:
        _mei_walk_tuplet_tree(child, positions, facets, (cur_a, cur_b))


def _mei_layers(
    measure: ET.Element,
    ordinal: int,
    positions: _NotePositions,
    facets: _FacetCounts,
) -> None:
    for staff in measure.findall("staff"):
        staff_no = int(staff.attrib["n"])
        voices = []
        for layer in staff.findall("layer"):
            _mei_walk_tuplet_tree(layer, positions, facets)
            members = []
            for note in layer.iter():
                if note.tag not in _LAYER_EVENTS:
                    continue
                ident = _mei_id(note)
                if ident not in positions and note.tag in {"space", "mSpace"}:
                    continue
                members.append(ident)
                actual_staff = int(note.get("staff", str(staff_no)))
                if staff_no != actual_staff:
                    facets["cross_staff"][
                        ident, ordinal, staff_no, actual_staff
                    ] += 1
            if members:
                voices.append(tuple(members))
        if voices:
            facets["voice"][ordinal, staff_no, tuple(voices)] += 1


_COUNTED_CREDIT_TYPES = frozenset(
    {"title", "subtitle", "composer", "arranger", "lyricist"}
)


def _credit_lines(x_root: ET.Element) -> list[str]:
    out: list[str] = []
    for credit in x_root.findall("credit"):
        ctype = (credit.findtext("credit-type") or "").strip()
        if ctype not in _COUNTED_CREDIT_TYPES:
            continue
        joined = "".join(cw.text or "" for cw in credit.findall("credit-words"))
        for ln in joined.splitlines():
            norm = " ".join(ln.split())
            if norm:
                out.append(norm)
    return out


def _mei_heading_lines(mei: ET.Element) -> list[str]:
    out: list[str] = []
    for pg_head in mei.iter("pgHead"):
        for rend in pg_head.findall("rend"):
            for ln in "".join(rend.itertext()).splitlines():
                norm = " ".join(ln.split())
                if norm:
                    out.append(norm)
    return out


def _mei_chord_maps(
    mei: ET.Element, note_positions: _NotePositions
) -> tuple[dict[str, tuple[str, ...]], dict[str, str]]:
    chord_map: dict[str, tuple[str, ...]] = {}
    chord_rep: dict[str, str] = {}
    for ch in mei.iter("chord"):
        cid = _mei_id(ch)
        children = tuple(
            _mei_id(n)
            for n in ch.findall("note")
            if _mei_id(n) in note_positions
        )
        for nid in children:
            chord_rep[nid] = children[0]
        if cid and children:
            chord_map[cid] = children
            chord_rep[cid] = children[0]
            note_positions[cid] = note_positions[children[0]]
    return chord_map, chord_rep


def compare_mei_supplementary(x_inj: str, mei_text: str) -> Counter[str]:
    """Compare MEI facets using source note IDs and measure-local sites."""
    expected: _FacetCounts = defaultdict(Counter)
    actual: _FacetCounts = defaultdict(Counter)
    x_root = _parse_xml(x_inj)
    units = _beat_units(x_root)
    note_positions = _add_note_refs(x_inj, expected, units)
    _expected_directions(x_inj, units, expected)
    mei = _normalized_mei(mei_text)
    for ln in _credit_lines(x_root):
        expected["heading"][(ln,)] += 1
    for ln in _mei_heading_lines(mei):
        actual["heading"][(ln,)] += 1
    canonical = [measure.attrib["id"] for measure in _parts(x_root)]
    if [_mei_id(m) for m in mei.iter("measure")] != canonical:
        msg = "MEI re-export does not retain one-to-one canonical measure IDs"
        raise RenderRejected(msg)
    chord_map, chord_rep = _mei_chord_maps(mei, note_positions)
    pedal_style = "pedstar"
    ordinal = 0
    for node in mei.iter():
        if node.tag == "scoreDef":
            pedal_style = node.get("pedal.style", pedal_style)
        if node.tag == "measure":
            ordinal += 1
            _mei_layers(node, ordinal, note_positions, actual)
            _mei_controls(
                node,
                ordinal,
                pedal_style,
                (note_positions, chord_map, chord_rep),
                actual,
            )
    return Counter(
        {
            name: sum((expected[name] - actual[name]).values())
            + sum((actual[name] - expected[name]).values())
            for name in expected.keys() | actual.keys()
            if expected[name] != actual[name]
        }
    )


_DIFF_FAMILIES = {
    "pitch": "pitch",
    "accid": "accidental",
    "voice": "voice",
    "clef": "clef",
    "key": "key_signature",
    "time": "time_signature",
    "duration": "rhythm_rest",
    "head": "rhythm_rest",
    "dot": "rhythm_rest",
    "rest": "rhythm_rest",
    "beam": "beam",
    "tuplet": "tuplet",
    "tie": "tie",
    "slur": "slur",
    "dynam": "dynamic",
    "grace": "grace",
    "articulation": "articulation_fermata",
    "ornament": "ornament",
    "tremolo": "tremolo",
}


def _strip_mei_owned_from_score(score: Score) -> None:
    """Remove facets verified 1-to-1 by compare_mei_supplementary."""
    from music21 import articulations, expressions, tempo  # noqa: PLC0415

    strip_stream_classes = (
        expressions.TextExpression,
        expressions.RehearsalMark,
        tempo.MetronomeMark,
    )
    for st in score.recurse(streamsOnly=True, includeSelf=True):
        for el in list(st):
            if isinstance(el, strip_stream_classes) or type(el).__name__ in {
                "PedalMark",
                "ArpeggioMarkSpanner",
            }:
                st.remove(el)
    for gn in score.recurse().notesAndRests:
        if gn.articulations:
            gn.articulations = [
                a
                for a in gn.articulations
                if not isinstance(a, articulations.Fingering)
            ]
        if gn.expressions:
            gn.expressions = [
                e
                for e in gn.expressions
                if not isinstance(e, expressions.ArpeggioMark)
            ]


def _parse_score(text: str, fmt: str) -> Score:
    score = converter.parseData(text, format=fmt)
    if not isinstance(score, Score):
        msg = "music21 did not produce the required score"
        raise RenderRejected(msg)
    return score


_CONVERTER21_REGISTERED = False


def _ensure_converter21() -> None:
    global _CONVERTER21_REGISTERED  # noqa: PLW0603
    if not _CONVERTER21_REGISTERED:
        converter21.register()
        for logger_name in ("converter21", "converter21.mei.meireader"):
            lg = logging.getLogger(logger_name)
            lg.setLevel(logging.ERROR)
            lg.addFilter(lambda rec: rec.levelno >= logging.ERROR)
        _CONVERTER21_REGISTERED = True


def _is_mei_owned_note_op(op: object) -> bool:
    op_name = getattr(op, "name", "")
    obj1 = getattr(op, "obj1", None)
    obj2 = getattr(op, "obj2", None)
    if op_name in {"delarticulation", "insarticulation"}:
        a1 = list(getattr(obj1, "articulations", None) or ())
        a2 = list(getattr(obj2, "articulations", None) or ())
        f_name = _MEI_DIGIT_TAG + "ering"
        return [x for x in a1 if x != f_name] == [x for x in a2 if x != f_name]
    if op_name in {"delexpression", "insexpression"}:
        e1 = list(getattr(obj1, "expressions", None) or ())
        e2 = list(getattr(obj2, "expressions", None) or ())
        return [x for x in e1 if x != "arpeggio mark"] == [
            x for x in e2 if x != "arpeggio mark"
        ]
    return False


_MEI_OWNED_EXTRA_KINDS = frozenset(
    {"pedalmark", "arpeggio", "direction", "tempo", "rehearsal"}
)


def _musicdiff_mei(xml: str, mei: str) -> Counter[str]:
    if version("musicdiff") != MUSICDIFF_VERSION:
        msg = "Verovio gate requires pinned musicdiff 6.0"
        raise InstrumentMismatchError(msg)
    _ensure_converter21()
    mei_compat = mei.replace('meiversion="6.0-dev"', 'meiversion="5.1"', 1)
    try:
        reference = _parse_score(xml, "musicxml")
        rendered = _parse_score(mei_compat, "mei")
        _strip_mei_owned_from_score(reference)
        _strip_mei_owned_from_score(rendered)
        ops, _ = Comparison.annotated_scores_diff(
            AnnScore(reference, CONTENT_DETAIL),
            AnnScore(rendered, CONTENT_DETAIL),
        )
    except Exception as exc:
        msg = "MusicXML/MEI musicdiff comparison failed"
        raise RenderRejected(msg) from exc
    result: Counter[str] = Counter()
    for op in ops:
        if op.name.lower().startswith("staffgrp") or _is_mei_owned_note_op(op):
            continue
        extra_kinds = {
            obj.kind for obj in (op.obj1, op.obj2) if isinstance(obj, AnnExtra)
        }
        if extra_kinds and extra_kinds <= _MEI_OWNED_EXTRA_KINDS:
            # Checked 1-to-1 by compare_mei_supplementary on R_vrv.mei.
            continue
        categories = {_EXTRA_KINDS.get(k, "musicdiff") for k in extra_kinds}
        if not categories:
            tag = op.name.lower()
            categories.add(
                next(
                    (
                        name
                        for token, name in _DIFF_FAMILIES.items()
                        if token in tag
                    ),
                    "musicdiff",
                )
            )
        result.update(categories)
    return result


_VEROVIO_WORKER = """
import sys
from pathlib import Path
import json
from importlib.metadata import version
import resvg
import verovio

assert version("verovio") == sys.argv[4]
assert version("resvg") == sys.argv[5]
max_scratch_bytes = int(sys.argv[6]) if len(sys.argv) > 6 and sys.argv[6] else 0
written_bytes = 0


def _write_checked(path: Path, payload: bytes) -> None:
    global written_bytes
    written_bytes += len(payload)
    if max_scratch_bytes > 0 and written_bytes > max_scratch_bytes:
        sys.exit(93)
    path.write_bytes(payload)


raw_opts = json.loads(sys.argv[1])
xml_data = Path(sys.argv[2]).read_text(encoding="utf-8")
tool = verovio.toolkit()
box_opts = dict(raw_opts, svgBoundingBoxes=True, svgContentBoundingBoxes=True)
assert tool.setOptions(box_opts)
assert tool.loadData(xml_data)
count = tool.getPageCount()
assert 0 < count <= int(sys.argv[3])
for page in range(1, count + 1):
    _write_checked(
        Path(f"score-{page}.svg"), tool.renderToSVG(page).encode("utf-8")
    )
_write_checked(Path("score.mei"), tool.getMEI().encode("utf-8"))

clean_tool = verovio.toolkit()
clean_opts = dict(
    raw_opts, svgBoundingBoxes=False, svgContentBoundingBoxes=False
)
assert clean_tool.setOptions(clean_opts)
assert clean_tool.loadData(xml_data)
assert clean_tool.getPageCount() == count
opts = resvg.usvg.Options.default()
opts.load_system_fonts()
for page in range(1, count + 1):
    clean_svg = clean_tool.renderToSVG(page)
    tree = resvg.usvg.Tree.from_str(clean_svg, opts)
    png = resvg.render(
        tree,
        (1.0, 0.0, 0.0, 0.0, 1.0, 0.0),
        bg_color=(255, 255, 255, 255),
    )
    _write_checked(Path(f"score-{page}.png"), bytes(png))
"""


def _rendered_raster_count(directory: Path) -> int:
    return sum(
        re.fullmatch(r"score(?:-\d+)?\.png", path.name) is not None
        for path in directory.glob("score*.png")
        if path.is_file()
    )


def _run_verovio(  # noqa: PLR0913
    xml: str,
    style: LayoutStyle,
    downloads: Path,
    scratch_root: Path,
    *,
    max_pages: int,
    count_rasters: Callable[[int], None] | None,
) -> tuple[str, tuple[tuple[str, bytes], ...]]:
    scratch_root = scratch_root.resolve()
    scratch_root.mkdir(parents=True, exist_ok=True)
    python = downloads.resolve() / "w4-synth-venv" / "bin" / "python"
    rendered: list[tuple[str, bytes]] = []
    mei = ""
    with tempfile.TemporaryDirectory(prefix="vrv-", dir=scratch_root) as raw:
        work = Path(raw)
        staged = work / "input.musicxml"
        staged.write_text(xml, encoding="utf-8")
        env = {
            "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
            "HOME": str(work),
            "TMPDIR": str(work),
            "LANG": "C.UTF-8",
            "OMP_NUM_THREADS": "1",
            "HF_HUB_OFFLINE": "1",
            "TRANSFORMERS_OFFLINE": "1",
            "PIP_NO_INDEX": "1",
            "UV_OFFLINE": "1",
        }
        proc = subprocess.Popen(  # noqa: S603
            [
                str(python),
                "-c",
                _VEROVIO_WORKER,
                json.dumps(dict(style.verovio_options)),
                str(staged),
                str(max_pages),
                VEROVIO_VERSION,
                RESVG_VERSION,
                "0",
            ],
            cwd=work,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            close_fds=True,
            start_new_session=True,
        )
        try:
            proc.communicate(timeout=WORKER_TIMEOUT_SECONDS)
        except subprocess.TimeoutExpired as exc:
            with contextlib.suppress(ProcessLookupError):
                os.killpg(proc.pid, signal.SIGKILL)
            proc.wait(timeout=5.0)
            if count_rasters is not None:
                count_rasters(_rendered_raster_count(work))
            msg = "Verovio W4 job timed out"
            raise StopConditionError(msg) from exc
        rasters = _rendered_raster_count(work)
        if count_rasters is not None:
            count_rasters(rasters)
        reexport = work / "score.mei"
        if proc.returncode == 0 and reexport.is_file() and rasters > 0:
            with contextlib.suppress(RenderRejected, OSError):
                pngs = _numbered_pngs(work)
                mei = reexport.read_text(encoding="utf-8")
                rendered.extend(
                    (
                        (work / f"score-{page}.svg").read_text(
                            encoding="utf-8"
                        ),
                        png,
                    )
                    for page, png in enumerate(pngs, 1)
                )
        returncode = proc.returncode
    if returncode or not mei or not rendered:
        msg = f"Verovio W4 job failed or was incomplete ({returncode})"
        raise RenderRejected(msg)
    return mei, tuple(rendered)


def _verovio_pages(
    rendered: Sequence[tuple[str, bytes]], ids: Sequence[str]
) -> tuple[RenderedPage, ...]:
    pages = []
    ordinal = 1
    for page, (svg, png) in enumerate(rendered, 1):
        image = _from_png(png)
        geometry = parse_verovio_svg_geometry(
            svg,
            page=page,
            first_ordinal=ordinal,
            raster_size=image.size,
        )
        svg_root = _parse_xml(svg)
        actual = [
            node.get("id", node.get("data-id", ""))
            for node in svg_root.iter()
            if "measure" in _classes(node)
            and not _classes(node) & {"bounding-box", "content-bounding-box"}
        ]
        if actual != list(
            ids[ordinal - 1 : ordinal - 1 + len(geometry.measures)]
        ):
            msg = "Verovio SVG measures do not preserve canonical ordinal order"
            raise RenderRejected(msg)
        ordinal += len(actual)
        pages.append(RenderedPage(png, _expand_vertical(geometry, image)))
    if ordinal != len(ids) + 1:
        msg = "Verovio SVG pages do not cover all canonical measure ordinals"
        raise RenderRejected(msg)
    return tuple(pages)


def render_synthetic_probe_png(
    downloads_dir: Path, scratch_root: Path
) -> bytes:
    """Render one offline 2-measure grand-staff PNG for detector probes."""
    _, pages = _run_verovio(
        _CALIBRATION_XML,
        sample_layout_and_style(1, 2),
        downloads_dir,
        scratch_root,
        max_pages=2,
        count_rasters=None,
    )
    return pages[0][1]


@dataclass(frozen=True, slots=True)
class StagedVerovio:
    """Subprocess outputs ready for pure-Python SVG and MEI verification."""

    encoded: str
    ids: tuple[str, ...]
    categories: frozenset[str]
    mei: str
    rendered: tuple[tuple[str, bytes], ...]


def stage_verovio_subprocess(  # noqa: PLR0913
    x_inj: str,
    style: LayoutStyle,
    downloads_dir: Path,
    scratch_root: Path,
    ledger: RenderGateLedger,
    *,
    max_pages: int = 5_000,
    count_rasters: Callable[[int], None] | None = None,
) -> RenderResult | StagedVerovio:
    """Run the isolated Verovio subprocess and return raw SVG/PNG/MEI data."""
    engine = "verovio"
    _require_ledger(ledger, engine)
    categories: set[str] = set()
    try:
        encoded, ids = _labeled_xml(encode_layout_breaks(x_inj, style.layout))
        root = _parse_xml(encoded)
        categories = _categories(root, encoded)
        if blocked := _reject_unchecked_facets(root) | (
            categories & ledger.disabled_categories
        ):
            return _result(
                engine,
                ledger,
                categories,
                RenderGate(
                    "excluded",
                    dict.fromkeys(blocked, 1),
                    "unchecked_or_disabled_facets",
                ),
            )
        mei, rendered = _run_verovio(
            encoded,
            style,
            downloads_dir,
            scratch_root,
            max_pages=max_pages,
            count_rasters=count_rasters,
        )
    except _SAMPLE_ERRORS:
        return _result(
            engine,
            ledger,
            categories,
            RenderGate("dropped", reason="render_structure_or_conversion"),
        )
    return StagedVerovio(encoded, ids, frozenset(categories), mei, rendered)


def verify_verovio_outputs(
    staged: StagedVerovio, ledger: RenderGateLedger
) -> RenderResult:
    """Verify staged Verovio SVG geometry and MEI round-trip in pure Python."""
    engine = "verovio"
    _require_ledger(ledger, engine)
    categories = set(staged.categories)
    try:
        pages = _verovio_pages(staged.rendered, staged.ids)
        diff = _musicdiff_mei(
            staged.encoded, staged.mei
        ) + compare_mei_supplementary(staged.encoded, staged.mei)
        if diff:
            return _result(
                engine,
                ledger,
                categories,
                RenderGate("dropped", diff, "mei_roundtrip"),
            )
        layout = SystemLayout(
            len(staged.ids),
            tuple(
                SystemStart(a - 1, p, s)
                for p, s, a, _ in _rendered_spans(
                    [page.geometry for page in pages]
                )
            ),
        )
        texts = _system_texts(
            encode_layout_breaks(staged.encoded, layout), layout
        )
    except _SAMPLE_ERRORS:
        return _result(
            engine,
            ledger,
            categories,
            RenderGate("dropped", reason="render_structure_or_conversion"),
        )
    return _result(
        engine,
        ledger,
        categories,
        RenderGate("passed"),
        pages=pages,
        texts=texts,
    )


def render_verovio(  # noqa: PLR0913
    x_inj: str,
    style: LayoutStyle,
    downloads_dir: Path,
    scratch_root: Path,
    ledger: RenderGateLedger,
    *,
    max_pages: int = 5_000,
    count_rasters: Callable[[int], None] | None = None,
) -> RenderResult:
    """Render and admit a sample only with exact SVG and symbolic gates."""
    staged = stage_verovio_subprocess(
        x_inj,
        style,
        downloads_dir,
        scratch_root,
        ledger,
        max_pages=max_pages,
        count_rasters=count_rasters,
    )
    if isinstance(staged, RenderResult):
        return staged
    return verify_verovio_outputs(staged, ledger)
