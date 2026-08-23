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
import hashlib
import io
import json
import os
import re
import shutil
import sys
import tempfile
import warnings
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator, NamedTuple

import cv2
import numpy as np
import tifffile
from PIL import Image, ImageOps, UnidentifiedImageError


EXTENSIONS = {
    ".jpe", ".jfif", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp",
}
IMAGE_LIKE_EXTENSIONS = EXTENSIONS | {
    ".apng", ".avif", ".bmp", ".cur", ".dcx", ".dds", ".dib", ".emf", ".eps",
    ".exr", ".fits", ".flc", ".fli", ".gif", ".heic", ".heif", ".icns", ".ico",
    ".j2c", ".j2k", ".jf2", ".jpc", ".jp2", ".jpf", ".jpm", ".jpx", ".jxl",
    ".mpo", ".pbm", ".pcx", ".pdf", ".pgm", ".pnm", ".ppm", ".ps", ".psd",
    ".qoi", ".raw", ".sgi", ".svg", ".svgz", ".tga", ".wmf", ".xbm", ".xpm",
}
ENCODED_FORMATS = ("JPEG", "PNG", "TIFF", "WEBP")
DEFAULT_MAX_ENCODED_BYTES = 256 * 1024 * 1024
DEFAULT_MAX_PIXELS_PER_PAGE = 100_000_000
DEFAULT_MAX_PAGE_COUNT = 10_000
DEFAULT_MAX_TOTAL_PIXELS = 1_000_000_000
DEFAULT_MAX_INVENTORY_ENTRIES = 20_000
DEFAULT_MAX_INVENTORY_BYTES = 4 * 1024 * 1024 * 1024
DEFAULT_MAX_WORKING_BYTES_PER_PAGE = 8 * 1024 * 1024 * 1024
DEFAULT_MAX_WORK_UNITS_PER_PAGE = 500_000_000_000
DEFAULT_BACKGROUND_SCALE = 34.0
DEFAULT_MIN_BACKGROUND_SIGMA = 24.0
MAX_BACKGROUND_KERNEL_DIMENSION = 511
MAX_EXTREME_ASPECT_KERNEL_DIMENSION = 127
EXTREME_ASPECT_RATIO = 64
MAX_MORPHOLOGY_KERNEL_DIMENSION = 511
MAX_MORPHOLOGY_ITERATIONS = 32
CONTINUOUS_TONE_ANALYSIS_LONG_EDGE = 600
DEFAULT_PAPER_LEVEL = 247.0
DEFAULT_BLACK_PERCENTILE = 0.35
DEFAULT_BLACK_FLOOR = 32.0
DEFAULT_BLACK_CEILING = 98.0
DEFAULT_WHITE_PERCENTILE = 88.0
DEFAULT_WHITE_FLOOR = 215.0
DEFAULT_MIN_TONE_RANGE = 40.0
DEFAULT_WHITEN_START = 178.0
DEFAULT_WHITEN_WIDTH = 58.0
DEFAULT_WHITE_CLIP = 251
MIN_DARK_CONTENT_CONTRAST = 50.0 / 255.0
FAINT_INK_START = 0.0
FAINT_INK_CONFIDENT = 6.0 / 255.0
MIN_FAINT_INK_CONTRAST = 2.0 / 255.0
EXIF_ORIENTATION = 274


class FileStat(NamedTuple):
    size: int
    mtime_ns: int
    ctime_ns: int
    device: int
    inode: int


class InputInfo(NamedTuple):
    path: Path
    file_hash: str | None
    file_stat: FileStat
    encoded_format: str
    width: int
    height: int
    encoded_bytes: int
    pixels: int
    source_bit_depth: int


class BackgroundBlurPlan(NamedTuple):
    size: tuple[int, int]
    kernel: tuple[int, int]
    sigma_x: float
    sigma_y: float


class UnsupportedBackgroundSigma(ValueError):
    pass


ASCII_WHITESPACE = b" \t\r\n\f"


def skip_ascii_whitespace(data: bytes, index: int) -> int:
    while index < len(data) and data[index] in ASCII_WHITESPACE:
        index += 1
    return index


def looks_like_svg(header: bytes) -> bool:
    lowered = header.lower()
    index = 3 if header.startswith(b"\xef\xbb\xbf") else 0
    index = skip_ascii_whitespace(header, index)

    if lowered.startswith(b"<?xml", index):
        declaration_end = lowered.find(b"?>", index + 5)
        if declaration_end < 0:
            return False
        index = skip_ascii_whitespace(header, declaration_end + 2)

    while lowered.startswith(b"<!--", index):
        comment_end = lowered.find(b"-->", index + 4)
        if comment_end < 0:
            return False
        index = skip_ascii_whitespace(header, comment_end + 3)

    if lowered.startswith(b"<!doctype", index):
        name_start = index + len(b"<!doctype")
        if name_start >= len(header) or header[name_start] not in ASCII_WHITESPACE:
            return False
        name_start = skip_ascii_whitespace(header, name_start)
        if not lowered.startswith(b"svg", name_start):
            return False
        name_end = name_start + len(b"svg")
        if (
            name_end < len(header)
            and header[name_end] not in ASCII_WHITESPACE + b"[>"
        ):
            return False
        declaration_end = lowered.find(b">", name_end)
        if declaration_end < 0:
            return False
        index = skip_ascii_whitespace(header, declaration_end + 1)

    if not lowered.startswith(b"<svg", index):
        return False
    name_end = index + len(b"<svg")
    return name_end == len(header) or header[name_end] in ASCII_WHITESPACE + b">"


def sniff_encoded_format(header: bytes) -> str | None:
    if header.startswith(b"\xff\xd8\xff"):
        return "JPEG"
    if header.startswith(b"\x89PNG\r\n\x1a\n"):
        return "PNG"
    if header.startswith((b"II*\x00", b"MM\x00*", b"II+\x00", b"MM\x00+")):
        return "TIFF"
    if len(header) >= 12 and header[:4] == b"RIFF" and header[8:12] == b"WEBP":
        return "WEBP"
    if header.startswith(b"BM"):
        return "BMP"
    if header.startswith((b"GIF87a", b"GIF89a")):
        return "GIF"
    if header.startswith(b"\x00\x00\x00\x0cjP  \r\n\x87\n") or header.startswith(
        b"\xffO\xffQ"
    ):
        return "JPEG2000"
    if header.startswith((b"8BPS\x00\x01", b"8BPS\x00\x02")):
        return "PSD"
    if header.startswith(b"%PDF-"):
        return "PDF"
    if looks_like_svg(header):
        return "SVG"
    if header.startswith(b"\x00\x00\x01\x00"):
        return "ICO"
    if header.startswith(b"DDS "):
        return "DDS"
    if len(header) >= 4 and header[0] == 0x0A and header[2] == 1:
        return "PCX"
    if re.match(br"P[1-7](?:\s|#)", header):
        return "NETPBM"
    if header.startswith(b"\x01\xda"):
        return "SGI"
    if len(header) >= 12 and header[4:8] == b"ftyp":
        brands = {header[8:12], header[16:20]}
        if brands & {b"avif", b"avis"}:
            return "AVIF"
        if brands & {b"heic", b"heix", b"hevc", b"hevx", b"mif1", b"msf1"}:
            return "HEIF"
    return None


@contextmanager
def pillow_pixel_limit(max_pixels: int) -> Iterator[None]:
    previous = Image.MAX_IMAGE_PIXELS
    Image.MAX_IMAGE_PIXELS = max_pixels
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("error", Image.DecompressionBombWarning)
            yield
    finally:
        Image.MAX_IMAGE_PIXELS = previous


def pixel_budget_error(path: Path, pixels: int, max_pixels: int) -> ValueError:
    return ValueError(
        f"image exceeds --max-pixels-per-page "
        f"({pixels} > {max_pixels}): {path}"
    )


def bomb_pixel_count(error: Warning | Exception, max_pixels: int) -> int:
    match = re.search(r"Image size \((\d+) pixels\)", str(error))
    return int(match.group(1)) if match else max_pixels + 1


def immutable_stat(stat_result: os.stat_result) -> FileStat:
    return FileStat(
        int(stat_result.st_size),
        int(stat_result.st_mtime_ns),
        int(stat_result.st_ctime_ns),
        int(stat_result.st_dev),
        int(stat_result.st_ino),
    )


def same_file_identity(left: FileStat, right: FileStat) -> bool:
    return (
        left.size,
        left.mtime_ns,
        left.device,
        left.inode,
    ) == (
        right.size,
        right.mtime_ns,
        right.device,
        right.inode,
    )


def same_open_file_state(left: FileStat, right: FileStat) -> bool:
    if not same_file_identity(left, right):
        return False
    # Windows fstat() may report a transient handle change time in st_ctime_ns
    # that advances during an otherwise read-only open.
    return os.name == "nt" or left.ctime_ns == right.ctime_ns


def read_encoded_snapshot(
    path: Path,
    *,
    max_encoded_bytes: int,
    require_stable: bool = True,
) -> tuple[bytes, FileStat, str]:
    try:
        before = immutable_stat(path.stat())
        if before.size > max_encoded_bytes:
            raise ValueError(
                f"encoded file exceeds --max-encoded-bytes "
                f"({before.size} > {max_encoded_bytes}): {path}"
            )
        if require_stable:
            with path.open("rb") as stream:
                opened = immutable_stat(os.fstat(stream.fileno()))
                encoded = stream.read(max_encoded_bytes + 1)
                closed = immutable_stat(os.fstat(stream.fileno()))
            after = immutable_stat(path.stat())
            if (
                before != after
                or not same_open_file_state(opened, closed)
                or not same_file_identity(before, opened)
                or not same_file_identity(closed, after)
            ):
                raise ValueError(f"input file changed while being read: {path}")
        else:
            encoded = path.read_bytes()
            after = immutable_stat(path.stat())
        if len(encoded) > max_encoded_bytes:
            raise ValueError(
                f"encoded file exceeds --max-encoded-bytes "
                f"({len(encoded)} > {max_encoded_bytes}): {path}"
            )
    except ValueError:
        raise
    except OSError as error:
        raise ValueError(f"cannot read input file {path}: {error}") from error
    if require_stable and len(encoded) != before.size:
        raise ValueError(f"input file changed while being read: {path}")
    return encoded, before, hashlib.sha256(encoded).hexdigest()


def read_stable_header(
    path: Path,
    *,
    max_encoded_bytes: int,
    length: int = 32,
) -> tuple[bytes, FileStat]:
    try:
        before = immutable_stat(path.stat())
        if before.size > max_encoded_bytes:
            raise ValueError(
                f"encoded file exceeds --max-encoded-bytes "
                f"({before.size} > {max_encoded_bytes}): {path}"
            )
        with path.open("rb") as stream:
            opened = immutable_stat(os.fstat(stream.fileno()))
            header = stream.read(length)
            closed = immutable_stat(os.fstat(stream.fileno()))
        after = immutable_stat(path.stat())
        if (
            before != after
            or not same_open_file_state(opened, closed)
            or not same_file_identity(before, opened)
            or not same_file_identity(closed, after)
        ):
            raise ValueError(f"input file changed while being inspected: {path}")
        return header, before
    except ValueError:
        raise
    except OSError as error:
        raise ValueError(f"cannot inspect input file {path}: {error}") from error


def verify_snapshot(
    info: InputInfo,
    *,
    max_encoded_bytes: int,
) -> bytes:
    encoded, file_stat, file_hash = read_encoded_snapshot(
        info.path,
        max_encoded_bytes=max_encoded_bytes,
    )
    if (
        info.file_hash is None
        or file_stat != info.file_stat
        or len(encoded) != info.encoded_bytes
        or file_hash != info.file_hash
    ):
        raise ValueError(f"input file changed after inventory: {info.path}")
    return encoded


def png_bit_depth(encoded: bytes, path: Path) -> int:
    if len(encoded) < 25 or encoded[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"invalid PNG header: {path}")
    return encoded[24]


def png_has_trns(encoded: bytes, path: Path) -> bool:
    offset = 8
    while offset + 12 <= len(encoded):
        length = int.from_bytes(encoded[offset : offset + 4], "big")
        chunk_end = offset + 12 + length
        if chunk_end > len(encoded):
            raise ValueError(f"invalid PNG chunk structure: {path}")
        chunk_type = encoded[offset + 4 : offset + 8]
        if chunk_type == b"tRNS":
            return True
        if chunk_type == b"IEND":
            return False
        offset = chunk_end
    raise ValueError(f"invalid PNG chunk structure: {path}")


def tiff_bit_depth(source: Image.Image) -> int:
    bits = source.tag_v2.get(258, (1,))
    if isinstance(bits, int):
        return bits
    values = tuple(int(value) for value in bits)
    if not values or len(set(values)) != 1:
        return 0
    return values[0]


def tiff_tag_values(
    source: Image.Image, tag: int, default: int, path: Path
) -> tuple[int, ...]:
    value = source.tag_v2.get(tag, default)
    values = value if isinstance(value, (tuple, list)) else (value,)
    try:
        parsed = tuple(int(item) for item in values)
    except (TypeError, ValueError) as error:
        raise ValueError(f"invalid TIFF tag {tag}: {path}") from error
    if not parsed:
        raise ValueError(f"invalid TIFF tag {tag}: {path}")
    return parsed


def encoded_bit_depth(source: Image.Image, encoded: bytes, path: Path) -> int:
    if source.format == "PNG":
        return png_bit_depth(encoded, path)
    if source.format == "TIFF":
        return tiff_bit_depth(source)
    return 8


def inspect_image(
    source: Image.Image, encoded: bytes, path: Path
) -> tuple[str, str, bool, int]:
    encoded_format = source.format
    if encoded_format is None:
        raise ValueError(f"cannot determine encoded image format: {path}")
    encoded_format = encoded_format.upper()
    if encoded_format not in ENCODED_FORMATS:
        raise ValueError(f"unsupported encoded image format {encoded_format}: {path}")
    frame_count = int(getattr(source, "n_frames", 1))
    if frame_count != 1:
        raise ValueError(
            f"exactly one image frame is required (found {frame_count}): {path}"
        )
    orientation = int(source.getexif().get(EXIF_ORIENTATION, 1))
    if orientation not in range(1, 9):
        raise ValueError(f"invalid EXIF orientation {orientation}: {path}")
    mode = source.mode
    bit_depth = encoded_bit_depth(source, encoded, path)
    png_trns = encoded_format == "PNG" and png_has_trns(encoded, path)
    has_alpha = mode in {"LA", "RGBA"} or png_trns or "transparency" in source.info
    supported_modes = {
        "1", "L", "P", "LA", "RGB", "RGBA",
        "I", "I;16", "I;16L", "I;16B", "I;16N",
    }
    if encoded_format == "TIFF":
        sample_formats = tiff_tag_values(source, 339, 1, path)
        if not sample_formats or any(value != 1 for value in sample_formats):
            raise ValueError(
                "unsupported TIFF SampleFormat "
                f"{sample_formats}; only unsigned integer samples are supported: {path}"
            )
        photometric = tiff_tag_values(source, 262, 1, path)
        samples_per_pixel = tiff_tag_values(
            source, 277, len(source.getbands()), path
        )
        fill_order = tiff_tag_values(source, 266, 1, path)
        grayscale_matrix = (
            len(photometric) == 1
            and photometric[0] in (0, 1)
            and samples_per_pixel == (1,)
            and bit_depth in (1, 8, 16)
            and fill_order in {(1,), (2,)}
        )
        rgb_matrix = (
            photometric == (2,)
            and samples_per_pixel == (3,)
            and bit_depth == 8
            and fill_order == (1,)
        )
        extra_samples = source.tag_v2.get(338)
        if has_alpha or extra_samples:
            raise ValueError(
                "TIFF alpha is unsupported because its premultiplication "
                f"semantics cannot be determined reliably: {path}"
            )
        if mode in supported_modes and not grayscale_matrix and not rgb_matrix:
            raise ValueError(
                "unsupported TIFF sample matrix "
                f"(PhotometricInterpretation={photometric}, "
                f"SamplesPerPixel={samples_per_pixel}, BitsPerSample={bit_depth}, "
                f"FillOrder={fill_order}); supported matrices are unsigned "
                "WhiteIsZero/BlackIsZero grayscale with 1 sample at 1, 8, or 16 "
                "bits and FillOrder 1/2, and unsigned RGB with 3 samples at 8 "
                f"bits and FillOrder 1: {path}"
            )
    if mode not in supported_modes:
        raise ValueError(f"unsupported Pillow image mode {mode}: {path}")
    if has_alpha and encoded_format not in {"PNG", "WEBP"}:
        raise ValueError(f"alpha is supported only for PNG and WebP inputs: {path}")
    if png_trns and bit_depth == 16 and mode in {
        "I", "I;16", "I;16L", "I;16B", "I;16N",
    }:
        raise ValueError(
            f"16-bit grayscale PNG transparency (tRNS) is unsupported: {path}"
        )
    if bit_depth == 16 and mode not in {
        "I", "I;16", "I;16L", "I;16B", "I;16N",
    }:
        raise ValueError(
            f"unsupported 16-bit {mode} image; only 16-bit grayscale is supported: {path}"
        )
    if bit_depth not in {1, 2, 4, 8, 16}:
        raise ValueError(f"unsupported encoded bit depth {bit_depth}: {path}")
    return encoded_format, mode, has_alpha, bit_depth


def pillow_to_numpy(
    image: Image.Image,
    *,
    mode: str,
    has_alpha: bool,
    bit_depth: int,
    path: Path,
) -> np.ndarray:
    if has_alpha and mode in {"1", "L", "P", "RGB"}:
        image = image.convert("RGBA")
    elif mode == "1":
        image = image.convert("L")
    elif mode == "P":
        image = image.convert("RGB")
    array = np.array(image)
    if mode == "I" and bit_depth == 16:
        if array.min(initial=0) < 0 or array.max(initial=0) > 65535:
            raise ValueError(f"16-bit grayscale samples are out of range: {path}")
        array = array.astype(np.uint16)
    elif array.dtype.kind == "u" and array.dtype.itemsize == 2:
        array = array.astype(np.uint16, copy=False)
    if array.dtype not in (np.dtype(np.uint8), np.dtype(np.uint16)):
        raise ValueError(
            f"unsupported decoded precision {array.dtype}; expected 8-bit or 16-bit: {path}"
        )
    if array.ndim == 3 and array.shape[2] == 3:
        array = array[:, :, ::-1]
    elif array.ndim == 3 and array.shape[2] == 4:
        array = array[:, :, [2, 1, 0, 3]]
    elif array.ndim not in {2, 3} or (array.ndim == 3 and array.shape[2] != 2):
        raise ValueError(f"unsupported decoded image shape {array.shape}: {path}")
    return np.ascontiguousarray(array)


def orient_tiff_array(values: np.ndarray, orientation: int, path: Path) -> np.ndarray:
    operations = {
        1: lambda array: array,
        2: np.fliplr,
        3: lambda array: np.flipud(np.fliplr(array)),
        4: np.flipud,
        5: lambda array: array.T,
        6: lambda array: np.rot90(array, 3),
        7: lambda array: np.flipud(np.fliplr(array)).T,
        8: lambda array: np.rot90(array, 1),
    }
    if orientation not in operations:
        raise ValueError(f"invalid EXIF orientation {orientation}: {path}")
    return np.ascontiguousarray(operations[orientation](values))


def decode_tiff_grayscale(encoded: bytes, path: Path, max_pixels: int) -> np.ndarray:
    try:
        with tifffile.TiffFile(io.BytesIO(encoded)) as tiff:
            if len(tiff.pages) != 1:
                raise ValueError(
                    f"exactly one image frame is required (found {len(tiff.pages)}): "
                    f"{path}"
                )
            page = tiff.pages[0]
            shape = tuple(int(value) for value in page.shape)
            height, width = shape if len(shape) == 2 else (0, 0)
            pixels = width * height
            if pixels > max_pixels:
                raise pixel_budget_error(path, pixels, max_pixels)
            orientation_tag = page.tags.get(EXIF_ORIENTATION)
            orientation = int(orientation_tag.value) if orientation_tag else 1
            if (
                width <= 0
                or height <= 0
                or page.dtype.kind not in {"b", "u"}
                or int(page.bitspersample) not in (1, 8, 16)
                or int(page.samplesperpixel) != 1
                or int(page.sampleformat) != 1
                or int(page.photometric) not in (0, 1)
                or int(page.fillorder) not in (1, 2)
                or bool(page.extrasamples)
            ):
                raise ValueError(
                    "TIFF fallback requires unsigned single-channel "
                    "FillOrder=1/2 WhiteIsZero/BlackIsZero data at 1, 8, or "
                    f"16 bits: {path}"
                )
            values = page.asarray()
            bit_depth = int(page.bitspersample)
            photometric = int(page.photometric)
    except ValueError:
        raise
    except (tifffile.TiffFileError, OSError) as error:
        raise ValueError(f"cannot decode TIFF safely: {path}") from error

    if values.shape != (height, width):
        raise ValueError(f"TIFF decoder returned an unexpected raster: {path}")
    if bit_depth == 16:
        gray = np.asarray(values, dtype=np.dtype("=u2")).copy()
        maximum = np.iinfo(np.uint16).max
    else:
        gray = np.asarray(values, dtype=np.uint8).copy()
        maximum = (1 << bit_depth) - 1
        if bit_depth == 1:
            gray *= 255
            maximum = 255
    if photometric == 0:
        gray = maximum - gray
    return orient_tiff_array(gray, orientation, path)


def inspect_tiff_grayscale(
    encoded: bytes, path: Path, max_pixels: int
) -> tuple[int, int, int]:
    try:
        with tifffile.TiffFile(io.BytesIO(encoded)) as tiff:
            if len(tiff.pages) != 1:
                raise ValueError(
                    f"exactly one image frame is required (found {len(tiff.pages)}): "
                    f"{path}"
                )
            page = tiff.pages[0]
            shape = tuple(int(value) for value in page.shape)
            height, width = shape if len(shape) == 2 else (0, 0)
            pixels = width * height
            if pixels > max_pixels:
                raise pixel_budget_error(path, pixels, max_pixels)
            bit_depth = int(page.bitspersample)
            if (
                width <= 0
                or height <= 0
                or page.dtype.kind not in {"b", "u"}
                or bit_depth not in (1, 8, 16)
                or int(page.samplesperpixel) != 1
                or int(page.sampleformat) != 1
                or int(page.photometric) not in (0, 1)
                or int(page.fillorder) not in (1, 2)
                or bool(page.extrasamples)
            ):
                raise ValueError(
                    "TIFF fallback requires unsigned single-channel "
                    "FillOrder=1/2 WhiteIsZero/BlackIsZero data at 1, 8, or "
                    f"16 bits: {path}"
                )
            return width, height, bit_depth
    except ValueError:
        raise
    except (tifffile.TiffFileError, OSError) as error:
        raise ValueError(f"cannot inspect TIFF safely: {path}") from error


def read_image(
    path: Path,
    *,
    max_encoded_bytes: int = DEFAULT_MAX_ENCODED_BYTES,
    max_pixels: int = DEFAULT_MAX_PIXELS_PER_PAGE,
    expected: InputInfo | None = None,
) -> np.ndarray:
    try:
        encoded = (
            verify_snapshot(expected, max_encoded_bytes=max_encoded_bytes)
            if expected is not None
            else read_encoded_snapshot(
                path,
                max_encoded_bytes=max_encoded_bytes,
                require_stable=False,
            )[0]
        )
        sniffed_format = sniff_encoded_format(encoded[:32])
        if sniffed_format not in ENCODED_FORMATS:
            raise ValueError(
                f"unsupported encoded image format "
                f"{sniffed_format or 'unknown'}: {path}"
            )
        try:
            with pillow_pixel_limit(max_pixels):
                with Image.open(
                    io.BytesIO(encoded), formats=list(ENCODED_FORMATS)
                ) as source:
                    pixels = source.width * source.height
                    if pixels > max_pixels:
                        raise pixel_budget_error(path, pixels, max_pixels)
                    encoded_format, mode, has_alpha, bit_depth = inspect_image(
                        source, encoded, path
                    )
                    if encoded_format != sniffed_format:
                        raise ValueError(
                            f"encoded format header says {sniffed_format} but "
                            f"decoder identified {encoded_format}: {path}"
                        )
                    if expected is not None and (
                        encoded_format != expected.encoded_format
                        or source.width != expected.width
                        or source.height != expected.height
                        or source.width * source.height != expected.pixels
                        or bit_depth != expected.source_bit_depth
                    ):
                        raise ValueError(
                            f"input metadata changed after inventory: {path}"
                        )
                    if (
                        encoded_format == "TIFF"
                        and tiff_tag_values(source, 262, 1, path)[0] in (0, 1)
                        and tiff_tag_values(
                            source, 277, len(source.getbands()), path
                        ) == (1,)
                    ):
                        return decode_tiff_grayscale(encoded, path, max_pixels)
                    oriented = ImageOps.exif_transpose(source)
                    oriented.load()
                    return pillow_to_numpy(
                        oriented,
                        mode=mode,
                        has_alpha=has_alpha,
                        bit_depth=bit_depth,
                        path=path,
                    )
        except (Image.DecompressionBombError, Image.DecompressionBombWarning) as error:
            raise pixel_budget_error(
                path, bomb_pixel_count(error, max_pixels), max_pixels
            ) from error
    except ValueError:
        raise
    except (OSError, SyntaxError, UnidentifiedImageError) as error:
        if "encoded" in locals() and sniffed_format == "TIFF":
            try:
                return decode_tiff_grayscale(encoded, path, max_pixels)
            except ValueError:
                pass
        raise ValueError(
            f"exactly one image frame is required (found 0): {path}"
        ) from error


def inventory_inputs(
    input_dir: Path,
    *,
    max_encoded_bytes: int,
    max_pixels_per_page: int,
    max_inventory_entries: int = DEFAULT_MAX_INVENTORY_ENTRIES,
    max_inventory_bytes: int = DEFAULT_MAX_INVENTORY_BYTES,
) -> list[InputInfo]:
    inputs: list[InputInfo] = []
    paths: list[Path] = []
    inventory_entries = 0
    with os.scandir(input_dir) as entries:
        for entry in entries:
            try:
                is_file = entry.is_file(follow_symlinks=True)
            except OSError:
                is_file = False
            if not is_file:
                continue
            inventory_entries += 1
            if inventory_entries > max_inventory_entries:
                raise ValueError(
                    f"inventory entry count exceeds --max-inventory-entries "
                    f"({inventory_entries} > {max_inventory_entries})"
                )
            paths.append(input_dir / entry.name)
    paths.sort(key=natural_key)
    inventory_bytes = 0
    file_states: dict[Path, FileStat] = {}
    for path in paths:
        try:
            file_state = immutable_stat(path.stat())
        except OSError as error:
            raise ValueError(f"cannot inspect input file {path}: {error}") from error
        inventory_bytes += file_state.size
        if inventory_bytes > max_inventory_bytes:
            raise ValueError(
                f"inventory bytes exceed --max-inventory-bytes "
                f"({inventory_bytes} > {max_inventory_bytes})"
            )
        file_states[path] = file_state

    for path in paths:
        suffix = path.suffix.casefold()
        header, file_stat = read_stable_header(
            path,
            max_encoded_bytes=max_encoded_bytes,
            length=4096,
        )
        if file_stat != file_states[path]:
            raise ValueError(f"input file changed during inventory: {path}")
        sniffed_format = sniff_encoded_format(header)
        if sniffed_format not in ENCODED_FORMATS:
            if sniffed_format is not None:
                raise ValueError(
                    f"unsupported encoded image format {sniffed_format}: {path}"
                )
            if suffix in IMAGE_LIKE_EXTENSIONS:
                raise ValueError(
                    "image-like input is not a decodable image; supported "
                    f"formats are JPEG, PNG, TIFF, and WebP: {path}"
                )
            continue
        encoded, file_stat, file_hash = read_encoded_snapshot(
            path,
            max_encoded_bytes=max_encoded_bytes,
        )
        if file_stat != file_states[path]:
            raise ValueError(f"input file changed during inventory: {path}")
        encoded_bytes = len(encoded)
        try:
            with pillow_pixel_limit(max_pixels_per_page):
                with Image.open(
                    io.BytesIO(encoded), formats=list(ENCODED_FORMATS)
                ) as source:
                    encoded_format = (source.format or "").upper()
                    width, height = source.size
                    frame_count = int(getattr(source, "n_frames", 1))
                    source_bit_depth = encoded_bit_depth(source, encoded, path)
        except (Image.DecompressionBombError, Image.DecompressionBombWarning) as error:
            raise pixel_budget_error(
                path,
                bomb_pixel_count(error, max_pixels_per_page),
                max_pixels_per_page,
            ) from error
        except (OSError, SyntaxError, UnidentifiedImageError) as error:
            if sniffed_format != "TIFF":
                raise ValueError(
                    f"image-like input is not a decodable image: {path}"
                ) from error
            width, height, source_bit_depth = inspect_tiff_grayscale(
                encoded, path, max_pixels_per_page
            )
            encoded_format = "TIFF"
            frame_count = 1

        if encoded_format not in ENCODED_FORMATS:
            raise ValueError(
                f"unsupported encoded image format {encoded_format or 'unknown'}: {path}"
            )
        if encoded_format != sniffed_format:
            raise ValueError(
                f"encoded format header says {sniffed_format} but decoder "
                f"identified {encoded_format}: {path}"
            )
        if suffix not in EXTENSIONS:
            raise ValueError(
                f"valid {encoded_format} image has an unsupported filename suffix "
                f"{suffix or '<none>'}: {path}"
            )
        pixels = width * height
        if pixels > max_pixels_per_page:
            raise pixel_budget_error(path, pixels, max_pixels_per_page)
        if frame_count != 1:
            raise ValueError(
                f"exactly one image frame is required (found {frame_count}): {path}"
            )
        inputs.append(
            InputInfo(
                path,
                file_hash,
                file_stat,
                encoded_format,
                width,
                height,
                encoded_bytes,
                pixels,
                source_bit_depth,
            )
        )
    return inputs


def rehash_inputs(
    inventory: list[InputInfo],
    *,
    max_encoded_bytes: int,
) -> None:
    for info in inventory:
        verify_snapshot(info, max_encoded_bytes=max_encoded_bytes)


def write_png(path: Path, image: np.ndarray) -> None:
    ok, encoded = cv2.imencode(".png", image, [cv2.IMWRITE_PNG_COMPRESSION, 9])
    if not ok:
        raise ValueError(f"cannot encode image: {path}")
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="xb", prefix=f".{path.name}.", suffix=".tmp", dir=path.parent,
            delete=False,
        ) as temporary:
            temporary_path = Path(temporary.name)
            temporary.write(encoded.tobytes())
            temporary.flush()
            os.fsync(temporary.fileno())
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def composite_alpha_on_white(image: np.ndarray) -> np.ndarray:
    if image.ndim != 3 or image.shape[2] not in (2, 4):
        return image
    color = image[:, :, :-1]
    alpha = image[:, :, -1:]
    native_max = int(np.iinfo(image.dtype).max)
    work_dtype = np.uint32 if image.dtype == np.uint8 else np.uint64
    color_work = color.astype(work_dtype)
    alpha_work = alpha.astype(work_dtype)
    composited = (
        color_work * alpha_work
        + native_max * (native_max - alpha_work)
        + native_max // 2
    ) // native_max
    result = composited.astype(image.dtype)
    return result[:, :, 0] if result.shape[2] == 1 else result


def to_grayscale(image: np.ndarray) -> np.ndarray:
    image = composite_alpha_on_white(image)
    if image.ndim == 2:
        return image
    if image.ndim == 3 and image.shape[2] == 1:
        return image[:, :, 0]
    if image.ndim == 3 and image.shape[2] == 3:
        return cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    raise ValueError(f"unsupported image shape: {image.shape}")


def is_faint_mark_component(
    component_width: int,
    component_height: int,
    area: int,
    image_width: int,
    image_height: int,
) -> bool:
    minimum_long_dimension = max(
        6, int(round(min(image_height, image_width) * 0.008))
    )
    slender = (
        min(component_width, component_height) <= 6
        and max(component_width, component_height) >= minimum_long_dimension
    )
    compact = (
        area <= image_width * image_height * 0.0025
        and component_width <= max(8, int(round(image_width * 0.08)))
        and component_height <= max(8, int(round(image_height * 0.08)))
    )
    return slender or compact


def coherent_near_paper_components(
    gray: np.ndarray, paper: float
) -> np.ndarray:
    deficit = paper - gray
    candidate = (
        (deficit >= 0.5 / 255.0)
        & (deficit <= 3.0 / 255.0)
    )
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        candidate.astype(np.uint8), connectivity=8
    )
    protected = np.zeros_like(candidate)
    if count <= 1:
        return protected

    height, width = gray.shape
    minimum_area = max(8, int(round(gray.size * 0.00002)))
    kernel = np.ones((3, 3), np.uint8)
    for label in range(1, count):
        left, top, component_width, component_height, area = stats[label]
        if area < minimum_area:
            continue
        spans_full_width = left == 0 and component_width == width
        spans_full_height = top == 0 and component_height == height
        touches_left = left == 0
        touches_right = left + component_width == width
        touches_top = top == 0
        touches_bottom = top + component_height == height
        orthogonally_bounded_band = (
            spans_full_width
            and not touches_top
            and not touches_bottom
        ) or (
            spans_full_height
            and not touches_left
            and not touches_right
        )
        if (
            touches_left
            or touches_right
            or touches_top
            or touches_bottom
        ) and not orthogonally_bounded_band:
            continue

        fill_fraction = area / float(component_width * component_height)
        shaped = (
            is_faint_mark_component(
                component_width,
                component_height,
                area,
                width,
                height,
            )
            or (
                component_width >= 3
                and component_height >= 3
                and fill_fraction >= 0.35
            )
        )
        if not shaped:
            continue

        y0, y1 = max(0, top - 1), min(height, top + component_height + 1)
        x0, x1 = max(0, left - 1), min(width, left + component_width + 1)
        roi = np.s_[y0:y1, x0:x1]
        component = labels[roi] == label
        inside_boundary = component & (
            cv2.dilate((~component).astype(np.uint8), kernel) != 0
        )
        outside_boundary = (~component) & (
            cv2.dilate(component.astype(np.uint8), kernel) != 0
        )
        if not np.any(inside_boundary) or not np.any(outside_boundary):
            continue

        inside_level = float(np.median(gray[roi][inside_boundary]))
        outside_values = gray[roi][outside_boundary]
        boundary_contrast = float(np.median(outside_values) - inside_level)
        edge_consistency = float(
            np.mean(outside_values >= inside_level + 0.5 / 255.0)
        )
        if boundary_contrast >= 0.5 / 255.0 and edge_consistency >= 0.75:
            protected[roi] |= component
    return protected


def smooth_boundary_shading(
    gray: np.ndarray, candidate: np.ndarray
) -> np.ndarray:
    height, width = gray.shape
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        candidate.astype(np.uint8), connectivity=8
    )
    shading = np.zeros_like(candidate)
    if count <= 1:
        return shading

    smoothed = cv2.GaussianBlur(gray, (0, 0), 1.2)
    gradient_x = cv2.Sobel(smoothed, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gradient_y = cv2.Sobel(smoothed, cv2.CV_32F, 0, 1, ksize=3) / 8.0
    gradient = cv2.magnitude(gradient_x, gradient_y)
    high_frequency = np.abs(gray - cv2.GaussianBlur(gray, (0, 0), 3.0))
    kernel = np.ones((3, 3), np.uint8)
    minimum_area = max(32, int(round(gray.size * 0.003)))
    residual_sigma = max(2.5, min(8.0, min(height, width) / 80.0))
    local_background = cv2.GaussianBlur(gray, (0, 0), residual_sigma)
    residual = np.abs(gray - local_background)
    local_smoothed = cv2.GaussianBlur(gray, (0, 0), 0.8)
    local_gradient_x = cv2.Sobel(local_smoothed, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    local_gradient_y = cv2.Sobel(local_smoothed, cv2.CV_32F, 0, 1, ksize=3) / 8.0
    local_gradient = cv2.magnitude(local_gradient_x, local_gradient_y)
    content_barrier = (
        (residual >= 4.0 / 255.0)
        | (local_gradient >= 5.0 / 255.0)
    ) & candidate
    content_barrier = cv2.dilate(
        content_barrier.astype(np.uint8), kernel
    ).astype(bool) & candidate

    for label in range(1, count):
        left, top, component_width, component_height, area = stats[label]
        if area < minimum_area:
            continue
        touches_edge = (
            left == 0
            or top == 0
            or left + component_width == width
            or top + component_height == height
        )
        broad = (
            component_width >= max(12, int(round(width * 0.08)))
            or component_height >= max(12, int(round(height * 0.08)))
        )
        if not touches_edge or not broad:
            continue

        y0, y1 = max(0, top - 1), min(height, top + component_height + 1)
        x0, x1 = max(0, left - 1), min(width, left + component_width + 1)
        roi = np.s_[y0:y1, x0:x1]
        component = labels[roi] == label
        traversable = component & ~content_barrier[roi]
        traversable_count, traversable_labels, _, _ = cv2.connectedComponentsWithStats(
            traversable.astype(np.uint8), connectivity=8
        )
        edge_labels = np.unique(
            np.concatenate(
                (
                    traversable_labels[0, :],
                    traversable_labels[-1, :],
                    traversable_labels[:, 0],
                    traversable_labels[:, -1],
                )
            )
        )
        edge_labels = edge_labels[edge_labels != 0]
        if edge_labels.size == 0:
            continue
        edge_connected = np.isin(traversable_labels, edge_labels)
        boundary = component & (
            cv2.dilate((~component).astype(np.uint8), kernel) != 0
        )
        if not np.any(boundary) or not np.any(edge_connected):
            continue
        smooth_edge = (
            float(np.percentile(gradient[roi][boundary], 90.0)) <= 1.25 / 255.0
        )
        low_frequency = (
            float(np.percentile(high_frequency[roi][edge_connected], 95.0))
            <= 2.5 / 255.0
        )
        barrier_fraction = float(np.mean(content_barrier[roi][component]))
        strong_internal_contrast = barrier_fraction >= 0.12
        if smooth_edge and low_frequency and not strong_internal_contrast:
            shading[roi] |= edge_connected
    return shading


def _continuous_tone_protection_standardized(
    image: np.ndarray, gray: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    height, width = gray.shape
    tile = max(
        8,
        int(round(min(height, width) / 32.0)),
        int(np.ceil(max(height, width) / 256.0)),
    )
    rows = (height + tile - 1) // tile
    columns = (width + tile - 1) // tile
    smoothed = cv2.GaussianBlur(gray, (0, 0), 2.0)
    residual = np.abs(gray - smoothed)
    gradient_x = cv2.Sobel(smoothed, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gradient_y = cv2.Sobel(smoothed, cv2.CV_32F, 0, 1, ksize=3) / 8.0
    gradient = cv2.magnitude(gradient_x, gradient_y)

    chroma_residual: np.ndarray | None = None
    if image.ndim == 3 and image.shape[2] >= 3:
        native_max = float(np.iinfo(image.dtype).max)
        color = image[:, :, :3].astype(np.float32) / native_max
        chroma = color - np.mean(color, axis=2, keepdims=True)
        chroma_residual = np.max(
            np.abs(chroma - cv2.GaussianBlur(chroma, (0, 0), 2.0)),
            axis=2,
        )

    evidence = np.zeros((rows, columns), np.uint8)
    strong_evidence = np.zeros((rows, columns), np.uint8)
    smooth_axis_evidence = np.zeros((rows, columns), np.uint8)
    gentle_axis_evidence = np.zeros((rows, columns), np.uint8)
    smooth_axis_gradient = 0.20 / 255.0
    gentle_axis_gradient = 0.08 / 255.0
    for row in range(rows):
        y0, y1 = row * tile, min(height, (row + 1) * tile)
        for column in range(columns):
            x0, x1 = column * tile, min(width, (column + 1) * tile)
            roi = np.s_[y0:y1, x0:x1]
            tonal_range = float(
                np.percentile(gray[roi], 90.0) - np.percentile(gray[roi], 10.0)
            )
            mean_gradient_x = float(np.mean(np.abs(gradient_x[roi])))
            mean_gradient_y = float(np.mean(np.abs(gradient_y[roi])))
            two_dimensional_gradient = (
                min(mean_gradient_x, mean_gradient_y) >= smooth_axis_gradient
            )
            meaningful_axis_gradient = (
                max(mean_gradient_x, mean_gradient_y) >= smooth_axis_gradient
            )
            textured = (
                float(np.percentile(residual[roi], 90.0)) >= 1.5 / 255.0
                and tonal_range >= 5.0 / 255.0
                and (
                    float(np.mean(residual[roi] >= 1.5 / 255.0)) >= 0.45
                    or two_dimensional_gradient
                )
            )
            modeled = (
                tonal_range >= 1.5 / 255.0
                and float(np.max(residual[roi])) <= 2.0 / 255.0
                and float(np.percentile(gradient[roi], 75.0))
                >= smooth_axis_gradient
                and float(np.mean(gradient[roi] >= smooth_axis_gradient)) >= 0.45
                and meaningful_axis_gradient
            )
            gentle_modeled = (
                tonal_range >= 0.75 / 255.0
                and float(np.max(residual[roi])) <= 2.0 / 255.0
                and float(np.percentile(gradient[roi], 75.0))
                >= gentle_axis_gradient
                and float(np.mean(gradient[roi] >= gentle_axis_gradient)) >= 0.45
                and max(mean_gradient_x, mean_gradient_y)
                >= gentle_axis_gradient
            )
            chromatic = (
                chroma_residual is not None
                and float(np.percentile(chroma_residual[roi], 85.0))
                >= 1.25 / 255.0
                and float(
                    np.mean(chroma_residual[roi] >= 1.25 / 255.0)
                ) >= 0.45
            )
            if textured or gentle_modeled or chromatic:
                evidence[row, column] = 1
            if textured or modeled or chromatic:
                strong_evidence[row, column] = 1
            if modeled:
                smooth_axis_evidence[row, column] = 1
            if gentle_modeled:
                gentle_axis_evidence[row, column] = 1

    evidence = cv2.morphologyEx(
        evidence, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)
    )
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        evidence, connectivity=8
    )
    accepted = np.zeros(count, dtype=bool)
    minimum_tiles = max(4, int(round(rows * columns * 0.004)))
    for label in range(1, count):
        left, top, component_width, component_height, area = stats[label]
        y0, y1 = top * tile, min(height, (top + component_height) * tile)
        x0, x1 = left * tile, min(width, (left + component_width) * tile)
        roi = np.s_[y0:y1, x0:x1]
        tile_component = labels[
            top:top + component_height, left:left + component_width
        ] == label
        smooth_axis_fraction = float(
            np.mean(
                smooth_axis_evidence[
                    top:top + component_height, left:left + component_width
                ][tile_component]
            )
        )
        gentle_axis_fraction = float(
            np.mean(
                gentle_axis_evidence[
                    top:top + component_height, left:left + component_width
                ][tile_component]
            )
        )
        strong_fraction = float(
            np.mean(
                strong_evidence[
                    top:top + component_height, left:left + component_width
                ][tile_component]
            )
        )
        mean_gradient_x = float(np.mean(np.abs(gradient_x[roi])))
        mean_gradient_y = float(np.mean(np.abs(gradient_y[roi])))
        varied_in_two_dimensions = (
            min(mean_gradient_x, mean_gradient_y) >= smooth_axis_gradient
        )
        changing_gradient = (
            max(
                float(np.std(gradient_x[roi])),
                float(np.std(gradient_y[roi])),
            )
            >= 0.12 / 255.0
            and float(np.percentile(residual[roi], 90.0)) >= 0.55 / 255.0
        )
        spans_full_width = left == 0 and left + component_width == columns
        spans_full_height = top == 0 and top + component_height == rows
        localized_extent = not (spans_full_width and spans_full_height)
        orthogonally_bounded = (
            (spans_full_width and not spans_full_height)
            or (spans_full_height and not spans_full_width)
        )
        orthogonal_boundary_contrast = False
        if spans_full_width and not spans_full_height:
            row_change = np.median(np.abs(np.diff(gray, axis=0)), axis=1)
            top_changes = row_change[
                max(0, y0 - tile):min(height - 1, y0 + tile)
            ]
            bottom_changes = row_change[
                max(0, y1 - tile):min(height - 1, y1 + tile)
            ]
            orthogonal_boundary_contrast = (
                top_changes.size > 0
                and bottom_changes.size > 0
                and min(float(np.max(top_changes)), float(np.max(bottom_changes)))
                >= 2.0 / 255.0
            )
        elif spans_full_height and not spans_full_width:
            column_change = np.median(np.abs(np.diff(gray, axis=1)), axis=0)
            left_changes = column_change[
                max(0, x0 - tile):min(width - 1, x0 + tile)
            ]
            right_changes = column_change[
                max(0, x1 - tile):min(width - 1, x1 + tile)
            ]
            orthogonal_boundary_contrast = (
                left_changes.size > 0
                and right_changes.size > 0
                and min(float(np.max(left_changes)), float(np.max(right_changes)))
                >= 2.0 / 255.0
            )
        horizontal_variation_is_bounded = (
            mean_gradient_x >= smooth_axis_gradient
            and not spans_full_width
        )
        vertical_variation_is_bounded = (
            mean_gradient_y >= smooth_axis_gradient
            and not spans_full_height
        )
        chroma_structure = (
            chroma_residual is not None
            and float(np.mean(chroma_residual[roi] >= 1.25 / 255.0)) >= 0.15
        )
        if (
            area >= minimum_tiles
            and component_width >= 2
            and component_height >= 2
            and (
                (
                    varied_in_two_dimensions
                    and strong_fraction >= 0.50
                    and (localized_extent or changing_gradient)
                )
                or (
                    smooth_axis_fraction >= 0.50
                    and (
                        (
                            orthogonally_bounded
                            and orthogonal_boundary_contrast
                        )
                        or (
                            not orthogonally_bounded
                            and (
                                horizontal_variation_is_bounded
                                or vertical_variation_is_bounded
                            )
                        )
                    )
                )
                or (
                    gentle_axis_fraction >= 0.50
                    and orthogonally_bounded
                    and orthogonal_boundary_contrast
                )
                or chroma_structure
            )
        ):
            accepted[label] = True
    tile_mask = cv2.dilate(
        accepted[labels].astype(np.uint8), np.ones((3, 3), np.uint8)
    )
    enclosure = cv2.resize(
        tile_mask, (width, height), interpolation=cv2.INTER_NEAREST
    ).astype(bool)

    paper = float(np.percentile(gray, 85.0))
    full_resolution_structure = (
        (residual >= 1.0 / 255.0)
        | (gradient >= 0.12 / 255.0)
    )
    if chroma_residual is not None:
        full_resolution_structure |= chroma_residual >= 1.0 / 255.0
    paper_distance = np.abs(gray - paper)
    paper_passage = (
        (paper_distance <= 0.5 / 255.0)
        | (
            (paper_distance <= 2.5 / 255.0)
            & ~full_resolution_structure
        )
    )
    traversable = (~enclosure) | paper_passage
    count, passage_labels = cv2.connectedComponents(
        traversable.astype(np.uint8), connectivity=8
    )
    exterior_labels = np.unique(passage_labels[~enclosure])
    exterior_labels = exterior_labels[exterior_labels != 0]
    exterior = (
        np.isin(passage_labels, exterior_labels)
        if count > 1 and exterior_labels.size
        else np.zeros_like(enclosure)
    )
    exterior = cv2.morphologyEx(
        exterior.astype(np.uint8),
        cv2.MORPH_OPEN,
        np.ones((9, 9), np.uint8),
    ).astype(bool)
    protection = enclosure & ~exterior
    extension_kernel_dimension = bounded_morphology_kernel_dimension(
        2 * tile + 1
    )
    extension_limit = cv2.dilate(
        enclosure.astype(np.uint8),
        np.ones(
            (extension_kernel_dimension, extension_kernel_dimension), np.uint8
        ),
    ).astype(bool)
    extension = (
        full_resolution_structure
        & (paper_distance > 2.5 / 255.0)
        & extension_limit
    )
    extension_count, extension_labels = cv2.connectedComponents(
        (protection | extension).astype(np.uint8), connectivity=8
    )
    protected_labels = np.unique(extension_labels[protection])
    protected_labels = protected_labels[protected_labels != 0]
    if extension_count > 1 and protected_labels.size:
        protection = np.isin(extension_labels, protected_labels)
    protection = cv2.morphologyEx(
        protection.astype(np.uint8),
        cv2.MORPH_CLOSE,
        np.ones((5, 5), np.uint8),
    ).astype(bool)

    distance_inside = cv2.distanceTransform(
        protection.astype(np.uint8), cv2.DIST_L2, 3
    )
    blend = protection.astype(np.float32)
    component_count, component_labels, component_stats, _ = (
        cv2.connectedComponentsWithStats(
            protection.astype(np.uint8), connectivity=8
        )
    )
    minimum_feather_span = max(8, int(round(min(height, width) * 0.02)))
    for label in range(1, component_count):
        _, _, component_width, component_height, _ = component_stats[label]
        if min(component_width, component_height) < minimum_feather_span:
            continue
        boundary = (component_labels == label) & (distance_inside <= 1.0)
        blend[boundary] = 0.75
    return protection, blend.astype(np.float32)


def continuous_tone_protection(
    image: np.ndarray, gray: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    height, width = gray.shape
    scale = min(
        1.0, CONTINUOUS_TONE_ANALYSIS_LONG_EDGE / float(max(height, width))
    )
    analysis_height = max(1, int(round(height * scale)))
    analysis_width = max(1, int(round(width * scale)))
    if (analysis_height, analysis_width) == (height, width):
        return _continuous_tone_protection_standardized(image, gray)

    interpolation = cv2.INTER_AREA if scale < 1.0 else cv2.INTER_LINEAR
    analysis_gray = cv2.resize(
        gray, (analysis_width, analysis_height), interpolation=interpolation
    )
    analysis_image = cv2.resize(
        image, (analysis_width, analysis_height), interpolation=interpolation
    )
    protection, _ = _continuous_tone_protection_standardized(
        analysis_image, analysis_gray
    )
    upscaled_protection = cv2.resize(
        protection.astype(np.uint8),
        (width, height),
        interpolation=cv2.INTER_NEAREST,
    ).astype(bool)
    return _refine_upscaled_continuous_tone_boundary(
        image, gray, upscaled_protection
    )


def _refine_upscaled_continuous_tone_boundary(
    image: np.ndarray,
    gray: np.ndarray,
    enclosure: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    smoothed = cv2.GaussianBlur(gray, (0, 0), 1.2)
    residual = np.abs(gray - smoothed)
    gradient_x = cv2.Sobel(smoothed, cv2.CV_32F, 1, 0, ksize=3) / 8.0
    gradient_y = cv2.Sobel(smoothed, cv2.CV_32F, 0, 1, ksize=3) / 8.0
    gradient = cv2.magnitude(gradient_x, gradient_y)
    local_mean = cv2.GaussianBlur(gray, (0, 0), 2.0)
    local_variance = np.maximum(
        cv2.GaussianBlur(gray * gray, (0, 0), 2.0) - local_mean * local_mean,
        0.0,
    )

    chroma_structure = np.zeros(gray.shape, dtype=bool)
    if image.ndim == 3 and image.shape[2] >= 3:
        native_max = float(np.iinfo(image.dtype).max)
        color = image[:, :, :3].astype(np.float32) / native_max
        chroma = color - np.mean(color, axis=2, keepdims=True)
        chroma_residual = np.max(
            np.abs(chroma - cv2.GaussianBlur(chroma, (0, 0), 1.2)),
            axis=2,
        )
        chroma_structure = chroma_residual >= 0.8 / 255.0

    paper = float(np.percentile(gray[~enclosure], 85.0)) if np.any(~enclosure) else (
        float(np.percentile(gray, 85.0))
    )
    paper_distance = np.abs(gray - paper)
    smooth_paper = (
        (residual < 0.8 / 255.0)
        & (gradient < 0.10 / 255.0)
        & (local_variance < (0.9 / 255.0) ** 2)
        & ~chroma_structure
    )
    paper_passage = (paper_distance <= 3.0 / 255.0) | smooth_paper
    traversable = (~enclosure) | paper_passage
    count, labels = cv2.connectedComponents(
        traversable.astype(np.uint8), connectivity=8
    )
    exterior_labels = np.unique(labels[~enclosure])
    exterior_labels = exterior_labels[exterior_labels != 0]
    exterior = (
        np.isin(labels, exterior_labels)
        if count > 1 and exterior_labels.size
        else np.zeros_like(enclosure)
    )
    protection = enclosure & ~exterior
    requested_boundary_search = max(
        3,
        int(np.ceil(max(gray.shape) / CONTINUOUS_TONE_ANALYSIS_LONG_EDGE)) * 2,
    )
    boundary_search = bounded_morphology_iterations(
        gray.shape, requested_boundary_search
    )
    interior_guard = cv2.erode(
        enclosure.astype(np.uint8),
        np.ones((3, 3), np.uint8),
        iterations=boundary_search,
    ).astype(bool)
    protection |= interior_guard
    protection = cv2.morphologyEx(
        protection.astype(np.uint8),
        cv2.MORPH_CLOSE,
        np.ones((3, 3), np.uint8),
    ).astype(bool)
    protection = cv2.erode(
        protection.astype(np.uint8),
        np.ones((3, 3), np.uint8),
        iterations=2,
    ).astype(bool)

    distance_inside = cv2.distanceTransform(
        protection.astype(np.uint8), cv2.DIST_L2, 3
    )
    blend = np.clip(distance_inside / 2.0, 0.0, 1.0).astype(np.float32)
    blend[~protection] = 0.0
    return protection, blend


def foreground_protection(
    image: np.ndarray, gray: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, float, float]:
    height, width = gray.shape
    paper = float(np.percentile(gray, 85.0))
    raw_dark = gray < paper - 40.0 / 255.0
    dark = raw_dark & ~smooth_boundary_shading(gray, raw_dark)

    scale = min(1.0, 640.0 / max(height, width))
    small_size = (
        max(1, int(round(width * scale))),
        max(1, int(round(height * scale))),
    )
    small_dark = cv2.resize(
        dark.astype(np.float32), small_size, interpolation=cv2.INTER_AREA
    )
    small_dark = (small_dark >= 0.375).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        small_dark, connectivity=8
    )
    accepted_large = np.zeros(count, dtype=bool)
    minimum_area = max(24, int(round(small_dark.size * 0.0015)))
    for label in range(1, count):
        _, _, component_width, component_height, area = stats[label]
        broad = (
            component_width >= max(3, int(small_size[0] * 0.035))
            or component_height >= max(3, int(small_size[1] * 0.035))
        )
        if area >= minimum_area and broad:
            accepted_large[label] = True
    large_small = accepted_large[labels].astype(np.uint8)
    large_dark = cv2.resize(
        large_small, (width, height), interpolation=cv2.INTER_NEAREST
    ).astype(bool)

    structure_sigma = max(1.5, min(5.0, min(height, width) / 500.0))
    local_mean = cv2.GaussianBlur(gray, (0, 0), structure_sigma)
    local_square_mean = cv2.GaussianBlur(gray * gray, (0, 0), structure_sigma)
    local_std = np.sqrt(np.maximum(local_square_mean - local_mean * local_mean, 0.0))
    structured = (local_std > 7.0 / 255.0).astype(np.float32)
    density_sigma = max(3.0, min(10.0, min(height, width) / 160.0))
    structure_density = cv2.GaussianBlur(structured, (0, 0), density_sigma)
    textured = (structure_density > 0.16) & (gray < paper + 8.0 / 255.0)

    bright_reference = max(paper, float(np.percentile(gray, 98.0)))
    near_paper_marks = coherent_near_paper_components(gray, bright_reference)
    local_faint_background = cv2.GaussianBlur(
        gray, (0, 0), max(2.0, min(6.0, min(height, width) / 100.0))
    )
    faint_candidate = (
        (bright_reference - gray >= 3.0 / 255.0)
        & (bright_reference - gray <= 40.0 / 255.0)
        & (local_faint_background - gray >= 1.25 / 255.0)
    )
    faint_count, faint_labels, faint_stats, _ = cv2.connectedComponentsWithStats(
        faint_candidate.astype(np.uint8), connectivity=8
    )
    faint_marks = np.zeros_like(faint_candidate)
    faint_minimum_area = max(8, int(round(gray.size * 0.00001)))
    boundary_kernel = np.ones((3, 3), np.uint8)
    for label in range(1, faint_count):
        left, top, component_width, component_height, area = faint_stats[label]
        outline_like = (
            component_width >= 8
            and component_height >= 8
            and area <= component_width * component_height * 0.30
            and area <= gray.size * 0.05
        )
        if area < faint_minimum_area or not (
            is_faint_mark_component(
                component_width,
                component_height,
                area,
                width,
                height,
            )
            or outline_like
        ):
            continue
        y0, y1 = max(0, top - 1), min(height, top + component_height + 1)
        x0, x1 = max(0, left - 1), min(width, left + component_width + 1)
        roi = np.s_[y0:y1, x0:x1]
        component = faint_labels[roi] == label
        inside_boundary = component & (
            cv2.dilate((~component).astype(np.uint8), boundary_kernel) != 0
        )
        outside_boundary = (~component) & (
            cv2.dilate(component.astype(np.uint8), boundary_kernel) != 0
        )
        if not np.any(inside_boundary) or not np.any(outside_boundary):
            continue
        inside_level = float(np.median(gray[roi][inside_boundary]))
        outside_values = gray[roi][outside_boundary]
        if (
            float(np.median(outside_values) - inside_level) >= 2.0 / 255.0
            and float(np.mean(outside_values >= inside_level + 1.5 / 255.0))
            >= 0.65
        ):
            faint_marks[roi] |= component

    continuous_tone, continuous_tone_blend = continuous_tone_protection(image, gray)
    paper_like = np.abs(gray - paper) <= 20.0 / 255.0
    paper_fraction = float(np.mean(paper_like))
    if (
        float(np.mean(continuous_tone)) >= 0.55
        and is_high_coverage_document_page(gray, paper, paper_fraction)
    ):
        continuous_tone = np.zeros_like(continuous_tone)
        continuous_tone_blend = np.zeros_like(continuous_tone_blend)
    protected = (
        large_dark
        | textured
        | near_paper_marks
        | faint_marks
        | continuous_tone
    )
    return (
        dark,
        protected,
        continuous_tone,
        continuous_tone_blend,
        paper,
        paper_fraction,
    )


def has_reliable_paper_background(
    gray: np.ndarray, paper: float, paper_fraction: float
) -> bool:
    lower_5, upper_95 = np.percentile(gray, (5.0, 95.0))
    background_is_supported = (
        paper_fraction >= 0.24
        and (paper >= 165.0 / 255.0 or paper_fraction >= 0.30)
    )
    background_is_upper_tone = float(upper_95) - paper <= 24.0 / 255.0
    darker_content_separation = paper - float(lower_5)
    light_uniform_paper = paper >= 165.0 / 255.0
    return (
        background_is_supported
        and background_is_upper_tone
        and (darker_content_separation >= 4.0 / 255.0 or light_uniform_paper)
    )


def is_high_coverage_document_page(
    gray: np.ndarray, paper: float, paper_fraction: float
) -> bool:
    lower_5, median, upper_95 = np.percentile(gray, (5.0, 50.0, 95.0))
    return (
        paper_fraction >= 0.80
        and abs(float(median) - paper) <= 12.0 / 255.0
        and float(upper_95) - paper <= 14.0 / 255.0
        and paper - float(lower_5) >= 15.0 / 255.0
    )


def has_majority_dark_content(
    gray: np.ndarray, dark: np.ndarray, protected: np.ndarray
) -> bool:
    dark_fraction = float(np.mean(dark))
    protected_fraction = float(np.mean(protected))
    lower_5, upper_95 = np.percentile(gray, (5.0, 95.0))
    return (
        dark_fraction > 0.50
        and protected_fraction > 0.50
        and float(upper_95 - lower_5) >= MIN_DARK_CONTENT_CONTRAST
    )


def smoothstep(values: np.ndarray) -> np.ndarray:
    clipped = np.clip(values, 0.0, 1.0)
    return clipped * clipped * (3.0 - 2.0 * clipped)


def has_ambiguous_near_paper_edge_component(
    gray: np.ndarray,
    bright_reference: float,
    minimum_region_fraction: float = 0.0,
) -> bool:
    deficit = bright_reference - gray
    candidate = (deficit >= 0.5 / 255.0) & (deficit < 3.0 / 255.0)
    smooth_shading = smooth_boundary_shading(gray, candidate)
    coherent_content = coherent_near_paper_components(gray, bright_reference)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        candidate.astype(np.uint8), connectivity=8
    )
    height, width = gray.shape
    minimum_area = max(
        8,
        int(round(gray.size * max(0.00001, minimum_region_fraction))),
    )
    kernel = np.ones((3, 3), np.uint8)
    for label in range(1, count):
        left, top, component_width, component_height, area = stats[label]
        touches_edge = (
            left == 0
            or top == 0
            or left + component_width == width
            or top + component_height == height
        )
        if area < minimum_area or not touches_edge:
            continue

        y0, y1 = max(0, top - 1), min(height, top + component_height + 1)
        x0, x1 = max(0, left - 1), min(width, left + component_width + 1)
        roi = np.s_[y0:y1, x0:x1]
        component = labels[roi] == label
        if np.all(coherent_content[roi][component]):
            continue
        inside_boundary = component & (
            cv2.dilate((~component).astype(np.uint8), kernel) != 0
        )
        outside_boundary = (~component) & (
            cv2.dilate(component.astype(np.uint8), kernel) != 0
        )
        if not np.any(inside_boundary) or not np.any(outside_boundary):
            continue

        inside_level = float(np.median(gray[roi][inside_boundary]))
        outside_values = gray[roi][outside_boundary]
        boundary_contrast = float(np.median(outside_values) - inside_level)
        edge_consistency = float(
            np.mean(outside_values >= inside_level + 1.5 / 255.0)
        )
        if (
            np.all(smooth_shading[roi][component])
            and boundary_contrast < 1.5 / 255.0
        ):
            continue
        if boundary_contrast >= 1.5 / 255.0 and edge_consistency >= 0.65:
            return True
    return False


def has_unsafe_low_contrast_separation(
    gray: np.ndarray,
    global_paper: float,
    minimum_region_fraction: float = 0.0,
) -> bool:
    bright_reference = max(global_paper, float(np.percentile(gray, 98.0)))
    if has_ambiguous_near_paper_edge_component(
        gray,
        bright_reference,
        minimum_region_fraction,
    ):
        return True
    deficit = bright_reference - gray
    candidate = (deficit >= 3.0 / 255.0) & (deficit <= 40.0 / 255.0)
    smooth_shading = smooth_boundary_shading(gray, candidate)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        candidate.astype(np.uint8), connectivity=8
    )
    if count <= 1:
        return False

    height, width = gray.shape
    minimum_area = max(
        8,
        int(round(gray.size * max(0.00001, minimum_region_fraction))),
    )
    kernel = np.ones((3, 3), np.uint8)
    local_mean = cv2.GaussianBlur(gray, (0, 0), 1.5)
    structure = np.abs(gray - local_mean) >= 1.5 / 255.0
    for label in range(1, count):
        left, top, component_width, component_height, area = stats[label]
        if area < minimum_area:
            continue
        touches_edge = (
            left == 0
            or top == 0
            or left + component_width == width
            or top + component_height == height
        )
        if not touches_edge and is_faint_mark_component(
            component_width,
            component_height,
            area,
            width,
            height,
        ):
            continue
        has_real_shape = (
            component_width >= 3 and component_height >= 3
        )
        if not has_real_shape:
            continue

        y0, y1 = max(0, top - 1), min(height, top + component_height + 1)
        x0, x1 = max(0, left - 1), min(width, left + component_width + 1)
        roi = np.s_[y0:y1, x0:x1]
        component = labels[roi] == label
        inside_boundary = component & (
            cv2.dilate((~component).astype(np.uint8), kernel) != 0
        )
        outside_boundary = (~component) & (
            cv2.dilate(component.astype(np.uint8), kernel) != 0
        )
        if not np.any(inside_boundary) or not np.any(outside_boundary):
            continue

        inside_level = float(np.median(gray[roi][inside_boundary]))
        outside_values = gray[roi][outside_boundary]
        boundary_contrast = float(np.median(outside_values) - inside_level)
        edge_consistency = float(
            np.mean(outside_values >= inside_level + 1.5 / 255.0)
        )
        if np.all(smooth_shading[roi][component]):
            continue

        interior = component & (
            cv2.erode(component.astype(np.uint8), kernel) != 0
        )
        structure_fraction = (
            float(np.mean(structure[roi][interior])) if np.any(interior) else 0.0
        )
        real_boundary = (
            boundary_contrast >= 2.0 / 255.0 and edge_consistency >= 0.65
        )
        coherent_structure = (
            structure_fraction >= 0.01
            and boundary_contrast >= 1.0 / 255.0
            and edge_consistency >= 0.55
        )
        if real_boundary or coherent_structure:
            return True
    return False


def background_blur_plan(
    shape: tuple[int, int], sigma: float
) -> BackgroundBlurPlan:
    height, width = shape
    if height < 1 or width < 1 or not np.isfinite(sigma) or sigma <= 0.0:
        raise UnsupportedBackgroundSigma("invalid background blur geometry or sigma")

    shortest_axis = max(1, min(height, width))
    extreme_aspect = max(height, width) > shortest_axis * EXTREME_ASPECT_RATIO
    kernel_limit = (
        MAX_EXTREME_ASPECT_KERNEL_DIMENSION
        if extreme_aspect
        else MAX_BACKGROUND_KERNEL_DIMENSION
    )
    downsample = max(1, int(np.ceil((6.0 * sigma + 1.0) / kernel_limit)))

    for _ in range(4):
        target_width = max(1, int(np.ceil(width / downsample)))
        target_height = max(1, int(np.ceil(height / downsample)))
        scale_x = width / target_width
        scale_y = height / target_height
        sigma_x = sigma / scale_x
        sigma_y = sigma / scale_y

        def kernel_dimension(
            source_axis: int, axis: int, scaled_sigma: float
        ) -> int:
            if axis == 1:
                return 1 if source_axis == 1 else kernel_limit + 2
            return 2 * int(np.ceil(3.0 * scaled_sigma)) + 1

        kernel = (
            kernel_dimension(width, target_width, sigma_x),
            kernel_dimension(height, target_height, sigma_y),
        )
        if max(kernel) <= kernel_limit:
            return BackgroundBlurPlan(
                (target_width, target_height), kernel, sigma_x, sigma_y
            )
        downsample += 1

    raise UnsupportedBackgroundSigma(
        f"sigma {sigma:g} cannot be represented within the background blur limit"
    )


def bounded_background_kernel(
    shape: tuple[int, int], sigma: float
) -> tuple[int, int]:
    return background_blur_plan(shape, sigma).kernel


def bounded_morphology_iterations(
    shape: tuple[int, int], requested: int
) -> int:
    shortest_axis = min(shape)
    geometric_limit = max(0, (shortest_axis - 1) // 2)
    return min(max(0, requested), MAX_MORPHOLOGY_ITERATIONS, geometric_limit)


def bounded_morphology_kernel_dimension(requested: int) -> int:
    limit = min(max(1, requested), MAX_MORPHOLOGY_KERNEL_DIMENSION)
    return limit if limit % 2 == 1 else max(1, limit - 1)


def background_blur(image: np.ndarray, sigma: float) -> np.ndarray:
    plan = background_blur_plan(image.shape[:2], sigma)
    original_size = (image.shape[1], image.shape[0])
    working = image
    if plan.size != original_size:
        working = cv2.resize(image, plan.size, interpolation=cv2.INTER_AREA)
    blurred = cv2.GaussianBlur(
        working,
        plan.kernel,
        sigmaX=plan.sigma_x,
        sigmaY=plan.sigma_y,
    )
    if plan.size != original_size:
        blurred = cv2.resize(
            blurred, original_size, interpolation=cv2.INTER_LINEAR
        )
    return blurred


def estimated_page_resources(
    info: InputInfo,
    *,
    background_scale: float,
    min_background_sigma: float,
) -> tuple[int, int]:
    pixels = info.pixels
    # Peak analysis holds several full-resolution float32 planes, masks,
    # connected-component labels, and (for color) a float32 color working set.
    working_bytes = info.encoded_bytes + pixels * 192
    sigma = max(min_background_sigma, min(info.width, info.height) / background_scale)
    try:
        kernel_width, kernel_height = bounded_background_kernel(
            (info.height, info.width), sigma
        )
    except UnsupportedBackgroundSigma:
        kernel_width = kernel_height = MAX_BACKGROUND_KERNEL_DIMENSION
    blur_taps = kernel_width + kernel_height
    work_units = pixels * (320 + 8 * blur_taps)
    return working_bytes, work_units


def restore_with_status(
    image: np.ndarray,
    **options: float | int,
) -> tuple[np.ndarray, dict[str, object]]:
    composited = composite_alpha_on_white(image)
    gray = to_grayscale(composited)
    output_dtype = gray.dtype
    native_max = float(np.iinfo(output_dtype).max)
    gray_float = gray.astype(np.float32) / native_max
    (
        dark,
        protected,
        continuous_tone,
        continuous_tone_blend,
        global_paper,
        paper_fraction,
    ) = (
        foreground_protection(composited, gray_float)
    )
    protected_fraction = float(np.mean(protected))
    continuous_tone_fraction = float(np.mean(continuous_tone))
    tonal_spread = float(np.percentile(gray_float, 95) - np.percentile(gray_float, 5))
    unsuitable_reason: str | None = None
    if paper_fraction < 0.24 and tonal_spread > 0.28:
        unsuitable_reason = "continuous_tone_page_has_no_reliable_paper_background"
    elif not has_reliable_paper_background(
        gray_float, global_paper, paper_fraction
    ):
        unsuitable_reason = "page_has_no_sufficiently_bright_paper_background"
    elif continuous_tone_fraction >= 0.55:
        unsuitable_reason = "bright_continuous_tone_content_requires_preservation"
    elif has_majority_dark_content(gray_float, dark, protected):
        unsuitable_reason = "foreground_or_illustration_covers_most_of_page"
    elif has_unsafe_low_contrast_separation(
        gray_float,
        global_paper,
        0.01 if is_high_coverage_document_page(
            gray_float, global_paper, paper_fraction
        ) else 0.0,
    ):
        unsuitable_reason = "unsafe_foreground_background_separation_requires_review"
    if unsuitable_reason is not None:
        return gray.copy(), {
            "status": "copied_unchanged",
            "reason": unsuitable_reason,
            "protected_fraction": round(protected_fraction, 6),
        }

    sigma = max(
        float(options["min_background_sigma"]),
        min(gray.shape) / float(options["background_scale"]),
    )
    try:
        for requested_sigma in (
            sigma,
            max(1.5, sigma * 0.08),
            max(0.8, sigma * 0.025),
        ):
            background_blur_plan(gray.shape, requested_sigma)
    except UnsupportedBackgroundSigma:
        return gray.copy(), {
            "status": "copied_unchanged",
            "reason": "requested_background_sigma_cannot_be_honored",
            "protected_fraction": round(protected_fraction, 6),
        }

    valid = (~(dark | protected)).astype(np.float32)
    weighted_sum = background_blur(gray_float * valid, sigma)
    weight = background_blur(valid, sigma)
    estimated = np.divide(
        weighted_sum,
        np.maximum(weight, 1.0e-4),
        out=np.full_like(weighted_sum, global_paper),
    )
    estimated[weight < 0.03] = global_paper
    protection_blend = background_blur(
        protected.astype(np.float32), max(1.5, sigma * 0.08)
    )
    background = estimated * (1.0 - protection_blend) + global_paper * protection_blend

    normalized = gray_float / np.maximum(background, 1.0 / native_max)
    normalized *= float(options["paper_level"]) / 255.0
    black = min(
        float(options["black_ceiling"]) / 255.0,
        max(
            float(options["black_floor"]) / 255.0,
            float(np.percentile(normalized, float(options["black_percentile"]))),
        ),
    )
    white = max(
        float(options["white_floor"]) / 255.0,
        float(np.percentile(normalized, float(options["white_percentile"]))),
    )
    min_range = float(options["min_tone_range"]) / 255.0
    if white <= black + min_range:
        white = black + min_range
    leveled = np.clip((normalized - black) / (white - black), 0.0, 1.0)
    x = np.clip(
        (leveled - float(options["whiten_start"]) / 255.0)
        / (float(options["whiten_width"]) / 255.0),
        0.0,
        1.0,
    )
    whitening = smoothstep(x)

    local_deficit = np.maximum(background - gray_float, 0.0)
    ink_confidence = smoothstep(
        (local_deficit - FAINT_INK_START)
        / (FAINT_INK_CONFIDENT - FAINT_INK_START)
    )
    exclusion_blend = background_blur(
        (dark | protected).astype(np.float32),
        max(0.8, sigma * 0.025),
    )
    foreground_confidence = np.maximum.reduce(
        (ink_confidence, exclusion_blend, continuous_tone_blend)
    )
    background_confidence = 1.0 - np.clip(foreground_confidence, 0.0, 1.0)
    cleaned = leveled + (1.0 - leveled) * whitening * background_confidence

    paper_normalized = float(options["paper_level"]) / 255.0
    paper_leveled = float(np.clip((paper_normalized - black) / (white - black), 0.0, 1.0))
    paper_x = float(np.clip(
        (paper_leveled - float(options["whiten_start"]) / 255.0)
        / (float(options["whiten_width"]) / 255.0),
        0.0,
        1.0,
    ))
    paper_cleaned = paper_leveled + (1.0 - paper_leveled) * (
        paper_x * paper_x * (3.0 - 2.0 * paper_x)
    )
    contrast_floor = np.maximum(
        1.0 / native_max,
        MIN_FAINT_INK_CONTRAST * foreground_confidence,
    )
    ink_pixels = (
        (local_deficit >= 0.5 / native_max)
        | dark
        | protected
    )
    cleaned[ink_pixels] = np.minimum(
        cleaned[ink_pixels],
        paper_cleaned - contrast_floor[ink_pixels],
    )
    cleaned = (
        cleaned * (1.0 - continuous_tone_blend)
        + gray_float * continuous_tone_blend
    )
    confidently_background = (background_confidence >= 0.995) & ~ink_pixels
    cleaned[
        confidently_background
        & (cleaned >= int(options["white_clip"]) / 255.0)
    ] = 1.0
    cleaned = np.clip(cleaned, 0.0, 1.0)
    status = "foreground_protected" if protected_fraction >= 0.001 else "normalized"
    reason = (
        "large_or_textured_foreground_excluded_from_background_estimation"
        if status == "foreground_protected"
        else "paper_background_normalized"
    )
    return np.rint(cleaned * native_max).astype(output_dtype), {
        "status": status,
        "reason": reason,
        "protected_fraction": round(protected_fraction, 6),
    }


def restore(
    image: np.ndarray,
    *,
    background_scale: float,
    min_background_sigma: float,
    paper_level: float,
    black_percentile: float,
    black_floor: float,
    black_ceiling: float,
    white_percentile: float,
    white_floor: float,
    min_tone_range: float,
    whiten_start: float,
    whiten_width: float,
    white_clip: int,
) -> np.ndarray:
    restored, _ = restore_with_status(
        image,
        background_scale=background_scale,
        min_background_sigma=min_background_sigma,
        paper_level=paper_level,
        black_percentile=black_percentile,
        black_floor=black_floor,
        black_ceiling=black_ceiling,
        white_percentile=white_percentile,
        white_floor=white_floor,
        min_tone_range=min_tone_range,
        whiten_start=whiten_start,
        whiten_width=whiten_width,
        white_clip=white_clip,
    )
    return restored


def page_number(path: Path) -> int | None:
    digits = "".join(
        character for character in path.stem.rsplit("_", 1)[-1] if character.isdigit()
    )
    return int(digits) if digits else None


def natural_key(
    path: Path,
) -> tuple[tuple[tuple[int, int | str], ...], str]:
    return (
        tuple(
            (0, int(part)) if part.isdigit() else (1, part.casefold())
            for part in re.split(r"(\d+)", path.name)
            if part
        ),
        path.name,
    )


def validate_range(
    parser: argparse.ArgumentParser,
    name: str,
    value: float,
    minimum: float,
    maximum: float,
) -> None:
    if not minimum <= value <= maximum:
        parser.error(f"{name} must be between {minimum:g} and {maximum:g}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Restore paper tone in scanned pages.")
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument(
        "--pages",
        nargs="+",
        type=int,
        metavar="N",
        help="process page numbers parsed from the final underscore-delimited filename segment",
    )
    parser.add_argument(
        "--background-scale",
        type=float,
        default=DEFAULT_BACKGROUND_SCALE,
        help=f"local-background blur divisor, 1..1000 (default: {DEFAULT_BACKGROUND_SCALE:g})",
    )
    parser.add_argument(
        "--min-background-sigma",
        type=float,
        default=DEFAULT_MIN_BACKGROUND_SIGMA,
        help=f"minimum local-background blur sigma, 0.1..1000 (default: {DEFAULT_MIN_BACKGROUND_SIGMA:g})",
    )
    parser.add_argument(
        "--paper-level",
        type=float,
        default=DEFAULT_PAPER_LEVEL,
        help=f"normalization target for paper tone, 1..255 (default: {DEFAULT_PAPER_LEVEL:g})",
    )
    parser.add_argument(
        "--black-percentile",
        type=float,
        default=DEFAULT_BLACK_PERCENTILE,
        help=f"percentile used for the dark endpoint, 0..50 (default: {DEFAULT_BLACK_PERCENTILE:g})",
    )
    parser.add_argument(
        "--black-floor",
        type=float,
        default=DEFAULT_BLACK_FLOOR,
        help=f"minimum dark endpoint, 0..254 (default: {DEFAULT_BLACK_FLOOR:g})",
    )
    parser.add_argument(
        "--black-ceiling",
        type=float,
        default=DEFAULT_BLACK_CEILING,
        help=f"maximum dark endpoint, 0..254 (default: {DEFAULT_BLACK_CEILING:g})",
    )
    parser.add_argument(
        "--white-percentile",
        type=float,
        default=DEFAULT_WHITE_PERCENTILE,
        help=f"percentile used for the light endpoint, 50..100 (default: {DEFAULT_WHITE_PERCENTILE:g})",
    )
    parser.add_argument(
        "--white-floor",
        type=float,
        default=DEFAULT_WHITE_FLOOR,
        help=f"minimum light endpoint, 1..255 (default: {DEFAULT_WHITE_FLOOR:g})",
    )
    parser.add_argument(
        "--min-tone-range",
        type=float,
        default=DEFAULT_MIN_TONE_RANGE,
        help=f"minimum distance between endpoints, 1..255 (default: {DEFAULT_MIN_TONE_RANGE:g})",
    )
    parser.add_argument(
        "--whiten-start",
        type=float,
        default=DEFAULT_WHITEN_START,
        help=f"gray level where smooth paper whitening begins, 0..254 (default: {DEFAULT_WHITEN_START:g})",
    )
    parser.add_argument(
        "--whiten-width",
        type=float,
        default=DEFAULT_WHITEN_WIDTH,
        help=f"width of the smooth whitening transition, 1..255 (default: {DEFAULT_WHITEN_WIDTH:g})",
    )
    parser.add_argument(
        "--white-clip",
        type=int,
        default=DEFAULT_WHITE_CLIP,
        help=f"final near-white clipping threshold, 1..255 (default: {DEFAULT_WHITE_CLIP})",
    )
    parser.add_argument(
        "--max-encoded-bytes",
        type=int,
        default=DEFAULT_MAX_ENCODED_BYTES,
        help=f"maximum encoded bytes per input, before reading (default: {DEFAULT_MAX_ENCODED_BYTES})",
    )
    parser.add_argument(
        "--max-pixels-per-page",
        type=int,
        default=DEFAULT_MAX_PIXELS_PER_PAGE,
        help=f"maximum decoded pixels per page (default: {DEFAULT_MAX_PIXELS_PER_PAGE})",
    )
    parser.add_argument(
        "--max-page-count",
        type=int,
        default=DEFAULT_MAX_PAGE_COUNT,
        help=f"maximum selected page count (default: {DEFAULT_MAX_PAGE_COUNT})",
    )
    parser.add_argument(
        "--max-total-pixels",
        type=int,
        default=DEFAULT_MAX_TOTAL_PIXELS,
        help=f"maximum selected total pixels (default: {DEFAULT_MAX_TOTAL_PIXELS})",
    )
    parser.add_argument(
        "--max-inventory-entries",
        type=int,
        default=DEFAULT_MAX_INVENTORY_ENTRIES,
        help=f"maximum top-level ordinary files inventoried (default: {DEFAULT_MAX_INVENTORY_ENTRIES})",
    )
    parser.add_argument(
        "--max-inventory-bytes",
        type=int,
        default=DEFAULT_MAX_INVENTORY_BYTES,
        help=f"maximum aggregate bytes across inventoried files (default: {DEFAULT_MAX_INVENTORY_BYTES})",
    )
    parser.add_argument(
        "--max-working-bytes-per-page",
        type=int,
        default=DEFAULT_MAX_WORKING_BYTES_PER_PAGE,
        help=f"maximum estimated peak working memory per page (default: {DEFAULT_MAX_WORKING_BYTES_PER_PAGE})",
    )
    parser.add_argument(
        "--max-work-units-per-page",
        type=int,
        default=DEFAULT_MAX_WORK_UNITS_PER_PAGE,
        help=f"maximum estimated full-resolution processing work per page (default: {DEFAULT_MAX_WORK_UNITS_PER_PAGE})",
    )
    args = parser.parse_args()
    if not args.input.is_dir():
        parser.error(f"input is not a directory: {args.input}")
    if os.path.lexists(args.output):
        parser.error(f"output path must not exist: {args.output}")
    input_dir = args.input.resolve()
    output_dir = args.output.resolve()
    if (
        input_dir == output_dir
        or input_dir in output_dir.parents
        or output_dir in input_dir.parents
    ):
        parser.error("input and output must be separate, non-nested directories")
    validate_range(parser, "--background-scale", args.background_scale, 1.0, 1000.0)
    validate_range(
        parser, "--min-background-sigma", args.min_background_sigma, 0.1, 1000.0
    )
    validate_range(parser, "--paper-level", args.paper_level, 1.0, 255.0)
    validate_range(parser, "--black-percentile", args.black_percentile, 0.0, 50.0)
    validate_range(parser, "--black-floor", args.black_floor, 0.0, 254.0)
    validate_range(parser, "--black-ceiling", args.black_ceiling, 0.0, 254.0)
    validate_range(parser, "--white-percentile", args.white_percentile, 50.0, 100.0)
    validate_range(parser, "--white-floor", args.white_floor, 1.0, 255.0)
    validate_range(parser, "--min-tone-range", args.min_tone_range, 1.0, 255.0)
    validate_range(parser, "--whiten-start", args.whiten_start, 0.0, 254.0)
    validate_range(parser, "--whiten-width", args.whiten_width, 1.0, 255.0)
    validate_range(parser, "--white-clip", args.white_clip, 1.0, 255.0)
    for name in (
        "max_encoded_bytes",
        "max_pixels_per_page",
        "max_page_count",
        "max_total_pixels",
        "max_inventory_entries",
        "max_inventory_bytes",
        "max_working_bytes_per_page",
        "max_work_units_per_page",
    ):
        if getattr(args, name) <= 0:
            parser.error(f"--{name.replace('_', '-')} must be positive")
    for name, hard_limit in (
        ("max_encoded_bytes", DEFAULT_MAX_ENCODED_BYTES),
        ("max_pixels_per_page", DEFAULT_MAX_PIXELS_PER_PAGE),
        ("max_page_count", DEFAULT_MAX_PAGE_COUNT),
        ("max_total_pixels", DEFAULT_MAX_TOTAL_PIXELS),
        ("max_inventory_entries", DEFAULT_MAX_INVENTORY_ENTRIES),
        ("max_inventory_bytes", DEFAULT_MAX_INVENTORY_BYTES),
        ("max_working_bytes_per_page", DEFAULT_MAX_WORKING_BYTES_PER_PAGE),
        ("max_work_units_per_page", DEFAULT_MAX_WORK_UNITS_PER_PAGE),
    ):
        if getattr(args, name) > hard_limit:
            parser.error(
                f"--{name.replace('_', '-')} must not exceed {hard_limit}"
            )
    if args.black_percentile >= args.white_percentile:
        parser.error("--black-percentile must be less than --white-percentile")
    if args.black_floor > args.black_ceiling:
        parser.error("--black-floor must not exceed --black-ceiling")
    if args.black_ceiling >= args.white_floor:
        parser.error("--black-ceiling must be less than --white-floor")
    if args.black_ceiling + args.min_tone_range > 255:
        parser.error("--black-ceiling + --min-tone-range must not exceed 255")

    try:
        inventory = inventory_inputs(
            input_dir,
            max_encoded_bytes=args.max_encoded_bytes,
            max_pixels_per_page=args.max_pixels_per_page,
            max_inventory_entries=args.max_inventory_entries,
            max_inventory_bytes=args.max_inventory_bytes,
        )
    except (OSError, ValueError) as error:
        parser.error(str(error))
    all_files = [item.path for item in inventory]
    info_by_path = {item.path: item for item in inventory}
    all_output_names = [path.stem.casefold() for path in all_files]
    if len(all_output_names) != len(set(all_output_names)):
        parser.error("duplicate input stems would collide when written as PNG")

    files = all_files
    if args.pages is not None:
        if any(number <= 0 for number in args.pages):
            parser.error("--pages requires positive page numbers")
        if len(args.pages) != len(set(args.pages)):
            parser.error("--pages contains duplicate page numbers")
        pages_by_number: dict[int, list[Path]] = {}
        for path in all_files:
            number = page_number(path)
            if number is not None:
                pages_by_number.setdefault(number, []).append(path)
        missing = [number for number in args.pages if number not in pages_by_number]
        ambiguous = {
            number: [path.name for path in pages_by_number[number]]
            for number in args.pages
            if len(pages_by_number.get(number, [])) > 1
        }
        if missing:
            parser.error(
                "requested page number(s) missing: " + ", ".join(map(str, missing))
            )
        if ambiguous:
            details = "; ".join(
                f"{number} -> {', '.join(names)}"
                for number, names in ambiguous.items()
            )
            parser.error("requested page number(s) ambiguous: " + details)
        wanted = set(args.pages)
        files = [path for path in all_files if page_number(path) in wanted]
    if not files:
        parser.error("no matching input pages")
    if len(files) > args.max_page_count:
        parser.error(
            f"selected page count exceeds --max-page-count "
            f"({len(files)} > {args.max_page_count})"
        )
    total_pixels = sum(info_by_path[path].pixels for path in files)
    if total_pixels > args.max_total_pixels:
        parser.error(
            f"selected total pixels exceed --max-total-pixels "
            f"({total_pixels} > {args.max_total_pixels})"
        )
    for path in files:
        working_bytes, work_units = estimated_page_resources(
            info_by_path[path],
            background_scale=args.background_scale,
            min_background_sigma=args.min_background_sigma,
        )
        if working_bytes > args.max_working_bytes_per_page:
            parser.error(
                f"estimated page working memory exceeds "
                f"--max-working-bytes-per-page "
                f"({working_bytes} > {args.max_working_bytes_per_page}): {path}"
            )
        if work_units > args.max_work_units_per_page:
            parser.error(
                f"estimated page work exceeds --max-work-units-per-page "
                f"({work_units} > {args.max_work_units_per_page}): {path}"
            )

    try:
        output_dir.parent.mkdir(parents=True, exist_ok=True)
    except OSError as error:
        parser.error(f"cannot create output parent directory: {error}")

    staging_dir: Path | None = None
    committed = False
    completed = False
    pages: list[dict[str, object]] = []
    try:
        staging_dir = Path(
            tempfile.mkdtemp(
                prefix=f".{output_dir.name}.staging-", dir=output_dir.parent
            )
        )
        for index, path in enumerate(files, 1):
            input_info = info_by_path[path]
            image = read_image(
                path,
                max_encoded_bytes=args.max_encoded_bytes,
                max_pixels=args.max_pixels_per_page,
                expected=input_info,
            )
            source_bit_depth = input_info.source_bit_depth
            restored, page_status = restore_with_status(
                image,
                background_scale=args.background_scale,
                min_background_sigma=args.min_background_sigma,
                paper_level=args.paper_level,
                black_percentile=args.black_percentile,
                black_floor=args.black_floor,
                black_ceiling=args.black_ceiling,
                white_percentile=args.white_percentile,
                white_floor=args.white_floor,
                min_tone_range=args.min_tone_range,
                whiten_start=args.whiten_start,
                whiten_width=args.whiten_width,
                white_clip=args.white_clip,
            )
            output = staging_dir / f"{path.stem}.png"
            write_png(output, restored)
            pages.append(
                {
                    "input": path.name,
                    "output": output.name,
                    "source_bit_depth": source_bit_depth,
                    "output_bit_depth": int(restored.dtype.itemsize * 8),
                    "width": int(restored.shape[1]),
                    "height": int(restored.shape[0]),
                    **page_status,
                }
            )
            del image, restored
            print(f"processed {index}/{len(files)}: {path.name}", file=sys.stderr)
        summary = json.dumps(
            {
                "processed": len(files),
                "output": str(output_dir),
                "output_format": "PNG",
                "color_space": "grayscale",
                "pages": pages,
            }
        )
        if os.path.lexists(output_dir):
            raise ValueError(f"output path appeared during processing: {output_dir}")
        rehash_inputs(
            inventory,
            max_encoded_bytes=args.max_encoded_bytes,
        )
        staging_dir.rename(output_dir)
        staging_dir = None
        committed = True
        print(summary)
        completed = True
    except (MemoryError, OSError, ValueError, cv2.error) as error:
        parser.error(str(error) or error.__class__.__name__)
    finally:
        if staging_dir is not None:
            shutil.rmtree(staging_dir, ignore_errors=True)
        if committed and not completed:
            shutil.rmtree(output_dir, ignore_errors=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
