"""W3 real-data alignment, lossless page extraction, overlay QA and slicing.

Follows ``docs/evaluation-protocol.md``:
- Extracts embedded ``2300 x 3185`` rasters from restored facsimile PDFs
  losslessly without host ``poppler-utils``.
- Reconciles per-piece ``(page, system -> [first_ordinal, last_ordinal])``
  mappings across workspace ``source-map*.json``, ``system-map.json``,
  ``reviews/*/source-inventory.json``, ``notes.md`` and MusicXML ``<print>``
  starts, verifying exact ``1..M_piece`` partitioning.
- Detects 5-line treble and bass staves via thin horizontal-line filtering +
  dynamic programming matched to each page's verified ``K_page`` system count,
  computes padded crop boxes, and renders ``92`` annotated overlay PNGs.
- Verifies ``0`` content differences when slicing and re-assembling all ``23``
  accepted gold pieces at their physical printed-system boundaries.
- Joins the ``225`` live ``T01`` whole-round rows and ``req012`` post-``T01``
  ``B`` loci deterministically from the local archive.
"""

from __future__ import annotations

import contextlib
import hashlib
import itertools
import json
import math
import os
import re
import shutil
import subprocess
import tempfile
from collections import Counter
from collections.abc import Mapping
from dataclasses import dataclass, replace
from pathlib import Path
from typing import TYPE_CHECKING, Any

import numpy as np
from PIL import Image, ImageDraw

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.comparison import (
    CONTENT_CATEGORIES,
    ProtocolDefectError,
    ScoreParseError,
)
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation.wheel_lock import compute_wheel_lock_sha256
from sheet_music_omr.harness.gold import load_gold
from sheet_music_omr.harness.records import run_git, source_state
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import (
    SplitError,
    SystemStart,
    split_score,
    system_starts,
)
from sheet_music_omr.harness.worker import STEP_SECONDS, WorkerPool
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.musicxml_forward import to_musicxml
from sheet_music_omr.musicxml_reverse import MusicXmlError, from_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.supplementary import compare_file
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Sequence

type JsonDict = dict[str, Any]

EXPECTED_PAGE_SIZE = (2300, 3185)
EXPECTED_TOTAL_PAGES = 92
TARGET_STAFF_SPACE_PX = 14.0

TEST_GOLD_IDS: frozenset[str] = frozenset(
    {
        "gold-01",
        "gold-02",
        "gold-03",
        "gold-04",
        "gold-05",
        "gold-06",
        "gold-18",
    }
)

GROUP_BY_INDEX: dict[int, str] = {
    1: "G3",
    2: "G1",
    3: "G1",
    4: "G1",
    5: "G1",
    6: "G2",
    7: "G5",
    8: "G5",
    9: "G5",
    10: "G2",
    11: "G5",
    12: "G5",
    13: "G4",
    14: "G1",
    15: "G1",
    16: "G6",
    17: "G6",
    18: "G6",
    19: "G6",
    20: "G6",
    21: "G6",
    22: "G7",
    23: "G7",
}

BOOK_COHORT_BY_INDEX: dict[int, str] = {
    **dict.fromkeys(range(1, 16), "book1"),
    **dict.fromkeys(range(16, 22), "book2"),
    22: "g7",
    23: "g7",
}

_NON_CONVERGED_SUBSTRINGS: tuple[str, ...] = (
    "partial",
    "holdsremain",
    "remainingfull",
    "fresh-round-pending",
    "otherrowsandaspectsremain",
    "userholdremains",
    "userquestion",
)

T01_CAT_TO_W1: dict[str, tuple[str, ...]] = {
    "P1": ("pitch", "chord_membership"),
    "P2": ("clef",),
    "P3": ("ottava",),
    "P4": ("key_signature",),
    "D1": ("rhythm_rest",),
    "D2": ("tuplet",),
    "D3": ("voice",),
    "D4": ("cross_staff",),
    "D5": ("grace",),
    "D6": ("time_signature",),
    "D7": ("tremolo",),
    "X1": ("accidental",),
    "L1": ("tie",),
    "L2": ("slur",),
    "L3": ("articulation_fermata",),
    "L4": ("arpeggio",),
    "L5": ("ornament",),
    "L6": ("beam",),
    "N1": ("dynamic",),
    "N2": ("hairpin",),
    "N3": ("text",),
    "N4": ("tempo_rehearsal",),
    "N5": ("pedal",),
    "N6": ("fingering",),
    "N7": ("tempo_rehearsal",),
    "N8": ("repeat_ending",),
    "N9": ("heading",),
}


@dataclass(frozen=True, slots=True)
class DetectedSystemBox:
    """Detected 2-staff grand-staff system geometry in facsimile page pixels."""

    box_px: tuple[int, int, int, int]
    treble_lines_y: tuple[float, ...]
    bass_lines_y: tuple[float, ...]
    staff_space_px: float


def sha256_bytes(data: bytes) -> str:
    """Return lowercase hex SHA-256 of ``data``."""
    return hashlib.sha256(data).hexdigest()


def _compute_geometry_sha256(pieces: Sequence[Mapping[str, Any]]) -> str:
    payload = [
        {
            "gold_id": p["gold_id"],
            "systems": [
                {
                    "system_index": s["system_index"],
                    "page_index": s["page_index"],
                    "ordinal_range": s["ordinal_range"],
                    "facsimile_box_px": s["facsimile_box_px"],
                    "staff_space_px": s["staff_space_px"],
                }
                for s in p["systems"]
            ],
        }
        for p in pieces
    ]
    return sha256_bytes(
        json.dumps(payload, sort_keys=True, separators=(",", ":")).encode(
            "utf-8"
        )
    )


def _compute_masks_sha256(pieces: Sequence[Mapping[str, Any]]) -> str:
    masks_map = {
        str(p["gold_id"]): p.get("open_reading_masks", []) for p in pieces
    }
    return sha256_bytes(
        json.dumps(masks_map, sort_keys=True, separators=(",", ":")).encode(
            "utf-8"
        )
    )


def venv_python_path(venv_dir: Path) -> Path:
    """Resolve host-appropriate Python executable inside ``venv_dir``."""
    if os.name == "nt":
        return venv_dir / "Scripts" / "python.exe"
    return venv_dir / "bin" / "python"


def check_frozen_run_masks(
    run_dir: Path,
    downloads_dir: Path | None,
    masks_sha256: str,
) -> None:
    """Reject any change to ``open_reading_masks_sha256`` across S0 campaign."""
    frozen_p = run_dir / "alignment" / "frozen-open-reading-masks-sha256.txt"
    if (
        frozen_p.is_file()
        and frozen_p.read_text(encoding="utf-8").strip() != masks_sha256
    ):
        msg = "open_reading_masks_sha256 cannot change after inference freeze"
        raise StopConditionError(msg)
    if downloads_dir is not None:
        camp_p = downloads_dir / "w2-frozen-open-reading-masks-sha256.txt"
        if (
            camp_p.is_file()
            and camp_p.read_text(encoding="utf-8").strip() != masks_sha256
        ):
            msg = (
                "open_reading_masks_sha256 differs from S0 campaign freeze "
                "in downloads directory"
            )
            raise StopConditionError(msg)
        touch_p = downloads_dir / "w2-touch-log.jsonl"
        if touch_p.is_file():
            for ln in touch_p.read_text(encoding="utf-8").splitlines():
                if not ln.strip():
                    continue
                rec = json.loads(ln)
                rec_sha = rec.get("open_reading_masks_sha256")
                if (
                    rec.get("purpose") == "S0-baseline"
                    and isinstance(rec_sha, str)
                    and rec_sha != masks_sha256
                ):
                    msg = (
                        "open_reading_masks_sha256 differs from prior S0 "
                        "touch-log freeze"
                    )
                    raise StopConditionError(msg)


def freeze_run_masks(
    run_dir: Path,
    downloads_dir: Path,
    masks_sha256: str,
) -> None:
    """Persist ``open_reading_masks_sha256`` for run & S0 campaign."""
    check_frozen_run_masks(run_dir, downloads_dir, masks_sha256)
    for target_p in (
        run_dir / "alignment" / "frozen-open-reading-masks-sha256.txt",
        downloads_dir / "w2-frozen-open-reading-masks-sha256.txt",
    ):
        if not target_p.is_file():
            target_p.parent.mkdir(parents=True, exist_ok=True)
            tmp_p = target_p.with_name(f".{target_p.name}.{os.getpid()}.tmp")
            tmp_p.write_text(masks_sha256 + "\n", encoding="utf-8")
            tmp_p.replace(target_p)


def check_no_symlink_under(target: Path, root: Path) -> None:
    """Reject symlinks on any path component from ``root`` to ``target``."""
    cur = root
    if cur.is_symlink():
        msg = f"refusing symlink root directory {cur.name}"
        raise StopConditionError(msg)
    for part in target.relative_to(root).parts:
        cur = cur / part
        if cur.is_symlink():
            msg = f"refusing symlink path component {part}"
            raise StopConditionError(msg)
    if not target.resolve().is_relative_to(root.resolve()):
        msg = f"path escapes confinement root: {target.name}"
        raise StopConditionError(msg)


def atomic_write_text(dest: Path, content: str) -> None:
    """Write ``content`` to ``dest`` without following destination symlinks."""
    if (
        dest.parent.is_symlink()
        or dest.is_symlink()
        or (dest.exists() and not dest.is_file())
    ):
        msg = f"non-file or symlink destination blocked: {dest.name}"
        raise StopConditionError(msg)
    dest.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_s = tempfile.mkstemp(prefix=f".{dest.name}.", dir=dest.parent)
    tmp_p = Path(tmp_s)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            fh.write(content)
        tmp_p.replace(dest)
    except BaseException:
        tmp_p.unlink(missing_ok=True)
        raise


def atomic_save_png(im: Image.Image, dest: Path) -> None:
    """Save ``im`` as PNG at ``dest`` without following destination symlinks."""
    if (
        dest.parent.is_symlink()
        or dest.is_symlink()
        or (dest.exists() and not dest.is_file())
    ):
        msg = f"non-file or symlink destination blocked: {dest.name}"
        raise StopConditionError(msg)
    dest.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_s = tempfile.mkstemp(
        prefix=f".{dest.stem}.", suffix=".png", dir=dest.parent
    )
    os.close(fd)
    tmp_p = Path(tmp_s)
    try:
        im.save(tmp_p, format="PNG")
        tmp_p.replace(dest)
    except BaseException:
        tmp_p.unlink(missing_ok=True)
        raise


def sha256_file(path: Path) -> str:
    """Return lowercase hex SHA-256 of ``path``."""
    return sha256_bytes(path.read_bytes())


_PDFIUM_EXTRACT_SCRIPT = """\
import io
import json
import sys
from pathlib import Path
import pypdfium2 as pdfium
from pypdfium2.raw import FPDF_PAGEOBJ_IMAGE
from PIL import Image

jobs = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
for pdf_s, out_pngs in jobs:
    pdf = pdfium.PdfDocument(pdf_s)
    try:
        if len(pdf) != len(out_pngs):
            raise RuntimeError(
                f"{Path(pdf_s).name}: {len(pdf)} != {len(out_pngs)}"
            )
        for idx, out_s in enumerate(out_pngs):
            page = pdf[idx]
            turn = int(page.get_rotation()) % 360
            imgs = list(page.get_objects(filter=[FPDF_PAGEOBJ_IMAGE]))
            if len(imgs) != 1:
                raise RuntimeError(
                    f"{Path(pdf_s).name} p{idx+1}: {len(imgs)} images != 1"
                )
            buf = io.BytesIO()
            imgs[0].extract(buf, fb_format="png")
            buf.seek(0)
            with Image.open(buf) as im:
                grey = im.convert("L")
                if turn:
                    grey = grey.rotate(-turn, expand=True)
                if grey.size != (2300, 3185):
                    raise RuntimeError(
                        f"{Path(pdf_s).name} p{idx+1}: size {grey.size}"
                    )
                grey.save(out_s)
    finally:
        pdf.close()
"""


def extract_pdf_pages_via_pdfium(
    jobs: Sequence[tuple[Path, Sequence[Path]]],
    venv_python: Path,
    tmp_dir: Path,
) -> None:
    """Extract embedded page rasters losslessly via isolated ``pypdfium2``."""
    if tmp_dir.is_symlink():
        msg = f"refusing symlink temp directory {tmp_dir.name}"
        raise StopConditionError(msg)
    tmp_dir.mkdir(parents=True, exist_ok=True)
    work_dir = Path(tempfile.mkdtemp(prefix="pdfium-jobs-", dir=tmp_dir))
    cfg_p = work_dir / "pdfium_jobs.json"
    try:
        for pdf, outs in jobs:
            atomic_write_text(
                cfg_p,
                json.dumps([[str(pdf), [str(p) for p in outs]]], indent=2),
            )
            try:
                res = subprocess.run(  # noqa: S603 - isolated venv script
                    [
                        str(venv_python),
                        "-c",
                        _PDFIUM_EXTRACT_SCRIPT,
                        str(cfg_p),
                    ],
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=300,
                    start_new_session=True,
                )
                if res.returncode != 0:
                    msg = (
                        f"pypdfium2 extraction failed for {pdf.name}: "
                        f"{res.stderr.strip()[-500:]}"
                    )
                    raise StopConditionError(msg)
            except subprocess.TimeoutExpired as exc:
                msg = f"pypdfium2 extraction timed out (>300s) for {pdf.name}"
                raise StopConditionError(msg) from exc
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)


def _five_line_comb_score(
    profs: Sequence[np.ndarray], h: int, sp: float, slope: float = 0.0
) -> np.ndarray:
    """Compute top-5-strip order-statistic 5-line comb score."""
    offs = [round(k * sp) for k in range(5)]
    lim = h - offs[-1]
    if lim <= 0:
        return np.zeros(0, dtype=np.float32)
    n_s = len(profs)
    mid_s = 0.5 * (n_s - 1)
    aligned = np.zeros((n_s, lim), dtype=np.float32)
    for s_i, prof_s in enumerate(profs):
        stack = np.stack([prof_s[off : lim + off] for off in offs], axis=0)
        s_ord = np.sort(stack, axis=0)
        sc = 0.5 * s_ord[0] + 0.5 * s_ord[1]
        dy = round(slope * (s_i - mid_s))
        if dy == 0:
            aligned[s_i] = sc
        elif 0 < dy < lim:
            aligned[s_i, :-dy] = sc[dy:]
        elif 0 < -dy < lim:
            aligned[s_i, -dy:] = sc[:dy]
    k_keep = min(n_s, max(1, n_s - 3))
    return np.sort(aligned, axis=0)[-k_keep:].mean(axis=0)


def _find_main_horizontal_span(  # noqa: C901, PLR0912, PLR0915
    thin: np.ndarray,
    top_line: float,
    yb: float,
    best_sp: float,
) -> tuple[int, int]:
    """Locate main system ``(x0, x1)`` while excluding facing-page slivers."""
    h, w = thin.shape
    bot_line = yb + 4.0 * best_sp
    staff_col = np.maximum(
        thin[
            max(0, int(top_line) - 4) : min(
                h, int(top_line + 4.0 * best_sp) + 5
            ),
            :,
        ].mean(axis=0),
        thin[max(0, int(yb) - 4) : min(h, int(bot_line) + 5), :].mean(axis=0),
    )
    col_cut = max(2.2, 0.13 * float(staff_col.max())) if w > 0 else 2.2
    active = staff_col >= col_cut
    if not np.any(active):
        return max(0, round(0.05 * w)), min(w, round(0.95 * w))

    # Identify outer facing-page seams (>= 16 blank cols in outer 15% margin)
    # BEFORE bridging internal gaps so narrow facing-page seams never merge!
    seam_min_blank = max(14, round(0.95 * best_sp))
    outer_lo = round(0.15 * w)
    outer_hi = round(0.85 * w)
    raw_runs: list[tuple[int, int]] = []
    in_r = False
    r0 = 0
    for x_i in range(w):
        if active[x_i] and not in_r:
            in_r, r0 = True, x_i
        elif not active[x_i] and in_r:
            raw_runs.append((r0, x_i - 1))
            in_r = False
    if in_r:
        raw_runs.append((r0, w - 1))

    seam_left = 0
    for (_la, lb), (ra, _rb) in itertools.pairwise(raw_runs):
        if lb < outer_lo and (ra - lb - 1) >= seam_min_blank:
            seam_left = lb + 1
    seam_right = w
    for (_la, lb), (ra, _rb) in itertools.pairwise(raw_runs):
        if (
            ra > outer_hi
            and (ra - lb - 1) >= seam_min_blank
            and seam_right == w
        ):
            seam_right = ra - 1

    interior = active.copy()
    if seam_left > 0:
        interior[:seam_left] = False
    if seam_right < w:
        interior[seam_right + 1 :] = False
    on_idx = np.nonzero(interior)[0]
    if len(on_idx) == 0:
        on_idx = np.nonzero(active)[0]

    bridge_px = max(24, round(2.4 * best_sp))
    bridged = interior.copy()
    for a_i, b_i in itertools.pairwise(on_idx):
        if int(b_i - a_i - 1) <= bridge_px:
            bridged[a_i : b_i + 1] = True

    runs: list[tuple[int, int, int]] = []
    in_run = False
    r_start = 0
    for x_i in range(w):
        if bridged[x_i] and not in_run:
            in_run, r_start = True, x_i
        elif not bridged[x_i] and in_run:
            runs.append((x_i - r_start, r_start, x_i - 1))
            in_run = False
    if in_run:
        runs.append((w - r_start, r_start, w - 1))

    runs.sort(reverse=True)
    _, ix0, ix1 = runs[0]
    x0 = (
        0
        if (ix0 <= round(3.0 * best_sp) and seam_left == 0)
        else max(0, round(ix0 - 4.6 * best_sp))
    )
    x1 = (
        w
        if ((w - 1 - ix1) <= round(2.5 * best_sp) and seam_right == w)
        else min(w, round(ix1 + 2.4 * best_sp))
    )
    if seam_left > 0:
        x0 = max(x0, seam_left + 10)
    if seam_right < w:
        x1 = min(x1, seam_right - 10)
    return x0, max(x0 + 64, x1)


def _scan_outer_margin_1d(
    clear: np.ndarray, staff_edge: float, best_sp: float
) -> int:
    """Scan outward from ``staff_edge`` for a clear band >= ``4.0 * sp``."""
    n = len(clear)
    wide_blank = max(32, math.ceil(2.15 * best_sp))
    min_y = math.floor(staff_edge - 4.0 * best_sp)
    y_outer = max(6, round(staff_edge - 9.0 * best_sp))
    start_up = max(y_outer + wide_blank, round(staff_edge - 0.8 * best_sp))
    for y_c in range(min(n - 6, start_up), y_outer + wide_blank - 1, -1):
        if bool(clear[y_c - wide_blank : y_c].all()):
            y_goal = max(6, min(y_c - 8, min_y - 1))
            y_top = max(6, y_c - 8)
            while y_top > y_goal and bool(clear[y_top - 1]):
                y_top -= 1
            if y_top <= min_y:
                thin_lim = max(10, round(best_sp))
                above_lo = max(6, y_top - thin_lim - 1)
                if (
                    y_top >= min_y - 1
                    and not bool(clear[max(6, y_top - 3) : y_top].all())
                    and above_lo > y_outer + 16
                    and bool(clear[above_lo - 16 : above_lo].all())
                ):
                    continue
                return y_top
    hi_fb = min(n - 6, min_y + 4)
    for y_c in range(hi_fb, y_outer + 7, -1):
        if bool(clear[y_c - 8 : y_c].all()):
            return max(6, min(min_y, y_c - 4))
    return y_outer


def _find_outer_vertical_edges(
    dark: np.ndarray,
    chosen: Sequence[tuple[int, int]],
    x_spans: Sequence[tuple[int, int]],
    best_sp: float,
) -> tuple[int, int]:
    """Find outer top/bottom bounds crossing Tempo/Ped. gaps with >=4*sp pad."""
    h, w = dark.shape
    xa0, xb0 = max(0, x_spans[0][0]), min(w, x_spans[0][1])
    clear_t = dark[:, xa0 : max(xa0 + 32, xb0)].sum(axis=1) <= 3  # noqa: PLR2004
    y_outer_top = _scan_outer_margin_1d(clear_t, float(chosen[0][0]), best_sp)

    bot_last = float(chosen[-1][1] + 4.0 * best_sp)
    xak, xbk = max(0, x_spans[-1][0]), min(w, x_spans[-1][1])
    clear_b = dark[:, xak : max(xak + 32, xbk)].sum(axis=1) <= 3  # noqa: PLR2004
    bot_flip = _scan_outer_margin_1d(clear_b[::-1], h - bot_last, best_sp)
    return y_outer_top, min(h - 6, h - bot_flip)


def _compute_system_vertical_bounds(
    arr: np.ndarray,
    chosen: Sequence[tuple[int, int]],
    x_spans: Sequence[tuple[int, int]],
    best_sp: float,
) -> list[tuple[int, int]]:
    """Find clear-gutter ``(y0, y1)`` per system so notation is not clipped."""
    h, w = arr.shape
    n_sys = len(chosen)
    pad_r = 32
    padded = np.pad(arr, ((pad_r, pad_r), (0, 0)), mode="edge")
    csum = np.cumsum(
        np.vstack([np.zeros((1, w), dtype=np.float64), padded]), axis=0
    )
    local_bg = (csum[2 * pad_r + 1 :] - csum[: -(2 * pad_r + 1)]) / float(
        2 * pad_r + 1
    )
    dark = (local_bg - arr) > 24.0  # noqa: PLR2004

    splits: list[int] = []
    for s_idx in range(n_sys - 1):
        prev_bot = float(chosen[s_idx][1] + 4.0 * best_sp)
        next_top = float(chosen[s_idx + 1][0])
        lo_y = max(8, round(prev_bot + 0.8 * best_sp))
        hi_y = min(h - 9, round(next_top - 0.8 * best_sp))
        if hi_y < lo_y:
            splits.append(round(0.5 * (prev_bot + next_top)))
            continue
        xa = max(0, min(x_spans[s_idx][0], x_spans[s_idx + 1][0]) + 14)
        xb = min(w, max(x_spans[s_idx][1], x_spans[s_idx + 1][1]) - 14)
        row_cnt = dark[lo_y - 3 : hi_y + 4, xa : max(xa + 32, xb)].sum(axis=1)
        gutter_cnt = np.convolve(
            row_cnt, np.ones(7, dtype=np.int64), mode="valid"
        )
        ys = np.arange(lo_y, hi_y + 1, dtype=np.float64)
        mid_y = 0.5 * (prev_bot + next_top)
        clear_idx = np.nonzero(gutter_cnt == 0)[0]
        if len(clear_idx) > 0:
            best_rel = int(
                clear_idx[int(np.argmin(np.abs(ys[clear_idx] - mid_y)))]
            )
        else:
            min_g = int(gutter_cnt.min())
            near_idx = np.nonzero(gutter_cnt <= min_g + 2)[0]
            best_rel = int(
                near_idx[int(np.argmin(np.abs(ys[near_idx] - mid_y)))]
            )
        splits.append(lo_y + best_rel)

    y_outer_top, y_outer_bot = _find_outer_vertical_edges(
        dark, chosen, x_spans, best_sp
    )

    bounds: list[tuple[int, int]] = []
    for s_idx, (yt, yb) in enumerate(chosen):
        top_l = float(yt)
        bot_l = float(yb + 4.0 * best_sp)
        y0 = (
            y_outer_top
            if s_idx == 0
            else max(splits[s_idx - 1] + 3, round(top_l - 9.2 * best_sp))
        )
        y1 = (
            y_outer_bot
            if s_idx == n_sys - 1
            else min(splits[s_idx] - 3, round(bot_l + 9.2 * best_sp))
        )
        bounds.append((max(0, y0), min(h, max(y0 + 32, y1))))
    return bounds


def detect_grand_staff_systems(  # noqa: C901, PLR0912, PLR0915
    page_im: Image.Image, expected_systems: int
) -> list[DetectedSystemBox]:
    """Locate ``expected_systems`` 2-staff piano systems via comb + DP."""
    arr = np.asarray(page_im.convert("L"), dtype=np.float32)
    h, w = arr.shape
    if expected_systems <= 0:
        msg = f"expected_systems must be >= 1, got {expected_systems}"
        raise StopConditionError(msg)

    thin = np.zeros_like(arr)
    for delta in (2, 3, 4):
        above = arr[: -2 * delta, :]
        below = arr[2 * delta :, :]
        mid = arr[delta:-delta, :]
        contrast = np.maximum(0.0, np.minimum(above, below) - mid - 10.0)
        thin[delta:-delta, :] = np.maximum(thin[delta:-delta, :], contrast)

    x_lo, x_hi = int(0.12 * w), int(0.88 * w)
    strips = np.linspace(x_lo, x_hi, 9, dtype=int)
    profs = [
        np.convolve(
            thin[:, strips[s_i] : strips[s_i + 1]].mean(axis=1),
            np.array([0.25, 0.5, 0.25], dtype=np.float32),
            mode="same",
        )
        for s_i in range(8)
    ]

    n_needed = 2 * expected_systems
    best_sp = 16.5
    best_slope = 0.0
    best_peak_sum = -1.0
    for sp_cand in np.arange(12.0, 24.5, 0.25):
        sp_f = float(sp_cand)
        md_c = max(12, round(5.2 * sp_f))
        for slope_c in (-1.5, -0.75, 0.0, 0.75, 1.5):
            comb_c = _five_line_comb_score(profs, h, sp_f, slope_c)
            if len(comb_c) <= n_needed:
                continue
            peaks_c: list[float] = []
            picked_y: list[int] = []
            for y_idx in np.argsort(-comb_c):
                y_i = int(y_idx)
                if y_i < round(1.8 * sp_f) or y_i > h - round(5.8 * sp_f):
                    continue
                if all(abs(y_i - py) >= md_c for py in picked_y):
                    picked_y.append(y_i)
                    peaks_c.append(float(comb_c[y_i]))
                    if len(peaks_c) >= n_needed:
                        break
            if len(peaks_c) == n_needed:
                sc = sum(peaks_c)
                if sc > best_peak_sum:
                    best_peak_sum = sc
                    best_sp = sp_f
                    best_slope = slope_c

    comb = _five_line_comb_score(profs, h, best_sp, best_slope)
    max_comb = float(comb.max()) if len(comb) > 0 else 0.0
    min_support = max(0.05, 0.004 * max_comb)
    min_dist = max(12, round(5.2 * best_sp))
    cands: list[tuple[int, float]] = []
    for y_idx in np.argsort(-comb):
        y_int = int(y_idx)
        val_c = float(comb[y_int])
        if val_c < min_support:
            break
        if y_int < round(1.8 * best_sp) or y_int > h - round(5.8 * best_sp):
            continue
        if all(abs(y_int - cy) >= min_dist for cy, _ in cands):
            cands.append((y_int, val_c))
            if len(cands) >= n_needed + 4:
                break
    cands.sort(key=lambda t: t[0])
    if len(cands) < n_needed:
        msg = (
            f"found only {len(cands)} supported staves, need {n_needed} "
            f"(sp={best_sp:.2f})"
        )
        raise StopConditionError(msg)

    max_s = max(*(s for _, s in cands), 1e-6)
    m_c = len(cands)
    pair_score = np.full((m_c, m_c), -1e9, dtype=np.float64)
    for a in range(m_c):
        ya, sa = cands[a]
        na = math.log1p(9.0 * sa / max_s) / math.log(10.0)
        for b in range(a + 1, m_c):
            yb, sb = cands[b]
            nb = math.log1p(9.0 * sb / max_s) / math.log(10.0)
            gap_sp = (yb - (ya + 4.0 * best_sp)) / best_sp
            if gap_sp < 1.5 or gap_sp > 17.5:  # noqa: PLR2004
                continue
            gap_pen = 0.015 * max(0.0, abs(gap_sp - 6.5) - 5.5) ** 2
            pair_score[a, b] = float(na + nb) - gap_pen

    dp = np.full((expected_systems, m_c, m_c), -1e9, dtype=np.float64)
    neg_inf = -1e8
    prev: dict[tuple[int, int, int], tuple[int, int]] = {}
    for a in range(m_c):
        for b in range(a + 1, m_c):
            if pair_score[a, b] > neg_inf:
                dp[0, a, b] = pair_score[a, b]

    for k in range(1, expected_systems):
        for a in range(m_c):
            ya, _ = cands[a]
            for b in range(a + 1, m_c):
                if pair_score[a, b] <= neg_inf:
                    continue
                best_prev = -1e9
                best_pair = (-1, -1)
                for pa in range(a):
                    for pb in range(pa + 1, a):
                        if dp[k - 1, pa, pb] <= neg_inf:
                            continue
                        yb_prev = cands[pb][0] + 4.0 * best_sp
                        inter_sp = (ya - yb_prev) / best_sp
                        if inter_sp < 1.8:  # noqa: PLR2004
                            continue
                        inter_pen = 0.35 * max(0.0, 3.2 - inter_sp) ** 2
                        val = dp[k - 1, pa, pb] + pair_score[a, b] - inter_pen
                        if val > best_prev:
                            best_prev = val
                            best_pair = (pa, pb)
                if best_pair[0] >= 0:
                    dp[k, a, b] = best_prev
                    prev[(k, a, b)] = best_pair

    best_end = (-1, -1)
    best_total = -1e9
    for a in range(m_c):
        for b in range(a + 1, m_c):
            if dp[expected_systems - 1, a, b] > best_total:
                best_total = float(dp[expected_systems - 1, a, b])
                best_end = (a, b)
    if best_end[0] < 0:
        msg = f"could not form {expected_systems} grand-staff pairs on page"
        raise StopConditionError(msg)

    chosen: list[tuple[int, int]] = []
    cur = best_end
    for k in range(expected_systems - 1, -1, -1):
        chosen.append((cands[cur[0]][0], cands[cur[1]][0]))
        if k > 0:
            cur = prev[(k, cur[0], cur[1])]
    chosen.reverse()

    x_spans = [
        _find_main_horizontal_span(thin, float(yt), float(yb), best_sp)
        for yt, yb in chosen
    ]
    y_bounds = _compute_system_vertical_bounds(arr, chosen, x_spans, best_sp)

    boxes: list[DetectedSystemBox] = []
    for (yt, yb), (x0, x1), (y0, y1) in zip(
        chosen, x_spans, y_bounds, strict=True
    ):
        t_lines = tuple(round(float(yt) + k * best_sp, 2) for k in range(5))
        b_lines = tuple(round(float(yb) + k * best_sp, 2) for k in range(5))
        boxes.append(
            DetectedSystemBox(
                box_px=(x0, y0, x1, y1),
                treble_lines_y=t_lines,
                bass_lines_y=b_lines,
                staff_space_px=round(best_sp, 3),
            )
        )
    return boxes


def render_page_overlay(
    page_im: Image.Image,
    gold_id: str,
    page_index: int,
    systems: Sequence[tuple[int, tuple[int, int], DetectedSystemBox]],
    out_path: Path,
) -> None:
    """Render visual QA overlay showing system boxes and ordinals."""
    rgb = page_im.convert("RGB")
    w_pg, h_pg = rgb.size
    canvas = Image.new("RGB", (w_pg + 280, h_pg), (248, 250, 252))
    canvas.paste(rgb, (0, 0))
    draw = ImageDraw.Draw(canvas)
    draw.line([(w_pg, 0), (w_pg, h_pg)], fill=(203, 213, 225), width=2)
    colors = [(220, 38, 38), (22, 163, 74), (37, 99, 235), (180, 83, 9)]
    for idx, (sys_no, (o_lo, o_hi), det) in enumerate(systems):
        color = colors[idx % len(colors)]
        x0, y0, x1, y1 = det.box_px
        for t_y in det.treble_lines_y:
            draw.line([(x0, t_y), (x1, t_y)], fill=(6, 182, 212), width=1)
        for b_y in det.bass_lines_y:
            draw.line([(x0, b_y), (x1, b_y)], fill=(168, 85, 247), width=1)
        draw.rectangle([x0, y0, x1, y1], outline=color, width=2)
        yc = max(24, min(h_pg - 24, (y0 + y1) // 2))
        draw.line([(x1, yc), (w_pg + 8, yc)], fill=color, width=2)
        draw.rectangle([w_pg + 8, yc - 22, w_pg + 272, yc + 22], fill=color)
        draw.text(
            (w_pg + 14, yc - 17),
            f"{gold_id} p{page_index:02d} S{sys_no:02d}",
            fill=(255, 255, 255),
        )
        draw.text(
            (w_pg + 14, yc + 3),
            f"m.{o_lo}..{o_hi} (sp={det.staff_space_px:.1f})",
            fill=(255, 255, 255),
        )
    atomic_save_png(canvas, out_path)


def export_normalized_crop(
    page_im: Image.Image,
    box_px: tuple[int, int, int, int],
    staff_space_px: float,
    out_path: Path,
) -> tuple[int, int, float]:
    """Crop ``box_px`` and resample to ``TARGET_STAFF_SPACE_PX = 14.0``."""
    if out_path.is_symlink() or (out_path.exists() and not out_path.is_file()):
        msg = f"non-file destination blocks crop {out_path.name}"
        raise StopConditionError(msg)
    crop = page_im.convert("L").crop(box_px)
    scale = TARGET_STAFF_SPACE_PX / staff_space_px
    new_w = max(1, round(crop.width * scale))
    new_h = max(1, round(crop.height * scale))
    resized = crop.resize((new_w, new_h), resample=Image.Resampling.BICUBIC)
    atomic_save_png(resized, out_path)
    if out_path.is_symlink() or not out_path.is_file():
        msg = f"crop export did not produce file {out_path.name}"
        raise StopConditionError(msg)
    return new_w, new_h, round(scale, 6)


def resolve_piece_system_ordinals(  # noqa: C901, PLR0911, PLR0912, PLR0915
    idx: int,
    ws_dir: Path,
    gold_xml_bytes: bytes,
    source_page_count: int,
    measure_count: int,
) -> tuple[list[tuple[int, int, int, int, list[int], str]], str]:
    """Resolve ``[(page, sys, first_ord, last_ord, bars, trust), ...]``."""
    # 1. Structured root source-map / system-map JSON where complete
    sys_map_p = ws_dir / "system-map.json"
    src_map_p = ws_dir / "source-map.json"

    if idx == 7 and sys_map_p.exists():  # noqa: PLR2004
        raw_sys = json.loads(sys_map_p.read_text(encoding="utf-8"))["systems"]
        out: list[tuple[int, int, int, int, list[int], str]] = []
        for s_i, row in enumerate(raw_sys):
            last_o = (
                measure_count if s_i == len(raw_sys) - 1 else int(row["last"])
            )
            out.append(
                (
                    int(row["page"]),
                    int(row["system"]),
                    int(row["first"]),
                    last_o,
                    [],
                    "absent",
                )
            )
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "system-map.json"

    if idx == 5 and src_map_p.exists():  # noqa: PLR2004
        raw_m5 = json.loads(src_map_p.read_text(encoding="utf-8"))["measures"]
        grouped_5: dict[tuple[int, int], list[JsonDict]] = {}
        for r in raw_m5:
            grouped_5.setdefault(
                (int(r["pdf_page"]), int(r["system"])), []
            ).append(r)
        out = []
        for (pg, sy), items in sorted(grouped_5.items()):
            ords = [int(x["measure"]) for x in items]
            if sorted(ords) != list(range(min(ords), max(ords) + 1)):
                msg = f"non-contiguous measure rows in gold-05 p{pg} s{sy}"
                raise StopConditionError(msg)
            xs_set: set[int] = set()
            for x in items:
                xb = x.get("x_bounds_px_600dpi", [])
                if isinstance(xb, list):
                    xs_set.update(round(int(v) * 2300 / 5100) for v in xb)
            out.append(
                (pg, sy, min(ords), max(ords), sorted(xs_set), "approximate")
            )
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "source-map.json"

    if idx == 9 and src_map_p.exists():  # noqa: PLR2004
        rows = json.loads(src_map_p.read_text(encoding="utf-8"))
        grouped: dict[tuple[int, int], list[JsonDict]] = {}
        for r in rows:
            grouped.setdefault((int(r["page"]), int(r["system"])), []).append(r)
        out = []
        for (pg, sy), items in sorted(grouped.items()):
            ords = [int(x["ordinal"]) for x in items]
            xs = sorted(
                {round(int(x["x0"]) * 2300 / 5100) for x in items if "x0" in x}
                | {
                    round(int(x["x1"]) * 2300 / 5100)
                    for x in items
                    if "x1" in x
                }
            )
            out.append((pg, sy, min(ords), max(ords), xs, "approximate"))
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "source-map.json"

    if idx in {13, 14, 15, 16, 18, 21, 22, 23} and src_map_p.exists():
        raw = json.loads(src_map_p.read_text(encoding="utf-8"))
        sys_rows = raw["systems"] if isinstance(raw, dict) else raw
        out = []
        per_page_counter: Counter[int] = Counter()
        for row in sys_rows:
            pg = int(row.get("page") or row.get("pdf_page"))
            per_page_counter[pg] += 1
            sy = per_page_counter[pg]
            if "first_ordinal" in row:
                fo, lo = int(row["first_ordinal"]), int(row["last_ordinal"])
            elif "first" in row:
                fo, lo = int(row["first"]), int(row["last"])
            else:
                ords = row["ordinals"]
                fo, lo = int(ords[0]), int(ords[-1])
            bars, trust = _extract_barlines_2300(row)
            out.append((pg, sy, fo, lo, bars, trust))
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "source-map.json"

    if idx == 12 and src_map_p.exists():  # noqa: PLR2004
        sys_rows = json.loads(src_map_p.read_text(encoding="utf-8"))
        out = []
        for row in sys_rows:
            bars, trust = _extract_barlines_2300(row)
            out.append(
                (
                    int(row["page"]),
                    int(row["system"]),
                    int(row["first"]),
                    int(row["last"]),
                    bars,
                    trust,
                )
            )
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "source-map.json"

    # 2. Explicit workspace notes.md tables for gold-02, gold-08, gold-10
    notes_txt = (ws_dir / "notes.md").read_text(encoding="utf-8")
    if idx == 2:  # noqa: PLR2004
        out = _parse_gold02_notes(notes_txt)
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "notes.md"
    if idx == 8:  # noqa: PLR2004
        out = _parse_gold08_notes(notes_txt)
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "notes.md"
    if idx == 10:  # noqa: PLR2004
        out = _parse_gold10_notes(notes_txt)
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "notes.md"

    # 3. MusicXML printed system starts (reconciled with source-extracts.json)
    lay = system_starts(gold_xml_bytes)
    starts_1 = [s.ordinal + 1 for s in lay.starts]
    ranges = [
        (
            starts_1[k],
            (starts_1[k + 1] - 1) if k + 1 < len(starts_1) else measure_count,
        )
        for k in range(len(starts_1))
    ]
    if idx == 20:  # noqa: PLR2004
        return (
            _reconcile_source_extracts(
                idx,
                ws_dir / "source-extracts.json",
                ranges,
                source_page_count,
                measure_count,
            ),
            "musicxml-print+source-extracts",
        )

    xml_pages = [s.page for s in lay.starts]
    if max(xml_pages) == source_page_count:
        out = []
        p_cnt: Counter[int] = Counter()
        for pg, (fo, lo) in zip(xml_pages, ranges, strict=True):
            p_cnt[pg] += 1
            out.append((pg, p_cnt[pg], fo, lo, [], "absent"))
        _validate_partition(idx, out, source_page_count, measure_count)
        return out, "musicxml-print"

    # gold-11 (16=4x4), gold-17 (31 from source-inventory), gold-19 (24=4x6)
    page_sys_counts = _resolve_page_system_counts_from_reviews(
        idx, ws_dir, len(ranges), source_page_count
    )
    out = []
    cursor = 0
    for pg, k_sys in enumerate(page_sys_counts, start=1):
        for sy in range(1, k_sys + 1):
            fo, lo = ranges[cursor]
            out.append((pg, sy, fo, lo, [], "absent"))
            cursor += 1
    _validate_partition(idx, out, source_page_count, measure_count)
    return out, "musicxml-print+review-page-map"


def _resolve_page_system_counts_from_reviews(
    idx: int, ws_dir: Path, total_systems: int, source_page_count: int
) -> list[int]:
    if idx == 17:  # noqa: PLR2004
        counts: dict[int, int] = {}
        for inv_p in sorted(
            (ws_dir / "reviews").glob(
                "annotations-p*-11*/source-inventory.json"
            )
        ):
            d = json.loads(inv_p.read_text(encoding="utf-8"))
            sm = d.get("source_map", {})
            pg = int(sm["page"])
            sr = sm.get("systems") or sm.get("system_ranges")
            counts[pg] = len(sr)
        if (
            len(counts) == source_page_count
            and sum(counts.values()) == total_systems
        ):
            return [counts[p] for p in range(1, source_page_count + 1)]
    if total_systems % source_page_count == 0:
        per_p = total_systems // source_page_count
        return [per_p] * source_page_count
    msg = (
        f"cannot partition {total_systems} systems across "
        f"{source_page_count} pages for gold-{idx:02d}"
    )
    raise StopConditionError(msg)


def _parse_gold02_notes(
    text: str,
) -> list[tuple[int, int, int, int, list[int], str]]:
    out: list[tuple[int, int, int, int, list[int], str]] = []
    for m in re.finditer(
        r"^\|\s*([1-6])\s*\|\s*\d+[\u2013-]\d+\s*\|\s*([0-9\u2013\-;\s]+)\|",
        text,
        re.MULTILINE,
    ):
        pg = int(m.group(1))
        parts = [p.strip() for p in m.group(2).split(";") if p.strip()]
        for sy, part in enumerate(parts, start=1):
            a, b = re.split(r"[\u2013-]", part)
            out.append((pg, sy, int(a.strip()), int(b.strip()), [], "absent"))
    return out


def _parse_gold08_notes(
    text: str,
) -> list[tuple[int, int, int, int, list[int], str]]:
    out: list[tuple[int, int, int, int, list[int], str]] = []
    for m in re.finditer(
        r"^\|\s*([12])\s*\|\s*([1-5])\s*\|\s*(\d+)[\u2013-](\d+)\s*\|"
        r"\s*system-\d+\.pdf",
        text,
        re.MULTILINE,
    ):
        out.append(
            (
                int(m.group(1)),
                int(m.group(2)),
                int(m.group(3)),
                int(m.group(4)),
                [],
                "absent",
            )
        )
    return out


def _parse_gold10_notes(
    text: str,
) -> list[tuple[int, int, int, int, list[int], str]]:
    out: list[tuple[int, int, int, int, list[int], str]] = []
    cur = 1
    for m in re.finditer(
        r"^\|\s*([1-7])\s*\|\s*22[1-7]\s*\|\s*([0-9,]+)\s*\|\s*(\d+)-(\d+)\s*\|",
        text,
        re.MULTILINE,
    ):
        pg = int(m.group(1))
        counts = [int(x) for x in m.group(2).split(",")]
        for sy, c in enumerate(counts, start=1):
            out.append((pg, sy, cur, cur + c - 1, [], "absent"))
            cur += c
    return out


def _parse_source_extract_row(
    idx: int, r: object
) -> tuple[int, int, list[int] | None]:
    if not isinstance(r, dict):
        msg = f"gold-{idx:02d}: non-object entry in source-extracts"
        raise StopConditionError(msg)
    pk = r.get("pdf_page", r.get("source_page", r.get("page")))
    sk = r.get("source_system", r.get("system"))
    if (
        not isinstance(pk, int)
        or isinstance(pk, bool)
        or pk < 1
        or not isinstance(sk, int)
        or isinstance(sk, bool)
        or sk < 1
    ):
        msg = f"gold-{idx:02d}: malformed source-extracts page/system keys"
        raise StopConditionError(msg)
    if "ordinals" not in r:
        return pk, sk, None
    raw_o = r["ordinals"]
    if (
        not isinstance(raw_o, list)
        or not raw_o
        or not all(
            isinstance(x, int) and not isinstance(x, bool) for x in raw_o
        )
    ):
        msg = f"gold-{idx:02d}: malformed source-extracts ordinals"
        raise StopConditionError(msg)
    return pk, sk, list(raw_o)


def _reconcile_source_extracts(
    idx: int,
    extracts_p: Path,
    ranges: Sequence[tuple[int, int]],
    source_page_count: int,
    measure_count: int,
) -> list[tuple[int, int, int, int, list[int], str]]:
    """Reconcile reflowed MusicXML system ranges against source-extracts."""
    try:
        ext_raw = json.loads(extracts_p.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = f"gold-{idx:02d}: unreadable source-extracts.json ({exc})"
        raise StopConditionError(msg) from exc
    ext_rows = ext_raw.get("regions") if isinstance(ext_raw, dict) else ext_raw
    if not isinstance(ext_rows, list) or len(ext_rows) != len(ranges):
        msg = f"gold-{idx:02d}: source-extracts count != {len(ranges)} systems"
        raise StopConditionError(msg)

    parsed = sorted(
        (_parse_source_extract_row(idx, r) for r in ext_rows),
        key=lambda item: (item[0], item[1]),
    )
    keys = [(pg, sy) for pg, sy, _ in parsed]
    pages = [pg for pg, _ in keys]
    if len(set(keys)) != len(ranges) or set(pages) != set(
        range(1, source_page_count + 1)
    ):
        msg = f"gold-{idx:02d}: invalid source-extracts page/system keys"
        raise StopConditionError(msg)
    global_ids = [sy for _, sy in keys] == list(
        range(1, len(ranges) + 1)
    ) and pages == sorted(pages)
    p_cnt: Counter[int] = Counter()
    out: list[tuple[int, int, int, int, list[int], str]] = []
    for (pg, sy, ords), (fo, lo) in zip(parsed, ranges, strict=True):
        p_cnt[pg] += 1
        local_sy = p_cnt[pg] if global_ids else sy
        if ords is not None and ords != list(range(fo, lo + 1)):
            msg = (
                f"gold-{idx:02d}: source-extracts ordinals {ords} != {fo}..{lo}"
            )
            raise StopConditionError(msg)
        out.append((pg, local_sy, fo, lo, [], "absent"))
    _validate_partition(idx, out, source_page_count, measure_count)
    return out


def _extract_barlines_2300(row: Mapping[str, object]) -> tuple[list[int], str]:
    scale_600 = 2300.0 / 5100.0
    if "right_barlines_px_600dpi" in row and isinstance(
        row["right_barlines_px_600dpi"], list
    ):
        left = row.get("left_barline_px_600dpi")
        xs = (
            [round(float(left) * scale_600)]
            if isinstance(left, (int, float))
            else []
        ) + [
            round(float(x) * scale_600)
            for x in row["right_barlines_px_600dpi"]
            if isinstance(x, (int, float))
        ]
        return xs, "detected"
    if "bars" in row and isinstance(row["bars"], list):
        xs = []
        for b in row["bars"]:
            if isinstance(b, list) and len(b) == 2:  # noqa: PLR2004
                xs.append(round(0.5 * (float(b[0]) + float(b[1])) * scale_600))
            elif isinstance(b, (int, float)):
                xs.append(round(float(b) * scale_600))
        return xs, "detected"
    if "barlines" in row and isinstance(row["barlines"], list):
        xs = [
            round(float(x) * scale_600)
            for x in row["barlines"]
            if isinstance(x, (int, float))
        ]
        return xs, "detected"
    if "source_barline_x" in row and isinstance(row["source_barline_x"], list):
        xs = [
            round(float(x) * scale_600)
            for x in row["source_barline_x"]
            if isinstance(x, (int, float))
        ]
        return xs, "detected"
    if "bar_boundary_px_in_crop_approximate" in row and isinstance(
        row["bar_boundary_px_in_crop_approximate"], list
    ):
        xs = [
            round(float(x) * scale_600)
            for x in row["bar_boundary_px_in_crop_approximate"]
            if isinstance(x, (int, float))
        ]
        return xs, "approximate"
    return [], "absent"


def _validate_partition(
    idx: int,
    systems: Sequence[tuple[int, int, int, int, list[int], str]],
    source_page_count: int,
    measure_count: int,
) -> None:
    if not systems:
        msg = f"gold-{idx:02d}: empty system list"
        raise StopConditionError(msg)
    pages_seen = sorted({pg for pg, _, _, _, _, _ in systems})
    if pages_seen != list(range(1, source_page_count + 1)):
        msg = (
            f"gold-{idx:02d}: system pages {pages_seen} != "
            f"1..{source_page_count}"
        )
        raise StopConditionError(msg)
    by_page: dict[int, list[int]] = {}
    for pg, sy, *_ in systems:
        by_page.setdefault(pg, []).append(sy)
    for pg, sys_nums in by_page.items():
        if sys_nums != list(range(1, len(sys_nums) + 1)):
            msg = (
                f"gold-{idx:02d} p{pg}: system indices {sys_nums} != "
                f"1..{len(sys_nums)}"
            )
            raise StopConditionError(msg)
    expected_next = 1
    for pg, sy, fo, lo, _, _ in systems:
        if fo != expected_next or lo < fo:
            msg = (
                f"gold-{idx:02d} p{pg}s{sy}: ordinal range [{fo}, {lo}] "
                f"breaks contiguous partition at {expected_next}"
            )
            raise StopConditionError(msg)
        expected_next = lo + 1
    if expected_next - 1 != measure_count:
        msg = (
            f"gold-{idx:02d}: final ordinal {expected_next - 1} != "
            f"measure_count {measure_count}"
        )
        raise StopConditionError(msg)


def classify_aspect_convergence(
    piece_ac_entry: Mapping[str, object],
) -> tuple[str, list[str]]:
    """Apply the deterministic ``L0``/``L1`` convergence predicate."""
    aspects_obj = piece_ac_entry.get("aspects", {})
    if not isinstance(aspects_obj, Mapping):
        msg = "invalid aspects mapping in aspect-convergence.json"
        raise StopConditionError(msg)
    converged: list[str] = []
    for aspect in (
        "pitch",
        "duration",
        "accidental-display",
        "articulations-lines",
        "annotations",
    ):
        entry = aspects_obj.get(aspect)
        if not isinstance(entry, Mapping):
            msg = f"missing convergence entry for aspect {aspect!r}"
            raise StopConditionError(msg)
        status_raw = str(entry.get("status", "")).strip()
        if not status_raw:
            msg = f"empty convergence status for aspect {aspect!r}"
            raise StopConditionError(msg)
        if bool(entry.get("pending_complete_clean_scopes")):
            continue
        status_lower = status_raw.lower()
        if any(tok in status_lower for tok in _NON_CONVERGED_SUBSTRINGS):
            continue
        converged.append(aspect)
    tier = "L0" if len(converged) == 5 else "L1"  # noqa: PLR2004
    return tier, converged


def load_open_reading_masks(  # noqa: C901
    masks_path: Path,
    measure_counts: Mapping[str, int],
) -> tuple[dict[str, list[dict[str, object]]], str]:
    """Load and validate pre-registered open-reading masks from local input."""
    if not masks_path.is_file():
        msg = f"open-reading masks file missing: {masks_path.name}"
        raise StopConditionError(msg)
    raw = json.loads(masks_path.read_text(encoding="utf-8"))
    if (
        not isinstance(raw, dict)
        or raw.get("schema") != "omr-open-reading-masks-v1"
        or not isinstance(raw.get("pieces"), dict)
    ):
        msg = "open-reading masks must have schema omr-open-reading-masks-v1"
        raise StopConditionError(msg)

    raw_pieces: Mapping[str, Any] = raw["pieces"]
    expected_gids = set(measure_counts.keys())
    unknown_gids = sorted(set(raw_pieces.keys()) - expected_gids)
    if unknown_gids:
        msg = f"unknown gold_ids in open-reading masks: {unknown_gids}"
        raise StopConditionError(msg)

    valid_cats = set(CONTENT_CATEGORIES)
    masks: dict[str, list[dict[str, object]]] = {}
    for gid in sorted(expected_gids):
        m_max = measure_counts[gid]
        items_raw = raw_pieces.get(gid, [])
        if not isinstance(items_raw, list):
            msg = f"open-reading masks for {gid} must be a list"
            raise StopConditionError(msg)
        norm_items: list[dict[str, object]] = []
        for item in items_raw:
            if not isinstance(item, dict) or set(item.keys()) != {
                "measure_ordinal",
                "categories",
                "basis",
            }:
                msg = f"invalid open-reading mask entry keys for {gid}"
                raise StopConditionError(msg)
            ord_v = item["measure_ordinal"]
            cats_v = item["categories"]
            basis_v = item["basis"]
            if (
                isinstance(ord_v, bool)
                or not isinstance(ord_v, int)
                or not (1 <= ord_v <= m_max)
            ):
                msg = f"invalid mask measure {ord_v!r} for {gid} (1..{m_max})"
                raise StopConditionError(msg)
            if (
                not isinstance(cats_v, list)
                or not cats_v
                or not all(
                    isinstance(c, str) and c in valid_cats for c in cats_v
                )
            ):
                msg = f"invalid mask categories for {gid} m.{ord_v}"
                raise StopConditionError(msg)
            if not isinstance(basis_v, str) or not basis_v.strip():
                msg = f"empty mask basis for {gid} m.{ord_v}"
                raise StopConditionError(msg)
            norm_cats = sorted(set(cats_v))
            norm_items.append(
                {
                    "measure_ordinal": ord_v,
                    "categories": norm_cats,
                    "basis": basis_v.strip(),
                }
            )
        norm_items.sort(
            key=lambda e: (
                int(e["measure_ordinal"]),  # type: ignore[arg-type]
                tuple(str(c) for c in e["categories"]),  # type: ignore[attr-defined]
                str(e["basis"]),
            )
        )
        masks[gid] = norm_items

    canonical = json.dumps(masks, sort_keys=True, separators=(",", ":")).encode(
        "utf-8"
    )
    return masks, sha256_bytes(canonical)


def _extract_t01_live_rows(
    t01_main: object, t01_add: object
) -> list[tuple[int, int, str, str, str]]:
    main_rows = (
        t01_main["rows"]
        if isinstance(t01_main, dict) and "rows" in t01_main
        else t01_main
    )
    add_rows = (
        t01_add["rows"]
        if isinstance(t01_add, dict) and "rows" in t01_add
        else t01_add
    )
    if not isinstance(main_rows, list) or not isinstance(add_rows, list):
        msg = "invalid T01 findings JSON structure"
        raise StopConditionError(msg)

    live_rows: list[tuple[int, int, str, str, str]] = []
    for row in main_rows:
        if isinstance(row, dict):
            p_s, r_s = str(row["piece"]), str(row["round"])
            f_s, c_s, cls_s = (
                str(row["folder"]),
                str(row["cat"]),
                str(row["cls"]),
            )
        else:
            p_s, r_s = str(row[0]), str(row[1])
            f_s, c_s, cls_s = str(row[2]), str(row[3]), str(row[4])
        p_num = int(p_s.split("-")[0])
        rnd = int(r_s.lstrip("r"))
        live_rows.append((p_num, rnd, f_s, c_s, cls_s))

    for row in add_rows:
        if isinstance(row, dict):
            if str(row.get("tier", "")).startswith("nonstandard"):
                continue
            p_s, rnd_s = str(row["piece"]), str(row["round"])
            f_s, c_s, cls_s = (
                str(row["folder"]),
                str(row["cat"]),
                str(row["cls"]),
            )
        else:
            p_s, rnd_s = str(row[0]), str(row[1])
            f_s, c_s, cls_s = str(row[2]), str(row[3]), str(row[4])
        if rnd_s.startswith("r") and rnd_s[1:].isdigit():
            p_num = int(p_s.split("-")[0])
            live_rows.append((p_num, int(rnd_s[1:]), f_s, c_s, cls_s))
    return live_rows


def extract_t01_archive_join(  # noqa: C901, PLR0912, PLR0915
    desk_study_reports_dir: Path,
    immich_dir: Path,
    ws_by_num: Mapping[int, str],
    measure_counts: Mapping[str, int],
) -> JsonDict:
    """Join the ``225`` live ``T01`` ``r<N>`` rows to archive findings."""
    t01_main = json.loads(
        (desk_study_reports_dir / "T01-findings-classified.json").read_text(
            encoding="utf-8"
        )
    )
    t01_add = json.loads(
        (desk_study_reports_dir / "T01-findings-addendum.json").read_text(
            encoding="utf-8"
        )
    )
    live_rows = _extract_t01_live_rows(t01_main, t01_add)

    if len(live_rows) != 225:  # noqa: PLR2004
        msg = f"expected 225 live T01 r<N> rows, got {len(live_rows)}"
        raise StopConditionError(msg)

    # Group by (p_num, folder) preserving order (gold-04 root-r3 has 1 row in
    # T01-findings-classified.json followed by 1 in T01-findings-addendum.json).
    by_folder: dict[tuple[int, str], list[tuple[int, str, str]]] = {}
    for p_num, rnd, folder, cat, cls in live_rows:
        by_folder.setdefault((p_num, folder), []).append((rnd, cat, cls))

    round_a_ex_burst: Counter[int] = Counter()
    round_a_all: Counter[int] = Counter()
    late_non_a_by_pr: Counter[tuple[int, int]] = Counter()
    late_piece_rounds: set[tuple[int, int]] = set()
    survivor_anchors_test: set[tuple[str, int]] = set()
    survivor_spans_test: set[tuple[str, int]] = set()
    per_measure_rounds: dict[tuple[str, int, int], dict[str, Counter[str]]] = {}
    unlocated_rounds: dict[tuple[str, int], dict[str, Counter[str]]] = {}

    located_count = 0
    unlocated_count = 0

    for (p_num, folder), entries in sorted(by_folder.items()):
        gid = f"gold-{p_num:02d}"
        ws_name = ws_by_num[p_num]
        fp = immich_dir / ws_name / "reviews" / folder / "findings.json"
        if not fp.exists():
            msg = f"missing archive findings file for {gid} {folder}"
            raise StopConditionError(msg)
        raw_items = _load_t01_folder_entries(fp)
        is_gold01_r1_ann = p_num == 1 and (
            folder in {"annotations-fingering-1", "root-review-1"}
            or folder.endswith("_annotations_initial_full")
        )
        is_gold01_burst = p_num == 1 and (
            folder == "annotations-fingering-1"
            or folder.endswith("_annotations_initial_full")
        )

        for entry_idx, (rnd, cat, cls) in enumerate(entries):
            if cls == "A":
                round_a_all[rnd] += 1
                if not is_gold01_burst:
                    round_a_ex_burst[rnd] += 1
            if rnd >= 3:  # noqa: PLR2004
                late_piece_rounds.add((p_num, rnd))
                if cls != "A":
                    late_non_a_by_pr[(p_num, rnd)] += 1

            # gold-01 r1 rows 222-223: 2 narrative R retractions
            if (
                is_gold01_r1_ann
                and cls == "R"
                and entry_idx in {len(raw_items), len(raw_items) + 1}
            ):
                unlocated_count += 1
                unlocated_rounds.setdefault((gid, rnd), {}).setdefault(
                    cls, Counter()
                )[cat] += 1
                continue

            if entry_idx >= len(raw_items):
                msg = (
                    f"{gid} {folder}: T01 entry {entry_idx} exceeds "
                    f"findings.json length {len(raw_items)}"
                )
                raise StopConditionError(msg)
            item = raw_items[entry_idx]
            anchor_ords, span_ords = _resolve_finding_gold_ordinals(
                item, measure_counts[gid]
            )
            if not anchor_ords:
                unlocated_count += 1
                unlocated_rounds.setdefault((gid, rnd), {}).setdefault(
                    cls, Counter()
                )[cat] += 1
            else:
                located_count += 1
                for m_ord in anchor_ords:
                    per_measure_rounds.setdefault(
                        (gid, m_ord, rnd), {}
                    ).setdefault(cls, Counter())[cat] += 1
                if rnd >= 3 and cls == "A" and gid in TEST_GOLD_IDS:  # noqa: PLR2004
                    for m_ord in anchor_ords:
                        survivor_anchors_test.add((gid, m_ord))
                    for m_ord in span_ords:
                        survivor_spans_test.add((gid, m_ord))

    if (
        located_count != 211  # noqa: PLR2004
        or unlocated_count != 14  # noqa: PLR2004
        or len(survivor_anchors_test) != 23  # noqa: PLR2004
        or len(survivor_spans_test) != 25  # noqa: PLR2004
    ):
        msg = (
            f"T01 archive reconciliation mismatch: located={located_count}, "
            f"unlocated={unlocated_count}, "
            f"survivor_anchors={len(survivor_anchors_test)}, "
            f"survivor_spans={len(survivor_spans_test)}"
        )
        raise StopConditionError(msg)

    late_non_a_seq = [late_non_a_by_pr[pr] for pr in sorted(late_piece_rounds)]
    return {
        "total_live_rows": len(live_rows),
        "located_rows": located_count,
        "unlocated_rows": unlocated_count,
        "round_a_ex_burst": dict(sorted(round_a_ex_burst.items())),
        "round_a_all": dict(sorted(round_a_all.items())),
        "late_non_a_per_piece_round": late_non_a_seq,
        "test_survivor_anchor_measures": sorted(survivor_anchors_test),
        "test_survivor_span_measures": sorted(survivor_spans_test),
        "per_measure_rounds": per_measure_rounds,
        "unlocated_rounds": unlocated_rounds,
    }


def _load_t01_folder_entries(fp: Path) -> list[dict[str, object]]:
    data = json.loads(fp.read_text(encoding="utf-8"))
    if isinstance(data, list):
        return [x if isinstance(x, dict) else {} for x in data]
    if not isinstance(data, dict):
        return []
    out: list[dict[str, object]] = []
    # Container key order fixed in docs/evaluation-protocol.md:519-522
    for key in (
        "findings",
        "confirmed_findings",
        "confirmed_differences",
        "changes",
        "rejected_visual_finding",
        "rejected_provisional_finding",
        "finding",
        "withdrawn_intermediate_findings",
        "withdrawn_findings",
        "general_findings",
    ):
        val = data.get(key)
        if isinstance(val, list):
            out.extend(x if isinstance(x, dict) else {} for x in val)
        elif isinstance(val, dict):
            out.append(val)
        elif isinstance(val, str):
            out.append({})
    return out


def _resolve_finding_gold_ordinals(  # noqa: C901, PLR0912
    item: Mapping[str, object], m_max: int
) -> tuple[list[int], list[int]]:
    """Extract ``(anchors, spans)`` excluding ``candidate_measure``."""
    anchors: list[int] = []

    def add_int(val: object) -> None:
        if isinstance(val, int) and 1 <= val <= m_max:
            anchors.append(val)
        elif isinstance(val, str):
            for m in re.findall(r"\d+", val):
                v = int(m)
                if 1 <= v <= m_max:
                    anchors.append(v)

    for key in ("measure", "ordinal", "ordinal_measure", "measure_reference"):
        if key in item and item[key] is not None:
            add_int(item[key])
            break

    if not anchors and "start" in item:
        st = item["start"]
        if isinstance(st, dict) and "measure" in st:
            add_int(st["measure"])
        elif isinstance(st, list) and st:
            add_int(st[0])

    if (
        not anchors
        and "ordinals" in item
        and isinstance(item["ordinals"], list)
    ):
        for v in item["ordinals"]:
            add_int(v)

    if not anchors and "anchor" in item and isinstance(item["anchor"], dict):
        add_int(item["anchor"].get("measure"))

    if not anchors and "anchors" in item and isinstance(item["anchors"], list):
        for a in item["anchors"]:
            if isinstance(a, dict):
                add_int(a.get("measure"))

    dedup_anchors = sorted(set(anchors))
    spans = set(dedup_anchors)
    if dedup_anchors:
        lo = dedup_anchors[0]
        end_v = item.get("measure_end")
        if isinstance(end_v, int) and lo <= end_v <= m_max:
            spans.update(range(lo, end_v + 1))
        aft = item.get("after")
        if isinstance(aft, dict) and isinstance(aft.get("measure"), int):
            hi = int(aft["measure"])
            if lo <= hi <= m_max:
                spans.update(range(lo, hi + 1))
    return dedup_anchors, sorted(spans)


def verify_and_write_printed_system_slices(
    gold_id: str,
    gold_xml_path: Path,
    systems: Sequence[tuple[int, int, int, int, list[int], str]],
    slices_dir: Path,
) -> dict[str, object]:
    """Slice ``gold_xml_path`` at printed-system starts and verify 0 diffs."""
    raw_bytes = gold_xml_path.read_bytes()
    try:
        whole_score = from_musicxml(raw_bytes)
        starts = tuple(
            SystemStart(ordinal=fo - 1, page=pg, system=sy)
            for pg, sy, fo, _, _, _ in systems
        )
        docs = split_score(whole_score, starts)
        piece_dir = slices_dir / gold_id
        if piece_dir.is_symlink():
            msg = f"refusing symlink slice directory {piece_dir.name}"
            raise StopConditionError(msg)
        piece_dir.mkdir(parents=True, exist_ok=True)
        paths: list[Path] = []
        for s_idx, doc in enumerate(docs, start=1):
            out_p = piece_dir / f"s{s_idx:02d}.txt"
            atomic_write_text(out_p, write_document(doc))
            paths.append(out_p)
        parsed_docs = [
            parse_document(decode_source(p.read_bytes())) for p in paths
        ]
        assembled = assemble(parsed_docs)
        if (
            assembled.status is not AssemblyStatus.SUCCESS
            or assembled.score is None
        ):
            msg = (
                f"{gold_id} printed-system assembly failed: {assembled.status}"
            )
            raise StopConditionError(msg)
        forward_xml = to_musicxml(assembled.score).decode("utf-8")
        whole_text = write_document(whole_score)
        reassembled_text = write_document(assembled.score)
        comp = compare_file(forward_xml, gold_xml_path)
    except SplitError as exc:
        msg = f"{gold_id} printed-system split failed: {exc.detail}"
        raise StopConditionError(msg) from exc
    except (
        TextScoreError,
        MusicXmlError,
        ScoreParseError,
        ProtocolDefectError,
    ) as exc:
        msg = f"{gold_id} printed-system slice verification defect: {exc}"
        raise StopConditionError(msg) from exc
    diff_count = comp.category_counts().total()
    if diff_count != 0:
        msg = (
            f"{gold_id} printed-system slice round-trip produced {diff_count} "
            f"content differences: {dict(comp.category_counts())}"
        )
        raise StopConditionError(msg)
    return {
        "gold_id": gold_id,
        "printed_systems": len(parsed_docs),
        "texts_identical": whole_text == reassembled_text,
        "content_differences": 0,
    }


def build_alignment_and_overlays(  # noqa: C901, PLR0912, PLR0913, PLR0915
    *,
    run_dir: Path,
    downloads_dir: Path,
    gold_status_path: Path,
    gold_cache_dir: Path,
    scans_dir: Path,
    immich_dir: Path,
    open_reading_masks_path: Path,
    formal: bool = False,
) -> JsonDict:
    """Build manifest, test-exclusion, page PNGs, overlays and slices."""
    src_st = source_state(run_git)
    if formal and src_st.get("dirty"):
        msg = "project source tree has uncommitted changes during formal align"
        raise StopConditionError(msg)
    venv_dir = downloads_dir / "w2-copista-venv"
    venv_py = venv_python_path(venv_dir)
    lock_stamp = venv_dir / ".copista-wheel-lock-sha256"
    expected_lock_sha = compute_wheel_lock_sha256()
    if (
        venv_dir.is_symlink()
        or not (venv_dir / "pyvenv.cfg").is_file()
        or not venv_py.exists()
        or not lock_stamp.is_file()
        or lock_stamp.read_text(encoding="utf-8").strip() != expected_lock_sha
    ):
        msg = "verified w2-copista-venv missing; run 'setup-copista' first"
        raise StopConditionError(msg)

    gold_pieces = load_gold(gold_status_path, gold_cache_dir)
    if len(gold_pieces) != 23:  # noqa: PLR2004
        msg = f"expected 23 gold pieces, loaded {len(gold_pieces)}"
        raise StopConditionError(msg)

    status_raw = json.loads(gold_status_path.read_text(encoding="utf-8"))
    status_by_ws = {p["workspace"]: p for p in status_raw["pieces"]}
    scans_manifest = json.loads(
        (scans_dir / "manifest.json").read_text(encoding="utf-8")
    )
    ac_raw = json.loads(
        (immich_dir / "aspect-convergence.json").read_text(encoding="utf-8")
    )
    ac_by_ws = {p["workspace"]: p for p in ac_raw["pieces"]}

    pages_by_pdf: dict[str, list[dict[str, object]]] = {}
    for pg in scans_manifest["pages"]:
        pdf_name = Path(str(pg["pdf"]).replace("\\", "/")).name
        pages_by_pdf.setdefault(pdf_name, []).append(pg)

    # Enforce exact 92-page bound BEFORE any cleanup or PDF extraction!
    planned_total_pages = 0
    for gp in gold_pieces:
        st_entry = status_by_ws[gp.workspace]
        pdf_name = Path(str(st_entry["source"]).replace("\\", "/")).name
        p_metas = pages_by_pdf.get(pdf_name, [])
        if not p_metas or not (scans_dir / pdf_name).is_file():
            msg = f"missing scan PDF or page inventory for {gp.id}"
            raise StopConditionError(msg)
        planned_total_pages += len(p_metas)
    if planned_total_pages != EXPECTED_TOTAL_PAGES:
        msg = (
            f"expected {EXPECTED_TOTAL_PAGES} total pages before extraction, "
            f"got {planned_total_pages}"
        )
        raise StopConditionError(msg)

    measure_counts = {
        gp.id: int(status_by_ws[gp.workspace]["entered_ordinals"])
        for gp in gold_pieces
    }
    masks_by_gid, masks_sha256 = load_open_reading_masks(
        open_reading_masks_path, measure_counts
    )
    check_frozen_run_masks(run_dir, downloads_dir, masks_sha256)

    # Validate output/tmp directories (reject symlinks) BEFORE staging!
    tmp_root = run_dir / "tmp"
    pages_out_dir = run_dir / "pages"
    overlays_out_dir = run_dir / "overlays"
    slices_out_dir = run_dir / "slices"
    align_dir = run_dir / "alignment"
    splits_dir = run_dir / "splits"
    for d in (
        tmp_root,
        pages_out_dir,
        overlays_out_dir,
        slices_out_dir,
        align_dir,
        splits_dir,
    ):
        if d.is_symlink():
            msg = f"refusing symlink directory in run_dir: {d.name}"
            raise StopConditionError(msg)
    tmp_root.mkdir(parents=True, exist_ok=True)

    # Validate & extract all 92 PDF pages into fresh mkdtemp BEFORE cleanup!
    staged_pages_dir = Path(
        tempfile.mkdtemp(prefix="staged-pages-", dir=tmp_root)
    )
    pdf_jobs: list[tuple[Path, list[Path]]] = []
    staged_to_final: list[tuple[Path, str]] = []
    for gp in gold_pieces:
        st_entry = status_by_ws[gp.workspace]
        pdf_name = Path(str(st_entry["source"]).replace("\\", "/")).name
        page_metas = pages_by_pdf[pdf_name]
        outs: list[Path] = []
        for p_idx in range(1, len(page_metas) + 1):
            stem = f"{gp.id}-p{p_idx:02d}"
            st_png = staged_pages_dir / f"{stem}.png"
            outs.append(st_png)
            staged_to_final.append((st_png, stem))
        pdf_jobs.append((scans_dir / pdf_name, outs))
    try:
        extract_pdf_pages_via_pdfium(pdf_jobs, venv_py, tmp_root)
        for d in (
            pages_out_dir,
            overlays_out_dir,
            slices_out_dir,
            align_dir,
            splits_dir,
        ):
            if d.is_symlink():
                msg = f"refusing symlink directory in run_dir: {d.name}"
                raise StopConditionError(msg)
            d.mkdir(parents=True, exist_ok=True)
        # Invalidate prior QA record & promote verified staged pages!
        if (align_dir / "overlay-qa.json").is_symlink() or (
            align_dir / "overlay-qa.json"
        ).is_file():
            (align_dir / "overlay-qa.json").unlink(missing_ok=True)
        for st_png, stem in staged_to_final:
            dest_png = pages_out_dir / f"{stem}.png"
            for sidecar in pages_out_dir.glob(f"{stem}.*"):
                if sidecar == dest_png:
                    continue
                if sidecar.is_symlink() or sidecar.is_file():
                    sidecar.unlink(missing_ok=True)
            if dest_png.is_symlink() or (
                dest_png.exists() and not dest_png.is_file()
            ):
                msg = f"invalid destination in pages/: {dest_png.name}"
                raise StopConditionError(msg)
            st_png.replace(dest_png)
    finally:
        shutil.rmtree(staged_pages_dir)

    overrides_p = align_dir / "box-overrides.json"
    box_overrides: dict[str, list[int]] = (
        json.loads(overrides_p.read_text(encoding="utf-8"))
        if overrides_p.exists()
        else {}
    )
    used_overrides: set[str] = set()

    piece_records: list[dict[str, object]] = []
    test_exclusions: list[dict[str, object]] = []
    slice_tasks: list[
        tuple[str, Path, list[tuple[int, int, int, int, list[int], str]]]
    ] = []
    global_page_idx = 0

    for idx, gp in enumerate(gold_pieces, start=1):
        gid = gp.id
        ws_name = gp.workspace
        ws_dir = immich_dir / ws_name
        st_entry = status_by_ws[ws_name]
        pdf_name = Path(str(st_entry["source"]).replace("\\", "/")).name
        pdf_path = scans_dir / pdf_name
        pdf_sha = sha256_file(pdf_path)
        page_metas = pages_by_pdf[pdf_name]
        page_ims = [
            Image.open(pages_out_dir / f"{gid}-p{p_idx:02d}.png").convert("L")
            for p_idx in range(1, len(page_metas) + 1)
        ]

        m_cnt = measure_counts[gid]
        gold_bytes = gp.path.read_bytes()
        xml_sys_cnt = len(system_starts(gold_bytes).starts)
        sys_tuples, map_basis = resolve_piece_system_ordinals(
            idx, ws_dir, gold_bytes, len(page_ims), m_cnt
        )

        # Initial draft file & SHA-256
        b01_path = ws_dir / "build-01" / "score.musicxml"
        drf_path = ws_dir / "draft.musicxml"
        if b01_path.exists():
            init_kind = "build-01"
            init_path = b01_path
        elif drf_path.exists():
            init_kind = "draft.musicxml"
            init_path = drf_path
        else:
            msg = (
                f"{gid}: missing both build-01/score.musicxml & draft.musicxml"
            )
            raise StopConditionError(msg)

        tier, converged = classify_aspect_convergence(ac_by_ws[ws_name])
        split_name = "TEST" if gid in TEST_GOLD_IDS else "TRAIN_DEV"
        group_id = GROUP_BY_INDEX[idx]
        book_cohort = BOOK_COHORT_BY_INDEX[idx]

        # Detect grand-staff boxes per page and render overlays
        sys_by_page: dict[int, list[tuple[int, int, int, list[int], str]]] = {}
        for pg, sy, fo, lo, bars, b_trust in sys_tuples:
            sys_by_page.setdefault(pg, []).append((sy, fo, lo, bars, b_trust))

        page_records: list[dict[str, object]] = []
        system_records: list[dict[str, object]] = []
        piece_sys_idx = 0

        for p_idx, (im, p_meta) in enumerate(
            zip(page_ims, page_metas, strict=True), start=1
        ):
            global_page_idx += 1
            p_sys_list = sys_by_page[p_idx]
            det_boxes = detect_grand_staff_systems(im, len(p_sys_list))

            overlay_items: list[
                tuple[int, tuple[int, int], DetectedSystemBox]
            ] = []
            for (sy, fo, lo, bars, b_trust), raw_det in zip(
                p_sys_list, det_boxes, strict=True
            ):
                piece_sys_idx += 1
                ov_key = f"{gid}-s{piece_sys_idx:02d}"
                if ov_key in box_overrides:
                    used_overrides.add(ov_key)
                    ob = box_overrides[ov_key]
                    if (
                        not isinstance(ob, list)
                        or len(ob) != 4  # noqa: PLR2004
                        or not all(isinstance(v, int) for v in ob)
                        or ob[0] < 0
                        or ob[1] < 0
                        or ob[2] <= ob[0]
                        or ob[3] <= ob[1]
                        or ob[2] > EXPECTED_PAGE_SIZE[0]
                        or ob[3] > EXPECTED_PAGE_SIZE[1]
                    ):
                        msg = f"invalid box override for {ov_key}: {ob!r}"
                        raise StopConditionError(msg)
                    det = replace(raw_det, box_px=(ob[0], ob[1], ob[2], ob[3]))
                    trust_tag = "manual_override_pending_qa"
                else:
                    det = raw_det
                    trust_tag = "pending_overlay_qa"
                overlay_items.append((sy, (fo, lo), det))
                system_records.append(
                    {
                        "system_index": piece_sys_idx,
                        "page_index": p_idx,
                        "page_system_index": sy,
                        "ordinal_range": [fo, lo],
                        "split_measure": False,
                        "facsimile_box_px": list(det.box_px),
                        "staff_lines_y_px": {
                            "treble": list(det.treble_lines_y),
                            "bass": list(det.bass_lines_y),
                        },
                        "staff_space_px": det.staff_space_px,
                        "box_trust": trust_tag,
                        "barline_x_px": bars,
                        "barline_trust": b_trust,
                    }
                )

            ov_path = overlays_out_dir / f"{gid}-p{p_idx:02d}.png"
            render_page_overlay(im, gid, p_idx, overlay_items, ov_path)
            page_records.append(
                {
                    "page_index": p_idx,
                    "global_page_index": global_page_idx,
                    "facsimile_size_px": list(EXPECTED_PAGE_SIZE),
                    "effective_dpi": 303.33,
                    "has_pencil_annotations": bool(
                        p_meta.get("pencil_annotations")
                    ),
                    "quad_norm": p_meta.get("quad"),
                    "raw_crop_status": "absent",
                    "ordinal_range": [
                        p_sys_list[0][1],
                        p_sys_list[-1][2],
                    ],
                    "overlay_qa_status": "pending",
                    "source_sha256": p_meta.get("source_sha256"),
                }
            )

        slice_tasks.append((gid, gp.path, sys_tuples))

        piece_records.append(
            {
                "gold_id": gid,
                "split": split_name,
                "group": group_id,
                "book_cohort": book_cohort,
                "measure_count": m_cnt,
                "page_count": len(page_ims),
                "xml_print_system_count": xml_sys_cnt,
                "printed_system_count": len(system_records),
                "map_basis": map_basis,
                "model_draft_free": True,
                "accepted_xml_path": str(gp.path),
                "accepted_xml_sha256": gp.sha256,
                "initial_draft_kind": init_kind,
                "initial_draft_path": str(init_path),
                "initial_draft_sha256": sha256_file(init_path),
                "label_tier": tier,
                "converged_aspects": converged,
                "open_reading_masks": masks_by_gid[gid],
                "open_reading_masks_sha256": masks_sha256,
                "pages": page_records,
                "systems": system_records,
            }
        )

        if split_name == "TEST":
            test_exclusions.append(
                {
                    "gold_id": gid,
                    "group": group_id,
                    "book_cohort": book_cohort,
                    "measure_count": m_cnt,
                    "page_count": len(page_ims),
                    "printed_system_count": len(system_records),
                    "accepted_xml_sha256": gp.sha256,
                    "facsimile_pdf_sha256": pdf_sha,
                    "page_source_sha256": [
                        p_meta.get("source_sha256") for p_meta in page_metas
                    ],
                }
            )

    unused_ov = sorted(set(box_overrides) - used_overrides)
    if unused_ov:
        msg = f"unknown keys in box-overrides.json: {unused_ov}"
        raise StopConditionError(msg)

    slice_verifications: list[dict[str, object]] = []
    slice_pool = WorkerPool(temp=run_dir / "tmp", jobs=1)
    try:
        for gid_t, gp_path, s_tups in slice_tasks:
            s_out = slice_pool.call(
                verify_and_write_printed_system_slices,
                {
                    "gold_id": gid_t,
                    "gold_xml_path": gp_path,
                    "systems": s_tups,
                    "slices_dir": slices_out_dir,
                },
                context=f"{gid_t}:slice-roundtrip",
                timeout=STEP_SECONDS,
            )
            if not s_out.ok or not isinstance(s_out.value, dict):
                msg = (
                    f"{gid_t} slice round-trip {s_out.status}: "
                    f"{s_out.message or 'exceeded 300s deadline'}"
                )
                raise StopConditionError(msg)
            slice_verifications.append(s_out.value)
    finally:
        slice_pool.close()

    page_digests: list[str] = []
    overlay_digests: list[str] = []
    for p in piece_records:
        gid_s = str(p["gold_id"])
        for pg in p["pages"]:  # type: ignore[attr-defined]
            stem = f"{gid_s}-p{int(pg['page_index']):02d}"  # type: ignore[index]
            pg_sha = sha256_file(pages_out_dir / f"{stem}.png")
            ov_sha = sha256_file(overlays_out_dir / f"{stem}.png")
            pg["page_png_sha256"] = pg_sha  # type: ignore[index]
            pg["overlay_sha256"] = ov_sha  # type: ignore[index]
            page_digests.append(f"{stem}:{pg_sha}")
            overlay_digests.append(f"{stem}:{ov_sha}")
    pages_sha256 = sha256_bytes("\n".join(page_digests).encode("utf-8"))
    overlays_sha256 = sha256_bytes("\n".join(overlay_digests).encode("utf-8"))
    geometry_sha256 = _compute_geometry_sha256(piece_records)

    manifest_obj: JsonDict = {
        "schema": "omr-real-manifest-v1",
        "producer_git_commit": str(src_st["commit"]),
        "producer_code_tree_sha256": str(src_st["tree"]),
        "producer_dirty": bool(src_st.get("dirty")),
        "total_pieces": len(piece_records),
        "total_pages": global_page_idx,
        "total_measures": sum(
            int(str(p["measure_count"])) for p in piece_records
        ),
        "total_printed_systems": sum(
            int(str(p["printed_system_count"])) for p in piece_records
        ),
        "open_reading_masks_sha256": masks_sha256,
        "pages_sha256": pages_sha256,
        "overlays_sha256": overlays_sha256,
        "geometry_sha256": geometry_sha256,
        "slice_roundtrip_verified": len(slice_verifications),
        "pieces": piece_records,
    }
    atomic_write_text(
        align_dir / "manifest.json", json.dumps(manifest_obj, indent=2) + "\n"
    )
    atomic_write_text(
        splits_dir / "test-exclusion.json",
        json.dumps(
            {
                "schema": "omr-test-exclusion-v1",
                "test_piece_count": len(test_exclusions),
                "pieces": test_exclusions,
            },
            indent=2,
        )
        + "\n",
    )
    qa_file = align_dir / "overlay-qa.json"
    if qa_file.is_symlink():
        msg = "refusing symlink overlay-qa.json"
        raise StopConditionError(msg)
    existing_qa = (
        json.loads(qa_file.read_text(encoding="utf-8"))
        if qa_file.is_file()
        else {}
    )
    if (
        existing_qa.get("overlays_sha256") != overlays_sha256
        or existing_qa.get("pages_sha256") != pages_sha256
        or existing_qa.get("geometry_sha256") != geometry_sha256
        or existing_qa.get("open_reading_masks_sha256") != masks_sha256
    ):
        atomic_write_text(
            qa_file,
            json.dumps(
                {
                    "schema": "omr-overlay-qa-v1",
                    "pages_sha256": pages_sha256,
                    "overlays_sha256": overlays_sha256,
                    "geometry_sha256": geometry_sha256,
                    "open_reading_masks_sha256": masks_sha256,
                    "total_pages": global_page_idx,
                    "approved_pages": 0,
                    "pages": {
                        f"{p['gold_id']}-p{pg['page_index']:02d}": "pending"  # type: ignore[index]
                        for p in piece_records
                        for pg in p["pages"]  # type: ignore[attr-defined]
                    },
                },
                indent=2,
            )
            + "\n",
        )
    return manifest_obj


def _validate_manifest_inventory(manifest: Mapping[str, Any]) -> None:  # noqa: C901
    pieces = manifest.get("pieces")
    expected_gids = [f"gold-{i:02d}" for i in range(1, 24)]
    if (
        not isinstance(pieces, list)
        or [str(p.get("gold_id")) for p in pieces] != expected_gids
    ):
        msg = "manifest pieces must equal exact ordered gold-01..gold-23"
        raise StopConditionError(msg)
    total_pgs = 0
    total_syss = 0
    for p in pieces:
        gid_s = str(p.get("gold_id"))
        exp_split = "TEST" if gid_s in TEST_GOLD_IDS else "TRAIN_DEV"
        if p.get("split") != exp_split:
            msg = f"invalid or tampered split {p.get('split')!r} for {gid_s}"
            raise StopConditionError(msg)
        pgs = p.get("pages", [])
        syss = p.get("systems", [])
        p_cnt = int(p.get("page_count", len(pgs)))
        s_cnt = int(p.get("printed_system_count", len(syss)))
        if (
            not isinstance(pgs, list)
            or p_cnt < 1
            or [int(pg.get("page_index", 0)) for pg in pgs]
            != list(range(1, p_cnt + 1))
        ):
            msg = f"invalid page inventory for {p.get('gold_id')}"
            raise StopConditionError(msg)
        if (
            not isinstance(syss, list)
            or len(syss) != s_cnt
            or s_cnt < p_cnt
            or [int(s.get("system_index", 0)) for s in syss]
            != list(range(1, s_cnt + 1))
            or {int(s.get("page_index", 0)) for s in syss}
            != set(range(1, p_cnt + 1))
        ):
            msg = f"invalid system inventory for {p.get('gold_id')}"
            raise StopConditionError(msg)
        if "measure_count" in p:
            m_cnt = int(p["measure_count"])
            prev_end = 0
            for s in syss:
                rng = s.get("ordinal_range", [0, -1])
                fo, lo = int(rng[0]), int(rng[1])
                if fo != prev_end + 1 or lo < fo:
                    msg = f"non-contiguous system ordinals in {p['gold_id']}"
                    raise StopConditionError(msg)
                prev_end = lo
            if prev_end != m_cnt:
                msg = f"system ordinals end at {prev_end} != {m_cnt}"
                raise StopConditionError(msg)
        total_pgs += p_cnt
        total_syss += s_cnt
    exp_syss = manifest.get("total_printed_systems", total_syss)
    if total_pgs != EXPECTED_TOTAL_PAGES or total_syss != int(exp_syss):
        msg = (
            f"expected {EXPECTED_TOTAL_PAGES} pages and reconciled "
            f"{exp_syss} systems, got ({total_pgs}, {total_syss})"
        )
        raise StopConditionError(msg)


def approve_overlays_and_export_crops(  # noqa: C901, PLR0912, PLR0915
    run_dir: Path,
    reviewer_note: str,
    attested_overlays_sha256: str,
    downloads_dir: Path | None = None,
) -> dict[str, object]:
    """Mark all 92 inspected overlays ``approved`` and cut ``14.0 px`` crops."""
    if not reviewer_note or not reviewer_note.strip():
        msg = "approve-overlays requires a non-empty reviewer attestation note"
        raise StopConditionError(msg)
    if not attested_overlays_sha256 or not attested_overlays_sha256.strip():
        msg = "approve-overlays requires non-empty attested_overlays_sha256"
        raise StopConditionError(msg)
    align_dir = run_dir / "alignment"
    manifest_p = align_dir / "manifest.json"
    qa_path = align_dir / "overlay-qa.json"
    if not manifest_p.exists():
        msg = "alignment/manifest.json missing; run 'align' first"
        raise StopConditionError(msg)
    manifest = json.loads(manifest_p.read_text(encoding="utf-8"))
    actual_geom_sha = _compute_geometry_sha256(manifest["pieces"])
    if actual_geom_sha != manifest.get("geometry_sha256"):
        msg = "manifest system geometry edited since align; re-run align first"
        raise StopConditionError(msg)
    actual_masks_sha = _compute_masks_sha256(manifest["pieces"])
    if (
        "open_reading_masks_sha256" in manifest
        and actual_masks_sha != manifest.get("open_reading_masks_sha256")
    ):
        msg = "manifest open-reading masks edited since align; re-run align"
        raise StopConditionError(msg)
    check_frozen_run_masks(run_dir, downloads_dir, actual_masks_sha)
    _validate_manifest_inventory(manifest)
    pages_dir = run_dir / "pages"
    crops_dir = run_dir / "crops" / "facsimile"

    page_digests: list[str] = []
    overlay_digests: list[str] = []
    qa_pages: dict[str, str] = {}
    for piece in manifest["pieces"]:
        gid = piece["gold_id"]
        for pg in piece["pages"]:
            p_idx = int(pg["page_index"])
            key = f"{gid}-p{p_idx:02d}"
            pg_p = pages_dir / f"{key}.png"
            ov_p = run_dir / "overlays" / f"{key}.png"
            if not pg_p.is_file() or not ov_p.is_file():
                msg = f"missing page or overlay image for {key}"
                raise StopConditionError(msg)
            page_digests.append(f"{key}:{sha256_file(pg_p)}")
            overlay_digests.append(f"{key}:{sha256_file(ov_p)}")
            qa_pages[key] = "approved"

    actual_pg_sha = sha256_bytes("\n".join(page_digests).encode("utf-8"))
    actual_ov_sha = sha256_bytes("\n".join(overlay_digests).encode("utf-8"))
    if actual_pg_sha != manifest.get(
        "pages_sha256"
    ) or actual_ov_sha != manifest.get("overlays_sha256"):
        msg = "page or overlay PNG digests changed since align; re-run align"
        raise StopConditionError(msg)
    if attested_overlays_sha256.strip() != actual_ov_sha:
        msg = (
            "attested --overlays-sha256 does not match current 92 overlays "
            f"({actual_ov_sha})"
        )
        raise StopConditionError(msg)

    # Persist 92/92 approval before export, gated on crop_export_completed!
    qa_obj: dict[str, object] = {
        "schema": "omr-overlay-qa-v1",
        "pages_sha256": actual_pg_sha,
        "overlays_sha256": actual_ov_sha,
        "attested_overlays_sha256": attested_overlays_sha256.strip(),
        "geometry_sha256": actual_geom_sha,
        "open_reading_masks_sha256": manifest.get(
            "open_reading_masks_sha256", actual_masks_sha
        ),
        "total_pages": len(qa_pages),
        "approved_pages": len(qa_pages),
        "crop_export_completed": False,
        "reviewer_note": reviewer_note.strip(),
        "pages": qa_pages,
    }
    if (run_dir / "crops").is_symlink() or crops_dir.is_symlink():
        msg = "refusing symlink crops directory"
        raise StopConditionError(msg)
    crop_count = 0
    try:
        atomic_write_text(qa_path, json.dumps(qa_obj, indent=2) + "\n")
        crops_dir.mkdir(parents=True, exist_ok=True)
        for stale_crop in crops_dir.glob("*.png"):
            if stale_crop.is_symlink() or stale_crop.is_file():
                stale_crop.unlink(missing_ok=True)
        for piece in manifest["pieces"]:
            gid = piece["gold_id"]
            page_imgs: dict[int, Image.Image] = {}
            for pg in piece["pages"]:
                p_idx = int(pg["page_index"])
                key = f"{gid}-p{p_idx:02d}"
                pg["overlay_qa_status"] = "approved"
                page_imgs[p_idx] = Image.open(pages_dir / f"{key}.png")

            for sys_rec in piece["systems"]:
                s_idx = int(sys_rec["system_index"])
                p_idx = int(sys_rec["page_index"])
                box = tuple(int(v) for v in sys_rec["facsimile_box_px"])
                sp = float(sys_rec["staff_space_px"])
                out_crop = crops_dir / f"{gid}-s{s_idx:02d}.png"
                cw, ch, scale = export_normalized_crop(
                    page_imgs[p_idx],
                    (box[0], box[1], box[2], box[3]),
                    sp,
                    out_crop,
                )
                sys_rec["box_trust"] = "verified"
                sys_rec["crop_size_px"] = [cw, ch]
                sys_rec["crop_scale"] = scale
                crop_count += 1
        atomic_write_text(manifest_p, json.dumps(manifest, indent=2) + "\n")
        qa_obj["crop_export_completed"] = True
        atomic_write_text(qa_path, json.dumps(qa_obj, indent=2) + "\n")
    except OSError as exc:
        with contextlib.suppress(OSError):
            qa_path.unlink(missing_ok=True)
        msg = f"crop export filesystem failure: {exc}"
        raise StopConditionError(msg) from exc
    except BaseException:
        with contextlib.suppress(OSError):
            qa_path.unlink(missing_ok=True)
        raise
    return {"approved_pages": len(qa_pages), "exported_crops": crop_count}


def verify_approved_overlays(
    run_dir: Path,
) -> tuple[JsonDict, JsonDict]:
    """Verify ``overlay-qa.json``, crops, and page/geometry/mask hashes."""
    align_dir = run_dir / "alignment"
    manifest_p = align_dir / "manifest.json"
    qa_p = align_dir / "overlay-qa.json"
    if not manifest_p.exists() or not qa_p.exists():
        msg = "alignment/manifest.json or overlay-qa.json missing"
        raise StopConditionError(msg)
    manifest: JsonDict = json.loads(manifest_p.read_text(encoding="utf-8"))
    qa: JsonDict = json.loads(qa_p.read_text(encoding="utf-8"))
    _validate_manifest_inventory(manifest)
    actual_geom_sha = _compute_geometry_sha256(manifest["pieces"])
    actual_masks_sha = _compute_masks_sha256(manifest["pieces"])
    pages_map = qa.get("pages", {})
    if (
        not isinstance(pages_map, dict)
        or len(pages_map) != 92  # noqa: PLR2004
        or qa.get("approved_pages") != 92  # noqa: PLR2004
        or qa.get("crop_export_completed") is not True
        or qa.get("pages_sha256") != manifest.get("pages_sha256")
        or qa.get("overlays_sha256") != manifest.get("overlays_sha256")
        or qa.get("geometry_sha256") != manifest.get("geometry_sha256")
        or actual_geom_sha != qa.get("geometry_sha256")
        or not isinstance(manifest.get("open_reading_masks_sha256"), str)
        or actual_masks_sha != manifest.get("open_reading_masks_sha256")
        or qa.get("open_reading_masks_sha256")
        != manifest.get("open_reading_masks_sha256")
        or qa.get("attested_overlays_sha256") != manifest.get("overlays_sha256")
        or not isinstance(qa.get("reviewer_note"), str)
        or not str(qa.get("reviewer_note")).strip()
        or any(v != "approved" for v in pages_map.values())
    ):
        msg = (
            "overlay-qa.json is incomplete or stale for current geometry/masks"
        )
        raise StopConditionError(msg)

    seen_stems: set[str] = set()
    seen_crops: set[str] = set()
    page_digests: list[str] = []
    overlay_digests: list[str] = []
    crops_dir = run_dir / "crops" / "facsimile"
    expected_total_syss = sum(len(p["systems"]) for p in manifest["pieces"])
    for piece in manifest["pieces"]:
        gid = str(piece["gold_id"])
        for pg in piece["pages"]:
            stem = f"{gid}-p{int(pg['page_index']):02d}"
            seen_stems.add(stem)
            pg_p = run_dir / "pages" / f"{stem}.png"
            ov_p = run_dir / "overlays" / f"{stem}.png"
            if not pg_p.is_file() or not ov_p.is_file():
                msg = f"missing page or overlay image for {stem}"
                raise StopConditionError(msg)
            page_digests.append(f"{stem}:{sha256_file(pg_p)}")
            overlay_digests.append(f"{stem}:{sha256_file(ov_p)}")
        for sys_rec in piece["systems"]:
            s_idx = int(sys_rec["system_index"])
            crop_stem = f"{gid}-s{s_idx:02d}"
            crop_p = crops_dir / f"{crop_stem}.png"
            if (
                sys_rec.get("box_trust") != "verified"
                or crop_p.is_symlink()
                or not crop_p.is_file()
            ):
                msg = f"incomplete crop export for {crop_stem}"
                raise StopConditionError(msg)
            seen_crops.add(crop_stem)
    disk_crops = {p.stem for p in crops_dir.glob("*.png") if not p.is_symlink()}
    if (
        seen_stems != set(pages_map)
        or len(seen_crops) != expected_total_syss
        or disk_crops != seen_crops
    ):
        msg = "manifest page/crop inventory does not match reconciled systems"
        raise StopConditionError(msg)
    actual_pg_sha = sha256_bytes("\n".join(page_digests).encode("utf-8"))
    actual_ov_sha = sha256_bytes("\n".join(overlay_digests).encode("utf-8"))
    if actual_pg_sha != qa.get("pages_sha256") or actual_ov_sha != qa.get(
        "overlays_sha256"
    ):
        msg = "on-disk page or overlay PNGs do not match approved hashes"
        raise StopConditionError(msg)
    return manifest, qa
