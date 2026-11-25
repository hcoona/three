"""Blind W5 detector adapters, geometry metrics and assembly validation."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from itertools import pairwise
from typing import TYPE_CHECKING

import numpy as np
from music21 import converter
from PIL import Image

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation.alignment import (
    _compute_geometry_sha256,
    _compute_system_vertical_bounds,
    _find_main_horizontal_span,
    _five_line_comb_score,
    _validate_manifest_inventory,
    sha256_bytes,
    sha256_file,
    venv_python_path,
)
from sheet_music_omr.evaluation.copista import COPISTERIA_COMMIT
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import SystemDocument
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.semantics import validate_document
from sheet_music_omr.synthetic.ledgers import (
    run_offline_isolated_subprocess,
    verify_loopback_only_unshare,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence
    from pathlib import Path

    from sheet_music_omr.diagnostics import Diagnostic
    from sheet_music_omr.synthetic.degrade import Box


type PredictedBox = tuple[float, float, float, float, int]

_W3_DIGESTS = {
    "pages_sha256": (
        "8698d6cc4ad52b28ccb49d55442142a5cb6488a867be69e8c3457985cd3d0b83"
    ),
    "geometry_sha256": (
        "8a7dfe0ec37546c8f9347872dec946aa0c840aee36301f41eaa1ae835969e84e"
    ),
    "overlays_sha256": (
        "69a671f815ddc07470edefab97e61e247d8967ecd8783cedcc9f124835623c90"
    ),
    "test_exclusion_sha256": (
        "65a798c51095c8cd6f37374428e6e9a39cba7954d33fe047b49959f87538a80b"
    ),
}


def _snapshot_page_digests(
    run: Path, manifest: dict, qa: dict, *, include_test_pages: bool
) -> tuple[str, str]:
    page_rows, overlay_rows = [], []
    approved = qa["pages"]
    for piece in manifest["pieces"]:
        gold_id = piece["gold_id"]
        verify_raster = include_test_pages or piece["split"] == "TRAIN_DEV"
        for pg in piece["pages"]:
            stem = f"{gold_id}-p{int(pg['page_index']):02d}"
            if approved.get(stem) != "approved":
                msg = "W5 source page lacks approved W3 geometry"
                raise StopConditionError(msg)
            page_sha, overlay_sha = pg["page_png_sha256"], pg["overlay_sha256"]
            if (
                verify_raster
                and sha256_file(run / "pages" / f"{stem}.png") != page_sha
            ) or (
                piece["split"] == "TRAIN_DEV"
                and sha256_file(run / "overlays" / f"{stem}.png") != overlay_sha
            ):
                msg = "W5 source page or overlay has changed"
                raise StopConditionError(msg)
            page_rows.append(f"{stem}:{page_sha}")
            overlay_rows.append(f"{stem}:{overlay_sha}")
    return (
        sha256_bytes("\n".join(page_rows).encode()),
        sha256_bytes("\n".join(overlay_rows).encode()),
    )


def verify_w3_snapshot_attestation(
    w3_run_dir: Path, *, include_test_pages: bool = False
) -> dict[str, str]:
    """Verify the approved snapshot without reading TEST rasters by default."""
    try:
        align = w3_run_dir / "alignment"
        manifest = json.loads((align / "manifest.json").read_text("utf-8"))
        qa = json.loads((align / "overlay-qa.json").read_text("utf-8"))
        _validate_manifest_inventory(manifest)
        pages, overlays = _snapshot_page_digests(
            w3_run_dir, manifest, qa, include_test_pages=include_test_pages
        )
        geometry = _compute_geometry_sha256(manifest["pieces"])
        exclusion = sha256_file(w3_run_dir / "splits" / "test-exclusion.json")
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
        msg = "W5 source snapshot is missing or malformed"
        raise StopConditionError(msg) from exc
    verified = {
        "pages_sha256": pages,
        "geometry_sha256": geometry,
        "overlays_sha256": overlays,
        "test_exclusion_sha256": exclusion,
    }
    for key, expected in _W3_DIGESTS.items():
        if verified[key] != expected or (
            key != "test_exclusion_sha256"
            and (manifest.get(key) != expected or qa.get(key) != expected)
        ):
            msg = f"W5 source snapshot does not match accepted {key}"
            raise StopConditionError(msg)
    if (
        qa.get("attested_overlays_sha256") != overlays
        or qa.get("crop_export_completed") is not True
    ):
        msg = "W3 overlay approval is missing or incomplete"
        raise StopConditionError(msg)
    return verified


def _thin_ink(arr: np.ndarray) -> np.ndarray:
    thin = np.zeros_like(arr)
    for delta in (2, 3, 4):
        middle = arr[delta:-delta]
        contrast = np.minimum(arr[: -2 * delta], arr[2 * delta :]) - middle
        thin[delta:-delta] = np.maximum(thin[delta:-delta], contrast - 10)
    return thin


def _supported_peaks(
    comb: np.ndarray, spacing: float, height: int
) -> list[tuple[int, float]]:
    if not len(comb):
        return []
    threshold = max(2.2, 0.03 * float(comb.max()))
    distance = max(12, round(5.5 * spacing))
    y_min = round(1.8 * spacing)
    y_max = height - round(5.8 * spacing)
    chosen: list[tuple[int, float]] = []
    for idx in np.argsort(-comb, kind="stable"):
        y = int(idx)
        strength = float(comb[y])
        if strength < threshold:
            break
        if y < y_min or y > y_max:
            continue
        if all(abs(y - old_y) >= distance for old_y, _ in chosen):
            chosen.append((y, strength))
            if len(chosen) >= 16:  # noqa: PLR2004
                break
    return sorted(chosen)


def _staff_peaks(thin: np.ndarray) -> tuple[float, list[tuple[int, float]]]:
    height, width = thin.shape
    edges = np.linspace(0.12 * width, 0.88 * width, 9, dtype=int)
    profiles = [
        np.convolve(thin[:, start:end].mean(axis=1), [0.25, 0.5, 0.25], "same")
        for start, end in pairwise(edges)
    ]
    best: tuple[float, float, list[tuple[int, float]]] = (0.0, 16.5, [])
    for raw_spacing in np.arange(12.0, 24.51, 0.25):
        spacing = float(raw_spacing)
        sloped = [
            _five_line_comb_score(profiles, height, spacing, slope)
            for slope in (0.0, -0.75, 0.75, -1.5, 1.5, -2.25, 2.25)
        ]
        sloped = [c for c in sloped if len(c)]
        if not sloped:
            continue
        min_len = min(len(c) for c in sloped)
        comb = np.maximum.reduce([c[:min_len] for c in sloped])
        peaks = _supported_peaks(comb, spacing, height)
        if len(peaks) < 2:  # noqa: PLR2004
            continue
        top_strengths = sorted((s for _, s in peaks), reverse=True)[:10]
        score = sum(s**1.5 for s in top_strengths)
        if score > best[0]:
            best = (score, spacing, peaks)
    return best[1], best[2]


def _multi_barline_connectivity(
    arr: np.ndarray,
    y_bounds: tuple[int, int, int, int],
    x_span: tuple[int, int],
    spacing: float,
) -> float:
    a, bottom_a, b, bottom_b = y_bounds
    x0, x1 = x_span
    span_inter = arr[bottom_a + 1 : b, x0:x1]
    span_full = arr[max(0, a + 2) : min(arr.shape[0], bottom_b - 1), x0:x1]
    if span_inter.shape[1] < 12 or span_full.shape[0] <= 0:  # noqa: PLR2004
        return 0.0
    sw = span_inter.shape[1]
    cut_s = min(float(np.percentile(span_inter, 85)) - 20.0, 212.0)
    hmin_i = np.minimum.reduce(
        [span_inter[:, k : sw - 4 + k] for k in range(5)]
    )
    hmin_f = np.minimum.reduce([span_full[:, k : sw - 4 + k] for k in range(5)])
    both = np.minimum(
        (hmin_i < cut_s).mean(axis=0), (hmin_f < cut_s).mean(axis=0)
    )
    min_sep = max(8, round(2.0 * spacing))
    bar_xs: list[int] = []
    for idx in np.argsort(-both):
        xi = int(idx)
        if float(both[xi]) < 0.30:  # noqa: PLR2004
            break
        if all(abs(xi - px) >= min_sep for px in bar_xs):
            bar_xs.append(xi)
            if len(bar_xs) >= 2:  # noqa: PLR2004
                return float(both[bar_xs[1]])
    return 0.0


def _pair_weight(
    arr: np.ndarray,
    thin: np.ndarray,
    peak_a: tuple[int, float],
    peak_b: tuple[int, float],
    scale: tuple[float, float],
) -> float:
    max_s, spacing = scale
    a, sa = peak_a
    b, sb = peak_b
    bottom_a = a + round(4 * spacing)
    bottom_b = b + round(4 * spacing)
    gap = (b - bottom_a) / spacing
    if not 1.2 <= gap <= 17.5:  # noqa: PLR2004
        return -math.inf
    x0, x1 = _find_main_horizontal_span(thin, a, b, spacing)
    band_a = thin[max(0, a - 3) : min(thin.shape[0], bottom_a + 4), x0:x1]
    band_b = thin[max(0, b - 3) : min(thin.shape[0], bottom_b + 4), x0:x1]
    if not band_a.size or not band_b.size:
        return -math.inf
    cap_a = max(1.0, float(np.percentile(band_a, 88)))
    cap_b = max(1.0, float(np.percentile(band_b, 88)))
    col_profile = np.maximum(
        np.minimum(band_a, cap_a).mean(axis=0),
        np.minimum(band_b, cap_b).mean(axis=0),
    )
    run_w = min(len(col_profile), max(6, round(0.85 * spacing)))
    starts = (
        np.flatnonzero(
            np.convolve(
                col_profile >= max(1.8, 0.22 * float(col_profile.max())),
                np.ones(run_w, dtype=int),
                mode="valid",
            )
            >= run_w
        )
        if run_w > 0
        else np.zeros(0, dtype=int)
    )
    if not len(starts):
        return -math.inf
    left = x0 + int(starts[0])
    x_lo = max(0, left - round(2.8 * spacing))
    x_hi = min(arr.shape[1], left + round(5.5 * spacing))
    bridge = arr[max(0, a + 2) : min(arr.shape[0], bottom_b - 1), x_lo:x_hi]
    inter = arr[bottom_a + 1 : b, x_lo:x_hi]
    if not bridge.size or not inter.size:
        return -math.inf
    cut = min(float(np.percentile(bridge, 85)) - 22.0, 210.0)
    dark_inter = inter < cut
    dark_full = bridge < cut
    left_conn = max(
        float(
            np.minimum(dark_full.mean(axis=0), dark_inter.mean(axis=0)).max()
        ),
        0.90 * float(dark_inter.any(axis=1).mean()),
    )
    bar2 = _multi_barline_connectivity(
        arr, (a, bottom_a, b, bottom_b), (x0, x1), spacing
    )
    connectivity = max(left_conn, bar2)
    if connectivity < 0.42:  # noqa: PLR2004
        return -math.inf
    na = math.log1p(9.0 * sa / max(max_s, 1e-6)) / math.log(10.0)
    nb = math.log1p(9.0 * sb / max(max_s, 1e-6)) / math.log(10.0)
    gap_pen = 0.018 * max(0.0, abs(gap - 6.5) - 5.0) ** 2
    return 0.65 * (na + nb) + 1.45 * connectivity - 0.55 - gap_pen


def _best_predecessor_pair(
    dp_pair: np.ndarray,
    peaks: Sequence[tuple[int, float]],
    a: int,
    w_ab: float,
    spacing: float,
) -> tuple[float, tuple[int, int] | None]:
    ya = peaks[a][0]
    best_val = w_ab
    best_prev: tuple[int, int] | None = None
    for pa in range(a):
        for pb in range(pa + 1, min(a, pa + 4)):
            if not math.isfinite(dp_pair[pa, pb]):
                continue
            yb_prev = peaks[pb][0] + 4.0 * spacing
            inter_sp = (ya - yb_prev) / spacing
            if inter_sp < 1.8:  # noqa: PLR2004
                continue
            inter_pen = 0.30 * max(0.0, 3.0 - inter_sp) ** 2
            cand_val = float(dp_pair[pa, pb]) + w_ab - inter_pen
            if cand_val > best_val:
                best_val = cand_val
                best_prev = (pa, pb)
    return best_val, best_prev


def _pair_staves(
    arr: np.ndarray,
    thin: np.ndarray,
    peaks: Sequence[tuple[int, float]],
    spacing: float,
) -> list[tuple[int, int]]:
    n = len(peaks)
    if n < 2:  # noqa: PLR2004
        return []
    max_s = max(*(s for _, s in peaks), 1e-6)
    weights = np.full((n, n), -math.inf, dtype=np.float64)
    for a in range(n):
        for b in range(a + 1, min(n, a + 4)):
            weights[a, b] = _pair_weight(
                arr, thin, peaks[a], peaks[b], (max_s, spacing)
            )

    # Non-overlapping DP over candidate pairs with inter-system gap check.
    dp_pair = np.full((n, n), -math.inf, dtype=np.float64)
    prev_pair: dict[tuple[int, int], tuple[int, int] | None] = {}
    best_total = 0.0
    best_end: tuple[int, int] | None = None
    for a in range(n):
        for b in range(a + 1, min(n, a + 4)):
            w_ab = float(weights[a, b])
            if w_ab <= 0.0:
                continue
            best_val, best_prev = _best_predecessor_pair(
                dp_pair, peaks, a, w_ab, spacing
            )
            dp_pair[a, b] = best_val
            prev_pair[(a, b)] = best_prev
            if best_val > best_total:
                best_total = best_val
                best_end = (a, b)

    result: list[tuple[int, int]] = []
    cur = best_end
    while cur is not None:
        result.append((peaks[cur[0]][0], peaks[cur[1]][0]))
        cur = prev_pair[cur]
    return result[::-1]


def detect_grand_staff_systems_blind(
    page_im: Image.Image,
) -> list[PredictedBox]:
    """Locate grand staves from pixels alone using comb and variable-K DP."""
    arr = np.asarray(page_im.convert("L"), dtype=np.float32)
    if min(arr.shape) < 64:  # noqa: PLR2004
        return []
    thin = _thin_ink(arr)
    spacing, peaks = _staff_peaks(thin)
    pairs = _pair_staves(arr, thin, peaks, spacing)
    if not pairs:
        return []
    spans = [_find_main_horizontal_span(thin, a, b, spacing) for a, b in pairs]
    bounds = _compute_system_vertical_bounds(arr, pairs, spans, spacing)
    return [
        (x0, y0, x1, y1, 2)
        for (x0, x1), (y0, y1) in zip(spans, bounds, strict=True)
    ]


_COPISTERIA_WORKER = """
import json
import sys
from pathlib import Path

from PIL import Image
import torch
from copisteria import front, read
from copisteria.pipeline import inputs

page, root = map(Path, sys.argv[1:])
assert Path(front.__file__).resolve().is_relative_to(root)
torch.set_num_threads(1)
model = read.load_model(str(root / 'models' / 'evidence-2m.pt'), device='cpu')
dets, cands, feats = inputs(page, model)
with Image.open(page) as original:
    im = original.convert('L')
layout = front.build(dets, im.width, im.height, image=im, cands=cands)
boxes = []
for system in layout.systems:
    staves = [layout.staves[i] for i in system.staves]
    boxes.append([min(s.x0 for s in staves), min(s.y0 for s in staves),
                  max(s.x1 for s in staves), max(s.y1 for s in staves),
                  len(staves)])
Path('boxes.json').write_text(json.dumps(boxes), encoding='utf-8')
"""


def verify_copisteria_live_assets(downloads_dir: Path) -> str:
    """Verify staged copisteria checkout, wheel lock and model weights."""
    from sheet_music_omr.evaluation.copista import (  # noqa: PLC0415
        COPISTA_WEIGHTS,
        COPISTERIA_ARCHIVE_SHA256,
        _verify_copisteria_archive_and_unpack,
        _verify_venv_imports,
        compute_wheel_lock_sha256,
        copista_provenance_sha256,
    )

    cache_dir = downloads_dir / "w2-copista-cache"
    venv_dir = downloads_dir / "w2-copista-venv"
    prov_path = cache_dir / "provenance.json"
    if not prov_path.is_file():
        msg = "copista provenance.json missing; run setup-copista first"
        raise StopConditionError(msg)
    prov = json.loads(prov_path.read_text(encoding="utf-8"))
    archive_path = cache_dir / f"copisteria-{COPISTERIA_COMMIT}.tar.gz"
    src_root = cache_dir / f"copisteria-{COPISTERIA_COMMIT}"
    venv_py = venv_python_path(venv_dir)
    verified_tree_sha = _verify_copisteria_archive_and_unpack(
        archive_path, cache_dir, src_root, allow_extract=False
    )
    lock_stamp = venv_dir / ".copista-wheel-lock-sha256"
    if (
        prov.get("copisteria_commit") != COPISTERIA_COMMIT
        or prov.get("copisteria_archive_sha256") != COPISTERIA_ARCHIVE_SHA256
        or prov.get("copisteria_src_tree_sha256") != verified_tree_sha
        or venv_dir.is_symlink()
        or not (venv_dir / "pyvenv.cfg").is_file()
        or not lock_stamp.is_file()
        or lock_stamp.read_text(encoding="utf-8").strip()
        != compute_wheel_lock_sha256()
        or not _verify_venv_imports(venv_py, src_root)
    ):
        msg = "copisteria archive, wheel lock or venv failed verification"
        raise StopConditionError(msg)
    models_dir = src_root / "models"
    for rel_path, exp_bytes, exp_sha in COPISTA_WEIGHTS:
        wp = models_dir / rel_path
        if (
            not wp.is_file()
            or wp.stat().st_size != exp_bytes
            or sha256_file(wp) != exp_sha
        ):
            msg = f"copisteria weight verification failed for {rel_path}"
            raise StopConditionError(msg)
    return copista_provenance_sha256(prov)


def run_copisteria_front_detector(
    page_pngs: Sequence[Path], downloads_dir: Path, scratch_root: Path
) -> dict[Path, list[PredictedBox]]:
    """Run the pinned offline front-end on one raw page per isolated worker."""
    if not page_pngs:
        return {}
    verify_loopback_only_unshare()
    python = venv_python_path(downloads_dir / "w2-copista-venv").absolute()
    source = (
        downloads_dir / "w2-copista-cache" / f"copisteria-{COPISTERIA_COMMIT}"
    ).resolve()
    pages: dict[Path, list[PredictedBox]] = {}
    for page in page_pngs:
        records: list[list[PredictedBox]] = []

        def read_boxes(
            scratch: Path, records: list[list[PredictedBox]] = records
        ) -> None:
            result = scratch / "boxes.json"
            if result.is_file():
                boxes = []
                for x0, y0, x1, y1, count in json.loads(
                    result.read_text(encoding="utf-8")
                ):
                    box = (float(x0), float(y0), float(x1), float(y1))
                    _validate_box(box)
                    if not isinstance(count, int) or count <= 0:
                        msg = "invalid Copisteria staff count"
                        raise StopConditionError(msg)
                    boxes.append((*box, count))
                records.append(boxes)

        result = run_offline_isolated_subprocess(
            [
                str(python),
                "-c",
                _COPISTERIA_WORKER,
                "{SCRATCH_PNG}",
                str(source),
            ],
            scratch_root=scratch_root.absolute(),
            input_page_png=page,
            extra_env={
                "PYTHONPATH": str(source / "src"),
                "COPISTERIA_DETECTOR": "28m",
            },
            forbidden_paths=(downloads_dir,),
            on_before_cleanup=read_boxes,
        )
        if result.returncode or not records:
            msg = f"Copisteria layout worker failed ({result.returncode})"
            raise StopConditionError(msg)
        pages[page] = records[0]
    return pages


@dataclass(frozen=True, slots=True)
class HomrPageResult:
    """An external CLI attempt with no usable page-frame registration."""

    boxes: tuple[PredictedBox, ...]
    unscorable_page_frame: bool
    position_row_count: int
    returncode: int


def run_homr_cli_detector(
    page_pngs: Sequence[Path], downloads_dir: Path, scratch_root: Path
) -> dict[Path, HomrPageResult]:
    """Run HOMR offline; never interpret unverified normalized coordinates."""
    if not page_pngs:
        return {}
    verify_loopback_only_unshare()
    executable = venv_python_path(downloads_dir / "w5-homr-venv").with_name(
        "homr"
    )
    pages = {}
    for page in page_pngs:
        rows = 0

        def inspect_output(scratch: Path) -> None:
            nonlocal rows
            positions = scratch / "page.txt"
            if positions.is_file():
                rows = sum(
                    bool(row.strip())
                    for row in positions.read_text(
                        encoding="utf-8"
                    ).splitlines()
                )

        result = run_offline_isolated_subprocess(
            [
                str(executable.absolute()),
                "{SCRATCH_PNG}",
                "--no-title",
                "--write-staff-positions",
            ],
            scratch_root=scratch_root.absolute(),
            input_page_png=page,
            forbidden_paths=(downloads_dir,),
            on_before_cleanup=inspect_output,
        )
        # The pinned CLI publishes neither its crop offset/resized canvas
        # mapping nor grouping IDs. A teaser is not a registration attestation.
        pages[page] = HomrPageResult(
            boxes=(),
            unscorable_page_frame=True,
            position_row_count=rows,
            returncode=result.returncode,
        )
    return pages


def _validate_box(box: Box) -> None:
    x0, y0, x1, y1 = box
    if not all(map(math.isfinite, box)) or x1 <= x0 or y1 <= y0:
        msg = "matching requires finite, positive-area rectangles"
        raise ValueError(msg)


_FULL_IOU = 0.50
_FULL_HORIZONTAL_COVERAGE = 0.85
_FULL_VERTICAL_COVERAGE = 0.70
_ASSOCIATION_CONTAINMENT = 0.60
_INK_THRESHOLD = 128


def _coverage(p: Box, g: Box) -> tuple[bool, bool]:
    x = max(0.0, min(p[2], g[2]) - max(p[0], g[0]))
    y = max(0.0, min(p[3], g[3]) - max(p[1], g[1]))
    pw, ph = p[2] - p[0], p[3] - p[1]
    gw, gh = g[2] - g[0], g[3] - g[1]
    iou = x * y / (pw * ph + gw * gh - x * y)
    fully = (
        iou >= _FULL_IOU
        and x / max(1, gw) >= _FULL_HORIZONTAL_COVERAGE
        and y / max(1, gh) >= _FULL_VERTICAL_COVERAGE
    )
    associates = fully or (
        x / max(1, min(pw, gw)) >= _ASSOCIATION_CONTAINMENT
        and y / max(1, min(ph, gh)) >= _ASSOCIATION_CONTAINMENT
    )
    return fully, associates


def _clips_ink(pred: Box, gold: Box, gray: np.ndarray) -> bool:
    height, width = gray.shape
    x0 = max(0, math.ceil(gold[0] + 4))
    y0 = max(0, math.ceil(gold[1] + 4))
    x1 = min(width, math.floor(gold[2] - 4))
    y1 = min(height, math.floor(gold[3] - 4))
    ink = gray[y0:y1, x0:x1] < _INK_THRESHOLD
    yy, xx = np.nonzero(ink)
    inside = (
        (xx + x0 + 0.5 >= pred[0])
        & (xx + x0 + 0.5 < pred[2])
        & (yy + y0 + 0.5 >= pred[1])
        & (yy + y0 + 0.5 < pred[3])
    )
    return int((~inside).sum()) > 0.001 * len(xx)


@dataclass(frozen=True, slots=True)
class PageMatchReport:
    """One exact five-way gold partition and prediction-side diagnostics."""

    n_gold: int
    n_pred: int
    missed_gold: int
    merged_gold: int
    split_or_truncated_gold: int
    exact_two_staff_match: int
    exact_wrong_staff_count: int
    spurious_fp: int
    merged_preds: int
    ink_clipped_matches: int | None

    @property
    def n_matched(self) -> int:
        """Return the count of full one-to-one matches of either staff count."""
        return self.exact_two_staff_match + self.exact_wrong_staff_count


def summarize_page_matches(rows: Sequence[PageMatchReport]) -> PageMatchReport:
    """Pool page counts for one detector and split or group."""
    clipped = [row.ink_clipped_matches for row in rows]
    return PageMatchReport(
        n_gold=sum(row.n_gold for row in rows),
        n_pred=sum(row.n_pred for row in rows),
        missed_gold=sum(row.missed_gold for row in rows),
        merged_gold=sum(row.merged_gold for row in rows),
        split_or_truncated_gold=sum(
            row.split_or_truncated_gold for row in rows
        ),
        exact_two_staff_match=sum(row.exact_two_staff_match for row in rows),
        exact_wrong_staff_count=sum(
            row.exact_wrong_staff_count for row in rows
        ),
        spurious_fp=sum(row.spurious_fp for row in rows),
        merged_preds=sum(row.merged_preds for row in rows),
        ink_clipped_matches=(
            None
            if None in clipped
            else sum(n for n in clipped if n is not None)
        ),
    )


def match_page_boxes_with_pairs(  # noqa: C901
    pred_boxes: Sequence[PredictedBox],
    gold_boxes: Sequence[Box],
    page_gray: Image.Image | np.ndarray | None = None,
) -> tuple[PageMatchReport, dict[int, int]]:
    """Return W5 counts and exact 1:1 two-staff ``{gold: pred}`` pairs."""
    pred_coords = [(p[0], p[1], p[2], p[3]) for p in pred_boxes]
    for box in (*pred_coords, *gold_boxes):
        _validate_box(box)
    by_gold: list[list[tuple[int, bool]]] = [[] for _ in gold_boxes]
    by_pred = [0] * len(pred_boxes)
    for g_idx, gold in enumerate(gold_boxes):
        for p_idx, pred in enumerate(pred_coords):
            fully, related = _coverage(pred, gold)
            if related:
                by_gold[g_idx].append((p_idx, fully))
                by_pred[p_idx] += 1

    gray = (
        np.asarray(page_gray.convert("L"))
        if isinstance(page_gray, Image.Image)
        else page_gray
    )
    missed = merged = split = two = wrong = clipped = 0
    exact_pairs: dict[int, int] = {}
    for g_idx, (gold, predictions) in enumerate(
        zip(gold_boxes, by_gold, strict=True)
    ):
        if not predictions:
            missed += 1
        elif any(by_pred[p_idx] > 1 for p_idx, _ in predictions):
            merged += 1
        elif len(predictions) > 1 or not predictions[0][1]:
            split += 1
        else:
            p_idx = predictions[0][0]
            if pred_boxes[p_idx][4] == 2:  # noqa: PLR2004
                two += 1
                exact_pairs[g_idx] = p_idx
            else:
                wrong += 1
            if gray is not None:
                clipped += _clips_ink(pred_coords[p_idx], gold, gray)
    return (
        PageMatchReport(
            len(gold_boxes),
            len(pred_boxes),
            missed,
            merged,
            split,
            two,
            wrong,
            by_pred.count(0),
            sum(n > 1 for n in by_pred),
            clipped if gray is not None else None,
        ),
        exact_pairs,
    )


def match_page_boxes(
    pred_boxes: Sequence[PredictedBox],
    gold_boxes: Sequence[Box],
    page_gray: Image.Image | np.ndarray | None = None,
) -> PageMatchReport:
    """Classify each oracle exactly once using the W5 association precedence."""
    return match_page_boxes_with_pairs(pred_boxes, gold_boxes, page_gray)[0]


_TRAIN_DEV_GOLD = 323
_MIN_EXACT = 0.98
_MAX_ERROR = 0.01
_ELIGIBLE = ("comb_dp_blind", "copisteria_front")


@dataclass(frozen=True, slots=True)
class NoTrainDecision:
    """The W5 recommendation and the eligible detectors that meet the gate."""

    recommend_training: bool
    qualifying_detectors: tuple[str, ...]


def _qualifies(row: PageMatchReport) -> bool:
    n_pred = max(1, row.n_pred)
    return (
        row.n_gold == _TRAIN_DEV_GOLD
        and row.exact_two_staff_match / row.n_gold >= _MIN_EXACT
        and row.merged_preds / n_pred <= _MAX_ERROR
        and row.merged_gold / row.n_gold <= _MAX_ERROR
        and row.split_or_truncated_gold / row.n_gold <= _MAX_ERROR
        and row.spurious_fp / n_pred <= _MAX_ERROR
        and row.ink_clipped_matches == 0
    )


def evaluate_no_train_decision(
    train_dev_summary: Mapping[str, PageMatchReport],
) -> NoTrainDecision:
    """Apply the protocol cutoffs only to eligible blind TRAIN_DEV summaries."""
    passing = tuple(
        name
        for name in _ELIGIBLE
        if name in train_dev_summary and _qualifies(train_dev_summary[name])
    )
    return NoTrainDecision(not passing, passing)


@dataclass(frozen=True, slots=True)
class AssemblyValidationDiagnostic:
    """Content-free rule and optional source system/position."""

    code: str
    system_index: int | None = None
    line: int | None = None
    column: int | None = None


@dataclass(frozen=True, slots=True)
class AssemblyValidationReport:
    """Content-free per-piece validation suitable for the W2 G3 gate."""

    status: AssemblyStatus
    expected_sequence: tuple[tuple[int, int], ...]
    observed_sequence: tuple[tuple[int, int], ...]
    diagnostics: tuple[AssemblyValidationDiagnostic, ...] = ()
    schema: str = "omr-assembly-validation-v1"


def _diagnostic(
    rule: Diagnostic, index: int | None
) -> AssemblyValidationDiagnostic:
    return AssemblyValidationDiagnostic(
        rule.code, index, rule.location.line, rule.location.column
    )


def _parse_systems(
    system_texts: Sequence[str],
) -> tuple[list[SystemDocument], list[AssemblyValidationDiagnostic]]:
    docs: list[SystemDocument] = []
    diagnostics: list[AssemblyValidationDiagnostic] = []
    for index, text in enumerate(system_texts, 1):
        try:
            doc = parse_document(text)
        except TextScoreError as exc:
            diagnostics.extend(
                _diagnostic(rule, index) for rule in exc.diagnostics
            )
            continue
        if not isinstance(doc, SystemDocument):
            diagnostics.append(
                AssemblyValidationDiagnostic("not_system", index)
            )
            continue
        docs.append(doc)
        diagnostics.extend(
            _diagnostic(rule, index) for rule in validate_document(doc)
        )
    return docs, diagnostics


def validate_candidate_system_assembly(
    system_texts: Sequence[str], expected_sequence: Sequence[tuple[int, int]]
) -> AssemblyValidationReport:
    """Verify order before assembly and forward MusicXML validation."""
    expected = tuple(expected_sequence)
    docs, diagnostics = _parse_systems(system_texts)
    observed = tuple((doc.page, doc.system) for doc in docs)
    if not docs or observed != expected:
        diagnostics.append(AssemblyValidationDiagnostic("system_sequence"))
    if diagnostics:
        return AssemblyValidationReport(
            AssemblyStatus.FAILED, expected, observed, tuple(diagnostics)
        )

    names = [str(n) for n in range(1, len(docs) + 1)]
    assembly = assemble(docs, names=names)
    if assembly.status is not AssemblyStatus.SUCCESS or assembly.score is None:
        diagnostics.extend(
            _diagnostic(
                src.diagnostic, int(src.source) if src.source in names else None
            )
            for src in assembly.diagnostics
        )
        diagnostics.extend(
            AssemblyValidationDiagnostic("review_marker", int(marker.source))
            for marker in assembly.markers
        )
        diagnostics.extend(
            AssemblyValidationDiagnostic(
                "review_trill_end", int(trill.end.source)
            )
            for trill in assembly.trills
        )
        return AssemblyValidationReport(
            assembly.status, expected, observed, tuple(diagnostics)
        )

    try:
        xml = to_musicxml(assembly.score).decode("utf-8")
        converter.parse(xml, format="musicxml")
    except ReviewRequiredError:
        return AssemblyValidationReport(
            AssemblyStatus.NEEDS_PERSON,
            expected,
            observed,
            (AssemblyValidationDiagnostic("musicxml_review_required"),),
        )
    except Exception:  # noqa: BLE001 - music21 can raise format-specific errors
        return AssemblyValidationReport(
            AssemblyStatus.FAILED,
            expected,
            observed,
            (AssemblyValidationDiagnostic("musicxml_conversion_or_parse"),),
        )
    return AssemblyValidationReport(AssemblyStatus.SUCCESS, expected, observed)
