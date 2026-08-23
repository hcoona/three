# /// script
# requires-python = "==3.12.10"
# dependencies = [
#   "numpy==2.2.6",
#   "opencv-python-headless==4.12.0.88",
#   "pillow==12.3.0",
# ]
# ///

from __future__ import annotations

import argparse
import hashlib
import io
import json
import ntpath
import os
import re
import secrets
import shutil
import stat
import sys
import zlib
from pathlib import Path
from typing import NamedTuple

import cv2
import numpy as np
from PIL import Image, ImageOps, UnidentifiedImageError


EXTENSIONS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"}
PREMULTIPLIED_RGBA_MODE = "RGB" + "a"
MINIMUM_RESIDUAL_IMPROVEMENT_DEGREES = 0.02
MINIMUM_RESIDUAL_IMPROVEMENT_RATIO = 0.10
INDEPENDENT_AXIS_EPSILON_DEGREES = 1e-6
MINIMUM_CONVERGENCE_DIFFERENTIAL_DEGREES = 0.20
APPLICATION_THRESHOLD_DEGREES = 0.20
MAX_FINAL_HORIZONTAL_RESIDUAL_DEGREES = 0.04
MAX_FINAL_CONVERGENCE_RESIDUAL_DEGREES = 0.20
MAX_TRANSFORMED_OUT_INK_RATIO = 0.0005
MAX_CANDIDATE_INK_LOSS_RATIO = 0.005
MINIMUM_HORIZONTAL_Y_BANDS = 3
HORIZONTAL_Y_BAND_COUNT = 8
MINIMUM_HORIZONTAL_Y_SPREAD = 0.20
MINIMUM_HORIZONTAL_SPLIT_STRUCTURES = 5
MINIMUM_HORIZONTAL_SPLIT_Y_BANDS = 2
MINIMUM_HORIZONTAL_CONSENSUS_RATIO = 0.70
HORIZONTAL_CLUSTER_POSITION_TOLERANCE = 0.012
HORIZONTAL_CLUSTER_ANGLE_TOLERANCE_DEGREES = 0.20
HORIZONTAL_CLUSTER_X_GAP_RATIO = 0.04
STAFF_REFERENCE_X_TOLERANCE_RATIO = 0.015
HORIZONTAL_INLIER_TOLERANCE_DEGREES = 0.20
HORIZONTAL_CONFLICT_MINIMUM_RATIO = 0.30
HORIZONTAL_CONFLICT_SEPARATION_DEGREES = 0.35
HORIZONTAL_TRACK_POSITION_TOLERANCE = 0.025
HORIZONTAL_TRACK_X_TOLERANCE_RATIO = 0.04
HORIZONTAL_TRACK_LENGTH_RATIO = 0.40
HORIZONTAL_TRACK_AMBIGUITY_MARGIN = 0.002
VERTICAL_TRACK_POSITION_TOLERANCE = 0.025
VERTICAL_CONFLICT_MINIMUM_RATIO = 0.30
VERTICAL_CONFLICT_SEPARATION_DEGREES = 0.35
VERTICAL_TRACK_ANGLE_TOLERANCE_DEGREES = VERTICAL_CONFLICT_SEPARATION_DEGREES
VERTICAL_TRACK_MINIMUM_Y_OVERLAP_RATIO = 0.40
MINIMUM_VERTICAL_STRUCTURES = 10
MINIMUM_VERTICAL_STRUCTURES_PER_SIDE = 4
MINIMUM_VERTICAL_SIDE_BALANCE = 0.50
MINIMUM_VERTICAL_SIDE_SPREAD = 0.08
VERTICAL_X_BAND_COUNT = 8
MINIMUM_VERTICAL_X_BANDS_PER_SIDE = 2
MINIMUM_VERTICAL_CONSENSUS_RATIO = 0.70
VERTICAL_MODEL_INLIER_TOLERANCE_DEGREES = 0.35
MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES = 0.15
VERTICAL_CLUSTER_POSITION_TOLERANCE = 0.015
VERTICAL_CLUSTER_ANGLE_UNCERTAINTY_DEGREES = 0.35
VERTICAL_CLUSTER_ANGLE_SUPPORT_DEGREES = 2.0
MAXIMUM_VERTICAL_FRAGMENT_ANGLE_DEGREES = 8.0
VERTICAL_CLUSTER_Y_GAP_RATIO = 0.030
MINIMUM_VERTICAL_FRAGMENT_LENGTH_RATIO = 0.004
MINIMUM_VERTICAL_FIT_STRUCTURES = 6
MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE = 2
VERTICAL_EXCLUSION_SAFETY_PIXELS = 2.0
MINIMUM_VERTICAL_LONG_REFERENCE_LENGTH_RATIO = 0.12
MINIMUM_VERTICAL_STAFF_INTERSECTIONS = 5
MAXIMUM_STAFF_SPACING_RATIO = 2.25
MINIMUM_VERTICAL_INK_CONTINUITY = 0.60
NOTEHEAD_MINIMUM_LATERAL_EXTENT_PIXELS = 7.0
NOTEHEAD_MINIMUM_ROWS = 3
MINIMUM_VERTICAL_SIDE_Y_SPREAD = 0.18
MINIMUM_VERTICAL_SIDE_Y_BANDS = 2
VERTICAL_Y_BAND_COUNT = 8
FOREGROUND_BACKGROUND_TILE_DIVISOR = 32
FOREGROUND_MINIMUM_TILE_SIZE = 32
FOREGROUND_MAXIMUM_TILE_SIZE = 160
FOREGROUND_BACKGROUND_PERCENTILE = 90
FOREGROUND_MINIMUM_CONTRAST = 10.0
FOREGROUND_MAXIMUM_NOISE_THRESHOLD = 30.0
FOREGROUND_NOISE_MULTIPLIER = 3.0
MAX_ENCODED_BYTES = 512 * 1024 * 1024
MAX_DECODED_PIXELS = 80_000_000
MAX_WORKING_MEMORY_BYTES = 4 * 1024 * 1024 * 1024
WORKING_MEMORY_BYTES_PER_PIXEL = 128
OPENCV_MAXIMUM_SAFE_DIMENSION = 32766


def validate_image_limits(
    width: int, height: int, mode: str, path: Path
) -> None:
    if mode == "F":
        raise ValueError(f"floating-point images are unsupported: {path}")
    if width <= 0 or height <= 0:
        raise ValueError(f"image dimensions must be positive: {path}")
    pixels = width * height
    if pixels > MAX_DECODED_PIXELS:
        raise ValueError(
            f"decoded pixel limit exceeded ({pixels} > {MAX_DECODED_PIXELS}): {path}"
        )
    working_bytes = pixels * WORKING_MEMORY_BYTES_PER_PIXEL
    if working_bytes > MAX_WORKING_MEMORY_BYTES:
        raise ValueError(
            "estimated working-memory limit exceeded "
            f"({working_bytes} > {MAX_WORKING_MEMORY_BYTES} bytes): {path}"
        )


def opencv_dimension_reason(image: np.ndarray) -> str | None:
    height, width = image.shape
    if max(width, height) > OPENCV_MAXIMUM_SAFE_DIMENSION:
        return (
            "opencv_dimension_limit_exceeded:"
            f"{width}x{height}_requires_each_dimension_below_32767"
        )
    return None


def png_transparency_metadata(
    content: bytes, path: Path
) -> tuple[int, int, int | None] | None:
    if not content.startswith(b"\x89PNG\r\n\x1a\n"):
        return None
    offset = 8
    bit_depth: int | None = None
    color_type: int | None = None
    transparent_sample: int | None = None
    saw_trns = False
    saw_idat = False
    saw_iend = False
    while offset < len(content):
        if len(content) - offset < 12:
            raise ValueError(f"malformed PNG chunk framing: {path}")
        length = int.from_bytes(content[offset : offset + 4], "big")
        chunk_type = content[offset + 4 : offset + 8]
        data_start = offset + 8
        data_end = data_start + length
        chunk_end = data_end + 4
        if chunk_end > len(content):
            raise ValueError(f"malformed PNG chunk length: {path}")
        chunk_data = content[data_start:data_end]
        expected_crc = int.from_bytes(content[data_end:chunk_end], "big")
        actual_crc = zlib.crc32(chunk_type)
        actual_crc = zlib.crc32(chunk_data, actual_crc) & 0xFFFFFFFF
        if actual_crc != expected_crc:
            raise ValueError(f"PNG chunk CRC mismatch: {path}")
        if chunk_type == b"IHDR":
            if bit_depth is not None or offset != 8 or length != 13:
                raise ValueError(f"invalid PNG IHDR: {path}")
            bit_depth = chunk_data[8]
            color_type = chunk_data[9]
        elif chunk_type == b"tRNS":
            if saw_trns or saw_idat:
                raise ValueError(f"invalid PNG tRNS placement: {path}")
            saw_trns = True
            if bit_depth == 16 and color_type == 0:
                if length != 2:
                    raise ValueError(f"invalid 16-bit grayscale PNG tRNS: {path}")
                transparent_sample = int.from_bytes(chunk_data, "big")
        elif chunk_type == b"IDAT":
            saw_idat = True
        elif chunk_type == b"IEND":
            if length != 0 or chunk_end != len(content):
                raise ValueError(f"invalid PNG IEND: {path}")
            saw_iend = True
            break
        offset = chunk_end
    if bit_depth is None or color_type is None or not saw_idat or not saw_iend:
        raise ValueError(f"incomplete PNG structure: {path}")
    return bit_depth, color_type, transparent_sample


def normalized_tiff_decode_buffer(
    content: bytes, path: Path
) -> tuple[bytes, bool]:
    if content[:4] not in {b"II*\0", b"MM\0*"}:
        return content, False
    byte_order = "little" if content[:2] == b"II" else "big"

    def unsigned(offset: int, size: int) -> int:
        if offset < 0 or offset + size > len(content):
            raise ValueError(f"malformed TIFF metadata offsets: {path}")
        return int.from_bytes(content[offset : offset + size], byte_order)

    ifd_offset = unsigned(4, 4)
    entry_count = unsigned(ifd_offset, 2)
    entries_end = ifd_offset + 2 + entry_count * 12
    if entries_end + 4 > len(content):
        raise ValueError(f"malformed TIFF IFD: {path}")

    tags: dict[int, tuple[tuple[int, ...], int, int]] = {}
    type_sizes = {3: 2, 4: 4}
    for index in range(entry_count):
        entry_offset = ifd_offset + 2 + index * 12
        tag = unsigned(entry_offset, 2)
        field_type = unsigned(entry_offset + 2, 2)
        count = unsigned(entry_offset + 4, 4)
        if tag not in {258, 262, 277, 338, 339}:
            continue
        item_size = type_sizes.get(field_type)
        if item_size is None or count > 16:
            raise ValueError(f"unsupported TIFF metadata encoding: {path}")
        byte_count = count * item_size
        value_offset = (
            entry_offset + 8
            if byte_count <= 4
            else unsigned(entry_offset + 8, 4)
        )
        tag_values = tuple(
            unsigned(value_offset + item * item_size, item_size)
            for item in range(count)
        )
        tags[tag] = (tag_values, field_type, value_offset)

    bits_per_sample = tags.get(258, ((), 0, 0))[0]
    photometric = tags.get(262, ((), 0, 0))[0]
    if bits_per_sample != (16,) or photometric != (0,):
        return content, False
    samples_per_pixel = tags.get(277, ((1,), 0, 0))[0]
    sample_format = tags.get(339, ((1,), 0, 0))[0]
    extra_samples = tags.get(338, ((), 0, 0))[0]
    photometric_type = tags[262][1]
    if (
        samples_per_pixel != (1,)
        or sample_format != (1,)
        or extra_samples
        or photometric_type != 3
    ):
        raise ValueError(
            "unsupported unsigned 16-bit TIFF grayscale layout: "
            f"{path}"
        )

    normalized = bytearray(content)
    photometric_offset = tags[262][2]
    normalized[photometric_offset : photometric_offset + 2] = (
        1
    ).to_bytes(2, byte_order)
    return bytes(normalized), True


def composite_to_grayscale(
    image: Image.Image,
    path: Path,
    png_metadata: tuple[int, int, int | None] | None = None,
    invert_uint16: bool = False,
) -> np.ndarray:
    if image.mode == "F":
        raise ValueError(f"floating-point images are unsupported: {path}")
    if png_metadata is not None:
        bit_depth, color_type, transparent_sample = png_metadata
        has_native_transparency = color_type in {4, 6} or (
            image.has_transparency_data
        )
        if bit_depth == 16 and color_type == 0 and transparent_sample is not None:
            values = np.asarray(image)
            if values.size and (values.min() < 0 or values.max() > 65535):
                raise ValueError(f"unsupported grayscale sample depth in {path}")
            values = values.astype(np.uint16, copy=True)
            values[values == transparent_sample] = np.uint16(65535)
            return values
        if bit_depth == 16 and has_native_transparency:
            raise ValueError(
                "unsupported 16-bit PNG transparency; exact uint16 "
                f"compositing cannot be guaranteed: {path}"
            )
    if image.has_transparency_data:
        if image.mode in {PREMULTIPLIED_RGBA_MODE, "La", "PA"}:
            raise ValueError(f"unsupported premultiplied alpha mode {image.mode}: {path}")
        rgba = image.convert("RGBA")
        white = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        grayscale = Image.alpha_composite(white, rgba).convert("L")
        return np.array(grayscale, dtype=np.uint8, copy=True)
    if image.mode.startswith("I;16"):
        values = np.asarray(image, dtype=np.uint16).copy()
        if invert_uint16:
            np.bitwise_not(values, out=values)
        return values
    if image.mode == "I":
        values = np.asarray(image)
        if values.size and (values.min() < 0 or values.max() > 65535):
            raise ValueError(f"unsupported grayscale sample depth in {path}")
        values = values.astype(np.uint16)
        if invert_uint16:
            np.bitwise_not(values, out=values)
        return values
    if invert_uint16:
        raise ValueError(f"unsupported unsigned 16-bit TIFF decoder mode: {path}")
    return np.array(image.convert("L"), dtype=np.uint8, copy=True)


def tiff_uint16_white_is_zero(image: Image.Image, path: Path) -> bool:
    if image.format != "TIFF":
        return False

    def values(tag: int) -> tuple[int, ...]:
        value = image.tag_v2.get(tag)
        if value is None:
            return ()
        if isinstance(value, tuple):
            return tuple(int(item) for item in value)
        return (int(value),)

    bits_per_sample = values(258)
    if 16 not in bits_per_sample:
        return False
    samples_per_pixel = values(277) or (1,)
    sample_format = values(339) or (1,)
    photometric = values(262)
    extra_samples = values(338)
    if (
        bits_per_sample != (16,)
        or samples_per_pixel != (1,)
        or sample_format != (1,)
        or extra_samples
        or photometric not in {(0,), (1,)}
        or (not image.mode.startswith("I;16") and image.mode != "I")
    ):
        raise ValueError(
            "unsupported unsigned 16-bit TIFF grayscale layout: "
            f"{path}"
        )
    return photometric == (0,)


def analysis_grayscale(image: np.ndarray) -> np.ndarray:
    if image.dtype == np.uint8:
        return image
    if image.dtype == np.uint16:
        return ((image.astype(np.uint32) + 128) // 257).astype(np.uint8)
    raise ValueError(f"unsupported grayscale dtype: {image.dtype}")


def white_value(image: np.ndarray) -> int:
    return int(np.iinfo(image.dtype).max)


def decode_single_frame(content: bytes, path: Path) -> np.ndarray:
    png_metadata = png_transparency_metadata(content, path)
    decode_content, normalized_white_is_zero = normalized_tiff_decode_buffer(
        content, path
    )
    try:
        with Image.open(io.BytesIO(decode_content)) as source:
            frame_count = getattr(source, "n_frames", 1)
            if frame_count != 1:
                raise ValueError(
                    "input must contain exactly one decodable frame "
                    f"(found {frame_count}): {path}"
                )
            validate_image_limits(source.width, source.height, source.mode, path)
            source.seek(0)
            invert_uint16 = (
                normalized_white_is_zero
                or tiff_uint16_white_is_zero(source, path)
            )
            oriented = ImageOps.exif_transpose(source)
            oriented.load()
    except (UnidentifiedImageError, OSError, SyntaxError) as error:
        raise ValueError(f"cannot inspect or decode image: {path}") from error

    return composite_to_grayscale(
        oriented,
        path,
        png_metadata,
        invert_uint16=invert_uint16,
    )


def read_page(path: Path) -> tuple[np.ndarray, str]:
    try:
        encoded_size = path.stat().st_size
        if encoded_size > MAX_ENCODED_BYTES:
            raise ValueError(
                "encoded byte limit exceeded "
                f"({encoded_size} > {MAX_ENCODED_BYTES} bytes): {path}"
            )
        with path.open("rb") as source:
            content = source.read(encoded_size + 1)
    except OSError as error:
        raise ValueError(f"cannot read image: {path}") from error
    if len(content) != encoded_size:
        raise ValueError(f"image size changed while reading: {path}")
    digest = hashlib.sha256(content).hexdigest()
    return decode_single_frame(content, path), digest


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as source:
            for block in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as error:
        raise ValueError(f"cannot rehash input image: {path}") from error
    return digest.hexdigest()


def write_png(path: Path, image: np.ndarray) -> None:
    ok, encoded = cv2.imencode(".png", image, [cv2.IMWRITE_PNG_COMPRESSION, 9])
    if not ok:
        raise ValueError(f"cannot encode image: {path}")
    write_bytes(path, encoded.tobytes())


def write_bytes(path: Path, content: bytes) -> None:
    with path.open("xb") as output:
        output.write(content)


def materially_improved(before: float, after: float) -> bool:
    return (
        before - after >= MINIMUM_RESIDUAL_IMPROVEMENT_DEGREES
        and after <= before * (1.0 - MINIMUM_RESIDUAL_IMPROVEMENT_RATIO)
    )


def foreground_mask(image: np.ndarray) -> np.ndarray:
    image = analysis_grayscale(image)
    height, width = image.shape
    tile_size = max(
        FOREGROUND_MINIMUM_TILE_SIZE,
        min(
            FOREGROUND_MAXIMUM_TILE_SIZE,
            round(min(height, width) / FOREGROUND_BACKGROUND_TILE_DIVISOR),
        ),
    )
    rows = (height + tile_size - 1) // tile_size
    columns = (width + tile_size - 1) // tile_size
    background_tiles = np.empty((rows, columns), np.float32)
    for row in range(rows):
        top = row * tile_size
        bottom = min(height, top + tile_size)
        for column in range(columns):
            left = column * tile_size
            right = min(width, left + tile_size)
            background_tiles[row, column] = np.percentile(
                image[top:bottom, left:right],
                FOREGROUND_BACKGROUND_PERCENTILE,
            )
    background = cv2.resize(
        background_tiles,
        (width, height),
        interpolation=cv2.INTER_CUBIC,
    )
    background = cv2.GaussianBlur(
        background,
        (0, 0),
        max(1.0, tile_size / 4),
    )
    darkness = background - image.astype(np.float32)
    median = float(np.median(darkness))
    noise = float(np.median(np.abs(darkness - median)) * 1.4826)
    threshold = max(
        FOREGROUND_MINIMUM_CONTRAST,
        min(
            FOREGROUND_MAXIMUM_NOISE_THRESHOLD,
            median + FOREGROUND_NOISE_MULTIPLIER * noise,
        ),
    )
    foreground = (darkness >= threshold).astype(np.uint8)

    component_count, labels, statistics, _ = cv2.connectedComponentsWithStats(
        foreground,
        connectivity=8,
    )
    if component_count > 1:
        areas = statistics[:, cv2.CC_STAT_AREA]
        speckles = np.flatnonzero((areas <= 2) & (np.arange(component_count) != 0))
        if len(speckles):
            foreground[np.isin(labels, speckles)] = 0
    return foreground.astype(bool)


def ink_mask(image: np.ndarray) -> np.ndarray:
    return foreground_mask(image)


def clipping_metrics(
    source: np.ndarray,
    candidate: np.ndarray,
    source_to_destination: np.ndarray | None = None,
    forward_x: np.ndarray | None = None,
    forward_y: np.ndarray | None = None,
) -> tuple[bool, float, float]:
    source_ink = ink_mask(source)
    source_count = int(np.count_nonzero(source_ink))
    if source_count == 0:
        return False, 0.0, 0.0
    candidate_ink = ink_mask(candidate)
    candidate_count = int(np.count_nonzero(candidate_ink))
    global_ink_loss_ratio = max(
        0.0, (source_count - candidate_count) / source_count
    )
    transformed_out = 0
    spatially_unsupported = 0
    height, width = source.shape
    destination_x: np.ndarray
    destination_y: np.ndarray
    y, x = np.nonzero(source_ink)
    if source_to_destination is not None:
        destination_x = (
            source_to_destination[0, 0] * x
            + source_to_destination[0, 1] * y
            + source_to_destination[0, 2]
        )
        destination_y = (
            source_to_destination[1, 0] * x
            + source_to_destination[1, 1] * y
            + source_to_destination[1, 2]
        )
    elif forward_x is not None and forward_y is not None:
        if forward_x.shape != source.shape or forward_y.shape != source.shape:
            raise ValueError("forward clipping maps must match the source image")
        destination_x = forward_x[source_ink]
        destination_y = forward_y[source_ink]
    else:
        raise ValueError("source-to-output preservation map is required")

    in_bounds = (
        np.isfinite(destination_x)
        & np.isfinite(destination_y)
        & (destination_x >= 0)
        & (destination_x <= width - 1)
        & (destination_y >= 0)
        & (destination_y <= height - 1)
    )
    transformed_out = int(np.count_nonzero(~in_bounds))
    if np.any(in_bounds):
        coverage = cv2.dilate(
            candidate_ink.astype(np.uint8),
            np.ones((3, 3), np.uint8),
        ).astype(bool)
        sample_x = np.rint(destination_x[in_bounds]).astype(np.intp)
        sample_y = np.rint(destination_y[in_bounds]).astype(np.intp)
        spatially_unsupported = transformed_out + int(
            np.count_nonzero(~coverage[sample_y, sample_x])
        )
    else:
        spatially_unsupported = source_count
    transformed_out_ratio = transformed_out / source_count
    spatial_ink_loss_ratio = spatially_unsupported / source_count
    ink_loss_ratio = max(global_ink_loss_ratio, spatial_ink_loss_ratio)
    clipped = (
        transformed_out_ratio > MAX_TRANSFORMED_OUT_INK_RATIO
        or ink_loss_ratio > MAX_CANDIDATE_INK_LOSS_RATIO
    )
    return clipped, transformed_out_ratio, ink_loss_ratio


def vertical_forward_points(
    source_x: np.ndarray,
    source_y: np.ndarray,
    width: int,
    height: int,
    convergence_slope: float,
) -> tuple[np.ndarray, np.ndarray]:
    destination_x = source_x.astype(np.float64, copy=True)
    y_offset = source_y.astype(np.float64, copy=False) - (height - 1) / 2
    radians_per_x = np.radians(convergence_slope) / (width - 1)
    for _ in range(8):
        angle = np.radians(
            convergence_slope * (destination_x / (width - 1) - 0.5)
        )
        tangent = np.tan(angle)
        residual = destination_x + y_offset * tangent - source_x
        derivative = 1.0 + y_offset * (1.0 + tangent * tangent) * radians_per_x
        destination_x -= residual / derivative
    return destination_x.astype(np.float32), source_y.astype(np.float32, copy=True)


def weighted_median(values: np.ndarray, weights: np.ndarray) -> float:
    order = np.argsort(values)
    values = values[order]
    weights = weights[order]
    return float(values[np.searchsorted(np.cumsum(weights), weights.sum() / 2)])


def weighted_quantile(
    values: np.ndarray, weights: np.ndarray, quantile: float
) -> float:
    order = np.argsort(values)
    values = values[order]
    weights = weights[order]
    return float(
        values[
            np.searchsorted(
                np.cumsum(weights),
                float(weights.sum()) * quantile,
            )
        ]
    )


def weighted_linear_fit(
    x: np.ndarray, values: np.ndarray, weights: np.ndarray
) -> np.ndarray:
    total = float(weights.sum())
    mean_x = float(np.dot(x, weights) / total)
    mean_value = float(np.dot(values, weights) / total)
    centered_x = x - mean_x
    denominator = float(np.dot(weights, centered_x * centered_x))
    slope = (
        float(np.dot(weights, centered_x * (values - mean_value)) / denominator)
        if denominator > 1e-12
        else 0.0
    )
    return np.asarray((mean_value - slope * mean_x, slope))


def _vertical_model_candidates(
    angles: np.ndarray, positions: np.ndarray
) -> list[np.ndarray]:
    x = positions - 0.5
    candidates = [np.asarray((float(angle), 0.0)) for angle in angles]
    for first in range(len(angles)):
        for second in range(first + 1, len(angles)):
            separation = float(x[second] - x[first])
            if abs(separation) < 0.10:
                continue
            slope = float((angles[second] - angles[first]) / separation)
            candidates.append(
                np.asarray((float(angles[first] - slope * x[first]), slope))
            )
    return candidates


def _vertical_mode(
    angles: np.ndarray, positions: np.ndarray, weights: np.ndarray
) -> tuple[np.ndarray, np.ndarray, float]:
    x = positions - 0.5
    best_core = np.zeros(len(angles), dtype=bool)
    best_score = (-1.0, -1, -1.0, -1, float("-inf"))
    for candidate in _vertical_model_candidates(angles, positions):
        residual = np.abs(angles - (candidate[0] + candidate[1] * x))
        keep = residual <= VERTICAL_MODEL_INLIER_TOLERANCE_DEGREES
        core = residual <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
        if not np.any(core):
            continue
        mad = weighted_median(residual[core], weights[core])
        score = (
            float(weights[core].sum()),
            int(np.count_nonzero(core)),
            float(weights[keep].sum()),
            int(np.count_nonzero(keep)),
            -mad,
        )
        if score > best_score:
            best_core = core
            best_score = score

    coefficient = weighted_linear_fit(
        x[best_core], angles[best_core], weights[best_core]
    )
    for _ in range(3):
        residual = np.abs(angles - (coefficient[0] + coefficient[1] * x))
        core = residual <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
        if np.array_equal(core, best_core):
            break
        best_core = core
        coefficient = weighted_linear_fit(
            x[best_core], angles[best_core], weights[best_core]
        )
    residual = np.abs(angles - (coefficient[0] + coefficient[1] * x))
    keep = residual <= VERTICAL_MODEL_INLIER_TOLERANCE_DEGREES
    mad = weighted_median(residual[keep], weights[keep])
    return coefficient, keep, mad


def vertical_mode(
    angles: np.ndarray, positions: np.ndarray, weights: np.ndarray
) -> tuple[np.ndarray, np.ndarray, float]:
    return _vertical_mode(angles, positions, weights)


def _vertical_conflict_has_spatial_support(
    core_positions: np.ndarray, positions: np.ndarray
) -> bool:
    if np.ptp(core_positions) < 0.20:
        return False
    if np.all(positions <= 0.5):
        gated_positions = core_positions * 2.0
    elif np.all(positions >= 0.5):
        gated_positions = (core_positions - 0.5) * 2.0
    else:
        gated_positions = core_positions
    return bool(
        np.any(gated_positions <= 0.35)
        and np.any(gated_positions >= 0.65)
    )


def _vertical_multimodal_conflict(
    angles: np.ndarray, positions: np.ndarray, weights: np.ndarray
) -> bool:
    x = positions - 0.5
    primary_model, _, _ = _vertical_mode(angles, positions, weights)
    total_count = len(angles)
    total_weight = float(weights.sum())
    if np.all(positions <= 0.5):
        comparison_x = np.asarray((-0.5, 0.0))
    elif np.all(positions >= 0.5):
        comparison_x = np.asarray((0.0, 0.5))
    else:
        comparison_x = np.asarray((-0.5, 0.5))
    hypotheses: list[np.ndarray] = []

    for candidate in _vertical_model_candidates(angles, positions):
        residual = np.abs(angles - (candidate[0] + candidate[1] * x))
        core = residual <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
        core_count = int(np.count_nonzero(core))
        core_weight = float(weights[core].sum())
        if (
            core_count < VERTICAL_CONFLICT_MINIMUM_RATIO * total_count
            or core_weight < VERTICAL_CONFLICT_MINIMUM_RATIO * total_weight
            or not _vertical_conflict_has_spatial_support(
                positions[core], positions
            )
            or weighted_median(residual[core], weights[core])
            > MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
        ):
            continue

        prediction = candidate[0] + candidate[1] * comparison_x
        if any(
            np.max(
                np.abs(
                    prediction
                    - (existing[0] + existing[1] * comparison_x)
                )
            )
            < MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
            for existing in hypotheses
        ):
            continue
        hypotheses.append(candidate)

    predictions = [
        hypothesis[0] + hypothesis[1] * comparison_x
        for hypothesis in hypotheses
    ]
    primary_prediction = (
        primary_model[0] + primary_model[1] * comparison_x
    )
    if any(
        np.max(np.abs(primary_prediction - prediction))
        >= VERTICAL_CONFLICT_SEPARATION_DEGREES
        for prediction in predictions
    ):
        return True
    for first in range(len(predictions)):
        for second in range(first + 1, len(predictions)):
            first_sample_residual = np.abs(
                angles - (hypotheses[first][0] + hypotheses[first][1] * x)
            )
            second_sample_residual = np.abs(
                angles - (hypotheses[second][0] + hypotheses[second][1] * x)
            )
            first_support = (
                first_sample_residual <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
            ) & (first_sample_residual + 1e-12 < second_sample_residual)
            second_support = (
                second_sample_residual <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
            ) & (second_sample_residual + 1e-12 < first_sample_residual)
            if (
                np.count_nonzero(first_support) + 1e-12
                < VERTICAL_CONFLICT_MINIMUM_RATIO * total_count
                or np.count_nonzero(second_support) + 1e-12
                < VERTICAL_CONFLICT_MINIMUM_RATIO * total_count
                or float(weights[first_support].sum()) + 1e-12
                < VERTICAL_CONFLICT_MINIMUM_RATIO * total_weight
                or float(weights[second_support].sum()) + 1e-12
                < VERTICAL_CONFLICT_MINIMUM_RATIO * total_weight
                or not _vertical_conflict_has_spatial_support(
                    positions[first_support], positions
                )
                or not _vertical_conflict_has_spatial_support(
                    positions[second_support], positions
                )
            ):
                continue
            separation = np.abs(predictions[first] - predictions[second])
            if np.max(separation) >= VERTICAL_CONFLICT_SEPARATION_DEGREES:
                return True
            first_residual = float(np.max(np.abs(predictions[first])))
            second_residual = float(np.max(np.abs(predictions[second])))
            if materially_improved(
                max(first_residual, second_residual),
                min(first_residual, second_residual),
            ):
                return True
    return False


def vertical_consensus(
    angles: np.ndarray,
    positions: np.ndarray,
    weights: np.ndarray,
    *,
    public_mode: bool = True,
) -> tuple[np.ndarray, np.ndarray, float, str | None]:
    mode_function = vertical_mode if public_mode else _vertical_mode
    model, keep, angular_mad = mode_function(angles, positions, weights)
    if _vertical_multimodal_conflict(angles, positions, weights):
        return model, keep, angular_mad, "conflicting_evidence"
    alternatives = ~keep
    conflicting = False
    if np.count_nonzero(alternatives) >= 2:
        alternative_model, alternative_keep, alternative_mad = _vertical_mode(
            angles[alternatives], positions[alternatives], weights[alternatives]
        )
        alternative_count = int(np.count_nonzero(alternative_keep))
        alternative_weight = float(
            weights[alternatives][alternative_keep].sum()
        )
        prediction_separation = weighted_median(
            np.abs(
                (model[0] + model[1] * (positions - 0.5))
                - (
                    alternative_model[0]
                    + alternative_model[1] * (positions - 0.5)
                )
            ),
            weights,
        )
        conflicting = (
            alternative_count
            >= VERTICAL_CONFLICT_MINIMUM_RATIO * len(angles)
            and alternative_weight
            >= VERTICAL_CONFLICT_MINIMUM_RATIO * float(weights.sum())
            and alternative_mad <= MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES
            and prediction_separation
            >= VERTICAL_CONFLICT_SEPARATION_DEGREES
        )
    if conflicting:
        return model, keep, angular_mad, "conflicting_evidence"
    if (
        np.count_nonzero(keep) / len(angles)
        < MINIMUM_VERTICAL_CONSENSUS_RATIO
        or float(weights[keep].sum() / weights.sum())
        < MINIMUM_VERTICAL_CONSENSUS_RATIO
    ):
        return model, keep, angular_mad, "insufficient_vertical_consensus"
    if angular_mad > MAXIMUM_VERTICAL_ANGULAR_MAD_DEGREES:
        return model, keep, angular_mad, "excessive_vertical_angular_mad"
    return model, keep, angular_mad, None


def undirected_horizontal_angle(
    x1: float, y1: float, x2: float, y2: float
) -> float:
    directed = float(np.degrees(np.arctan2(y2 - y1, x2 - x1)))
    return (directed + 90.0) % 180.0 - 90.0


def line_segments(gray: np.ndarray) -> np.ndarray:
    gray = analysis_grayscale(gray)
    scale = min(1.0, 1400.0 / max(gray.shape))
    small = cv2.resize(gray, None, fx=scale, fy=scale, interpolation=cv2.INTER_LINEAR)
    lines = cv2.createLineSegmentDetector(cv2.LSD_REFINE_ADV).detect(small)[0]
    return np.empty((0, 4), np.float32) if lines is None else lines.reshape(-1, 4)


class HorizontalStructure(NamedTuple):
    angle: float
    position: float
    weight: float
    left: float
    right: float
    band: int
    role: str


class HorizontalSelection(NamedTuple):
    width: int
    height: int
    structures: tuple[HorizontalStructure, ...]


class VerticalStructure(NamedTuple):
    angle: float
    position: float
    weight: float
    half_width: float
    position_uncertainty: float
    angle_uncertainty: float
    top: float
    bottom: float
    band: int
    role: str
    system: int


class VerticalSelection(NamedTuple):
    width: int
    height: int
    structures: tuple[VerticalStructure, ...]


class VerticalFitStructures(np.ndarray):
    selection: VerticalSelection | None

    def __new__(
        cls,
        values: list[tuple[float, ...]] | np.ndarray,
        selection: VerticalSelection | None,
    ) -> VerticalFitStructures:
        result = np.asarray(values, dtype=np.float64).view(cls)
        result.selection = selection
        return result

    def __array_finalize__(self, source: np.ndarray | None) -> None:
        self.selection = getattr(source, "selection", None)


def clustered_horizontal_structures(
    gray: np.ndarray, *, detailed: bool = False
) -> (
    tuple[np.ndarray, np.ndarray, np.ndarray]
    | tuple[np.ndarray, np.ndarray, np.ndarray, list[tuple[float, float]]]
):
    segments: list[tuple[float, float, float, float, float]] = []
    minimum = max(gray.shape) * 0.05 * min(1.0, 1400.0 / max(gray.shape))
    scale = min(1.0, 1400.0 / max(gray.shape))
    scaled_width = gray.shape[1] * scale
    scaled_height = gray.shape[0] * scale
    for x1, y1, x2, y2 in line_segments(gray):
        length = float(np.hypot(x2 - x1, y2 - y1))
        angle = undirected_horizontal_angle(x1, y1, x2, y2)
        if length >= minimum and abs(angle) <= 2.0:
            if x2 < x1:
                x1, y1, x2, y2 = x2, y2, x1, y1
            slope = (y2 - y1) / max(x2 - x1, 1e-6)
            position = (y1 + (scaled_width / 2 - x1) * slope) / scaled_height
            if -0.02 <= position <= 1.02:
                segments.append(
                    (float(position), angle, length, float(x1), float(x2))
                )

    clusters: list[list[tuple[float, float, float, float, float]]] = []
    for segment in sorted(segments):
        position = segment[0]
        best = None
        best_distance = float("inf")
        for cluster in clusters:
            cluster_weights = np.asarray([item[2] for item in cluster])
            cluster_position = weighted_median(
                np.asarray([item[0] for item in cluster]), cluster_weights
            )
            cluster_angle = weighted_median(
                np.asarray([item[1] for item in cluster]), cluster_weights
            )
            cluster_left = min(item[3] for item in cluster)
            cluster_right = max(item[4] for item in cluster)
            x_gap = max(0.0, cluster_left - segment[4], segment[3] - cluster_right)
            distance = abs(position - cluster_position)
            if (
                distance <= HORIZONTAL_CLUSTER_POSITION_TOLERANCE
                and abs(segment[1] - cluster_angle)
                <= HORIZONTAL_CLUSTER_ANGLE_TOLERANCE_DEGREES
                and x_gap <= scaled_width * HORIZONTAL_CLUSTER_X_GAP_RATIO
                and distance < best_distance
            ):
                best = cluster
                best_distance = distance
        if best is None:
            clusters.append([segment])
        else:
            best.append(segment)

    angles: list[float] = []
    positions: list[float] = []
    weights: list[float] = []
    extents: list[tuple[float, float]] = []
    for cluster in clusters:
        cluster_weights = np.asarray([item[2] for item in cluster])
        cluster_angles = np.asarray([item[1] for item in cluster])
        angles.append(weighted_median(cluster_angles, cluster_weights))
        positions.append(
            weighted_median(
                np.asarray([item[0] for item in cluster]), cluster_weights
            )
        )
        weights.append(max(item[2] for item in cluster))
        extents.append(
            (
                min(item[3] for item in cluster) / scaled_width,
                max(item[4] for item in cluster) / scaled_width,
            )
        )
    result = np.asarray(angles), np.asarray(positions), np.asarray(weights)
    return (*result, extents) if detailed else result


def project_horizontal_selection(
    selection: HorizontalSelection, matrix: np.ndarray
) -> HorizontalSelection:
    projected: list[HorizontalStructure] = []
    for structure in selection.structures:
        x1 = structure.left * selection.width
        x2 = structure.right * selection.width
        center_y = structure.position * selection.height
        slope = float(np.tan(np.radians(structure.angle)))
        y1 = center_y + (x1 - selection.width / 2) * slope
        y2 = center_y + (x2 - selection.width / 2) * slope
        destination = matrix @ np.asarray([[x1, x2], [y1, y2], [1.0, 1.0]])
        if destination[0, 1] < destination[0, 0]:
            destination = destination[:, ::-1]
        dx = destination[0, 1] - destination[0, 0]
        projected_angle = undirected_horizontal_angle(
            destination[0, 0],
            destination[1, 0],
            destination[0, 1],
            destination[1, 1],
        )
        projected_slope = (destination[1, 1] - destination[1, 0]) / max(dx, 1e-6)
        projected_position = (
            destination[1, 0]
            + (selection.width / 2 - destination[0, 0]) * projected_slope
        ) / selection.height
        projected.append(
            HorizontalStructure(
                projected_angle,
                float(projected_position),
                float(abs(dx)),
                float(destination[0, 0] / selection.width),
                float(destination[0, 1] / selection.width),
                structure.band,
                structure.role,
            )
        )
    return HorizontalSelection(selection.width, selection.height, tuple(projected))


def project_horizontal_selection_through_vertical(
    selection: HorizontalSelection,
    convergence_slope: float,
) -> HorizontalSelection:
    projected: list[HorizontalStructure] = []
    for structure in selection.structures:
        source_x = np.asarray(
            [structure.left * selection.width, structure.right * selection.width],
            dtype=np.float32,
        )
        center_y = structure.position * selection.height
        slope = float(np.tan(np.radians(structure.angle)))
        source_y = center_y + (source_x - selection.width / 2) * slope
        destination_x, destination_y = vertical_forward_points(
            source_x,
            source_y,
            selection.width,
            selection.height,
            convergence_slope,
        )
        if destination_x[1] < destination_x[0]:
            destination_x = destination_x[::-1]
            destination_y = destination_y[::-1]
        dx = float(destination_x[1] - destination_x[0])
        projected_slope = float(
            (destination_y[1] - destination_y[0]) / max(dx, 1e-6)
        )
        projected.append(
            structure._replace(
                angle=undirected_horizontal_angle(
                    destination_x[0],
                    destination_y[0],
                    destination_x[1],
                    destination_y[1],
                ),
                position=float(
                    (
                        destination_y[0]
                        + (
                            selection.width / 2 - destination_x[0]
                        )
                        * projected_slope
                    )
                    / selection.height
                ),
                left=float(destination_x[0] / selection.width),
                right=float(destination_x[1] / selection.width),
            )
        )
    return HorizontalSelection(selection.width, selection.height, tuple(projected))


def tracked_horizontal_measurement(
    array: np.ndarray,
    positions: np.ndarray,
    weight: np.ndarray,
    extents: list[tuple[float, float]],
    selection: HorizontalSelection,
) -> tuple[float, int, str | None, float | None, int, str | None]:
    possible: dict[int, list[tuple[float, int]]] = {}
    candidate_possible: dict[int, list[tuple[float, int]]] = {}
    for expected_index, expected in enumerate(selection.structures):
        expected_length = max(expected.right - expected.left, 1e-6)
        for index in range(len(array)):
            left, right = extents[index]
            length = max(right - left, 1e-6)
            length_ratio = min(length, expected_length) / max(length, expected_length)
            x_gap = max(0.0, expected.left - right, left - expected.right)
            if (
                abs(positions[index] - expected.position)
                <= HORIZONTAL_TRACK_POSITION_TOLERANCE
                and x_gap <= HORIZONTAL_TRACK_X_TOLERANCE_RATIO
                and length_ratio >= HORIZONTAL_TRACK_LENGTH_RATIO
            ):
                center_delta = abs((left + right) / 2 - (expected.left + expected.right) / 2)
                cost = (
                    abs(positions[index] - expected.position)
                    + center_delta
                    + (1.0 - length_ratio) * 0.02
                )
                possible.setdefault(expected_index, []).append((cost, index))
                candidate_possible.setdefault(index, []).append((cost, expected_index))

    matched: list[tuple[HorizontalStructure, int]] = []
    matched_expected: set[int] = set()
    for expected_index, expected in enumerate(selection.structures):
        plausible = sorted(possible.get(expected_index, []))
        if not plausible or (
            len(plausible) > 1
            and plausible[1][0] - plausible[0][0]
            <= HORIZONTAL_TRACK_AMBIGUITY_MARGIN
        ):
            continue
        cost, index = plausible[0]
        reverse_plausible = sorted(candidate_possible[index])
        if (
            len(reverse_plausible) > 1
            and reverse_plausible[1][0] - reverse_plausible[0][0]
            <= HORIZONTAL_TRACK_AMBIGUITY_MARGIN
        ):
            continue
        reverse_cost, reverse_expected = reverse_plausible[0]
        if reverse_expected != expected_index or reverse_cost != cost:
            continue
        matched.append((expected, index))
        matched_expected.add(expected_index)

    fit = [(expected, index) for expected, index in matched if expected.role == "fit"]
    holdout = [
        (expected, index) for expected, index in matched if expected.role == "holdout"
    ]
    expected_fit_bands = {
        structure.band for structure in selection.structures if structure.role == "fit"
    }
    expected_holdout_bands = {
        structure.band
        for structure in selection.structures
        if structure.role == "holdout"
    }
    fit_bands = {expected.band for expected, _ in fit}
    holdout_bands = {expected.band for expected, _ in holdout}
    missing_fit = any(
        index not in matched_expected and structure.role == "fit"
        for index, structure in enumerate(selection.structures)
    )
    missing_holdout = any(
        index not in matched_expected and structure.role == "holdout"
        for index, structure in enumerate(selection.structures)
    )
    if (
        missing_fit
        or
        len(fit) < MINIMUM_HORIZONTAL_SPLIT_STRUCTURES
        or len(fit_bands) < MINIMUM_HORIZONTAL_SPLIT_Y_BANDS
        or fit_bands != expected_fit_bands
    ):
        return (
            0.0,
            len(matched),
            "missing_tracked_fit_horizontal_evidence",
            None,
            len(holdout),
            "tracked_fit_horizontal_evidence_is_insufficient",
        )
    if (
        missing_holdout
        or
        len(holdout) < MINIMUM_HORIZONTAL_SPLIT_STRUCTURES
        or len(holdout_bands) < MINIMUM_HORIZONTAL_SPLIT_Y_BANDS
        or holdout_bands != expected_holdout_bands
    ):
        return (
            0.0,
            len(matched),
            "missing_tracked_holdout_horizontal_evidence",
            None,
            len(holdout),
            "missing_tracked_holdout_horizontal_evidence",
        )
    fit_indices = np.asarray([index for _, index in fit])
    holdout_indices = np.asarray([index for _, index in holdout])
    return (
        float(np.average(array[fit_indices], weights=weight[fit_indices])),
        len(matched),
        None,
        float(np.average(array[holdout_indices], weights=weight[holdout_indices])),
        len(holdout),
        None,
    )


def horizontal_angle(
    gray: np.ndarray,
    *,
    detailed: bool = False,
    selection: HorizontalSelection | None = None,
    transform: np.ndarray | None = None,
) -> (
    tuple[float, int]
    | tuple[
        float,
        int,
        str | None,
        float | None,
        int,
        str | None,
        HorizontalSelection | None,
    ]
):
    clustered = clustered_horizontal_structures(gray, detailed=True)
    if len(clustered) == 3:
        array, positions, weight = clustered
        extents = [(0.0, 1.0)] * len(array)
    else:
        array, positions, weight, extents = clustered
    if selection is not None:
        if transform is not None:
            selection = project_horizontal_selection(selection, transform)
        result = tracked_horizontal_measurement(
            array, positions, weight, extents, selection
        )
        return (*result, selection) if detailed else result[:2]
    if len(array) < 10:
        result = (0.0, 0, "insufficient_independent_horizontal_structures")
        return (*result, None, 0, "insufficient_holdout_horizontal_structures", None) if detailed else result[:2]

    center = weighted_median(array, weight)
    keep = np.abs(array - center) <= HORIZONTAL_INLIER_TOLERANCE_DEGREES
    kept = int(np.count_nonzero(keep))
    count_ratio = kept / len(array)
    weight_ratio = float(weight[keep].sum() / weight.sum())
    outlier_angles = array[~keep]
    conflicting = False
    if len(outlier_angles) >= 3:
        outlier_weights = weight[~keep]
        alternative = weighted_median(outlier_angles, outlier_weights)
        alternative_keep = (
            np.abs(outlier_angles - alternative)
            <= HORIZONTAL_INLIER_TOLERANCE_DEGREES
        )
        alternative_ratio = int(np.count_nonzero(alternative_keep)) / len(array)
        conflicting = (
            alternative_ratio >= HORIZONTAL_CONFLICT_MINIMUM_RATIO
            and abs(alternative - center) >= HORIZONTAL_CONFLICT_SEPARATION_DEGREES
        )
    if conflicting:
        result = (0.0, kept, "conflicting_horizontal_modes")
        return (*result, None, 0, "conflicting_horizontal_modes", None) if detailed else result[:2]
    if (
        kept < 10
        or count_ratio < MINIMUM_HORIZONTAL_CONSENSUS_RATIO
        or weight_ratio < MINIMUM_HORIZONTAL_CONSENSUS_RATIO
    ):
        result = (0.0, kept, "insufficient_horizontal_consensus")
        return (*result, None, 0, "insufficient_horizontal_consensus", None) if detailed else result[:2]

    kept_positions = positions[keep]
    bands = np.unique(
        np.clip(
            (kept_positions * HORIZONTAL_Y_BAND_COUNT).astype(int),
            0,
            HORIZONTAL_Y_BAND_COUNT - 1,
        )
    )
    if (
        len(bands) < MINIMUM_HORIZONTAL_Y_BANDS
        or np.ptp(kept_positions) < MINIMUM_HORIZONTAL_Y_SPREAD
    ):
        result = (0.0, 0, "insufficient_independent_horizontal_y_bands")
        return (*result, None, 0, "insufficient_holdout_horizontal_bands", None) if detailed else result[:2]

    kept_bands = np.clip(
        (kept_positions * HORIZONTAL_Y_BAND_COUNT).astype(int),
        0,
        HORIZONTAL_Y_BAND_COUNT - 1,
    )
    ordered_bands = sorted(np.unique(kept_bands))
    fit_bands = set(ordered_bands[::2])
    holdout_bands = set(ordered_bands[1::2])
    fit = keep.copy()
    holdout = keep.copy()
    fit[keep] = np.asarray([band in fit_bands for band in kept_bands])
    holdout[keep] = np.asarray([band in holdout_bands for band in kept_bands])
    fit_samples = int(np.count_nonzero(fit))
    holdout_samples = int(np.count_nonzero(holdout))
    fit_band_count = len(np.unique(kept_bands[np.isin(kept_bands, list(fit_bands))]))
    holdout_band_count = len(
        np.unique(kept_bands[np.isin(kept_bands, list(holdout_bands))])
    )
    if (
        fit_samples < MINIMUM_HORIZONTAL_SPLIT_STRUCTURES
        or fit_band_count < MINIMUM_HORIZONTAL_SPLIT_Y_BANDS
    ):
        result = (0.0, kept, "insufficient_fit_horizontal_evidence")
        return (
            *result,
            None,
            holdout_samples,
            "fit_horizontal_evidence_is_insufficient",
            None,
        ) if detailed else result[:2]
    if (
        holdout_samples < MINIMUM_HORIZONTAL_SPLIT_STRUCTURES
        or holdout_band_count < MINIMUM_HORIZONTAL_SPLIT_Y_BANDS
    ):
        result = (0.0, kept, "insufficient_holdout_horizontal_evidence")
        return (
            *result,
            None,
            holdout_samples,
            "insufficient_holdout_horizontal_evidence",
            None,
        ) if detailed else result[:2]

    angle = float(np.average(array[fit], weights=weight[fit]))
    holdout_angle = float(np.average(array[holdout], weights=weight[holdout]))
    holdout_reason = None
    selected_structures = tuple(
        HorizontalStructure(
            float(array[index]),
            float(positions[index]),
            float(weight[index]),
            float(extents[index][0]),
            float(extents[index][1]),
            int(kept_bands[np.flatnonzero(keep).tolist().index(index)]),
            "fit" if fit[index] else "holdout",
        )
        for index in np.flatnonzero(keep)
    )
    selected = HorizontalSelection(
        gray.shape[1], gray.shape[0], selected_structures
    )
    if detailed:
        return (
            angle,
            kept,
            None,
            holdout_angle,
            holdout_samples,
            holdout_reason,
            selected,
        )
    return angle, kept


def horizontal_measurement(
    gray: np.ndarray,
    selection: HorizontalSelection | None = None,
    transform: np.ndarray | None = None,
) -> tuple[
    float,
    int,
    str | None,
    float | None,
    int,
    str | None,
    HorizontalSelection | None,
]:
    kwargs: dict[str, object] = {"detailed": True}
    if selection is not None:
        kwargs.update(selection=selection, transform=transform)
    result = horizontal_angle(gray, **kwargs)
    if len(result) == 2:
        angle, samples = result
        return angle, samples, None, None, 0, "holdout_not_available", None
    if len(result) == 6:
        return (*result, None)
    return result


def vertical_segment_half_width(
    foreground: np.ndarray, x1: float, y1: float, x2: float, y2: float
) -> float:
    search_radius = max(4, min(32, round(foreground.shape[1] * 0.025)))
    measured: list[float] = []
    for fraction in np.linspace(0.1, 0.9, 9):
        x = int(round(x1 + (x2 - x1) * fraction))
        y = int(round(y1 + (y2 - y1) * fraction))
        if not (0 <= y < foreground.shape[0] and 0 <= x < foreground.shape[1]):
            continue
        start = max(0, x - search_radius)
        stop = min(foreground.shape[1], x + search_radius + 1)
        dark = np.flatnonzero(foreground[y, start:stop]) + start
        nearby = dark[np.abs(dark - x) <= 2]
        if len(nearby) == 0:
            continue
        seed = int(nearby[np.argmin(np.abs(nearby - x))])
        component = {seed}
        changed = True
        while changed:
            changed = False
            for candidate in dark:
                value = int(candidate)
                if value not in component and any(
                    abs(value - member) <= 2 for member in component
                ):
                    component.add(value)
                    changed = True
        measured.append(float(max(abs(value - x) for value in component)))
    return max(0.5, float(np.median(measured)) if measured else 0.5)


def horizontal_structural_references(
    segments: np.ndarray, width: int, height: int
) -> list[
    tuple[
        float,
        float,
        float,
        float,
        float,
        tuple[tuple[float, float], ...],
    ]
]:
    fragments: list[tuple[float, float, float, float, float, float, float]] = []
    minimum_fragment = width * 0.02
    for x1, y1, x2, y2 in segments:
        if x2 < x1:
            x1, y1, x2, y2 = x2, y2, x1, y1
        length = float(np.hypot(x2 - x1, y2 - y1))
        angle = undirected_horizontal_angle(x1, y1, x2, y2)
        if length >= minimum_fragment and abs(angle) <= 2.0:
            slope = (y2 - y1) / max(x2 - x1, 1e-6)
            center_y = y1 + (width / 2 - x1) * slope
            fragments.append(
                (
                    float(x1),
                    float(y1),
                    float(x2),
                    float(y2),
                    length,
                    float(center_y),
                    angle,
                )
            )

    clusters: list[
        list[tuple[float, float, float, float, float, float, float]]
    ] = []
    tolerance = max(2.0, height * 0.003)
    maximum_x_gap = max(2.0, width * HORIZONTAL_CLUSTER_X_GAP_RATIO)
    for fragment in sorted(fragments, key=lambda item: item[5]):
        center_y = fragment[5]
        best = None
        best_distance = float("inf")
        for cluster in clusters:
            cluster_y = weighted_median(
                np.asarray([item[5] for item in cluster]),
                np.asarray([item[4] for item in cluster]),
            )
            cluster_angle = weighted_median(
                np.asarray([item[6] for item in cluster]),
                np.asarray([item[4] for item in cluster]),
            )
            distance = abs(center_y - cluster_y)
            x_gap = min(
                max(0.0, item[0] - fragment[2], fragment[0] - item[2])
                for item in cluster
            )
            if (
                distance <= tolerance
                and abs(fragment[6] - cluster_angle) <= 0.4
                and x_gap <= maximum_x_gap
                and distance < best_distance
            ):
                best = cluster
                best_distance = distance
        if best is None:
            clusters.append([fragment])
        else:
            best.append(fragment)

    references: list[
        tuple[
            float,
            float,
            float,
            float,
            float,
            tuple[tuple[float, float], ...],
        ]
    ] = []
    for cluster in clusters:
        weights = np.asarray([item[4] for item in cluster])
        total_support = float(weights.sum())
        x1 = min(item[0] for item in cluster)
        x2 = max(item[2] for item in cluster)
        if total_support < width * 0.10 or x2 - x1 < width * 0.10:
            continue
        angle = weighted_median(
            np.asarray([item[6] for item in cluster]), weights
        )
        slope = float(np.tan(np.radians(angle)))
        center_y = weighted_median(
            np.asarray([item[5] for item in cluster]), weights
        )
        y1 = center_y + (x1 - width / 2) * slope
        y2 = center_y + (x2 - width / 2) * slope
        occupied = tuple(sorted((item[0], item[2]) for item in cluster))
        references.append((x1, y1, x2, y2, total_support, occupied))
    return references


def staff_intersections(
    segment: tuple[float, float, float, float],
    references: list[tuple],
    width: int | None = None,
    height: int | None = None,
) -> int:
    x1, y1, x2, y2 = segment
    if y2 < y1:
        x1, y1, x2, y2 = x2, y2, x1, y1
    horizontal_tolerance = max(
        2.0, (width or 0) * STAFF_REFERENCE_X_TOLERANCE_RATIO
    )
    vertical_tolerance = max(2.0, (height or 0) * 0.004)
    intersections = 0
    for reference in references:
        hx1, hy1, hx2, hy2, _ = reference[:5]
        occupied = reference[5] if len(reference) > 5 else ((hx1, hx2),)
        expected_y = (hy1 + hy2) / 2
        for _ in range(2):
            fraction = (expected_y - y1) / max(y2 - y1, 1e-6)
            vertical_x = x1 + (x2 - x1) * fraction
            horizontal_fraction = (vertical_x - hx1) / max(hx2 - hx1, 1e-6)
            expected_y = hy1 + (hy2 - hy1) * horizontal_fraction
        if (
            any(
                start - horizontal_tolerance
                <= vertical_x
                <= stop + horizontal_tolerance
                for start, stop in occupied
            )
            and y1 - vertical_tolerance
            <= expected_y
            <= y2 + vertical_tolerance
        ):
            intersections += 1
    return intersections


def staff_intersection_coordinates(
    segment: tuple[float, float, float, float],
    references: list[tuple],
    width: int,
    height: int,
) -> list[float]:
    x1, y1, x2, y2 = segment
    if y2 < y1:
        x1, y1, x2, y2 = x2, y2, x1, y1
    horizontal_tolerance = max(
        2.0, width * STAFF_REFERENCE_X_TOLERANCE_RATIO
    )
    vertical_tolerance = max(2.0, height * 0.004)
    coordinates: list[float] = []
    for reference in references:
        hx1, hy1, hx2, hy2, _ = reference[:5]
        occupied = reference[5] if len(reference) > 5 else ((hx1, hx2),)
        expected_y = (hy1 + hy2) / 2
        for _ in range(2):
            fraction = (expected_y - y1) / max(y2 - y1, 1e-6)
            vertical_x = x1 + (x2 - x1) * fraction
            horizontal_fraction = (vertical_x - hx1) / max(hx2 - hx1, 1e-6)
            expected_y = hy1 + (hy2 - hy1) * horizontal_fraction
        if (
            any(
                start - horizontal_tolerance
                <= vertical_x
                <= stop + horizontal_tolerance
                for start, stop in occupied
            )
            and y1 - vertical_tolerance
            <= expected_y
            <= y2 + vertical_tolerance
        ):
            coordinates.append(float(expected_y))
    merged: list[list[float]] = []
    merge_tolerance = max(2.0, height * 0.005)
    for coordinate in sorted(coordinates):
        if merged and coordinate - merged[-1][-1] <= merge_tolerance:
            merged[-1].append(coordinate)
        else:
            merged.append([coordinate])
    return [float(np.mean(group)) for group in merged]


def spans_complete_staff_system(intersections: list[float]) -> bool:
    if len(intersections) < MINIMUM_VERTICAL_STAFF_INTERSECTIONS:
        return False
    values = np.asarray(intersections)
    for start in range(len(values) - MINIMUM_VERTICAL_STAFF_INTERSECTIONS + 1):
        group = values[start : start + MINIMUM_VERTICAL_STAFF_INTERSECTIONS]
        gaps = np.diff(group)
        if (
            np.all(gaps > 1.0)
            and float(np.max(gaps) / np.min(gaps)) <= MAXIMUM_STAFF_SPACING_RATIO
        ):
            return True
    return False


def vertical_structure_has_notehead_topology(
    foreground: np.ndarray,
    segment: tuple[float, float, float, float],
    intersection_y: list[float],
    half_width: float,
) -> bool:
    height, width = foreground.shape
    if not np.any(foreground):
        return False
    x1, y1, x2, y2 = segment
    if y2 < y1:
        x1, y1, x2, y2 = x2, y2, x1, y1
    support_top = max(0, int(np.ceil(y1)))
    support_bottom = min(height - 1, int(np.floor(y2)))
    if support_bottom <= support_top:
        return True
    endpoint_margin = max(8, round(height * 0.012))
    top = max(0, support_top - endpoint_margin)
    bottom = min(height - 1, support_bottom + endpoint_margin)
    radius = max(10, round(width * 0.018))
    excluded_radius = max(1.5, min(3.0, height * 0.002))
    extents: list[tuple[int, float, float, bool]] = []
    supported = 0
    eligible = 0
    for y in range(top, bottom + 1):
        crossing = any(
            abs(y - reference_y) <= excluded_radius
            for reference_y in intersection_y
        )
        fraction = (y - y1) / max(y2 - y1, 1e-6)
        x = x1 + (x2 - x1) * fraction
        center = int(round(x))
        left = max(0, center - radius)
        right = min(width, center + radius + 1)
        dark = np.flatnonzero(foreground[y, left:right]) + left
        within_support = support_top <= y <= support_bottom
        eligible += int(within_support)
        nearby = dark[np.abs(dark - x) <= max(2.0, half_width + 1.5)]
        if len(nearby) == 0:
            continue
        supported += int(within_support)
        seed = int(nearby[np.argmin(np.abs(nearby - x))])
        component = {seed}
        changed = True
        while changed:
            changed = False
            for candidate in dark:
                value = int(candidate)
                if value not in component and any(
                    abs(value - member) <= 1 for member in component
                ):
                    component.add(value)
                    changed = True
        left_extent = max(0.0, x - min(component))
        right_extent = max(0.0, max(component) - x)
        asymmetry = min(left_extent, right_extent) / max(
            1.0, max(left_extent, right_extent)
        )
        extents.append(
            (y, float(max(component) - min(component) + 1), asymmetry, crossing)
        )
    if eligible == 0 or supported / eligible < MINIMUM_VERTICAL_INK_CONTINUITY:
        return True
    if not extents:
        return True
    lateral_extents = [
        extent for _, extent, _, crossing in extents if not crossing
    ]
    if not lateral_extents:
        return False
    baseline = float(np.percentile(lateral_extents, 35))
    if (
        baseline >= NOTEHEAD_MINIMUM_LATERAL_EXTENT_PIXELS
        and support_bottom - support_top
        < height * MINIMUM_VERTICAL_LONG_REFERENCE_LENGTH_RATIO
    ):
        return True
    blob_limit = max(
        NOTEHEAD_MINIMUM_LATERAL_EXTENT_PIXELS,
        baseline * 2.5,
    )
    blob_rows = [
        y
        for y, extent, asymmetry, crossing in extents
        if not crossing and extent >= blob_limit and asymmetry <= 0.55
    ]
    run = 0
    previous = -10
    for y in blob_rows:
        run = run + 1 if y - previous <= 2 else 1
        if run >= NOTEHEAD_MINIMUM_ROWS:
            return True
        previous = y
    return False


def clustered_vertical_structures(
    gray: np.ndarray, *, detailed: bool = False
) -> (
    tuple[np.ndarray, np.ndarray, np.ndarray]
    | tuple[
        np.ndarray,
        np.ndarray,
        np.ndarray,
        list[list[tuple[float, float, float, float, float, float, float]]],
    ]
):
    fragments: list[
        tuple[float, float, float, float, float, float, float]
    ] = []
    scale = min(1.0, 1400.0 / max(gray.shape))
    width = round(gray.shape[1] * scale)
    height = round(gray.shape[0] * scale)
    small = cv2.resize(gray, (width, height), interpolation=cv2.INTER_LINEAR)
    detected_segments = line_segments(gray)
    horizontal_references = horizontal_structural_references(
        detected_segments, width, height
    )
    structural_foreground = foreground_mask(small)
    for x1, y1, x2, y2 in detected_segments:
        if y2 < y1:
            x1, y1, x2, y2 = x2, y2, x1, y1
        dy, dx = y2 - y1, x2 - x1
        length = float(np.hypot(dx, dy))
        if dy <= 0 or length < max(5.0, height * MINIMUM_VERTICAL_FRAGMENT_LENGTH_RATIO):
            continue
        angle = float(np.degrees(np.arctan2(dx, dy)))
        slope = dx / dy
        position = float((x1 + (height / 2 - y1) * slope) / width)
        if (
            abs(angle) <= MAXIMUM_VERTICAL_FRAGMENT_ANGLE_DEGREES
            and 0.02 <= position <= 0.98
        ):
            midpoint_y = (y1 + y2) / 2
            extrapolation = abs(height / 2 - midpoint_y)
            angle_uncertainty = min(
                5.0, float(np.degrees(np.arctan2(2.0, length)))
            )
            uncertainty = (
                extrapolation
                * np.tan(np.radians(angle_uncertainty))
                / width
            )
            fragments.append(
                (
                    position,
                    angle,
                    length,
                    float(uncertainty),
                    0.0,
                    float(y1 / height),
                    float(y2 / height),
                )
            )

    clusters: list[
        list[tuple[float, float, float, float, float, float, float]]
    ] = []
    for segment in sorted(fragments, key=lambda item: (item[5], item[0])):
        position, _, _, uncertainty, _, _, _ = segment
        best: list[
            tuple[float, float, float, float, float, float, float]
        ] | None = None
        best_distance = float("inf")
        for cluster in clusters:
            cluster_weights = np.asarray([item[2] for item in cluster])
            cluster_position = weighted_median(
                np.asarray([item[0] for item in cluster]), cluster_weights
            )
            cluster_angle = weighted_median(
                np.asarray([item[1] for item in cluster]), cluster_weights
            )
            angle_support = max(
                VERTICAL_CLUSTER_ANGLE_SUPPORT_DEGREES,
                min(
                    6.0,
                    float(
                        np.degrees(
                            np.arctan2(
                                2.0,
                                min(
                                    segment[2],
                                    weighted_median(
                                        cluster_weights, cluster_weights
                                    ),
                                ),
                            )
                        )
                    ),
                ),
            )
            cluster_uncertainty = max(item[3] for item in cluster)
            distance = abs(position - cluster_position)
            y_gap = min(
                min(abs(segment[5] - item[6]), abs(item[5] - segment[6]))
                if segment[6] < item[5] or item[6] < segment[5]
                else 0.0
                for item in cluster
            )
            y_overlap_ratio = max(
                max(
                    0.0,
                    min(segment[6], item[6]) - max(segment[5], item[5]),
                )
                / max(
                    1e-6,
                    min(segment[6] - segment[5], item[6] - item[5]),
                )
                for item in cluster
            )
            angle_difference = abs(segment[1] - cluster_angle)
            if (
                distance
                <= (
                    VERTICAL_CLUSTER_POSITION_TOLERANCE
                    + uncertainty
                    + cluster_uncertainty
                )
                and (
                    angle_difference < VERTICAL_CONFLICT_SEPARATION_DEGREES
                    or (
                        y_overlap_ratio < 0.80
                        and angle_difference <= angle_support
                    )
                )
                and y_gap <= VERTICAL_CLUSTER_Y_GAP_RATIO
                and distance < best_distance
            ):
                best = cluster
                best_distance = distance
        if best is None:
            clusters.append([segment])
        else:
            best.append(segment)

    positions: list[float] = []
    angles: list[float] = []
    weights: list[float] = []
    accepted_clusters: list[
        list[tuple[float, float, float, float, float, float, float]]
    ] = []
    for cluster in clusters:
        cluster_weights = np.asarray([item[2] for item in cluster])
        cluster_positions = np.asarray([item[0] for item in cluster])
        cluster_angles = np.asarray([item[1] for item in cluster])
        position = weighted_median(cluster_positions, cluster_weights)
        angle_center = weighted_median(cluster_angles, cluster_weights)
        angle_keep = np.abs(cluster_angles - angle_center) <= max(
            0.35,
            3 * weighted_median(
                np.abs(cluster_angles - angle_center), cluster_weights
            ),
        )
        angle = weighted_median(
            cluster_angles[angle_keep], cluster_weights[angle_keep]
        )
        midpoint_y = (
            np.asarray([(item[5] + item[6]) / 2 for item in cluster])
            * height
        )
        midpoint_x = np.asarray(
            [
                item[0] * width
                + (
                    ((item[5] + item[6]) / 2) * height
                    - height / 2
                )
                * np.tan(np.radians(item[1]))
                for item in cluster
            ]
        )
        if len(cluster) >= 2 and np.ptp(midpoint_y) >= height * 0.012:
            line_fit = weighted_linear_fit(
                midpoint_y, midpoint_x, cluster_weights
            )
            fit_angle = float(np.degrees(np.arctan(line_fit[1])))
            fit_residual = np.abs(
                midpoint_x
                - (line_fit[0] + line_fit[1] * midpoint_y)
            )
            if (
                abs(fit_angle) <= 3.0
                and weighted_median(fit_residual, cluster_weights)
                <= max(1.5, width * 0.003)
            ):
                angle = fit_angle
                position = float(
                    (line_fit[0] + line_fit[1] * height / 2) / width
                )
        if abs(angle) > 3.0 or not 0.035 <= position <= 0.965:
            continue
        y1 = min(item[5] for item in cluster)
        y2 = max(item[6] for item in cluster)
        slope = float(np.tan(np.radians(angle)))
        center_x = position * width
        assembled = (
            center_x + (y1 * height - height / 2) * slope,
            y1 * height,
            center_x + (y2 * height - height / 2) * slope,
            y2 * height,
        )
        assembled_length = float(
            np.hypot(assembled[2] - assembled[0], assembled[3] - assembled[1])
        )
        intersection_y = staff_intersection_coordinates(
            assembled, horizontal_references, width, height
        )
        endpoint_extension = height * 0.015
        topology_segment = (
            assembled[0] - slope * endpoint_extension,
            assembled[1] - endpoint_extension,
            assembled[2] + slope * endpoint_extension,
            assembled[3] + endpoint_extension,
        )
        topology_intersection_y = staff_intersection_coordinates(
            topology_segment, horizontal_references, width, height
        )
        complete_staff = spans_complete_staff_system(intersection_y)
        if (
            assembled_length
            < height * MINIMUM_VERTICAL_LONG_REFERENCE_LENGTH_RATIO
            and not complete_staff
        ):
            continue
        uncertainty = min(
            0.01,
            max(abs(item[0] - position) + item[3] for item in cluster),
        )
        half_width = (
            vertical_segment_half_width(structural_foreground, *assembled) / scale
        )
        if vertical_structure_has_notehead_topology(
            structural_foreground,
            assembled,
            topology_intersection_y,
            half_width * scale,
        ):
            continue
        structure = [
            (
                position,
                angle,
                assembled_length,
                uncertainty,
                half_width,
                y1,
                y2,
            )
        ]
        positions.append(position)
        angles.append(angle)
        weights.append(min(assembled_length, height * 0.06))
        accepted_clusters.append(structure)
    result = np.asarray(angles), np.asarray(positions), np.asarray(weights)
    return (*result, accepted_clusters) if detailed else result


def vertical_side_system_reason(
    positions: np.ndarray,
    clusters: list[list[tuple[float, ...]]],
    *,
    minimum_per_side: int,
    system_ids: np.ndarray | None = None,
) -> str | None:
    if system_ids is None:
        system_ids = vertical_system_identities(clusters)
    for side in (positions < 0.5, positions > 0.5):
        indexes = np.flatnonzero(side)
        if len(indexes) < minimum_per_side:
            return "insufficient_grouped_vertical_structures_on_both_sides"
        starts = [item[5] for index in indexes for item in clusters[index]]
        stops = [item[6] for index in indexes for item in clusters[index]]
        if not starts or max(stops) - min(starts) < MINIMUM_VERTICAL_SIDE_Y_SPREAD:
            return "insufficient_system_spanning_vertical_evidence_on_both_sides"
        distributed_reference = any(
            item[6] - item[5]
            >= (
                MINIMUM_VERTICAL_SIDE_Y_SPREAD
                + MINIMUM_VERTICAL_LONG_REFERENCE_LENGTH_RATIO
            )
            for index in indexes
            for item in clusters[index]
        )
        side_systems = {
            int(system_ids[index])
            for index in indexes
        }
        system_centers = [
            (
                min(
                    item[5]
                    for index in indexes
                    if int(system_ids[index]) == system
                    for item in clusters[index]
                )
                + max(
                    item[6]
                    for index in indexes
                    if int(system_ids[index]) == system
                    for item in clusters[index]
                )
            )
            / 2
            for system in side_systems
        ]
        if (
            not distributed_reference
            and (
                len(side_systems) < MINIMUM_VERTICAL_SIDE_Y_BANDS
                or np.ptp(system_centers) < MINIMUM_VERTICAL_SIDE_Y_SPREAD
            )
        ):
            return "insufficient_vertical_system_diversity_on_both_sides"
        bands = {
            int(
                np.clip(
                    coordinate * VERTICAL_Y_BAND_COUNT,
                    0,
                    VERTICAL_Y_BAND_COUNT - 1,
                )
            )
            for index in indexes
            for item in clusters[index]
            for coordinate in (item[5], item[6])
        }
        if len(bands) < MINIMUM_VERTICAL_SIDE_Y_BANDS and not distributed_reference:
            return "insufficient_grouped_vertical_y_bands_on_both_sides"
    return None


def vertical_system_identities(
    clusters: list[list[tuple[float, ...]]],
) -> np.ndarray:
    intervals = [
        (
            min(float(item[5]) for item in cluster),
            max(float(item[6]) for item in cluster),
        )
        for cluster in clusters
    ]
    identities = np.full(len(intervals), -1, dtype=int)
    representatives: list[tuple[float, float]] = []
    for index in sorted(
        range(len(intervals)),
        key=lambda value: (
            (intervals[value][0] + intervals[value][1]) / 2,
            intervals[value],
        ),
    ):
        top, bottom = intervals[index]
        span = max(bottom - top, 1e-6)
        best_system = None
        best_distance = float("inf")
        for system, (reference_top, reference_bottom) in enumerate(representatives):
            reference_span = max(reference_bottom - reference_top, 1e-6)
            center_distance = abs(
                (top + bottom - reference_top - reference_bottom) / 2
            )
            endpoint_distance = max(
                abs(top - reference_top),
                abs(bottom - reference_bottom),
            )
            if (
                center_distance <= max(0.04, 0.25 * min(span, reference_span))
                and endpoint_distance
                <= max(0.05, 0.35 * min(span, reference_span))
                and center_distance < best_distance
            ):
                best_system = system
                best_distance = center_distance
        if best_system is None:
            best_system = len(representatives)
            representatives.append((top, bottom))
        identities[index] = best_system
    return identities


def vertical_region_validation(
    gray: np.ndarray,
    excluded_structures: np.ndarray | None = None,
    holdout_bands: set[int] | None = None,
) -> tuple[float | None, int, str | None]:
    angles, positions, weights, clusters = clustered_vertical_structures(
        gray, detailed=True
    )
    keep = np.ones(len(positions), dtype=bool)
    if holdout_bands:
        bands = np.clip(
            (positions * VERTICAL_X_BAND_COUNT).astype(int),
            0,
            VERTICAL_X_BAND_COUNT - 1,
        )
        keep &= np.isin(bands, list(holdout_bands))
    if excluded_structures is not None:
        structures = np.asarray(excluded_structures, dtype=np.float64)
        if structures.ndim == 1:
            structures = structures[:, None]
        for structure in structures:
            position = float(structure[0])
            position_uncertainty = (
                float(structure[3])
                if len(structure) >= 4
                else 0.0
            )
            normalized_half_width = (
                (float(structure[2]) if len(structure) >= 3 else 6.5)
                + VERTICAL_EXCLUSION_SAFETY_PIXELS
            ) / max(1, gray.shape[1])
            keep &= (
                np.abs(positions - position)
                > VERTICAL_CLUSTER_POSITION_TOLERANCE
                + position_uncertainty
                + normalized_half_width
            )
    angles = angles[keep]
    positions = positions[keep]
    weights = weights[keep]
    clusters = [clusters[index] for index in np.flatnonzero(keep)]
    reason = vertical_side_system_reason(
        positions,
        clusters,
        minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
    )
    if reason is not None:
        return None, len(angles), reason
    if len(angles) < MINIMUM_VERTICAL_FIT_STRUCTURES:
        return None, len(angles), "insufficient_disjoint_vertical_fit_structures"
    _, consensus, _, consensus_reason = vertical_consensus(
        angles, positions, weights
    )
    if consensus_reason is not None:
        return None, int(np.count_nonzero(consensus)), consensus_reason
    distribution_reason = vertical_fit_distribution_reason(
        positions, minimum_balance=0.40
    )
    if distribution_reason is not None:
        return None, len(angles), distribution_reason
    checked_samples = 0
    for side in (positions < 0.5, positions > 0.5):
        _, side_keep, _, side_reason = vertical_consensus(
            angles[side], positions[side], weights[side], public_mode=False
        )
        if side_reason is not None:
            return (
                None,
                checked_samples + int(np.count_nonzero(side_keep)),
                side_reason,
            )
        if np.count_nonzero(side_keep) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE:
            return None, checked_samples, "insufficient_robust_structural_validation"
        checked_samples += int(np.count_nonzero(side_keep))
    angles = angles[consensus]
    positions = positions[consensus]
    weights = weights[consensus]
    clusters = [clusters[index] for index in np.flatnonzero(consensus)]
    _, repeated_consensus, _, consensus_reason = vertical_consensus(
        angles, positions, weights, public_mode=False
    )
    if consensus_reason is not None:
        return None, int(np.count_nonzero(repeated_consensus)), consensus_reason
    distribution_reason = vertical_fit_distribution_reason(
        positions, minimum_balance=0.40
    )
    if distribution_reason is not None:
        return None, len(angles), f"{distribution_reason}_after_consensus_filtering"
    reason = vertical_side_system_reason(
        positions,
        clusters,
        minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
    )
    if reason is not None:
        return None, len(angles), f"{reason}_after_consensus_filtering"
    side_angles: list[float] = []
    total_samples = 0
    for side in (positions < 0.5, positions > 0.5):
        values = angles[side]
        side_positions = positions[side]
        side_weights = weights[side]
        _, side_keep, _, side_reason = vertical_consensus(
            values, side_positions, side_weights
        )
        if side_reason is not None:
            return None, total_samples + int(np.count_nonzero(side_keep)), side_reason
        if np.count_nonzero(side_keep) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE:
            return None, total_samples, "insufficient_robust_structural_validation"
        side_angles.append(
            float(np.average(values[side_keep], weights=side_weights[side_keep]))
        )
        total_samples += int(np.count_nonzero(side_keep))
    return abs(side_angles[1] - side_angles[0]), total_samples, None


def vertical_distribution_reason(positions: np.ndarray) -> str | None:
    if len(positions) < MINIMUM_VERTICAL_STRUCTURES:
        return "insufficient_independent_vertical_structures"
    centered = positions - 0.5
    left = positions[centered < 0]
    right = positions[centered > 0]
    if (
        len(left) < MINIMUM_VERTICAL_STRUCTURES_PER_SIDE
        or len(right) < MINIMUM_VERTICAL_STRUCTURES_PER_SIDE
    ):
        return "insufficient_independent_structures_on_both_sides"
    if min(len(left), len(right)) / max(len(left), len(right)) < MINIMUM_VERTICAL_SIDE_BALANCE:
        return "unbalanced_vertical_structure_counts"
    if np.ptp(left) < MINIMUM_VERTICAL_SIDE_SPREAD or np.ptp(right) < MINIMUM_VERTICAL_SIDE_SPREAD:
        return "insufficient_vertical_structure_spread_on_both_sides"
    bands = np.clip(
        (positions * VERTICAL_X_BAND_COUNT).astype(int),
        0,
        VERTICAL_X_BAND_COUNT - 1,
    )
    midpoint = VERTICAL_X_BAND_COUNT // 2
    if (
        len(np.unique(bands[bands < midpoint])) < MINIMUM_VERTICAL_X_BANDS_PER_SIDE
        or len(np.unique(bands[bands >= midpoint]))
        < MINIMUM_VERTICAL_X_BANDS_PER_SIDE
    ):
        return "insufficient_independent_vertical_x_bands_on_both_sides"
    return None


def vertical_fit_distribution_reason(
    positions: np.ndarray,
    *,
    minimum_balance: float = MINIMUM_VERTICAL_SIDE_BALANCE,
) -> str | None:
    if len(positions) < MINIMUM_VERTICAL_FIT_STRUCTURES:
        return "insufficient_disjoint_vertical_fit_structures"
    left = positions[positions < 0.5]
    right = positions[positions > 0.5]
    if (
        len(left) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE
        or len(right) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE
    ):
        return "insufficient_disjoint_vertical_fit_structures_on_both_sides"
    if min(len(left), len(right)) / max(len(left), len(right)) < minimum_balance:
        return "unbalanced_vertical_structure_counts"
    if np.ptp(left) < MINIMUM_VERTICAL_SIDE_SPREAD or np.ptp(right) < MINIMUM_VERTICAL_SIDE_SPREAD:
        return "insufficient_vertical_structure_spread_on_both_sides"
    bands = np.clip(
        (positions * VERTICAL_X_BAND_COUNT).astype(int),
        0,
        VERTICAL_X_BAND_COUNT - 1,
    )
    midpoint = VERTICAL_X_BAND_COUNT // 2
    if (
        len(np.unique(bands[bands < midpoint])) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE
        or len(np.unique(bands[bands >= midpoint]))
        < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE
    ):
        return "insufficient_independent_vertical_x_bands_on_both_sides"
    return None


def vertical_model(
    gray: np.ndarray, *, detailed: bool = False
) -> (
    tuple[np.ndarray | None, int, str | None]
    | tuple[np.ndarray | None, int, str | None, np.ndarray, set[int]]
):
    a, positions, w, clusters = clustered_vertical_structures(gray, detailed=True)
    total_samples = len(a)
    if len(a) < MINIMUM_VERTICAL_STRUCTURES:
        result = (
            None,
            len(a),
            "insufficient_independent_vertical_structures",
        )
        return (*result, np.empty(0), set()) if detailed else result
    consensus_model, consensus, angular_mad, consensus_reason = vertical_consensus(
        a, positions, w
    )
    if consensus_reason is not None:
        result = (
            None,
            int(np.count_nonzero(consensus)),
            consensus_reason,
        )
        return (*result, np.empty(0), set()) if detailed else result
    distribution_reason = vertical_distribution_reason(positions)
    if distribution_reason is not None:
        result = (None, len(a), distribution_reason)
        return (*result, np.empty(0), set()) if detailed else result
    system_reason = vertical_side_system_reason(
        positions,
        clusters,
        minimum_per_side=MINIMUM_VERTICAL_STRUCTURES_PER_SIDE,
    )
    if system_reason is not None:
        result = (None, len(a), system_reason)
        return (*result, np.empty(0), set()) if detailed else result
    a = a[consensus]
    positions = positions[consensus]
    w = w[consensus]
    clusters = [
        clusters[index] for index in np.flatnonzero(consensus)
    ]
    _, repeated_consensus, _, consensus_reason = vertical_consensus(
        a, positions, w, public_mode=False
    )
    if consensus_reason is not None:
        result = (
            None,
            int(np.count_nonzero(repeated_consensus)),
            f"{consensus_reason}_after_consensus_filtering",
        )
        return (*result, np.empty(0), set()) if detailed else result
    distribution_reason = vertical_distribution_reason(positions)
    if distribution_reason is not None:
        result = (
            None,
            len(a),
            f"{distribution_reason}_after_consensus_filtering",
        )
        return (*result, np.empty(0), set()) if detailed else result
    system_reason = vertical_side_system_reason(
        positions,
        clusters,
        minimum_per_side=MINIMUM_VERTICAL_STRUCTURES_PER_SIDE,
    )
    if system_reason is not None:
        result = (None, len(a), f"{system_reason}_after_consensus_filtering")
        return (*result, np.empty(0), set()) if detailed else result
    bands = np.clip(
        (positions * VERTICAL_X_BAND_COUNT).astype(int),
        0,
        VERTICAL_X_BAND_COUNT - 1,
    )
    fit_bands = set(range(0, VERTICAL_X_BAND_COUNT, 2))
    holdout_bands = set(range(1, VERTICAL_X_BAND_COUNT, 2))
    fit = np.asarray([band in fit_bands for band in bands])
    fit_reason = vertical_fit_distribution_reason(positions[fit])
    if fit_reason is not None:
        result = (None, len(a), fit_reason)
        return (*result, np.empty(0), set()) if detailed else result
    holdout_angles = a[~fit].copy()
    holdout_positions = positions[~fit].copy()
    holdout_weights = w[~fit].copy()
    holdout_clusters = [
        clusters[index] for index in np.flatnonzero(~fit)
    ]
    fitted_structures = []
    for index in np.flatnonzero(fit):
        cluster = clusters[index]
        position = positions[index]
        angle = a[index]
        position_uncertainty = max(
            abs(item[0] - position) + item[3] for item in cluster
        )
        angle_uncertainty = max(
            VERTICAL_CLUSTER_ANGLE_UNCERTAINTY_DEGREES,
            max(abs(item[1] - angle) for item in cluster),
        )
        fitted_structures.append(
            (
                position,
                angle,
                max(item[4] for item in cluster),
                position_uncertainty,
                angle_uncertainty,
            )
        )
    fitted_structures_array = np.asarray(fitted_structures, dtype=np.float64)
    a, positions, w = a[fit], positions[fit], w[fit]
    fit_clusters = [clusters[index] for index in np.flatnonzero(fit)]
    x = positions - 0.5
    design = np.column_stack((np.ones_like(x), x))
    for _ in range(4):
        coefficient = np.linalg.lstsq(
            design * np.sqrt(w)[:, None], a * np.sqrt(w), rcond=None
        )[0]
        residual = a - design @ coefficient
        limit = max(0.12, weighted_median(np.abs(residual), w) * 3)
        keep = np.abs(residual) <= limit
        a, positions, w, design = (
            a[keep],
            positions[keep],
            w[keep],
            design[keep],
        )
        fit_clusters = [
            fit_clusters[index] for index in np.flatnonzero(keep)
        ]
        distribution_reason = vertical_fit_distribution_reason(positions)
        if distribution_reason is not None:
            result = (
                None,
                len(a),
                f"{distribution_reason}_after_outlier_rejection",
            )
            return (*result, np.empty(0), set()) if detailed else result
        _, stage_consensus, _, consensus_reason = vertical_consensus(
            a, positions, w, public_mode=False
        )
        if consensus_reason is not None:
            result = (
                None,
                int(np.count_nonzero(stage_consensus)),
                f"{consensus_reason}_after_outlier_rejection",
            )
            return (*result, np.empty(0), set()) if detailed else result
        a, positions, w, design = (
            a[stage_consensus],
            positions[stage_consensus],
            w[stage_consensus],
            design[stage_consensus],
        )
        fit_clusters = [
            fit_clusters[index] for index in np.flatnonzero(stage_consensus)
        ]
        x = positions - 0.5
        design = np.column_stack((np.ones_like(x), x))
        distribution_reason = vertical_fit_distribution_reason(positions)
        if distribution_reason is not None:
            result = (
                None,
                len(a),
                f"{distribution_reason}_after_outlier_rejection",
            )
            return (*result, np.empty(0), set()) if detailed else result
        system_reason = vertical_side_system_reason(
            positions,
            fit_clusters,
            minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
        )
        if system_reason is not None:
            result = (
                None,
                len(a),
                f"{system_reason}_after_outlier_rejection",
            )
            return (*result, np.empty(0), set()) if detailed else result
    coefficient = np.linalg.lstsq(
        design * np.sqrt(w)[:, None], a * np.sqrt(w), rcond=None
    )[0]
    edges = np.array((coefficient[0] - coefficient[1] / 2, coefficient[0] + coefficient[1] / 2))
    if abs(coefficient[1]) / 2 > 0.8:
        result = (
            None,
            len(a),
            "estimated_convergence_exceeds_small_angle_capability",
        )
        return (*result, np.empty(0), set()) if detailed else result
    selected_structures: list[VerticalStructure] = []
    selected_cluster_groups = fit_clusters + holdout_clusters
    selected_systems = vertical_system_identities(selected_cluster_groups)
    selected_system_index = 0
    for role, selected_angles, selected_positions, selected_weights, selected_clusters in (
        ("fit", a, positions, w, fit_clusters),
        (
            "holdout",
            holdout_angles,
            holdout_positions,
            holdout_weights,
            holdout_clusters,
        ),
    ):
        for angle, position, weight, cluster in zip(
            selected_angles,
            selected_positions,
            selected_weights,
            selected_clusters,
        ):
            selected_structures.append(
                VerticalStructure(
                    float(angle),
                    float(position),
                    float(weight),
                    max(float(item[4]) for item in cluster),
                    max(
                        abs(float(item[0]) - float(position)) + float(item[3])
                        for item in cluster
                    ),
                    max(
                        VERTICAL_CLUSTER_ANGLE_UNCERTAINTY_DEGREES,
                        max(
                            abs(float(item[1]) - float(angle))
                            for item in cluster
                        ),
                    ),
                    min(float(item[5]) for item in cluster),
                    max(float(item[6]) for item in cluster),
                    int(
                        np.clip(
                            float(position) * VERTICAL_X_BAND_COUNT,
                            0,
                            VERTICAL_X_BAND_COUNT - 1,
                        )
                    ),
                    role,
                    int(selected_systems[selected_system_index]),
                )
            )
            selected_system_index += 1
    selection = VerticalSelection(
        gray.shape[1],
        gray.shape[0],
        tuple(selected_structures),
    )
    fitted_structures_array = VerticalFitStructures(
        fitted_structures_array,
        selection,
    )
    result = (coefficient, total_samples, None)
    return (*result, fitted_structures_array, holdout_bands) if detailed else result


def vertical_measurement(
    gray: np.ndarray,
) -> tuple[np.ndarray | None, int, str | None, np.ndarray, set[int]]:
    result = vertical_model(gray, detailed=True)
    if len(result) == 3:
        model, samples, reason = result
        return model, samples, reason, np.empty(0), set()
    return result


def vertical_selection_from_fit(
    fitted_structures: np.ndarray,
) -> VerticalSelection | None:
    return getattr(fitted_structures, "selection", None)


def project_vertical_selection(
    selection: VerticalSelection,
    convergence_slope: float,
) -> VerticalSelection:
    projected: list[VerticalStructure] = []
    for structure in selection.structures:
        center_x = structure.position * selection.width
        slope = float(np.tan(np.radians(structure.angle)))
        source_y = np.asarray(
            [structure.top * selection.height, structure.bottom * selection.height],
            dtype=np.float32,
        )
        source_x = center_x + (
            source_y - (selection.height - 1) / 2
        ) * slope
        destination_x, destination_y = vertical_forward_points(
            source_x,
            source_y,
            selection.width,
            selection.height,
            convergence_slope,
        )
        dy = float(destination_y[1] - destination_y[0])
        dx = float(destination_x[1] - destination_x[0])
        projected.append(
            structure._replace(
                angle=float(np.degrees(np.arctan2(dx, max(dy, 1e-6)))),
                position=float(
                    (
                        destination_x[0]
                        + (
                            (selection.height - 1) / 2 - destination_y[0]
                        )
                        * dx
                        / max(dy, 1e-6)
                    )
                    / selection.width
                ),
                top=float(destination_y[0] / selection.height),
                bottom=float(destination_y[1] / selection.height),
            )
        )
    return VerticalSelection(selection.width, selection.height, tuple(projected))


def selected_vertical_measurement(
    selection: VerticalSelection,
) -> tuple[np.ndarray | None, int, str | None, float | None, int, str | None]:
    fit = [structure for structure in selection.structures if structure.role == "fit"]
    holdout = [
        structure for structure in selection.structures if structure.role == "holdout"
    ]
    if not fit:
        return (
            None,
            0,
            "missing_tracked_fit_vertical_evidence",
            None,
            0,
            "tracked_fit_vertical_evidence_is_insufficient",
        )
    fit_angles = np.asarray([structure.angle for structure in fit])
    fit_positions = np.asarray([structure.position for structure in fit])
    fit_weights = np.asarray([structure.weight for structure in fit])
    fit_clusters = [
        [
            (
                structure.position,
                structure.angle,
                structure.weight,
                structure.position_uncertainty,
                structure.half_width,
                structure.top,
                structure.bottom,
            )
        ]
        for structure in fit
    ]
    fit_system_reason = vertical_side_system_reason(
        fit_positions,
        fit_clusters,
        minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
        system_ids=np.asarray([structure.system for structure in fit]),
    )
    fit_reason = vertical_fit_distribution_reason(fit_positions)
    if fit_reason is None:
        fit_reason = fit_system_reason
    if fit_reason is None:
        model, fit_keep, _, fit_reason = vertical_consensus(
            fit_angles,
            fit_positions,
            fit_weights,
            public_mode=False,
        )
    else:
        model = None
        fit_keep = np.zeros(len(fit), dtype=bool)
    if fit_reason is not None:
        return (
            None,
            int(np.count_nonzero(fit_keep)),
            fit_reason,
            None,
            len(holdout),
            "tracked_fit_vertical_evidence_is_insufficient",
        )
    if not holdout:
        return (
            model,
            len(fit),
            None,
            None,
            0,
            "missing_tracked_holdout_vertical_evidence",
        )
    holdout_angles = np.asarray([structure.angle for structure in holdout])
    holdout_positions = np.asarray([structure.position for structure in holdout])
    holdout_weights = np.asarray([structure.weight for structure in holdout])
    holdout_clusters = [
        [
            (
                structure.position,
                structure.angle,
                structure.weight,
                structure.position_uncertainty,
                structure.half_width,
                structure.top,
                structure.bottom,
            )
        ]
        for structure in holdout
    ]
    distribution_reason = vertical_fit_distribution_reason(
        holdout_positions,
        minimum_balance=0.40,
    )
    if distribution_reason is None:
        distribution_reason = vertical_side_system_reason(
            holdout_positions,
            holdout_clusters,
            minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
            system_ids=np.asarray(
                [structure.system for structure in holdout]
            ),
        )
    if distribution_reason is not None:
        return (
            model,
            len(fit),
            None,
            None,
            len(holdout),
            distribution_reason,
        )
    _, holdout_keep, _, holdout_reason = vertical_consensus(
        holdout_angles,
        holdout_positions,
        holdout_weights,
        public_mode=False,
    )
    if holdout_reason is not None:
        return (
            model,
            len(fit),
            None,
            None,
            int(np.count_nonzero(holdout_keep)),
            holdout_reason,
        )
    retained_holdout = [
        structure
        for structure, retained in zip(holdout, holdout_keep)
        if retained
    ]
    retained_positions = holdout_positions[holdout_keep]
    retained_clusters = [
        cluster
        for cluster, retained in zip(holdout_clusters, holdout_keep)
        if retained
    ]
    holdout_reason = vertical_side_system_reason(
        retained_positions,
        retained_clusters,
        minimum_per_side=MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE,
        system_ids=np.asarray(
            [structure.system for structure in retained_holdout]
        ),
    )
    if holdout_reason is not None:
        return (
            model,
            len(fit),
            None,
            None,
            int(np.count_nonzero(holdout_keep)),
            f"{holdout_reason}_after_consensus_filtering",
        )
    side_angles: list[float] = []
    retained_holdout_keep = np.zeros(len(holdout_keep), dtype=bool)
    for side in (holdout_positions < 0.5, holdout_positions > 0.5):
        if np.count_nonzero(side) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE:
            return (
                model,
                len(fit),
                None,
                None,
                int(np.count_nonzero(holdout_keep)),
                "insufficient_tracked_holdout_vertical_evidence_on_both_sides",
            )
        _, side_keep, _, side_reason = vertical_consensus(
            holdout_angles[side],
            holdout_positions[side],
            holdout_weights[side],
            public_mode=False,
        )
        side_consensus = np.zeros(len(holdout_keep), dtype=bool)
        side_consensus[side] = side_keep
        retained = side_consensus & holdout_keep
        retained_holdout_keep |= retained
        if side_reason is not None:
            return (
                model,
                len(fit),
                None,
                None,
                int(np.count_nonzero(retained_holdout_keep)),
                side_reason,
            )
        if np.count_nonzero(retained) < MINIMUM_VERTICAL_FIT_STRUCTURES_PER_SIDE:
            return (
                model,
                len(fit),
                None,
                None,
                int(np.count_nonzero(retained_holdout_keep)),
                "insufficient_tracked_holdout_vertical_evidence_on_both_sides",
            )
        side_angles.append(
            float(
                np.average(
                    holdout_angles[retained],
                    weights=holdout_weights[retained],
                )
            )
        )
    return (
        model,
        len(fit),
        None,
        abs(side_angles[1] - side_angles[0]),
        int(np.count_nonzero(retained_holdout_keep)),
        None,
    )


def tracked_vertical_measurement(
    gray: np.ndarray,
    selection: VerticalSelection,
    convergence_slope: float,
) -> tuple[
    np.ndarray | None,
    int,
    str | None,
    float | None,
    int,
    str | None,
    VerticalSelection,
]:
    projected = project_vertical_selection(selection, convergence_slope)
    angles, positions, weights, clusters = clustered_vertical_structures(
        gray,
        detailed=True,
    )
    possible: dict[int, list[tuple[float, int]]] = {}
    reverse: dict[int, list[tuple[float, int]]] = {}
    for expected_index, expected in enumerate(projected.structures):
        expected_span = max(expected.bottom - expected.top, 1e-6)
        for index, cluster in enumerate(clusters):
            top = min(float(item[5]) for item in cluster)
            bottom = max(float(item[6]) for item in cluster)
            overlap = max(0.0, min(bottom, expected.bottom) - max(top, expected.top))
            overlap_ratio = overlap / max(expected_span, bottom - top, 1e-6)
            angle_tolerance = min(
                VERTICAL_TRACK_ANGLE_TOLERANCE_DEGREES,
                VERTICAL_CONFLICT_SEPARATION_DEGREES,
            )
            if (
                abs(float(positions[index]) - expected.position)
                <= VERTICAL_TRACK_POSITION_TOLERANCE
                + expected.position_uncertainty
                and abs(float(angles[index]) - expected.angle) < angle_tolerance
                and overlap_ratio >= VERTICAL_TRACK_MINIMUM_Y_OVERLAP_RATIO
            ):
                cost = (
                    abs(float(positions[index]) - expected.position)
                    + abs(float(angles[index]) - expected.angle) * 0.02
                    + (1.0 - overlap_ratio) * 0.01
                )
                possible.setdefault(expected_index, []).append((cost, index))
                reverse.setdefault(index, []).append((cost, expected_index))

    matched: list[VerticalStructure] = []
    matched_expected: set[int] = set()
    for expected_index, expected in enumerate(projected.structures):
        plausible = possible.get(expected_index, [])
        if len(plausible) != 1:
            continue
        cost, index = plausible[0]
        if len(reverse[index]) != 1:
            continue
        reverse_cost, reverse_expected = reverse[index][0]
        if reverse_expected != expected_index or reverse_cost != cost:
            continue
        cluster = clusters[index]
        matched_expected.add(expected_index)
        matched.append(
            expected._replace(
                angle=float(angles[index]),
                position=float(positions[index]),
                weight=float(weights[index]),
                half_width=max(float(item[4]) for item in cluster),
                top=min(float(item[5]) for item in cluster),
                bottom=max(float(item[6]) for item in cluster),
            )
        )
    missing_fit = any(
        index not in matched_expected and structure.role == "fit"
        for index, structure in enumerate(projected.structures)
    )
    missing_holdout = any(
        index not in matched_expected and structure.role == "holdout"
        for index, structure in enumerate(projected.structures)
    )
    measured = VerticalSelection(projected.width, projected.height, tuple(matched))
    model, samples, model_reason, validation, validation_samples, validation_reason = (
        selected_vertical_measurement(measured)
    )
    if missing_fit:
        model = None
        model_reason = "missing_tracked_fit_vertical_evidence"
    if missing_holdout:
        validation = None
        validation_reason = "missing_tracked_holdout_vertical_evidence"
    return (
        model,
        samples,
        model_reason,
        validation,
        validation_samples,
        validation_reason,
        projected,
    )


def vertical_edges(model: np.ndarray | None) -> tuple[float, float]:
    if model is None:
        return 0.0, 0.0
    return (
        float(model[0] - model[1] / 2),
        float(model[0] + model[1] / 2),
    )


def convergence_residual(model: np.ndarray | None) -> float | None:
    if model is None:
        return None
    left, right = vertical_edges(model)
    return abs(right - left)


def unsupported_dimension_metrics(reason: str) -> dict[str, object]:
    metrics: dict[str, object] = {
        "status": "review_required",
        "review_required": True,
        "horizontal_angle": 0.0,
        "horizontal_samples": 0,
        "horizontal_status": "review_required",
        "horizontal_reason": reason,
        "vertical_samples": 0,
        "vertical_status": "review_required",
        "vertical_reason": reason,
        "vertical_left_angle": 0.0,
        "vertical_right_angle": 0.0,
        "horizontal_before_angle": 0.0,
        "horizontal_after_angle": 0.0,
        "horizontal_samples_after": 0,
        "horizontal_consensus_reason_before": reason,
        "horizontal_holdout_before_angle": None,
        "horizontal_holdout_samples_before": 0,
        "horizontal_holdout_reason_before": reason,
        "horizontal_holdout_after_angle": None,
        "horizontal_holdout_samples_after": 0,
        "horizontal_holdout_reason_after": reason,
        "horizontal_applied": False,
        "horizontal_reverted": False,
        "horizontal_candidate_clipped": False,
        "horizontal_transformed_out_ink_ratio": 0.0,
        "horizontal_candidate_ink_loss_ratio": 0.0,
        "horizontal_candidate_rejection_reason": reason,
        "horizontal_validation_vertical_before_left_angle": 0.0,
        "horizontal_validation_vertical_before_right_angle": 0.0,
        "horizontal_validation_vertical_samples_before": 0,
        "horizontal_validation_vertical_model_reason_before": reason,
        "horizontal_validation_vertical_after_left_angle": 0.0,
        "horizontal_validation_vertical_after_right_angle": 0.0,
        "horizontal_validation_vertical_samples_after": 0,
        "horizontal_validation_vertical_model_reason_after": reason,
        "horizontal_validation_convergence_differential_before": None,
        "horizontal_validation_convergence_differential_after": None,
        "vertical_before_left_angle": 0.0,
        "vertical_before_right_angle": 0.0,
        "vertical_before_convergence_differential": 0.0,
        "vertical_before_common_tilt": 0.0,
        "vertical_after_left_angle": 0.0,
        "vertical_after_right_angle": 0.0,
        "vertical_after_convergence_differential": 0.0,
        "vertical_after_common_tilt": 0.0,
        "vertical_samples_after": 0,
        "vertical_after_model_reason": reason,
        "vertical_validation_before_convergence_differential": None,
        "vertical_validation_samples_before": 0,
        "vertical_validation_reason_before": reason,
        "vertical_validation_after_convergence_differential": None,
        "vertical_validation_samples_after": 0,
        "vertical_validation_reason_after": reason,
        "vertical_applied": False,
        "vertical_reverted": False,
        "vertical_candidate_clipped": False,
        "vertical_transformed_out_ink_ratio": 0.0,
        "vertical_candidate_ink_loss_ratio": 0.0,
        "vertical_candidate_rejection_reason": reason,
        "cumulative_clipping_reverted": False,
        "cumulative_transformed_out_ink_ratio": 0.0,
        "cumulative_ink_loss_ratio": 0.0,
        "original_to_final_clipping_reverted": False,
        "original_to_final_transformed_out_ink_ratio": 0.0,
        "original_to_final_ink_loss_ratio": 0.0,
        "unchanged": True,
    }
    return metrics


def rectify(image: np.ndarray) -> tuple[np.ndarray, dict[str, object]]:
    dimension_reason = opencv_dimension_reason(image)
    if dimension_reason is not None:
        return image, unsupported_dimension_metrics(dimension_reason)
    (
        horizontal_before,
        horizontal_samples_before,
        horizontal_model_reason_before,
        horizontal_holdout_before,
        horizontal_holdout_samples_before,
        horizontal_holdout_reason_before,
        horizontal_selection_before,
    ) = horizontal_measurement(image)
    (
        horizontal_check_vertical_model_before,
        horizontal_check_vertical_samples_before,
        horizontal_check_vertical_reason_before,
        _,
        _,
    ) = vertical_measurement(image)
    horizontal_check_vertical_edges_before = vertical_edges(
        horizontal_check_vertical_model_before
    )
    horizontal_check_vertical_model_after = None
    horizontal_check_vertical_samples_after = 0
    horizontal_check_vertical_reason_after = "horizontal_candidate_not_evaluated"
    horizontal_check_vertical_edges_after = (0.0, 0.0)
    horizontal_validation_convergence_before = convergence_residual(
        horizontal_check_vertical_model_before
    )
    horizontal_validation_convergence_after = None
    height, width = image.shape
    corrected = image
    original_forward_x = np.broadcast_to(
        np.arange(width, dtype=np.float32)[None, :], (height, width)
    ).copy()
    original_forward_y = np.broadcast_to(
        np.arange(height, dtype=np.float32)[:, None], (height, width)
    ).copy()
    horizontal_applied = False
    horizontal_reverted = False
    horizontal_clipped = False
    horizontal_transformed_out_ink_ratio = 0.0
    horizontal_candidate_ink_loss_ratio = 0.0
    horizontal_candidate_rejection_reason = None
    horizontal_final_selection = horizontal_selection_before
    horizontal_holdout_after = None
    horizontal_holdout_samples_after = 0
    horizontal_holdout_reason_after = "horizontal_candidate_not_evaluated"
    if horizontal_model_reason_before == "conflicting_horizontal_modes":
        horizontal_status = "review_required"
        horizontal_reason = horizontal_model_reason_before
    elif horizontal_samples_before < 10 or horizontal_model_reason_before is not None:
        horizontal_status = "low_confidence"
        horizontal_reason = (
            horizontal_model_reason_before
            or "insufficient_long_near_horizontal_segments"
        )
    elif abs(horizontal_before) < 0.04:
        horizontal_status = "not_needed"
        horizontal_reason = "measured_rotation_below_application_threshold"
    else:
        horizontal_status = "candidate"
        horizontal_reason = None
    if horizontal_status == "candidate":
        matrix = cv2.getRotationMatrix2D(
            (width / 2, height / 2), horizontal_before, 1.0
        )
        candidate = cv2.warpAffine(
            image, matrix, (width, height), flags=cv2.INTER_CUBIC,
            borderMode=cv2.BORDER_CONSTANT, borderValue=white_value(image)
        )
        (
            residual,
            residual_samples,
            residual_reason,
            horizontal_holdout_after,
            horizontal_holdout_samples_after,
            horizontal_holdout_reason_after,
            _,
        ) = horizontal_measurement(
            candidate,
            horizontal_selection_before,
            matrix if horizontal_selection_before is not None else None,
        )
        (
            horizontal_check_vertical_model_after,
            horizontal_check_vertical_samples_after,
            horizontal_check_vertical_reason_after,
            _,
            _,
        ) = vertical_measurement(candidate)
        horizontal_check_vertical_edges_after = vertical_edges(
            horizontal_check_vertical_model_after
        )
        horizontal_validation_convergence_after = convergence_residual(
            horizontal_check_vertical_model_after
        )
        (
            horizontal_clipped,
            horizontal_transformed_out_ink_ratio,
            horizontal_candidate_ink_loss_ratio,
        ) = clipping_metrics(image, candidate, source_to_destination=matrix)
        vertical_not_worse = (
            horizontal_validation_convergence_before is None
            or (
                horizontal_validation_convergence_after is not None
                and horizontal_validation_convergence_after
                <= horizontal_validation_convergence_before
                + INDEPENDENT_AXIS_EPSILON_DEGREES
            )
        )
        holdout_improved = (
            horizontal_holdout_before is not None
            and horizontal_holdout_reason_before is None
            and horizontal_holdout_after is not None
            and horizontal_holdout_reason_after is None
            and materially_improved(
                abs(horizontal_holdout_before),
                abs(horizontal_holdout_after),
            )
        )
        if (
            residual_samples >= 10
            and residual_reason is None
            and materially_improved(abs(horizontal_before), abs(residual))
            and holdout_improved
            and vertical_not_worse
            and not horizontal_clipped
        ):
            corrected = candidate
            source_x = original_forward_x
            source_y = original_forward_y
            original_forward_x = (
                matrix[0, 0] * source_x
                + matrix[0, 1] * source_y
                + matrix[0, 2]
            )
            original_forward_y = (
                matrix[1, 0] * source_x
                + matrix[1, 1] * source_y
                + matrix[1, 2]
            )
            horizontal_applied = True
            if horizontal_selection_before is not None:
                horizontal_final_selection = project_horizontal_selection(
                    horizontal_selection_before,
                    matrix,
                )
            horizontal_status = "applied"
            if horizontal_validation_convergence_before is None:
                horizontal_reason = (
                    "remeasured_horizontal_residual_improved_while_vertical_"
                    "convergence_was_unavailable"
                )
            else:
                horizontal_reason = (
                    "material_horizontal_residual_improvement_without_"
                    "convergence_worsening"
                )
        else:
            horizontal_reverted = True
            horizontal_status = "reverted"
            if residual_samples < 10 or residual_reason is not None:
                horizontal_reason = (
                    "remeasurement_had_insufficient_horizontal_evidence:"
                    f"{residual_reason}"
                )
            elif horizontal_clipped:
                horizontal_reason = "candidate_would_clip_source_ink"
            elif not materially_improved(
                abs(horizontal_before), abs(residual)
            ):
                horizontal_reason = (
                    "remeasured_horizontal_residual_did_not_materially_improve"
                )
            elif not holdout_improved:
                if (
                    horizontal_holdout_before is None
                    or horizontal_holdout_reason_before is not None
                    or horizontal_holdout_after is None
                    or horizontal_holdout_reason_after is not None
                ):
                    horizontal_reason = "held_out_horizontal_evidence_is_insufficient"
                else:
                    horizontal_reason = (
                        "held_out_horizontal_residual_did_not_materially_improve"
                    )
            elif horizontal_check_vertical_model_after is None:
                horizontal_reason = (
                    "remeasurement_had_insufficient_vertical_evidence:"
                    f"{horizontal_check_vertical_reason_after}"
                )
            else:
                horizontal_reason = "remeasured_convergence_residual_worsened"
            horizontal_candidate_rejection_reason = horizontal_reason

    (
        horizontal_after_rotation,
        horizontal_samples_after_rotation,
        _,
        _,
        _,
        _,
        _,
    ) = horizontal_measurement(corrected)
    (
        model_before,
        vertical_samples_before,
        vertical_model_reason_before,
        vertical_fit_positions_before,
        vertical_holdout_bands_before,
    ) = vertical_measurement(corrected)
    vertical_selection_before = vertical_selection_from_fit(
        vertical_fit_positions_before
    )
    if vertical_selection_before is None:
        (
            vertical_validation_before,
            vertical_validation_samples_before,
            vertical_validation_reason_before,
        ) = vertical_region_validation(
            corrected,
            vertical_fit_positions_before,
            vertical_holdout_bands_before,
        )
    else:
        (
            selected_model_before,
            _,
            selected_model_reason_before,
            vertical_validation_before,
            vertical_validation_samples_before,
            vertical_validation_reason_before,
        ) = selected_vertical_measurement(vertical_selection_before)
        if selected_model_before is None:
            model_before = None
            vertical_model_reason_before = selected_model_reason_before
        else:
            model_before = selected_model_before
    vertical_validation_after = None
    vertical_validation_samples_after = 0
    vertical_validation_reason_after = "vertical_candidate_not_evaluated"
    edges_before = vertical_edges(model_before)
    convergence_differential_before = abs(edges_before[1] - edges_before[0])
    common_vertical_tilt_before = abs((edges_before[0] + edges_before[1]) / 2)
    vertical_applied = False
    vertical_reverted = False
    vertical_source = corrected
    vertical_source_forward_x = original_forward_x
    vertical_source_forward_y = original_forward_y
    horizontal_selection_before_vertical = horizontal_final_selection
    vertical_clipped = False
    vertical_transformed_out_ink_ratio = 0.0
    vertical_candidate_ink_loss_ratio = 0.0
    vertical_candidate_rejection_reason = None
    if model_before is None:
        vertical_status = (
            "review_required"
            if vertical_model_reason_before == "conflicting_evidence"
            else "low_confidence"
        )
        vertical_reason = vertical_model_reason_before
    elif vertical_validation_reason_before == "conflicting_evidence":
        vertical_status = "review_required"
        vertical_reason = "conflicting_evidence"
    elif (
        convergence_differential_before
        < MINIMUM_CONVERGENCE_DIFFERENTIAL_DEGREES
    ):
        if common_vertical_tilt_before >= APPLICATION_THRESHOLD_DEGREES:
            vertical_status = "review_required"
            if horizontal_samples_after_rotation >= 10:
                vertical_reason = (
                    "common_vertical_tilt_without_meaningful_convergence;"
                    "independent_horizontal_orientation_does_not_support_an_"
                    "additional_rotation"
                )
            else:
                vertical_reason = (
                    "common_vertical_tilt_without_meaningful_convergence_and_"
                    "insufficient_independent_orientation_evidence"
                )
        else:
            vertical_status = "not_needed"
            vertical_reason = "measured_convergence_below_application_threshold"
    elif convergence_differential_before < APPLICATION_THRESHOLD_DEGREES:
        vertical_status = "not_needed"
        vertical_reason = "measured_convergence_below_application_threshold"
    else:
        vertical_status = "candidate"
        vertical_reason = None
    if (
        model_before is not None
        and convergence_differential_before
        >= MINIMUM_CONVERGENCE_DIFFERENTIAL_DEGREES
        and vertical_validation_reason_before != "conflicting_evidence"
        and (
            vertical_selection_before is None
            or vertical_validation_before is not None
        )
    ):
        x = np.arange(width, dtype=np.float32)
        normalized = x / (width - 1) - 0.5
        local_angle = model_before[1] * normalized
        slope = np.tan(np.radians(local_angle)).astype(np.float32)
        y_offset = np.arange(height, dtype=np.float32) - (height - 1) / 2
        map_x = x[None, :] + y_offset[:, None] * slope[None, :]
        map_y = np.broadcast_to(np.arange(height, dtype=np.float32)[:, None], (height, width))
        candidate = cv2.remap(
            corrected, map_x, map_y, cv2.INTER_CUBIC,
            borderMode=cv2.BORDER_CONSTANT, borderValue=white_value(image)
        )
        if vertical_selection_before is None:
            (
                model_after,
                vertical_samples_after,
                vertical_model_reason_after,
                vertical_fit_positions_after,
                vertical_holdout_bands_after,
            ) = vertical_measurement(candidate)
            (
                vertical_validation_after,
                vertical_validation_samples_after,
                vertical_validation_reason_after,
            ) = vertical_region_validation(
                candidate,
                vertical_fit_positions_after,
                vertical_holdout_bands_after,
            )
        else:
            (
                model_after,
                vertical_samples_after,
                vertical_model_reason_after,
                vertical_validation_after,
                vertical_validation_samples_after,
                vertical_validation_reason_after,
                _,
            ) = tracked_vertical_measurement(
                candidate,
                vertical_selection_before,
                float(model_before[1]),
            )
        edges_after = vertical_edges(model_after)
        (
            candidate_horizontal,
            candidate_horizontal_samples,
            candidate_horizontal_reason,
            _,
            _,
            _,
            _,
        ) = horizontal_measurement(candidate)
        candidate_forward_x, candidate_forward_y = vertical_forward_points(
            np.broadcast_to(
                np.arange(width, dtype=np.float32)[None, :], (height, width)
            ),
            np.broadcast_to(
                np.arange(height, dtype=np.float32)[:, None], (height, width)
            ),
            width,
            height,
            float(model_before[1]),
        )
        (
            vertical_clipped,
            vertical_transformed_out_ink_ratio,
            vertical_candidate_ink_loss_ratio,
        ) = clipping_metrics(
            corrected,
            candidate,
            forward_x=candidate_forward_x,
            forward_y=candidate_forward_y,
        )
        horizontal_not_worse = (
            horizontal_samples_after_rotation < 10
            or (
                candidate_horizontal_samples >= 10
                and candidate_horizontal_reason is None
                and abs(candidate_horizontal)
                <= abs(horizontal_after_rotation)
                + INDEPENDENT_AXIS_EPSILON_DEGREES
            )
        )
        convergence_differential_after = abs(edges_after[1] - edges_after[0])
        if (
            model_after is not None
            and materially_improved(
                convergence_differential_before,
                convergence_differential_after,
            )
            and vertical_validation_before is not None
            and vertical_validation_after is not None
            and materially_improved(
                vertical_validation_before,
                vertical_validation_after,
            )
            and horizontal_not_worse
            and not vertical_clipped
        ):
            corrected = candidate
            original_forward_x, original_forward_y = vertical_forward_points(
                original_forward_x,
                original_forward_y,
                width,
                height,
                float(model_before[1]),
            )
            vertical_applied = True
            if horizontal_final_selection is not None:
                horizontal_final_selection = (
                    project_horizontal_selection_through_vertical(
                        horizontal_final_selection,
                        float(model_before[1]),
                    )
                )
            vertical_status = "applied"
            if horizontal_samples_after_rotation < 10:
                vertical_reason = (
                    "remeasured_convergence_residual_improved_while_horizontal_"
                    "residual_was_unavailable"
                )
            else:
                vertical_reason = (
                    "material_convergence_residual_improvement_without_"
                    "horizontal_residual_worsening"
                )
        else:
            vertical_reverted = True
            vertical_status = "reverted"
            if model_after is None:
                vertical_reason = (
                    "remeasurement_model_unavailable:"
                    f"{vertical_model_reason_after}"
                )
            elif vertical_clipped:
                vertical_reason = "candidate_would_clip_source_ink"
            elif not materially_improved(
                convergence_differential_before,
                convergence_differential_after,
            ):
                vertical_reason = (
                    "remeasured_convergence_residual_did_not_materially_improve"
                )
            elif vertical_validation_before is None:
                vertical_reason = (
                    "independent_region_validation_unavailable:"
                    f"{vertical_validation_reason_before}"
                )
            elif vertical_validation_after is None:
                vertical_reason = (
                    "independent_region_remeasurement_unavailable:"
                    f"{vertical_validation_reason_after}"
                )
            elif not materially_improved(
                vertical_validation_before,
                vertical_validation_after,
            ):
                vertical_reason = (
                    "independent_structural_convergence_did_not_materially_improve"
                )
            elif candidate_horizontal_samples < 10:
                vertical_reason = (
                    "remeasurement_had_insufficient_horizontal_evidence"
                )
            else:
                vertical_reason = "remeasured_horizontal_residual_worsened"
            vertical_candidate_rejection_reason = vertical_reason

    if horizontal_applied and horizontal_final_selection is None:
        (
            horizontal_after,
            horizontal_samples_after,
            horizontal_model_reason_after,
            horizontal_final_holdout_after,
            horizontal_final_holdout_samples_after,
            horizontal_final_holdout_reason_after,
        ) = (
            0.0,
            0,
            "missing_tracked_fit_horizontal_evidence",
            None,
            0,
            "missing_tracked_holdout_horizontal_evidence",
        )
    else:
        (
            horizontal_after,
            horizontal_samples_after,
            horizontal_model_reason_after,
            horizontal_final_holdout_after,
            horizontal_final_holdout_samples_after,
            horizontal_final_holdout_reason_after,
            _,
        ) = horizontal_measurement(
            corrected,
            horizontal_final_selection if horizontal_applied else None,
        )
    (
        model_after,
        vertical_samples_after,
        vertical_model_reason_after,
        _,
        _,
    ) = vertical_measurement(corrected)
    final_vertical_validation = vertical_validation_after
    final_vertical_validation_reason = vertical_validation_reason_after
    if (
        vertical_applied
        and vertical_selection_before is not None
        and model_before is not None
    ):
        (
            model_after,
            vertical_samples_after,
            vertical_model_reason_after,
            final_vertical_validation,
            _,
            final_vertical_validation_reason,
            _,
        ) = tracked_vertical_measurement(
            corrected,
            vertical_selection_before,
            float(model_before[1]),
        )
    edges_after = vertical_edges(model_after)
    final_convergence_residual = abs(edges_after[1] - edges_after[0])
    if horizontal_applied and (
        horizontal_samples_after < 10
        or horizontal_model_reason_after is not None
        or not materially_improved(abs(horizontal_before), abs(horizontal_after))
        or horizontal_holdout_before is None
        or horizontal_holdout_reason_before is not None
        or horizontal_final_holdout_after is None
        or horizontal_final_holdout_reason_after is not None
        or not materially_improved(
            abs(horizontal_holdout_before),
            abs(horizontal_final_holdout_after),
        )
    ):
        horizontal_status = "reverted"
        if (
            horizontal_model_reason_after is not None
            or horizontal_holdout_before is None
            or horizontal_holdout_reason_before is not None
            or horizontal_final_holdout_after is None
            or horizontal_final_holdout_reason_after is not None
        ):
            horizontal_reason = "final_independent_horizontal_evidence_is_insufficient"
        elif not materially_improved(
            abs(horizontal_holdout_before),
            abs(horizontal_final_holdout_after),
        ):
            horizontal_reason = (
                "final_held_out_horizontal_residual_not_materially_improved"
            )
        else:
            horizontal_reason = "final_horizontal_residual_not_materially_improved"
        horizontal_applied = False
        horizontal_reverted = True
        corrected = image
        original_forward_x = np.broadcast_to(
            np.arange(width, dtype=np.float32)[None, :], (height, width)
        ).copy()
        original_forward_y = np.broadcast_to(
            np.arange(height, dtype=np.float32)[:, None], (height, width)
        ).copy()
        if vertical_applied:
            vertical_applied = False
            vertical_reverted = True
            vertical_status = "reverted"
            vertical_reason = "dependent_horizontal_transform_was_reverted"
        (
            horizontal_after,
            horizontal_samples_after,
            horizontal_model_reason_after,
            horizontal_final_holdout_after,
            horizontal_final_holdout_samples_after,
            horizontal_final_holdout_reason_after,
            _,
        ) = horizontal_measurement(
            corrected,
            (
                horizontal_selection_before_vertical
                if horizontal_applied
                else None
            ),
        )
        (
            model_after,
            vertical_samples_after,
            vertical_model_reason_after,
            _,
            _,
        ) = vertical_measurement(corrected)
        edges_after = vertical_edges(model_after)
    elif horizontal_applied:
        horizontal_status = "applied"
    if vertical_applied and (
        model_after is None
        or not materially_improved(
            convergence_differential_before, final_convergence_residual
        )
        or (
            vertical_selection_before is not None
            and (
                vertical_validation_before is None
                or final_vertical_validation is None
                or final_vertical_validation_reason is not None
                or not materially_improved(
                    vertical_validation_before,
                    final_vertical_validation,
                )
            )
        )
    ):
        vertical_status = "reverted"
        if model_after is None:
            vertical_reason = (
                "final_tracked_vertical_evidence_is_insufficient:"
                f"{vertical_model_reason_after}"
            )
        elif (
            vertical_selection_before is not None
            and (
                final_vertical_validation is None
                or final_vertical_validation_reason is not None
            )
        ):
            vertical_reason = (
                "final_tracked_vertical_holdout_is_insufficient:"
                f"{final_vertical_validation_reason}"
            )
        elif (
            vertical_selection_before is not None
            and vertical_validation_before is not None
            and final_vertical_validation is not None
            and not materially_improved(
                vertical_validation_before,
                final_vertical_validation,
            )
        ):
            vertical_reason = (
                "final_tracked_vertical_holdout_not_materially_improved"
            )
        else:
            vertical_reason = "final_convergence_residual_not_materially_improved"
        vertical_applied = False
        vertical_reverted = True
        corrected = vertical_source
        original_forward_x = vertical_source_forward_x
        original_forward_y = vertical_source_forward_y
        (
            horizontal_after,
            horizontal_samples_after,
            horizontal_model_reason_after,
            horizontal_final_holdout_after,
            horizontal_final_holdout_samples_after,
            horizontal_final_holdout_reason_after,
            _,
        ) = horizontal_measurement(
            corrected,
            (
                horizontal_selection_before_vertical
                if horizontal_applied
                else None
            ),
        )
        (
            model_after,
            vertical_samples_after,
            vertical_model_reason_after,
            _,
            _,
        ) = vertical_measurement(corrected)
        edges_after = vertical_edges(model_after)
    elif vertical_applied:
        vertical_status = "applied"
    cumulative_clipped, cumulative_transformed_out_ink_ratio, cumulative_ink_loss_ratio = (
        clipping_metrics(
            image,
            corrected,
            forward_x=original_forward_x,
            forward_y=original_forward_y,
        )
    )
    if cumulative_clipped and (horizontal_applied or vertical_applied):
        corrected = image
        horizontal_applied = False
        vertical_applied = False
        horizontal_reverted = horizontal_reverted or horizontal_status == "applied"
        vertical_reverted = vertical_reverted or vertical_status == "applied"
        if horizontal_reverted:
            horizontal_reason = "final_cumulative_transform_would_clip_source_ink"
        if vertical_reverted:
            vertical_reason = "final_cumulative_transform_would_clip_source_ink"
        (
            horizontal_after,
            horizontal_samples_after,
            horizontal_model_reason_after,
            _,
            _,
            _,
            _,
        ) = horizontal_measurement(corrected)
        (
            model_after,
            vertical_samples_after,
            vertical_model_reason_after,
            _,
            _,
        ) = vertical_measurement(corrected)
        edges_after = vertical_edges(model_after)

    final_convergence_residual = abs(edges_after[1] - edges_after[0])
    final_common_vertical_tilt = abs((edges_after[0] + edges_after[1]) / 2)

    if horizontal_model_reason_after == "conflicting_horizontal_modes":
        horizontal_status = "review_required"
        horizontal_reason = horizontal_model_reason_after
    elif horizontal_samples_after < 10 or horizontal_model_reason_after is not None:
        if not horizontal_reverted:
            horizontal_status = "low_confidence"
            horizontal_reason = (
                horizontal_model_reason_after
                or "final_horizontal_evidence_is_insufficient"
            )
    elif abs(horizontal_after) > MAX_FINAL_HORIZONTAL_RESIDUAL_DEGREES:
        horizontal_status = "review_required"
        horizontal_reason = "final_horizontal_residual_requires_review"
    elif horizontal_applied:
        horizontal_status = "applied"
    elif horizontal_reverted:
        horizontal_status = "reverted"
    else:
        horizontal_status = "not_needed"
        horizontal_reason = "final_rotation_within_residual_limit"

    if (
        vertical_reason == "conflicting_evidence"
        and not vertical_applied
        and not vertical_reverted
    ):
        vertical_status = "review_required"
    elif model_after is None:
        if not vertical_reverted:
            vertical_status = (
                "review_required"
                if vertical_model_reason_after == "conflicting_evidence"
                else "low_confidence"
            )
            vertical_reason = vertical_model_reason_after
    elif final_convergence_residual > MAX_FINAL_CONVERGENCE_RESIDUAL_DEGREES:
        vertical_status = "review_required"
        vertical_reason = "final_convergence_residual_requires_review"
    elif final_common_vertical_tilt >= APPLICATION_THRESHOLD_DEGREES:
        vertical_status = "review_required"
        vertical_reason = (
            "final_common_vertical_tilt_requires_independent_orientation_review"
        )
    elif vertical_applied:
        vertical_status = "applied"
    elif vertical_reverted:
        vertical_status = "reverted"
    else:
        vertical_status = "not_needed"
        vertical_reason = "final_convergence_within_residual_limit"
    if (
        horizontal_status == "review_required"
        or vertical_status == "review_required"
    ):
        status = "review_required"
    elif horizontal_applied and vertical_applied:
        status = "applied"
    elif horizontal_applied or vertical_applied:
        status = "partially_applied"
    elif horizontal_reverted or vertical_reverted:
        status = "reverted"
    elif horizontal_status == "low_confidence" or vertical_status == "low_confidence":
        status = "low_confidence"
    else:
        status = "unchanged"
    return corrected, {
        "status": status,
        "review_required": status == "review_required",
        "horizontal_angle": horizontal_before if horizontal_applied else 0.0,
        "horizontal_samples": horizontal_samples_before,
        "horizontal_status": horizontal_status,
        "horizontal_reason": horizontal_reason,
        "vertical_samples": vertical_samples_before,
        "vertical_status": vertical_status,
        "vertical_reason": vertical_reason,
        "vertical_left_angle": edges_before[0] if vertical_applied else 0.0,
        "vertical_right_angle": edges_before[1] if vertical_applied else 0.0,
        "horizontal_before_angle": horizontal_before,
        "horizontal_after_angle": horizontal_after,
        "horizontal_samples_after": horizontal_samples_after,
        "horizontal_consensus_reason_before": horizontal_model_reason_before,
        "horizontal_holdout_before_angle": horizontal_holdout_before,
        "horizontal_holdout_samples_before": horizontal_holdout_samples_before,
        "horizontal_holdout_reason_before": horizontal_holdout_reason_before,
        "horizontal_holdout_after_angle": horizontal_holdout_after,
        "horizontal_holdout_samples_after": horizontal_holdout_samples_after,
        "horizontal_holdout_reason_after": horizontal_holdout_reason_after,
        "horizontal_applied": horizontal_applied,
        "horizontal_reverted": horizontal_reverted,
        "horizontal_candidate_clipped": horizontal_clipped,
        "horizontal_transformed_out_ink_ratio": horizontal_transformed_out_ink_ratio,
        "horizontal_candidate_ink_loss_ratio": horizontal_candidate_ink_loss_ratio,
        "horizontal_candidate_rejection_reason": (
            horizontal_candidate_rejection_reason
        ),
        "horizontal_validation_vertical_before_left_angle": (
            horizontal_check_vertical_edges_before[0]
        ),
        "horizontal_validation_vertical_before_right_angle": (
            horizontal_check_vertical_edges_before[1]
        ),
        "horizontal_validation_vertical_samples_before": (
            horizontal_check_vertical_samples_before
        ),
        "horizontal_validation_vertical_model_reason_before": (
            horizontal_check_vertical_reason_before
        ),
        "horizontal_validation_vertical_after_left_angle": (
            horizontal_check_vertical_edges_after[0]
        ),
        "horizontal_validation_vertical_after_right_angle": (
            horizontal_check_vertical_edges_after[1]
        ),
        "horizontal_validation_vertical_samples_after": (
            horizontal_check_vertical_samples_after
        ),
        "horizontal_validation_vertical_model_reason_after": (
            horizontal_check_vertical_reason_after
        ),
        "horizontal_validation_convergence_differential_before": (
            horizontal_validation_convergence_before
        ),
        "horizontal_validation_convergence_differential_after": (
            horizontal_validation_convergence_after
        ),
        "vertical_before_left_angle": edges_before[0],
        "vertical_before_right_angle": edges_before[1],
        "vertical_before_convergence_differential": (
            convergence_differential_before
        ),
        "vertical_before_common_tilt": common_vertical_tilt_before,
        "vertical_after_left_angle": edges_after[0],
        "vertical_after_right_angle": edges_after[1],
        "vertical_after_convergence_differential": final_convergence_residual,
        "vertical_after_common_tilt": final_common_vertical_tilt,
        "vertical_samples_after": vertical_samples_after,
        "vertical_after_model_reason": vertical_model_reason_after,
        "vertical_validation_before_convergence_differential": (
            vertical_validation_before
        ),
        "vertical_validation_samples_before": vertical_validation_samples_before,
        "vertical_validation_reason_before": vertical_validation_reason_before,
        "vertical_validation_after_convergence_differential": (
            vertical_validation_after
        ),
        "vertical_validation_samples_after": vertical_validation_samples_after,
        "vertical_validation_reason_after": vertical_validation_reason_after,
        "vertical_applied": vertical_applied,
        "vertical_reverted": vertical_reverted,
        "vertical_candidate_clipped": vertical_clipped,
        "vertical_transformed_out_ink_ratio": vertical_transformed_out_ink_ratio,
        "vertical_candidate_ink_loss_ratio": vertical_candidate_ink_loss_ratio,
        "vertical_candidate_rejection_reason": vertical_candidate_rejection_reason,
        "cumulative_clipping_reverted": cumulative_clipped
        and (horizontal_reverted or vertical_reverted),
        "cumulative_transformed_out_ink_ratio": (
            cumulative_transformed_out_ink_ratio
        ),
        "cumulative_ink_loss_ratio": cumulative_ink_loss_ratio,
        "original_to_final_clipping_reverted": cumulative_clipped
        and (horizontal_reverted or vertical_reverted),
        "original_to_final_transformed_out_ink_ratio": (
            cumulative_transformed_out_ink_ratio
        ),
        "original_to_final_ink_loss_ratio": cumulative_ink_loss_ratio,
        "unchanged": not horizontal_applied and not vertical_applied,
    }


def page_number(path: Path) -> int | None:
    digits = "".join(
        character
        for character in path.stem.rsplit("_", 1)[-1]
        if character.isdigit()
    )
    return int(digits) if digits else None


def natural_sort_key(path: Path) -> tuple[tuple[int, object], ...]:
    return tuple(
        (0, int(part)) if part.isdigit() else (1, part.casefold())
        for part in re.split(r"(\d+)", path.name)
    )


def path_key(path: Path) -> str:
    return str(path.resolve(strict=False)).casefold()


def is_reparse_point(path: Path) -> bool:
    try:
        attributes = path.lstat().st_file_attributes
    except AttributeError:
        return path.is_symlink()
    return bool(attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def reject_reparse_components(path: Path, description: str) -> None:
    absolute = Path(os.path.abspath(path))
    components = [Path(absolute.anchor)]
    for part in absolute.parts[1:]:
        components.append(components[-1] / part)
    for component in components:
        if (component.exists() or component.is_symlink()) and is_reparse_point(component):
            raise ValueError(
                f"{description} must not traverse a symlink or reparse point: "
                f"{component}"
            )


def reject_ntfs_ads_path(path: Path, description: str) -> None:
    drive, tail = ntpath.splitdrive(str(path))
    if ":" in drive.removesuffix(":") or ":" in tail:
        raise ValueError(
            f"{description} must not contain an NTFS alternate data stream path"
        )


def same_existing_file(first: Path, second: Path) -> bool:
    try:
        return first.samefile(second)
    except (FileNotFoundError, OSError):
        return False


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Correct small-angle global rotation and linear vertical convergence; "
            "this is not curved dewarping or page-orientation detection."
        )
    )
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--pages", nargs="+", type=int)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    if args.pages is not None:
        if any(page <= 0 for page in args.pages):
            parser.error("--pages values must be positive")
        if len(set(args.pages)) != len(args.pages):
            parser.error("--pages values must be unique")
    if not args.input.is_dir():
        parser.error(f"input is not a directory: {args.input}")
    try:
        reject_ntfs_ads_path(args.output, "output")
        output = Path(os.path.abspath(args.output))
        reject_ntfs_ads_path(output, "resolved output")
    except ValueError as error:
        parser.error(str(error))
    if args.input.resolve() == output:
        parser.error("input and output must be separate directories")
    try:
        reject_reparse_components(output.parent, "output parent")
    except (OSError, RuntimeError, ValueError) as error:
        parser.error(str(error))
    if not output.parent.is_dir():
        parser.error(f"output parent is not a directory: {output.parent}")
    if output.exists() or output.is_symlink():
        parser.error(f"output directory must not exist: {output}")
    files = sorted(
        (path for path in args.input.iterdir() if path.suffix.lower() in EXTENSIONS),
        key=natural_sort_key,
    )
    if args.pages is not None:
        matches: dict[int, list[Path]] = {page: [] for page in args.pages}
        for path in files:
            number = page_number(path)
            if number in matches:
                matches[number].append(path)
        missing = [page for page, paths in matches.items() if not paths]
        ambiguous = {
            page: paths for page, paths in matches.items() if len(paths) > 1
        }
        if missing:
            parser.error(
                "--pages values have no matching input: "
                + ", ".join(map(str, missing))
            )
        if ambiguous:
            details = "; ".join(
                f"{page}: {', '.join(path.name for path in paths)}"
                for page, paths in ambiguous.items()
            )
            parser.error(f"--pages values are ambiguous: {details}")
        wanted = set(args.pages)
        files = [path for path in files if page_number(path) in wanted]
    if not files:
        parser.error("no matching input pages")

    stems: dict[str, Path] = {}
    for path in files:
        key = path.stem.casefold()
        if key in stems:
            parser.error(
                "multiple inputs map to the same output PNG: "
                f"{stems[key].name}, {path.name}"
            )
        stems[key] = path

    output_pages = [output / f"{path.stem}.png" for path in files]
    try:
        for output_page in output_pages:
            reject_ntfs_ads_path(output_page, "output page")
    except ValueError as error:
        parser.error(str(error))
    input_keys = {path_key(path) for path in files}
    output_keys = {path_key(path) for path in output_pages}
    if len(output_keys) != len(output_pages):
        parser.error("multiple inputs resolve to the same output PNG")
    if input_keys & output_keys:
        parser.error("an output page would overwrite an input page")

    try:
        reject_ntfs_ads_path(args.report, "--report")
        if args.report.suffix.casefold() != ".json":
            raise ValueError("--report must have a .json suffix")
        report_path = Path(os.path.abspath(args.report))
        reject_ntfs_ads_path(report_path, "resolved --report")
        if report_path.suffix.casefold() != ".json":
            raise ValueError("resolved --report path must have a .json suffix")
        try:
            report_relative = report_path.relative_to(output)
        except ValueError as error:
            raise ValueError(
                "--report must reside inside the new output directory"
            ) from error
        if report_relative.parts != ("report.json",):
            raise ValueError(
                "--report must be the ordinary standalone report.json inside output"
            )
        report_key = path_key(report_path)
        if report_key in input_keys:
            raise ValueError("--report would overwrite an input page")
        if report_key in output_keys:
            raise ValueError("--report would overwrite an output page")
        if any(same_existing_file(report_path, path) for path in files):
            raise ValueError("--report aliases an input page")
    except (OSError, RuntimeError, ValueError) as error:
        parser.error(str(error))

    staging = output.parent / (
        f".{output.name}.rectify-{os.getpid()}-{secrets.token_hex(6)}.staging"
    )
    try:
        reject_ntfs_ads_path(staging, "staging output")
    except ValueError as error:
        parser.error(str(error))
    try:
        staging.mkdir()
        report: list[dict[str, object]] = []
        source_hashes: dict[Path, str] = {}
        for index, path in enumerate(files, 1):
            try:
                image, source_hash = read_page(path)
            except ValueError as error:
                parser.error(str(error))
            corrected, metrics = rectify(image)
            staged_page = staging / f"{path.stem}.png"
            write_png(staged_page, corrected)
            source_hashes[path] = source_hash
            report.append(
                {
                    "file": path.name,
                    "source_sha256": source_hash,
                    "input_bit_depth": image.dtype.itemsize * 8,
                    "output_bit_depth": corrected.dtype.itemsize * 8,
                    **metrics,
                }
            )
            print(f"processed {index}/{len(files)}: {path.name}", file=sys.stderr)
        staged_report = staging / report_relative
        staged_report.parent.mkdir(parents=True, exist_ok=True)
        write_bytes(
            staged_report,
            json.dumps(report, indent=2).encode("utf-8"),
        )
        for path, expected_hash in source_hashes.items():
            if file_sha256(path) != expected_hash:
                raise ValueError(
                    f"input changed during processing; refusing publication: {path}"
                )
        os.rename(staging, output)
    finally:
        shutil.rmtree(staging, ignore_errors=True)
    print(json.dumps({"processed": len(files), "output": str(output)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
