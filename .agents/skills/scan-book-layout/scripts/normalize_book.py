# /// script
# requires-python = "==3.12.10"
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
from collections import Counter
import io
import json
import os
import re
import shutil
import sys
import uuid
import warnings
from contextlib import contextmanager
from pathlib import Path
from typing import BinaryIO, Iterator, NamedTuple

import cv2
import numpy as np
import tifffile
from PIL import Image, ImageOps, UnidentifiedImageError

warnings.simplefilter("error", Image.DecompressionBombWarning)


SUPPORTED_IMAGE_EXTENSIONS = {
    ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".tif", ".tiff", ".webp",
}
SUPPORTED_IMAGE_FORMATS = frozenset({"JPEG", "PNG", "TIFF", "WEBP"})
PILLOW_DECODE_ALLOWLIST = tuple(sorted(SUPPORTED_IMAGE_FORMATS))
PILLOW_INVENTORY_ALLOWLIST = (
    "AVIF", "BMP", "CUR", "DCX", "DDS", "DIB", "EPS", "FITS", "FLI", "GIF",
    "ICNS", "ICO", "JPEG", "JPEG2000", "PCX", "PNG", "PPM", "PSD",
    "QOI", "SGI", "TGA", "TIFF", "WEBP", "WMF", "XBM", "XPM",
)
KNOWN_IMAGE_EXTENSIONS = frozenset(
    extension.lower() for extension in Image.registered_extensions()
) | {
    ".apng", ".avif", ".bmp", ".dcx", ".dib", ".dds", ".emf", ".eps", ".fits",
    ".flc", ".fli", ".gif", ".heic", ".heif", ".icns", ".ico", ".j2c",
    ".j2k", ".jf2", ".jpc", ".jp2", ".jpf", ".jpm", ".jpx", ".jxl",
    ".mpo", ".pbm", ".pcx", ".pdf", ".pgm", ".pnm", ".ppm", ".ps",
    ".psd", ".qoi", ".raw", ".sgi", ".svg", ".tga", ".webp", ".wmf",
    ".xbm", ".xpm",
}
MAX_TOP_LEVEL_ENTRIES = 10_000
MAX_IMAGE_FILE_BYTES = 128 * 1024 * 1024
MAX_AGGREGATE_IMAGE_BYTES = 2 * 1024 * 1024 * 1024
INVENTORY_HEADER_BYTES = 64 * 1024
MAX_INVENTORY_PROBE_BYTES = 4 * 1024 * 1024
MAX_CANVAS_WIDTH = 30_000
MAX_CANVAS_HEIGHT = 30_000
MAX_CANVAS_PIXELS = 80_000_000
MAX_CANVAS_WORKING_BYTES = 512 * 1024 * 1024
MAX_GRAYSCALE_BYTES_PER_PIXEL = 2
CANVAS_WORKING_RASTER_COPIES = 3
EDGE_CONTEXT_BYTES_PER_PIXEL = 8
EDGE_BAND_BYTES_PER_PIXEL = 24
EDGE_ROW_WORKING_BYTES = 64
ENCODING_RASTER_COPIES = 3
DECODER_ENCODED_BUFFER_COPIES = 2
MAX_RETAINED_EDGE_COMPONENTS_PER_PAGE = 4_096
MAX_RETAINED_EDGE_COMPONENTS_BATCH = 16_384
MAX_RETAINED_EDGE_REPORT_BYTES_BATCH = 32 * 1024 * 1024


class ReviewRequiredError(ValueError):
    pass


class EncodedImageHeader(NamedTuple):
    detected_format: str
    frame_count: int | None
    width: int
    height: int
    decoded_bytes_per_pixel: int
    grayscale_bytes_per_pixel: int
    decode_working_bytes_per_pixel: int
    encoded_bytes: int = 0


def decoder_format(
    image: Image.Image, frame_count: int | None
) -> str:
    detected_format = (image.format or "").upper()
    if detected_format != "PNG":
        return detected_format

    info = image.info
    loop = info.get("loop")
    has_animation_metadata = (
        isinstance(loop, int)
        and not isinstance(loop, bool)
    ) or type(info.get("default_image")) is bool
    if (
        frame_count not in (None, 1)
        or getattr(image, "is_animated", False) is True
        or has_animation_metadata
    ):
        return "APNG"
    return detected_format


def read_bounded_header(path: Path) -> bytes:
    with path.open("rb") as stream:
        return stream.read(INVENTORY_HEADER_BYTES)


def header_image_format(header: bytes) -> str | None:
    if not header:
        return None
    try:
        with Image.open(
            io.BytesIO(header), formats=PILLOW_INVENTORY_ALLOWLIST
        ) as image:
            return (image.format or "").upper() or None
    except (Image.DecompressionBombWarning, Image.DecompressionBombError) as error:
        raise ValueError("source raster exceeds Pillow's safe pixel limit") from error
    except (UnidentifiedImageError, OSError):
        if header[:4] in {b"II*\x00", b"MM\x00*"}:
            return "TIFF"
        return None


def has_image_container_signature(header: bytes) -> bool:
    return (
        header.startswith((b"\xff\xd8\xff", b"\x89PNG\r\n\x1a\n", b"BM"))
        or header[:4] in {b"II*\x00", b"MM\x00*", b"GIF8"}
        or (
            len(header) >= 12
            and header[:4] == b"RIFF"
            and header[8:12] == b"WEBP"
        )
        or header.startswith(b"\x00\x00\x00\x0cjP  \r\n\x87\n")
        or header.startswith(b"\xb1h\xde:")
    )


class BoundedFileReader:
    def __init__(self, stream: BinaryIO, readable_bytes: int) -> None:
        self.stream = stream
        self.readable_bytes = readable_bytes

    def read(self, size: int = -1) -> bytes:
        remaining = max(0, self.readable_bytes - self.tell())
        return self.stream.read(remaining if size < 0 else min(size, remaining))

    def readline(self, size: int = -1) -> bytes:
        remaining = max(0, self.readable_bytes - self.tell())
        return self.stream.readline(remaining if size < 0 else min(size, remaining))

    def seek(self, offset: int, whence: int = os.SEEK_SET) -> int:
        if whence == os.SEEK_SET:
            target = offset
        elif whence == os.SEEK_CUR:
            target = self.tell() + offset
        elif whence == os.SEEK_END:
            target = self.readable_bytes + offset
        else:
            raise ValueError(f"invalid seek mode: {whence}")
        if target < 0 or target > self.readable_bytes:
            raise OSError("image read exceeded the bounded stream extent")
        return self.stream.seek(target, os.SEEK_SET)

    def tell(self) -> int:
        return self.stream.tell()

    def readable(self) -> bool:
        return True

    def seekable(self) -> bool:
        return True


@contextmanager
def read_bounded_image_candidate(
    path: Path, readable_bytes: int | None = None
) -> Iterator[BoundedFileReader]:
    try:
        size = path.stat().st_size
    except OSError as error:
        raise ValueError(f"cannot inspect image candidate: {path}") from error
    if size > MAX_IMAGE_FILE_BYTES:
        raise ValueError(
            f"image candidate exceeds {MAX_IMAGE_FILE_BYTES} byte file budget: {path}"
        )
    extent = size if readable_bytes is None else min(size, readable_bytes)
    with path.open("rb") as stream:
        yield BoundedFileReader(stream, extent)


def probe_image_format(path: Path, header: bytes) -> str | None:
    detected = header_image_format(header)
    if detected is not None:
        return detected
    try:
        with read_bounded_image_candidate(path) as stream, Image.open(
            stream, formats=PILLOW_INVENTORY_ALLOWLIST
        ) as image:
            return (image.format or "").upper() or None
    except (Image.DecompressionBombWarning, Image.DecompressionBombError) as error:
        raise ValueError(
            f"source raster exceeds Pillow's safe pixel limit: {path}"
        ) from error
    except (UnidentifiedImageError, OSError):
        return None


def decoded_bytes_per_pixel(image: Image.Image) -> int:
    if image.mode == "1":
        return 1
    if image.mode in {"I;16", "I;16L", "I;16B", "I;16N"}:
        return 2
    return max(1, len(image.getbands())) * {
        "I": 4,
        "F": 4,
    }.get(image.mode, 1)


def decode_working_bytes_per_pixel(image: Image.Image) -> int:
    decoded = decoded_bytes_per_pixel(image)
    grayscale = (
        2 if image.mode in {"I", "I;16", "I;16L", "I;16B", "I;16N"} else 1
    )
    transparency = image.info.get("transparency")

    if image.mode in {"I;16", "I;16L", "I;16B", "I;16N"}:
        # Decoder storage, EXIF copy, Pillow's array materialization, the
        # native-endian uint16 copy, and a possible grayscale inversion copy.
        return 5 * grayscale
    if image.mode == "I":
        # The Pillow I raster and its EXIF copy are 32-bit. NumPy then
        # materializes another 32-bit array before producing uint16 grayscale.
        return 3 * decoded + 2 * grayscale
    if image.mode in {"LA", "RGBA"} or transparency is not None:
        # Keep the source and EXIF copy while creating RGBA input, white
        # background, alpha-composite, L image, and Pillow's L materialization.
        return 2 * decoded + 12 + 2 * grayscale

    # Source storage, EXIF copy, converted L storage, Pillow materialization,
    # and a possible grayscale inversion array.
    return 2 * decoded + 3 * grayscale


def tiff_dimensions_are_display_oriented(
    image: Image.Image, orientation: int
) -> bool:
    if (image.format or "").upper() != "TIFF" or orientation not in {5, 6, 7, 8}:
        return False
    try:
        stored_width = int(image.tag_v2[256])
        stored_height = int(image.tag_v2[257])
    except (AttributeError, KeyError, TypeError, ValueError):
        return False
    return image.size == (stored_height, stored_width)


def displayed_header_size(image: Image.Image) -> tuple[int, int]:
    width, height = image.size
    try:
        orientation = int(image.getexif().get(274, 1))
    except (OSError, TypeError, ValueError):
        orientation = 1
    if orientation in {5, 6, 7, 8} and not tiff_dimensions_are_display_oriented(
        image, orientation
    ):
        return height, width
    return width, height


def validate_header_dimensions(path: Path, width: int, height: int) -> None:
    if width <= 0 or height <= 0:
        raise ValueError(f"source raster dimensions must be positive: {path}")
    if width > MAX_CANVAS_WIDTH or height > MAX_CANVAS_HEIGHT:
        raise ValueError(
            "source raster has an extreme aspect or axis beyond the fixed "
            f"{MAX_CANVAS_WIDTH}x{MAX_CANVAS_HEIGHT} resize preflight limit: "
            f"{width}x{height}: {path}"
        )
    pixels = width * height
    if pixels > MAX_CANVAS_PIXELS:
        raise ValueError(
            "source raster pixel budget exceeded before decode "
            f"({pixels} pixels, limit {MAX_CANVAS_PIXELS}): {path}"
        )


def inspect_encoded_image(
    path: Path, header: bytes | None = None
) -> EncodedImageHeader | None:
    header = read_bounded_header(path) if header is None else header
    if (
        path.suffix.lower() not in KNOWN_IMAGE_EXTENSIONS
        and probe_image_format(path, header) is None
    ):
        return None
    try:
        encoded_bytes = path.stat().st_size
        with read_bounded_image_candidate(path) as stream, Image.open(
            stream, formats=PILLOW_INVENTORY_ALLOWLIST
        ) as image:
            try:
                frame_count = int(getattr(image, "n_frames", 1))
            except (OSError, TypeError, ValueError):
                frame_count = None
            detected_format = decoder_format(image, frame_count)
            width, height = displayed_header_size(image)
            validate_header_dimensions(path, width, height)
            validate_source_depth(image, path, (image.format or "").upper())
            return EncodedImageHeader(
                detected_format,
                frame_count,
                width,
                height,
                decoded_bytes_per_pixel(image),
                2 if image.mode in {"I", "I;16", "I;16L", "I;16B", "I;16N"} else 1,
                decode_working_bytes_per_pixel(image),
                encoded_bytes,
            )
    except (Image.DecompressionBombWarning, Image.DecompressionBombError) as error:
        raise ValueError(
            f"source raster exceeds Pillow's safe pixel limit: {path}"
        ) from error
    except (UnidentifiedImageError, OSError):
        if header[:4] in {b"II*\x00", b"MM\x00*"}:
            try:
                with tifffile.TiffFile(path) as tiff:
                    page_count = len(tiff.pages)
                    if page_count == 0:
                        raise ValueError(f"TIFF contains no image frames: {path}")
                    page = tiff.pages[0]
                    height, width = (int(value) for value in page.shape[:2])
                    orientation_tag = page.tags.get(274)
                    orientation = (
                        int(orientation_tag.value) if orientation_tag else 1
                    )
                    if orientation in {5, 6, 7, 8}:
                        width, height = height, width
                    validate_header_dimensions(path, width, height)
                    return EncodedImageHeader(
                        "TIFF", page_count, width, height,
                        int(page.dtype.itemsize) * int(page.samplesperpixel),
                        2 if int(page.dtype.itemsize) > 1 else 1,
                        10 if int(page.dtype.itemsize) > 1 else 5,
                        encoded_bytes,
                    )
            except (tifffile.TiffFileError, OSError):
                pass
        return None


def encoded_image_info(
    path: Path, header: bytes | None = None
) -> tuple[str, int | None] | None:
    inspected = inspect_encoded_image(path, header)
    if inspected is None:
        return None
    return inspected.detected_format, inspected.frame_count


def source_raw_mode(image: Image.Image) -> str:
    tiles = getattr(image, "tile", ())
    if not tiles:
        return image.mode
    tile_args = getattr(tiles[0], "args", "")
    if isinstance(tile_args, str):
        return tile_args
    if isinstance(tile_args, (tuple, list)) and tile_args:
        raw_mode = tile_args[0]
        if isinstance(raw_mode, str):
            return raw_mode
    return image.mode


def tiff_tag_values(image: Image.Image, tag: int, default: int) -> tuple[int, ...]:
    value = image.tag_v2.get(tag, default)
    values = value if isinstance(value, (tuple, list)) else (value,)
    try:
        parsed = tuple(int(item) for item in values)
    except (TypeError, ValueError) as error:
        raise ValueError(f"invalid TIFF tag {tag}") from error
    if not parsed or any(item <= 0 for item in parsed):
        raise ValueError(f"invalid TIFF tag {tag}")
    return parsed


def replace_source_raw_mode(
    image: Image.Image, old_mode: str, new_mode: str
) -> None:
    updated_tiles = []
    for tile in image.tile:
        args = tile.args
        if isinstance(args, str):
            updated_args = new_mode if args == old_mode else args
        elif isinstance(args, (tuple, list)) and args and args[0] == old_mode:
            updated_args = (new_mode, *args[1:])
        else:
            updated_args = args
        updated_tiles.append(tile._replace(args=updated_args))
    image.tile = updated_tiles


def tiff_white_is_zero_needs_inversion(
    image: Image.Image, path: Path, detected_format: str
) -> bool:
    if detected_format != "TIFF":
        return False

    photometric_value = image.tag_v2.get(262, 1)
    if isinstance(photometric_value, (tuple, list)):
        if len(photometric_value) != 1:
            raise ValueError(f"invalid TIFF PhotometricInterpretation: {path}")
        photometric_value = photometric_value[0]
    try:
        photometric = int(photometric_value)
    except (TypeError, ValueError) as error:
        raise ValueError(f"invalid TIFF PhotometricInterpretation: {path}") from error
    if photometric not in (0, 1):
        return False
    if photometric == 1:
        return False

    bits_per_sample = tiff_tag_values(image, 258, 1)
    sample_formats = tiff_tag_values(image, 339, 1)
    samples_per_pixel = tiff_tag_values(image, 277, len(image.getbands()))[0]
    if (
        samples_per_pixel != 1
        or bits_per_sample not in {(1,), (8,), (16,)}
        or sample_formats != (1,)
    ):
        raise ValueError(
            "unsupported WhiteIsZero TIFF grayscale format "
            f"(mode {image.mode}, BitsPerSample {bits_per_sample}, "
            f"SampleFormat {sample_formats}): {path}"
        )

    raw_mode = source_raw_mode(image)
    if raw_mode == "L;IR":
        # Pillow exposes this valid FillOrder=2 mode but its raw decoder cannot
        # load it. Decode reversed bits and perform the photometric inversion here.
        replace_source_raw_mode(image, raw_mode, "L;R")
        return True
    raw_base, separator, raw_options = raw_mode.partition(";")
    pillow_inverts_during_decode = (
        separator == ";" and raw_base in {"1", "L"} and "I" in raw_options
    )
    return not pillow_inverts_during_decode


def validate_source_depth(
    image: Image.Image, path: Path, detected_format: str
) -> None:
    raw_mode = source_raw_mode(image)
    raw_depth_match = re.search(r";(\d+)", raw_mode)
    raw_depth = int(raw_depth_match.group(1)) if raw_depth_match else 0

    if detected_format != "TIFF":
        if raw_depth > 8 and image.mode not in {
            "I;16", "I;16L", "I;16B", "I;16N",
        }:
            raise ValueError(
                f"unsupported high-depth color/alpha mode {raw_mode}: {path}"
            )
        return

    bits_per_sample = tiff_tag_values(image, 258, 1)
    sample_formats = tiff_tag_values(image, 339, 1)
    if any(sample_format != 1 for sample_format in sample_formats):
        raise ValueError(
            f"unsupported TIFF SampleFormat {sample_formats}: {path}"
        )
    sample_depth = max((*bits_per_sample, raw_depth))
    if sample_depth <= 8:
        return

    samples_per_pixel = tiff_tag_values(image, 277, len(image.getbands()))[0]
    grayscale_modes = {"I;16", "I;16L", "I;16B", "I;16N"}
    if samples_per_pixel != 1 or image.mode not in grayscale_modes:
        raise ValueError(
            "unsupported high-depth color/alpha TIFF "
            f"(mode {image.mode}, raw mode {raw_mode}, "
            f"BitsPerSample {bits_per_sample}, SampleFormat {sample_formats}): "
            f"{path}"
        )
    if bits_per_sample != (16,):
        raise ValueError(
            f"unsupported TIFF sample depth {bits_per_sample}: {path}"
        )


def image_to_gray_array(
    image: Image.Image, path: Path, invert_grayscale: bool = False
) -> np.ndarray:
    transparency = image.info.get("transparency")
    if image.mode in {"I;16", "I;16L", "I;16B", "I;16N"}:
        array = np.asarray(image)
        gray = np.asarray(array, dtype=np.dtype("=u2")).copy()
        if invert_grayscale:
            gray = np.iinfo(np.uint16).max - gray
        if transparency is not None:
            if not isinstance(transparency, int) or not 0 <= transparency <= 65535:
                raise ValueError(f"unsupported 16-bit transparency: {path}")
            gray[gray == transparency] = np.iinfo(np.uint16).max
        return gray
    if image.mode == "I":
        if transparency is not None:
            raise ValueError(f"unsupported integer transparency: {path}")
        array = np.asarray(image)
        if array.size and (int(array.min()) < 0 or int(array.max()) > 65535):
            raise ValueError(f"unsupported integer grayscale range: {path}")
        gray = array.astype(np.uint16)
        if invert_grayscale:
            gray = np.iinfo(np.uint16).max - gray
        return gray
    if image.mode in {"LA", "RGBA"} or transparency is not None:
        if image.mode not in {"1", "L", "P", "LA", "RGB", "RGBA"}:
            raise ValueError(
                f"unsupported alpha/transparency mode {image.mode}: {path}"
            )
        rgba = image.convert("RGBA")
        white = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        composited = Image.alpha_composite(white, rgba).convert("L")
        return np.asarray(composited, dtype=np.uint8)
    if "A" in image.getbands():
        raise ValueError(f"unsupported alpha mode {image.mode}: {path}")
    gray = np.asarray(image.convert("L"), dtype=np.uint8)
    if invert_grayscale:
        gray = np.iinfo(np.uint8).max - gray
    return gray


def orient_tiff_array(values: np.ndarray, orientation: int) -> np.ndarray:
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
        raise ValueError(f"unsupported TIFF orientation {orientation}")
    return np.ascontiguousarray(operations[orientation](values))


def decode_tiff_uint16_fallback(
    source: bytes | Path, path_or_header: Path | bytes
) -> np.ndarray:
    if isinstance(source, bytes):
        path = path_or_header
        if not isinstance(path, Path):
            raise TypeError("TIFF byte fallback requires its source path")
        header = source[:4]
        tiff_source: object = io.BytesIO(source)
    else:
        path = source
        if not isinstance(path_or_header, bytes):
            raise TypeError("TIFF path fallback requires its bounded header")
        header = path_or_header
        tiff_source = path
    byte_order = {b"II": "<", b"MM": ">"}.get(header[:2])
    if byte_order is None:
        raise ValueError(f"invalid TIFF byte-order marker: {path}")
    try:
        with tifffile.TiffFile(tiff_source) as tiff:
            if tiff.byteorder != byte_order or len(tiff.pages) != 1:
                raise ValueError(
                    f"TIFF fallback metadata disagrees with the container: {path}"
                )
            page = tiff.pages[0]
            shape = tuple(int(value) for value in page.shape)
            height, width = shape if len(shape) == 2 else (0, 0)
            pixel_count = width * height
            if (
                Image.MAX_IMAGE_PIXELS is not None
                and pixel_count > Image.MAX_IMAGE_PIXELS
            ):
                raise Image.DecompressionBombError(
                    f"TIFF raster size {pixel_count} exceeds the safe limit"
                )
            orientation_tag = page.tags.get(274)
            orientation = int(orientation_tag.value) if orientation_tag else 1
            if (
                width <= 0
                or height <= 0
                or page.dtype.kind != "u"
                or page.dtype.itemsize != 2
                or int(page.bitspersample) != 16
                or int(page.samplesperpixel) != 1
                or int(page.sampleformat) != 1
                or int(page.photometric) not in (0, 1)
                or int(page.fillorder) not in (1, 2)
                or bool(page.extrasamples)
            ):
                raise ValueError(
                    "TIFF fallback requires unsigned 16-bit single-channel "
                    f"FillOrder=1/2 WhiteIsZero/BlackIsZero data: {path}"
                )
            values = page.asarray()
            photometric = int(page.photometric)
    except (tifffile.TiffFileError, OSError) as error:
        raise ValueError(f"cannot decode image: {path}") from error

    if values.shape != (height, width) or values.dtype.kind != "u":
        raise ValueError(
            f"TIFF fallback decoder returned an unexpected raster: {path}"
        )
    gray = np.asarray(values, dtype=np.dtype("=u2")).copy()
    if photometric == 0:
        gray = np.iinfo(np.uint16).max - gray
    return orient_tiff_array(gray, orientation)


def read_gray(path: Path) -> np.ndarray:
    header = read_bounded_header(path)
    encoded_bytes = path.stat().st_size
    try:
        with read_bounded_image_candidate(path) as stream, Image.open(
            stream, formats=PILLOW_DECODE_ALLOWLIST
        ) as image:
            container_format = (image.format or "").upper()
            if container_format not in SUPPORTED_IMAGE_FORMATS:
                raise ValueError(
                    f"unsupported encoded image format {container_format or 'unknown'}: {path}"
                )
            try:
                frame_count = int(getattr(image, "n_frames", 1))
            except (OSError, TypeError, ValueError) as error:
                raise ValueError(f"cannot determine frame count: {path}") from error
            if frame_count != 1:
                raise ValueError(
                    "input must contain exactly one decodable frame "
                    f"({frame_count} frames): {path}"
                )
            detected_format = decoder_format(image, frame_count)
            if detected_format != container_format:
                raise ValueError(
                    f"unsupported encoded image format {detected_format}: {path}"
                )
            width, height = displayed_header_size(image)
            validate_header_dimensions(path, width, height)
            header_info = EncodedImageHeader(
                detected_format,
                frame_count,
                width,
                height,
                decoded_bytes_per_pixel(image),
                2 if image.mode in {"I", "I;16", "I;16L", "I;16B", "I;16N"} else 1,
                decode_working_bytes_per_pixel(image),
                encoded_bytes,
            )
            validate_header_processing_memory(path, header_info, width, height)
            image.seek(0)
            validate_source_depth(image, path, container_format)
            invert_grayscale = tiff_white_is_zero_needs_inversion(
                image, path, container_format
            )
            try:
                try:
                    orientation = int(image.getexif().get(274, 1))
                except (OSError, TypeError, ValueError):
                    orientation = 1
                oriented = (
                    image
                    if tiff_dimensions_are_display_oriented(image, orientation)
                    else ImageOps.exif_transpose(image)
                )
                return image_to_gray_array(oriented, path, invert_grayscale)
            except OSError:
                if (
                    container_format == "TIFF"
                    and tiff_tag_values(image, 258, 1) == (16,)
                    and tiff_tag_values(image, 339, 1) == (1,)
                    and tiff_tag_values(image, 277, len(image.getbands())) == (1,)
                    and tiff_tag_values(image, 266, 1) == (2,)
                ):
                    return decode_tiff_uint16_fallback(path, header)
                raise
    except (Image.DecompressionBombWarning, Image.DecompressionBombError) as error:
        raise ValueError(
            f"source raster exceeds Pillow's safe pixel limit: {path}"
        ) from error
    except UnidentifiedImageError as error:
        if header[:4] in {b"II*\x00", b"MM\x00*"}:
            return decode_tiff_uint16_fallback(path, header)
        raise ValueError(f"cannot decode image: {path}") from error
    except OSError as error:
        if header[:4] in {b"II*\x00", b"MM\x00*"}:
            return decode_tiff_uint16_fallback(path, header)
        raise ValueError(f"cannot decode image: {path}") from error


def write_png(path: Path, image: np.ndarray) -> None:
    if image.dtype not in (np.dtype(np.uint8), np.dtype(np.uint16)):
        raise ValueError(f"unsupported grayscale dtype {image.dtype}: {path}")
    ok, encoded = cv2.imencode(".png", image, [cv2.IMWRITE_PNG_COMPRESSION, 9])
    if not ok:
        raise ValueError(f"cannot encode image: {path}")
    encoded.tofile(path)


def natural_key(path: Path) -> tuple[object, ...]:
    return tuple(
        int(part) if part.isdigit() else part.casefold()
        for part in re.split(r"(\d+)", path.name)
    )


def page_number(path: Path) -> int | None:
    runs = re.findall(r"\d+", path.stem)
    return int(runs[-1]) if runs else None


def clean_right_edge(
    image: np.ndarray,
    confidence_threshold: float,
    enabled: bool = True,
    max_reported_components: int = MAX_RETAINED_EDGE_COMPONENTS_PER_PAGE,
    max_report_bytes: int = MAX_RETAINED_EDGE_REPORT_BYTES_BATCH,
) -> tuple[np.ndarray, dict[str, object]]:
    height, width = image.shape
    cleaned = image.copy()
    candidates: list[dict[str, object]] = []
    candidate_report_bytes = 0
    removed = 0
    inspection_band_width = max(12, round(width * 0.04))
    inspection_band_width = min(width, inspection_band_width)
    maximum_border_width = max(3, round(width * 0.02))
    scanner_minimum_width = max(3, round(width * (7.0 / 2600.0)))
    scanner_maximum_width = max(
        scanner_minimum_width, round(width * (40.0 / 2600.0))
    )
    band_start = width - inspection_band_width
    white = int(np.iinfo(image.dtype).max)
    band_image = image[:, band_start:]
    context_start = max(0, band_start - 3 * inspection_band_width)
    intensity_scale = 255.0 / white
    normalized_context = image[:, context_start:].astype(np.float32) * intensity_scale
    normalized_band = band_image.astype(np.float32) * intensity_scale
    row_background = np.percentile(normalized_context, 80, axis=1)
    background_filter_height = min(31, height if height % 2 else height - 1)
    background_filter_height = max(1, background_filter_height)
    if background_filter_height > 1:
        radius = background_filter_height // 2
        padded_background = np.pad(row_background, radius, mode="edge")
        row_background = np.median(
            np.lib.stride_tricks.sliding_window_view(
                padded_background, background_filter_height
            ),
            axis=1,
        )
    minimum_foreground_contrast = 3.0
    strong_foreground_contrast = 12.0
    local_background_sigmas = sorted(
        {
            3.0,
            max(3.0, scanner_maximum_width / 2.0),
        }
    )
    foreground_contrast = np.full_like(normalized_band, -255.0)
    for local_background_sigma in local_background_sigmas:
        local_background = cv2.GaussianBlur(
            normalized_context,
            (0, 0),
            sigmaX=local_background_sigma,
            sigmaY=local_background_sigma,
            borderType=cv2.BORDER_REPLICATE,
        )[:, -inspection_band_width:]
        np.maximum(
            foreground_contrast,
            local_background - normalized_band,
            out=foreground_contrast,
        )

    interior_reference_width = max(
        1, inspection_band_width - scanner_maximum_width
    )
    interior_background = np.percentile(
        normalized_band[:, :interior_reference_width], 80, axis=1
    )
    if background_filter_height > 1:
        padded_interior = np.pad(interior_background, radius, mode="edge")
        interior_background = np.median(
            np.lib.stride_tricks.sliding_window_view(
                padded_interior, background_filter_height
            ),
            axis=1,
        )
    interior_reference_contrast = (
        interior_background[:, None] - normalized_band
    )
    np.maximum(
        foreground_contrast,
        interior_reference_contrast,
        out=foreground_contrast,
    )
    weak_foreground = (
        foreground_contrast >= minimum_foreground_contrast
    ).astype(np.uint8)
    strong_foreground = foreground_contrast >= strong_foreground_contrast
    weak_count, weak_labels, _, _ = cv2.connectedComponentsWithStats(
        weak_foreground, 8
    )
    retained_weak_labels = np.zeros(weak_count, dtype=bool)
    retained_weak_labels[np.unique(weak_labels[strong_foreground])] = True
    retained_weak_labels[0] = False
    dark = retained_weak_labels[weak_labels].astype(np.uint8)
    strongly_dark = normalized_band <= row_background[:, None] * (96.0 / 255.0)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(dark, 8)
    component_count = count - 1
    if component_count > max_reported_components:
        raise ReviewRequiredError(
            "right-edge candidate report budget exceeded "
            f"({component_count} candidates, limit {max_reported_components}); "
            "all candidates require manual review and no cleanup was published"
        )
    long_run_width = max(12, round(width * 0.04))
    short_branch_width = max(3, round(width * 0.005))

    for label in range(1, count):
        local_x, y, component_width, component_height, _ = (
            int(value) for value in stats[label]
        )
        x = band_start + local_x
        right_gap = inspection_band_width - (local_x + component_width)

        height_ratio = component_height / height
        label_box = labels[
            y:y + component_height, local_x:local_x + component_width
        ]
        rows = label_box == label
        actual_border_contact = right_gap == 0 and bool(np.any(rows[:, -1]))
        reaches_inner_band_boundary = local_x == 0 and bool(np.any(rows[:, 0]))
        edge_contact = float(np.count_nonzero(rows[:, -1])) if right_gap == 0 else 0.0
        edge_contact /= component_height
        row_spans: list[int] = []
        for row in rows:
            columns = np.flatnonzero(row)
            row_spans.append(int(columns[-1] - columns[0] + 1) if columns.size else 0)
        max_horizontal_run = max(row_spans, default=0)
        nonzero_spans = {span for span in row_spans if span}
        variable_horizontal_profile = len(nonzero_spans) > 1
        nonempty_spans = [span for span in row_spans if span]
        minimum_horizontal_run = min(nonempty_spans, default=0)
        horizontal_profile_stddev = float(np.std(nonempty_spans))
        span_counts = Counter(nonempty_spans)
        modal_horizontal_run, modal_span_rows = (
            span_counts.most_common(1)[0] if span_counts else (0, 0)
        )
        boundary_recessions = [
            modal_horizontal_run - span for span in nonempty_spans
        ]
        maximum_boundary_recession = max(boundary_recessions, default=0)
        deep_boundary_rows = [
            index
            for index, recession in enumerate(boundary_recessions)
            if recession >= 2
        ]
        deep_boundary_intervals = np.diff(deep_boundary_rows)
        repeated_regular_deep_recessions = (
            len(deep_boundary_rows) >= 4
            and bool(deep_boundary_intervals.size)
            and float(np.mean(deep_boundary_intervals)) >= 2.0
            and float(np.std(deep_boundary_intervals))
            <= max(0.5, float(np.mean(deep_boundary_intervals)) * 0.15)
        )
        boundary_variation_ratio = (
            1.0 - modal_span_rows / len(nonempty_spans)
            if nonempty_spans
            else 0.0
        )
        component_box = band_image[
            y:y + component_height, local_x:local_x + component_width
        ]
        component_pixels = component_box[rows]
        intensity_stddev_native = float(np.std(component_pixels))
        intensity_stddev = intensity_stddev_native * 255.0 / white
        solid_strip_ratio = float(np.count_nonzero(rows)) / rows.size
        dark_solid_ratio = (
            float(
                np.count_nonzero(
                    strongly_dark[
                        y:y + component_height,
                        local_x:local_x + component_width,
                    ]
                    & rows
                )
            )
            / rows.size
        )
        contiguous_to_edge = all(
            not np.any(row) or np.all(row[np.flatnonzero(row)[0]:])
            for row in rows
        )
        inward_background_matches = 0
        inward_background_samples = 0
        inward_column = local_x - 1
        for row_offset, row in enumerate(rows):
            if inward_column < 0 or not np.any(row):
                continue
            inward_background_samples += 1
            inward_value = normalized_band[y + row_offset, inward_column]
            if abs(float(inward_value) - float(row_background[y + row_offset])) <= 8.0:
                inward_background_matches += 1
        inward_background_ratio = (
            inward_background_matches / inward_background_samples
            if inward_background_samples
            else 0.0
        )
        row_relative_inward_branch_rows = 0
        for row_offset, row in enumerate(rows):
            columns = np.flatnonzero(row)
            if not columns.size:
                continue
            branch_end = local_x + int(columns[0])
            inward_contrast = (
                row_background[y + row_offset]
                - normalized_band[y + row_offset, :branch_end]
            )
            inward_foreground = inward_contrast >= minimum_foreground_contrast
            inward_run = 0
            for is_foreground in inward_foreground[::-1]:
                if not is_foreground:
                    break
                inward_run += 1
            if inward_run >= short_branch_width:
                row_relative_inward_branch_rows += 1
        inward_branch_rows = sum(
            span > modal_horizontal_run + 1 for span in nonempty_spans
        ) + row_relative_inward_branch_rows
        long_horizontal_rows = sum(span >= long_run_width for span in row_spans)
        long_horizontal_ratio = long_horizontal_rows / component_height
        artifact_isolated = component_width <= maximum_border_width
        connected_to_long_horizontal = (
            max_horizontal_run >= long_run_width or long_horizontal_ratio >= 0.01
        )
        connected_to_broad_content = (
            component_width > maximum_border_width or reaches_inner_band_boundary
        )
        branch_rows = sum(
            span >= minimum_horizontal_run + short_branch_width
            for span in nonempty_spans
        )
        meaningful_horizontal_branches = (
            (
                max_horizontal_run
                >= minimum_horizontal_run + short_branch_width
                and branch_rows >= 1
            )
            or row_relative_inward_branch_rows >= 1
        )
        thin_flush_edge_component = actual_border_contact and component_width <= 2
        ordinary_rule_boundary_variation = maximum_boundary_recession <= 1
        rule_like_geometry = (
            solid_strip_ratio >= 0.95
            and horizontal_profile_stddev <= 0.1
        ) or (
            solid_strip_ratio >= 0.95
            and horizontal_profile_stddev <= 0.5
            and contiguous_to_edge
            and ordinary_rule_boundary_variation
        )
        uniform_rule_like = rule_like_geometry
        scanner_tonal_variation = (
            solid_strip_ratio >= 0.95
            and horizontal_profile_stddev <= 0.5
            and 2.0 <= intensity_stddev <= 12.0
        )
        low_variance_solid_strip = (
            solid_strip_ratio >= 0.95
            and dark_solid_ratio >= 0.9
            and intensity_stddev <= 12.0
            and horizontal_profile_stddev <= 0.5
            and contiguous_to_edge
        )
        scanner_width_cue = (
            scanner_minimum_width <= component_width <= scanner_maximum_width
            and modal_horizontal_run >= 3
            and minimum_horizontal_run >= 1
        )
        scanner_solidness_cue = (
            low_variance_solid_strip and solid_strip_ratio >= 0.95
        )
        scanner_background_separation_cue = (
            inward_background_samples >= component_height * 0.9
            and inward_background_ratio >= 0.98
        )
        scanner_boundary_cue = (
            not rule_like_geometry
            and 0.02 <= boundary_variation_ratio <= 0.20
            and horizontal_profile_stddev <= 0.5
            and maximum_boundary_recession >= 2
            and repeated_regular_deep_recessions
        )
        no_inward_branches = (
            inward_branch_rows == 0
            and not meaningful_horizontal_branches
            and not connected_to_long_horizontal
            and not connected_to_broad_content
        )
        scanner_attachment_absence_cue = no_inward_branches
        scanner_strip_specific_cues = (
            scanner_width_cue
            and scanner_solidness_cue
            and scanner_background_separation_cue
            and scanner_boundary_cue
            and scanner_attachment_absence_cue
        )
        benign_strip_boundary_variation = (
            scanner_strip_specific_cues and variable_horizontal_profile
        )
        ambiguous_attachment = (
            (variable_horizontal_profile and not benign_strip_boundary_variation)
            or not low_variance_solid_strip
            or not scanner_background_separation_cue
        )
        attached_content = (
            connected_to_long_horizontal
            or connected_to_broad_content
            or meaningful_horizontal_branches
            or ambiguous_attachment
        )

        def evidence_above_floor(value: float, floor: float) -> float:
            return max(0.0, min(1.0, (value - floor) / (1.0 - floor)))

        height_score = evidence_above_floor(height_ratio, 0.75)
        contact_score = evidence_above_floor(edge_contact, 0.9)
        solidity_score = (
            1.0
            if scanner_boundary_cue
            else evidence_above_floor(solid_strip_ratio, 0.95)
        )
        profile_score = (
            1.0
            if scanner_boundary_cue
            else max(0.0, min(1.0, 1.0 - horizontal_profile_stddev / 0.5))
        )
        darkness_score = (
            1.0
            if scanner_boundary_cue and dark_solid_ratio >= 0.95
            else evidence_above_floor(dark_solid_ratio, 0.9)
        )
        intensity_score = max(0.0, min(1.0, 1.0 - intensity_stddev / 12.0))
        confidence = (
            0.62
            + 0.10 * height_score
            + 0.08 * contact_score
            + 0.06 * solidity_score
            + 0.06 * darkness_score
            + 0.04 * profile_score
            + 0.04 * intensity_score
        )
        eligible = (
            scanner_minimum_width <= component_width <= scanner_maximum_width
            and height_ratio >= 0.75
            and actual_border_contact
            and edge_contact >= 0.9
            and artifact_isolated
            and low_variance_solid_strip
            and scanner_tonal_variation
            and scanner_strip_specific_cues
            and not rule_like_geometry
            and no_inward_branches
            and not ambiguous_attachment
            and not attached_content
        )
        accepted = enabled and eligible and confidence >= confidence_threshold
        reasons: list[str] = []
        if not enabled:
            reasons.append("edge cleanup disabled")
        if thin_flush_edge_component:
            reasons.append(
                "thin 1-2px flush-edge component is indistinguishable from a genuine marginal rule"
            )
        if rule_like_geometry:
            reasons.append(
                "rule-like flush-edge geometry is indistinguishable from a genuine marginal or page-frame rule"
            )
        elif component_width >= 3 and not scanner_strip_specific_cues:
            reasons.append(
                "insufficient scanner-strip-specific width, solidness, background-separation, boundary, or attachment-absence evidence"
            )
        if height_ratio < 0.75:
            reasons.append("insufficient large-height extent")
        if not actual_border_contact:
            reasons.append(
                "internal or near-edge component does not touch the actual "
                "outermost right column"
            )
        if edge_contact < 0.9:
            reasons.append("insufficient sustained physical-border contact")
        if not artifact_isolated:
            reasons.append("not isolated from broad page content")
        if not low_variance_solid_strip:
            reasons.append("insufficient low-variance solid-strip evidence")
        if not scanner_tonal_variation:
            reasons.append(
                "scanner tonal variation is outside the required "
                "2.0-12.0 intensity standard-deviation range"
            )
        if inward_branch_rows:
            reasons.append("component has branches or content extending inward")
        if connected_to_long_horizontal:
            reasons.append("connected to long horizontal content or staff endings")
        elif meaningful_horizontal_branches:
            reasons.append("meaningful horizontal branches or short staff endings")
        elif ambiguous_attachment:
            reasons.append("ambiguous attached content requires review")
        if enabled and eligible and confidence < confidence_threshold:
            reasons.append("confidence below threshold")
        if accepted:
            decision = "removed: isolated border-connected artifact"
            status = "removed"
            reasons.append("eligible and confidence met threshold")
        else:
            decision = "preserved: " + "; ".join(reasons)
            status = "preserved/review"

        candidate = {
            "bounds": [x, y, component_width, component_height],
            "confidence": round(confidence, 3),
            "eligible": eligible,
            "accepted": accepted,
            "status": status,
            "decision": decision,
            "reasons": reasons,
            "right_gap_pixels": right_gap,
            "touches_outermost_column": actual_border_contact,
            "touches_physical_border": actual_border_contact,
            "within_physical_border_tolerance": actual_border_contact,
            "encoding_tolerance_used": False,
            "height_ratio": round(height_ratio, 3),
            "edge_contact_ratio": round(edge_contact, 3),
            "component_width_ratio": round(component_width / width, 4),
            "thin_flush_edge_component": thin_flush_edge_component,
            "uniform_rule_like": uniform_rule_like,
            "rule_like_geometry": rule_like_geometry,
            "scanner_tonal_variation": scanner_tonal_variation,
            "scanner_strip_specific_cues": scanner_strip_specific_cues,
            "scanner_width_cue": scanner_width_cue,
            "scanner_width_range_pixels": [
                scanner_minimum_width,
                scanner_maximum_width,
            ],
            "scanner_solidness_cue": scanner_solidness_cue,
            "scanner_background_separation_cue": scanner_background_separation_cue,
            "scanner_boundary_cue": scanner_boundary_cue,
            "scanner_attachment_absence_cue": scanner_attachment_absence_cue,
            "low_variance_solid_strip": low_variance_solid_strip,
            "solid_strip_ratio": round(solid_strip_ratio, 3),
            "dark_solid_ratio": round(dark_solid_ratio, 3),
            "inward_background_ratio": round(inward_background_ratio, 3),
            "inward_background_samples": inward_background_samples,
            "intensity_stddev": round(intensity_stddev, 3),
            "intensity_stddev_native": round(intensity_stddev_native, 3),
            "horizontal_profile_stddev": round(horizontal_profile_stddev, 3),
            "modal_horizontal_run": modal_horizontal_run,
            "boundary_variation_ratio": round(boundary_variation_ratio, 3),
            "maximum_boundary_recession": maximum_boundary_recession,
            "repeated_regular_deep_recessions": (
                repeated_regular_deep_recessions
            ),
            "ordinary_rule_boundary_variation": (
                ordinary_rule_boundary_variation
            ),
            "minimum_horizontal_run": minimum_horizontal_run,
            "inward_branch_rows": inward_branch_rows,
            "row_relative_inward_branch_rows": (
                row_relative_inward_branch_rows
            ),
            "no_inward_branches": no_inward_branches,
            "artifact_isolated": artifact_isolated,
            "connected_to_long_horizontal": connected_to_long_horizontal,
            "connected_to_broad_content": connected_to_broad_content,
            "reaches_inner_inspection_band_boundary": reaches_inner_band_boundary,
            "meaningful_horizontal_branches": meaningful_horizontal_branches,
            "ambiguous_attachment": ambiguous_attachment,
            "attached_content": attached_content,
            "review_required": not accepted,
            "variable_horizontal_profile": variable_horizontal_profile,
            "max_horizontal_run": max_horizontal_run,
            "short_branch_width_pixels": short_branch_width,
            "horizontal_branch_rows": branch_rows,
            "long_horizontal_row_ratio": round(long_horizontal_ratio, 3),
        }
        candidate_size = retained_size(candidate)
        if candidate_report_bytes + candidate_size > max_report_bytes:
            raise ReviewRequiredError(
                "right-edge candidate report memory budget exceeded "
                f"(limit {max_report_bytes} bytes); all candidates require "
                "manual review and no cleanup was published"
            )
        candidate_report_bytes += candidate_size
        candidates.append(candidate)

        if accepted:
            cleaned_box = cleaned[
                y:y + component_height, x:x + component_width
            ]
            cleaned_box[rows] = white
            removed += 1

    return cleaned, {
        "side": "right",
        "enabled": enabled,
        "confidence_threshold": confidence_threshold,
        "threshold_semantics": "accept eligible candidates with confidence >= threshold",
        "inspection_band_width_pixels": inspection_band_width,
        "foreground_segmentation": (
            "multi-scale local and interior-reference paper-background "
            "contrast with weak components retained only when connected to "
            "strong contrast"
        ),
        "minimum_foreground_contrast_8bit_equivalent": minimum_foreground_contrast,
        "strong_foreground_contrast_8bit_equivalent": strong_foreground_contrast,
        "local_background_gaussian_sigma_pixels": local_background_sigmas,
        "interior_reference_width_pixels": interior_reference_width,
        "scanner_artifact_width_range_pixels": [
            scanner_minimum_width,
            scanner_maximum_width,
        ],
        "estimated_background_8bit_equivalent_range": [
            round(float(np.min(row_background)), 3),
            round(float(np.max(row_background)), 3),
        ],
        "physical_border_tolerance_pixels": 0,
        "physical_border_semantics": (
            "deletion requires actual contact with the outermost column; "
            "one-pixel inset components, all 1-2px flush-edge components, and "
            "rule-like 3px+ marginal/page-frame geometry are preserved; tonal "
            "variation alone never distinguishes a scanner strip"
        ),
        "inspected_components": len(candidates),
        "candidates": candidates,
        "removed": removed,
        "left_edge_action": "none",
    }


def validate_canvas(width: int, height: int) -> None:
    if width <= 0 or height <= 0:
        raise ValueError("canvas dimensions must be positive")
    if width > MAX_CANVAS_WIDTH or height > MAX_CANVAS_HEIGHT:
        raise ValueError(
            "canvas dimensions exceed the fixed limit "
            f"{MAX_CANVAS_WIDTH}x{MAX_CANVAS_HEIGHT}: {width}x{height}"
        )
    pixels = width * height
    if pixels > MAX_CANVAS_PIXELS:
        raise ValueError(
            "canvas pixel budget exceeded "
            f"({pixels} pixels, limit {MAX_CANVAS_PIXELS})"
        )
    working_bytes = (
        pixels
        * MAX_GRAYSCALE_BYTES_PER_PIXEL
        * CANVAS_WORKING_RASTER_COPIES
    )
    if working_bytes > MAX_CANVAS_WORKING_BYTES:
        raise ValueError(
            "canvas working-memory budget exceeded "
            f"({working_bytes} bytes, limit {MAX_CANVAS_WORKING_BYTES})"
        )


def fitted_size(
    source_width: int,
    source_height: int,
    target_width: int,
    target_height: int,
) -> tuple[int, int]:
    scale = min(target_width / source_width, target_height / source_height)
    return (
        max(1, min(target_width, round(source_width * scale))),
        max(1, min(target_height, round(source_height * scale))),
    )


def processing_working_bytes(
    image: np.ndarray, target_width: int, target_height: int
) -> int:
    height, width = image.shape
    bytes_per_pixel = image.dtype.itemsize
    source_bytes = width * height * bytes_per_pixel
    resized_width, resized_height = fitted_size(
        width, height, target_width, target_height
    )
    resized_bytes = resized_width * resized_height * bytes_per_pixel
    canvas_bytes = target_width * target_height * bytes_per_pixel

    inspection_width = min(width, max(12, round(width * 0.04)))
    context_width = min(width, 4 * inspection_width)
    edge_workspace = (
        height * context_width * EDGE_CONTEXT_BYTES_PER_PIXEL
        + height * inspection_width * EDGE_BAND_BYTES_PER_PIXEL
        + height * EDGE_ROW_WORKING_BYTES
    )
    cleanup_peak = 2 * source_bytes + edge_workspace

    interpolation_workspace = max(source_bytes, resized_bytes)
    resize_peak = source_bytes + resized_bytes + interpolation_workspace
    padding_peak = source_bytes + resized_bytes + canvas_bytes
    encoding_peak = ENCODING_RASTER_COPIES * canvas_bytes
    return max(cleanup_peak, resize_peak, padding_peak, encoding_peak)


def header_processing_working_bytes(
    header: EncodedImageHeader, target_width: int, target_height: int
) -> int:
    pixels = header.width * header.height
    grayscale_source_bytes = pixels * header.grayscale_bytes_per_pixel
    resized_width, resized_height = fitted_size(
        header.width, header.height, target_width, target_height
    )
    resized_bytes = (
        resized_width * resized_height * header.grayscale_bytes_per_pixel
    )
    canvas_bytes = (
        target_width * target_height * header.grayscale_bytes_per_pixel
    )
    inspection_width = min(
        header.width, max(12, round(header.width * 0.04))
    )
    context_width = min(header.width, 4 * inspection_width)
    edge_workspace = (
        header.height * context_width * EDGE_CONTEXT_BYTES_PER_PIXEL
        + header.height * inspection_width * EDGE_BAND_BYTES_PER_PIXEL
        + header.height * EDGE_ROW_WORKING_BYTES
    )
    decode_peak = (
        header.encoded_bytes * DECODER_ENCODED_BUFFER_COPIES
        + min(header.encoded_bytes, INVENTORY_HEADER_BYTES)
        + pixels * header.decode_working_bytes_per_pixel
    )
    cleanup_peak = 2 * grayscale_source_bytes + edge_workspace
    interpolation_workspace = max(grayscale_source_bytes, resized_bytes)
    resize_peak = grayscale_source_bytes + resized_bytes + interpolation_workspace
    padding_peak = grayscale_source_bytes + resized_bytes + canvas_bytes
    encoding_peak = ENCODING_RASTER_COPIES * canvas_bytes
    return max(
        decode_peak, cleanup_peak, resize_peak, padding_peak, encoding_peak
    )


def validate_header_processing_memory(
    path: Path,
    header: EncodedImageHeader,
    target_width: int,
    target_height: int,
) -> None:
    working_bytes = header_processing_working_bytes(
        header, target_width, target_height
    )
    if working_bytes > MAX_CANVAS_WORKING_BYTES:
        raise ValueError(
            "page working-memory budget exceeded before decode "
            f"({working_bytes} bytes, limit {MAX_CANVAS_WORKING_BYTES}): {path}"
        )


def validate_processing_memory(
    image: np.ndarray, target_width: int, target_height: int
) -> None:
    if image.dtype not in (np.dtype(np.uint8), np.dtype(np.uint16)):
        raise ValueError(f"unsupported grayscale dtype {image.dtype}")
    working_bytes = processing_working_bytes(image, target_width, target_height)
    if working_bytes > MAX_CANVAS_WORKING_BYTES:
        raise ValueError(
            "page working-memory budget exceeded "
            f"({working_bytes} bytes, limit {MAX_CANVAS_WORKING_BYTES})"
        )


def normalize(image: np.ndarray, target_width: int, target_height: int) -> np.ndarray:
    validate_canvas(target_width, target_height)
    if image.ndim != 2:
        raise ValueError("normalization requires a two-dimensional grayscale raster")
    height, width = image.shape
    if width <= 0 or height <= 0:
        raise ValueError("source raster dimensions must be positive")
    if width > MAX_CANVAS_WIDTH or height > MAX_CANVAS_HEIGHT:
        raise ValueError(
            "source raster has an extreme aspect or axis beyond the fixed "
            f"{MAX_CANVAS_WIDTH}x{MAX_CANVAS_HEIGHT} resize preflight limit: "
            f"{width}x{height}"
        )
    if width * height > MAX_CANVAS_PIXELS:
        raise ValueError(
            "source raster pixel budget exceeded before resize "
            f"({width * height} pixels, limit {MAX_CANVAS_PIXELS})"
        )
    validate_processing_memory(image, target_width, target_height)
    scale = min(target_width / width, target_height / height)
    resized_width, resized_height = fitted_size(
        width, height, target_width, target_height
    )
    interpolation = cv2.INTER_AREA if scale < 1 else cv2.INTER_CUBIC
    resized = cv2.resize(image, (resized_width, resized_height), interpolation=interpolation)
    canvas = np.full(
        (target_height, target_width), np.iinfo(image.dtype).max, image.dtype
    )
    x = (target_width - resized_width) // 2
    y = (target_height - resized_height) // 2
    canvas[y:y + resized_height, x:x + resized_width] = resized
    return canvas


def disjoint_directories(input_dir: Path, output_dir: Path) -> bool:
    input_resolved = input_dir.resolve()
    output_resolved = output_dir.resolve()
    return (
        input_resolved != output_resolved
        and input_resolved not in output_resolved.parents
        and output_resolved not in input_resolved.parents
    )


def commit_transaction(staging: Path, output: Path) -> None:
    os.replace(staging, output)


def retained_size(value: object, seen: set[int] | None = None) -> int:
    if seen is None:
        seen = set()
    identity = id(value)
    if identity in seen:
        return 0
    seen.add(identity)
    size = sys.getsizeof(value)
    if isinstance(value, dict):
        size += sum(
            retained_size(key, seen) + retained_size(item, seen)
            for key, item in value.items()
        )
    elif isinstance(value, (list, tuple, set, frozenset)):
        size += sum(retained_size(item, seen) for item in value)
    return size


def main() -> int:
    parser = argparse.ArgumentParser(description="Clean scan edges and normalize page canvases.")
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--width", type=int)
    parser.add_argument("--height", type=int)
    parser.add_argument(
        "--auto-canvas",
        action="store_true",
        help="use the maximum selected source width and height",
    )
    parser.add_argument(
        "--pages",
        nargs="*",
        type=int,
        help="select the last numeric run in each filename stem",
    )
    parser.add_argument(
        "--edge-confidence",
        "--cleanup-confidence",
        dest="edge_confidence",
        type=float,
        default=0.9,
        help="minimum inclusive confidence for eligible right-edge cleanup (default: 0.9)",
    )
    parser.add_argument(
        "--no-edge-cleanup",
        action="store_true",
        help="analyze and report right-edge candidates without changing them",
    )
    args = parser.parse_args()

    if not args.input.is_dir():
        parser.error("input must be an existing directory")
    if not disjoint_directories(args.input, args.output):
        parser.error("input and output must be separate, non-nested directories")
    if not 0 <= args.edge_confidence <= 1:
        parser.error("--edge-confidence must be between 0 and 1")
    explicit_canvas = args.width is not None or args.height is not None
    if args.auto_canvas == explicit_canvas:
        parser.error("use either --auto-canvas or both --width and --height")
    if explicit_canvas and (args.width is None or args.height is None):
        parser.error("--width and --height must be supplied together")
    if explicit_canvas and (args.width < 100 or args.height < 100):
        parser.error("target dimensions must be at least 100x100")
    if explicit_canvas:
        try:
            validate_canvas(args.width, args.height)
        except ValueError as error:
            parser.error(str(error))

    top_level_entries = sorted(args.input.iterdir(), key=natural_key)
    if len(top_level_entries) > MAX_TOP_LEVEL_ENTRIES:
        parser.error(
            "top-level entry budget exceeded "
            f"({len(top_level_entries)} entries, limit {MAX_TOP_LEVEL_ENTRIES})"
        )
    inventory = []
    supported_paths: set[Path] = set()
    supported_headers: dict[Path, EncodedImageHeader] = {}
    aggregate_image_bytes = 0
    for path in top_level_entries:
        extension = path.suffix.lower()
        if not path.is_file():
            inventory.append(
                {
                    "name": path.name,
                    "extension": extension,
                    "classification": (
                        "directory" if path.is_dir() else "unsupported_entry"
                    ),
                    "detected_format": None,
                    "detected_frames": None,
                }
            )
            continue
        try:
            header = read_bounded_header(path)
            image_candidate = (
                extension in KNOWN_IMAGE_EXTENSIONS
                or has_image_container_signature(header)
                or probe_image_format(path, header) is not None
            )
            if image_candidate:
                file_size = path.stat().st_size
                if file_size > MAX_IMAGE_FILE_BYTES:
                    parser.error(
                        f"image candidate exceeds {MAX_IMAGE_FILE_BYTES} "
                        f"byte file budget: {path}"
                    )
                aggregate_image_bytes += file_size
                if aggregate_image_bytes > MAX_AGGREGATE_IMAGE_BYTES:
                    parser.error(
                        "aggregate image candidate byte budget exceeded "
                        f"({aggregate_image_bytes} bytes, "
                        f"limit {MAX_AGGREGATE_IMAGE_BYTES})"
                    )
            inspected = inspect_encoded_image(path, header)
        except (OSError, ValueError) as error:
            parser.error(f"cannot inspect top-level file {path}: {error}")
        detected_format = inspected.detected_format if inspected else None
        frame_count = inspected.frame_count if inspected else None
        detected_supported = (
            detected_format in SUPPORTED_IMAGE_FORMATS and frame_count == 1
        )
        if (
            extension in KNOWN_IMAGE_EXTENSIONS
            and extension not in SUPPORTED_IMAGE_EXTENSIONS
        ):
            classification = "unsupported_image"
        elif detected_supported:
            classification = "supported_image"
            try:
                assert inspected is not None
                validate_header_processing_memory(
                    path, inspected, inspected.width, inspected.height
                )
                raster = read_gray(path)
                decoded_width, decoded_height = raster.shape[1], raster.shape[0]
                validate_header_dimensions(path, decoded_width, decoded_height)
                inspected = inspected._replace(
                    width=decoded_width, height=decoded_height
                )
                validate_header_processing_memory(
                    path, inspected, decoded_width, decoded_height
                )
                del raster
                supported_paths.add(path)
                supported_headers[path] = inspected
            except (OSError, ValueError) as error:
                parser.error(str(error))
        elif (
            inspected is not None
            or extension in KNOWN_IMAGE_EXTENSIONS
            or image_candidate
        ):
            classification = "unsupported_image"
        else:
            classification = "non_image"
        inventory.append(
            {
                "name": path.name,
                "extension": extension,
                "classification": classification,
                "detected_format": detected_format,
                "detected_frames": frame_count,
            }
        )
    unsupported_images = [
        item
        for item in inventory
        if item["classification"] == "unsupported_image"
    ]
    if unsupported_images:
        parser.error(
            "unsupported image-like top-level file(s): "
            + ", ".join(
                (
                    f"{item['name']} ({item['extension'] or 'no extension'}); "
                    f"detected {item['detected_format'] or 'unknown'}"
                    + (
                        f", {item['detected_frames']} frames"
                        if item["detected_frames"] is not None
                        else ", unknown frame count"
                        if item["detected_format"]
                        else ""
                    )
                )
                for item in unsupported_images
            )
        )
    all_files = [path for path in top_level_entries if path in supported_paths]
    all_output_names = [path.stem.casefold() for path in all_files]
    if len(all_output_names) != len(set(all_output_names)):
        parser.error("duplicate input stems would collide when written as PNG")

    files = all_files
    if args.pages is not None:
        if not args.pages:
            parser.error("--pages requires at least one page number")
        if len(args.pages) != len(set(args.pages)):
            parser.error("--pages contains duplicate page numbers")
        pages_by_number: dict[int, list[Path]] = {}
        for path in all_files:
            number = page_number(path)
            if number is not None:
                pages_by_number.setdefault(number, []).append(path)
        absent = [number for number in args.pages if number not in pages_by_number]
        ambiguous = {
            number: [path.name for path in pages_by_number[number]]
            for number in args.pages
            if len(pages_by_number.get(number, [])) > 1
        }
        if absent:
            parser.error(
                "requested page number(s) absent: " + ", ".join(map(str, absent))
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

    if args.output.exists():
        parser.error("output directory must not exist")
    if args.auto_canvas:
        target_width = 0
        target_height = 0
        try:
            for path in files:
                inspected = supported_headers[path]
                target_width = max(target_width, inspected.width)
                target_height = max(target_height, inspected.height)
                validate_canvas(target_width, target_height)
        except (OSError, ValueError) as error:
            parser.error(str(error))
    else:
        target_width, target_height = args.width, args.height
    try:
        for path in files:
            validate_header_processing_memory(
                path, supported_headers[path], target_width, target_height
            )
    except ValueError as error:
        parser.error(str(error))

    args.output.parent.mkdir(parents=True, exist_ok=True)
    staging = args.output.parent / (
        f".{args.output.name}.scan-book-layout-{uuid.uuid4().hex}"
    )
    staging.mkdir()
    pages: list[dict[str, object]] = []
    total_removed = 0
    retained_edge_components = 0
    retained_edge_report_bytes = 0
    try:
        for index, path in enumerate(files, 1):
            image = read_gray(path)
            try:
                validate_processing_memory(image, target_width, target_height)
            except ValueError as error:
                parser.error(str(error))
            source_size = [image.shape[1], image.shape[0]]
            remaining_component_budget = (
                MAX_RETAINED_EDGE_COMPONENTS_BATCH - retained_edge_components
            )
            if remaining_component_budget < 0:
                remaining_component_budget = 0
            try:
                remaining_report_budget = max(
                    0,
                    MAX_RETAINED_EDGE_REPORT_BYTES_BATCH
                    - retained_edge_report_bytes,
                )
                cleaned, cleanup = clean_right_edge(
                    image,
                    args.edge_confidence,
                    enabled=not args.no_edge_cleanup,
                    max_reported_components=min(
                        MAX_RETAINED_EDGE_COMPONENTS_PER_PAGE,
                        remaining_component_budget,
                    ),
                    max_report_bytes=remaining_report_budget,
                )
            except ReviewRequiredError as error:
                parser.error(str(error))
            retained_edge_components += int(cleanup["inspected_components"])
            retained_edge_report_bytes += retained_size(cleanup)
            if retained_edge_report_bytes > MAX_RETAINED_EDGE_REPORT_BYTES_BATCH:
                parser.error(
                    "right-edge report memory budget exceeded "
                    f"({retained_edge_report_bytes} bytes, "
                    f"limit {MAX_RETAINED_EDGE_REPORT_BYTES_BATCH}); "
                    "all candidates require manual review and no cleanup was published"
                )
            total_removed += int(cleanup["removed"])
            output_path = staging / f"{path.stem}.png"
            del image
            normalized = normalize(cleaned, target_width, target_height)
            del cleaned
            write_png(output_path, normalized)
            del normalized
            pages.append(
                {
                    "input": path.name,
                    "output": output_path.relative_to(staging).as_posix(),
                    "source_size": source_size,
                    "cleanup": cleanup,
                }
            )
            print(f"processed {index}/{len(files)}: {path.name}", file=sys.stderr)

        staged_report = staging / "cleanup.json"
        report = {
            "processed": len(files),
            "output_format": "PNG",
            "output_paths_relative_to": "output_root",
            "output_root_from_report": ".",
            "canvas": [target_width, target_height],
            "canvas_mode": "auto" if args.auto_canvas else "explicit",
            "edge_cleanup_enabled": not args.no_edge_cleanup,
            "edge_confidence_threshold": args.edge_confidence,
            "edge_artifacts_removed": total_removed,
            "top_level_file_inventory": inventory,
            "pages": pages,
        }
        with staged_report.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(report, stream, indent=2)
            stream.write("\n")
        with staged_report.open("r", encoding="utf-8") as stream:
            shutil.copyfileobj(stream, sys.stdout, length=64 * 1024)
            sys.stdout.flush()
        commit_transaction(staging, args.output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
