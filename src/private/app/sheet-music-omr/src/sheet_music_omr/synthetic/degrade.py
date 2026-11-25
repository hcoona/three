"""Seeded Tier R page degradation, label geometry and crop statistics."""

from __future__ import annotations

import io
import math
from dataclasses import dataclass, fields
from typing import TYPE_CHECKING

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

if TYPE_CHECKING:
    from collections.abc import Iterable, Sequence


type Box = tuple[float, float, float, float]
type PixelBox = tuple[int, int, int, int]

_STAFF_LINES = 5
_CLEAN_INK = 128
_DEGRADED_INK = 170
_PAPER_RESIDUAL_LIMIT = 12


@dataclass(frozen=True, slots=True)
class Displacement:
    """The forward clean-to-degraded coordinate map, in page pixels."""

    width: int
    height: int
    amplitude_x: float
    amplitude_y: float
    phase_x: float
    phase_y: float
    skew: float

    def forward(
        self, x: np.ndarray, y: np.ndarray
    ) -> tuple[np.ndarray, np.ndarray]:
        """Map page coordinates using the same field as the image resampler."""
        ax = math.tau * (y / self.height + 0.21 * x / self.width)
        ay = math.tau * (x / self.width + 0.18 * y / self.height)
        return (
            x + self.amplitude_x * np.sin(ax + self.phase_x),
            y
            + self.amplitude_y * np.sin(ay + self.phase_y)
            + self.skew * (x - self.width / 2),
        )


@dataclass(frozen=True, slots=True)
class OverlayCheck:
    """A check of the clean foreground mapped into the degraded labels."""

    missed_systems: int
    merged_systems: int
    clipped_ink_pixels: int

    @property
    def passed(self) -> bool:
        """Return whether every system survives without merging or clipping."""
        return not any(
            (self.missed_systems, self.merged_systems, self.clipped_ink_pixels)
        )


@dataclass(frozen=True, slots=True)
class TierRPage:
    """A degraded page, co-warped boxes and the checked YOLO label rows."""

    image: Image.Image
    system_boxes: tuple[PixelBox, ...]
    measure_boxes: tuple[PixelBox, ...]
    yolo_rows: tuple[str, ...]
    displacement: Displacement
    overlay_check: OverlayCheck

    @property
    def yolo_text(self) -> str:
        """Return newline-terminated YOLO labels."""
        return "".join(f"{row}\n" for row in self.yolo_rows)


def render_warped_overlay(page: TierRPage) -> Image.Image:
    """Draw checked labels on a copy of the degraded page for contact sheets."""
    overlay = page.image.copy()
    draw = ImageDraw.Draw(overlay)
    for box in page.measure_boxes:
        draw.rectangle(box, outline=(0, 100, 220), width=1)
    for number, (x0, y0, x1, y1) in enumerate(page.system_boxes, 1):
        draw.rectangle((x0, y0, x1, y1), outline=(180, 34, 20), width=2)
        draw.text((x0 + 2, y0 + 2), str(number), fill=(180, 34, 20))
    return overlay


def _resample_images(
    images: Sequence[Image.Image], field: Displacement
) -> list[Image.Image]:
    sources = []
    for raw_im in images:
        composited = (
            Image.alpha_composite(
                Image.new("RGBA", raw_im.size, "white"), raw_im.convert("RGBA")
            )
            if "A" in raw_im.getbands() or "transparency" in raw_im.info
            else raw_im
        )
        sources.append(np.asarray(composited.convert("RGB"), dtype=np.float32))
    results = [np.empty(sources[0].shape, dtype=np.uint8) for _ in sources]
    for row in range(0, field.height, 128):
        y, x = np.mgrid[row : min(row + 128, field.height), : field.width]
        xf, yf = x + 0.5, y + 0.5
        sx, sy = xf, yf
        # The field is a contraction at page aspect ratios: invert the forward
        # map rather than approximating the inverse by negating displacement.
        for _ in range(8):
            mx, my = field.forward(sx, sy)
            sx, sy = sx + xf - mx, sy + yf - my
        sx, sy = sx - 0.5, sy - 0.5
        x_int, y_int = np.floor(sx).astype(int), np.floor(sy).astype(int)
        wx, wy = sx - x_int, sy - y_int
        strips = [np.zeros((*x.shape, 3), dtype=np.float32) for _ in sources]
        for ox, w_x in ((0, 1 - wx), (1, wx)):
            for oy, w_y in ((0, 1 - wy), (1, wy)):
                cx, cy = x_int + ox, y_int + oy
                valid = (
                    (cx >= 0)
                    & (cy >= 0)
                    & (cx < field.width)
                    & (cy < field.height)
                )[..., None]
                cy_c = np.clip(cy, 0, field.height - 1)
                cx_c = np.clip(cx, 0, field.width - 1)
                weight = (w_x * w_y)[..., None]
                for source, acc in zip(sources, strips, strict=True):
                    np.add(
                        acc,
                        np.where(valid, source[cy_c, cx_c], 255) * weight,
                        out=acc,
                    )
        for result, strip in zip(results, strips, strict=True):
            result[row : row + len(strip)] = np.rint(strip).astype(np.uint8)
    return [Image.fromarray(result) for result in results]


def _resample(image: Image.Image, field: Displacement) -> Image.Image:
    return _resample_images((image,), field)[0]


def _warp_box(box: Box, field: Displacement) -> PixelBox:
    x0, y0, x1, y1 = box
    if not (0 <= x0 < x1 <= field.width and 0 <= y0 < y1 <= field.height):
        msg = "invalid clean label rectangle"
        raise ValueError(msg)
    x = np.linspace(x0, x1, math.ceil(x1 - x0) * 2 + 1)
    y = np.linspace(y0, y1, math.ceil(y1 - y0) * 2 + 1)
    wx, wy = field.forward(
        np.concatenate([x, x, np.full_like(y, x0), np.full_like(y, x1)]),
        np.concatenate([np.full_like(x, y0), np.full_like(x, y1), y, y]),
    )
    return (
        max(0, math.floor(float(wx.min()))),
        max(0, math.floor(float(wy.min()))),
        min(field.width, math.ceil(float(wx.max()))),
        min(field.height, math.ceil(float(wy.max()))),
    )


def _yolo_row(cls: int, box: PixelBox, width: int, height: int) -> str:
    x0, y0, x1, y1 = box
    if x0 >= x1 or y0 >= y1:
        msg = "a warped label lies entirely outside the page"
        raise ValueError(msg)
    return (
        f"{cls} {(x0 + x1) / (2 * width):.12f} "
        f"{(y0 + y1) / (2 * height):.12f} "
        f"{(x1 - x0) / width:.12f} {(y1 - y0) / height:.12f}"
    )


_DIGITS = (
    ((0.15, 0.28), (0.50, 0.02), (0.43, 0.98)),
    (
        (0.10, 0.26),
        (0.36, 0.03),
        (0.73, 0.15),
        (0.72, 0.40),
        (0.08, 0.95),
        (0.85, 0.91),
    ),
    (
        (0.12, 0.14),
        (0.72, 0.05),
        (0.53, 0.43),
        (0.31, 0.51),
        (0.70, 0.60),
        (0.77, 0.80),
        (0.53, 0.98),
        (0.10, 0.88),
    ),
    (
        (0.52, 0.02),
        (0.07, 0.63),
        (0.86, 0.57),
        (0.56, 0.61),
        (0.61, 0.28),
        (0.56, 0.98),
    ),
    (
        (0.86, 0.06),
        (0.20, 0.08),
        (0.15, 0.47),
        (0.64, 0.46),
        (0.79, 0.71),
        (0.59, 0.97),
        (0.11, 0.87),
    ),
)


def _draw_clutter(image: Image.Image, rng: np.random.Generator) -> None:
    draw = ImageDraw.Draw(image, "RGBA")
    w, h = image.size
    size = max(6.0, min(w, h) * 0.012)
    for _ in range(int(rng.integers(1, 4))):
        x, y = (
            float(rng.uniform(0.08, 0.88) * w),
            float(rng.uniform(0.10, 0.90) * h),
        )
        t = np.linspace(0, 1, 24)
        curvature = rng.uniform(-0.9, 0.9)
        curve = zip(
            x + t * size * 3,
            y + curvature * size * (4 * t * (1 - t)) + 0.3 * t * size,
            strict=True,
        )
        graphite = (70, 63, 61, int(rng.integers(26, 52)))
        line_width = max(1, round(size / 14))
        draw.line(list(curve), fill=graphite, width=line_width)
        digit = _DIGITS[int(rng.integers(len(_DIGITS)))]
        draw.line(
            [
                (x + (3.3 + dx + 0.18 * dy) * size, y + dy * size)
                for dx, dy in digit
            ],
            fill=graphite,
            width=line_width,
        )


def _paper(
    warped: Image.Image,
    mirror_im: Image.Image | None,
    field: Displacement,
    rng: np.random.Generator,
    *,
    warped_mirror: Image.Image | None = None,
) -> Image.Image:
    blurred = warped.filter(ImageFilter.GaussianBlur(rng.uniform(0.55, 1.0)))
    optical = blurred.filter(ImageFilter.UnsharpMask(1.1, 10, 2))
    w, h = warped.size
    x, y = np.linspace(0, 1, w)[None, :], np.linspace(0, 1, h)[:, None]
    paper = 249 + rng.uniform(-7, 5) * x + rng.uniform(-6, 5) * y
    paper = paper + 3 * np.sin(math.tau * (0.62 * x + 0.47 * y))
    margin = x if rng.integers(2) == 0 else 1 - x
    paper -= rng.uniform(6, 16) * np.exp(-margin / rng.uniform(0.015, 0.045))
    show = (
        warped_mirror
        if warped_mirror is not None
        else (
            warped
            if mirror_im is None
            else _resample(
                mirror_im.resize((w, h), Image.Resampling.BICUBIC), field
            )
        )
    )
    ghost = show.transpose(Image.Transpose.FLIP_LEFT_RIGHT).convert("L")
    ghost = ghost.filter(ImageFilter.GaussianBlur(1.8))
    paper -= rng.uniform(0.02, 0.06) * (255 - np.asarray(ghost))
    paper += rng.normal(0.0, rng.uniform(0.4, 1.5), (h, w))
    warm = float(rng.uniform(0, 6))
    tint = np.array([0, -0.37 * warm, -warm])
    rgb = np.asarray(optical, dtype=np.float32) * (
        (paper[..., None] + tint) / 255.0
    )
    return Image.fromarray(np.clip(np.rint(rgb), 0, 255).astype(np.uint8))


def _ink_positions(
    clean: np.ndarray, box: Box, field: Displacement
) -> tuple[np.ndarray, np.ndarray]:
    x0, y0, x1, y1 = (math.ceil(v - 0.5) for v in box)
    yy, xx = np.nonzero(clean[y0:y1, x0:x1] < _CLEAN_INK)
    fx, fy = field.forward(xx + x0 + 0.5, yy + y0 + 0.5)
    visible = (fx >= 0) & (fx < field.width) & (fy >= 0) & (fy < field.height)
    return fx[visible], fy[visible]


def _includes(box: PixelBox, x: np.ndarray, y: np.ndarray) -> np.ndarray:
    x0, y0, x1, y1 = box
    return (x0 <= x) & (x < x1) & (y0 <= y) & (y < y1)


def verify_warped_overlay(
    clean: Image.Image,
    degraded: Image.Image,
    clean_systems: Sequence[Box],
    warped_systems: Sequence[PixelBox],
    field: Displacement,
) -> OverlayCheck:
    """Check mapped ink containment and meaningful warped ink survival."""
    if "A" in clean.getbands() or "transparency" in clean.info:
        paper = Image.new("RGBA", clean.size, "white")
        clean = Image.alpha_composite(paper, clean.convert("RGBA"))
    clean_gray = np.asarray(clean.convert("L"))
    dark = np.asarray(degraded.convert("L")) < _DEGRADED_INK
    claims = [0] * len(warped_systems)
    missed = clipped = 0
    for index, box in enumerate(clean_systems):
        x, y = _ink_positions(clean_gray, box, field)
        if not len(x) or index >= len(warped_systems):
            missed += 1
            continue
        own = _includes(warped_systems[index], x, y)
        clipped += len(x) - int(own.sum())
        has_ink = dark[y.astype(int), x.astype(int)] & own
        min_surviving = max(1, min(16, len(x)), math.ceil(0.25 * len(x)))
        missed += int(has_ink.sum()) < min_surviving
        for pred_index, pred in enumerate(warped_systems):
            if float(_includes(pred, x, y).mean()) >= 0.5:  # noqa: PLR2004
                claims[pred_index] += 1
    return OverlayCheck(
        missed + max(0, len(warped_systems) - len(clean_systems)),
        sum(n > 1 for n in claims),
        clipped,
    )


def apply_tier_r_degradation(  # noqa: PLR0913
    page_im: Image.Image,
    system_boxes: Sequence[Box],
    measure_boxes: Sequence[Box],
    *,
    seed: int,
    enable_clutter: bool = True,
    mirror_im: Image.Image | None = None,
) -> TierRPage:
    """Degrade a page and co-warp its pre-clutter system and measure labels."""
    rng = np.random.default_rng(seed)
    w, h = page_im.size
    ax, ay = rng.uniform(0.0002, 0.0020) * h, rng.uniform(0.0, 0.0014) * h
    px, py = rng.uniform(0, math.tau, 2)
    field = Displacement(
        w, h, ax, ay, px, py, math.tan(math.radians(rng.uniform(-0.10, 0.10)))
    )
    systems = tuple(_warp_box(box, field) for box in system_boxes)
    measures = tuple(_warp_box(box, field) for box in measure_boxes)
    yolo = tuple(_yolo_row(0, b, w, h) for b in systems) + tuple(
        _yolo_row(1, b, w, h) for b in measures
    )
    if mirror_im is None:
        warped = _resample(page_im, field)
        warped_mirror = None
    else:
        warped, warped_mirror = _resample_images(
            (page_im, mirror_im.resize((w, h), Image.Resampling.BICUBIC)),
            field,
        )
    page = _paper(warped, mirror_im, field, rng, warped_mirror=warped_mirror)
    quality = int(rng.integers(78, 96))
    with io.BytesIO() as buffer:
        page.save(buffer, "JPEG", quality=quality)
        buffer.seek(0)
        with Image.open(buffer) as decoded:
            page = decoded.convert("RGB")
    check = verify_warped_overlay(page_im, page, system_boxes, systems, field)
    if not check.passed:
        msg = f"Tier R overlay rejected: {check}"
        raise ValueError(msg)
    if enable_clutter:
        _draw_clutter(page, rng)
    return TierRPage(page, systems, measures, yolo, field, check)


def crop_normalized_system(
    page: Image.Image,
    box: PixelBox,
    local_staff_space_px: float,
    *,
    target_staff_space_px: float = 14.0,
) -> Image.Image:
    """Cut the co-warped system box and rescale both axes by the staff space."""
    if not all(
        math.isfinite(value) and value > 0
        for value in (target_staff_space_px, local_staff_space_px)
    ):
        msg = "staff-space scale must be finite and positive"
        raise ValueError(msg)
    scale = target_staff_space_px / local_staff_space_px
    crop = page.crop(box)
    return crop.resize(
        (max(1, round(crop.width * scale)), max(1, round(crop.height * scale))),
        Image.Resampling.LANCZOS,
    )


@dataclass(frozen=True, slots=True)
class CropStatistics:
    """Objective descriptors measured from one normalized system crop."""

    staff_space_px: float
    staff_line_thickness_px: float
    laplacian_blur_var: float
    bg_noise_sigma: float
    p95_p05_contrast: float
    staff_slope_ptp_px: float


def _staff_comb(arr: np.ndarray) -> tuple[float, list[int]]:
    thin = np.zeros_like(arr)
    for delta in (2, 3, 4):
        thin[delta:-delta] = np.maximum(
            thin[delta:-delta],
            np.minimum(arr[: -2 * delta], arr[2 * delta :]) - arr[delta:-delta],
        )
    profile = thin.mean(axis=1)
    best: tuple[float, float, int] = (0, 0, 0)
    for sp in np.arange(6.0, min(36, len(profile) / 4), 0.25):
        offsets = [round(k * sp) for k in range(_STAFF_LINES)]
        n = len(profile) - offsets[-1]
        score = np.min([profile[o : o + n] for o in offsets], axis=0)
        peak = int(np.argmax(score))
        if score[peak] > best[0]:
            best = float(score[peak]), float(sp), peak
    support, spacing, start = best
    if support <= 0:
        msg = "crop has no measurable five-line staff"
        raise ValueError(msg)
    lines = [start + round(k * spacing) for k in range(_STAFF_LINES)]
    return spacing, lines


def estimate_staff_space(image: Image.Image) -> float:
    """Estimate staff-space pixels from the strongest five-line ink comb."""
    return _staff_comb(np.asarray(image.convert("L"), dtype=np.float32))[0]


def crop_tier_r_systems(
    page: TierRPage, *, target_staff_space_px: float = 14.0
) -> tuple[Image.Image, ...]:
    """Crop in verified label order using each warped system's staff space."""
    return tuple(
        crop_normalized_system(
            page.image,
            box,
            estimate_staff_space(page.image.crop(box)),
            target_staff_space_px=target_staff_space_px,
        )
        for box in page.system_boxes
    )


def compute_crop_statistics(crop: Image.Image) -> CropStatistics:
    """Measure staff spacing, thickness, blur, paper noise, contrast and bow."""
    arr = np.asarray(crop.convert("L"), dtype=np.float32)
    spacing, lines = _staff_comb(arr)
    width = arr.shape[1]
    p05, p95 = np.percentile(arr, [5, 95])
    radius = max(2, round(spacing / 3))
    thickness: list[float] = []
    slopes: list[float] = []
    for line in lines:
        lo, hi = max(0, line - radius), min(len(arr), line + radius + 1)
        strip = arr[lo:hi, width // 10 : width - width // 10]
        local_bg = np.percentile(strip, 85, axis=0)
        depth = local_bg - strip
        center = np.argmax(depth, axis=0)
        peak = depth[center, np.arange(strip.shape[1])]
        supported = peak > 20  # noqa: PLR2004
        if np.any(supported):
            runs = (depth > 0.5 * peak).sum(axis=0)
            thickness.append(float(np.median(runs[supported])))
        offsets = [
            float(np.median(center[idx][supported[idx]]))
            for idx in np.array_split(np.arange(len(center)), 12)
            if np.any(supported[idx])
        ]
        if offsets:
            slopes.append(float(np.ptp(offsets)))
    center = arr[1:-1, 1:-1]
    lap = arr[1:-1, :-2] + arr[1:-1, 2:] + arr[:-2, 1:-1] + arr[2:, 1:-1]
    local = np.asarray(crop.convert("L").filter(ImageFilter.GaussianBlur(3)))
    paper = (arr >= np.percentile(arr, 60)) & (
        np.abs(arr - local) < _PAPER_RESIDUAL_LIMIT
    )
    residual = (arr - local)[paper]
    return CropStatistics(
        spacing,
        float(np.median(thickness)) if thickness else 0.0,
        float(np.var(lap - 4 * center)),
        float(np.std(residual)) if residual.size else 0.0,
        float(p95 - p05),
        float(np.median(slopes)) if slopes else 0.0,
    )


@dataclass(frozen=True, slots=True)
class Percentiles:
    """The p05, median, and p95 of one measured descriptor."""

    p05: float
    median: float
    p95: float


@dataclass(frozen=True, slots=True)
class CropComparison:
    """Synthetic and TRAIN_DEV crop distributions for one descriptor."""

    synthetic: Percentiles
    train_dev: Percentiles
    train_dev_median_in_synthetic_interval: bool


def _percentiles(values: Iterable[float]) -> Percentiles:
    p05, median, p95 = np.percentile(tuple(values), [5, 50, 95])
    return Percentiles(float(p05), float(median), float(p95))


def compare_crop_statistics(
    synthetic: Sequence[CropStatistics], train_dev: Sequence[CropStatistics]
) -> dict[str, CropComparison]:
    """Compare measured distributions; real inputs must come from TRAIN_DEV."""
    if not synthetic or not train_dev:
        msg = "both synthetic and TRAIN_DEV crop sets are required"
        raise ValueError(msg)
    result = {}
    for field in fields(CropStatistics):
        synth = _percentiles(getattr(row, field.name) for row in synthetic)
        real = _percentiles(getattr(row, field.name) for row in train_dev)
        result[field.name] = CropComparison(
            synth, real, synth.p05 <= real.median <= synth.p95
        )
    return result
