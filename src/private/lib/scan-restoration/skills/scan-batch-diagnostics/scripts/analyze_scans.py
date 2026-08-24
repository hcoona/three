# /// script
# requires-python = ">=3.12,<3.13"
# dependencies = [
#   "imagecodecs==2026.6.26",
#   "numpy==2.2.6",
#   "opencv-python-headless==4.12.0.88",
#   "Pillow==12.3.0",
#   "tifffile==2026.7.31",
# ]
# ///

from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
import tempfile
import warnings
from collections import Counter
from pathlib import Path
from typing import Any, NamedTuple

import cv2
import numpy as np
import tifffile
from PIL import Image, ImageOps, UnidentifiedImageError


SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"}
PILLOW_FORMAT_ALLOWLIST = ("JPEG", "PNG", "TIFF", "WEBP")
FORMAT_ALIASES = {
    ".jpg": "jpeg",
    ".jpeg": "jpeg",
    ".png": "png",
    ".tif": "tiff",
    ".tiff": "tiff",
    ".webp": "webp",
}
NUMBER_RE = re.compile(r"(\d+)")
ABSOLUTE_QUALITY_THRESHOLDS = {
    "brightness": {
        "page_min_warning_below": 180.0,
        "batch_median_warning_below": 210.0,
        "batch_median_warning_above": 252.0,
    },
    "ink_fraction": {
        "batch_median_warning_below": 0.001,
        "batch_median_warning_above": 0.40,
        "page_max_warning_above": 0.65,
    },
    "border_contamination": {
        "batch_median_warning_above": 0.12,
        "page_max_warning_above": 0.40,
    },
}
MAX_ENCODED_FILE_BYTES = 256 * 1024 * 1024
MAX_AGGREGATE_ENCODED_BYTES = 8 * 1024 * 1024 * 1024
MAX_DIRECTORY_ENTRIES = 10_000
MAX_CANDIDATE_FILES = 1_000
MAX_DECODE_WORKING_BYTES = 384 * 1024 * 1024
ANALYSIS_MAX_DIMENSION = 1400
FLOAT_ANALYSIS_BYTES_PER_PIXEL = 32
MAX_HOUGH_LINE_CANDIDATES = 2048
MAX_CLUSTER_FRAGMENTS = 256


def natural_key(path: Path) -> tuple[tuple[int, object], ...]:
    """Return a deterministic, case-insensitive key with numeric runs as integers."""
    parts: list[tuple[int, object]] = []
    for part in NUMBER_RE.split(path.name):
        if part.isdigit():
            parts.append((0, int(part)))
        else:
            parts.append((1, part.casefold()))
    parts.append((1, path.name))
    return tuple(parts)


def filename_format(path: Path) -> str:
    return FORMAT_ALIASES[path.suffix.lower()]


def inventory_directory(input_dir: Path) -> tuple[list[Path], list[Path]]:
    """Stream and bound a non-recursive directory inventory before analysis."""
    candidates: list[Path] = []
    unsupported: list[Path] = []
    aggregate_encoded_bytes = 0
    entry_count = 0

    with os.scandir(input_dir) as entries:
        for entry in entries:
            entry_count += 1
            if entry_count > MAX_DIRECTORY_ENTRIES:
                raise ValueError(
                    f"directory entry count exceeds the {MAX_DIRECTORY_ENTRIES}-entry limit"
                )
            if not entry.is_file():
                continue
            path = Path(entry.path)
            if path.suffix.lower() not in SUPPORTED_EXTENSIONS:
                unsupported.append(path)
                continue
            if len(candidates) >= MAX_CANDIDATE_FILES:
                raise ValueError(
                    f"candidate image count exceeds the {MAX_CANDIDATE_FILES}-file limit"
                )
            size = entry.stat().st_size
            aggregate_encoded_bytes += max(0, size)
            if aggregate_encoded_bytes > MAX_AGGREGATE_ENCODED_BYTES:
                raise ValueError(
                    "aggregate encoded image size exceeds the "
                    f"{MAX_AGGREGATE_ENCODED_BYTES}-byte batch limit"
                )
            candidates.append(path)

    candidates.sort(key=natural_key)
    unsupported.sort(key=natural_key)
    return candidates, unsupported


def mode_bit_depth(mode: str) -> int | None:
    if mode.startswith("I;16"):
        return 16
    if mode in {"1", "L", "LA", "P", "RGB", "RGBA", "CMYK", "YCbCr"}:
        return 1 if mode == "1" else 8
    if mode in {"I", "F"}:
        return 32
    return None


def _metadata_values(value: object) -> tuple[int, ...]:
    if isinstance(value, (tuple, list)):
        return tuple(int(item) for item in value)
    return (int(value),)


def source_sample_encoding(
    image: Image.Image, data: bytes
) -> tuple[int | None, str | None, bool, int | None, int | None]:
    """Determine encoded sample depth/type before Pillow can down-convert pixels."""
    depth = mode_bit_depth(image.mode)
    sample_format = (
        "float"
        if image.mode == "F"
        else "signed_integer"
        if image.mode == "I" or "S" in image.mode
        else "unsigned_integer"
        if depth is not None
        else None
    )
    grayscale = len(image.getbands()) == 1
    photometric: int | None = None
    samples_per_pixel: int | None = None

    if image.format == "PNG":
        if (
            len(data) < 29
            or data[:8] != b"\x89PNG\r\n\x1a\n"
            or data[12:16] != b"IHDR"
        ):
            raise ValueError("PNG is missing a valid IHDR depth declaration")
        depth = int(data[24])
        color_type = int(data[25])
        if depth not in {1, 2, 4, 8, 16} or color_type not in {0, 2, 3, 4, 6}:
            raise ValueError("PNG has an unsupported IHDR sample encoding")
        sample_format = "unsigned_integer"
        grayscale = color_type == 0

    elif image.format == "TIFF":
        bits_tag = image.tag_v2.get(258)
        if bits_tag is not None:
            bits = _metadata_values(bits_tag)
            if not bits or len(set(bits)) != 1:
                raise ValueError("TIFF has unsupported mixed per-sample bit depths")
            depth = bits[0]

        format_tag = image.tag_v2.get(339)
        if format_tag is not None:
            formats = _metadata_values(format_tag)
            if not formats or len(set(formats)) != 1:
                raise ValueError("TIFF has unsupported mixed sample formats")
            sample_format = {
                1: "unsigned_integer",
                2: "signed_integer",
                3: "float",
            }.get(formats[0], "unsupported")

        samples_per_pixel = int(image.tag_v2.get(277, len(image.getbands())))
        photometric_tag = image.tag_v2.get(262)
        if photometric_tag is not None:
            photometric_values = _metadata_values(photometric_tag)
            if len(photometric_values) != 1:
                raise ValueError(
                    "TIFF has unsupported multiple PhotometricInterpretation values"
                )
            photometric = photometric_values[0]
        grayscale = samples_per_pixel == 1 and (
            photometric is None or photometric in {0, 1}
        )

    return depth, sample_format, grayscale, photometric, samples_per_pixel


def validate_sample_encoding(
    mode: str,
    depth: int | None,
    sample_format: str | None,
    grayscale: bool,
    encoded_format: str | None = None,
    photometric: int | None = None,
    samples_per_pixel: int | None = None,
) -> None:
    if sample_format == "signed_integer":
        raise ValueError(f"unsupported signed sample mode {mode} ({depth}-bit)")
    if sample_format == "float":
        raise ValueError(f"unsupported floating-point sample mode {mode} ({depth}-bit)")
    if sample_format not in {None, "unsigned_integer"}:
        raise ValueError(f"unsupported sample format for Pillow mode {mode}")
    if encoded_format == "TIFF":
        matrix_key = (photometric, samples_per_pixel, depth, sample_format)
        grayscale_matrix = {
            (photometric_value, 1, depth_value, "unsigned_integer")
            for photometric_value in (0, 1)
            for depth_value in (1, 8, 16)
        }
        rgb_matrix = {(2, 3, 8, "unsigned_integer")}
        if matrix_key not in grayscale_matrix | rgb_matrix:
            raise ValueError(
                "unsupported TIFF sample matrix "
                f"(PhotometricInterpretation={photometric}, "
                f"SamplesPerPixel={samples_per_pixel}, BitsPerSample={depth}, "
                f"SampleFormat={sample_format}); supported matrices are unsigned "
                "WhiteIsZero/BlackIsZero grayscale with 1 sample at 1, 8, or 16 "
                "bits, and unsigned RGB with 3 samples at 8 bits"
            )
        expected_mode = {
            (0, 1, 1, "unsigned_integer"): "1",
            (1, 1, 1, "unsigned_integer"): "1",
            (0, 1, 8, "unsigned_integer"): "L",
            (1, 1, 8, "unsigned_integer"): "L",
            (0, 1, 16, "unsigned_integer"): "I;16",
            (1, 1, 16, "unsigned_integer"): "I;16",
            (2, 3, 8, "unsigned_integer"): "RGB",
        }[matrix_key]
        mode_matches = (
            mode.startswith(expected_mode)
            if expected_mode == "I;16"
            else mode == expected_mode
        )
        if not mode_matches:
            raise ValueError(
                f"unsafe TIFF decoder mode {mode} for supported sample matrix; "
                f"expected {expected_mode}"
            )
    if depth is not None and depth > 16:
        raise ValueError(f"unsupported {depth}-bit sample mode {mode}")
    if depth == 16 and (not grayscale or not mode.startswith("I;16")):
        raise ValueError(f"unsupported 16-bit color/sample mode {mode}")


def tiff_photometric_name(
    encoded_format: str | None, photometric: int | None
) -> str | None:
    if encoded_format != "TIFF" or photometric is None:
        return None
    return {
        0: "WhiteIsZero",
        1: "BlackIsZero",
        2: "RGB",
        3: "Palette",
        5: "CMYK",
        6: "YCbCr",
        8: "CIELab",
    }.get(photometric, f"unsupported ({photometric})")


def orient_array(values: np.ndarray, orientation: int) -> np.ndarray:
    operations = {
        1: lambda array: array,
        2: np.fliplr,
        3: lambda array: np.rot90(array, 2),
        4: np.flipud,
        5: np.transpose,
        6: lambda array: np.rot90(array, 3),
        7: lambda array: np.flipud(np.fliplr(array)).T,
        8: lambda array: np.rot90(array, 1),
    }
    if orientation not in operations:
        raise ValueError(f"unsupported EXIF orientation {orientation}")
    return np.ascontiguousarray(operations[orientation](values))


def validate_exif_orientation(value: object) -> int:
    try:
        numeric_value = float(value)
    except (TypeError, ValueError, OverflowError) as error:
        raise ValueError(f"invalid EXIF orientation value {value!r}") from error
    if not np.isfinite(numeric_value) or not numeric_value.is_integer():
        raise ValueError(
            f"invalid EXIF orientation value {value!r}; "
            "expected an integer from 1 through 8"
        )
    orientation = int(numeric_value)
    if orientation not in range(1, 9):
        raise ValueError(
            f"invalid EXIF orientation {orientation}; expected an integer from 1 through 8"
        )
    return orientation


def estimate_decode_working_bytes(
    width: int,
    height: int,
    mode: str,
    *,
    has_transparency: bool,
    orientation: int,
) -> int:
    if width <= 0 or height <= 0:
        raise ValueError("image header reports non-positive dimensions")
    pixels = width * height
    if Image.MAX_IMAGE_PIXELS is not None and pixels > Image.MAX_IMAGE_PIXELS:
        raise Image.DecompressionBombError(
            f"image raster size {pixels} exceeds the safe pixel limit"
        )
    source_bytes = max(1, Image.getmodebands(mode)) * pixels
    # Pillow stores these multiband modes in four-byte Imaging pixels.
    if mode in {"LA", "RGB", "RGBA", "RGBX", "CMYK", "YCbCr", "LAB", "HSV"}:
        source_bytes = 4 * pixels
    if mode.startswith("I;16"):
        source_bytes = 2 * pixels
    elif mode in {"I", "F"}:
        source_bytes = 4 * pixels
    orientation_copy = source_bytes if orientation != 1 else 0
    if has_transparency:
        # Budget native-size RGBA conversion, white canvas, composite, and L
        # result before any analysis resize. The uint16 tRNS path uses less.
        conversion_bytes = 13 * pixels
    elif mode.startswith("I;16"):
        conversion_bytes = 5 * pixels
    elif mode == "1":
        conversion_bytes = 2 * pixels
    elif mode == "L":
        conversion_bytes = pixels
    else:
        conversion_bytes = 2 * pixels
    return source_bytes + orientation_copy + conversion_bytes


def enforce_decode_budget(
    width: int,
    height: int,
    mode: str,
    *,
    has_transparency: bool,
    orientation: int,
) -> None:
    estimate = estimate_decode_working_bytes(
        width,
        height,
        mode,
        has_transparency=has_transparency,
        orientation=orientation,
    )
    if estimate > MAX_DECODE_WORKING_BYTES:
        raise ValueError(
            f"estimated decode working memory {estimate} exceeds the "
            f"{MAX_DECODE_WORKING_BYTES}-byte limit"
        )


def scaled_raster_pixels(
    width: int, height: int, max_dimension: int | None
) -> int:
    if max_dimension is None or max(width, height) <= max_dimension:
        return width * height
    scale = max_dimension / max(width, height)
    return max(1, int(np.ceil(width * scale))) * max(
        1, int(np.ceil(height * scale))
    )


def estimate_tiff_uint16_working_bytes(
    width: int,
    height: int,
    *,
    orientation: int,
    photometric: int,
    max_dimension: int | None,
) -> int:
    """Conservatively estimate the largest simultaneous 16-bit TIFF allocation."""
    pixels = width * height
    analysis_pixels = scaled_raster_pixels(width, height, max_dimension)
    peaks = [
        4 * pixels,  # decoder array plus endian/native owned copy
        4 * pixels if orientation != 1 else 2 * pixels,
        4 * pixels if photometric == 0 else 2 * pixels,
        7 * pixels,  # uint16 source, uint32 conversion, and uint8 result
        2 * pixels + 2 * analysis_pixels,  # resize source/destination/workspace
    ]
    if max_dimension is not None:
        peaks.append(FLOAT_ANALYSIS_BYTES_PER_PIXEL * analysis_pixels)
    return max(peaks)


def enforce_tiff_uint16_budget(
    width: int,
    height: int,
    *,
    orientation: int,
    photometric: int,
    max_dimension: int | None,
) -> None:
    estimate = estimate_tiff_uint16_working_bytes(
        width,
        height,
        orientation=orientation,
        photometric=photometric,
        max_dimension=max_dimension,
    )
    if estimate > MAX_DECODE_WORKING_BYTES:
        raise ValueError(
            f"estimated decode working memory {estimate} exceeds the "
            f"{MAX_DECODE_WORKING_BYTES}-byte limit"
        )


def decode_tiff_uint16(
    data: bytes, max_dimension: int | None = None
) -> tuple[np.ndarray, int, int, int, int, str]:
    """Decode validated 16-bit grayscale TIFF samples without losing byte order."""
    header_byte_order = {b"II": "<", b"MM": ">"}.get(data[:2])
    if header_byte_order is None:
        raise ValueError("TIFF has an invalid byte-order marker")
    with warnings.catch_warnings():
        warnings.simplefilter("error")
        with tifffile.TiffFile(io.BytesIO(data)) as tiff:
            if tiff.byteorder != header_byte_order:
                raise ValueError("TIFF decoder byte order disagrees with the header")
            if len(tiff.pages) != 1:
                raise ValueError("TIFF fallback requires exactly one image frame")
            page = tiff.pages[0]
            height, width = page.shape if len(page.shape) == 2 else (0, 0)
            photometric = int(page.photometric)
            if (
                Image.MAX_IMAGE_PIXELS is not None
                and width * height > Image.MAX_IMAGE_PIXELS
            ):
                raise Image.DecompressionBombError(
                    f"TIFF raster size {width * height} exceeds the safe limit"
                )
            if (
                width <= 0
                or height <= 0
                or page.dtype.kind != "u"
                or page.dtype.itemsize != 2
                or int(page.bitspersample) != 16
                or int(page.samplesperpixel) != 1
                or int(page.sampleformat) != 1
                or photometric not in {0, 1}
                or bool(page.extrasamples)
            ):
                raise ValueError(
                    "TIFF fallback metadata disagrees with the validated "
                    "16-bit unsigned grayscale matrix"
                )
            orientation_tag = page.tags.get(274)
            orientation = validate_exif_orientation(
                orientation_tag.value if orientation_tag else 1
            )
            enforce_tiff_uint16_budget(
                width,
                height,
                orientation=orientation,
                photometric=photometric,
                max_dimension=max_dimension,
            )
            decoded = page.asarray()
            if decoded.shape != (height, width) or decoded.dtype.kind != "u":
                raise ValueError("TIFF fallback decoder returned an unexpected raster")
            values = np.asarray(decoded, dtype=np.uint16)
            if values is decoded:
                values = decoded.copy()
    if values.shape != (height, width) or values.dtype.kind != "u":
        raise ValueError("TIFF fallback decoder returned an unexpected raster")
    source_mode = "I;16B" if header_byte_order == ">" else "I;16L"
    return (
        values,
        width,
        height,
        photometric,
        orientation,
        source_mode,
    )


def read_tiff_uint16_gray(
    data: bytes, max_dimension: int | None = None
) -> tuple[np.ndarray, dict[str, object]]:
    values, source_width, source_height, photometric, orientation, source_mode = (
        decode_tiff_uint16(data, max_dimension)
    )
    values = orient_array(values, orientation)
    display_height, display_width = values.shape
    if photometric == 0:
        values = np.subtract(np.uint16(65535), values, dtype=np.uint16)
    scaled = values.astype(np.uint32)
    scaled *= np.uint32(255)
    scaled += np.uint32(32767)
    scaled //= np.uint32(65535)
    gray = scaled.astype(np.uint8)
    del values, scaled
    if max_dimension is not None and max(gray.shape) > max_dimension:
        scale = max_dimension / max(gray.shape)
        gray = cv2.resize(
            gray, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA
        )
    analysis_height, analysis_width = gray.shape
    return gray, {
        "encoded_format": "TIFF",
        "source_mode": source_mode,
        "source_bit_depth": 16,
        "source_samples_per_pixel": 1,
        "source_sample_format": "unsigned_integer",
        "source_photometric_interpretation": tiff_photometric_name(
            "TIFF", photometric
        ),
        "transparency_composited_onto": None,
        "exif_orientation_applied": orientation != 1,
        "source_width": source_width,
        "source_height": source_height,
        "display_width": display_width,
        "display_height": display_height,
        "display_aspect_ratio": round(display_width / display_height, 6),
        "analysis_width": analysis_width,
        "analysis_height": analysis_height,
        "analysis_aspect_ratio": round(analysis_width / analysis_height, 6),
    }


def composite_transparency_onto_white(image: Image.Image) -> Image.Image | np.ndarray:
    """Flatten supported alpha and PNG tRNS representations onto white."""
    if "A" in image.getbands():
        rgba = image.convert("RGBA")
        white = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        return Image.alpha_composite(white, rgba).convert("L")

    transparency = image.info.get("transparency")
    if transparency is None:
        raise ValueError("image does not contain transparency")

    if image.mode.startswith("I;16"):
        if not isinstance(transparency, int) or not 0 <= transparency <= 65535:
            raise ValueError(
                f"unsupported transparency metadata for Pillow mode {image.mode}"
            )
        values = np.array(image, dtype=np.uint16, copy=True)
        values[values == transparency] = np.uint16(65535)
        scaled = values.astype(np.uint32)
        scaled *= np.uint32(255)
        scaled += np.uint32(32767)
        scaled //= np.uint32(65535)
        return scaled.astype(np.uint8)

    supported_metadata_modes = {"1", "L", "P", "RGB"}
    if image.mode not in supported_metadata_modes:
        raise ValueError(
            f"unsupported transparency metadata for Pillow mode {image.mode}"
        )
    rgba = image.convert("RGBA")
    white = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
    return Image.alpha_composite(white, rgba).convert("L")


def read_encoded_file(path: Path) -> bytes:
    size = path.stat().st_size
    if size <= 0:
        raise ValueError("image file is empty")
    if size > MAX_ENCODED_FILE_BYTES:
        raise ValueError(
            f"encoded image size {size} exceeds the {MAX_ENCODED_FILE_BYTES}-byte limit"
        )
    with path.open("rb") as stream:
        data = stream.read(MAX_ENCODED_FILE_BYTES + 1)
    if len(data) > MAX_ENCODED_FILE_BYTES:
        raise ValueError(
            f"encoded image exceeds the {MAX_ENCODED_FILE_BYTES}-byte limit"
        )
    if not data:
        raise ValueError("image file is empty")
    return data


def read_gray(
    path: Path,
    data: bytes | None = None,
    *,
    max_dimension: int | None = None,
) -> tuple[np.ndarray, dict[str, object]]:
    """Decode safely, orient, and flatten supported transparency onto white."""
    if data is None:
        data = read_encoded_file(path)
    frame_count, frame_error = image_frame_count(path, data)
    if frame_count != 1:
        detail = frame_error or f"decoder reported {frame_count} frames"
        raise ValueError(f"single-frame validation failed: {detail}")
    with warnings.catch_warnings():
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        try:
            opened_image = Image.open(
                io.BytesIO(data), formats=PILLOW_FORMAT_ALLOWLIST
            )
        except UnidentifiedImageError:
            if path.suffix.lower() in {".tif", ".tiff"} and data[:2] in {
                b"II",
                b"MM",
            }:
                return read_tiff_uint16_gray(data, max_dimension)
            raise
        with opened_image as opened:
            encoded_format = opened.format
            source_mode = opened.mode
            (
                source_depth,
                source_sample_format,
                source_is_grayscale,
                source_photometric,
                source_samples_per_pixel,
            ) = source_sample_encoding(opened, data)
            validate_sample_encoding(
                source_mode,
                source_depth,
                source_sample_format,
                source_is_grayscale,
                encoded_format,
                source_photometric,
                source_samples_per_pixel,
            )
            orientation = validate_exif_orientation(opened.getexif().get(274, 1))
            orientation_applied = orientation != 1
            has_transparency = (
                "A" in opened.getbands() or "transparency" in opened.info
            )
            original_width, original_height = opened.size
            displayed_width, displayed_height = (
                (original_height, original_width)
                if orientation in {5, 6, 7, 8}
                else (original_width, original_height)
            )
            use_tiff_uint16_decoder = (
                encoded_format == "TIFF"
                and source_depth == 16
                and source_is_grayscale
                and source_samples_per_pixel == 1
                and source_photometric in {0, 1}
            )
            if not use_tiff_uint16_decoder:
                enforce_decode_budget(
                    opened.width,
                    opened.height,
                    opened.mode,
                    has_transparency=has_transparency,
                    orientation=orientation,
                )
            if use_tiff_uint16_decoder:
                gray, metadata = read_tiff_uint16_gray(data, max_dimension)
                if (
                    metadata["source_photometric_interpretation"]
                    != tiff_photometric_name(encoded_format, source_photometric)
                ):
                    raise ValueError(
                        "TIFF fallback metadata disagrees with Pillow metadata"
                    )
                return gray, metadata

            if (
                max_dimension is not None
                and max(opened.size) > max_dimension
                and encoded_format == "JPEG"
            ):
                opened.draft("L" if opened.mode in {"1", "L"} else "RGB", (
                    max_dimension,
                    max_dimension,
                ))
            opened.load()
            oriented = (
                opened if orientation == 1 else ImageOps.exif_transpose(opened)
            )
            has_transparency = "A" in oriented.getbands() or "transparency" in oriented.info
            white_is_zero = encoded_format == "TIFF" and source_photometric == 0
            if white_is_zero and has_transparency:
                raise ValueError(
                    "unsupported TIFF WhiteIsZero representation with transparency"
                )
            if has_transparency:
                flattened = composite_transparency_onto_white(oriented)
                if isinstance(flattened, np.ndarray):
                    analysis_image = Image.fromarray(flattened, mode="L")
                else:
                    analysis_image = flattened
                if (
                    max_dimension is not None
                    and max(analysis_image.size) > max_dimension
                ):
                    analysis_image.thumbnail(
                        (max_dimension, max_dimension), Image.Resampling.LANCZOS
                    )
                gray = np.asarray(analysis_image, dtype=np.uint8).copy()
            elif oriented.mode.startswith("I;16"):
                if max_dimension is not None and max(oriented.size) > max_dimension:
                    oriented.thumbnail(
                        (max_dimension, max_dimension), Image.Resampling.LANCZOS
                    )
                values = np.asarray(oriented, dtype=np.float64)
                if white_is_zero:
                    # Pillow exposes 16-bit WhiteIsZero TIFF sample values unchanged,
                    # unlike its display-normalized 1- and 8-bit decodes.
                    values = 65535.0 - values
                gray = np.clip(np.rint(values * (255.0 / 65535.0)), 0, 255).astype(
                    np.uint8
                )
                return gray, {
                    "encoded_format": encoded_format,
                    "source_mode": source_mode,
                    "source_bit_depth": source_depth,
                    "source_samples_per_pixel": source_samples_per_pixel,
                    "source_sample_format": source_sample_format,
                    "source_photometric_interpretation": tiff_photometric_name(
                        encoded_format, source_photometric
                    ),
                    "transparency_composited_onto": None,
                    "exif_orientation_applied": orientation_applied,
                    "source_width": original_width,
                    "source_height": original_height,
                    "display_width": displayed_width,
                    "display_height": displayed_height,
                    "display_aspect_ratio": round(
                        displayed_width / displayed_height, 6
                    ),
                    "analysis_width": int(gray.shape[1]),
                    "analysis_height": int(gray.shape[0]),
                    "analysis_aspect_ratio": round(
                        int(gray.shape[1]) / int(gray.shape[0]), 6
                    ),
                }
            elif oriented.mode == "1":
                analysis_image = oriented.convert("L")
                if max_dimension is not None and max(oriented.size) > max_dimension:
                    analysis_image.thumbnail(
                        (max_dimension, max_dimension), Image.Resampling.LANCZOS
                    )
                gray = np.asarray(analysis_image, dtype=np.uint8).copy()
            else:
                analysis_image = (
                    oriented.convert("L") if oriented.mode == "P" else oriented
                )
                if (
                    max_dimension is not None
                    and max(analysis_image.size) > max_dimension
                ):
                    analysis_image.thumbnail(
                        (max_dimension, max_dimension), Image.Resampling.LANCZOS
                    )
                grayscale = analysis_image.convert("L")
                gray = np.asarray(grayscale, dtype=np.uint8).copy()
    if gray.ndim != 2 or gray.size == 0:
        raise ValueError("decoder returned no image")
    return gray, {
        "encoded_format": encoded_format,
        "source_mode": source_mode,
        "source_bit_depth": source_depth,
        "source_samples_per_pixel": source_samples_per_pixel,
        "source_sample_format": source_sample_format,
        "source_photometric_interpretation": tiff_photometric_name(
            encoded_format, source_photometric
        ),
        "transparency_composited_onto": "white" if has_transparency else None,
        "exif_orientation_applied": orientation_applied,
        "source_width": original_width,
        "source_height": original_height,
        "display_width": displayed_width,
        "display_height": displayed_height,
        "display_aspect_ratio": round(displayed_width / displayed_height, 6),
        "analysis_width": int(gray.shape[1]),
        "analysis_height": int(gray.shape[0]),
        "analysis_aspect_ratio": round(
            int(gray.shape[1]) / int(gray.shape[0]), 6
        ),
    }


def image_frame_count(
    path: Path, data: bytes | None = None
) -> tuple[int | None, str | None]:
    """Inspect image headers and frame metadata without decoding every frame."""
    try:
        if data is None:
            data = read_encoded_file(path)
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            with Image.open(
                io.BytesIO(data), formats=PILLOW_FORMAT_ALLOWLIST
            ) as image:
                count = int(getattr(image, "n_frames", 1))
                if image.format not in PILLOW_FORMAT_ALLOWLIST:
                    return None, "Pillow identified a format outside the allowlist"
    except UnidentifiedImageError as error:
        if path.suffix.lower() in {".tif", ".tiff"}:
            try:
                with tifffile.TiffFile(io.BytesIO(data)) as tiff:
                    return len(tiff.pages), None
            except Exception:
                pass
        return (
            None,
            f"Pillow header/frame inspection failed: "
            f"{type(error).__name__}: {error}",
        )
    except Exception as error:
        return (
            None,
            f"Pillow header/frame inspection failed: "
            f"{type(error).__name__}: {error}",
        )
    if count < 1:
        return None, "Pillow reported a non-positive frame count"
    return count, None


def weighted_median(values: np.ndarray, weights: np.ndarray) -> float:
    if values.size == 0 or weights.size != values.size:
        raise ValueError("weighted median requires matching non-empty arrays")
    valid = np.isfinite(values) & np.isfinite(weights) & (weights > 0)
    if not np.any(valid):
        raise ValueError("weighted median has no finite positive-weight evidence")
    values = values[valid]
    weights = weights[valid]
    order = np.argsort(values, kind="stable")
    ordered_values = values[order]
    ordered_weights = weights[order]
    cutoff = float(np.sum(ordered_weights)) / 2.0
    return float(ordered_values[np.searchsorted(np.cumsum(ordered_weights), cutoff)])


def content_mask(gray: np.ndarray) -> np.ndarray:
    if gray.ndim != 2 or gray.size == 0 or min(gray.shape) < 2:
        raise ValueError("image dimensions are too small for analysis")
    blurred = cv2.GaussianBlur(gray, (3, 3), 0)
    _, mask = cv2.threshold(
        blurred, 0, 255, cv2.THRESH_BINARY_INV | cv2.THRESH_OTSU
    )
    return mask


def paper_relative_metrics(
    gray: np.ndarray,
) -> tuple[np.ndarray, dict[str, float]]:
    """Measure ink as contrast below a robust local/global paper estimate."""
    if gray.ndim != 2 or gray.size == 0 or min(gray.shape) < 8:
        raise ValueError("image dimensions are too small for paper estimation")
    blurred = cv2.GaussianBlur(gray, (3, 3), 0)
    height, width = blurred.shape
    margin_y = max(1, round(height * 0.08))
    margin_x = max(1, round(width * 0.08))
    interior = blurred[margin_y:-margin_y, margin_x:-margin_x]
    if interior.size == 0:
        interior = blurred
    global_paper = float(np.percentile(interior, 90))

    kernel_size = max(9, min(81, round(min(gray.shape) * 0.05)))
    if kernel_size % 2 == 0:
        kernel_size += 1
    local_paper = cv2.morphologyEx(
        blurred,
        cv2.MORPH_CLOSE,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size)),
    ).astype(np.float32)
    local_paper = cv2.GaussianBlur(
        local_paper, (0, 0), sigmaX=max(2.0, kernel_size / 6.0)
    )
    expected_paper = np.maximum(local_paper, global_paper - 18.0)
    contrast = np.maximum(expected_paper - blurred.astype(np.float32), 0.0)

    background_cutoff = float(np.percentile(contrast, 70))
    background_residuals = contrast[contrast <= background_cutoff]
    residual_center = float(np.median(background_residuals))
    residual_mad = float(
        np.median(np.abs(background_residuals - residual_center))
    )
    threshold = float(
        np.clip(
            max(
                6.0,
                global_paper * 0.035,
                residual_center + 4.0 * 1.4826 * residual_mad,
            ),
            6.0,
            30.0,
        )
    )
    ink = contrast >= threshold
    border = max(4, round(min(gray.shape) * 0.012))
    edge_ink = np.concatenate(
        (
            ink[:border].ravel(),
            ink[-border:].ravel(),
            ink[:, :border].ravel(),
            ink[:, -border:].ravel(),
        )
    )
    return (ink.astype(np.uint8) * np.uint8(255)), {
        "global_paper_level": global_paper,
        "local_paper_median": float(np.median(local_paper)),
        "ink_contrast_threshold": threshold,
        "ink_fraction": float(np.mean(ink)),
        "border_contamination_fraction": float(np.mean(edge_ink)),
    }


def projection_score(mask: np.ndarray, rotation_degrees: float) -> float:
    if mask.ndim != 2 or mask.size == 0 or min(mask.shape) < 3:
        return 0.0
    height, width = mask.shape
    matrix = cv2.getRotationMatrix2D(
        ((width - 1) / 2.0, (height - 1) / 2.0), rotation_degrees, 1.0
    )
    rotated = cv2.warpAffine(
        mask,
        matrix,
        (width, height),
        flags=cv2.INTER_NEAREST,
        borderMode=cv2.BORDER_CONSTANT,
        borderValue=0,
    )
    margin = max(1, round(width * 0.03))
    interior = rotated[:, margin:-margin] if width > 2 * margin else rotated
    if interior.size == 0:
        return 0.0
    projection = np.count_nonzero(interior, axis=1).astype(float)
    score = float(np.var(projection) / max(float(np.mean(projection)), 1.0))
    return score if np.isfinite(score) else 0.0


class LineFragment(NamedTuple):
    intercept: float
    angle: float
    length: float
    endpoints: tuple[int, int, int, int]
    support_label: int = 0
    thickness: float = 1.0


def strongest_line_fragments(
    fragments: list[LineFragment],
) -> tuple[list[LineFragment], bool]:
    ordered = sorted(
        fragments,
        key=lambda item: (
            -item.length,
            item.intercept,
            item.angle,
            item.endpoints,
        ),
    )
    truncated = len(ordered) > MAX_CLUSTER_FRAGMENTS
    return ordered[:MAX_CLUSTER_FRAGMENTS], truncated


def characterize_fragment_support(
    fragment: LineFragment,
    support: np.ndarray,
    labels: np.ndarray,
) -> LineFragment:
    x1, y1, x2, y2 = fragment.endpoints
    dx, dy = float(x2 - x1), float(y2 - y1)
    length = max(float(np.hypot(dx, dy)), 1.0)
    normal_x, normal_y = -dy / length, dx / length
    max_scan = max(8, min(48, round(min(support.shape) * 0.10)))
    offsets = np.arange(-max_scan, max_scan + 1, dtype=float)
    thicknesses: list[float] = []
    support_labels: list[int] = []

    for fraction in np.linspace(0.1, 0.9, 9):
        center_x = x1 + dx * float(fraction)
        center_y = y1 + dy * float(fraction)
        xs = np.rint(center_x + normal_x * offsets).astype(int)
        ys = np.rint(center_y + normal_y * offsets).astype(int)
        valid = (
            (xs >= 0)
            & (xs < support.shape[1])
            & (ys >= 0)
            & (ys < support.shape[0])
        )
        occupied = np.zeros(offsets.size, dtype=bool)
        occupied[valid] = support[ys[valid], xs[valid]] != 0
        occupied_indexes = np.flatnonzero(occupied)
        if occupied_indexes.size == 0:
            continue
        nearest = int(occupied_indexes[np.argmin(np.abs(offsets[occupied_indexes]))])
        start = nearest
        end = nearest
        while start > 0 and occupied[start - 1]:
            start -= 1
        while end + 1 < occupied.size and occupied[end + 1]:
            end += 1
        thicknesses.append(float(end - start + 1))
        run_valid = valid[start : end + 1]
        run_labels = labels[ys[start : end + 1][run_valid], xs[start : end + 1][run_valid]]
        support_labels.extend(int(label) for label in run_labels if label)

    label = Counter(support_labels).most_common(1)[0][0] if support_labels else 0
    thickness = float(np.median(thicknesses)) if thicknesses else 1.0
    return LineFragment(
        fragment.intercept,
        fragment.angle,
        fragment.length,
        fragment.endpoints,
        label,
        thickness,
    )


def cluster_line_fragments(
    fragments: list[LineFragment],
    band_tolerance: float,
    angle_tolerance: float = 1.5,
    intercept_scale: float = 1.0,
    connected_support_one_vote: bool = False,
    support_angles: dict[int, float] | None = None,
) -> list[tuple[float, float, float, int]]:
    """Merge Hough fragments into one vote per supported physical stroke."""
    fragments, _ = strongest_line_fragments(fragments)
    clusters: list[list[LineFragment]] = []
    for fragment in sorted(fragments, key=lambda item: (item.intercept, item.angle)):
        best: list[LineFragment] | None = None
        best_distance = float("inf")
        for cluster in clusters:
            intercept = weighted_median(
                np.asarray([item.intercept for item in cluster]),
                np.asarray([item.length for item in cluster]),
            )
            angle = weighted_median(
                np.asarray([item.angle for item in cluster]),
                np.asarray([item.length for item in cluster]),
            )
            intercept_delta = abs(fragment.intercept - intercept)
            intercept_distance = intercept_delta / band_tolerance
            angle_distance = abs(fragment.angle - angle) / angle_tolerance
            distance = max(intercept_distance, angle_distance)
            labels = {item.support_label for item in cluster if item.support_label}
            same_support = (
                fragment.support_label != 0 and fragment.support_label in labels
            )
            different_known_support = (
                connected_support_one_vote
                and fragment.support_label != 0
                and bool(labels)
                and fragment.support_label not in labels
            )
            physical_thickness = max(
                fragment.thickness,
                max(item.thickness for item in cluster),
            )
            same_thick_stroke = (
                same_support
                and (
                    connected_support_one_vote
                    or (
                        abs(fragment.angle - angle) <= max(10.0, angle_tolerance)
                        and intercept_delta * intercept_scale
                        <= max(
                            band_tolerance * intercept_scale,
                            physical_thickness * 3.0,
                        )
                    )
                )
            )
            if (
                not different_known_support
                and (distance <= 1.0 or same_thick_stroke)
                and distance < best_distance
            ):
                best = cluster
                best_distance = distance
        if best is None:
            clusters.append([fragment])
        else:
            best.append(fragment)
    result: list[tuple[float, float, float, int]] = []
    for cluster in clusters:
        positions = np.asarray([item.intercept for item in cluster], dtype=float)
        angles = np.asarray([item.angle for item in cluster], dtype=float)
        lengths = np.asarray([item.length for item in cluster], dtype=float)
        try:
            angle = weighted_median(angles, lengths)
        except ValueError:
            continue
        labels = {item.support_label for item in cluster if item.support_label}
        if support_angles is not None and len(labels) == 1:
            angle = support_angles.get(next(iter(labels)), angle)
        result.append(
            (
                float(np.average(positions, weights=lengths)),
                angle,
                float(np.max(lengths)),
                len(cluster),
            )
        )
    return result


def vertical_support_angles(
    labels: np.ndarray, support_labels: set[int]
) -> dict[int, float]:
    """Fit one physical vertical-stroke angle to each connected component."""
    result: dict[int, float] = {}
    for label in support_labels:
        ys, xs = np.nonzero(labels == label)
        if ys.size < 2:
            continue
        centered_y = ys.astype(float) - float(np.mean(ys))
        denominator = float(np.dot(centered_y, centered_y))
        if denominator <= 0:
            continue
        centered_x = xs.astype(float) - float(np.mean(xs))
        slope = float(np.dot(centered_y, centered_x) / denominator)
        result[label] = float(np.degrees(np.arctan(slope)))
    return result


def angular_consistency(
    angles: np.ndarray, weights: np.ndarray, tolerance: float = 2.0
) -> float:
    if angles.size < 2:
        return 0.0
    center = weighted_median(angles, weights)
    deviations = np.abs(angles - center)
    order = np.argsort(deviations, kind="stable")
    cumulative = np.cumsum(weights[order])
    p90 = float(
        deviations[order][
            np.searchsorted(cumulative, float(np.sum(weights)) * 0.9)
        ]
    )
    inlier_weight = float(np.sum(weights[deviations <= tolerance]))
    inlier_fraction = inlier_weight / float(np.sum(weights))
    return max(0.0, 1.0 - p90 / (tolerance * 2.0)) * inlier_fraction


def horizontal_skew(mask: np.ndarray) -> dict[str, object]:
    if mask.ndim != 2 or mask.size == 0 or min(mask.shape) < 8:
        return {
            "status": "insufficient_evidence",
            "angle_degrees_clockwise": None,
            "deskew_rotation_degrees_ccw": None,
            "confidence": 0.0,
            "projection_peak_ratio": None,
            "hough_line_count": 0,
            "hough_line_limit_reached": False,
            "independent_line_count": 0,
            "angular_consistency": 0.0,
            "hough_angle_degrees_clockwise": None,
        }
    ink_pixels = int(np.count_nonzero(mask))
    if ink_pixels < max(250, round(mask.size * 0.001)):
        return {
            "status": "insufficient_content",
            "angle_degrees_clockwise": None,
            "deskew_rotation_degrees_ccw": None,
            "confidence": 0.0,
            "projection_peak_ratio": None,
            "hough_line_count": 0,
            "hough_line_limit_reached": False,
            "independent_line_count": 0,
            "angular_consistency": 0.0,
            "hough_angle_degrees_clockwise": None,
        }

    coarse_angles = np.arange(-5.0, 5.0001, 0.25)
    coarse_scores = np.asarray(
        [projection_score(mask, float(angle)) for angle in coarse_angles]
    )
    coarse_best = float(coarse_angles[int(np.argmax(coarse_scores))])
    fine_angles = np.arange(
        max(-5.0, coarse_best - 0.3), min(5.0, coarse_best + 0.3) + 0.001, 0.05
    )
    fine_scores = np.asarray(
        [projection_score(mask, float(angle)) for angle in fine_angles]
    )
    best_index = int(np.argmax(fine_scores))
    deskew_ccw = float(fine_angles[best_index])
    baseline = float(np.median(coarse_scores))
    peak_ratio = float(fine_scores[best_index] / max(baseline, 1e-9))

    lines = cv2.HoughLinesP(
        cv2.Canny(mask, 50, 150),
        1,
        np.pi / 720,
        threshold=max(20, mask.shape[1] // 30),
        minLineLength=max(20, round(mask.shape[1] * 0.10)),
        maxLineGap=max(4, round(mask.shape[1] * 0.02)),
    )
    raw_fragments: list[LineFragment] = []
    _, support_labels = cv2.connectedComponents((mask != 0).astype(np.uint8), 8)
    hough_output_truncated = (
        lines is not None and len(lines) > MAX_HOUGH_LINE_CANDIDATES
    )
    if lines is not None:
        for x1, y1, x2, y2 in lines[:MAX_HOUGH_LINE_CANDIDATES, 0]:
            dx, dy = int(x2) - int(x1), int(y2) - int(y1)
            angle = float(np.degrees(np.arctan2(dy, dx)))
            if angle > 90:
                angle -= 180
            elif angle < -90:
                angle += 180
            length = float(np.hypot(dx, dy))
            if (
                abs(angle) <= 12
                and length >= 0.1 * mask.shape[1]
            ):
                slope = float(dy) / float(dx) if dx else 0.0
                center_intercept = (
                    float(y1) - slope * float(x1)
                    + slope * (mask.shape[1] - 1) / 2.0
                )
                if 0.04 * mask.shape[0] < center_intercept < 0.96 * mask.shape[0]:
                    fragment = LineFragment(
                        center_intercept,
                        angle,
                        length,
                        (int(x1), int(y1), int(x2), int(y2)),
                    )
                    raw_fragments.append(fragment)
    selected_fragments, fragment_selection_truncated = strongest_line_fragments(
        raw_fragments
    )
    fragments = [
        characterize_fragment_support(fragment, mask, support_labels)
        for fragment in selected_fragments
    ]
    bands = cluster_line_fragments(
        fragments, max(2.0, mask.shape[0] * 0.012), angle_tolerance=1.25
    )
    hough_angle = (
        weighted_median(
            np.asarray([band[1] for band in bands]),
            np.asarray([band[2] for band in bands]),
        )
        if bands
        else None
    )

    # Rotating an image counter-clockwise by +a corrects a clockwise baseline of +a.
    skew_clockwise = deskew_ccw
    agreement = (
        max(0.0, 1.0 - abs(skew_clockwise - hough_angle) / 2.0)
        if hough_angle is not None
        else 0.5
    )
    independence = min(1.0, len(bands) / 3.0)
    line_angular_consistency = (
        angular_consistency(
            np.asarray([band[1] for band in bands]),
            np.asarray([band[2] for band in bands]),
        )
        if len(bands) >= 2
        else 0.0
    )
    confidence = (
        min(1.0, max(0.0, (peak_ratio - 1.0) / 0.18))
        * agreement
        * independence
        * line_angular_consistency
    )
    at_search_limit = abs(deskew_ccw) >= 4.95
    evidence_truncated = hough_output_truncated or fragment_selection_truncated
    if evidence_truncated:
        status = "review_only_truncated"
    elif len(bands) < 2:
        status = "no_line_evidence"
    elif at_search_limit:
        status = "search_limit"
    elif peak_ratio < 1.01 or confidence < 0.5:
        status = "low_confidence"
    else:
        status = "ok"
    return {
        "status": status,
        "angle_degrees_clockwise": round(skew_clockwise, 3),
        "deskew_rotation_degrees_ccw": round(deskew_ccw, 3),
        "confidence": round(confidence, 3),
        "projection_peak_ratio": round(peak_ratio, 4),
        "hough_line_count": len(fragments),
        "hough_line_limit_reached": evidence_truncated,
        "independent_line_count": len(bands),
        "angular_consistency": round(line_angular_consistency, 3),
        "hough_angle_degrees_clockwise": (
            round(hough_angle, 3) if hough_angle is not None else None
        ),
    }


def vertical_convergence(mask: np.ndarray) -> dict[str, object]:
    def empty_side() -> dict[str, object]:
        return {
            "line_count": 0,
            "total_length_fraction": 0.0,
            "count_confidence": 0.0,
            "total_length_confidence": 0.0,
            "x_coverage_fraction": 0.0,
            "x_coverage_confidence": 0.0,
            "band_coverage_fraction": 0.0,
            "band_coverage_confidence": 0.0,
            "system_coverage_fraction": 0.0,
            "system_coverage_confidence": 0.0,
            "coverage_confidence": 0.0,
            "angular_consistency": 0.0,
            "confidence": 0.0,
        }

    if mask.ndim != 2 or mask.size == 0 or min(mask.shape) < 8:
        return {
            "status": "insufficient_evidence",
            "line_count": 0,
            "hough_line_limit_reached": False,
            "independent_line_count": 0,
            "left_line_count": 0,
            "right_line_count": 0,
            "left_deviation_degrees": None,
            "right_deviation_degrees": None,
            "left_angle_spread_degrees": None,
            "right_angle_spread_degrees": None,
            "angular_consistency": 0.0,
            "minimum_line_length_fraction": 0.045,
            "median_line_length_fraction": 0.0,
            "length_confidence": 0.0,
            "right_minus_left_degrees": None,
            "confidence": 0.0,
            "left_confidence": 0.0,
            "right_confidence": 0.0,
            "left_evidence": empty_side(),
            "right_evidence": empty_side(),
        }
    height, width = mask.shape
    minimum_length_fraction = 0.045
    kernel_height = max(7, round(height * 0.03))
    vertical = cv2.morphologyEx(
        mask, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_RECT, (1, kernel_height))
    )
    lines = cv2.HoughLinesP(
        vertical,
        1,
        np.pi / 720,
        threshold=max(10, height // 70),
        minLineLength=max(14, round(height * minimum_length_fraction)),
        maxLineGap=max(3, round(height * 0.012)),
    )
    raw_fragments: list[LineFragment] = []
    _, support_labels = cv2.connectedComponents((vertical != 0).astype(np.uint8), 8)
    hough_output_truncated = (
        lines is not None and len(lines) > MAX_HOUGH_LINE_CANDIDATES
    )
    if lines is not None:
        for x1, y1, x2, y2 in lines[:MAX_HOUGH_LINE_CANDIDATES, 0]:
            if y1 <= y2:
                top_x, top_y, bottom_x, bottom_y = x1, y1, x2, y2
            else:
                top_x, top_y, bottom_x, bottom_y = x2, y2, x1, y1
            dy = int(bottom_y) - int(top_y)
            dx = int(bottom_x) - int(top_x)
            if dy <= 0:
                continue
            deviation = float(np.degrees(np.arctan2(dx, dy)))
            length = float(np.hypot(dx, dy))
            if (
                abs(deviation) <= 15
                and length >= height * minimum_length_fraction
            ):
                slope = float(dx) / float(dy)
                center_intercept = (
                    float(top_x) - slope * float(top_y)
                    + slope * (height - 1) / 2.0
                ) / width
                if 0.04 < center_intercept < 0.96:
                    fragment = LineFragment(
                        center_intercept,
                        deviation,
                        length,
                        (int(top_x), int(top_y), int(bottom_x), int(bottom_y)),
                    )
                    raw_fragments.append(fragment)
    selected_fragments, fragment_selection_truncated = strongest_line_fragments(
        raw_fragments
    )
    fragments = [
        characterize_fragment_support(fragment, vertical, support_labels)
        for fragment in selected_fragments
    ]
    evidence = cluster_line_fragments(
        fragments,
        max(0.035, 5.0 / width),
        angle_tolerance=1.5,
        intercept_scale=float(width),
        connected_support_one_vote=True,
        support_angles=vertical_support_angles(
            support_labels,
            {fragment.support_label for fragment in fragments if fragment.support_label},
        ),
    )

    def side_statistics(
        items: list[tuple[float, float, float, int]],
    ) -> tuple[float | None, float | None]:
        if not items:
            return None, None
        angle = weighted_median(
            np.asarray([item[1] for item in items]),
            np.asarray([item[2] for item in items]),
        )
        angles = np.asarray([item[1] for item in items], dtype=float)
        return angle, float(np.max(angles) - np.min(angles))

    left = [item for item in evidence if item[0] <= 0.45]
    right = [item for item in evidence if item[0] >= 0.55]
    left_angle, left_angle_spread = side_statistics(left)
    right_angle, right_angle_spread = side_statistics(right)
    spread = (
        right_angle - left_angle
        if left_angle is not None and right_angle is not None
        else None
    )
    enough = len(left) >= 2 and len(right) >= 2

    def interval_coverage(intervals: list[tuple[int, int]]) -> float:
        if not intervals:
            return 0.0
        merged: list[list[int]] = []
        for start, end in sorted(intervals):
            if not merged or start > merged[-1][1] + 1:
                merged.append([start, end])
            else:
                merged[-1][1] = max(merged[-1][1], end)
        return min(1.0, sum(end - start + 1 for start, end in merged) / height)

    def side_evidence(
        items: list[tuple[float, float, float, int]],
        lower: float,
        upper: float,
    ) -> dict[str, object]:
        if not items:
            return empty_side()
        positions = np.asarray([item[0] for item in items], dtype=float)
        lengths = np.asarray([item[2] for item in items], dtype=float)
        angles = np.asarray([item[1] for item in items], dtype=float)
        count_confidence = min(1.0, len(items) / 2.0)
        total_length_fraction = float(np.sum(lengths) / height)
        total_length_confidence = min(1.0, total_length_fraction / 0.18)
        x_span = float(np.max(positions) - np.min(positions)) if len(items) >= 2 else 0.0
        x_coverage = min(1.0, x_span / max(upper - lower, 1e-9))
        x_coverage_confidence = min(1.0, x_span / 0.12)
        bands = {
            min(2, int((position - lower) / max(upper - lower, 1e-9) * 3.0))
            for position in positions
        }
        band_coverage = len(bands) / 3.0
        band_coverage_confidence = min(1.0, len(bands) / 2.0)
        relevant_fragments = [
            fragment
            for fragment in fragments
            if lower <= fragment.intercept <= upper
        ]
        system_coverage = interval_coverage(
            [
                (
                    max(0, min(fragment.endpoints[1], fragment.endpoints[3])),
                    min(height - 1, max(fragment.endpoints[1], fragment.endpoints[3])),
                )
                for fragment in relevant_fragments
            ]
        )
        system_confidence = min(1.0, system_coverage / 0.18)
        coverage_confidence = min(
            x_coverage_confidence,
            band_coverage_confidence,
            system_confidence,
        )
        consistency = (
            angular_consistency(angles, lengths) if len(items) >= 2 else 0.0
        )
        confidence = (
            count_confidence
            * total_length_confidence
            * coverage_confidence
            * consistency
        )
        return {
            "line_count": len(items),
            "total_length_fraction": round(total_length_fraction, 4),
            "count_confidence": round(count_confidence, 3),
            "total_length_confidence": round(total_length_confidence, 3),
            "x_coverage_fraction": round(x_coverage, 3),
            "x_coverage_confidence": round(x_coverage_confidence, 3),
            "band_coverage_fraction": round(band_coverage, 3),
            "band_coverage_confidence": round(band_coverage_confidence, 3),
            "system_coverage_fraction": round(system_coverage, 3),
            "system_coverage_confidence": round(system_confidence, 3),
            "coverage_confidence": round(coverage_confidence, 3),
            "angular_consistency": round(consistency, 3),
            "confidence": round(confidence, 3),
        }

    left_evidence = side_evidence(left, 0.04, 0.45)
    right_evidence = side_evidence(right, 0.55, 0.96)
    combined_angular_consistency = min(
        float(left_evidence["angular_consistency"]),
        float(right_evidence["angular_consistency"]),
    )
    structural_lengths = [item[2] / height for item in left + right]
    median_length_fraction = (
        float(np.median(structural_lengths)) if structural_lengths else 0.0
    )
    length_confidence = min(
        1.0,
        max(
            0.0,
            (
                min(
                    float(np.median([item[2] / height for item in left])),
                    float(np.median([item[2] / height for item in right])),
                )
                - minimum_length_fraction
            )
            / minimum_length_fraction,
        ),
    ) if left and right else 0.0
    confidence = (
        min(
            float(left_evidence["confidence"]),
            float(right_evidence["confidence"]),
        )
        * length_confidence
    )
    evidence_truncated = hough_output_truncated or fragment_selection_truncated
    status = (
        "review_only_truncated"
        if evidence_truncated
        else (
            "insufficient_structural_lines"
            if not enough
            else ("low_confidence" if confidence < 0.5 else "ok")
        )
    )
    return {
        "status": status,
        "line_count": len(fragments),
        "hough_line_limit_reached": evidence_truncated,
        "independent_line_count": len(evidence),
        "left_line_count": len(left),
        "right_line_count": len(right),
        "left_deviation_degrees": (
            round(left_angle, 3) if left_angle is not None else None
        ),
        "right_deviation_degrees": (
            round(right_angle, 3) if right_angle is not None else None
        ),
        "left_angle_spread_degrees": (
            round(left_angle_spread, 3) if left_angle_spread is not None else None
        ),
        "right_angle_spread_degrees": (
            round(right_angle_spread, 3) if right_angle_spread is not None else None
        ),
        "angular_consistency": round(combined_angular_consistency, 3),
        "minimum_line_length_fraction": minimum_length_fraction,
        "median_line_length_fraction": round(median_length_fraction, 4),
        "length_confidence": round(length_confidence, 3),
        "right_minus_left_degrees": round(spread, 3) if spread is not None else None,
        "confidence": round(confidence, 3),
        "left_confidence": left_evidence["confidence"],
        "right_confidence": right_evidence["confidence"],
        "left_evidence": left_evidence,
        "right_evidence": right_evidence,
    }


def trusted_vertical_convergence(convergence: dict[str, object]) -> bool:
    return (
        convergence.get("status") == "ok"
        and float(convergence.get("confidence", 0.0)) >= 0.5
        and float(convergence.get("length_confidence", 0.0)) >= 0.5
    )


def page_metrics(
    path: Path, order: int, data: bytes | None = None
) -> dict[str, object]:
    image, decode = read_gray(
        path, data, max_dimension=ANALYSIS_MAX_DIMENSION
    )
    analysis_height, analysis_width = image.shape
    if (
        int(decode["analysis_width"]) != analysis_width
        or int(decode["analysis_height"]) != analysis_height
    ):
        raise ValueError("analysis raster dimensions disagree with decode metadata")
    width = int(decode["display_width"])
    height = int(decode["display_height"])
    if height < 8 or width < 8 or analysis_height < 2 or analysis_width < 2:
        raise ValueError("image dimensions are too small for reliable analysis")
    scale = min(1.0, ANALYSIS_MAX_DIMENSION / max(analysis_height, analysis_width))
    small = (
        cv2.resize(image, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        if scale < 1.0
        else image
    )
    percentiles = np.percentile(small, [1, 10, 50, 90, 99])
    mask, relative = paper_relative_metrics(small)
    return {
        "file": path.name,
        "order": order,
        "format": filename_format(path),
        **decode,
        "width": width,
        "height": height,
        "aspect_ratio": decode["display_aspect_ratio"],
        "p01": float(percentiles[0]),
        "p10": float(percentiles[1]),
        "median": float(percentiles[2]),
        "p90": float(percentiles[3]),
        "p99": float(percentiles[4]),
        "paper_estimate": {
            "global_level": relative["global_paper_level"],
            "local_median": relative["local_paper_median"],
        },
        "ink_contrast_threshold": relative["ink_contrast_threshold"],
        "ink_fraction": relative["ink_fraction"],
        "border_dark_fraction": relative["border_contamination_fraction"],
        "horizontal_skew": horizontal_skew(mask),
        "vertical_convergence": vertical_convergence(mask),
    }


def serialized_page_metrics(row: dict[str, object]) -> dict[str, object]:
    serialized = dict(row)
    for field in ("p01", "p10", "median", "p90", "p99", "ink_contrast_threshold"):
        if field in serialized:
            serialized[field] = round(float(serialized[field]), 2)
    for field in ("ink_fraction", "border_dark_fraction"):
        if field in serialized:
            serialized[field] = round(float(serialized[field]), 6)
    if isinstance(paper := serialized.get("paper_estimate"), dict):
        serialized["paper_estimate"] = {
            **paper,
            "global_level": round(float(paper["global_level"]), 2),
            "local_median": round(float(paper["local_median"]), 2),
        }
    return serialized


def robust_outliers(
    rows: list[dict[str, object]], value_getter: Any, absolute_limit: float | None = None
) -> list[str]:
    candidates = [
        (str(row["file"]), float(value))
        for row in rows
        if (value := value_getter(row)) is not None and np.isfinite(float(value))
    ]
    if not candidates:
        return []
    values = np.asarray([item[1] for item in candidates])
    median = float(np.median(values))
    mad = float(np.median(np.abs(values - median)))
    if mad > 1e-12:
        flagged = np.abs(values - median) / (1.4826 * mad) > 3.5
    else:
        flagged = np.abs(values - median) > 1e-12
    if absolute_limit is not None:
        flagged |= np.abs(values) >= absolute_limit
    return [candidates[index][0] for index in np.where(flagged)[0]]


def numeric_summary(values: list[float]) -> dict[str, object]:
    finite_values = [float(value) for value in values if np.isfinite(float(value))]
    if not finite_values:
        return {"count": 0, "min": None, "median": None, "max": None}
    array = np.asarray(finite_values)
    return {
        "count": len(finite_values),
        "min": round(float(np.min(array)), 3),
        "median": round(float(np.median(array)), 3),
        "max": round(float(np.max(array)), 3),
    }


def absolute_quality_summary(
    rows: list[dict[str, object]], field: str, metric: str
) -> dict[str, object]:
    values = [
        float(row[field]) for row in rows if np.isfinite(float(row[field]))
    ]
    summary = numeric_summary(values)
    thresholds = ABSOLUTE_QUALITY_THRESHOLDS[metric]
    warnings: list[str] = []
    if values:
        raw_values = np.asarray(values)
        minimum = float(np.min(raw_values))
        median = float(np.median(raw_values))
        maximum = float(np.max(raw_values))
        if (
            threshold := thresholds.get("page_min_warning_below")
        ) is not None and minimum < threshold:
            warnings.append(
                f"at least one page is below the absolute minimum warning threshold ({threshold:g})"
            )
        if (
            threshold := thresholds.get("batch_median_warning_below")
        ) is not None and median < threshold:
            warnings.append(
                f"batch median is below the absolute warning threshold ({threshold:g})"
            )
        if (
            threshold := thresholds.get("batch_median_warning_above")
        ) is not None and median > threshold:
            warnings.append(
                f"batch median is above the absolute warning threshold ({threshold:g})"
            )
        if (
            threshold := thresholds.get("page_max_warning_above")
        ) is not None and maximum > threshold:
            warnings.append(
                f"at least one page is above the absolute maximum warning threshold ({threshold:g})"
            )
    return {
        **summary,
        "thresholds": thresholds,
        "warnings": warnings,
        "status": "warning" if warnings else ("ok" if values else "no_data"),
    }


def ordered_union(sequence: list[dict[str, object]], *groups: list[str]) -> list[str]:
    wanted = {name for group in groups for name in group}
    return [str(item["file"]) for item in sequence if str(item["file"]) in wanted]


def representative_lists(sequence: list[dict[str, object]]) -> dict[str, list[str]]:
    if not sequence:
        return {"beginning": [], "middle": [], "end": [], "combined": []}
    decoded = [
        (index, item)
        for index, item in enumerate(sequence)
        if item["status"] == "decoded"
    ]
    if not decoded:
        return {"beginning": [], "middle": [], "end": [], "combined": []}

    def nearest(target: float) -> list[str]:
        _, item = min(decoded, key=lambda pair: (abs(pair[0] - target), pair[0]))
        return [str(item["file"])]

    beginning = nearest(0.0)
    middle = nearest((len(sequence) - 1) / 2.0)
    end = nearest(float(len(sequence) - 1))
    return {
        "beginning": beginning,
        "middle": middle,
        "end": end,
        "combined": ordered_union(sequence, beginning, middle, end),
    }


def mandatory_review_entries(
    sequence: list[dict[str, object]],
    representatives: dict[str, list[str]],
    outliers: dict[str, list[str]],
) -> list[dict[str, object]]:
    reasons: dict[str, list[str]] = {}
    for region in ("beginning", "middle", "end"):
        for name in representatives[region]:
            reasons.setdefault(name, []).append(f"representative {region} page")
    for category, names in outliers.items():
        if category != "combined":
            for name in names:
                reasons.setdefault(name, []).append(f"outlier: {category}")
    for item in sequence:
        name = str(item["file"])
        if item["status"] == "decode_failed":
            reasons.setdefault(name, []).append(f"decode failed: {item['error']}")
        elif str(item["status"]).startswith("unsupported_frame_count"):
            reasons.setdefault(name, []).append(str(item["reason"]))
    return [
        {"file": str(item["file"]), "reasons": reasons[str(item["file"])]}
        for item in sequence
        if str(item["file"]) in reasons
    ]


def format_inventory(
    files: list[Path],
    successful_names: set[str],
    unsupported_names: set[str],
) -> list[dict[str, object]]:
    candidate_counts = Counter(filename_format(path) for path in files)
    success_counts = Counter(
        filename_format(path) for path in files if path.name in successful_names
    )
    unsupported_counts = Counter(
        filename_format(path) for path in files if path.name in unsupported_names
    )
    return [
        {
            "format": extension,
            "candidate_count": candidate_counts[extension],
            "successful_decodes": success_counts[extension],
            "failed_decodes": (
                candidate_counts[extension]
                - success_counts[extension]
                - unsupported_counts[extension]
            ),
            "unsupported_candidates": unsupported_counts[extension],
        }
        for extension in sorted(candidate_counts)
    ]


def validate_output_parent(parent: Path) -> None:
    if not parent.is_absolute():
        raise ValueError("report parent must be absolute")
    if not parent.exists() or not parent.is_dir():
        raise ValueError(f"report parent directory does not exist: {parent}")


def validate_output_path(output: Path, source_paths: list[Path]) -> Path:
    output = output.expanduser()
    if not output.is_absolute():
        raise ValueError("--output must be an absolute path")
    output = Path(os.path.abspath(output))
    if output.suffix.lower() != ".json":
        raise ValueError("--output must name a .json file")
    validate_output_parent(output.parent)
    if output.exists() and output.is_dir():
        raise ValueError(f"output path is a directory: {output}")

    resolved_output = output.resolve(strict=False)
    for source in source_paths:
        resolved_source = source.resolve(strict=True)
        if os.path.normcase(str(resolved_output)) == os.path.normcase(
            str(resolved_source)
        ):
            raise ValueError(f"output resolves to source image: {source}")
        if output.exists():
            try:
                if os.path.samefile(output, source):
                    raise ValueError(f"output is the same file as source image: {source}")
            except FileNotFoundError:
                pass
    return output


def write_json_atomic(
    output: Path, report: dict[str, object], source_paths: list[Path]
) -> None:
    output = validate_output_path(output, source_paths)
    handle = tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        prefix=f".{output.name}.",
        suffix=".tmp",
        dir=output.parent,
        delete=False,
    )
    temporary = Path(handle.name)
    try:
        with handle:
            json.dump(report, handle, indent=2, allow_nan=False)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, output)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description="Analyze a batch of scanned pages.")
    parser.add_argument("input", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not args.input.expanduser().is_absolute():
        parser.error("input directory must be an absolute path")
    input_dir = args.input.expanduser().resolve()
    if not input_dir.is_dir():
        parser.error(f"input directory does not exist: {input_dir}")

    try:
        files, unsupported_files = inventory_directory(input_dir)
    except (OSError, ValueError) as error:
        parser.error(f"cannot inventory input directory: {error}")
    if not files:
        parser.error(f"no supported images found in {input_dir}")
    try:
        output = validate_output_path(args.output, files)
    except ValueError as error:
        parser.error(str(error))

    unsupported_files = [
        path
        for path in unsupported_files
        if path.resolve(strict=True) != output
    ]

    rows: list[dict[str, object]] = []
    decode_failures: list[dict[str, object]] = []
    unsupported_candidates: list[dict[str, object]] = []
    candidate_sequence: list[dict[str, object]] = []
    for index, path in enumerate(files, 1):
        try:
            encoded_data = read_encoded_file(path)
        except Exception as error:
            encoded_data = None
            frame_count, frame_count_error = None, (
                f"bounded encoded-file read failed: {type(error).__name__}: {error}"
            )
        else:
            frame_count, frame_count_error = image_frame_count(path, encoded_data)
        if frame_count != 1:
            if frame_count_error:
                reason = (
                    f"unsupported image with unknown frame count: {frame_count_error}; "
                    "provide exactly one decodable image per file"
                )
                status = "unsupported_frame_count_unknown"
            else:
                reason = (
                    f"unsupported image ({frame_count} frames); "
                    "provide exactly one image per file"
                )
                status = "unsupported_frame_count"
            unsupported = {
                "file": path.name,
                "order": index,
                "format": filename_format(path),
                "frame_count": frame_count,
                "frame_count_error": frame_count_error,
                "reason": reason,
            }
            unsupported_candidates.append(unsupported)
            candidate_sequence.append({**unsupported, "status": status})
            print(f"warning: {path.name}: {reason}", file=sys.stderr)
            print(f"analyzed {index}/{len(files)}", file=sys.stderr)
            continue
        try:
            assert encoded_data is not None
            row = page_metrics(path, index, encoded_data)
            rows.append(row)
            candidate_sequence.append(
                {
                    "file": path.name,
                    "order": index,
                    "format": filename_format(path),
                    "status": "decoded",
                    "error": None,
                }
            )
        except Exception as error:
            failure = {
                "file": path.name,
                "order": index,
                "format": filename_format(path),
                "error": str(error),
            }
            decode_failures.append(failure)
            candidate_sequence.append(
                {**failure, "status": "decode_failed"}
            )
            print(f"warning: cannot decode {path.name}: {error}", file=sys.stderr)
        print(f"analyzed {index}/{len(files)}", file=sys.stderr)

    successful_names = {str(row["file"]) for row in rows}
    unsupported_names = {str(item["file"]) for item in unsupported_candidates}
    representatives = representative_lists(candidate_sequence)
    outliers = {
        "width": robust_outliers(rows, lambda row: row["width"]),
        "height": robust_outliers(rows, lambda row: row["height"]),
        "aspect_ratio": robust_outliers(rows, lambda row: row["aspect_ratio"]),
        "brightness": robust_outliers(rows, lambda row: row["median"]),
        "ink_fraction": robust_outliers(rows, lambda row: row["ink_fraction"]),
        "border_contamination": robust_outliers(
            rows, lambda row: row["border_dark_fraction"]
        ),
        "horizontal_skew": robust_outliers(
            rows,
            lambda row: row["horizontal_skew"]["angle_degrees_clockwise"]
            if row["horizontal_skew"]["status"] == "ok"
            else None,
            absolute_limit=0.35,
        ),
        "vertical_convergence": robust_outliers(
            rows,
            lambda row: row["vertical_convergence"]["right_minus_left_degrees"]
            if trusted_vertical_convergence(row["vertical_convergence"])
            else None,
            absolute_limit=0.5,
        ),
        "decode_failures": [item["file"] for item in decode_failures],
        "unsupported_frame_count": [
            item["file"] for item in unsupported_candidates
        ],
        "geometry_uncertainty": [
            str(row["file"])
            for row in rows
            if row["horizontal_skew"]["status"] != "ok"
            or float(row["horizontal_skew"]["confidence"]) < 0.5
            or not trusted_vertical_convergence(row["vertical_convergence"])
        ],
    }
    outliers["combined"] = ordered_union(candidate_sequence, *outliers.values())

    widths = [int(row["width"]) for row in rows]
    heights = [int(row["height"]) for row in rows]
    aspect_ratios = [float(row["aspect_ratio"]) for row in rows]
    skew_values = [
        float(row["horizontal_skew"]["angle_degrees_clockwise"])
        for row in rows
        if row["horizontal_skew"]["status"] == "ok"
    ]
    convergence_values = [
        float(row["vertical_convergence"]["right_minus_left_degrees"])
        for row in rows
        if trusted_vertical_convergence(row["vertical_convergence"])
    ]
    quality_summary = {
        "brightness": absolute_quality_summary(rows, "median", "brightness"),
        "ink_fraction": absolute_quality_summary(
            rows, "ink_fraction", "ink_fraction"
        ),
        "border_contamination": absolute_quality_summary(
            rows, "border_dark_fraction", "border_contamination"
        ),
    }
    mandatory_entries = mandatory_review_entries(
        candidate_sequence, representatives, outliers
    )
    report: dict[str, object] = {
        "schema_version": 14,
        "input": str(input_dir),
        "ordering": "case-insensitive natural filename order; original filename tie-break",
        "format_detection": "candidates are selected by filename extension; Pillow inspects headers and is restricted to the JPEG, PNG, TIFF, and WebP allowlist for oriented single-frame decoding",
        "supported_filename_extensions": sorted(SUPPORTED_EXTENSIONS),
        "candidate_count": len(files),
        "page_count": len(rows),
        "failed_decode_count": len(decode_failures),
        "unsupported_candidate_count": len(unsupported_candidates),
        "decode_failures": decode_failures,
        "unsupported_candidates": unsupported_candidates,
        "candidate_sequence": candidate_sequence,
        "format_inventory": format_inventory(
            files, successful_names, unsupported_names
        ),
        "unsupported_files": [
            {
                "file": path.name,
                "extension": path.suffix.lower() or None,
                "reason": "unsupported filename extension",
            }
            for path in unsupported_files
        ],
        "dimensions": (
            {
                "width": {
                    "min": min(widths),
                    "median": int(np.median(widths)),
                    "max": max(widths),
                },
                "height": {
                    "min": min(heights),
                    "median": int(np.median(heights)),
                    "max": max(heights),
                },
                "aspect_ratio": numeric_summary(aspect_ratios),
            }
            if rows
            else None
        ),
        "geometry_summary": {
            "horizontal_skew_degrees_clockwise": numeric_summary(skew_values),
            "vertical_right_minus_left_degrees": numeric_summary(convergence_values),
        },
        "quality_summary": quality_summary,
        "review_lists": {
            "representative": representatives,
            "outliers": outliers,
            "mandatory_visual_review": ordered_union(
                candidate_sequence, representatives["combined"], outliers["combined"]
            ),
            "mandatory_visual_review_entries": mandatory_entries,
        },
        "pages": [serialized_page_metrics(row) for row in rows],
    }
    write_json_atomic(output, report, files)
    print(
        json.dumps(
            {
                "candidate_count": len(files),
                "page_count": len(rows),
                "failed_decode_count": len(decode_failures),
                "unsupported_candidate_count": len(unsupported_candidates),
                "output": str(output),
            },
            allow_nan=False,
        )
    )
    if not rows:
        print("error: zero images decoded successfully; report was written", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
