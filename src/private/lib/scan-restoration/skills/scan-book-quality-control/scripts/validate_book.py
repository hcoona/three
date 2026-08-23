# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "numpy==2.2.6",
#   "opencv-python-headless==4.12.0.88",
#   "pillow==12.3.0",
# ]
# ///

from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import os
import re
import secrets
import stat
import struct
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageOps


EXTENSIONS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"}
ALLOWED_FORMATS = {
    ".jpg": {"JPEG"},
    ".jpeg": {"JPEG"},
    ".png": {"PNG"},
    ".tif": {"TIFF"},
    ".tiff": {"TIFF"},
    ".webp": {"WEBP"},
}
IMAGE_CANDIDATE_EXTENSIONS = EXTENSIONS | {
    ".avif", ".bmp", ".dib", ".gif", ".heic", ".heif", ".ico", ".j2k",
    ".jp2", ".pbm", ".pdf", ".pgm", ".pnm", ".ppm", ".psd", ".svg", ".svgz",
}
THRESHOLDS = {
    "foreground_minimum_contrast_gray": 10.0,
    "foreground_otsu_minimum_gray": 10.0,
    "foreground_otsu_maximum_gray": 72.0,
    "foreground_component_minimum_area_fraction": 0.00001,
    "foreground_component_minimum_span_fraction": 0.008,
    "fine_component_maximum_span_fraction": 0.004,
    "fine_component_maximum_area_pixels": 128,
    "fine_component_minimum_isolation_pixels": 1.0,
    "fine_damage_presence_minimum_contrast_gray": 5.0,
    "content_bounds_lower_quantile": 0.005,
    "content_bounds_upper_quantile": 0.995,
    "likely_blank_source_ink_fraction_below": 0.0002,
    "likely_blank_dirt_ink_fraction_below": 0.002,
    "likely_blank_dirt_maximum_component_span_fraction": 0.08,
    "likely_blank_raw_ink_fraction_below": 0.002,
    "likely_blank_fine_component_ink_fraction_below": 0.001,
    "likely_blank_fine_component_count_below": 64,
    "nonblank_minimum_ink_retention_ratio": 0.70,
    "nonblank_minimum_absolute_ink_loss_to_fail": 0.0002,
    "complete_erasure_output_ink_fraction_below": 0.00005,
    "blank_maximum_added_ink_fraction": 0.010,
    "border_outer_width_fraction": 0.02,
    "border_internal_band_start_fraction": 0.02,
    "border_internal_band_end_fraction": 0.08,
    "border_visual_review_dark_fraction": 0.04,
    "border_severe_dark_fraction": 0.18,
    "blank_border_severe_dark_fraction": 0.06,
    "border_minimum_dark_fraction_increase": 0.02,
    "border_minimum_long_axis_breadth": 0.45,
    "border_minimum_connected_component_fraction": 0.01,
    "border_minimum_connected_component_span": 0.20,
    "border_suspicious_dark_fraction_decrease": 0.08,
    "edge_component_search_depth_fraction": 0.12,
    "edge_component_minimum_length_fraction": 0.45,
    "edge_component_minimum_thickness_fraction": 0.004,
    "edge_component_minimum_fill_fraction": 0.60,
    "edge_component_minimum_zone_fraction": 0.01,
    "edge_frame_projection_minimum_occupancy_fraction": 0.45,
    "edge_frame_maximum_geometry_error_fraction": 0.025,
    "edge_frame_minimum_corner_support_fraction": 0.10,
    "edge_frame_maximum_retained_candidates": 16,
    "edge_frame_maximum_projection_candidates_per_side": 16,
    "edge_frame_maximum_combination_evaluations": 4096,
    "edge_component_minimum_zone_fraction_increase": 0.01,
    "edge_component_minimum_thickness_increase_fraction": 0.003,
    "edge_component_match_minimum_long_axis_iou": 0.50,
    "edge_component_match_maximum_cross_axis_distance_fraction": 0.03,
    "edge_component_match_maximum_thickness_ratio": 4.0,
    "geometry_minimum_hough_lines": 6,
    "geometry_residual_outlier_degrees": 0.50,
    "geometry_worsening_degrees": 0.30,
    "geometry_worsening_ratio": 1.50,
    "anisotropic_stretch_review_fraction": 0.02,
    "anisotropic_stretch_failure_fraction": 0.05,
    "content_similarity_review_below": 0.55,
    "content_similarity_failure_below": 0.22,
    "orientation_minimum_similarity": 0.55,
    "orientation_minimum_margin": 0.12,
    "orientation_corroborating_minimum_margin": 0.06,
    "orientation_corroborating_minimum_similarity": 0.60,
    "substitution_minimum_similarity": 0.72,
    "substitution_minimum_margin": 0.18,
    "perceptual_duplicate_similarity": 0.985,
    "decoded_pixel_duplicate_similarity": 0.995,
    "color_source_chroma_fraction_minimum": 0.01,
    "color_introduced_chroma_fraction_review_above": 0.01,
    "color_introduced_chroma_fraction_increase_review": 0.01,
    "color_chroma_retention_review_below": 0.55,
    "color_signature_similarity_review_below": 0.80,
    "color_spatial_signature_similarity_review_below": 0.80,
    "removed_edge_component_minimum_physical_edge_clearance_fraction": 0.02,
    "paper_brightness_drop_review_gray": 18.0,
    "paper_output_p90_review_below": 220.0,
    "paper_highlight_drop_review_gray": 18.0,
    "paper_highlight_range_minimum_source_gray": 12.0,
    "paper_highlight_range_retention_review_below": 0.45,
    "paper_dark_clip_ceiling_review_below": 240.0,
    "paper_dark_clip_fraction_review_above": 0.20,
    "paper_dark_clip_fraction_increase_review": 0.15,
    "paper_local_unevenness_review_gray": 18.0,
    "paper_local_unevenness_increase_review_gray": 10.0,
    "paper_color_cast_review_lab_chroma": 8.0,
    "paper_color_cast_increase_review_lab_chroma": 5.0,
    "paper_extreme_dark_p90_failure_below": 100.0,
    "paper_extreme_dark_drop_failure_gray": 80.0,
    "paper_extreme_unevenness_failure_gray": 90.0,
    "paper_extreme_unevenness_increase_failure_gray": 60.0,
    "paper_extreme_color_cast_failure_lab_chroma": 45.0,
    "paper_extreme_color_cast_increase_failure_lab_chroma": 35.0,
    "dpi_change_review_fraction": 0.02,
    "dpi_change_failure_fraction": 0.10,
    "physical_size_change_review_fraction": 0.02,
    "physical_size_change_failure_fraction": 0.05,
    "projection_landmark_minimum_span_fraction": 0.10,
    "projection_landmark_maximum_fit_error": 0.050,
    "foreground_registration_review_fraction": 0.02,
    "registration_component_minimum_match_count": 2,
    "registration_component_minimum_spatial_span_fraction": 0.15,
    "registration_component_maximum_scale_deviation_fraction": 0.12,
    "registration_component_maximum_offset_deviation_fraction": 0.06,
    "registration_canvas_verification_minimum_coverage": 0.85,
}
SAFETY_BUDGET_DEFAULTS = {
    "maximum_page_count_per_side": 500,
    "maximum_decoded_pixels_per_page": 50_000_000,
    "maximum_total_compact_feature_pixels": 2_000_000_000,
    "maximum_retained_feature_bytes": 256 * 1024 * 1024,
    "maximum_peak_encoded_buffer_bytes": 1_000_000_000,
    "maximum_cross_match_comparisons": 250_000,
    "maximum_duplicate_comparisons": 125_000,
    "maximum_components_per_extraction": 100_000,
    "maximum_component_match_comparisons": 250_000,
    "maximum_inventory_entries_per_side": 10_000,
    "maximum_inventory_recursion_depth": 32,
    "maximum_inventory_total_bytes_hashed_per_side": 10_000_000_000,
    "maximum_inventory_file_bytes": 1_000_000_000,
}
MAXIMUM_MANIFEST_JSON_BYTES = 4 * 1024 * 1024
MAXIMUM_APPROVAL_JSON_BYTES = 4 * 1024 * 1024
MAXIMUM_EVIDENCE_JSON_BYTES = 64 * 1024 * 1024
MAXIMUM_FINAL_JSON_BYTES = 4 * 1024 * 1024
MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT = 3
MAXIMUM_RETAINED_DUPLICATE_DECISIONS = 256
MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES = 8
MAXIMUM_FINE_COMPONENTS_RETAINED = 4096
MAXIMUM_FINE_DAMAGE_EXAMPLES = 8


class DecodedPixelBudgetError(ValueError):
    pass


class ComponentBudgetError(ValueError):
    pass


ACTIVE_COMPONENT_BUDGETS = {
    "maximum_components_per_extraction": SAFETY_BUDGET_DEFAULTS[
        "maximum_components_per_extraction"
    ],
    "maximum_component_match_comparisons": SAFETY_BUDGET_DEFAULTS[
        "maximum_component_match_comparisons"
    ],
}


def preflight_component_complexity(mask: np.ndarray, context: str) -> None:
    maximum = ACTIVE_COMPONENT_BUDGETS["maximum_components_per_extraction"]
    maximum_sample_pixels = 1_000_000
    height, width = mask.shape
    if mask.size <= maximum_sample_pixels:
        component_count = (
            int(
                cv2.connectedComponents(
                    mask.astype(np.uint8, copy=False), connectivity=8
                )[0]
            )
            - 1
        )
        if component_count <= maximum:
            return
        raise ComponentBudgetError(
            "connected-component extraction safety budget exceeded during "
            "bounded complexity preflight: "
            f"context={context}, components={component_count}, limit={maximum}, "
            f"source_size={width}x{height}"
        )

    columns_per_tile = min(width, maximum_sample_pixels)
    maximum_retained_row_runs = max(maximum, 4096)

    def row_runs(y: int) -> list[tuple[int, int]]:
        starts: list[int] = []
        ends: list[int] = []
        left = False
        for x in range(0, width, columns_per_tile):
            x_end = min(width, x + columns_per_tile)
            tile = mask[y, x:x_end] != 0
            previous = np.empty(tile.size, dtype=bool)
            previous[0] = left
            previous[1:] = tile[:-1]
            following = np.empty(tile.size, dtype=bool)
            following[:-1] = tile[1:]
            following[-1] = bool(mask[y, x_end] != 0) if x_end < width else False
            starts.extend((np.flatnonzero(tile & ~previous) + x).tolist())
            ends.extend((np.flatnonzero(tile & ~following) + x).tolist())
            left = bool(tile[-1])
            if len(starts) > maximum_retained_row_runs:
                raise ComponentBudgetError(
                    "connected-component extraction safety budget exceeded during "
                    "bounded complexity preflight: "
                    f"context={context}, row_runs={len(starts)}, limit={maximum}, "
                    f"retained_row_run_limit={maximum_retained_row_runs}, "
                    f"streaming_tile_pixels={maximum_sample_pixels}, "
                    f"source_size={width}x{height}"
                )
        return list(zip(starts, ends))

    completed_components = 0
    previous_runs: list[tuple[int, int, int]] = []
    for y in range(height):
        current_ranges = row_runs(y)
        parent: dict[int, int] = {
            label: label for _, _, label in previous_runs
        }

        def find(label: int) -> int:
            root = label
            while parent[root] != root:
                root = parent[root]
            while parent[label] != label:
                following = parent[label]
                parent[label] = root
                label = following
            return root

        def union(left_label: int, right_label: int) -> int:
            left_root = find(left_label)
            right_root = find(right_label)
            if left_root != right_root:
                parent[right_root] = left_root
            return left_root

        current_runs: list[tuple[int, int, int]] = []
        previous_index = 0
        next_label = max(parent, default=-1) + 1
        for start, end in current_ranges:
            while (
                previous_index < len(previous_runs)
                and previous_runs[previous_index][1] < start - 1
            ):
                previous_index += 1
            overlap_index = previous_index
            overlaps: list[int] = []
            while (
                overlap_index < len(previous_runs)
                and previous_runs[overlap_index][0] <= end + 1
            ):
                overlaps.append(previous_runs[overlap_index][2])
                overlap_index += 1
            if overlaps:
                label = overlaps[0]
                for overlap in overlaps[1:]:
                    label = union(label, overlap)
            else:
                label = next_label
                parent[label] = label
                next_label += 1
            current_runs.append((start, end, label))

        previous_roots = {find(label) for _, _, label in previous_runs}
        current_roots = {find(label) for _, _, label in current_runs}
        completed_components += len(previous_roots - current_roots)
        compact_labels = {
            root: index for index, root in enumerate(sorted(current_roots))
        }
        previous_runs = [
            (start, end, compact_labels[find(label)])
            for start, end, label in current_runs
        ]
        if completed_components > maximum:
            raise ComponentBudgetError(
                "connected-component extraction safety budget exceeded during "
                "bounded complexity preflight: "
                f"context={context}, completed_components={completed_components}, "
                f"active_components={len(compact_labels)}, limit={maximum}, "
                f"streaming_tile_pixels={maximum_sample_pixels}, "
                f"source_size={width}x{height}"
            )

    estimated_components = completed_components + len(
        {label for _, _, label in previous_runs}
    )
    if estimated_components > maximum:
        raise ComponentBudgetError(
            "connected-component extraction safety budget exceeded during "
            "bounded complexity preflight: "
            f"context={context}, estimated_components={estimated_components}, "
            f"limit={maximum}, "
            f"streaming_tile_pixels={maximum_sample_pixels}, "
            f"source_size={width}x{height}"
        )


def connected_component_stats(
    mask: np.ndarray, context: str
) -> tuple[int, np.ndarray, np.ndarray, np.ndarray]:
    preflight_component_complexity(mask, context)
    result = cv2.connectedComponentsWithStats(
        mask.astype(np.uint8, copy=False), connectivity=8
    )
    component_count = int(result[0]) - 1
    maximum = ACTIVE_COMPONENT_BUDGETS["maximum_components_per_extraction"]
    if component_count > maximum:
        raise ComponentBudgetError(
            "connected-component extraction safety budget exceeded: "
            f"context={context}, components={component_count}, limit={maximum}"
        )
    return result


@dataclass
class ImmutableFile:
    path: Path
    data: bytearray
    snapshot: dict[str, object]

    def close(self) -> None:
        pass


@dataclass(frozen=True)
class PublishedJSON:
    path: Path
    size: int
    sha256: str


class BoundedMemoryReader:
    def __init__(self, data: bytes | bytearray | memoryview):
        self._data = memoryview(data).cast("B")
        self._position = 0
        self._closed = False

    def read(self, size: int = -1) -> bytes:
        if self._closed:
            raise ValueError("I/O operation on closed file")
        remaining = max(0, len(self._data) - self._position)
        count = remaining if size is None or size < 0 else min(size, remaining)
        start = self._position
        self._position += count
        return self._data[start : start + count].tobytes()

    def readinto(self, buffer: object) -> int:
        if self._closed:
            raise ValueError("I/O operation on closed file")
        target = memoryview(buffer).cast("B")
        count = min(len(target), max(0, len(self._data) - self._position))
        target[:count] = self._data[self._position : self._position + count]
        self._position += count
        return count

    def seek(self, offset: int, whence: int = os.SEEK_SET) -> int:
        if self._closed:
            raise ValueError("I/O operation on closed file")
        origin = (
            0
            if whence == os.SEEK_SET
            else self._position
            if whence == os.SEEK_CUR
            else len(self._data)
            if whence == os.SEEK_END
            else None
        )
        if origin is None:
            raise ValueError(f"invalid whence: {whence}")
        position = origin + offset
        if position < 0:
            raise ValueError("negative seek position")
        self._position = position
        return position

    def tell(self) -> int:
        return self._position

    def readable(self) -> bool:
        return not self._closed

    def seekable(self) -> bool:
        return not self._closed

    def close(self) -> None:
        if not self._closed:
            self._closed = True
            self._data.release()

    def __enter__(self) -> "BoundedMemoryReader":
        return self

    def __exit__(self, *args: object) -> None:
        self.close()


def estimated_peak_encoded_buffer_bytes(encoded_bytes: int) -> int:
    if encoded_bytes < 0:
        raise ValueError("encoded byte count must be nonnegative")
    # Retain one exact source buffer and reserve one full-size transient read for
    # decoders that don't consistently use readinto().
    return encoded_bytes * 2


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    document: dict[str, object] = {}
    for key, value in pairs:
        if key in document:
            raise ValueError(f"duplicate JSON key: {key}")
        document[key] = value
    return document


def _capture_regular_file(
    path: Path, maximum_bytes: int, description: str = "JSON file"
) -> ImmutableFile:
    absolute = Path(os.path.abspath(path))
    if (
        absolute.is_symlink()
        or is_reparse_point(absolute)
        or traverses_reparse_point(absolute)
    ):
        raise ValueError(f"file must be a regular non-reparse file: {path}")
    before = absolute.stat()
    if not stat.S_ISREG(before.st_mode):
        raise ValueError(f"file must be regular: {path}")
    if before.st_size > maximum_bytes:
        raise ValueError(
            f"{description} exceeds safety budget: {path} has {before.st_size} bytes, "
            f"limit is {maximum_bytes}"
        )
    with absolute.open("rb") as stream:
        data = bytearray(stream.read(maximum_bytes + 1))
    if len(data) > maximum_bytes:
        raise ValueError(
            f"{description} exceeds safety budget while reading: {path}, "
            f"limit is {maximum_bytes}"
        )
    after = absolute.stat()
    if (
        not stat.S_ISREG(after.st_mode)
        or before.st_size != after.st_size
        or before.st_mtime_ns != after.st_mtime_ns
        or len(data) != after.st_size
    ):
        raise ValueError(f"file mutated while being captured: {path}")
    snapshot = {
        "safe_regular_file": True,
        "size": len(data),
        "mtime_ns": int(after.st_mtime_ns),
        "sha256": hashlib.sha256(data).hexdigest(),
    }
    return ImmutableFile(absolute, data, snapshot)


def capture_regular_file(path: Path, maximum_bytes: int) -> ImmutableFile:
    return _capture_regular_file(path, maximum_bytes)


def capture_image_file(path: Path, maximum_bytes: int) -> ImmutableFile:
    return _capture_regular_file(path, maximum_bytes, "image file")


def parse_json_bytes(data: bytes | bytearray, path: Path) -> object:
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError(f"JSON file is not valid UTF-8: {path}") from error
    return json.loads(text, object_pairs_hook=reject_duplicate_keys)


def load_json_file(path: Path, maximum_bytes: int) -> object:
    captured = capture_regular_file(path, maximum_bytes)
    try:
        return parse_json_bytes(captured.data, path)
    finally:
        captured.close()


def captured_file_still_published(captured: ImmutableFile) -> bool:
    return _captured_file_still_published(captured)


def image_capture_still_published(captured: ImmutableFile) -> bool:
    return _captured_file_still_published(captured)


def _captured_file_still_published(captured: ImmutableFile) -> bool:
    try:
        current = regular_file_snapshot(
            captured.path, int(captured.snapshot["size"])
        )
    except (OSError, ValueError):
        return False
    return (
        current.get("safe_regular_file") is True
        and current.get("size") == captured.snapshot["size"]
        and current.get("sha256") == captured.snapshot["sha256"]
    )


def close_immutable_files(*captured_files: ImmutableFile | None) -> None:
    for captured in captured_files:
        if captured is not None:
            captured.close()


def natural_key(path: Path) -> tuple[object, ...]:
    return tuple(
        int(part) if part.isdigit() else part.casefold()
        for part in re.split(r"(\d+)", path.name)
    )


def sha256_file(path: Path, maximum_bytes: int | None = None) -> str:
    if maximum_bytes is not None:
        size = path.stat().st_size
        if size > maximum_bytes:
            raise ValueError(
                f"file exceeds hashing safety budget: {path} has {size} bytes, "
                f"limit is {maximum_bytes}"
            )
    digest = hashlib.sha256()
    bytes_hashed = 0
    with path.open("rb") as stream:
        while True:
            read_size = 1024 * 1024
            if maximum_bytes is not None:
                read_size = min(read_size, maximum_bytes + 1 - bytes_hashed)
            chunk = stream.read(read_size)
            if not chunk:
                break
            bytes_hashed += len(chunk)
            if maximum_bytes is not None and bytes_hashed > maximum_bytes:
                raise ValueError(
                    f"file exceeds hashing safety budget while hashing: {path}, "
                    f"limit is {maximum_bytes}"
                )
            digest.update(chunk)
    return digest.hexdigest()


def read_immutable_file(
    path: Path,
    maximum_bytes: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_file_bytes"],
) -> bytearray:
    try:
        captured = capture_image_file(path, maximum_bytes)
    except OSError as error:
        raise ValueError(f"cannot read image bytes: {path}: {error}") from error
    try:
        if not captured.data:
            raise ValueError(f"empty or missing image: {path}")
        return captured.data
    finally:
        captured.close()


def looks_like_image(path: Path) -> bool:
    if path.suffix.lower() in IMAGE_CANDIDATE_EXTENSIONS:
        return True
    try:
        with path.open("rb") as stream:
            header = stream.read(4096)
    except (OSError, ValueError):
        return False
    return (
        header.startswith((b"\x89PNG\r\n\x1a\n", b"\xff\xd8\xff", b"GIF8", b"BM", b"II*\x00", b"MM\x00*"))
        or (header.startswith(b"RIFF") and header[8:12] == b"WEBP")
        or header.startswith(b"\x00\x00\x00\x0cjP  \r\n\x87\n")
        or (header[4:8] == b"ftyp" and header[8:12] in {b"avif", b"heic", b"heif", b"mif1"})
        or header.startswith((b"%PDF-", b"8BPS"))
        or b"<svg" in header.lstrip(b"\xef\xbb\xbf \t\r\n").lower()
    )


def path_status(path: Path) -> dict[str, bool]:
    return {
        "is_symlink": path.is_symlink(),
        "is_reparse_point": is_reparse_point(path),
    }


def candidate_inventory(
    directory: Path,
    maximum_pages: int = SAFETY_BUDGET_DEFAULTS["maximum_page_count_per_side"],
    maximum_entries: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_entries_per_side"],
    maximum_depth: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_recursion_depth"],
    maximum_total_bytes_hashed: int = SAFETY_BUDGET_DEFAULTS[
        "maximum_inventory_total_bytes_hashed_per_side"
    ],
    maximum_file_bytes: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_file_bytes"],
) -> dict[str, object]:
    top_level: list[dict[str, object]] = []
    unsupported: list[dict[str, object]] = []
    nested: list[dict[str, object]] = []
    unrecognized: list[dict[str, object]] = []
    all_entries: list[dict[str, object]] = []
    budget_failures: list[str] = []
    page_count_rejected = False
    supported_images_seen = 0
    planned_bytes_to_hash = 0
    if not directory.is_dir():
        return {
            "top_level_files": top_level,
            "unsupported_image_candidates": unsupported,
            "unrecognized_files": unrecognized,
            "nested_entries": nested,
            "all_entries": all_entries,
            "budget": {
                "entries_seen": 0,
                "maximum_depth_seen": 0,
                "bytes_hashed": 0,
                "rejected": False,
                "failures": [],
            },
            "page_count": {
                "supported_images_seen": 0,
                "rejected": False,
            },
        }
    pending = [(os.scandir(directory), 0)]
    maximum_depth_seen = 0
    abort_enumeration = False
    try:
        while pending and not abort_enumeration:
            iterator, depth = pending[-1]
            try:
                entry = next(iterator)
            except StopIteration:
                iterator.close()
                pending.pop()
                continue
            maximum_depth_seen = max(maximum_depth_seen, depth)
            if len(all_entries) >= maximum_entries:
                budget_failures.append(
                    f"inventory entry-count safety budget exceeded: limit={maximum_entries}"
                )
                break
            path = Path(entry.path)
            relative = path.relative_to(directory)
            status = path_status(path)
            linked = status["is_symlink"] or status["is_reparse_point"]
            if not linked and entry.is_dir(follow_symlinks=False):
                evidence = {
                    "path": str(relative),
                    "kind": "directory",
                    **status,
                }
                if depth >= maximum_depth:
                    budget_failures.append(
                        "inventory recursion-depth safety budget exceeded: "
                        f"path={relative}, depth={depth + 1}, limit={maximum_depth}"
                    )
                    abort_enumeration = True
                else:
                    pending.append((os.scandir(path), depth + 1))
            elif not linked and entry.is_file(follow_symlinks=False):
                file_status = path.stat(follow_symlinks=False)
                file_bytes = int(file_status.st_size)
                suffix = path.suffix.lower()
                top_level_supported = path.parent == directory and suffix in EXTENSIONS
                if top_level_supported:
                    supported_images_seen += 1
                    if supported_images_seen > maximum_pages:
                        page_count_rejected = True
                        abort_enumeration = True
                hash_allowed = True
                if file_bytes > maximum_file_bytes:
                    budget_failures.append(
                        "inventory per-file hashing safety budget exceeded: "
                        f"path={relative}, bytes={file_bytes}, limit={maximum_file_bytes}"
                    )
                    hash_allowed = False
                    abort_enumeration = True
                elif planned_bytes_to_hash + file_bytes > maximum_total_bytes_hashed:
                    budget_failures.append(
                        "inventory total hashing safety budget exceeded before full hashing: "
                        f"path={relative}, planned={planned_bytes_to_hash + file_bytes}, "
                        f"limit={maximum_total_bytes_hashed}"
                    )
                    hash_allowed = False
                    abort_enumeration = True
                if suffix in EXTENSIONS:
                    classification = "supported_image"
                elif suffix in IMAGE_CANDIDATE_EXTENSIONS:
                    classification = "unsupported_image"
                elif hash_allowed and not page_count_rejected and looks_like_image(path):
                    classification = "unsupported_image"
                else:
                    classification = "unrecognized"
                evidence = {
                    "path": str(relative),
                    "kind": "file",
                    "classification": classification,
                    "bytes": file_bytes,
                    "sha256": None,
                    "hash_skipped_by_safety_budget": not hash_allowed,
                    "file_identity": {
                        "device": int(file_status.st_dev),
                        "inode": int(file_status.st_ino),
                        "link_count": int(file_status.st_nlink),
                    },
                    **status,
                }
                if hash_allowed:
                    planned_bytes_to_hash += file_bytes
            else:
                evidence = {"path": str(relative), "kind": "other", **status}
            all_entries.append(evidence)
            if evidence["kind"] == "directory" or path.parent != directory or linked:
                nested.append(evidence)
            elif evidence["kind"] == "file":
                top_level.append(evidence)
                if evidence["classification"] == "unsupported_image":
                    unsupported.append(evidence)
                elif evidence["classification"] == "unrecognized":
                    unrecognized.append(evidence)
    finally:
        for iterator, _ in pending:
            iterator.close()
    bytes_hashed = 0
    if not budget_failures and not page_count_rejected:
        for evidence in all_entries:
            if evidence.get("kind") != "file":
                continue
            path = directory / str(evidence["path"])
            evidence["sha256"] = sha256_file(path, maximum_file_bytes)
            evidence["hash_skipped_by_safety_budget"] = False
            bytes_hashed += int(evidence["bytes"])
    else:
        for evidence in all_entries:
            if evidence.get("kind") == "file":
                evidence["hash_skipped_by_safety_budget"] = True
    sort_key = lambda item: natural_key(Path(str(item["path"])))
    return {
        "top_level_files": sorted(top_level, key=sort_key),
        "unsupported_image_candidates": sorted(unsupported, key=sort_key),
        "unrecognized_files": sorted(unrecognized, key=sort_key),
        "nested_entries": sorted(nested, key=sort_key),
        "all_entries": sorted(all_entries, key=sort_key),
        "nested_image_candidates": sorted(
            [
                item
                for item in nested
                if item.get("classification") in {"supported_image", "unsupported_image"}
            ],
            key=sort_key,
        ),
        "budget": {
            "entries_seen": len(all_entries),
            "maximum_depth_seen": maximum_depth_seen,
            "bytes_hashed": bytes_hashed,
            "rejected": bool(budget_failures),
            "failures": budget_failures,
        },
        "page_count": {
            "supported_images_seen": supported_images_seen,
            "rejected": page_count_rejected,
        },
    }


def cross_tree_file_identity_aliases(
    inventories: dict[str, dict[str, list[dict[str, object]]]]
) -> list[dict[str, str]]:
    input_identities: dict[tuple[int, int], str] = {}
    for item in inventories["input"]["all_entries"]:
        identity = item.get("file_identity")
        if item.get("kind") == "file" and isinstance(identity, dict):
            input_identities[(int(identity["device"]), int(identity["inode"]))] = str(item["path"])
    aliases: list[dict[str, str]] = []
    for item in inventories["output"]["all_entries"]:
        identity = item.get("file_identity")
        if item.get("kind") != "file" or not isinstance(identity, dict):
            continue
        key = (int(identity["device"]), int(identity["inode"]))
        if key in input_identities:
            aliases.append(
                {"input": input_identities[key], "output": str(item["path"])}
            )
    return aliases


def page_identity(path: Path) -> tuple[int, ...] | None:
    numbers = tuple(int(value) for value in re.findall(r"\d+", path.stem))
    return numbers or None


def load_pairing_manifest(
    manifest_path: Path,
    inputs: list[Path],
    outputs: list[Path],
    captured: ImmutableFile | None = None,
) -> dict[str, object]:
    issues: list[str] = []
    pairs: list[tuple[Path, Path]] = []
    owned_capture = captured is None
    try:
        if captured is None:
            captured = capture_regular_file(
                manifest_path, MAXIMUM_MANIFEST_JSON_BYTES
            )
        document = parse_json_bytes(captured.data, manifest_path)
        entries = document.get("pairs") if isinstance(document, dict) else None
        if not isinstance(entries, list):
            raise ValueError("root must contain a pairs array")
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return {
            "strategy": "explicit pairing manifest validation failed",
            "positional_pairing": False,
            "issues": [f"cannot read pairing manifest: {error}"],
            "pairs": [],
            "manifest_sha256": (
                captured.snapshot["sha256"]
                if captured is not None
                else None
            ),
        }
    finally:
        if owned_capture and captured is not None:
            captured.close()
    input_by_name = {path.name: path for path in inputs}
    output_by_name = {path.name: path for path in outputs}
    seen_inputs: list[str] = []
    seen_outputs: list[str] = []
    for index, entry in enumerate(entries, start=1):
        if not isinstance(entry, dict):
            issues.append(f"manifest pair {index} must be an object")
            continue
        input_name = entry.get("input")
        output_name = entry.get("output")
        if (
            not isinstance(input_name, str)
            or Path(input_name).name != input_name
            or not isinstance(output_name, str)
            or Path(output_name).name != output_name
        ):
            issues.append(f"manifest pair {index} must use exact top-level filenames")
            continue
        seen_inputs.append(input_name)
        seen_outputs.append(output_name)
        if input_name not in input_by_name:
            issues.append(f"manifest input does not exist: {input_name}")
        if output_name not in output_by_name:
            issues.append(f"manifest output does not exist: {output_name}")
        if input_name in input_by_name and output_name in output_by_name:
            pairs.append((input_by_name[input_name], output_by_name[output_name]))
    duplicate_inputs = sorted(name for name, count in Counter(seen_inputs).items() if count > 1)
    duplicate_outputs = sorted(name for name, count in Counter(seen_outputs).items() if count > 1)
    if duplicate_inputs:
        issues.append(f"manifest repeats inputs: {duplicate_inputs}")
    if duplicate_outputs:
        issues.append(f"manifest repeats outputs: {duplicate_outputs}")
    missing_inputs = sorted(set(input_by_name) - set(seen_inputs))
    missing_outputs = sorted(set(output_by_name) - set(seen_outputs))
    if missing_inputs:
        issues.append(f"manifest omits inputs: {missing_inputs}")
    if missing_outputs:
        issues.append(f"manifest omits outputs: {missing_outputs}")
    return {
        "strategy": "exact explicit pairing manifest",
        "positional_pairing": False,
        "issues": issues,
        "pairs": pairs if not issues else [],
        "manifest_sha256": captured.snapshot["sha256"],
    }


def pairing_manifest_inventory(
    manifest_path: Path | None,
    maximum_file_bytes: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_file_bytes"],
    captured: ImmutableFile | None = None,
) -> dict[str, object] | None:
    if manifest_path is None:
        return None
    absolute = Path(os.path.abspath(manifest_path))
    entry: dict[str, object] = {
        "path": str(absolute),
        "exists": os.path.lexists(absolute),
        **path_status(absolute),
    }
    if captured is not None:
        snapshot = captured.snapshot
        entry.update(
            {
                "kind": "file",
                "bytes": snapshot["size"],
                "mtime_ns": snapshot["mtime_ns"],
                "sha256": snapshot["sha256"],
            }
        )
    elif (
        entry["exists"]
        and absolute.is_file()
        and not entry["is_symlink"]
        and not entry["is_reparse_point"]
        and not traverses_reparse_point(absolute)
    ):
        status = absolute.stat()
        effective_maximum_bytes = min(
            maximum_file_bytes, MAXIMUM_MANIFEST_JSON_BYTES
        )
        if status.st_size > effective_maximum_bytes:
            entry.update(
                {
                    "kind": "budget_rejected",
                    "bytes": int(status.st_size),
                    "maximum_json_bytes": effective_maximum_bytes,
                    "sha256": None,
                    "hash_skipped_by_safety_budget": True,
                }
            )
            return entry
        entry.update(
            {
                "kind": "file",
                "bytes": int(status.st_size),
                "mtime_ns": int(status.st_mtime_ns),
                "sha256": sha256_file(absolute, effective_maximum_bytes),
            }
        )
    else:
        entry["kind"] = "unsafe_or_missing"
    return entry


def pair_pages(
    inputs: list[Path],
    outputs: list[Path],
    manifest_path: Path | None = None,
    manifest_capture: ImmutableFile | None = None,
) -> dict[str, object]:
    if manifest_path is not None:
        return load_pairing_manifest(
            manifest_path, inputs, outputs, manifest_capture
        )
    input_keys = [page_identity(path) for path in inputs]
    output_keys = [page_identity(path) for path in outputs]
    if all(key is None for key in input_keys + output_keys):
        return {
            "strategy": "identity unavailable; explicit pairing manifest required",
            "positional_pairing": False,
            "issues": [
                "nonnumeric filenames require a pairing manifest "
                "(--pairing-manifest); positional pairing is forbidden"
            ],
            "pairs": [],
            "manifest_sha256": None,
        }

    issues: list[str] = []
    input_counts = Counter(input_keys)
    output_counts = Counter(output_keys)
    if any(key is None for key in input_keys):
        issues.append("some input filenames lack numeric identity")
    if any(key is None for key in output_keys):
        issues.append("some output filenames lack numeric identity")
    duplicate_inputs = sorted(key for key, count in input_counts.items() if key is not None and count > 1)
    duplicate_outputs = sorted(key for key, count in output_counts.items() if key is not None and count > 1)
    if duplicate_inputs:
        issues.append(f"duplicate input numeric identities: {duplicate_inputs}")
    if duplicate_outputs:
        issues.append(f"duplicate output numeric identities: {duplicate_outputs}")
    input_set = {key for key in input_keys if key is not None}
    output_set = {key for key in output_keys if key is not None}
    missing_outputs = sorted(input_set - output_set)
    unexpected_outputs = sorted(output_set - input_set)
    if missing_outputs:
        issues.append(f"numeric identities missing from output: {missing_outputs}")
    if unexpected_outputs:
        issues.append(f"numeric identities only in output: {unexpected_outputs}")

    if not issues and len(inputs) == len(outputs):
        output_by_key = dict(zip(output_keys, outputs))
        return {
            "strategy": "unique numeric filename identity, naturally ordered by input",
            "positional_pairing": False,
            "issues": [],
            "pairs": [(path, output_by_key[page_identity(path)]) for path in inputs],
            "manifest_sha256": None,
        }

    safe_output_by_key = {
        key: path
        for key, path in zip(output_keys, outputs)
        if key is not None and output_counts[key] == 1 and input_counts[key] == 1
    }
    safe_pairs = [
        (path, safe_output_by_key[key])
        for key, path in zip(input_keys, inputs)
        if key is not None and key in safe_output_by_key
    ]
    return {
        "strategy": "numeric identity validation failed; only unambiguous identity matches compared",
        "positional_pairing": False,
        "issues": issues,
        "pairs": safe_pairs,
        "manifest_sha256": None,
    }


def png_encoded_metadata(data: bytes | bytearray | memoryview) -> dict[str, object]:
    with BoundedMemoryReader(data) as stream:
        signature = stream.read(8)
        length_data = stream.read(4)
        chunk_type = stream.read(4)
        ihdr = stream.read(13)
        if (
            signature != b"\x89PNG\r\n\x1a\n"
            or len(length_data) != 4
            or struct.unpack(">I", length_data)[0] != 13
            or chunk_type != b"IHDR"
            or len(ihdr) != 13
        ):
            raise ValueError("invalid PNG IHDR")
        _, _, bit_depth, color_type, _, _, _ = struct.unpack(">IIBBBBB", ihdr)
        channel_map = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
        if color_type not in channel_map:
            raise ValueError(f"unsupported PNG color type: {color_type}")
        has_trns = False
        physical_resolution: dict[str, object] | None = None
        stream.read(4)
        while True:
            length_data = stream.read(4)
            chunk_type = stream.read(4)
            if len(length_data) != 4 or len(chunk_type) != 4:
                break
            length = struct.unpack(">I", length_data)[0]
            if chunk_type == b"tRNS":
                has_trns = True
            if chunk_type == b"pHYs" and length == 9:
                data = stream.read(length)
                x_ppm, y_ppm, unit = struct.unpack(">IIB", data)
                physical_resolution = {
                    "source": "PNG pHYs",
                    "raw_x_pixels_per_unit": int(x_ppm),
                    "raw_y_pixels_per_unit": int(y_ppm),
                    "unit": "meter" if unit == 1 else "unknown",
                    "reliable": unit == 1 and x_ppm > 0 and y_ppm > 0,
                    "dpi_x": float(x_ppm * 0.0254) if unit == 1 and x_ppm > 0 else None,
                    "dpi_y": float(y_ppm * 0.0254) if unit == 1 and y_ppm > 0 else None,
                }
                stream.seek(4, os.SEEK_CUR)
                if chunk_type in {b"IDAT", b"IEND"}:
                    break
                continue
            stream.seek(length + 4, os.SEEK_CUR)
            if chunk_type in {b"IDAT", b"IEND"}:
                break
    return {
        "source": "PNG IHDR/chunks",
        "bit_depth_bits": int(bit_depth),
        "channel_count": channel_map[color_type],
        "color_type": int(color_type),
        "has_trns": has_trns,
        "has_encoded_alpha": color_type in {4, 6} or has_trns,
        "physical_resolution": physical_resolution or {
            "source": None,
            "unit": None,
            "reliable": False,
            "dpi_x": None,
            "dpi_y": None,
        },
    }


def rational_float(value: object) -> float | None:
    try:
        result = float(value)
    except (TypeError, ValueError, ZeroDivisionError):
        return None
    return result if result > 0 and np.isfinite(result) else None


def tagged_resolution(
    x_value: object, y_value: object, unit_value: object, source: str
) -> dict[str, object]:
    x = rational_float(x_value)
    y = rational_float(y_value)
    try:
        unit = int(unit_value)
    except (TypeError, ValueError):
        unit = 0
    scale = 1.0 if unit == 2 else 2.54 if unit == 3 else None
    return {
        "source": source,
        "raw_x_resolution": x,
        "raw_y_resolution": y,
        "unit": "inch" if unit == 2 else "centimeter" if unit == 3 else "unknown",
        "reliable": scale is not None and x is not None and y is not None,
        "dpi_x": x * scale if scale is not None and x is not None else None,
        "dpi_y": y * scale if scale is not None and y is not None else None,
    }


def pillow_encoded_metadata(opened: Image.Image) -> dict[str, object]:
    mode_channels = {
        "1": 1, "L": 1, "LA": 2, "P": 1, "RGB": 3, "RGBA": 4,
        "CMYK": 4, "YCbCr": 3, "LAB": 3, "HSV": 3,
        "I": 1, "F": 1, "I;16": 1, "I;16L": 1, "I;16B": 1, "I;16N": 1,
    }
    bit_depth = 1 if opened.mode == "1" else 16 if opened.mode.startswith("I;16") else 32 if opened.mode in {"I", "F"} else 8
    channels = mode_channels.get(opened.mode, len(opened.getbands()))
    metadata: dict[str, object] = {
        "source": "Pillow encoded mode metadata",
        "bit_depth_bits": bit_depth,
        "channel_count": channels,
        "has_encoded_alpha": "A" in opened.getbands() or "transparency" in opened.info,
    }
    resolution = {
        "source": None,
        "unit": None,
        "reliable": False,
        "dpi_x": None,
        "dpi_y": None,
    }
    if opened.format == "JPEG":
        exif = opened.getexif()
        resolution = tagged_resolution(
            exif.get(282), exif.get(283), exif.get(296), "JPEG EXIF resolution"
        )
        if not resolution["reliable"]:
            density = opened.info.get("jfif_density")
            unit = opened.info.get("jfif_unit")
            if isinstance(density, tuple) and len(density) == 2:
                resolution = tagged_resolution(
                    density[0],
                    density[1],
                    2 if unit == 1 else 3 if unit == 2 else 0,
                    "JPEG JFIF density",
                )
    if opened.format == "TIFF":
        bits = opened.tag_v2.get(258)
        samples = opened.tag_v2.get(277)
        if bits is not None:
            values = list(bits) if isinstance(bits, tuple) else [bits]
            metadata["bits_per_sample"] = [int(value) for value in values]
            metadata["bit_depth_bits"] = max(metadata["bits_per_sample"])
        if samples is not None:
            metadata["samples_per_pixel"] = int(samples)
            metadata["channel_count"] = int(samples)
        metadata["extra_samples"] = [
            int(value)
            for value in (
                opened.tag_v2.get(338)
                if isinstance(opened.tag_v2.get(338), tuple)
                else (() if opened.tag_v2.get(338) is None else (opened.tag_v2.get(338),))
            )
        ]
        metadata["source"] = "TIFF BitsPerSample/SamplesPerPixel/Pillow metadata"
        resolution = tagged_resolution(
            opened.tag_v2.get(282),
            opened.tag_v2.get(283),
            opened.tag_v2.get(296),
            "TIFF XResolution/YResolution/ResolutionUnit",
        )
    metadata["physical_resolution"] = resolution
    return metadata


def encoded_metadata(
    data: bytes | bytearray | memoryview, opened: Image.Image
) -> dict[str, object]:
    return png_encoded_metadata(data) if opened.format == "PNG" else pillow_encoded_metadata(opened)


def probe_image_pixels(
    path: Path,
    maximum_decoded_pixels: int = SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"],
    data: bytes | bytearray | memoryview | None = None,
) -> dict[str, int]:
    immutable = read_immutable_file(path) if data is None else data
    if not immutable:
        raise ValueError(f"empty or missing image: {path}")
    previous_limit = Image.MAX_IMAGE_PIXELS
    try:
        Image.MAX_IMAGE_PIXELS = maximum_decoded_pixels
        with BoundedMemoryReader(immutable) as encoded_stream:
            with Image.open(encoded_stream) as opened:
                width, height = opened.size
                frame_count = int(getattr(opened, "n_frames", 1))
    except Image.DecompressionBombError as error:
        raise DecodedPixelBudgetError(
            "decoded pixel safety budget exceeded before allocation: "
            f"{path.name} exceeds configured limit {maximum_decoded_pixels}: {error}"
        ) from error
    except (OSError, ValueError) as error:
        raise ValueError(f"cannot inspect image dimensions: {path}: {error}") from error
    finally:
        Image.MAX_IMAGE_PIXELS = previous_limit
    if width <= 0 or height <= 0:
        raise ValueError(f"invalid decoded dimensions: {path}: {width}x{height}")
    return {
        "width": int(width),
        "height": int(height),
        "pixels": int(width) * int(height),
        "frame_count": frame_count,
    }


def read_image(
    path: Path,
    maximum_decoded_pixels: int = SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"],
    data: bytes | bytearray | memoryview | None = None,
) -> tuple[np.ndarray, dict[str, object]]:
    immutable = read_immutable_file(path) if data is None else data
    if not immutable:
        raise ValueError(f"empty or missing image: {path}")
    previous_limit = Image.MAX_IMAGE_PIXELS
    try:
        Image.MAX_IMAGE_PIXELS = maximum_decoded_pixels
        with BoundedMemoryReader(immutable) as encoded_stream, Image.open(
            encoded_stream
        ) as opened:
            width, height = opened.size
            decoded_pixels = int(width) * int(height)
            if decoded_pixels > maximum_decoded_pixels:
                raise ValueError(
                    "decoded pixel safety budget exceeded before allocation: "
                    f"{path.name} has {decoded_pixels} pixels, limit is {maximum_decoded_pixels}"
                )
            actual_format = opened.format
            allowed_formats = ALLOWED_FORMATS.get(path.suffix.lower(), set())
            if actual_format not in allowed_formats:
                raise ValueError(
                    f"unsupported or extension-mismatched actual image format: "
                    f"{path.name} ({actual_format or 'unknown'})"
                )
            frame_count = int(getattr(opened, "n_frames", 1))
            exif_orientation = int(opened.getexif().get(274, 1))
            raw_mode = opened.mode
            encoded = encoded_metadata(immutable, opened)
            physical_resolution = dict(encoded["physical_resolution"])
            if exif_orientation in {5, 6, 7, 8}:
                physical_resolution["dpi_x"], physical_resolution["dpi_y"] = (
                    physical_resolution["dpi_y"],
                    physical_resolution["dpi_x"],
                )
                physical_resolution["axes_swapped_by_exif_orientation"] = True
            else:
                physical_resolution["axes_swapped_by_exif_orientation"] = False
            encoded_depth = int(encoded["bit_depth_bits"])
            encoded_channels = int(encoded["channel_count"])
            if encoded_depth > 8 and encoded_channels > 1:
                raise ValueError(
                    "greater-than-8-bit multi-channel image is unsupported before lossy "
                    f"conversion ({encoded_depth}-bit, {encoded_channels} channels)"
                )
            decoded_pillow = ImageOps.exif_transpose(opened)
            oriented_size = list(decoded_pillow.size)
            native_decoded = np.asarray(decoded_pillow)
            transparency_value = decoded_pillow.info.get("transparency")
            supported_native_16_bit = (
                encoded_depth == 16
                and encoded_channels == 1
                and decoded_pillow.mode in {"I;16", "I;16L", "I;16B", "I;16N"}
                and native_decoded.dtype.kind == "u"
                and native_decoded.dtype.itemsize == 2
                and native_decoded.ndim == 2
            )
            if encoded_depth > 8 and not supported_native_16_bit:
                raise ValueError(
                    "unsupported greater-than-8-bit native decoded representation "
                    f"({decoded_pillow.mode}, {native_decoded.dtype}, "
                    f"{native_decoded.shape})"
                )
            if decoded_pillow.mode in {"CMYK", "YCbCr", "LAB", "HSV"}:
                decoded_pillow = decoded_pillow.convert("RGB")
            elif decoded_pillow.mode == "P":
                decoded_pillow = decoded_pillow.convert(
                    "RGBA" if "transparency" in decoded_pillow.info else "RGB"
                )
            elif decoded_pillow.mode == "LA":
                pass
            decoded = np.asarray(decoded_pillow)
            metric_color = np.asarray(decoded_pillow.convert("RGBA"))
            if supported_native_16_bit:
                identity_pixels = np.ascontiguousarray(native_decoded)
                identity_mode = "native-16-bit-grayscale"
                identity_bands = 1
                alpha_policy = (
                    "native color-key transparency retained in identity metadata; "
                    "metrics white-composite"
                    if transparency_value is not None
                    else "opaque"
                )
                identity_transparency = (
                    int(transparency_value)
                    if isinstance(transparency_value, int)
                    else None
                )
                if transparency_value is not None and identity_transparency is None:
                    raise ValueError(
                        "unsupported 16-bit grayscale transparency representation"
                    )
            else:
                identity_pillow = (
                    decoded_pillow.convert("RGBA")
                    if transparency_value is not None
                    and "A" not in decoded_pillow.getbands()
                    else decoded_pillow
                )
                identity_pixels = np.ascontiguousarray(np.asarray(identity_pillow))
                identity_mode = identity_pillow.mode
                identity_bands = (
                    1 if identity_pixels.ndim == 2 else int(identity_pixels.shape[2])
                )
                alpha_policy = (
                    "represented in canonical samples; metrics white-composite"
                    if "A" in identity_pillow.getbands()
                    else "opaque"
                )
                identity_transparency = None
            if identity_pixels.dtype.itemsize > 1:
                identity_pixels = identity_pixels.astype(
                    identity_pixels.dtype.newbyteorder("<"), copy=False
                )
            identity_metadata = {
                "canonical_decoded_identity_version": 2,
                "width": int(decoded_pillow.size[0]),
                "height": int(decoded_pillow.size[1]),
                "raw_mode": raw_mode,
                "canonical_mode": identity_mode,
                "sample_dtype": identity_pixels.dtype.str,
                "sample_depth_bits": encoded_depth,
                "channel_count": identity_bands,
                "alpha_policy": alpha_policy,
                "transparent_sample": identity_transparency,
            }
            identity_header = json.dumps(
                identity_metadata,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            ).encode("utf-8")
            decoded_content_sha256 = hashlib.sha256(
                len(identity_header).to_bytes(8, "big")
                + identity_header
                + identity_pixels.tobytes(order="C")
            ).hexdigest()
    except Image.DecompressionBombError as error:
        raise DecodedPixelBudgetError(
            "decoded pixel safety budget exceeded before allocation: "
            f"{path.name} exceeds configured limit {maximum_decoded_pixels}: {error}"
        ) from error
    except (OSError, ValueError) as error:
        raise ValueError(f"cannot decode image: {path}: {error}") from error
    finally:
        Image.MAX_IMAGE_PIXELS = previous_limit
    if decoded.size == 0:
        raise ValueError(f"cannot decode image: {path}")
    alpha: np.ndarray | None = None
    alpha_scale: float | None = None
    if decoded.ndim == 2:
        image = decoded
        channels = 1
    elif decoded.ndim == 3 and decoded.shape[2] in (2, 3, 4):
        channels = int(decoded.shape[2])
        if channels in (2, 4):
            alpha = decoded[:, :, -1]
            color = decoded[:, :, :-1]
            scale = (
                1.0
                if np.issubdtype(decoded.dtype, np.bool_)
                else float(np.iinfo(decoded.dtype).max)
            )
            alpha_scale = scale
            alpha_float = alpha.astype(np.float32) / scale
            if channels == 2:
                image = (
                    color[:, :, 0].astype(np.float32) * alpha_float
                    + scale * (1.0 - alpha_float)
                ).astype(decoded.dtype)
            else:
                composited = (
                    color.astype(np.float32) * alpha_float[:, :, None]
                    + scale * (1.0 - alpha_float[:, :, None])
                ).astype(decoded.dtype)
                image = cv2.cvtColor(composited, cv2.COLOR_RGB2GRAY)
        else:
            image = cv2.cvtColor(decoded, cv2.COLOR_RGB2GRAY)
    else:
        raise ValueError(f"unsupported decoded channel layout: {path}")
    if alpha is None and transparency_value is not None and raw_mode != "P":
        alpha_max = (
            1
            if raw_mode == "1" or np.issubdtype(native_decoded.dtype, np.bool_)
            else np.iinfo(native_decoded.dtype).max
            if np.issubdtype(native_decoded.dtype, np.integer)
            else 1.0
        )
        if native_decoded.ndim == 2 and isinstance(transparency_value, int):
            transparent_sample = (
                bool(transparency_value) if raw_mode == "1" else transparency_value
            )
            transparent_mask = native_decoded == transparent_sample
        elif (
            native_decoded.ndim == 3
            and native_decoded.shape[2] == 3
            and isinstance(transparency_value, tuple)
            and len(transparency_value) == 3
        ):
            transparent_mask = np.all(
                native_decoded == np.asarray(transparency_value, dtype=native_decoded.dtype),
                axis=2,
            )
        else:
            transparent_mask = None
        if transparent_mask is not None:
            alpha = np.full(native_decoded.shape[:2], alpha_max, dtype=native_decoded.dtype)
            alpha[transparent_mask] = 0
            alpha_scale = float(alpha_max)
            alpha_float = alpha.astype(np.float32) / alpha_scale
            image = (
                image.astype(np.float32) * alpha_float
                + alpha_scale * (1.0 - alpha_float)
            ).astype(image.dtype)
    if image.dtype != np.uint8:
        maximum = (
            1.0
            if np.issubdtype(image.dtype, np.bool_)
            else float(np.iinfo(image.dtype).max)
            if np.issubdtype(image.dtype, np.integer)
            else 1.0
        )
        image = np.clip(image.astype(np.float32) * (255.0 / maximum), 0, 255).astype(np.uint8)
    if image is None or image.size == 0:
        raise ValueError(f"cannot decode image: {path}")
    supported_16_bit_mode = supported_native_16_bit
    mode_supported = raw_mode in {
        "1", "L", "LA", "P", "RGB", "RGBA", "CMYK", "YCbCr", "LAB", "HSV",
        "I;16", "I;16L", "I;16B", "I;16N",
    }
    depth_supported = (
        raw_mode == "1"
        or (encoded_depth <= 8 and native_decoded.dtype in {np.dtype(np.uint8), np.dtype(bool)})
        or supported_16_bit_mode
    )
    transparency = {
        "decoder": "Pillow ImageOps.exif_transpose",
        "actual_format": actual_format,
        "raw_mode": raw_mode,
        "decoded_mode": decoded_pillow.mode,
        "exif_orientation": exif_orientation,
        "exif_orientation_applied": exif_orientation not in (0, 1),
        "oriented_size": oriented_size,
        "frame_count": frame_count,
        "sample_dtype": str(native_decoded.dtype),
        "sample_depth_bits": encoded_depth,
        "encoded_metadata": encoded,
        "physical_resolution": {
            **physical_resolution,
            "physical_width_inches": (
                float(oriented_size[0]) / float(physical_resolution["dpi_x"])
                if physical_resolution["reliable"]
                else None
            ),
            "physical_height_inches": (
                float(oriented_size[1]) / float(physical_resolution["dpi_y"])
                if physical_resolution["reliable"]
                else None
            ),
        },
        "_decoded_content_sha256": decoded_content_sha256,
        "_encoded_sha256": hashlib.sha256(immutable).hexdigest(),
        "_encoded_bytes": len(immutable),
        "supported_16_bit_mode": supported_16_bit_mode,
        "mode_supported": mode_supported,
        "depth_supported": depth_supported,
        "channel_count": encoded_channels,
        "has_alpha": alpha is not None,
        "nonopaque_fraction": (
            float(np.mean(alpha < alpha_scale)) if alpha is not None else 0.0
        ),
        "fully_transparent_fraction": (
            float(np.mean(alpha == 0)) if alpha is not None else 0.0
        ),
        "alpha_composited_over": "white" if alpha is not None else None,
        "_metric_color_rgba": metric_color,
    }
    return image, transparency


def decoded_pixel_signature(image: np.ndarray) -> np.ndarray:
    resized = cv2.resize(image, (256, 256), interpolation=cv2.INTER_AREA)
    return cv2.GaussianBlur(resized, (3, 3), 0)


def decoded_pixel_similarity(left: np.ndarray, right: np.ndarray) -> float:
    foreground_union = (left < 0.95) | (right < 0.95)
    if left.dtype == np.uint8 or right.dtype == np.uint8:
        foreground_union = (left < 242) | (right < 242)
    if not np.any(foreground_union):
        return 1.0
    difference = np.abs(
        left[foreground_union].astype(np.float32)
        - right[foreground_union].astype(np.float32)
    )
    scale = 255.0 if left.dtype == np.uint8 or right.dtype == np.uint8 else 1.0
    return float(1.0 - np.mean(difference) / scale)


def clean_foreground_components(raw: np.ndarray) -> np.ndarray:
    height, width = raw.shape
    count, labels, stats, _ = connected_component_stats(
        raw, "foreground cleanup"
    )
    minimum_area = max(
        2, round(raw.size * THRESHOLDS["foreground_component_minimum_area_fraction"])
    )
    minimum_span = max(
        2,
        round(
            min(height, width)
            * THRESHOLDS["foreground_component_minimum_span_fraction"]
        ),
    )
    keep = (
        (stats[:, cv2.CC_STAT_AREA] >= minimum_area)
        | (
            np.maximum(
                stats[:, cv2.CC_STAT_WIDTH],
                stats[:, cv2.CC_STAT_HEIGHT],
            )
            >= minimum_span
        )
    )
    if count:
        keep[0] = False
    return keep[labels].astype(np.uint8)


def foreground_contrast(image: np.ndarray) -> tuple[np.ndarray, float]:
    luminance = image.astype(np.float32)
    height, width = image.shape
    kernel = max(15, min(101, (min(height, width) // 12) | 1))
    local_paper = cv2.GaussianBlur(luminance, (kernel, kernel), 0)
    global_paper = float(np.percentile(luminance, 90))
    contrast = np.maximum(local_paper - luminance, global_paper - luminance)
    contrast_u8 = np.clip(contrast, 0, 255).astype(np.uint8)
    otsu, _ = cv2.threshold(
        contrast_u8, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU
    )
    threshold = float(
        np.clip(
            otsu,
            THRESHOLDS["foreground_otsu_minimum_gray"],
            THRESHOLDS["foreground_otsu_maximum_gray"],
        )
    )
    threshold = max(threshold, THRESHOLDS["foreground_minimum_contrast_gray"])
    return contrast, threshold


def adaptive_foreground_mask(image: np.ndarray) -> np.ndarray:
    contrast, threshold = foreground_contrast(image)
    return clean_foreground_components((contrast >= threshold).astype(np.uint8))


def bounded_fine_component_isolations(
    component_centers: np.ndarray,
    candidate_indices: list[int],
) -> dict[int, float]:
    minimum_isolation = float(
        THRESHOLDS["fine_component_minimum_isolation_pixels"]
    )
    cell_size = max(minimum_isolation, np.finfo(np.float64).eps)
    grid: dict[tuple[int, int], list[int]] = {}
    cells: list[tuple[int, int]] = []
    for index, center in enumerate(component_centers):
        cell = (
            int(np.floor(float(center[0]) / cell_size)),
            int(np.floor(float(center[1]) / cell_size)),
        )
        cells.append(cell)
        grid.setdefault(cell, []).append(index)

    comparison_limit = ACTIVE_COMPONENT_BUDGETS[
        "maximum_component_match_comparisons"
    ]
    comparisons = 0
    isolations: dict[int, float] = {}
    for index in candidate_indices:
        center = component_centers[index]
        cell_x, cell_y = cells[index]
        isolation = minimum_isolation
        for neighbor_y in range(cell_y - 1, cell_y + 2):
            for neighbor_x in range(cell_x - 1, cell_x + 2):
                for neighbor in grid.get((neighbor_x, neighbor_y), ()):
                    if neighbor == index:
                        continue
                    comparisons += 1
                    if comparisons > comparison_limit:
                        raise ComponentBudgetError(
                            "fine component neighbor comparison safety budget "
                            "exceeded: "
                            f"comparisons={comparisons}, limit={comparison_limit}"
                        )
                    distance = float(
                        np.hypot(
                            component_centers[neighbor, 0] - center[0],
                            component_centers[neighbor, 1] - center[1],
                        )
                    )
                    if distance < isolation:
                        isolation = distance
        isolations[index] = isolation
    return isolations


def fine_component_features(image: np.ndarray) -> dict[str, object]:
    contrast, threshold = foreground_contrast(image)
    raw = (contrast >= threshold).astype(np.uint8)
    count, _, stats, centroids = connected_component_stats(
        raw, "fine foreground component census"
    )
    height, width = image.shape
    maximum_span = max(
        2,
        round(
            min(height, width)
            * THRESHOLDS["fine_component_maximum_span_fraction"]
        ),
    )
    maximum_area = max(
        int(THRESHOLDS["fine_component_maximum_area_pixels"]),
        round(image.size * THRESHOLDS["foreground_component_minimum_area_fraction"]),
    )
    component_centers = centroids[1:] if count > 1 else np.empty((0, 2))
    candidate_indices: list[int] = []
    for index in range(1, count):
        component_width = int(stats[index, cv2.CC_STAT_WIDTH])
        component_height = int(stats[index, cv2.CC_STAT_HEIGHT])
        area = int(stats[index, cv2.CC_STAT_AREA])
        if (
            max(component_width, component_height) > maximum_span
            or area > maximum_area
        ):
            continue
        candidate_indices.append(index - 1)
    isolations = bounded_fine_component_isolations(
        component_centers, candidate_indices
    )
    candidates: list[list[float]] = []
    for center_index in candidate_indices:
        index = center_index + 1
        center = centroids[index]
        component_width = int(stats[index, cv2.CC_STAT_WIDTH])
        component_height = int(stats[index, cv2.CC_STAT_HEIGHT])
        area = int(stats[index, cv2.CC_STAT_AREA])
        isolation = isolations[center_index]
        if isolation < THRESHOLDS["fine_component_minimum_isolation_pixels"]:
            continue
        candidates.append(
            [
                float(center[0] / width),
                float(center[1] / height),
                float(component_width / width),
                float(component_height / height),
                float(area),
                isolation,
            ]
        )
    candidates.sort(key=lambda item: (item[4], item[1], item[0]))
    truncated = len(candidates) > MAXIMUM_FINE_COMPONENTS_RETAINED
    aggregate_ink_pixels = int(sum(component[4] for component in candidates))
    census = np.asarray(
        candidates[:MAXIMUM_FINE_COMPONENTS_RETAINED], dtype=np.float32
    ).reshape(-1, 6)
    presence_masks: dict[str, np.ndarray] = {}
    permissive = (
        contrast >= THRESHOLDS["fine_damage_presence_minimum_contrast_gray"]
    ).astype(np.float32)
    for size in (256, 512):
        reduced = cv2.resize(permissive, (size, size), interpolation=cv2.INTER_AREA)
        presence_masks[str(size)] = np.packbits(
            reduced > 0.0, axis=None
        )
    return {
        "component_census": census,
        "component_count": len(candidates),
        "aggregate_ink_pixel_count": aggregate_ink_pixels,
        "aggregate_ink_fraction": float(aggregate_ink_pixels / image.size),
        "raw_ink_pixel_count": int(np.count_nonzero(raw)),
        "raw_ink_fraction": float(np.mean(raw)),
        "component_census_truncated": truncated,
        "presence_masks": presence_masks,
    }


def unpack_presence_mask(features: dict[str, object], size: int) -> np.ndarray:
    packed = features["presence_masks"][str(size)]
    assert isinstance(packed, np.ndarray)
    return np.unpackbits(packed, count=size * size).reshape(size, size).astype(bool)


def mapped_fine_damage(
    source: dict[str, object],
    output: dict[str, object],
    source_features: dict[str, object],
    output_features: dict[str, object],
    registration: dict[str, object],
) -> dict[str, object]:
    source_fine = source_features["fine_components"]
    output_fine = output_features["fine_components"]
    census = source_fine["component_census"]
    assert isinstance(census, np.ndarray)
    verified = bool(registration.get("verified"))
    source_bounds = source["content_bounds"]
    output_bounds = output["content_bounds"]
    bounds_mapped = bool(
        source_bounds["measurable"] and output_bounds["measurable"]
    )
    missing: list[dict[str, object]] = []
    masks = {
        size: unpack_presence_mask(output_fine, size)
        for size in (256, 512)
    }
    for component in census:
        source_x, source_y = float(component[0]), float(component[1])
        if bounds_mapped:
            source_width = max(float(source_bounds["width_fraction"]), 1e-9)
            source_height = max(float(source_bounds["height_fraction"]), 1e-9)
            relative_x = (
                source_x - float(source_bounds["left_fraction"])
            ) / source_width
            relative_y = (
                source_y - float(source_bounds["top_fraction"])
            ) / source_height
            mapped_x = float(output_bounds["left_fraction"]) + relative_x * float(
                output_bounds["width_fraction"]
            )
            mapped_y = float(output_bounds["top_fraction"]) + relative_y * float(
                output_bounds["height_fraction"]
            )
        else:
            mapped_x, mapped_y = source_x, source_y
        if not (0.0 <= mapped_x < 1.0 and 0.0 <= mapped_y < 1.0):
            continue
        absent_at_all_scales = True
        for size, mask in masks.items():
            x = min(size - 1, max(0, round(mapped_x * (size - 1))))
            y = min(size - 1, max(0, round(mapped_y * (size - 1))))
            radius = 1 if size == 256 else 2
            if np.any(
                mask[
                    max(0, y - radius):min(size, y + radius + 1),
                    max(0, x - radius):min(size, x + radius + 1),
                ]
            ):
                absent_at_all_scales = False
                break
        if absent_at_all_scales:
            missing.append(
                {
                    "source_x_fraction": source_x,
                    "source_y_fraction": source_y,
                    "source_area_pixels": int(component[4]),
                }
            )
    return {
        "method": (
            "multiscale permissive decoded-contrast presence at mapped source "
            "small-component locations, constrained by verified registration "
            "and source/output content bounds"
        ),
        "registration_verified": verified,
        "content_bounds_mapped": bounds_mapped,
        "source_small_component_count": int(source_fine["component_count"]),
        "source_census_truncated": bool(source_fine["component_census_truncated"]),
        "output_census_truncated": bool(output_fine["component_census_truncated"]),
        "missing_component_count": len(missing),
        "missing_examples": missing[:MAXIMUM_FINE_DAMAGE_EXAMPLES],
        "failed": verified and bool(missing),
    }


def scanner_dark_mask(image: np.ndarray) -> np.ndarray:
    paper = float(np.percentile(image, 90))
    cutoff = min(96.0, paper - 80.0)
    return (image.astype(np.float32) <= cutoff).astype(np.uint8)


def rotated_images(image: np.ndarray) -> dict[str, np.ndarray]:
    return {
        "0": image,
        "90": cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE),
        "180": cv2.rotate(image, cv2.ROTATE_180),
        "270": cv2.rotate(image, cv2.ROTATE_90_COUNTERCLOCKWISE),
    }


def identity_transform_images(image: np.ndarray) -> dict[str, np.ndarray]:
    return {
        **rotated_images(image),
        "mirror_horizontal": cv2.flip(image, 1),
        "mirror_vertical": cv2.flip(image, 0),
        "transpose": cv2.transpose(image),
        "transverse": cv2.flip(cv2.transpose(image), -1),
    }


def orientation_invariant_similarity(
    source_signature: np.ndarray, candidate_signatures: dict[str, np.ndarray]
) -> tuple[float, str]:
    scores = {
        label: signature_similarity(source_signature, signature)
        for label, signature in candidate_signatures.items()
    }
    best = max(scores, key=scores.get)
    return scores[best], best


def orientation_invariant_pixel_similarity(
    left: np.ndarray, right_signatures: dict[str, np.ndarray]
) -> tuple[float, str]:
    scores = {
        label: decoded_pixel_similarity(left, signature)
        for label, signature in right_signatures.items()
    }
    best = max(scores, key=scores.get)
    return scores[best], best


def content_signature(
    image: np.ndarray, foreground_mask: np.ndarray | None = None
) -> np.ndarray:
    foreground = (
        adaptive_foreground_mask(image)
        if foreground_mask is None
        else foreground_mask
    ) * 255
    points = cv2.findNonZero(foreground)
    if points is not None:
        x, y, width, height = cv2.boundingRect(points)
        margin_x = max(2, width // 30)
        margin_y = max(2, height // 30)
        x0, y0 = max(0, x - margin_x), max(0, y - margin_y)
        foreground = foreground[
            y0:min(foreground.shape[0], y + height + margin_y),
            x0:min(foreground.shape[1], x + width + margin_x),
        ]
    signature = cv2.resize(foreground, (128, 128), interpolation=cv2.INTER_AREA)
    signature = cv2.GaussianBlur(signature.astype(np.float32) / 255.0, (5, 5), 0)
    signature -= float(np.mean(signature))
    norm = float(np.linalg.norm(signature))
    return signature / norm if norm else signature


def local_compact_signature(
    image: np.ndarray, foreground_mask: np.ndarray | None = None
) -> np.ndarray:
    mask = (
        adaptive_foreground_mask(image)
        if foreground_mask is None
        else foreground_mask
    ).astype(np.float32)
    density = cv2.resize(mask, (16, 16), interpolation=cv2.INTER_AREA)
    row_projection = cv2.resize(
        np.mean(mask, axis=1).reshape(-1, 1), (1, 32), interpolation=cv2.INTER_AREA
    ).ravel()
    column_projection = cv2.resize(
        np.mean(mask, axis=0).reshape(1, -1), (32, 1), interpolation=cv2.INTER_AREA
    ).ravel()
    signature = np.concatenate((density.ravel(), row_projection, column_projection))
    signature -= float(np.mean(signature))
    norm = float(np.linalg.norm(signature))
    return signature / norm if norm else signature


def signature_similarity(left: np.ndarray, right: np.ndarray) -> float:
    left_norm = float(np.linalg.norm(left))
    right_norm = float(np.linalg.norm(right))
    if left_norm == 0.0 or right_norm == 0.0:
        return 1.0 if left_norm == right_norm else 0.0
    return float(np.clip(np.sum(left * right), -1.0, 1.0))


def color_signature(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, float]:
    thumbnail = cv2.resize(rgb, (96, 96), interpolation=cv2.INTER_AREA)
    lab = cv2.cvtColor(thumbnail, cv2.COLOR_RGB2LAB).astype(np.float32)
    chroma = np.hypot(lab[:, :, 1] - 128.0, lab[:, :, 2] - 128.0)
    chromatic = chroma >= 8.0
    chroma_fraction = float(np.mean(chromatic))
    if not np.any(chromatic):
        return (
            np.zeros(64, dtype=np.float32),
            np.zeros(48, dtype=np.float32),
            chroma_fraction,
        )
    histogram, _, _ = np.histogram2d(
        lab[:, :, 1][chromatic],
        lab[:, :, 2][chromatic],
        bins=(8, 8),
        range=((0, 256), (0, 256)),
    )
    signature = histogram.astype(np.float32).ravel()
    norm = float(np.linalg.norm(signature))
    signature = signature / norm if norm else signature

    spatial_regions: list[float] = []
    for row in range(4):
        y0, y1 = thumbnail.shape[0] * row // 4, thumbnail.shape[0] * (row + 1) // 4
        for column in range(4):
            x0 = thumbnail.shape[1] * column // 4
            x1 = thumbnail.shape[1] * (column + 1) // 4
            region_chromatic = chromatic[y0:y1, x0:x1]
            region_fraction = float(np.mean(region_chromatic))
            if np.any(region_chromatic):
                region_a = lab[y0:y1, x0:x1, 1][region_chromatic]
                region_b = lab[y0:y1, x0:x1, 2][region_chromatic]
                mean_a = float(np.mean(region_a) - 128.0) / 128.0
                mean_b = float(np.mean(region_b) - 128.0) / 128.0
            else:
                mean_a = 0.0
                mean_b = 0.0
            spatial_regions.extend((mean_a, mean_b, region_fraction))
    spatial_signature = np.asarray(spatial_regions, dtype=np.float32)
    spatial_norm = float(np.linalg.norm(spatial_signature))
    if spatial_norm:
        spatial_signature /= spatial_norm
    return signature, spatial_signature, chroma_fraction


def background_tonal_metrics(
    image: np.ndarray, rgba: np.ndarray, color_capable: bool
) -> dict[str, object]:
    luminance = image.astype(np.float32)
    paper_floor = float(np.percentile(luminance, 55))
    paper_mask = luminance >= paper_floor
    paper = luminance[paper_mask]
    percentile_points = (1, 5, 10, 25, 50, 75, 90, 95, 99)

    tile_levels: list[float] = []
    height, width = image.shape
    for row in range(4):
        y0, y1 = height * row // 4, height * (row + 1) // 4
        for column in range(4):
            x0, x1 = width * column // 4, width * (column + 1) // 4
            tile = luminance[y0:y1, x0:x1]
            if tile.size:
                tile_levels.append(float(np.percentile(tile, 85)))

    maximum = float(np.max(paper))
    paper_percentiles = {
        f"p{point}": float(np.percentile(paper, point))
        for point in percentile_points
    }
    result: dict[str, object] = {
        "measurement_stage": "absolute decoded samples before min-max normalization",
        "luminance_percentiles": {
            f"p{point}": float(np.percentile(luminance, point))
            for point in percentile_points
        },
        "paper_selection_luminance_floor": paper_floor,
        "paper_selected_fraction": float(np.mean(paper_mask)),
        "paper_brightness_percentiles": paper_percentiles,
        "paper_highlight_range_p99_minus_p50": float(
            paper_percentiles["p99"] - paper_percentiles["p50"]
        ),
        "paper_ceiling_value": maximum,
        "paper_ceiling_fraction": float(np.mean(paper >= maximum - 1.0)),
        "paper_white_clip_fraction": float(np.mean(paper >= 254.0)),
        "local_paper_tile_p85": tile_levels,
        "local_paper_unevenness_p90_minus_p10": float(
            np.percentile(tile_levels, 90) - np.percentile(tile_levels, 10)
        ),
        "color_capable": color_capable,
        "background_rgb_median": None,
        "background_color_cast_lab_chroma": None,
    }
    if color_capable:
        alpha = rgba[:, :, 3].astype(np.float32) / 255.0
        rgb = (
            rgba[:, :, :3].astype(np.float32) * alpha[:, :, None]
            + 255.0 * (1.0 - alpha[:, :, None])
        ).astype(np.uint8)
        median_rgb = np.median(rgb[paper_mask], axis=0)
        lab = cv2.cvtColor(
            np.rint(median_rgb).astype(np.uint8).reshape(1, 1, 3),
            cv2.COLOR_RGB2LAB,
        )[0, 0].astype(np.float32)
        result["background_rgb_median"] = median_rgb.tolist()
        result["background_color_cast_lab_chroma"] = float(
            np.hypot(lab[1] - 128.0, lab[2] - 128.0)
        )
    return result


def projection_landmarks(
    image: np.ndarray, foreground_mask: np.ndarray | None = None
) -> dict[str, object]:
    foreground = cv2.resize(
        (
            adaptive_foreground_mask(image)
            if foreground_mask is None
            else foreground_mask
        ).astype(np.float32),
        (256, 256),
        interpolation=cv2.INTER_AREA,
    )
    quantiles = np.asarray([0.10, 0.25, 0.50, 0.75, 0.90], dtype=np.float64)
    result: dict[str, object] = {"quantiles": quantiles.tolist()}
    for axis, projection in (
        ("x", np.sum(foreground, axis=0)),
        ("y", np.sum(foreground, axis=1)),
    ):
        total = float(np.sum(projection))
        if total <= 0:
            result[axis] = None
            continue
        cumulative = np.cumsum(projection) / total
        values = np.interp(quantiles, cumulative, np.arange(256)) / 255.0
        result[axis] = values.tolist()
    return result


def projection_scale(
    source_landmarks: dict[str, object],
    output_landmarks: dict[str, object],
    axis: str,
) -> dict[str, object]:
    source_values = source_landmarks.get(axis)
    output_values = output_landmarks.get(axis)
    if not isinstance(source_values, list) or not isinstance(output_values, list):
        return {"measurable": False, "scale": None, "fit_error": None}
    source = np.asarray(source_values, dtype=np.float64)
    output = np.asarray(output_values, dtype=np.float64)
    source_centered = source - np.mean(source)
    denominator = float(np.dot(source_centered, source_centered))
    span = float(source[-1] - source[0])
    if (
        denominator <= 0
        or span < THRESHOLDS["projection_landmark_minimum_span_fraction"]
    ):
        return {"measurable": False, "scale": None, "fit_error": None}
    scale = float(np.dot(source_centered, output - np.mean(output)) / denominator)
    fitted = np.mean(output) + scale * source_centered
    fit_error = float(np.sqrt(np.mean((output - fitted) ** 2)))
    return {
        "measurable": fit_error <= THRESHOLDS["projection_landmark_maximum_fit_error"],
        "scale": scale,
        "fit_error": fit_error,
        "source_span": span,
        "output_span": float(output[-1] - output[0]),
    }


def registered_content_scales(
    source: dict[str, object], output: dict[str, object]
) -> dict[str, object]:
    source_bounds = source["content_bounds"]
    output_bounds = output["content_bounds"]
    assert isinstance(source_bounds, dict) and isinstance(output_bounds, dict)
    bounds_measurable = bool(
        source_bounds["measurable"] and output_bounds["measurable"]
    )
    bbox_x = (
        float(output_bounds["width"]) / float(source_bounds["width"])
        if bounds_measurable and float(source_bounds["width"]) > 0
        else None
    )
    bbox_y = (
        float(output_bounds["height"]) / float(source_bounds["height"])
        if bounds_measurable and float(source_bounds["height"]) > 0
        else None
    )
    projection_x = projection_scale(
        source["projection_landmarks"], output["projection_landmarks"], "x"
    )
    projection_y = projection_scale(
        source["projection_landmarks"], output["projection_landmarks"], "y"
    )
    projection_physical_x = (
        float(projection_x["scale"])
        * float(output["width"])
        / float(source["width"])
        if projection_x["measurable"]
        else None
    )
    projection_physical_y = (
        float(projection_y["scale"])
        * float(output["height"])
        / float(source["height"])
        if projection_y["measurable"]
        else None
    )
    return {
        "bounds_measurable": bounds_measurable,
        "bbox_x": bbox_x,
        "bbox_y": bbox_y,
        "projection_x": projection_x,
        "projection_y": projection_y,
        "projection_physical_x": projection_physical_x,
        "projection_physical_y": projection_physical_y,
        "registered_x": (
            projection_physical_x
            if projection_physical_x is not None
            else bbox_x
        ),
        "registered_y": (
            projection_physical_y
            if projection_physical_y is not None
            else bbox_y
        ),
    }


def _component_registration_scale(
    source_mask: np.ndarray, output_mask: np.ndarray
) -> dict[str, object] | None:
    def components(mask: np.ndarray) -> list[dict[str, float]]:
        count, labels, stats, centroids = connected_component_stats(
            mask, "foreground registration"
        )
        ys, xs = np.nonzero(mask)
        pixel_labels = labels[ys, xs]
        component_range = count

        def sums(values: np.ndarray) -> np.ndarray:
            return np.bincount(
                pixel_labels,
                weights=values.astype(np.float64, copy=False),
                minlength=component_range,
            )

        x = xs.astype(np.float64)
        y = ys.astype(np.float64)
        m00 = np.bincount(pixel_labels, minlength=component_range).astype(
            np.float64
        )
        m10, m01 = sums(x), sums(y)
        m20, m11, m02 = sums(x * x), sums(x * y), sums(y * y)
        m30, m21 = sums(x * x * x), sums(x * x * y)
        m12, m03 = sums(x * y * y), sums(y * y * y)
        found = []
        for index in range(1, count):
            area = int(stats[index, cv2.CC_STAT_AREA])
            width = int(stats[index, cv2.CC_STAT_WIDTH])
            height = int(stats[index, cv2.CC_STAT_HEIGHT])
            if area < 9 or width < 2 or height < 2:
                continue
            center_x = float(centroids[index, 0])
            center_y = float(centroids[index, 1])
            mu20 = m20[index] - center_x * m10[index]
            mu02 = m02[index] - center_y * m01[index]
            mu11 = m11[index] - center_x * m01[index]
            mu30 = (
                m30[index]
                - 3.0 * center_x * m20[index]
                + 2.0 * center_x * center_x * m10[index]
            )
            mu03 = (
                m03[index]
                - 3.0 * center_y * m02[index]
                + 2.0 * center_y * center_y * m01[index]
            )
            mu21 = (
                m21[index]
                - 2.0 * center_x * m11[index]
                - center_y * m20[index]
                + 2.0 * center_x * center_x * m01[index]
            )
            mu12 = (
                m12[index]
                - 2.0 * center_y * m11[index]
                - center_x * m02[index]
                + 2.0 * center_y * center_y * m10[index]
            )

            def eta(moment: float, order: int) -> float:
                return float(moment / max(m00[index] ** (1.0 + order / 2.0), 1e-30))

            n20, n02, n11 = eta(mu20, 2), eta(mu02, 2), eta(mu11, 2)
            n30, n03 = eta(mu30, 3), eta(mu03, 3)
            n21, n12 = eta(mu21, 3), eta(mu12, 3)
            hu = np.asarray(
                [
                    n20 + n02,
                    (n20 - n02) ** 2 + 4.0 * n11**2,
                    (n30 - 3.0 * n12) ** 2 + (3.0 * n21 - n03) ** 2,
                    (n30 + n12) ** 2 + (n21 + n03) ** 2,
                ]
            )
            hu = -np.sign(hu) * np.log10(np.maximum(np.abs(hu), 1e-30))
            found.append(
                {
                    "left": float(stats[index, cv2.CC_STAT_LEFT]),
                    "top": float(stats[index, cv2.CC_STAT_TOP]),
                    "width": float(width),
                    "height": float(height),
                    "center_x": center_x,
                    "center_y": center_y,
                    "aspect": float(width / height),
                    "fill": float(area / (width * height)),
                    **{f"hu{item}": float(hu[item]) for item in range(4)},
                }
            )
        return found

    source_components = components(source_mask)
    output_components = components(output_mask)
    if not source_components or not output_components:
        return None
    comparison_count = len(source_components) * len(output_components)
    comparison_limit = ACTIVE_COMPONENT_BUDGETS[
        "maximum_component_match_comparisons"
    ]
    if comparison_count > comparison_limit:
        raise ComponentBudgetError(
            "foreground component matching safety budget exceeded: "
            f"comparisons={comparison_count}, limit={comparison_limit}"
        )

    def distance(left: dict[str, float], right: dict[str, float]) -> float:
        hu_distance = np.mean(
            [
                min(abs(left[f"hu{index}"] - right[f"hu{index}"]), 4.0)
                for index in range(4)
            ]
        )
        return float(
            abs(np.log(left["aspect"] / right["aspect"]))
            + abs(left["fill"] - right["fill"])
            + 0.08 * hu_distance
        )

    source_best = [
        min(
            range(len(output_components)),
            key=lambda index: distance(item, output_components[index]),
        )
        for item in source_components
    ]
    output_best = [
        min(
            range(len(source_components)),
            key=lambda index: distance(item, source_components[index]),
        )
        for item in output_components
    ]
    matches = [
        (source_components[source_index], output_components[output_index])
        for source_index, output_index in enumerate(source_best)
        if output_best[output_index] == source_index
        and distance(source_components[source_index], output_components[output_index]) <= 0.30
    ]
    if not matches:
        return None
    x_scales = np.asarray(
        [output["width"] / source["width"] for source, output in matches],
        dtype=np.float64,
    )
    y_scales = np.asarray(
        [output["height"] / source["height"] for source, output in matches],
        dtype=np.float64,
    )
    normalized_x = float(np.median(x_scales))
    normalized_y = float(np.median(y_scales))
    x_offsets = np.asarray(
        [
            output["center_x"] - normalized_x * source["center_x"]
            for source, output in matches
        ],
        dtype=np.float64,
    )
    y_offsets = np.asarray(
        [
            output["center_y"] - normalized_y * source["center_y"]
            for source, output in matches
        ],
        dtype=np.float64,
    )
    x_offset = float(np.median(x_offsets))
    y_offset = float(np.median(y_offsets))

    def relative_deviation(values: np.ndarray, center: float) -> float:
        return float(np.max(np.abs(values - center)) / max(abs(center), 1e-12))

    scale_deviation = max(
        relative_deviation(x_scales, normalized_x),
        relative_deviation(y_scales, normalized_y),
    )
    offset_deviation = max(
        float(np.max(np.abs(x_offsets - x_offset))) / source_mask.shape[1],
        float(np.max(np.abs(y_offsets - y_offset))) / source_mask.shape[0],
    )
    source_centers = np.asarray(
        [[source["center_x"], source["center_y"]] for source, _ in matches]
    )
    output_centers = np.asarray(
        [[output["center_x"], output["center_y"]] for _, output in matches]
    )
    source_span = float(
        np.hypot(*np.ptp(source_centers, axis=0))
        / np.hypot(source_mask.shape[1], source_mask.shape[0])
    )
    output_span = float(
        np.hypot(*np.ptp(output_centers, axis=0))
        / np.hypot(output_mask.shape[1], output_mask.shape[0])
    )
    distributed_matches = (
        len(matches) >= THRESHOLDS["registration_component_minimum_match_count"]
        and min(source_span, output_span)
        >= THRESHOLDS["registration_component_minimum_spatial_span_fraction"]
        and scale_deviation
        <= THRESHOLDS["registration_component_maximum_scale_deviation_fraction"]
        and offset_deviation
        <= THRESHOLDS["registration_component_maximum_offset_deviation_fraction"]
    )
    complete_single_component_match = (
        len(source_components) == 1
        and len(output_components) == 1
        and len(matches) == 1
        and max(normalized_x, normalized_y) / min(normalized_x, normalized_y)
        <= 1.12
    )

    transformed = cv2.warpAffine(
        source_mask.astype(np.uint8),
        np.asarray(
            [[normalized_x, 0.0, x_offset], [0.0, normalized_y, y_offset]],
            dtype=np.float32,
        ),
        (output_mask.shape[1], output_mask.shape[0]),
        flags=cv2.INTER_NEAREST,
        borderValue=0,
    )
    overlap = int(np.count_nonzero((transformed != 0) & (output_mask != 0)))
    transformed_count = int(np.count_nonzero(transformed))
    output_count = int(np.count_nonzero(output_mask))
    source_coverage = overlap / transformed_count if transformed_count else 0.0
    output_coverage = overlap / output_count if output_count else 0.0
    verified_canvas_transform = (
        min(source_coverage, output_coverage)
        >= THRESHOLDS["registration_canvas_verification_minimum_coverage"]
    )
    if (
        not distributed_matches
        and not complete_single_component_match
        and not verified_canvas_transform
    ):
        return None
    return {
        "normalized_x_scale": normalized_x,
        "normalized_y_scale": normalized_y,
        "match_count": len(matches),
        "spatial_span_fraction": min(source_span, output_span),
        "maximum_scale_deviation_fraction": scale_deviation,
        "maximum_offset_deviation_fraction": offset_deviation,
        "verified_canvas_transform": verified_canvas_transform,
        "source_transform_coverage": source_coverage,
        "output_transform_coverage": output_coverage,
        "distributed_match_registration": distributed_matches,
        "complete_single_component_registration": complete_single_component_match,
    }


def independent_geometric_scale(
    source: dict[str, object],
    output: dict[str, object],
    source_features: dict[str, object],
    output_features: dict[str, object],
) -> dict[str, object]:
    source_gray = source_features["registration_gray"]
    output_gray = output_features["registration_gray"]
    assert isinstance(source_gray, np.ndarray) and isinstance(output_gray, np.ndarray)
    normalized_x = None
    normalized_y = None
    evidence: dict[str, object] = {}
    component_budget_error: str | None = None
    detector = cv2.SIFT_create(nfeatures=1000, contrastThreshold=0.01)
    source_points, source_descriptors = detector.detectAndCompute(source_gray, None)
    output_points, output_descriptors = detector.detectAndCompute(output_gray, None)
    if (
        source_descriptors is not None
        and output_descriptors is not None
        and len(source_points) >= 4
        and len(output_points) >= 4
    ):
        matches = cv2.BFMatcher(cv2.NORM_L2).knnMatch(
            source_descriptors, output_descriptors, k=2
        )
        good = [
            first
            for first, second in matches
            if first.distance < 0.75 * second.distance
        ]
        if len(good) >= 4:
            source_coordinates = np.float32(
                [source_points[item.queryIdx].pt for item in good]
            )
            output_coordinates = np.float32(
                [output_points[item.trainIdx].pt for item in good]
            )
            matrix, inliers = cv2.estimateAffine2D(
                source_coordinates,
                output_coordinates,
                method=cv2.RANSAC,
                ransacReprojThreshold=3.0,
            )
            if matrix is not None and inliers is not None:
                accepted = inliers.ravel().astype(bool)
                source_span = np.ptp(source_coordinates[accepted], axis=0)
                x_scale = float(np.hypot(matrix[0, 0], matrix[1, 0]))
                y_scale = float(np.hypot(matrix[0, 1], matrix[1, 1]))
                dot = float(
                    abs(np.dot(matrix[:, 0], matrix[:, 1]))
                    / max(x_scale * y_scale, 1e-12)
                )
                if (
                    int(np.count_nonzero(accepted)) >= 6
                    and float(np.min(source_span)) >= 25.6
                    and 0.05 <= x_scale <= 20.0
                    and 0.05 <= y_scale <= 20.0
                    and dot <= 0.15
                ):
                    normalized_x = x_scale
                    normalized_y = y_scale
                    evidence = {
                        "method": "source-to-output SIFT affine registration",
                        "feature_match_count": len(good),
                        "inlier_count": int(np.count_nonzero(accepted)),
                        "verified": True,
                        "review_required": False,
                    }
    if normalized_x is None or normalized_y is None:
        source_mask = source_features["registration_foreground"]
        output_mask = output_features["registration_foreground"]
        assert isinstance(source_mask, np.ndarray) and isinstance(output_mask, np.ndarray)
        try:
            component_scale = _component_registration_scale(
                source_mask, output_mask
            )
        except ComponentBudgetError as error:
            component_scale = None
            component_budget_error = str(error)
        if component_scale is not None:
            normalized_x = float(component_scale["normalized_x_scale"])
            normalized_y = float(component_scale["normalized_y_scale"])
            verified_canvas = bool(component_scale["verified_canvas_transform"])
            evidence = {
                "method": (
                    "verified foreground canvas transform"
                    if verified_canvas
                    and not component_scale["distributed_match_registration"]
                    else "robust spatially distributed foreground component geometry"
                ),
                "component_match_count": component_scale["match_count"],
                "component_spatial_span_fraction": component_scale[
                    "spatial_span_fraction"
                ],
                "component_maximum_scale_deviation_fraction": component_scale[
                    "maximum_scale_deviation_fraction"
                ],
                "component_maximum_offset_deviation_fraction": component_scale[
                    "maximum_offset_deviation_fraction"
                ],
                "source_transform_coverage": component_scale[
                    "source_transform_coverage"
                ],
                "output_transform_coverage": component_scale[
                    "output_transform_coverage"
                ],
                "verified": True,
                "review_required": False,
            }

    canvas_x = float(output["width"]) / float(source["width"])
    canvas_y = float(output["height"]) / float(source["height"])
    if normalized_x is None or normalized_y is None:
        same_canvas = (
            int(output["width"]) == int(source["width"])
            and int(output["height"]) == int(source["height"])
        )
        if same_canvas:
            physical_x = 1.0
            physical_y = 1.0
            evidence = {
                "method": "verified identical-canvas transform",
                "verified": True,
                "review_required": False,
            }
        else:
            conservative_area_scale = max(1.0, canvas_x * canvas_y)
            physical_x = float(np.sqrt(conservative_area_scale))
            physical_y = physical_x
            evidence = {
                "method": "unverified transform; conservative ink retention",
                "verified": False,
                "review_required": True,
                "untrusted_canvas_x_scale": canvas_x,
                "untrusted_canvas_y_scale": canvas_y,
            }
    else:
        physical_x = normalized_x * canvas_x
        physical_y = normalized_y * canvas_y
    evidence.update(
        {
            "canvas_x_scale": canvas_x,
            "canvas_y_scale": canvas_y,
            "physical_x_scale": physical_x,
            "physical_y_scale": physical_y,
            "area_scale": physical_x * physical_y,
        }
    )
    if component_budget_error is not None:
        evidence.update(
            {
                "component_budget_exhausted": True,
                "component_budget_failure": component_budget_error,
                "verified": False,
                "review_required": True,
            }
        )
    return evidence


def compact_features(
    image: np.ndarray,
    rgba: np.ndarray,
    foreground_mask: np.ndarray | None = None,
) -> dict[str, object]:
    gray_thumbnail = cv2.resize(image, (256, 256), interpolation=cv2.INTER_AREA)
    gray_foreground = adaptive_foreground_mask(gray_thumbnail)
    alpha = rgba[:, :, 3].astype(np.float32) / 255.0
    rgb = (
        rgba[:, :, :3].astype(np.float32) * alpha[:, :, None]
        + 255.0 * (1.0 - alpha[:, :, None])
    ).astype(np.uint8)
    color_thumbnail = cv2.resize(rgb, (128, 128), interpolation=cv2.INTER_AREA)
    gray_rotations = identity_transform_images(gray_thumbnail)
    foreground_rotations = identity_transform_images(gray_foreground)
    color_rotations = identity_transform_images(color_thumbnail)
    color_signatures: dict[str, np.ndarray] = {}
    spatial_color_signatures: dict[str, np.ndarray] = {}
    chroma_fractions: dict[str, float] = {}
    for label, rotated in color_rotations.items():
        signature, spatial_signature, chroma_fraction = color_signature(rotated)
        color_signatures[label] = signature
        spatial_color_signatures[label] = spatial_signature
        chroma_fractions[label] = chroma_fraction
    return {
        "structure_signatures": {
            label: content_signature(rotated, foreground_rotations[label])
            for label, rotated in gray_rotations.items()
        },
        "pixel_signatures": {
            label: decoded_pixel_signature(rotated)
            for label, rotated in gray_rotations.items()
        },
        "local_signatures": {
            label: local_compact_signature(rotated, foreground_rotations[label])
            for label, rotated in gray_rotations.items()
        },
        "color_signatures": color_signatures,
        "spatial_color_signatures": spatial_color_signatures,
        "chroma_fractions": chroma_fractions,
        "projection_landmarks": projection_landmarks(image, foreground_mask),
        "registration_gray": gray_thumbnail,
        "registration_foreground": gray_foreground,
        "fine_components": fine_component_features(image),
    }


def retained_feature_bytes(features: dict[str, object]) -> int:
    total = 0
    pending: list[object] = [features]
    while pending:
        value = pending.pop()
        if isinstance(value, np.ndarray):
            total += int(value.nbytes)
        elif isinstance(value, dict):
            pending.extend(value.values())
        elif isinstance(value, (list, tuple)):
            pending.extend(value)
    return total


def content_comparison(
    source_features: dict[str, object], output_features: dict[str, object]
) -> dict[str, object]:
    source_signature = source_features["structure_signatures"]["0"]
    scores = {
        label: signature_similarity(source_signature, signature)
        for label, signature in output_features["structure_signatures"].items()
    }
    best_orientation = max(scores, key=scores.get)
    source_color = source_features["color_signatures"]["0"]
    color_scores = {
        label: signature_similarity(source_color, signature)
        for label, signature in output_features["color_signatures"].items()
    }
    source_spatial_color = source_features["spatial_color_signatures"]["0"]
    spatial_color_scores = {
        label: signature_similarity(source_spatial_color, signature)
        for label, signature in output_features["spatial_color_signatures"].items()
    }
    pixel_scores = {
        label: decoded_pixel_similarity(
            source_features["pixel_signatures"]["0"], signature
        )
        for label, signature in output_features["pixel_signatures"].items()
    }
    local_scores = {
        label: signature_similarity(
            source_features["local_signatures"]["0"], signature
        )
        for label, signature in output_features["local_signatures"].items()
    }
    return {
        "orientation_scores": scores,
        "expected_orientation_similarity": scores["0"],
        "best_orientation": best_orientation,
        "best_orientation_similarity": scores[best_orientation],
        "orientation_margin": scores[best_orientation] - scores["0"],
        "color_orientation_scores": color_scores,
        "spatial_color_orientation_scores": spatial_color_scores,
        "pixel_orientation_scores": pixel_scores,
        "local_orientation_scores": local_scores,
        "expected_color_similarity": color_scores["0"],
        "expected_spatial_color_similarity": spatial_color_scores["0"],
        "source_chroma_fraction": source_features["chroma_fractions"]["0"],
        "output_chroma_fraction": output_features["chroma_fractions"]["0"],
    }


def band_slices(height: int, width: int) -> dict[str, tuple[slice, slice]]:
    if height <= 0 or width <= 0:
        raise ValueError("border metrics require positive image dimensions")
    outer_y = max(1, round(height * THRESHOLDS["border_outer_width_fraction"]))
    outer_x = max(1, round(width * THRESHOLDS["border_outer_width_fraction"]))
    inner_y0 = min(
        height - 1,
        max(outer_y, round(height * THRESHOLDS["border_internal_band_start_fraction"])),
    )
    inner_y1 = min(
        height,
        max(inner_y0 + 1, round(height * THRESHOLDS["border_internal_band_end_fraction"])),
    )
    inner_x0 = min(
        width - 1,
        max(outer_x, round(width * THRESHOLDS["border_internal_band_start_fraction"])),
    )
    inner_x1 = min(
        width,
        max(inner_x0 + 1, round(width * THRESHOLDS["border_internal_band_end_fraction"])),
    )
    return {
        "outer_top": (slice(0, outer_y), slice(None)),
        "outer_bottom": (slice(height - outer_y, height), slice(None)),
        "outer_left": (slice(None), slice(0, outer_x)),
        "outer_right": (slice(None), slice(width - outer_x, width)),
        "inner_top": (slice(inner_y0, min(inner_y1, height)), slice(None)),
        "inner_bottom": (slice(max(0, height - inner_y1), height - inner_y0), slice(None)),
        "inner_left": (slice(None), slice(inner_x0, min(inner_x1, width))),
        "inner_right": (slice(None), slice(max(0, width - inner_x1), width - inner_x0)),
    }


def _weighted_median(angles: list[float], weights: list[float]) -> float:
    order = np.argsort(angles)
    ordered_angles = np.asarray(angles)[order]
    cumulative = np.cumsum(np.asarray(weights)[order])
    return float(ordered_angles[np.searchsorted(cumulative, cumulative[-1] / 2)])


def estimate_skew(image: np.ndarray) -> dict[str, object]:
    edges = cv2.Canny(image, 50, 150, apertureSize=3)
    minimum = max(30, min(image.shape) // 12)
    lines = cv2.HoughLinesP(
        edges, 1, np.pi / 1800, threshold=30, minLineLength=minimum, maxLineGap=12
    )
    horizontal_angles: list[float] = []
    horizontal_weights: list[float] = []
    vertical_lines: list[tuple[float, float, float]] = []
    if lines is not None:
        for x1, y1, x2, y2 in lines[:, 0]:
            angle = float(np.degrees(np.arctan2(y2 - y1, x2 - x1)))
            while angle <= -90:
                angle += 180
            while angle > 90:
                angle -= 180
            if abs(angle) <= 15:
                horizontal_angles.append(angle)
                horizontal_weights.append(float(np.hypot(x2 - x1, y2 - y1)))
            if abs(angle) >= 75:
                if y1 != y2:
                    vertical_lines.append(
                        (
                            float((x1 + x2) / (2.0 * image.shape[1])),
                            float(np.degrees(np.arctan((x2 - x1) / (y2 - y1)))),
                            float(np.hypot(x2 - x1, y2 - y1)),
                        )
                    )
    minimum_lines = int(THRESHOLDS["geometry_minimum_hough_lines"])
    horizontal_measurable = len(horizontal_angles) >= minimum_lines
    left = [line for line in vertical_lines if line[0] < 0.45]
    right = [line for line in vertical_lines if line[0] > 0.55]
    minimum_per_side = max(2, minimum_lines // 2)
    vertical_measurable = (
        len(vertical_lines) >= minimum_lines
        and len(left) >= minimum_per_side
        and len(right) >= minimum_per_side
    )
    left_slope = (
        _weighted_median([item[1] for item in left], [item[2] for item in left])
        if vertical_measurable
        else None
    )
    right_slope = (
        _weighted_median([item[1] for item in right], [item[2] for item in right])
        if vertical_measurable
        else None
    )
    convergence = (
        abs(float(left_slope) - float(right_slope))
        if vertical_measurable
        else None
    )
    return {
        "horizontal": {
            "measurable": horizontal_measurable,
            "line_count": len(horizontal_angles),
            "residual_degrees": (
                abs(_weighted_median(horizontal_angles, horizontal_weights))
                if horizontal_measurable else None
            ),
        },
        "vertical_convergence_barline": {
            "measurable": vertical_measurable,
            "line_count": len(vertical_lines),
            "left_line_count": len(left),
            "right_line_count": len(right),
            "left_model_slope_degrees": left_slope,
            "right_model_slope_degrees": right_slope,
            "model": "left/right positional vertical-line slope divergence",
            "residual_degrees": convergence,
        },
    }


def analyze_band(mask: np.ndarray) -> dict[str, object]:
    if mask.size == 0:
        raise ValueError("border metric band must contain at least one pixel")
    dark_fraction = float(np.mean(mask))
    if not np.any(mask):
        return {
            "dark_fraction": dark_fraction,
            "long_axis_breadth": 0.0,
            "largest_component_fraction": 0.0,
            "largest_component_span": 0.0,
        }
    horizontal = mask.shape[1] >= mask.shape[0]
    occupied = np.any(mask, axis=0 if horizontal else 1)
    breadth = float(np.mean(occupied))
    component_count, _, stats, _ = connected_component_stats(
        mask, "border-band analysis"
    )
    if component_count <= 1:
        component_fraction = 0.0
        component_span = 0.0
    else:
        largest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        component_fraction = float(stats[largest, cv2.CC_STAT_AREA] / mask.size)
        span_pixels = stats[largest, cv2.CC_STAT_WIDTH if horizontal else cv2.CC_STAT_HEIGHT]
        component_span = float(span_pixels / (mask.shape[1] if horizontal else mask.shape[0]))
    return {
        "dark_fraction": dark_fraction,
        "long_axis_breadth": breadth,
        "largest_component_fraction": component_fraction,
        "largest_component_span": component_span,
    }


def edge_component_analysis(mask: np.ndarray) -> dict[str, dict[str, object]]:
    height, width = mask.shape
    depth_y = max(1, round(height * THRESHOLDS["edge_component_search_depth_fraction"]))
    depth_x = max(1, round(width * THRESHOLDS["edge_component_search_depth_fraction"]))
    full_count, _, full_stats, _ = connected_component_stats(
        mask, "full-page edge analysis"
    )

    def full_candidates(horizontal: bool) -> list[dict[str, float]]:
        candidates = []
        cross_axis_size = height if horizontal else width
        long_axis_size = width if horizontal else height
        edge_zone_size = (
            depth_y * width if horizontal else depth_x * height
        )
        for index in range(1, full_count):
            component_width = int(full_stats[index, cv2.CC_STAT_WIDTH])
            component_height = int(full_stats[index, cv2.CC_STAT_HEIGHT])
            area = int(full_stats[index, cv2.CC_STAT_AREA])
            length = component_width if horizontal else component_height
            thickness = component_height if horizontal else component_width
            length_fraction = float(length / long_axis_size)
            thickness_fraction = float(thickness / cross_axis_size)
            fill_fraction = float(area / (component_width * component_height))
            zone_fraction = float(area / edge_zone_size)
            if horizontal:
                long_start = float(full_stats[index, cv2.CC_STAT_LEFT] / width)
                long_end = float(
                    (full_stats[index, cv2.CC_STAT_LEFT] + component_width) / width
                )
                cross_start = float(full_stats[index, cv2.CC_STAT_TOP] / height)
                cross_end = float(
                    (full_stats[index, cv2.CC_STAT_TOP] + component_height) / height
                )
            else:
                long_start = float(full_stats[index, cv2.CC_STAT_TOP] / height)
                long_end = float(
                    (full_stats[index, cv2.CC_STAT_TOP] + component_height) / height
                )
                cross_start = float(full_stats[index, cv2.CC_STAT_LEFT] / width)
                cross_end = float(
                    (full_stats[index, cv2.CC_STAT_LEFT] + component_width) / width
                )
            if (
                length_fraction
                >= THRESHOLDS["edge_component_minimum_length_fraction"]
                and thickness_fraction
                >= THRESHOLDS["edge_component_minimum_thickness_fraction"]
                and fill_fraction
                >= THRESHOLDS["edge_component_minimum_fill_fraction"]
                and zone_fraction
                >= THRESHOLDS["edge_component_minimum_zone_fraction"]
            ):
                candidates.append(
                    {
                        "length_fraction": length_fraction,
                        "thickness_fraction": thickness_fraction,
                        "fill_fraction": fill_fraction,
                        "zone_fraction": zone_fraction,
                        "long_axis_start_fraction": long_start,
                        "long_axis_end_fraction": long_end,
                        "cross_axis_start_fraction": cross_start,
                        "cross_axis_end_fraction": cross_end,
                    }
                )
        return candidates

    full_horizontal_candidates = full_candidates(True)
    full_vertical_candidates = full_candidates(False)
    zones = {
        "top": (mask[:depth_y, :], True, height, 0, 0),
        "bottom": (
            mask[height - depth_y :, :],
            True,
            height,
            0,
            height - depth_y,
        ),
        "left": (mask[:, :depth_x], False, width, 0, 0),
        "right": (
            mask[:, width - depth_x :],
            False,
            width,
            width - depth_x,
            0,
        ),
    }
    results: dict[str, dict[str, object]] = {}
    for side, (zone, horizontal, cross_axis_size, x_offset, y_offset) in zones.items():
        count, _, stats, _ = connected_component_stats(
            zone, f"{side} edge-zone analysis"
        )
        candidates: list[dict[str, float]] = []
        for index in range(1, count):
            component_width = int(stats[index, cv2.CC_STAT_WIDTH])
            component_height = int(stats[index, cv2.CC_STAT_HEIGHT])
            area = int(stats[index, cv2.CC_STAT_AREA])
            length = component_width if horizontal else component_height
            thickness = component_height if horizontal else component_width
            length_fraction = float(length / (width if horizontal else height))
            thickness_fraction = float(thickness / cross_axis_size)
            fill_fraction = float(area / (component_width * component_height))
            zone_fraction = float(area / zone.size)
            if horizontal:
                long_axis_start_fraction = float(
                    stats[index, cv2.CC_STAT_LEFT] / width
                )
                long_axis_end_fraction = float(
                    (stats[index, cv2.CC_STAT_LEFT] + component_width) / width
                )
                cross_axis_start_fraction = float(
                    (y_offset + stats[index, cv2.CC_STAT_TOP]) / height
                )
                cross_axis_end_fraction = float(
                    (
                        y_offset
                        + stats[index, cv2.CC_STAT_TOP]
                        + component_height
                    )
                    / height
                )
            else:
                long_axis_start_fraction = float(
                    stats[index, cv2.CC_STAT_TOP] / height
                )
                long_axis_end_fraction = float(
                    (stats[index, cv2.CC_STAT_TOP] + component_height) / height
                )
                cross_axis_start_fraction = float(
                    (x_offset + stats[index, cv2.CC_STAT_LEFT]) / width
                )
                cross_axis_end_fraction = float(
                    (
                        x_offset
                        + stats[index, cv2.CC_STAT_LEFT]
                        + component_width
                    )
                    / width
                )
            if (
                length_fraction
                >= THRESHOLDS["edge_component_minimum_length_fraction"]
                and thickness_fraction
                >= THRESHOLDS["edge_component_minimum_thickness_fraction"]
                and fill_fraction
                >= THRESHOLDS["edge_component_minimum_fill_fraction"]
                and zone_fraction
                >= THRESHOLDS["edge_component_minimum_zone_fraction"]
            ):
                candidates.append(
                    {
                        "length_fraction": length_fraction,
                        "thickness_fraction": thickness_fraction,
                        "fill_fraction": fill_fraction,
                        "zone_fraction": zone_fraction,
                        "long_axis_start_fraction": long_axis_start_fraction,
                        "long_axis_end_fraction": long_axis_end_fraction,
                        "cross_axis_start_fraction": cross_axis_start_fraction,
                        "cross_axis_end_fraction": cross_axis_end_fraction,
                    }
                )
        candidates.sort(
            key=lambda item: (
                item["cross_axis_start_fraction"],
                item["long_axis_start_fraction"],
                item["thickness_fraction"],
            )
        )
        largest = max(candidates, key=lambda item: item["zone_fraction"], default=None)
        search_depth = float(
            (depth_y / height) if horizontal else (depth_x / width)
        )
        outside_candidates = []
        for candidate in (
            full_horizontal_candidates if horizontal else full_vertical_candidates
        ):
            if side in {"top", "left"}:
                outside = (
                    candidate["cross_axis_start_fraction"] >= search_depth
                )
            else:
                outside = (
                    candidate["cross_axis_end_fraction"] <= 1.0 - search_depth
                )
            if outside:
                outside_candidates.append(candidate)
        results[side] = {
            "search_depth_fraction": search_depth,
            "dark_fraction": float(np.mean(zone)),
            "broad_connected_component_count": len(candidates),
            "broad_connected_components": candidates,
            "registration_candidate_components_outside_edge_zone": (
                outside_candidates
            ),
            "largest_broad_connected_component": largest,
        }
    projection_candidates: dict[str, list[dict[str, float]]] = {}
    for side, (zone, horizontal, cross_axis_size, x_offset, y_offset) in zones.items():
        projection = np.mean(zone, axis=1 if horizontal else 0)
        active = (
            projection
            >= THRESHOLDS["edge_frame_projection_minimum_occupancy_fraction"]
        )
        candidates = []
        starts = np.flatnonzero(active & ~np.r_[False, active[:-1]])
        ends = np.flatnonzero(active & ~np.r_[active[1:], False]) + 1
        for start, end in zip(starts, ends):
            band = zone[start:end, :] if horizontal else zone[:, start:end]
            occupied = np.any(band, axis=0 if horizontal else 1)
            occupied_indices = np.flatnonzero(occupied)
            if occupied_indices.size == 0:
                continue
            long_start_pixel = int(occupied_indices[0])
            long_end_pixel = int(occupied_indices[-1]) + 1
            length = long_end_pixel - long_start_pixel
            thickness = int(end - start)
            long_axis_size = width if horizontal else height
            length_fraction = float(length / long_axis_size)
            thickness_fraction = float(thickness / cross_axis_size)
            area = int(np.count_nonzero(band))
            fill_fraction = float(area / max(1, length * thickness))
            zone_fraction = float(area / zone.size)
            if (
                length_fraction
                < THRESHOLDS["edge_component_minimum_length_fraction"]
                or thickness_fraction
                < THRESHOLDS["edge_component_minimum_thickness_fraction"]
                or zone_fraction
                < THRESHOLDS["edge_component_minimum_zone_fraction"]
            ):
                continue
            if horizontal:
                cross_start = (y_offset + int(start)) / height
                cross_end = (y_offset + int(end)) / height
            else:
                cross_start = (x_offset + int(start)) / width
                cross_end = (x_offset + int(end)) / width
            candidates.append(
                {
                    "length_fraction": length_fraction,
                    "thickness_fraction": thickness_fraction,
                    "fill_fraction": fill_fraction,
                    "zone_fraction": zone_fraction,
                    "long_axis_start_fraction": float(
                        long_start_pixel / long_axis_size
                    ),
                    "long_axis_end_fraction": float(
                        long_end_pixel / long_axis_size
                    ),
                    "cross_axis_start_fraction": float(cross_start),
                    "cross_axis_end_fraction": float(cross_end),
                    "projection_occupancy_fraction": float(
                        np.mean(projection[start:end])
                    ),
                }
            )
        candidate_count = len(candidates)
        candidates.sort(
            key=lambda item: (
                -float(item["projection_occupancy_fraction"]),
                -float(item["length_fraction"]),
                -float(item["zone_fraction"]),
                float(item["cross_axis_start_fraction"]),
            )
        )
        projection_limit = int(
            THRESHOLDS["edge_frame_maximum_projection_candidates_per_side"]
        )
        candidates = candidates[:projection_limit]
        projection_candidates[side] = candidates
        results[side]["side_projection_components"] = candidates
        results[side]["side_projection_component_count"] = candidate_count
        results[side]["side_projection_components_truncated"] = (
            candidate_count > projection_limit
        )

    geometry_tolerance = THRESHOLDS[
        "edge_frame_maximum_geometry_error_fraction"
    ]

    def center(candidate: dict[str, float], axis: str) -> float:
        return (
            candidate[f"{axis}_start_fraction"]
            + candidate[f"{axis}_end_fraction"]
        ) / 2.0

    frame_candidates = []
    combination_limit = int(
        THRESHOLDS["edge_frame_maximum_combination_evaluations"]
    )
    combination_evaluations = 0
    combination_budget_exhausted = False
    integral = cv2.integral(mask.astype(np.uint8, copy=False), sdepth=cv2.CV_64F)
    projection_spatial_index = {
        side: sorted(
            (
                center(candidate, "cross_axis"),
                index,
                candidate,
            )
            for index, candidate in enumerate(projection_candidates[side])
        )
        for side in ("left", "right")
    }

    def candidates_near(
        side: str, coordinate: float
    ) -> list[tuple[int, dict[str, float]]]:
        indexed = projection_spatial_index[side]
        coordinates = [item[0] for item in indexed]
        start = bisect.bisect_left(coordinates, coordinate - geometry_tolerance)
        end = bisect.bisect_right(coordinates, coordinate + geometry_tolerance)
        return [(indexed[index][1], indexed[index][2]) for index in range(start, end)]

    def envelope_mean(top: int, bottom: int, left: int, right: int) -> float:
        if bottom <= top or right <= left:
            return 0.0
        total = (
            integral[bottom, right]
            - integral[top, right]
            - integral[bottom, left]
            + integral[top, left]
        )
        return float(total / ((bottom - top) * (right - left)))

    for top_index, top in enumerate(projection_candidates["top"]):
        top_y = center(top, "cross_axis")
        for bottom_index, bottom in enumerate(projection_candidates["bottom"]):
            bottom_y = center(bottom, "cross_axis")
            if bottom_y <= top_y:
                continue
            if max(
                abs(top["long_axis_start_fraction"] - bottom["long_axis_start_fraction"]),
                abs(top["long_axis_end_fraction"] - bottom["long_axis_end_fraction"]),
            ) > geometry_tolerance:
                continue
            left_candidates = candidates_near(
                "left",
                (
                    float(top["long_axis_start_fraction"])
                    + float(bottom["long_axis_start_fraction"])
                )
                / 2.0,
            )
            right_candidates = candidates_near(
                "right",
                (
                    float(top["long_axis_end_fraction"])
                    + float(bottom["long_axis_end_fraction"])
                )
                / 2.0,
            )
            for left_index, left in left_candidates:
                left_x = center(left, "cross_axis")
                if max(
                    abs(left["long_axis_start_fraction"] - top_y),
                    abs(left["long_axis_end_fraction"] - bottom_y),
                    abs(top["long_axis_start_fraction"] - left_x),
                    abs(bottom["long_axis_start_fraction"] - left_x),
                ) > geometry_tolerance:
                    continue
                for right_index, right in right_candidates:
                    if combination_evaluations >= combination_limit:
                        combination_budget_exhausted = True
                        break
                    combination_evaluations += 1
                    right_x = center(right, "cross_axis")
                    errors = [
                        abs(top["long_axis_start_fraction"] - bottom["long_axis_start_fraction"]),
                        abs(top["long_axis_end_fraction"] - bottom["long_axis_end_fraction"]),
                        abs(left["long_axis_start_fraction"] - top_y),
                        abs(left["long_axis_end_fraction"] - bottom_y),
                        abs(top["long_axis_start_fraction"] - left_x),
                        abs(bottom["long_axis_start_fraction"] - left_x),
                        abs(right["long_axis_start_fraction"] - top_y),
                        abs(right["long_axis_end_fraction"] - bottom_y),
                        abs(top["long_axis_end_fraction"] - right_x),
                        abs(bottom["long_axis_end_fraction"] - right_x),
                    ]
                    if max(errors) > geometry_tolerance or right_x <= left_x:
                        continue
                    corner_support = []
                    for y, x, y_thickness, x_thickness in (
                        (top_y, left_x, top["thickness_fraction"], left["thickness_fraction"]),
                        (top_y, right_x, top["thickness_fraction"], right["thickness_fraction"]),
                        (bottom_y, left_x, bottom["thickness_fraction"], left["thickness_fraction"]),
                        (bottom_y, right_x, bottom["thickness_fraction"], right["thickness_fraction"]),
                    ):
                        half_height = max(1, round(y_thickness * height))
                        half_width = max(1, round(x_thickness * width))
                        y_pixel = round(y * height)
                        x_pixel = round(x * width)
                        corner = mask[
                            max(0, y_pixel - half_height) : min(height, y_pixel + half_height + 1),
                            max(0, x_pixel - half_width) : min(width, x_pixel + half_width + 1),
                        ]
                        corner_support.append(float(np.mean(corner)) if corner.size else 0.0)
                    if min(corner_support) < THRESHOLDS[
                        "edge_frame_minimum_corner_support_fraction"
                    ]:
                        continue
                    envelope_top = max(0, round(top_y * height))
                    envelope_bottom = min(
                        height, round(bottom_y * height) + 1
                    )
                    envelope_left = max(0, round(left_x * width))
                    envelope_right = min(
                        width, round(right_x * width) + 1
                    )
                    frame_candidates.append(
                        {
                            "top_fraction": top_y,
                            "bottom_fraction": bottom_y,
                            "left_fraction": left_x,
                            "right_fraction": right_x,
                            "width_fraction": right_x - left_x,
                            "height_fraction": bottom_y - top_y,
                            "side_thickness_fractions": {
                                "top": top["thickness_fraction"],
                                "bottom": bottom["thickness_fraction"],
                                "left": left["thickness_fraction"],
                                "right": right["thickness_fraction"],
                            },
                            "side_projection_indices": {
                                "top": top_index,
                                "bottom": bottom_index,
                                "left": left_index,
                                "right": right_index,
                            },
                            "corner_support_fractions": corner_support,
                            "minimum_corner_support_fraction": min(corner_support),
                            "envelope_fill_fraction": envelope_mean(
                                envelope_top,
                                envelope_bottom,
                                envelope_left,
                                envelope_right,
                            ),
                            "maximum_geometry_error_fraction": max(errors),
                        }
                    )
                if combination_budget_exhausted:
                    break
            if combination_budget_exhausted:
                break
        if combination_budget_exhausted:
            break
    frame_candidates.sort(
        key=lambda frame: (
            -float(frame["minimum_corner_support_fraction"]),
            float(frame["maximum_geometry_error_fraction"]),
        )
    )
    retained_limit = int(THRESHOLDS["edge_frame_maximum_retained_candidates"])
    projection_truncated_sides = [
        side
        for side in ("top", "bottom", "left", "right")
        if results[side]["side_projection_components_truncated"]
    ]
    retention_truncated = len(frame_candidates) > retained_limit
    truncation_reasons = []
    if projection_truncated_sides:
        truncation_reasons.append("side_projection_top_k")
    if combination_budget_exhausted:
        truncation_reasons.append("combination_budget")
    if retention_truncated:
        truncation_reasons.append("candidate_retention")
    results["rectangular_frames"] = {
        "candidates": frame_candidates[
            :retained_limit
        ],
        "candidate_count": len(frame_candidates),
        "truncated": bool(truncation_reasons),
        "generation_complete": not combination_budget_exhausted,
        "projection_truncated_sides": projection_truncated_sides,
        "combination_evaluations": combination_evaluations,
        "combination_budget": combination_limit,
        "combination_budget_exhausted": combination_budget_exhausted,
        "truncation_reasons": truncation_reasons,
    }
    return results


def edge_component_match_score(
    source: dict[str, float],
    output: dict[str, float],
    registration: dict[str, float] | None = None,
    horizontal: bool = True,
) -> float | None:
    long_scale = (
        registration["x_scale"] if horizontal else registration["y_scale"]
    ) if registration else 1.0
    long_offset = (
        registration["x_offset"] if horizontal else registration["y_offset"]
    ) if registration else 0.0
    cross_scale = (
        registration["y_scale"] if horizontal else registration["x_scale"]
    ) if registration else 1.0
    cross_offset = (
        registration["y_offset"] if horizontal else registration["x_offset"]
    ) if registration else 0.0
    source_long_start = source["long_axis_start_fraction"] * long_scale + long_offset
    source_long_end = source["long_axis_end_fraction"] * long_scale + long_offset
    source_cross_start = source["cross_axis_start_fraction"] * cross_scale + cross_offset
    source_cross_end = source["cross_axis_end_fraction"] * cross_scale + cross_offset
    intersection = max(
        0.0,
        min(source_long_end, output["long_axis_end_fraction"])
        - max(source_long_start, output["long_axis_start_fraction"]),
    )
    union = max(
        source_long_end, output["long_axis_end_fraction"]
    ) - min(source_long_start, output["long_axis_start_fraction"])
    long_axis_iou = intersection / union if union > 0.0 else 0.0
    source_center = (
        source_cross_start + source_cross_end
    ) / 2.0
    output_center = (
        output["cross_axis_start_fraction"] + output["cross_axis_end_fraction"]
    ) / 2.0
    cross_axis_distance = abs(output_center - source_center)
    minimum_thickness = min(
        source["thickness_fraction"] * cross_scale, output["thickness_fraction"]
    )
    thickness_ratio = (
        max(source["thickness_fraction"] * cross_scale, output["thickness_fraction"])
        / minimum_thickness
        if minimum_thickness > 0.0
        else float("inf")
    )
    if (
        long_axis_iou
        < THRESHOLDS["edge_component_match_minimum_long_axis_iou"]
        or cross_axis_distance
        > THRESHOLDS["edge_component_match_maximum_cross_axis_distance_fraction"]
        or thickness_ratio
        > THRESHOLDS["edge_component_match_maximum_thickness_ratio"]
    ):
        return None
    return (
        (1.0 - long_axis_iou)
        + cross_axis_distance
        / THRESHOLDS["edge_component_match_maximum_cross_axis_distance_fraction"]
        + abs(1.0 - thickness_ratio)
    )


def edge_frame_match_score(
    source: dict[str, object],
    output: dict[str, object],
    registration: dict[str, float] | None = None,
) -> float | None:
    x_scale = float(registration["x_scale"]) if registration else 1.0
    y_scale = float(registration["y_scale"]) if registration else 1.0
    x_offset = float(registration["x_offset"]) if registration else 0.0
    y_offset = float(registration["y_offset"]) if registration else 0.0
    expected = {
        "left_fraction": float(source["left_fraction"]) * x_scale + x_offset,
        "right_fraction": float(source["right_fraction"]) * x_scale + x_offset,
        "top_fraction": float(source["top_fraction"]) * y_scale + y_offset,
        "bottom_fraction": float(source["bottom_fraction"]) * y_scale + y_offset,
    }
    boundary_errors = [
        abs(float(output[key]) - value) for key, value in expected.items()
    ]
    if max(boundary_errors) > THRESHOLDS[
        "edge_frame_maximum_geometry_error_fraction"
    ]:
        return None
    source_thickness = source["side_thickness_fractions"]
    output_thickness = output["side_thickness_fractions"]
    assert isinstance(source_thickness, dict)
    assert isinstance(output_thickness, dict)
    thickness_ratios = []
    for side in ("top", "bottom", "left", "right"):
        scale = y_scale if side in {"top", "bottom"} else x_scale
        expected_thickness = float(source_thickness[side]) * abs(scale)
        observed_thickness = float(output_thickness[side])
        minimum = min(expected_thickness, observed_thickness)
        ratio = (
            max(expected_thickness, observed_thickness) / minimum
            if minimum > 0.0
            else float("inf")
        )
        thickness_ratios.append(ratio)
    if max(thickness_ratios) > THRESHOLDS[
        "edge_component_match_maximum_thickness_ratio"
    ]:
        return None
    return sum(boundary_errors) + sum(abs(1.0 - ratio) for ratio in thickness_ratios)


def compare_edge_component_analysis(
    source: dict[str, dict[str, object]],
    output: dict[str, dict[str, object]],
    registration: dict[str, float] | None = None,
) -> dict[str, object]:
    planned_comparisons = 0
    for side in ("top", "bottom", "left", "right"):
        source_edge = source[side].get("broad_connected_components", [])
        output_edge = output[side].get("broad_connected_components", [])
        source_outside = source[side].get(
            "registration_candidate_components_outside_edge_zone", []
        )
        output_outside = output[side].get(
            "registration_candidate_components_outside_edge_zone", []
        )
        planned_comparisons += len(source_edge) * len(output_edge)
        if registration is not None:
            planned_comparisons += len(source_outside) * len(output_edge)
            planned_comparisons += len(source_edge) * len(output_outside)
    source_frames = source.get("rectangular_frames", {}).get("candidates", [])
    output_frames = output.get("rectangular_frames", {}).get("candidates", [])
    planned_comparisons += len(source_frames) * len(output_frames)
    comparison_limit = ACTIVE_COMPONENT_BUDGETS[
        "maximum_component_match_comparisons"
    ]
    if planned_comparisons > comparison_limit:
        raise ComponentBudgetError(
            "edge component matching safety budget exceeded: "
            f"comparisons={planned_comparisons}, limit={comparison_limit}"
        )
    registration_is_reliable = None
    canvas_supports_uniform_scaling_padding = False
    if registration is not None:
        x_scale = float(registration["x_scale"])
        y_scale = float(registration["y_scale"])
        registration_is_reliable = (
            0.75 <= x_scale <= 1.33
            and 0.75 <= y_scale <= 1.33
            and max(x_scale, y_scale) / min(x_scale, y_scale) <= 1.15
        )
        canvas_keys = (
            "source_canvas_width",
            "source_canvas_height",
            "output_canvas_width",
            "output_canvas_height",
        )
        if all(key in registration for key in canvas_keys):
            source_canvas_width = float(registration["source_canvas_width"])
            source_canvas_height = float(registration["source_canvas_height"])
            output_canvas_width = float(registration["output_canvas_width"])
            output_canvas_height = float(registration["output_canvas_height"])
            if min(
                source_canvas_width,
                source_canvas_height,
                output_canvas_width,
                output_canvas_height,
            ) > 0.0:
                physical_x_scale = (
                    x_scale * output_canvas_width / source_canvas_width
                )
                physical_y_scale = (
                    y_scale * output_canvas_height / source_canvas_height
                )
                padding_tolerance = 0.01
                uniform_physical_scale = (
                    physical_x_scale > 0.0
                    and physical_y_scale > 0.0
                    and max(physical_x_scale, physical_y_scale)
                    / min(physical_x_scale, physical_y_scale)
                    <= 1.05
                )
                canvas_contains_scaled_source = (
                    float(registration["x_offset"]) >= -padding_tolerance
                    and float(registration["y_offset"]) >= -padding_tolerance
                    and float(registration["x_offset"]) + x_scale
                    <= 1.0 + padding_tolerance
                    and float(registration["y_offset"]) + y_scale
                    <= 1.0 + padding_tolerance
                )
                canvas_supports_uniform_scaling_padding = (
                    uniform_physical_scale and canvas_contains_scaled_source
                )
                if canvas_supports_uniform_scaling_padding:
                    registration_is_reliable = True
    broad_sides = []
    new_sides = []
    failure_sides = []
    removed_sides = []
    removed_failure_sides = []
    sides: dict[str, object] = {}
    for side in ("top", "bottom", "left", "right"):
        source_components = source[side].get("broad_connected_components", [])
        output_components = output[side].get("broad_connected_components", [])
        registration_candidates = source[side].get(
            "registration_candidate_components_outside_edge_zone", []
        )
        output_registration_candidates = output[side].get(
            "registration_candidate_components_outside_edge_zone", []
        )
        assert isinstance(source_components, list)
        assert isinstance(output_components, list)
        assert isinstance(registration_candidates, list)
        assert isinstance(output_registration_candidates, list)
        possible_matches = []
        for source_index, source_component in enumerate(source_components):
            assert isinstance(source_component, dict)
            for output_index, output_component in enumerate(output_components):
                assert isinstance(output_component, dict)
                score = edge_component_match_score(
                    source_component, output_component, horizontal=side in {"top", "bottom"}
                )
                if score is not None:
                    possible_matches.append(
                        (
                            score,
                            "edge_zone",
                            source_index,
                            output_index,
                            "direct",
                        )
                    )
                elif registration is not None:
                    score = edge_component_match_score(
                        source_component,
                        output_component,
                        registration,
                        side in {"top", "bottom"},
                    )
                    if score is not None:
                        possible_matches.append(
                            (
                                score + 0.5,
                                "edge_zone",
                                source_index,
                                output_index,
                                "registration_adjusted",
                            )
                        )
        if registration is not None:
            for source_index, source_component in enumerate(registration_candidates):
                assert isinstance(source_component, dict)
                cross_scale = float(
                    registration[
                        "y_scale" if side in {"top", "bottom"} else "x_scale"
                    ]
                )
                cross_offset = float(
                    registration[
                        "y_offset" if side in {"top", "bottom"} else "x_offset"
                    ]
                )
                registered_start = (
                    float(source_component["cross_axis_start_fraction"])
                    * cross_scale
                    + cross_offset
                )
                registered_end = (
                    float(source_component["cross_axis_end_fraction"])
                    * cross_scale
                    + cross_offset
                )
                search_depth = float(output[side]["search_depth_fraction"])
                if side in {"top", "left"}:
                    registered_inside_output_zone = (
                        registered_end > 0.0 and registered_start < search_depth
                    )
                else:
                    registered_inside_output_zone = (
                        registered_end > 1.0 - search_depth
                        and registered_start < 1.0
                    )
                if not registered_inside_output_zone:
                    continue
                for output_index, output_component in enumerate(output_components):
                    assert isinstance(output_component, dict)
                    score = edge_component_match_score(
                        source_component,
                        output_component,
                        registration,
                        side in {"top", "bottom"},
                    )
                    if score is not None:
                        possible_matches.append(
                            (
                                score + 0.5,
                                "outside_edge_zone",
                                source_index,
                                output_index,
                                "registration_adjusted_from_outside_edge_zone",
                            )
                        )
        matched_sources: set[tuple[str, int]] = set()
        matched_outputs: set[int] = set()
        matches: dict[int, tuple[str, int]] = {}
        match_kinds: dict[int, str] = {}
        for (
            _,
            source_origin,
            source_index,
            output_index,
            match_kind,
        ) in sorted(possible_matches):
            source_key = (source_origin, source_index)
            if (
                source_key not in matched_sources
                and output_index not in matched_outputs
            ):
                matched_sources.add(source_key)
                matched_outputs.add(output_index)
                matches[output_index] = source_key
                match_kinds[output_index] = match_kind

        preserved_outside_matches = []
        if registration is not None and canvas_supports_uniform_scaling_padding:
            possible_preserved_outside_matches = []
            for source_index, source_component in enumerate(source_components):
                if ("edge_zone", source_index) in matched_sources:
                    continue
                assert isinstance(source_component, dict)
                for output_index, output_component in enumerate(
                    output_registration_candidates
                ):
                    assert isinstance(output_component, dict)
                    score = edge_component_match_score(
                        source_component,
                        output_component,
                        registration,
                        side in {"top", "bottom"},
                    )
                    if score is not None:
                        possible_preserved_outside_matches.append(
                            (score, source_index, output_index)
                        )
            matched_outside_outputs: set[int] = set()
            for score, source_index, output_index in sorted(
                possible_preserved_outside_matches
            ):
                source_key = ("edge_zone", source_index)
                if (
                    source_key in matched_sources
                    or output_index in matched_outside_outputs
                ):
                    continue
                matched_sources.add(source_key)
                matched_outside_outputs.add(output_index)
                preserved_outside_matches.append(
                    (source_index, output_index, score)
                )

        comparison = {
            "source_components": source_components,
            "source_registration_candidates_outside_edge_zone": (
                registration_candidates
            ),
            "output_components": output_components,
            "output_registration_candidates_outside_edge_zone": (
                output_registration_candidates
            ),
            "component_comparisons": [],
        }
        side_is_new = False
        side_is_failure = False
        side_has_removed = False
        side_has_high_confidence_removal = False
        if output_components:
            broad_sides.append(side)
        component_comparisons = comparison["component_comparisons"]
        assert isinstance(component_comparisons, list)
        for source_index, output_index, _ in preserved_outside_matches:
            component_comparisons.append(
                {
                    "source_index": source_index,
                    "source_component_origin": "edge_zone",
                    "output_index": output_index,
                    "output_component_origin": "outside_edge_zone",
                    "source": source_components[source_index],
                    "output": output_registration_candidates[output_index],
                    "match_kind": "registration_adjusted_to_outside_edge_zone",
                    "uncertain_shifted_component": True,
                    "zone_fraction_increase": None,
                    "thickness_fraction_increase": None,
                    "new_source_relative_component": False,
                    "high_confidence_scanner_strip": False,
                    "preserved_source_edge_content": True,
                }
            )
        for output_index, output_component in enumerate(output_components):
            assert isinstance(output_component, dict)
            source_match = matches.get(output_index)
            source_origin = source_match[0] if source_match is not None else None
            source_index = source_match[1] if source_match is not None else None
            if source_origin == "edge_zone":
                source_component = source_components[source_index]
            elif source_origin == "outside_edge_zone":
                source_component = registration_candidates[source_index]
            else:
                source_component = None
            match_kind = match_kinds.get(output_index)
            if source_component is None:
                zone_increase = float(output_component["zone_fraction"])
                thickness_increase = float(output_component["thickness_fraction"])
                is_new = True
                high_confidence = True
            else:
                assert isinstance(source_component, dict)
                registration_adjusted = (
                    match_kind is not None
                    and match_kind.startswith("registration_adjusted")
                    and registration is not None
                )
                if registration_adjusted:
                    long_scale = float(
                        registration[
                            "x_scale" if side in {"top", "bottom"} else "y_scale"
                        ]
                    )
                    cross_scale = float(
                        registration[
                            "y_scale" if side in {"top", "bottom"} else "x_scale"
                        ]
                    )
                else:
                    long_scale = 1.0
                    cross_scale = 1.0
                zone_increase = float(output_component["zone_fraction"]) - float(
                    source_component["zone_fraction"]
                ) * abs(long_scale * cross_scale)
                thickness_increase = float(
                    output_component["thickness_fraction"]
                ) - float(source_component["thickness_fraction"]) * abs(cross_scale)
                is_new = (
                    zone_increase
                    >= THRESHOLDS["edge_component_minimum_zone_fraction_increase"]
                )
                high_confidence = (
                    is_new
                    and thickness_increase
                    >= THRESHOLDS[
                        "edge_component_minimum_thickness_increase_fraction"
                    ]
                )
            component_comparisons.append(
                {
                    "source_index": source_index,
                    "source_component_origin": source_origin,
                    "output_index": output_index,
                    "source": source_component,
                    "output": output_component,
                    "match_kind": match_kind,
                    "uncertain_shifted_component": (
                        match_kind is not None
                        and match_kind.startswith("registration_adjusted")
                    ),
                    "zone_fraction_increase": zone_increase,
                    "thickness_fraction_increase": thickness_increase,
                    "new_source_relative_component": is_new,
                    "high_confidence_scanner_strip": high_confidence,
                }
            )
            if is_new:
                side_is_new = True
            if high_confidence:
                side_is_failure = True
        for source_index, source_component in enumerate(source_components):
            if ("edge_zone", source_index) not in matched_sources:
                assert isinstance(source_component, dict)
                if side in {"top", "left"}:
                    physical_edge_clearance = float(
                        source_component["cross_axis_start_fraction"]
                    )
                else:
                    physical_edge_clearance = 1.0 - float(
                        source_component["cross_axis_end_fraction"]
                    )
                registered_expected_in_zone = None
                if registration is not None:
                    cross_scale = (
                        registration["y_scale"]
                        if side in {"top", "bottom"}
                        else registration["x_scale"]
                    )
                    cross_offset = (
                        registration["y_offset"]
                        if side in {"top", "bottom"}
                        else registration["x_offset"]
                    )
                    registered_start = (
                        float(source_component["cross_axis_start_fraction"])
                        * cross_scale
                        + cross_offset
                    )
                    registered_end = (
                        float(source_component["cross_axis_end_fraction"])
                        * cross_scale
                        + cross_offset
                    )
                    search_depth = float(output[side]["search_depth_fraction"])
                    if side in {"top", "left"}:
                        registered_expected_in_zone = (
                            registered_end > 0.0 and registered_start < search_depth
                        )
                    else:
                        registered_expected_in_zone = (
                            registered_end > 1.0 - search_depth
                            and registered_start < 1.0
                        )
                high_confidence_removal = (
                    physical_edge_clearance
                    >= THRESHOLDS[
                        "removed_edge_component_minimum_physical_edge_clearance_fraction"
                    ]
                    and (
                        registered_expected_in_zone is not False
                        or registration_is_reliable is False
                    )
                )
                component_comparisons.append(
                    {
                        "source_index": source_index,
                        "output_index": None,
                        "source": source_component,
                        "output": None,
                        "match_kind": None,
                        "uncertain_shifted_component": False,
                        "zone_fraction_increase": None,
                        "thickness_fraction_increase": None,
                        "new_source_relative_component": False,
                        "high_confidence_scanner_strip": False,
                        "potential_removed_source_content": True,
                        "physical_edge_clearance_fraction": physical_edge_clearance,
                        "registered_expected_in_edge_zone": registered_expected_in_zone,
                        "registration_reliable_for_removal_confidence": (
                            registration_is_reliable
                        ),
                        "removal_confidence": (
                            "high" if high_confidence_removal else "review"
                        ),
                        "high_confidence_removed_content": high_confidence_removal,
                    }
                )
                side_has_removed = True
                if high_confidence_removal:
                    side_has_high_confidence_removal = True
        comparison["new_source_relative_component"] = side_is_new
        comparison["high_confidence_scanner_strip"] = side_is_failure
        comparison["potential_removed_source_content"] = side_has_removed
        comparison["high_confidence_removed_content"] = (
            side_has_high_confidence_removal
        )
        if side_is_new:
            new_sides.append(side)
        if side_is_failure:
            failure_sides.append(side)
        if side_has_removed:
            removed_sides.append(side)
        if side_has_high_confidence_removal:
            removed_failure_sides.append(side)
        sides[side] = comparison
    source_frame_analysis = source.get("rectangular_frames", {})
    output_frame_analysis = output.get("rectangular_frames", {})
    assert isinstance(source_frame_analysis, dict)
    assert isinstance(output_frame_analysis, dict)
    source_frames = source_frame_analysis.get("candidates", [])
    output_frames = output_frame_analysis.get("candidates", [])
    assert isinstance(source_frames, list)
    assert isinstance(output_frames, list)
    possible_frame_matches = []
    for source_index, source_frame in enumerate(source_frames):
        assert isinstance(source_frame, dict)
        for output_index, output_frame in enumerate(output_frames):
            assert isinstance(output_frame, dict)
            score = edge_frame_match_score(source_frame, output_frame)
            if score is not None:
                possible_frame_matches.append(
                    (score, source_index, output_index, "direct")
                )
            elif registration is not None:
                score = edge_frame_match_score(
                    source_frame, output_frame, registration
                )
                if score is not None:
                    possible_frame_matches.append(
                        (
                            score + 0.5,
                            source_index,
                            output_index,
                            "registration_adjusted",
                        )
                    )
    matched_source_frames: set[int] = set()
    matched_output_frames: set[int] = set()
    frame_matches: dict[int, tuple[int, str]] = {}
    for _, source_index, output_index, match_kind in sorted(
        possible_frame_matches
    ):
        if (
            source_index in matched_source_frames
            or output_index in matched_output_frames
        ):
            continue
        matched_source_frames.add(source_index)
        matched_output_frames.add(output_index)
        frame_matches[output_index] = (source_index, match_kind)
    frame_comparisons = []
    new_frame = False
    frame_failure = False
    registration_adjusted_frame = False
    source_consistent_frame = False
    for output_index, output_frame in enumerate(output_frames):
        assert isinstance(output_frame, dict)
        match = frame_matches.get(output_index)
        if match is None:
            source_index = None
            source_frame = None
            match_kind = None
            high_confidence = True
            materially_enlarged = False
            new_frame = True
            frame_failure = True
        else:
            source_index, match_kind = match
            source_frame = source_frames[source_index]
            assert isinstance(source_frame, dict)
            high_confidence = False
            materially_enlarged = False
            if match_kind == "registration_adjusted":
                registration_adjusted_frame = True
            else:
                source_consistent_frame = True
                source_thickness = source_frame["side_thickness_fractions"]
                output_thickness = output_frame["side_thickness_fractions"]
                assert isinstance(source_thickness, dict)
                assert isinstance(output_thickness, dict)
                materially_enlarged = all(
                    float(output_thickness[side])
                    - float(source_thickness[side])
                    >= THRESHOLDS[
                        "edge_component_minimum_thickness_increase_fraction"
                    ]
                    for side in ("top", "bottom", "left", "right")
                )
                if materially_enlarged:
                    high_confidence = True
                    frame_failure = True
        frame_comparisons.append(
            {
                "source_index": source_index,
                "output_index": output_index,
                "source": source_frame,
                "output": output_frame,
                "match_kind": match_kind,
                "uncertain_transform_match": match_kind == "registration_adjusted",
                "new_source_relative_rectangular_frame": match is None,
                "materially_enlarged_rectangular_frame": materially_enlarged,
                "high_confidence_scanner_frame": high_confidence,
            }
        )
    for source_index, source_frame in enumerate(source_frames):
        if source_index not in matched_source_frames:
            frame_comparisons.append(
                {
                    "source_index": source_index,
                    "output_index": None,
                    "source": source_frame,
                    "output": None,
                    "match_kind": None,
                    "uncertain_transform_match": False,
                    "new_source_relative_rectangular_frame": False,
                    "materially_enlarged_rectangular_frame": False,
                    "high_confidence_scanner_frame": False,
                    "potential_removed_source_frame_or_border": True,
                }
            )
    frame_evidence_truncated = bool(source_frame_analysis.get("truncated")) or bool(
        output_frame_analysis.get("truncated")
    )
    if frame_evidence_truncated:
        frame_failure = False
        for comparison in frame_comparisons:
            comparison["high_confidence_scanner_frame"] = False
            comparison["confidence_limited_by_truncated_evidence"] = True
    return {
        "sides": sides,
        "broad_connected_edge_sides": broad_sides,
        "new_edge_contamination_sides": new_sides,
        "scanner_strip_failure_sides": failure_sides,
        "potential_removed_content_sides": removed_sides,
        "removed_content_failure_sides": removed_failure_sides,
        "rectangular_frame_comparison": {
            "source_frames": source_frames,
            "output_frames": output_frames,
            "frame_comparisons": frame_comparisons,
            "source_candidate_count": int(
                source_frame_analysis.get("candidate_count", len(source_frames))
            ),
            "output_candidate_count": int(
                output_frame_analysis.get("candidate_count", len(output_frames))
            ),
            "evidence_truncated": frame_evidence_truncated,
            "comparison_inconclusive": frame_evidence_truncated,
        },
        "new_rectangular_frame": new_frame,
        "scanner_frame_failure": frame_failure,
        "registration_adjusted_rectangular_frame": registration_adjusted_frame,
        "source_consistent_rectangular_frame": source_consistent_frame,
        "potential_removed_source_frame_or_border": any(
            item.get("potential_removed_source_frame_or_border", False)
            for item in frame_comparisons
        ),
        "frame_evidence_truncated": frame_evidence_truncated,
    }


def structure_metrics(
    image: np.ndarray, foreground_mask: np.ndarray | None = None
) -> dict[str, int]:
    dark = (
        adaptive_foreground_mask(image)
        if foreground_mask is None
        else foreground_mask
    )
    width = image.shape[1]
    horizontal_kernel = cv2.getStructuringElement(
        cv2.MORPH_RECT, (max(12, width // 5), 1)
    )
    long_horizontal = cv2.morphologyEx(dark, cv2.MORPH_OPEN, horizontal_kernel)
    count, _, stats, _ = connected_component_stats(
        long_horizontal, "long-horizontal structure analysis"
    )
    lines = sum(
        1
        for index in range(1, count)
        if stats[index, cv2.CC_STAT_WIDTH] >= width // 5
    )
    return {"long_horizontal_structure_count": lines}


def content_bounds(
    image: np.ndarray, foreground_mask: np.ndarray | None = None
) -> dict[str, object]:
    foreground = (
        adaptive_foreground_mask(image)
        if foreground_mask is None
        else foreground_mask
    )
    points = cv2.findNonZero(foreground)
    if points is None:
        return {"measurable": False, "x": None, "y": None, "width": None, "height": None}
    coordinates = points.reshape(-1, 2).astype(np.float64)
    image_height, image_width = image.shape
    lower = THRESHOLDS["content_bounds_lower_quantile"]
    upper = THRESHOLDS["content_bounds_upper_quantile"]
    left, right = np.quantile(coordinates[:, 0], (lower, upper))
    top, bottom = np.quantile(coordinates[:, 1], (lower, upper))
    x = int(np.floor(left))
    y = int(np.floor(top))
    right_pixel = int(np.ceil(right))
    bottom_pixel = int(np.ceil(bottom))
    width = max(1, right_pixel - x + 1)
    height = max(1, bottom_pixel - y + 1)
    trimmed = coordinates[
        (coordinates[:, 0] >= left)
        & (coordinates[:, 0] <= right)
        & (coordinates[:, 1] >= top)
        & (coordinates[:, 1] <= bottom)
    ]
    if not trimmed.size:
        trimmed = coordinates
    return {
        "measurable": True,
        "method": "adaptive foreground, connected-component cleanup, 0.5%-99.5% quantile bounds",
        "x": int(x),
        "y": int(y),
        "width": int(width),
        "height": int(height),
        "width_fraction": float(width / image.shape[1]),
        "height_fraction": float(height / image.shape[0]),
        "aspect_ratio": float(width / height),
        "left_fraction": float(x / image_width),
        "top_fraction": float(y / image_height),
        "right_fraction": float((x + width) / image_width),
        "bottom_fraction": float((y + height) / image_height),
        "centroid_x_fraction": float(np.mean(trimmed[:, 0]) / image_width),
        "centroid_y_fraction": float(np.mean(trimmed[:, 1]) / image_height),
    }


def metrics(
    path: Path,
    maximum_decoded_pixels: int = SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"],
    data: bytes | bytearray | memoryview | None = None,
) -> tuple[dict[str, object], dict[str, object]]:
    image, transparency = (
        read_image(path, data=data)
        if maximum_decoded_pixels
        == SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"]
        else read_image(path, maximum_decoded_pixels, data)
    )
    rgba = transparency.pop("_metric_color_rgba")
    decoded_content_sha256 = str(transparency.pop("_decoded_content_sha256"))
    encoded_sha256 = str(transparency.pop("_encoded_sha256"))
    encoded_bytes = int(transparency.pop("_encoded_bytes"))
    assert isinstance(rgba, np.ndarray)
    height, width = image.shape
    frame_count = int(transparency["frame_count"])
    if frame_count <= 0:
        raise ValueError(f"cannot determine decoded frame count: {path}")
    foreground = adaptive_foreground_mask(image)
    foreground_count = int(np.count_nonzero(foreground))
    component_count, _, component_stats, _ = connected_component_stats(
        foreground, "page foreground metrics"
    )
    maximum_component_span_fraction = max(
        (
            max(
                int(component_stats[index, cv2.CC_STAT_WIDTH]) / width,
                int(component_stats[index, cv2.CC_STAT_HEIGHT]) / height,
            )
            for index in range(1, component_count)
        ),
        default=0.0,
    )
    border_analysis = {
        name: analyze_band(foreground[rows, columns])
        for name, (rows, columns) in band_slices(height, width).items()
    }
    border = {
        name: float(analysis["dark_fraction"])
        for name, analysis in border_analysis.items()
    }
    edge_components = edge_component_analysis(scanner_dark_mask(image))
    features = compact_features(image, rgba, foreground)
    raw_mode = str(transparency["raw_mode"])
    tonal_quality = background_tonal_metrics(
        image,
        rgba,
        raw_mode in {"P", "RGB", "RGBA", "CMYK", "YCbCr", "LAB", "HSV"},
    )
    return (
        {
            "file": path.name,
            "bytes": encoded_bytes,
            "sha256": encoded_sha256,
            "decoded_content_sha256": decoded_content_sha256,
            "frame_count": frame_count,
            "width": width,
            "height": height,
            "transparency": transparency,
            "ink_fraction": float(np.mean(foreground)),
            "ink_pixel_count": foreground_count,
            "maximum_foreground_component_span_fraction": float(
                maximum_component_span_fraction
            ),
            "foreground_method": (
                "background-relative local/global paper contrast with Otsu-constrained "
                "threshold and connected-component cleanup"
            ),
            "border_dark_fractions": border,
            "border_analysis": border_analysis,
            "edge_component_analysis": edge_components,
            "edge_component_method": (
                "absolute dark-pixel connected components within each outer 12% side, "
                "plus all-side projections and perpendicular corner geometry for "
                "low-fill rectangular frames, using a paper-relative cutoff capped "
                "at grayscale 96"
            ),
            "geometry": estimate_skew(image),
            "structure": structure_metrics(image, foreground),
            "content_bounds": content_bounds(image, foreground),
            "projection_landmarks": features["projection_landmarks"],
            "background_tonal_quality": tonal_quality,
        },
        features,
    )


def separated_directories(input_dir: Path, output_dir: Path) -> bool:
    source = input_dir.resolve()
    destination = output_dir.resolve()
    return (
        source != destination
        and source not in destination.parents
        and destination not in source.parents
    )


def is_reparse_point(path: Path) -> bool:
    try:
        attributes = path.lstat().st_file_attributes
    except AttributeError:
        return path.is_symlink()
    return bool(attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def traverses_reparse_point(path: Path) -> bool:
    current = Path(os.path.abspath(path))
    while True:
        if os.path.lexists(current) and is_reparse_point(current):
            return True
        if current.parent == current:
            return False
        current = current.parent


def filesystem_snapshot(
    roots: dict[str, Path],
    inventories: dict[str, dict[str, list[dict[str, object]]]],
    paired_paths: list[tuple[Path, Path]],
    manifest_path: Path | None = None,
    maximum_total_bytes_hashed: int = SAFETY_BUDGET_DEFAULTS[
        "maximum_inventory_total_bytes_hashed_per_side"
    ] * 2,
    maximum_file_bytes: int = SAFETY_BUDGET_DEFAULTS["maximum_inventory_file_bytes"],
) -> dict[str, object]:
    paths: dict[str, Path] = {}
    for side, inventory in inventories.items():
        root = roots[side]
        for item in inventory["all_entries"]:
            relative = str(item["path"])
            paths[f"{side}:{relative}"] = root / relative
    for input_path, output_path in paired_paths:
        paths[f"paired-input:{input_path.name}"] = input_path
        paths[f"paired-output:{output_path.name}"] = output_path
    if manifest_path is not None:
        paths["pairing-manifest"] = Path(os.path.abspath(manifest_path))

    snapshot: dict[str, object] = {}
    hash_cache: dict[Path, str | None] = {}
    hash_forbidden = {
        (roots[side] / str(item["path"])).resolve()
        for side, inventory in inventories.items()
        for item in inventory["all_entries"]
        if item.get("hash_skipped_by_safety_budget")
    }
    bytes_hashed = 0
    for identity, path in sorted(paths.items()):
        if not os.path.lexists(path):
            snapshot[identity] = {"exists": False}
            continue
        status = path.lstat()
        entry: dict[str, object] = {
            "exists": True,
            "mode": int(status.st_mode),
            "size": int(status.st_size),
            "mtime_ns": int(status.st_mtime_ns),
            "device": int(status.st_dev),
            "inode": int(status.st_ino),
            "link_count": int(status.st_nlink),
            "is_symlink": path.is_symlink(),
            "is_reparse_point": is_reparse_point(path),
        }
        if path.is_file() and not entry["is_symlink"] and not entry["is_reparse_point"]:
            canonical = path.resolve()
            if canonical not in hash_cache:
                if canonical in hash_forbidden:
                    hash_cache[canonical] = None
                elif entry["size"] > maximum_file_bytes:
                    hash_cache[canonical] = None
                elif bytes_hashed + int(entry["size"]) > maximum_total_bytes_hashed:
                    hash_cache[canonical] = None
                else:
                    effective_file_limit = (
                        min(maximum_file_bytes, MAXIMUM_MANIFEST_JSON_BYTES)
                        if identity == "pairing-manifest"
                        else maximum_file_bytes
                    )
                    if int(entry["size"]) > effective_file_limit:
                        hash_cache[canonical] = None
                    else:
                        hash_cache[canonical] = sha256_file(
                            path, effective_file_limit
                        )
                        bytes_hashed += int(entry["size"])
            entry["sha256"] = hash_cache[canonical]
            entry["hash_skipped_by_safety_budget"] = hash_cache[canonical] is None
        snapshot[identity] = entry
    snapshot["__inventory_hash_budget__"] = {
        "bytes_hashed": bytes_hashed,
        "rejected": any(value is None for value in hash_cache.values()),
    }
    return snapshot


def canonical_hash(
    document: dict[str, object],
    maximum_bytes: int = MAXIMUM_EVIDENCE_JSON_BYTES,
) -> str:
    digest = hashlib.sha256()
    encoded_bytes = 0
    encoder = json.JSONEncoder(
        sort_keys=True, separators=(",", ":"), allow_nan=False
    )
    for text in encoder.iterencode(document):
        chunk = text.encode("utf-8")
        encoded_bytes += len(chunk)
        if encoded_bytes > maximum_bytes:
            raise ValueError(
                "canonical JSON exceeds hashing safety budget: "
                f"limit is {maximum_bytes}"
            )
        digest.update(chunk)
    return digest.hexdigest()


def regular_file_snapshot(
    path: Path, maximum_bytes: int = MAXIMUM_EVIDENCE_JSON_BYTES
) -> dict[str, object]:
    absolute = Path(os.path.abspath(path))
    if (
        not absolute.is_file()
        or absolute.is_symlink()
        or is_reparse_point(absolute)
        or traverses_reparse_point(absolute)
    ):
        return {"safe_regular_file": False}
    status = absolute.stat()
    if status.st_size > maximum_bytes:
        return {
            "safe_regular_file": False,
            "oversized": True,
            "size": int(status.st_size),
            "maximum_bytes": maximum_bytes,
        }
    return {
        "safe_regular_file": True,
        "size": int(status.st_size),
        "mtime_ns": int(status.st_mtime_ns),
        "device": int(status.st_dev),
        "inode": int(status.st_ino),
        "link_count": int(status.st_nlink),
        "sha256": sha256_file(absolute, maximum_bytes),
    }


def paired_rows_match_snapshot(
    paired_paths: list[tuple[Path, Path]],
    input_by_path: dict[Path, dict[str, object] | None],
    output_by_path: dict[Path, dict[str, object] | None],
    snapshot: dict[str, object],
) -> bool:
    for input_path, output_path in paired_paths:
        for prefix, path, row in (
            ("paired-input", input_path, input_by_path.get(input_path)),
            ("paired-output", output_path, output_by_path.get(output_path)),
        ):
            if row is None:
                continue
            current = snapshot.get(f"{prefix}:{path.name}")
            if (
                not isinstance(current, dict)
                or current.get("sha256") != row.get("sha256")
                or current.get("size") != row.get("bytes")
            ):
                return False
    return True


def image_snapshots_match_paths(
    image_snapshots: dict[Path, dict[str, object]],
) -> bool:
    for path, snapshot in image_snapshots.items():
        try:
            status = path.stat()
            if (
                status.st_size != snapshot["size"]
                or sha256_file(path, int(snapshot["size"])) != snapshot["sha256"]
            ):
                return False
        except OSError:
            return False
    return True


def image_captures_still_published(
    image_captures: dict[Path, ImmutableFile],
) -> bool:
    return all(
        image_capture_still_published(captured)
        for captured in image_captures.values()
    )


def publication_target_policy(
    target_argument: Path, input_dir: Path, output_dir: Path, option: str
) -> dict[str, object]:
    target = Path(os.path.abspath(target_argument))
    errors: list[str] = []
    if target.suffix.casefold() != ".json":
        errors.append(f"{option} target must have a .json extension")
    if os.path.lexists(target):
        errors.append(f"{option} target must not already exist")
    parent = target.parent
    if not parent.is_dir():
        errors.append(f"{option} parent directory must already exist")
    else:
        current = parent
        while True:
            if is_reparse_point(current):
                errors.append(f"{option} path must not traverse a link or reparse point")
                break
            if current.parent == current:
                break
            current = current.parent
    resolved_target = parent.resolve(strict=False) / target.name
    for label, tree in (("input", input_dir), ("output", output_dir)):
        if tree.is_dir():
            resolved_tree = tree.resolve()
            if resolved_target == resolved_tree or resolved_tree in resolved_target.parents:
                errors.append(f"{option} target must be outside the {label} tree")
    return {
        "target": str(resolved_target),
        "required_extension": ".json",
        "target_preexisting": os.path.lexists(target),
        "parent_preexisting_directory": parent.is_dir(),
        "outside_input_output_trees": not any(
            "outside the" in error for error in errors
        ),
        "link_or_reparse_free_parent_chain": not any(
            "link or reparse" in error for error in errors
        ),
        "exclusive_atomic_create": True,
        "errors": errors,
    }


def atomic_create_json(
    path: Path,
    document: dict[str, object],
    maximum_bytes: int = MAXIMUM_EVIDENCE_JSON_BYTES,
) -> PublishedJSON:
    temporary = path.with_name(f".{path.name}.{secrets.token_hex(16)}.tmp")
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    descriptor = os.open(temporary, flags, 0o600)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            descriptor = -1
            written = 0
            output_digest = hashlib.sha256()
            encoder = json.JSONEncoder(indent=2, allow_nan=False)
            for text in encoder.iterencode(document):
                chunk = text.encode("utf-8")
                written += len(chunk)
                if written > maximum_bytes:
                    raise ValueError(
                        f"JSON output exceeds safety budget: {path}, "
                        f"limit is {maximum_bytes}"
                    )
                stream.write(chunk)
                output_digest.update(chunk)
            if written + 1 > maximum_bytes:
                raise ValueError(
                    f"JSON output exceeds safety budget: {path}, "
                    f"limit is {maximum_bytes}"
                )
            stream.write(b"\n")
            output_digest.update(b"\n")
            stream.flush()
            os.fsync(stream.fileno())
        published = PublishedJSON(
            path=path,
            size=written + 1,
            sha256=output_digest.hexdigest(),
        )
        os.rename(temporary, path)
        directory_flags = os.O_RDONLY
        if hasattr(os, "O_DIRECTORY"):
            directory_flags |= os.O_DIRECTORY
        try:
            directory_descriptor = os.open(path.parent, directory_flags)
        except OSError:
            directory_descriptor = -1
        if directory_descriptor >= 0:
            try:
                os.fsync(directory_descriptor)
            finally:
                os.close(directory_descriptor)
        return published
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass





def add_review(
    review: dict[str, list[str]], page: str, reason: str
) -> None:
    review.setdefault(page, [])
    if reason not in review[page]:
        review[page].append(reason)


def relative_change(source: float, output: float) -> float:
    return abs(output - source) / source if source > 0 else 0.0


def physical_resolution_comparison(
    source: dict[str, object],
    output: dict[str, object],
    unreliable_workflow_note: str | None,
) -> dict[str, object]:
    source_resolution = source["transparency"]["physical_resolution"]
    output_resolution = output["transparency"]["physical_resolution"]
    assert isinstance(source_resolution, dict) and isinstance(output_resolution, dict)
    source_reliable = bool(source_resolution["reliable"])
    output_reliable = bool(output_resolution["reliable"])
    dpi_changes: dict[str, float] | None = None
    physical_changes: dict[str, float] | None = None
    if source_reliable and output_reliable:
        dpi_changes = {
            "x": relative_change(
                float(source_resolution["dpi_x"]), float(output_resolution["dpi_x"])
            ),
            "y": relative_change(
                float(source_resolution["dpi_y"]), float(output_resolution["dpi_y"])
            ),
        }
        physical_changes = {
            "width": relative_change(
                float(source_resolution["physical_width_inches"]),
                float(output_resolution["physical_width_inches"]),
            ),
            "height": relative_change(
                float(source_resolution["physical_height_inches"]),
                float(output_resolution["physical_height_inches"]),
            ),
        }
    missing_or_unreliable = not source_reliable or not output_reliable
    maximum_dpi_change = max(dpi_changes.values()) if dpi_changes else None
    maximum_physical_change = (
        max(physical_changes.values()) if physical_changes else None
    )
    severe_missing = source_reliable and not output_reliable
    failure_required = (
        (
            severe_missing
            and unreliable_workflow_note is None
        )
        or (
            maximum_dpi_change is not None
            and maximum_dpi_change >= THRESHOLDS["dpi_change_failure_fraction"]
        )
        or (
            maximum_physical_change is not None
            and maximum_physical_change
            >= THRESHOLDS["physical_size_change_failure_fraction"]
        )
    )
    review_required = (
        missing_or_unreliable
        or (
            maximum_dpi_change is not None
            and maximum_dpi_change >= THRESHOLDS["dpi_change_review_fraction"]
        )
        or (
            maximum_physical_change is not None
            and maximum_physical_change
            >= THRESHOLDS["physical_size_change_review_fraction"]
        )
    )
    return {
        "source": source_resolution,
        "output": output_resolution,
        "dpi_relative_changes": dpi_changes,
        "physical_size_relative_changes": physical_changes,
        "missing_or_unreliable_metadata": missing_or_unreliable,
        "documented_unreliable_dpi_workflow": unreliable_workflow_note is not None,
        "workflow_note": unreliable_workflow_note,
        "review_required": review_required,
        "failure_required": failure_required,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate a processed scan batch.")
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument(
        "--evidence-report",
        type=Path,
        required=True,
        help="New mechanical evidence report on the first run; preserved evidence report on approval.",
    )
    parser.add_argument(
        "--approval",
        type=Path,
        help="Separate reviewer approval JSON for the second run.",
    )
    parser.add_argument(
        "--final-report",
        type=Path,
        help="Separate new final report path for the second run.",
    )
    parser.add_argument(
        "--pairing-manifest",
        type=Path,
        help='JSON object with a complete one-to-one "pairs" array of input/output filenames.',
    )
    parser.add_argument(
        "--dpi-workflow-note",
        "--unreliable-dpi-workflow-note",
        dest="dpi_workflow_note",
        help=(
            "Document a workflow known not to preserve reliable DPI metadata. "
            "Missing/unreliable DPI remains mandatory review rather than a hard failure."
        ),
    )
    parser.add_argument(
        "--max-pages",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_page_count_per_side"],
        help="Maximum supported image count in either input or output.",
    )
    parser.add_argument(
        "--max-decoded-pixels-per-page",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"],
        help="Maximum width times height permitted before decoding any page.",
    )
    parser.add_argument(
        "--max-total-compact-feature-pixels",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_total_compact_feature_pixels"],
        help="Maximum sum of input and output page pixels permitted for compact-feature extraction.",
    )
    parser.add_argument(
        "--max-retained-feature-bytes",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_retained_feature_bytes"],
        help="Maximum aggregate bytes retained for compact cross-page features.",
    )
    parser.add_argument(
        "--max-peak-encoded-buffer-bytes",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_peak_encoded_buffer_bytes"],
        help=(
            "Maximum encoded bytes retained at once while streaming image "
            "preflight and analysis."
        ),
    )
    parser.add_argument(
        "--max-cross-match-comparisons",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_cross_match_comparisons"],
        help="Maximum output-to-input compact-signature comparisons.",
    )
    parser.add_argument(
        "--max-duplicate-comparisons",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_duplicate_comparisons"],
        help="Maximum compact-signature output duplicate comparisons.",
    )
    parser.add_argument(
        "--max-components-per-extraction",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_components_per_extraction"],
        help="Maximum connected components accepted from any single mask extraction.",
    )
    parser.add_argument(
        "--max-component-match-comparisons",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_component_match_comparisons"],
        help="Maximum pairwise component comparisons in any registration or edge match.",
    )
    parser.add_argument(
        "--max-inventory-entries",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_inventory_entries_per_side"],
        help="Maximum filesystem inventory entries per input/output tree.",
    )
    parser.add_argument(
        "--max-inventory-depth",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_inventory_recursion_depth"],
        help="Maximum recursive inventory depth below each batch root.",
    )
    parser.add_argument(
        "--max-inventory-total-bytes-hashed",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS[
            "maximum_inventory_total_bytes_hashed_per_side"
        ],
        help="Maximum total regular-file bytes fully hashed per batch tree.",
    )
    parser.add_argument(
        "--max-inventory-file-bytes",
        type=int,
        default=SAFETY_BUDGET_DEFAULTS["maximum_inventory_file_bytes"],
        help="Maximum size of any inventory file eligible for full hashing.",
    )
    args = parser.parse_args(argv)
    if args.dpi_workflow_note is not None:
        args.dpi_workflow_note = args.dpi_workflow_note.strip()
        if not args.dpi_workflow_note:
            parser.error("--dpi-workflow-note must be nonempty")
    budget_arguments = {
        "--max-pages": args.max_pages,
        "--max-decoded-pixels-per-page": args.max_decoded_pixels_per_page,
        "--max-total-compact-feature-pixels": args.max_total_compact_feature_pixels,
        "--max-retained-feature-bytes": args.max_retained_feature_bytes,
        "--max-peak-encoded-buffer-bytes": args.max_peak_encoded_buffer_bytes,
        "--max-cross-match-comparisons": args.max_cross_match_comparisons,
        "--max-duplicate-comparisons": args.max_duplicate_comparisons,
        "--max-components-per-extraction": args.max_components_per_extraction,
        "--max-component-match-comparisons": args.max_component_match_comparisons,
        "--max-inventory-entries": args.max_inventory_entries,
        "--max-inventory-depth": args.max_inventory_depth,
        "--max-inventory-total-bytes-hashed": args.max_inventory_total_bytes_hashed,
        "--max-inventory-file-bytes": args.max_inventory_file_bytes,
    }
    custom_budget_arguments = [
        value
        for name, configured in budget_arguments.items()
        if configured != {
            "--max-pages": SAFETY_BUDGET_DEFAULTS["maximum_page_count_per_side"],
            "--max-decoded-pixels-per-page": SAFETY_BUDGET_DEFAULTS[
                "maximum_decoded_pixels_per_page"
            ],
            "--max-total-compact-feature-pixels": SAFETY_BUDGET_DEFAULTS[
                "maximum_total_compact_feature_pixels"
            ],
            "--max-retained-feature-bytes": SAFETY_BUDGET_DEFAULTS[
                "maximum_retained_feature_bytes"
            ],
            "--max-peak-encoded-buffer-bytes": SAFETY_BUDGET_DEFAULTS[
                "maximum_peak_encoded_buffer_bytes"
            ],
            "--max-cross-match-comparisons": SAFETY_BUDGET_DEFAULTS[
                "maximum_cross_match_comparisons"
            ],
            "--max-duplicate-comparisons": SAFETY_BUDGET_DEFAULTS[
                "maximum_duplicate_comparisons"
            ],
            "--max-components-per-extraction": SAFETY_BUDGET_DEFAULTS[
                "maximum_components_per_extraction"
            ],
            "--max-component-match-comparisons": SAFETY_BUDGET_DEFAULTS[
                "maximum_component_match_comparisons"
            ],
            "--max-inventory-entries": SAFETY_BUDGET_DEFAULTS[
                "maximum_inventory_entries_per_side"
            ],
            "--max-inventory-depth": SAFETY_BUDGET_DEFAULTS[
                "maximum_inventory_recursion_depth"
            ],
            "--max-inventory-total-bytes-hashed": SAFETY_BUDGET_DEFAULTS[
                "maximum_inventory_total_bytes_hashed_per_side"
            ],
            "--max-inventory-file-bytes": SAFETY_BUDGET_DEFAULTS[
                "maximum_inventory_file_bytes"
            ],
        }[name]
        for value in (name, str(configured))
    ]
    invalid_budgets = [
        f"{name} must be a positive integer"
        for name, value in budget_arguments.items()
        if value <= 0
    ]
    if invalid_budgets:
        parser.error("; ".join(invalid_budgets))
    ACTIVE_COMPONENT_BUDGETS.update(
        {
            "maximum_components_per_extraction": args.max_components_per_extraction,
            "maximum_component_match_comparisons": args.max_component_match_comparisons,
        }
    )

    for label, root in (("input", args.input), ("output", args.output)):
        if os.path.lexists(root) and is_reparse_point(root):
            print(
                f"{label} root directory must not be a symlink, junction, or reparse point: {root}",
                file=sys.stderr,
            )
            return 2

    approval_mode = args.approval is not None or args.final_report is not None
    if approval_mode and (args.approval is None or args.final_report is None):
        print("--approval and --final-report must be supplied together", file=sys.stderr)
        return 2
    if approval_mode:
        final_policy = publication_target_policy(
            args.final_report, args.input, args.output, "--final-report"
        )
        if final_policy["errors"]:
            print("; ".join(final_policy["errors"]), file=sys.stderr)
            return 2
        if (
            not args.evidence_report.is_file()
            or traverses_reparse_point(args.evidence_report)
        ):
            print("--evidence-report must be an existing regular non-reparse file on the second run", file=sys.stderr)
            return 2
    else:
        evidence_policy = publication_target_policy(
            args.evidence_report, args.input, args.output, "--evidence-report"
        )
        if evidence_policy["errors"]:
            print("; ".join(evidence_policy["errors"]), file=sys.stderr)
            return 2

    manifest_capture: ImmutableFile | None = None
    try:
        if args.pairing_manifest is not None:
            manifest_capture = capture_regular_file(
                args.pairing_manifest,
                min(args.max_inventory_file_bytes, MAXIMUM_MANIFEST_JSON_BYTES),
            )
    except (OSError, ValueError) as error:
        print(f"cannot capture --pairing-manifest: {error}", file=sys.stderr)
        return 2
    manifest_inventory = pairing_manifest_inventory(
        args.pairing_manifest, args.max_inventory_file_bytes, manifest_capture
    )
    if args.pairing_manifest is not None and (
        manifest_inventory is None or manifest_inventory["kind"] != "file"
    ):
        print("--pairing-manifest must be an existing regular non-reparse file on a non-reparse path", file=sys.stderr)
        return 2

    failures: list[str] = []
    review: dict[str, list[str]] = {}
    if not args.input.is_dir():
        failures.append(f"input directory does not exist: {args.input}")
    if not args.output.is_dir():
        failures.append(f"output directory does not exist: {args.output}")
    if not failures and not separated_directories(args.input, args.output):
        failures.append("input and output must be distinct, non-nested directories")

    inventory_arguments = {
        "maximum_pages": args.max_pages,
        "maximum_entries": args.max_inventory_entries,
        "maximum_depth": args.max_inventory_depth,
        "maximum_total_bytes_hashed": args.max_inventory_total_bytes_hashed,
        "maximum_file_bytes": args.max_inventory_file_bytes,
    }
    candidate_evidence = {
        "input": candidate_inventory(args.input, **inventory_arguments),
        "output": candidate_inventory(args.output, **inventory_arguments),
    }
    inputs = sorted(
        (
            args.input / str(item["path"])
            for item in candidate_evidence["input"]["top_level_files"]
            if item.get("classification") == "supported_image"
        ),
        key=natural_key,
    )
    outputs = sorted(
        (
            args.output / str(item["path"])
            for item in candidate_evidence["output"]["top_level_files"]
            if item.get("classification") == "supported_image"
        ),
        key=natural_key,
    )
    file_identity_aliases = cross_tree_file_identity_aliases(candidate_evidence)
    for alias in file_identity_aliases:
        failures.append(
            "input/output file-identity alias is forbidden: "
            f"{alias['input']} <=> {alias['output']}"
        )
    for side, evidence in candidate_evidence.items():
        for failure in evidence["budget"]["failures"]:
            failures.append(f"{side} {failure}")
        for item in evidence["all_entries"]:
            if item["is_symlink"] or item["is_reparse_point"]:
                failures.append(
                    f"{side} entry is a link or reparse point: {item['path']}"
                )
        for item in evidence["unsupported_image_candidates"]:
            failures.append(f"unsupported {side} image candidate: {item['path']}")
        for item in evidence["nested_entries"]:
            if item.get("classification") in {"supported_image", "unsupported_image"}:
                failures.append(f"nested {side} image candidate: {item['path']}")
            else:
                add_review(
                    review,
                    f"__{side}_inventory__",
                    f"classify nested {item['kind']}: {item['path']}",
                )
        for item in evidence["unrecognized_files"]:
            add_review(
                review,
                f"__{side}_inventory__",
                f"classify unrecognized top-level file: {item['path']}",
            )
    if not inputs:
        failures.append("input contains no supported images")
    if not outputs:
        failures.append("output contains no supported images")
    if len(inputs) != len(outputs):
        failures.append(f"page count differs: input={len(inputs)}, output={len(outputs)}")

    page_count_rejected = any(
        bool(evidence["page_count"]["rejected"])
        for evidence in candidate_evidence.values()
    )
    inventory_budget_rejected = any(
        bool(evidence["budget"]["rejected"])
        for evidence in candidate_evidence.values()
    )
    if page_count_rejected:
        failures.append(
            "page-count safety budget exceeded before decoding: "
            f"input={len(inputs)}, output={len(outputs)}, per-side limit={args.max_pages}"
        )
    image_snapshots: dict[Path, dict[str, object]] = {}
    # Kept as an empty compatibility collection for publication checks: image
    # handles and encoded bytes are deliberately closed after each streamed page.
    image_captures: dict[Path, ImmutableFile] = {}
    image_probes: dict[Path, dict[str, int]] = {}
    peak_encoded_buffer_bytes = 0
    encoded_buffer_budget_rejected = False
    decoded_pixel_budget_rejected = False
    if not page_count_rejected and not inventory_budget_rejected:
        for path in inputs + outputs:
            captured: ImmutableFile | None = None
            data: bytes | bytearray | memoryview | None = None
            try:
                estimated_peak = estimated_peak_encoded_buffer_bytes(
                    int(path.stat().st_size)
                )
                peak_encoded_buffer_bytes = max(
                    peak_encoded_buffer_bytes, estimated_peak
                )
                if estimated_peak > args.max_peak_encoded_buffer_bytes:
                    encoded_buffer_budget_rejected = True
                    raise ValueError(
                        "peak encoded-buffer safety budget exceeded before allocation: "
                        f"{path.name} estimates {estimated_peak} bytes, "
                        f"limit is {args.max_peak_encoded_buffer_bytes}"
                    )
                captured = capture_image_file(
                    path,
                    args.max_inventory_file_bytes,
                )
                data = captured.data
                actual_estimated_peak = estimated_peak_encoded_buffer_bytes(
                    len(data)
                )
                peak_encoded_buffer_bytes = max(
                    peak_encoded_buffer_bytes, actual_estimated_peak
                )
                if actual_estimated_peak > args.max_peak_encoded_buffer_bytes:
                    encoded_buffer_budget_rejected = True
                    raise ValueError(
                        "peak encoded-buffer safety budget exceeded after bounded capture: "
                        f"{path.name} estimates {actual_estimated_peak} bytes, "
                        f"limit is {args.max_peak_encoded_buffer_bytes}"
                    )
                image_snapshots[path] = captured.snapshot
                probe = probe_image_pixels(
                    path, args.max_decoded_pixels_per_page, data
                )
                image_probes[path] = probe
                if probe["pixels"] > args.max_decoded_pixels_per_page:
                    failures.append(
                        "decoded pixel safety budget exceeded before allocation: "
                        f"{path.name} has {probe['pixels']} pixels, "
                        f"limit is {args.max_decoded_pixels_per_page}"
                    )
            except DecodedPixelBudgetError as error:
                decoded_pixel_budget_rejected = True
                failures.append(str(error))
            except ValueError as error:
                if "image file exceeds safety budget" in str(error):
                    encoded_buffer_budget_rejected = True
                failures.append(str(error))
            finally:
                close_immutable_files(captured)
                data = None
                captured = None
    total_feature_pixels = sum(probe["pixels"] for probe in image_probes.values())
    total_feature_budget_rejected = (
        total_feature_pixels > args.max_total_compact_feature_pixels
    )
    if total_feature_budget_rejected:
        failures.append(
            "total compact-feature workload safety budget exceeded before decoding: "
            f"planned={total_feature_pixels} pixel-units, "
            f"limit={args.max_total_compact_feature_pixels}"
        )
    planned_cross_match_comparisons = len(inputs) * len(outputs)
    planned_duplicate_comparisons = len(outputs) * max(0, len(outputs) - 1) // 2
    comparison_budget_rejected = (
        planned_cross_match_comparisons > args.max_cross_match_comparisons
        or planned_duplicate_comparisons > args.max_duplicate_comparisons
    )
    if planned_cross_match_comparisons > args.max_cross_match_comparisons:
        failures.append(
            "cross-match comparison safety budget exceeded before comparison: "
            f"planned={planned_cross_match_comparisons}, "
            f"limit={args.max_cross_match_comparisons}"
        )
    if planned_duplicate_comparisons > args.max_duplicate_comparisons:
        failures.append(
            "duplicate comparison safety budget exceeded before comparison: "
            f"planned={planned_duplicate_comparisons}, "
            f"limit={args.max_duplicate_comparisons}"
        )
    safety_budgets = {
        "limits": {
            "maximum_page_count_per_side": args.max_pages,
            "maximum_decoded_pixels_per_page": args.max_decoded_pixels_per_page,
            "maximum_total_compact_feature_pixels": args.max_total_compact_feature_pixels,
            "maximum_retained_feature_bytes": args.max_retained_feature_bytes,
            "maximum_peak_encoded_buffer_bytes": args.max_peak_encoded_buffer_bytes,
            "maximum_cross_match_comparisons": args.max_cross_match_comparisons,
            "maximum_duplicate_comparisons": args.max_duplicate_comparisons,
            "maximum_inventory_entries_per_side": args.max_inventory_entries,
            "maximum_inventory_recursion_depth": args.max_inventory_depth,
            "maximum_inventory_total_bytes_hashed_per_side": args.max_inventory_total_bytes_hashed,
            "maximum_inventory_file_bytes": args.max_inventory_file_bytes,
            "maximum_components_per_extraction": args.max_components_per_extraction,
            "maximum_component_match_comparisons": args.max_component_match_comparisons,
        },
        "observed": {
            "input_page_count": len(inputs),
            "output_page_count": len(outputs),
            "largest_probed_page_pixels": max(
                (probe["pixels"] for probe in image_probes.values()), default=0
            ),
            "total_compact_feature_pixel_units": total_feature_pixels,
            "retained_feature_bytes": 0,
            "peak_encoded_buffer_bytes": peak_encoded_buffer_bytes,
            "planned_cross_match_comparisons": planned_cross_match_comparisons,
            "planned_duplicate_comparisons": planned_duplicate_comparisons,
            "performed_cross_match_comparisons": 0,
            "performed_duplicate_comparisons": 0,
            "component_budget_failures": [],
            "inventory": {
                side: evidence["budget"]
                for side, evidence in candidate_evidence.items()
            },
        },
        "rejected": {
            "page_count": page_count_rejected,
            "decoded_pixels_per_page": any(
                probe["pixels"] > args.max_decoded_pixels_per_page
                for probe in image_probes.values()
            ) or decoded_pixel_budget_rejected,
            "total_compact_feature_workload": total_feature_budget_rejected,
            "retained_feature_memory": False,
            "peak_encoded_buffer_memory": encoded_buffer_budget_rejected,
            "cross_match_comparisons": (
                planned_cross_match_comparisons > args.max_cross_match_comparisons
            ),
            "duplicate_comparisons": (
                planned_duplicate_comparisons > args.max_duplicate_comparisons
            ),
            "inventory": inventory_budget_rejected,
            "component_extraction_or_matching": False,
        },
        "comparison_indexing": {
            "exact_decoded_sha256_index": True,
            "compact_identity_transform_signatures_precomputed": True,
            "quadratic_comparisons_allowed_only_within_explicit_limits": True,
        },
    }

    input_rows: list[dict[str, object] | None] = []
    output_rows: list[dict[str, object] | None] = []
    input_by_path: dict[Path, dict[str, object] | None] = {}
    output_by_path: dict[Path, dict[str, object] | None] = {}
    input_features: dict[Path, dict[str, object]] = {}
    output_features: dict[Path, dict[str, object]] = {}
    retained_features_bytes = 0
    retained_feature_budget_rejected = False
    component_budget_rejected = False
    dimensions: set[tuple[int, int]] = set()
    decode_workload_rejected = (
        page_count_rejected
        or total_feature_budget_rejected
        or inventory_budget_rejected
    )
    for path in inputs:
        captured: ImmutableFile | None = None
        data: bytes | bytearray | memoryview | None = None
        try:
            probe = image_probes.get(path)
            if (
                decode_workload_rejected
                or probe is None
                or probe["pixels"] > args.max_decoded_pixels_per_page
            ):
                raise ValueError(f"image analysis skipped by safety preflight: {path.name}")
            captured = capture_image_file(
                path,
                args.max_inventory_file_bytes,
            )
            if captured.snapshot != image_snapshots[path]:
                raise ValueError(f"image mutated after safety preflight: {path.name}")
            if not image_capture_still_published(captured):
                raise ValueError(f"image mutated after safety preflight: {path.name}")
            data = captured.data
            row, features = (
                metrics(path, data=data)
                if args.max_decoded_pixels_per_page
                == SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"]
                else metrics(
                    path, args.max_decoded_pixels_per_page, data
                )
            )
            input_rows.append(row)
            input_by_path[path] = row
            feature_bytes = retained_feature_bytes(features)
            if retained_features_bytes + feature_bytes > args.max_retained_feature_bytes:
                if not retained_feature_budget_rejected:
                    failures.append(
                        "aggregate retained-feature memory safety budget exceeded: "
                        f"planned={retained_features_bytes + feature_bytes}, "
                        f"limit={args.max_retained_feature_bytes}"
                    )
                    retained_feature_budget_rejected = True
            else:
                input_features[path] = features
                retained_features_bytes += feature_bytes
            if int(row["frame_count"]) != 1:
                failures.append(f"image is not a single decoded frame: {path.name} ({row['frame_count']} frames)")
            transparency = row["transparency"]
            if transparency["has_alpha"] and float(transparency["nonopaque_fraction"]) > 0:
                add_review(
                    review,
                    path.name,
                    "input contains transparent pixels; inspect white-composited decoded content",
                )
            if bool(transparency["exif_orientation_applied"]):
                add_review(review, path.name, "input EXIF orientation was applied by Pillow; verify displayed orientation")
            if int(transparency["sample_depth_bits"]) > 8:
                add_review(review, path.name, "input has greater-than-8-bit samples; verify tonal depth was reviewed")
            if not bool(transparency["mode_supported"]) or not bool(transparency["depth_supported"]):
                add_review(review, path.name, "input decoded mode or sample depth is unsupported; verify conversion and tonal integrity")
        except (OSError, ValueError) as error:
            failures.append(str(error))
            if isinstance(error, ComponentBudgetError):
                component_budget_rejected = True
                safety_budgets["observed"]["component_budget_failures"].append(
                    str(error)
                )
                add_review(
                    review,
                    path.name,
                    "component analysis exceeded its explicit safety budget; "
                    "inspect the page manually",
                )
            input_rows.append(None)
            input_by_path[path] = None
        finally:
            close_immutable_files(captured)
            data = None
            captured = None
    for path in outputs:
        captured = None
        data = None
        try:
            probe = image_probes.get(path)
            if (
                decode_workload_rejected
                or probe is None
                or probe["pixels"] > args.max_decoded_pixels_per_page
            ):
                raise ValueError(f"image analysis skipped by safety preflight: {path.name}")
            captured = capture_image_file(
                path,
                args.max_inventory_file_bytes,
            )
            if captured.snapshot != image_snapshots[path]:
                raise ValueError(f"image mutated after safety preflight: {path.name}")
            if not image_capture_still_published(captured):
                raise ValueError(f"image mutated after safety preflight: {path.name}")
            data = captured.data
            row, features = (
                metrics(path, data=data)
                if args.max_decoded_pixels_per_page
                == SAFETY_BUDGET_DEFAULTS["maximum_decoded_pixels_per_page"]
                else metrics(
                    path, args.max_decoded_pixels_per_page, data
                )
            )
            output_rows.append(row)
            output_by_path[path] = row
            feature_bytes = retained_feature_bytes(features)
            if retained_features_bytes + feature_bytes > args.max_retained_feature_bytes:
                if not retained_feature_budget_rejected:
                    failures.append(
                        "aggregate retained-feature memory safety budget exceeded: "
                        f"planned={retained_features_bytes + feature_bytes}, "
                        f"limit={args.max_retained_feature_bytes}"
                    )
                    retained_feature_budget_rejected = True
            else:
                output_features[path] = features
                retained_features_bytes += feature_bytes
            dimensions.add((int(row["width"]), int(row["height"])))
            if int(row["frame_count"]) != 1:
                failures.append(f"image is not a single decoded frame: {path.name} ({row['frame_count']} frames)")
            transparency = row["transparency"]
            if transparency["has_alpha"] and float(transparency["nonopaque_fraction"]) > 0:
                add_review(
                    review,
                    path.name,
                    "output contains transparent pixels; inspect white-composited decoded content and hidden transparency",
                )
            if bool(transparency["exif_orientation_applied"]):
                add_review(review, path.name, "output EXIF orientation was applied by Pillow; verify displayed orientation")
            if int(transparency["sample_depth_bits"]) > 8:
                add_review(review, path.name, "output has greater-than-8-bit samples; verify tonal depth was reviewed")
            if not bool(transparency["mode_supported"]) or not bool(transparency["depth_supported"]):
                add_review(review, path.name, "output decoded mode or sample depth is unsupported; verify conversion and tonal integrity")
        except (OSError, ValueError) as error:
            failures.append(str(error))
            if isinstance(error, ComponentBudgetError):
                component_budget_rejected = True
                safety_budgets["observed"]["component_budget_failures"].append(
                    str(error)
                )
                add_review(
                    review,
                    path.name,
                    "component analysis exceeded its explicit safety budget; "
                    "inspect the page manually",
                )
            output_rows.append(None)
            output_by_path[path] = None
        finally:
            close_immutable_files(captured)
            data = None
            captured = None
    if len(dimensions) > 1:
        failures.append(f"inconsistent output dimensions: {sorted(dimensions)}")
    safety_budgets["observed"]["retained_feature_bytes"] = retained_features_bytes
    safety_budgets["observed"]["peak_encoded_buffer_bytes"] = (
        peak_encoded_buffer_bytes
    )
    safety_budgets["rejected"]["retained_feature_memory"] = (
        retained_feature_budget_rejected
    )
    safety_budgets["rejected"]["component_extraction_or_matching"] = (
        component_budget_rejected
    )
    comparison_budget_rejected = (
        comparison_budget_rejected or retained_feature_budget_rejected
    )

    pairs: list[dict[str, object]] = []
    geometry_residuals: dict[str, list[tuple[str, float]]] = {
        "horizontal": [],
        "vertical_convergence_barline": [],
    }
    pairing = (
        {
            "strategy": "pairing skipped by directory safety preflight",
            "positional_pairing": False,
            "issues": [],
            "pairs": [],
            "manifest_sha256": (
                manifest_inventory.get("sha256")
                if manifest_inventory is not None
                else None
            ),
        }
        if page_count_rejected or inventory_budget_rejected
        else pair_pages(
            inputs, outputs, args.pairing_manifest, manifest_capture
        )
    )
    pairing_issues = list(pairing["issues"])
    if (
        args.pairing_manifest is not None
        and manifest_inventory is not None
        and pairing.get("manifest_sha256") != manifest_inventory.get("sha256")
    ):
        pairing_issues.append("pairing manifest mutated while its mapping was being loaded")
    if pairing_issues:
        failures.extend(f"pairing identity failure: {issue}" for issue in pairing_issues)
        add_review(
            review,
            "__batch_pairing__",
            "provide and verify an exact one-to-one page identity mapping",
        )
    paired_paths = list(pairing["pairs"])
    for index, (input_path, output_path) in enumerate(paired_paths, start=1):
        source = input_by_path[input_path]
        result = output_by_path[output_path]
        pair: dict[str, object] = {
            "page_index": index,
            "input_file": input_path.name,
            "output_file": output_path.name,
        }
        if (
            source is None
            or result is None
            or input_path not in input_features
            or output_path not in output_features
        ):
            pair["comparable"] = False
            if source is not None and result is not None:
                pair["comparison_skipped_by_retained_feature_memory_budget"] = True
            pairs.append(pair)
            continue
        pair["comparable"] = True
        resolution_comparison = physical_resolution_comparison(
            source, result, args.dpi_workflow_note
        )
        pair["physical_resolution_comparison"] = resolution_comparison
        if resolution_comparison["failure_required"]:
            failures.append(
                f"missing or materially changed DPI/physical size metadata: {output_path.name}"
            )
        if resolution_comparison["review_required"]:
            if resolution_comparison["missing_or_unreliable_metadata"]:
                detail = (
                    "under the documented unreliable-DPI workflow"
                    if args.dpi_workflow_note is not None
                    else "without a documented unreliable-DPI workflow"
                )
                add_review(
                    review,
                    output_path.name,
                    f"source/output DPI metadata is missing or unreliable {detail}; verify intended physical size",
                )
            else:
                add_review(
                    review,
                    output_path.name,
                    "DPI metadata or computed physical page size materially changed; verify intended print dimensions",
                )
        source_ink = float(source["ink_fraction"])
        output_ink = float(result["ink_fraction"])
        source_ink_pixels = int(source["ink_pixel_count"])
        output_ink_pixels = int(result["ink_pixel_count"])
        source_component_span = float(
            source["maximum_foreground_component_span_fraction"]
        )
        source_fine = input_features[input_path]["fine_components"]
        source_raw_ink = float(source_fine["raw_ink_fraction"])
        source_raw_ink_pixels = int(source_fine["raw_ink_pixel_count"])
        source_fine_ink = float(source_fine["aggregate_ink_fraction"])
        source_fine_ink_pixels = int(source_fine["aggregate_ink_pixel_count"])
        source_fine_count = int(source_fine["component_count"])
        blank_raw_ink_bounded = (
            source_raw_ink
            < THRESHOLDS["likely_blank_raw_ink_fraction_below"]
        )
        blank_fine_components_bounded = (
            source_fine_ink
            < THRESHOLDS["likely_blank_fine_component_ink_fraction_below"]
            and source_fine_count
            < THRESHOLDS["likely_blank_fine_component_count_below"]
        )
        likely_blank = blank_raw_ink_bounded and blank_fine_components_bounded and (
            source_ink < THRESHOLDS["likely_blank_source_ink_fraction_below"]
            or (
                source_ink < THRESHOLDS["likely_blank_dirt_ink_fraction_below"]
                and source_component_span
                < THRESHOLDS[
                    "likely_blank_dirt_maximum_component_span_fraction"
                ]
            )
        )
        content_scales = registered_content_scales(source, result)
        ink_registration = independent_geometric_scale(
            source,
            result,
            input_features[input_path],
            output_features[output_path],
        )
        if ink_registration.get("component_budget_exhausted"):
            component_budget_rejected = True
            component_failure = str(
                ink_registration["component_budget_failure"]
            )
            failures.append(component_failure)
            safety_budgets["observed"]["component_budget_failures"].append(
                component_failure
            )
            safety_budgets["rejected"][
                "component_extraction_or_matching"
            ] = True
        registered_ink_area_scale = float(ink_registration["area_scale"])
        scale_normalized_output_ink_pixels = (
            output_ink_pixels / registered_ink_area_scale
            if registered_ink_area_scale
            else 0.0
        )
        retention = (
            scale_normalized_output_ink_pixels / source_ink_pixels
            if source_ink_pixels
            else None
        )
        ink_loss = max(
            0.0, source_ink_pixels - scale_normalized_output_ink_pixels
        )
        ink_loss_fraction = ink_loss / (
            int(source["width"]) * int(source["height"])
        )
        pair.update(
            {
                "likely_blank": likely_blank,
                "source_ink_fraction": source_ink,
                "output_ink_fraction": output_ink,
                "source_ink_pixel_count": source_ink_pixels,
                "output_ink_pixel_count": output_ink_pixels,
                "source_raw_ink_fraction": source_raw_ink,
                "source_raw_ink_pixel_count": source_raw_ink_pixels,
                "source_fine_component_ink_fraction": source_fine_ink,
                "source_fine_component_ink_pixel_count": source_fine_ink_pixels,
                "source_fine_component_count": source_fine_count,
                "scale_normalized_output_ink_pixel_count": scale_normalized_output_ink_pixels,
                "registered_ink_area_scale": registered_ink_area_scale,
                "ink_scale_registration": ink_registration,
                "ink_retention_ratio": retention,
                "absolute_ink_loss": ink_loss_fraction,
                "ink_comparison_method": (
                    "connected foreground mass normalized by independently estimated "
                    "source-to-output feature/component registration, with the canvas "
                    "transform used only as a fail-closed fallback; output foreground "
                    "bounds do not determine the ink scale"
                ),
            }
        )
        if ink_registration["review_required"]:
            add_review(
                review,
                output_path.name,
                "ink scale/registration is unverified; conservative retention was used",
            )
        if likely_blank:
            add_review(
                review,
                output_path.name,
                "likely blank: confirm intentional blank and cleanliness",
            )
        fine_damage = mapped_fine_damage(
            source,
            result,
            input_features[input_path],
            output_features[output_path],
            ink_registration,
        )
        blank_fine_loss_suppression = (
            likely_blank
            and blank_raw_ink_bounded
            and blank_fine_components_bounded
        )
        fine_damage["blank_page_suppression_applied"] = (
            bool(fine_damage["failed"]) and blank_fine_loss_suppression
        )
        fine_damage["failed"] = (
            bool(fine_damage["failed"]) and not blank_fine_loss_suppression
        )
        pair["mapped_fine_component_damage"] = fine_damage
        if fine_damage["source_census_truncated"] or fine_damage[
            "output_census_truncated"
        ]:
            add_review(
                review,
                output_path.name,
                "fine-component census reached its compact retention limit; "
                "inspect isolated notation and punctuation",
            )
        if fine_damage["failed"]:
            failures.append(
                "mapped fine notation/punctuation loss: "
                f"{output_path.name} "
                f"({fine_damage['missing_component_count']} isolated component(s))"
            )
            add_review(
                review,
                output_path.name,
                "mapped multiscale decoded difference indicates deleted fine "
                "isolated notation or punctuation",
            )
        content = content_comparison(
            input_features[input_path], output_features[output_path]
        )
        pair["content_identity"] = content
        expected_similarity = float(content["expected_orientation_similarity"])
        best_orientation = str(content["best_orientation"])
        orientation_margin = float(content["orientation_margin"])
        corroborating_orientation_modalities: list[str] = []
        for modality, score_key in (
            ("decoded pixels", "pixel_orientation_scores"),
            ("local compact foreground", "local_orientation_scores"),
            ("color", "color_orientation_scores"),
        ):
            modality_scores = content[score_key]
            modality_best = max(modality_scores, key=modality_scores.get)
            modality_margin = float(modality_scores[modality_best]) - float(
                modality_scores["0"]
            )
            if (
                modality_best == best_orientation
                and float(modality_scores[modality_best])
                >= THRESHOLDS["orientation_corroborating_minimum_similarity"]
                and modality_margin
                >= THRESHOLDS["orientation_corroborating_minimum_margin"]
            ):
                corroborating_orientation_modalities.append(modality)
        orientation_candidate = (
            best_orientation != "0"
            and float(content["best_orientation_similarity"])
            >= THRESHOLDS["orientation_minimum_similarity"]
            and orientation_margin >= THRESHOLDS["orientation_minimum_margin"]
        )
        content["orientation_corroborating_modalities"] = (
            corroborating_orientation_modalities
        )
        content["orientation_decision"] = (
            "failure"
            if orientation_candidate and corroborating_orientation_modalities
            else "mandatory_review"
            if orientation_candidate
            else "no_rotation_signal"
        )
        if orientation_candidate and corroborating_orientation_modalities and not likely_blank:
            failures.append(
                f"content orientation mismatch: {output_path.name} best matches source after "
                + (
                    f"{best_orientation} degree rotation"
                    if best_orientation in {"90", "180", "270"}
                    else best_orientation.replace("_", " ")
                )
            )
            add_review(
                review,
                output_path.name,
                f"content appears transformed by {best_orientation.replace('_', ' ')}; corroborated by "
                + ", ".join(corroborating_orientation_modalities),
            )
        elif orientation_candidate:
            add_review(
                review,
                output_path.name,
                f"ambiguous orientation/reflection signal ({best_orientation.replace('_', ' ')}) from structural signature "
                "without independent color/pixel/local corroboration; repetitive content requires review",
            )
        elif expected_similarity < THRESHOLDS["content_similarity_failure_below"] and not likely_blank:
            failures.append(
                f"severe source/output structural mismatch: {output_path.name} ({expected_similarity:.3f})"
            )
            add_review(review, output_path.name, "severe structural mismatch; check substitution or damage")
        elif expected_similarity < THRESHOLDS["content_similarity_review_below"]:
            add_review(
                review,
                output_path.name,
                f"low source/output structural similarity ({expected_similarity:.3f}); inspect crop, damage, or substitution",
            )
        source_chroma = float(content["source_chroma_fraction"])
        output_chroma = float(content["output_chroma_fraction"])
        chroma_retention = output_chroma / source_chroma if source_chroma else None
        introduced_color = (
            source_chroma < THRESHOLDS["color_source_chroma_fraction_minimum"]
            and output_chroma
            >= THRESHOLDS["color_introduced_chroma_fraction_review_above"]
            and output_chroma - source_chroma
            >= THRESHOLDS["color_introduced_chroma_fraction_increase_review"]
        )
        color_similarity = float(content["expected_color_similarity"])
        spatial_color_similarity = float(
            content["expected_spatial_color_similarity"]
        )
        color_review = introduced_color or (
            source_chroma >= THRESHOLDS["color_source_chroma_fraction_minimum"]
            and (
                chroma_retention is None
                or chroma_retention < THRESHOLDS["color_chroma_retention_review_below"]
                or color_similarity < THRESHOLDS["color_signature_similarity_review_below"]
                or spatial_color_similarity
                < THRESHOLDS[
                    "color_spatial_signature_similarity_review_below"
                ]
            )
        )
        pair["color_fidelity"] = {
            "source_chroma_fraction": source_chroma,
            "output_chroma_fraction": output_chroma,
            "chroma_retention_ratio": chroma_retention,
            "color_signature_similarity": color_similarity,
            "spatial_color_signature_similarity": spatial_color_similarity,
            "introduced_color_review_required": introduced_color,
            "review_required": color_review,
        }
        if color_review:
            add_review(
                review,
                output_path.name,
                (
                    "grayscale or near-zero-chroma source gained material output chroma; inspect introduced color"
                    if introduced_color
                    else "global or spatial color-aware signature indicates regional hue/chroma loss, swap, or shift; inspect color fidelity even when luminance is unchanged"
                ),
            )
        source_tonal = source["background_tonal_quality"]
        output_tonal = result["background_tonal_quality"]
        assert isinstance(source_tonal, dict) and isinstance(output_tonal, dict)
        source_paper = source_tonal["paper_brightness_percentiles"]
        output_paper = output_tonal["paper_brightness_percentiles"]
        assert isinstance(source_paper, dict) and isinstance(output_paper, dict)
        brightness_drop = float(source_paper["p90"]) - float(output_paper["p90"])
        highlight_drop = float(source_paper["p99"]) - float(output_paper["p99"])
        source_range = float(source_tonal["paper_highlight_range_p99_minus_p50"])
        output_range = float(output_tonal["paper_highlight_range_p99_minus_p50"])
        range_retention = output_range / source_range if source_range > 0 else None
        unevenness_increase = float(
            output_tonal["local_paper_unevenness_p90_minus_p10"]
        ) - float(source_tonal["local_paper_unevenness_p90_minus_p10"])
        source_cast = source_tonal["background_color_cast_lab_chroma"]
        output_cast = output_tonal["background_color_cast_lab_chroma"]
        source_cast_baseline = 0.0 if source_cast is None else float(source_cast)
        cast_increase = (
            float(output_cast) - source_cast_baseline
            if output_cast is not None
            else None
        )
        darkened = (
            brightness_drop >= THRESHOLDS["paper_brightness_drop_review_gray"]
            and float(output_paper["p90"])
            <= THRESHOLDS["paper_output_p90_review_below"]
        )
        highlight_loss = (
            highlight_drop >= THRESHOLDS["paper_highlight_drop_review_gray"]
            or (
                range_retention is not None
                and source_range
                >= THRESHOLDS["paper_highlight_range_minimum_source_gray"]
                and range_retention
                < THRESHOLDS["paper_highlight_range_retention_review_below"]
                and highlight_drop >= 8.0
            )
        )
        dark_clipping = (
            float(output_tonal["paper_ceiling_value"])
            <= THRESHOLDS["paper_dark_clip_ceiling_review_below"]
            and float(output_tonal["paper_ceiling_fraction"])
            >= THRESHOLDS["paper_dark_clip_fraction_review_above"]
            and float(output_tonal["paper_ceiling_fraction"])
            - float(source_tonal["paper_ceiling_fraction"])
            >= THRESHOLDS["paper_dark_clip_fraction_increase_review"]
        )
        uneven = (
            float(output_tonal["local_paper_unevenness_p90_minus_p10"])
            >= THRESHOLDS["paper_local_unevenness_review_gray"]
            and unevenness_increase
            >= THRESHOLDS["paper_local_unevenness_increase_review_gray"]
        )
        color_cast = (
            output_cast is not None
            and cast_increase is not None
            and float(output_cast)
            >= THRESHOLDS["paper_color_cast_review_lab_chroma"]
            and cast_increase
            >= THRESHOLDS["paper_color_cast_increase_review_lab_chroma"]
        )
        pair["background_tonal_comparison"] = {
            "source": source_tonal,
            "output": output_tonal,
            "paper_p90_brightness_drop": brightness_drop,
            "paper_p99_highlight_drop": highlight_drop,
            "paper_highlight_range_retention_ratio": range_retention,
            "local_paper_unevenness_increase": unevenness_increase,
            "source_background_color_cast_baseline_lab_chroma": source_cast_baseline,
            "background_color_cast_increase_lab_chroma": cast_increase,
            "darkened_review_required": darkened,
            "highlight_loss_review_required": highlight_loss,
            "dark_clipping_review_required": dark_clipping,
            "unevenness_review_required": uneven,
            "color_cast_review_required": color_cast,
        }
        if darkened:
            add_review(
                review,
                output_path.name,
                f"paper/background materially darkened or became gray (p90 drop {brightness_drop:.1f})",
            )
        if highlight_loss:
            add_review(
                review,
                output_path.name,
                f"paper highlight range or brightness was materially lost (p99 drop {highlight_drop:.1f})",
            )
        if dark_clipping:
            add_review(
                review,
                output_path.name,
                "paper highlights appear clipped to a dark tonal ceiling",
            )
        if uneven:
            add_review(
                review,
                output_path.name,
                f"paper/background gained local tonal unevenness ({unevenness_increase:.1f} gray levels)",
            )
        if color_cast:
            add_review(
                review,
                output_path.name,
                f"paper/background gained a color cast ({cast_increase:.1f} Lab chroma)",
            )
        extreme_dark = (
            float(output_paper["p90"])
            <= THRESHOLDS["paper_extreme_dark_p90_failure_below"]
            and brightness_drop
            >= THRESHOLDS["paper_extreme_dark_drop_failure_gray"]
        )
        extreme_uneven = (
            float(output_tonal["local_paper_unevenness_p90_minus_p10"])
            >= THRESHOLDS["paper_extreme_unevenness_failure_gray"]
            and unevenness_increase
            >= THRESHOLDS["paper_extreme_unevenness_increase_failure_gray"]
        )
        extreme_cast = (
            output_cast is not None
            and cast_increase is not None
            and float(output_cast)
            >= THRESHOLDS["paper_extreme_color_cast_failure_lab_chroma"]
            and cast_increase
            >= THRESHOLDS["paper_extreme_color_cast_increase_failure_lab_chroma"]
        )
        if extreme_dark:
            failures.append(
                f"objective extreme paper/background darkening corruption: {output_path.name}"
            )
        if extreme_uneven:
            failures.append(
                f"objective extreme paper/background unevenness corruption: {output_path.name}"
            )
        if extreme_cast:
            failures.append(
                f"objective extreme paper/background color-cast corruption: {output_path.name}"
            )
        if likely_blank:
            if output_ink - source_ink > THRESHOLDS["blank_maximum_added_ink_fraction"]:
                failures.append(f"blank page gained excessive ink: {output_path.name}")
        else:
            complete_erasure = (
                output_ink
                <= THRESHOLDS["complete_erasure_output_ink_fraction_below"]
                and source_ink
                >= THRESHOLDS["likely_blank_source_ink_fraction_below"]
            )
            low_retention = (
                retention is not None
                and retention < THRESHOLDS["nonblank_minimum_ink_retention_ratio"]
                and ink_loss_fraction
                >= THRESHOLDS["nonblank_minimum_absolute_ink_loss_to_fail"]
            )
            if complete_erasure or low_retention:
                detail = "complete source ink erasure" if complete_erasure else "material source ink loss"
                failures.append(f"{detail}: {output_path.name}")
                add_review(review, output_path.name, "low source-to-output ink retention")

        border = result["border_dark_fractions"]
        source_border = source["border_dark_fractions"]
        border_analysis = result["border_analysis"]
        assert isinstance(border, dict) and isinstance(source_border, dict)
        assert isinstance(border_analysis, dict)
        flagged_bands = [
            name
            for name, value in border.items()
            if float(value) > THRESHOLDS["border_visual_review_dark_fraction"]
        ]
        severe_threshold = (
            THRESHOLDS["blank_border_severe_dark_fraction"]
            if likely_blank
            else THRESHOLDS["border_severe_dark_fraction"]
        )
        severe_bands = [name for name, value in border.items() if float(value) > severe_threshold]
        contamination_bands = []
        for name in severe_bands:
            analysis = border_analysis[name]
            assert isinstance(analysis, dict)
            increased = float(border[name]) - float(source_border[name])
            broad = (
                float(analysis["long_axis_breadth"])
                >= THRESHOLDS["border_minimum_long_axis_breadth"]
            )
            connected = (
                float(analysis["largest_component_fraction"])
                >= THRESHOLDS["border_minimum_connected_component_fraction"]
                and float(analysis["largest_component_span"])
                >= THRESHOLDS["border_minimum_connected_component_span"]
            )
            if (
                increased >= THRESHOLDS["border_minimum_dark_fraction_increase"]
                and broad
                and connected
            ):
                contamination_bands.append(name)
        pair["border_flagged_bands"] = flagged_bands
        pair["border_severe_bands"] = severe_bands
        pair["border_contamination_bands"] = contamination_bands
        decreased_bands = [
            name
            for name in border
            if float(source_border[name]) - float(border[name])
            >= THRESHOLDS["border_suspicious_dark_fraction_decrease"]
        ]
        pair["border_large_decrease_bands"] = decreased_bands
        source_edge_components = source["edge_component_analysis"]
        output_edge_components = result["edge_component_analysis"]
        assert isinstance(source_edge_components, dict)
        assert isinstance(output_edge_components, dict)
        source_bounds_for_edges = source["content_bounds"]
        output_bounds_for_edges = result["content_bounds"]
        edge_registration = None
        if source_bounds_for_edges["measurable"] and output_bounds_for_edges["measurable"]:
            source_width_fraction = float(source_bounds_for_edges["width_fraction"])
            source_height_fraction = float(source_bounds_for_edges["height_fraction"])
            if source_width_fraction > 0 and source_height_fraction > 0:
                x_scale = (
                    float(output_bounds_for_edges["width_fraction"])
                    / source_width_fraction
                )
                y_scale = (
                    float(output_bounds_for_edges["height_fraction"])
                    / source_height_fraction
                )
                edge_registration = {
                    "x_scale": x_scale,
                    "y_scale": y_scale,
                    "x_offset": float(output_bounds_for_edges["left_fraction"])
                    - float(source_bounds_for_edges["left_fraction"]) * x_scale,
                    "y_offset": float(output_bounds_for_edges["top_fraction"])
                    - float(source_bounds_for_edges["top_fraction"]) * y_scale,
                    "source_canvas_width": float(source["width"]),
                    "source_canvas_height": float(source["height"]),
                    "output_canvas_width": float(result["width"]),
                    "output_canvas_height": float(result["height"]),
                }
        try:
            edge_comparison = compare_edge_component_analysis(
                source_edge_components, output_edge_components, edge_registration
            )
        except ComponentBudgetError as error:
            component_budget_rejected = True
            component_failure = str(error)
            failures.append(component_failure)
            safety_budgets["observed"]["component_budget_failures"].append(
                component_failure
            )
            safety_budgets["rejected"][
                "component_extraction_or_matching"
            ] = True
            add_review(
                review,
                output_path.name,
                "edge component comparison exhausted its safety budget",
            )
            pair["edge_component_comparison_budget_exhausted"] = True
            pair["edge_component_comparison_budget_failure"] = component_failure
            pair["comparable"] = False
            pairs.append(pair)
            continue
        broad_edge_sides = edge_comparison["broad_connected_edge_sides"]
        new_edge_contamination_sides = edge_comparison[
            "new_edge_contamination_sides"
        ]
        scanner_strip_failure_sides = edge_comparison[
            "scanner_strip_failure_sides"
        ]
        removed_content_sides = edge_comparison[
            "potential_removed_content_sides"
        ]
        removed_content_failure_sides = edge_comparison[
            "removed_content_failure_sides"
        ]
        scanner_frame_failure = bool(edge_comparison["scanner_frame_failure"])
        new_rectangular_frame = bool(edge_comparison["new_rectangular_frame"])
        registration_adjusted_rectangular_frame = bool(
            edge_comparison["registration_adjusted_rectangular_frame"]
        )
        source_consistent_rectangular_frame = bool(
            edge_comparison["source_consistent_rectangular_frame"]
        )
        potential_removed_source_frame_or_border = bool(
            edge_comparison["potential_removed_source_frame_or_border"]
        )
        frame_evidence_truncated = bool(
            edge_comparison["frame_evidence_truncated"]
        )
        assert isinstance(broad_edge_sides, list)
        assert isinstance(new_edge_contamination_sides, list)
        assert isinstance(scanner_strip_failure_sides, list)
        assert isinstance(removed_content_sides, list)
        assert isinstance(removed_content_failure_sides, list)
        pair["edge_component_comparison"] = edge_comparison["sides"]
        pair["broad_connected_edge_sides"] = broad_edge_sides
        pair["new_edge_contamination_sides"] = new_edge_contamination_sides
        pair["scanner_strip_failure_sides"] = scanner_strip_failure_sides
        pair["potential_removed_edge_content_sides"] = removed_content_sides
        pair["removed_edge_content_failure_sides"] = (
            removed_content_failure_sides
        )
        pair["rectangular_frame_comparison"] = edge_comparison[
            "rectangular_frame_comparison"
        ]
        pair["new_rectangular_frame"] = new_rectangular_frame
        pair["scanner_frame_failure"] = scanner_frame_failure
        pair["registration_adjusted_rectangular_frame"] = (
            registration_adjusted_rectangular_frame
        )
        pair["source_consistent_rectangular_frame"] = (
            source_consistent_rectangular_frame
        )
        pair["potential_removed_source_frame_or_border"] = (
            potential_removed_source_frame_or_border
        )
        pair["frame_evidence_truncated"] = frame_evidence_truncated
        shifted_edge_sides = [
            side
            for side, comparison in edge_comparison["sides"].items()
            if any(
                item["uncertain_shifted_component"]
                for item in comparison["component_comparisons"]
            )
        ]
        pair["registration_adjusted_edge_sides"] = shifted_edge_sides
        if flagged_bands:
            add_review(
                review,
                output_path.name,
                "edge-band ink; distinguish legitimate content/music braces from dirt",
            )
        if severe_bands and not contamination_bands:
            add_review(
                review,
                output_path.name,
                "dense edge content is unchanged or not spatially contamination-like; inspect notation/cover",
            )
        if contamination_bands:
            add_review(
                review,
                output_path.name,
                "new broad connected edge artifact; verify contamination versus shifted/cropped legitimate content "
                f"({', '.join(contamination_bands)})",
            )
        if broad_edge_sides and not new_edge_contamination_sides:
            add_review(
                review,
                output_path.name,
                "broad connected edge content is source-consistent; verify legitimate staff, page number, border, or cover content "
                f"({', '.join(broad_edge_sides)})",
            )
        if new_edge_contamination_sides:
            add_review(
                review,
                output_path.name,
                "new source-relative broad connected edge contamination, including inset strips; inspect against source "
                f"({', '.join(new_edge_contamination_sides)})",
            )
        if shifted_edge_sides:
            add_review(
                review,
                output_path.name,
                "edge components match only after global content registration/canvas shift; "
                f"review without treating as new scanner-strip evidence ({', '.join(shifted_edge_sides)})",
            )
        if scanner_strip_failure_sides:
            failures.append(
                "new high-confidence black scanner strip: "
                f"{output_path.name} ({', '.join(scanner_strip_failure_sides)})"
            )
        if scanner_frame_failure:
            failures.append(
                "new high-confidence four-sided black scanner frame: "
                f"{output_path.name}"
            )
            add_review(
                review,
                output_path.name,
                "four-sided side-projection/perpendicular frame evidence is new "
                "or materially enlarged; compare all corners and sides against source",
            )
        elif new_rectangular_frame:
            add_review(
                review,
                output_path.name,
                "new four-sided rectangular edge geometry requires source comparison",
            )
        if registration_adjusted_rectangular_frame:
            add_review(
                review,
                output_path.name,
                "four-sided rectangular frame or page border matches only after "
                "global content registration; verify transformed preservation",
            )
        elif source_consistent_rectangular_frame:
            add_review(
                review,
                output_path.name,
                "four-sided rectangular frame or page border is source-consistent; "
                "verify legitimate border versus preserved scanner frame",
            )
        if potential_removed_source_frame_or_border:
            add_review(
                review,
                output_path.name,
                "source four-sided frame or page border is unmatched; verify intended "
                "scanner-frame cleanup versus removed page border",
            )
        if frame_evidence_truncated:
            add_review(
                review,
                output_path.name,
                "rectangular-frame candidate evidence reached its retention limit; "
                "inspect all four edge zones",
            )
        if removed_content_sides:
            add_review(
                review,
                output_path.name,
                "unmatched source-side broad connected component in the inspected outer 12%; "
                "verify crop/padding versus removed page content "
                f"({', '.join(removed_content_sides)})",
            )
        if removed_content_failure_sides:
            failures.append(
                "high-confidence removed source edge content: "
                f"{output_path.name} ({', '.join(removed_content_failure_sides)})"
            )
        if decreased_bands:
            add_review(
                review,
                output_path.name,
                "large edge-content decrease; verify legitimate cleaning versus crop/content loss "
                f"({', '.join(decreased_bands)})",
            )

        source_geometry = source["geometry"]
        output_geometry = result["geometry"]
        assert isinstance(source_geometry, dict) and isinstance(output_geometry, dict)
        geometry: dict[str, object] = {}
        axis_labels = {
            "horizontal": "horizontal skew",
            "vertical_convergence_barline": "vertical convergence/barline",
        }
        for axis, label in axis_labels.items():
            source_axis = source_geometry[axis]
            output_axis = output_geometry[axis]
            axis_comparison: dict[str, object] = {
                "source": source_axis,
                "output": output_axis,
                "worsened": None,
            }
            if output_axis["measurable"]:
                output_residual = float(output_axis["residual_degrees"])
                geometry_residuals[axis].append((output_path.name, output_residual))
                if output_residual > THRESHOLDS["geometry_residual_outlier_degrees"]:
                    add_review(
                        review,
                        output_path.name,
                        f"output {label} residual exceeds 0.50 degrees ({output_residual:.3f})",
                    )
            if source_axis["measurable"] and output_axis["measurable"]:
                source_residual = float(source_axis["residual_degrees"])
                worsened = (
                    output_residual - source_residual
                    > THRESHOLDS["geometry_worsening_degrees"]
                    and output_residual
                    > source_residual * THRESHOLDS["geometry_worsening_ratio"]
                )
                axis_comparison["worsened"] = worsened
                if worsened:
                    failures.append(f"{label} residual worsened: {output_path.name}")
                    add_review(review, output_path.name, f"measured {label} residual worsened")
            else:
                axis_comparison["comparison_review_required"] = True
                add_review(
                    review,
                    output_path.name,
                    f"{label} worsening comparison is unmeasurable",
                )
            geometry[axis] = axis_comparison

        source_aspect = int(source["width"]) / int(source["height"])
        output_aspect = int(result["width"]) / int(result["height"])
        canvas_anisotropy = abs(output_aspect / source_aspect - 1.0)
        source_bounds = source["content_bounds"]
        output_bounds = result["content_bounds"]
        bounds_measurable = bool(content_scales["bounds_measurable"])
        horizontal_scale = content_scales["bbox_x"]
        vertical_scale = content_scales["bbox_y"]
        content_anisotropy = (
            abs(float(horizontal_scale) / float(vertical_scale) - 1.0)
            if horizontal_scale is not None and vertical_scale else None
        )
        projection_x = content_scales["projection_x"]
        projection_y = content_scales["projection_y"]
        projection_measurable = bool(
            projection_x["measurable"]
            and projection_y["measurable"]
            and content_scales["projection_physical_y"]
            and float(content_scales["projection_physical_y"]) > 0
        )
        projection_anisotropy = (
            abs(
                float(content_scales["projection_physical_x"])
                / float(content_scales["projection_physical_y"])
                - 1.0
            )
            if projection_measurable
            else None
        )
        anisotropy = max(
            float(content_anisotropy) if content_anisotropy is not None else 0.0,
            float(projection_anisotropy)
            if projection_anisotropy is not None
            else 0.0,
        )
        margin_change = canvas_anisotropy > THRESHOLDS[
            "anisotropic_stretch_review_fraction"
        ]
        anisotropic_stretch = {
            "measurable": bounds_measurable or projection_measurable,
            "source_aspect_ratio": source_aspect,
            "output_aspect_ratio": output_aspect,
            "canvas_residual_fraction": canvas_anisotropy,
            "canvas_margin_change_review_required": margin_change,
            "source_content_bounds": source_bounds,
            "output_content_bounds": output_bounds,
            "horizontal_content_scale": horizontal_scale,
            "vertical_content_scale": vertical_scale,
            "content_bbox_residual_fraction": content_anisotropy,
            "projection_landmark_x": projection_x,
            "projection_landmark_y": projection_y,
            "projection_landmark_residual_fraction": projection_anisotropy,
            "residual_fraction": anisotropy,
            "review_required": anisotropy > THRESHOLDS["anisotropic_stretch_review_fraction"],
            "failed": anisotropy > THRESHOLDS["anisotropic_stretch_failure_fraction"],
        }
        if anisotropic_stretch["review_required"]:
            add_review(
                review,
                output_path.name,
                f"registered content/internal-landmark anisotropic-stretch residual ({anisotropy:.3%})",
            )
        elif margin_change:
            add_review(
                review,
                output_path.name,
                "canvas aspect changed while registered content scale remained "
                "non-anisotropic; verify intentional blank-margin crop or padding",
            )
        if anisotropic_stretch["failed"]:
            failures.append(
                f"registered content/internal-landmark anisotropic stretch exceeds limit: {output_path.name}"
            )
        geometry["anisotropic_stretch"] = anisotropic_stretch
        registration: dict[str, object] = {
            "measurable": bounds_measurable,
            "source_content_bounds": source_bounds,
            "output_content_bounds": output_bounds,
            "review_required": False,
        }
        if bounds_measurable:
            edge_deltas = {
                edge: float(output_bounds[f"{edge}_fraction"])
                - float(source_bounds[f"{edge}_fraction"])
                for edge in ("left", "top", "right", "bottom")
            }
            centroid_delta_x = (
                float(output_bounds["centroid_x_fraction"])
                - float(source_bounds["centroid_x_fraction"])
            )
            centroid_delta_y = (
                float(output_bounds["centroid_y_fraction"])
                - float(source_bounds["centroid_y_fraction"])
            )
            centroid_shift = float(np.hypot(centroid_delta_x, centroid_delta_y))
            isotropic_scale = float(
                np.sqrt(float(horizontal_scale) * float(vertical_scale))
            )
            scale_change = abs(isotropic_scale - 1.0)
            crop_or_bbox_shift = max(abs(value) for value in edge_deltas.values())
            registration_residual = max(
                centroid_shift, scale_change, crop_or_bbox_shift
            )
            registration.update(
                {
                    "normalized_bbox_edge_deltas": edge_deltas,
                    "normalized_centroid_delta": {
                        "x": centroid_delta_x,
                        "y": centroid_delta_y,
                        "distance": centroid_shift,
                    },
                    "isotropic_content_scale": isotropic_scale,
                    "isotropic_scale_change_fraction": scale_change,
                    "crop_or_bbox_shift_fraction": crop_or_bbox_shift,
                    "residual_fraction": registration_residual,
                    "review_required": registration_residual
                    > THRESHOLDS["foreground_registration_review_fraction"],
                }
            )
            if registration["review_required"]:
                add_review(
                    review,
                    output_path.name,
                    "normalized foreground bbox/centroid/isotropic-scale shift or crop "
                    f"is material ({registration_residual:.3%}); inspect registration and content loss",
                )
        else:
            registration["comparison_review_required"] = True
            add_review(
                review,
                output_path.name,
                "normalized foreground bbox/centroid/scale comparison is unmeasurable",
            )
        geometry["foreground_registration"] = registration
        pair["geometry"] = geometry
        pairs.append(pair)

    comparable_pair_records = [
        (pair, input_path, output_path)
        for pair, (input_path, output_path) in zip(
            pairs, paired_paths, strict=True
        )
        if pair.get("comparable")
    ]
    if comparison_budget_rejected:
        comparable_pair_records = []
    comparable_path_pairs = [
        (input_path, output_path)
        for _, input_path, output_path in comparable_pair_records
    ]
    source_signatures = {
        path: features["structure_signatures"]["0"]
        for path, features in input_features.items()
    }
    output_signatures = {
        path: features["structure_signatures"]
        for path, features in output_features.items()
    }
    source_pixel_signatures = {
        path: features["pixel_signatures"]["0"]
        for path, features in input_features.items()
    }
    output_pixel_signatures = {
        path: features["pixel_signatures"]
        for path, features in output_features.items()
    }
    source_local_signatures = {
        path: features["local_signatures"]["0"]
        for path, features in input_features.items()
    }
    output_local_signatures = {
        path: features["local_signatures"]
        for path, features in output_features.items()
    }
    mapped_source_by_output = {
        output_path: input_path for input_path, output_path in comparable_path_pairs
    }
    comparable_pair_by_output = {
        output_path: pair
        for pair, _, output_path in comparable_pair_records
    }
    decoded_sha_input_index: dict[str, list[Path]] = {}
    for source_path, source_row in input_by_path.items():
        if source_row is not None:
            decoded_sha_input_index.setdefault(
                str(source_row["decoded_content_sha256"]), []
            ).append(source_path)
    decoded_identity_summary: list[dict[str, object]] = []
    retained_scores_by_output: dict[Path, list[tuple[Path, float, str]]] = {}
    identity_score_retention_truncated = False
    exact_alternate_retention_truncated = False
    duplicate_decisions: list[dict[str, object]] = []
    duplicate_decisions_omitted = 0
    duplicate_candidate_count = 0
    duplicate_failure_count = 0
    duplicate_review_only_count = 0
    duplicate_blank_cleanup_review_only_count = 0
    duplicate_affected_outputs: set[str] = set()
    duplicate_failure_examples: list[str] = []
    duplicate_review_examples: list[str] = []
    performed_cross_match_comparisons = 0
    performed_duplicate_comparisons = 0
    for output_path in (
        output_features if not comparison_budget_rejected else {}
    ):
        performed_cross_match_comparisons += len(source_signatures)
        scored = [
            (
                source_path,
                *orientation_invariant_similarity(
                    source_signature, output_signatures[output_path]
                ),
            )
            for source_path, source_signature in source_signatures.items()
        ]
        scores = sorted(
            scored,
            key=lambda item: item[1],
            reverse=True,
        )
        retained_scores = scores[
            :MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT
        ]
        mapped_source = mapped_source_by_output.get(output_path)
        if (
            mapped_source is not None
            and all(item[0] != mapped_source for item in retained_scores)
        ):
            retained_scores.append(
                next(item for item in scores if item[0] == mapped_source)
            )
        retained_scores_by_output[output_path] = retained_scores
        truncated = len(retained_scores) < len(scores)
        identity_score_retention_truncated |= truncated
        decoded_identity_summary.append(
            {
                "output": output_path.name,
                "mapped_source": (
                    mapped_source.name
                    if mapped_source is not None
                    else None
                ),
                "evaluated_source_count": len(scores),
                "retained_candidate_count": len(retained_scores),
                "retention_truncated": truncated,
                "retained_candidates": [
                    {
                        "input": source_path.name,
                        "similarity": score,
                        "rotation": int(rotation) if rotation.isdigit() else None,
                        "transformation": rotation,
                    }
                    for source_path, score, rotation in retained_scores
                ],
            }
        )
        if output_path not in mapped_source_by_output and scores:
            add_review(
                review,
                output_path.name,
                f"unpaired output decoded-content best match is {scores[0][0].name} "
                f"({scores[0][1]:.3f}); resolve page identity",
            )
    for pair, input_path, output_path in comparable_pair_records:
        retained_scores = retained_scores_by_output[output_path]
        alternate_scores = [
            (candidate, score, rotation)
            for candidate, score, rotation in retained_scores
            if candidate != input_path
        ]
        if alternate_scores:
            alternate_path, alternate_score, alternate_rotation = alternate_scores[0]
            paired_entry = next(
                item for item in retained_scores if item[0] == input_path
            )
            paired_score = float(paired_entry[1])
            output_row = output_by_path[output_path]
            input_row = input_by_path[input_path]
            exact_alternates = [
                candidate
                for candidate in (
                    decoded_sha_input_index.get(
                        str(output_row["decoded_content_sha256"]), []
                    )
                    if output_row is not None
                    else []
                )
                if candidate != input_path
            ]
            pair["cross_page_identity"] = {
                "evaluated_source_count": len(source_signatures),
                "retained_evidence": (
                    "paired candidate and strongest alternate; all source scores "
                    "were evaluated but nondecisive scores are not serialized"
                ),
                "strongest_alternate_input": alternate_path.name,
                "strongest_alternate_similarity": alternate_score,
                "strongest_alternate_rotation": (
                    int(alternate_rotation) if alternate_rotation.isdigit() else None
                ),
                "strongest_alternate_transformation": alternate_rotation,
                "paired_similarity": paired_score,
                "paired_best_rotation": (
                    int(paired_entry[2]) if paired_entry[2].isdigit() else None
                ),
                "paired_best_transformation": paired_entry[2],
                "exact_decoded_alternate_count": len(exact_alternates),
                "exact_decoded_alternate_inputs": [
                    candidate.name
                    for candidate in exact_alternates[
                        :MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT
                    ]
                ],
                "exact_decoded_alternates_truncated": (
                    len(exact_alternates)
                    > MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT
                ),
            }
            if pair["cross_page_identity"][
                "exact_decoded_alternates_truncated"
            ]:
                exact_alternate_retention_truncated = True
                add_review(
                    review,
                    "__batch_identity__",
                    "exact decoded alternate identity evidence was truncated; "
                    "review all duplicate source identities before approval",
                )
            paired_pixel, _ = orientation_invariant_pixel_similarity(
                source_pixel_signatures[input_path],
                output_pixel_signatures[output_path],
            )
            alternate_pixel, _ = orientation_invariant_pixel_similarity(
                source_pixel_signatures[alternate_path],
                output_pixel_signatures[output_path],
            )
            paired_local, _ = orientation_invariant_similarity(
                source_local_signatures[input_path],
                output_local_signatures[output_path],
            )
            alternate_local, _ = orientation_invariant_similarity(
                source_local_signatures[alternate_path],
                output_local_signatures[output_path],
            )
            alternate_corroboration = [
                name
                for name, alternate_value, paired_value in (
                    ("decoded pixels", alternate_pixel, paired_pixel),
                    ("local compact foreground", alternate_local, paired_local),
                )
                if alternate_value >= THRESHOLDS["substitution_minimum_similarity"]
                and alternate_value - paired_value
                >= THRESHOLDS["substitution_minimum_margin"] / 2
            ]
            pair["cross_page_identity"]["corroboration"] = {
                "paired_pixel_similarity": paired_pixel,
                "alternate_pixel_similarity": alternate_pixel,
                "paired_local_similarity": paired_local,
                "alternate_local_similarity": alternate_local,
                "alternate_favoring_modalities": alternate_corroboration,
            }
            if exact_alternates and (
                input_row is None
                or output_row["decoded_content_sha256"]
                != input_row["decoded_content_sha256"]
            ):
                failures.append(
                    f"probable substituted page: {output_path.name} exactly decodes as "
                    f"{exact_alternates[0].name}"
                )
                add_review(
                    review,
                    output_path.name,
                    f"exact decoded substitution from {exact_alternates[0].name}",
                )
            elif (
                not pair["likely_blank"]
                and
                alternate_score >= THRESHOLDS["substitution_minimum_similarity"]
                and alternate_score - paired_score >= THRESHOLDS["substitution_minimum_margin"]
                and alternate_corroboration
            ):
                failures.append(
                    f"probable substituted page: {output_path.name} matches {alternate_path.name} "
                    f"better than {input_path.name}"
                )
                add_review(
                    review,
                    output_path.name,
                    f"probable substitution from {alternate_path.name}",
                )
            elif (
                alternate_score >= THRESHOLDS["substitution_minimum_similarity"]
                and alternate_score - paired_score >= THRESHOLDS["substitution_minimum_margin"]
            ):
                add_review(
                    review,
                    output_path.name,
                    f"uncorroborated structural substitution signal favoring {alternate_path.name}; "
                    "inspect mapping and repetitive content",
                )
            elif (
                alternate_score >= THRESHOLDS["substitution_minimum_similarity"]
                and abs(alternate_score - paired_score)
                < THRESHOLDS["substitution_minimum_margin"]
            ):
                add_review(
                    review,
                    output_path.name,
                    f"ambiguous decoded-content identity: also matches {alternate_path.name} "
                    f"({alternate_score:.3f} versus paired {paired_score:.3f})",
                )

    for left_index, (left_input, left_output) in enumerate(comparable_path_pairs):
        for right_input, right_output in comparable_path_pairs[left_index + 1:]:
            if mapped_source_by_output[left_output] == mapped_source_by_output[right_output]:
                continue
            performed_duplicate_comparisons += 1
            similarity, structure_rotation = orientation_invariant_similarity(
                output_signatures[left_output]["0"], output_signatures[right_output]
            )
            pixel_similarity, pixel_rotation = orientation_invariant_pixel_similarity(
                output_pixel_signatures[left_output]["0"],
                output_pixel_signatures[right_output],
            )
            color_similarity, color_rotation = orientation_invariant_similarity(
                output_features[left_output]["spatial_color_signatures"]["0"],
                output_features[right_output]["spatial_color_signatures"],
            )
            left_row = output_by_path[left_output]
            right_row = output_by_path[right_output]
            exact_decoded_match = (
                left_row is not None
                and right_row is not None
                and left_row["decoded_content_sha256"]
                == right_row["decoded_content_sha256"]
            )
            if (
                exact_decoded_match
                or (
                    (
                        similarity >= THRESHOLDS["perceptual_duplicate_similarity"]
                        or pixel_similarity
                        >= THRESHOLDS["decoded_pixel_duplicate_similarity"]
                    )
                    and color_similarity
                    >= THRESHOLDS["perceptual_duplicate_similarity"]
                )
            ):
                duplicate_candidate_count += 1
                duplicate_affected_outputs.update(
                    (left_output.name, right_output.name)
                )
                source_similarity, _ = orientation_invariant_similarity(
                    source_signatures[left_input],
                    input_features[right_input]["structure_signatures"],
                )
                source_pixel_similarity, _ = (
                    orientation_invariant_pixel_similarity(
                        source_pixel_signatures[left_input],
                        input_features[right_input]["pixel_signatures"],
                    )
                )
                left_input_row = input_by_path[left_input]
                right_input_row = input_by_path[right_input]
                exact_source_decoded_match = (
                    left_input_row is not None
                    and right_input_row is not None
                    and left_input_row["decoded_content_sha256"]
                    == right_input_row["decoded_content_sha256"]
                )
                source_color_similarity, _ = orientation_invariant_similarity(
                    input_features[left_input]["spatial_color_signatures"]["0"],
                    input_features[right_input]["spatial_color_signatures"],
                )
                source_equality_established = exact_source_decoded_match
                left_pair = comparable_pair_by_output[left_output]
                right_pair = comparable_pair_by_output[right_output]
                blank_cleanup_exception = (
                    exact_decoded_match
                    and not source_equality_established
                    and all(
                        bool(pair["likely_blank"])
                        and float(pair["source_raw_ink_fraction"])
                        < THRESHOLDS["likely_blank_raw_ink_fraction_below"]
                        and float(pair["source_fine_component_ink_fraction"])
                        < THRESHOLDS[
                            "likely_blank_fine_component_ink_fraction_below"
                        ]
                        and int(pair["source_fine_component_count"])
                        < THRESHOLDS["likely_blank_fine_component_count_below"]
                        and int(pair["output_ink_pixel_count"])
                        < int(pair["source_ink_pixel_count"])
                        for pair in (left_pair, right_pair)
                    )
                )
                duplicate_decision = {
                    "left_output": left_output.name,
                    "right_output": right_output.name,
                    "left_source": left_input.name,
                    "right_source": right_input.name,
                    "output_exact_decoded_match": exact_decoded_match,
                    "output_structure_similarity": similarity,
                    "output_pixel_similarity": pixel_similarity,
                    "output_color_similarity": color_similarity,
                    "output_structure_transformation": structure_rotation,
                    "output_pixel_transformation": pixel_rotation,
                    "output_color_transformation": color_rotation,
                    "source_exact_decoded_match": exact_source_decoded_match,
                    "source_pixel_similarity": source_pixel_similarity,
                    "source_color_similarity": source_color_similarity,
                    "source_equality_established": source_equality_established,
                    "source_equality_method": (
                        "canonical native decoded SHA-256"
                        if source_equality_established
                        else None
                    ),
                    "blank_dirt_cleanup_review_exception": blank_cleanup_exception,
                    "blank_dirt_cleanup_exception_basis": (
                        "both independently likely-blank sources have bounded raw "
                        "and fine-component ink, and each exact duplicate output "
                        "contains less foreground than its paired source"
                        if blank_cleanup_exception
                        else None
                    ),
                }
                if (
                    len(duplicate_decisions)
                    < MAXIMUM_RETAINED_DUPLICATE_DECISIONS
                ):
                    duplicate_decisions.append(duplicate_decision)
                else:
                    duplicate_decisions_omitted += 1
                example = f"{left_output.name}~{right_output.name}"
                if not source_equality_established and not blank_cleanup_exception:
                    duplicate_failure_count += 1
                    if len(duplicate_failure_examples) < MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES:
                        duplicate_failure_examples.append(example)
                else:
                    duplicate_review_only_count += 1
                    if blank_cleanup_exception:
                        duplicate_blank_cleanup_review_only_count += 1
                    if len(duplicate_review_examples) < MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES:
                        duplicate_review_examples.append(example)
    if duplicate_failure_count:
        failures.append(
            "duplicated output content: "
            f"{duplicate_failure_count} near-identical decoded signatures pair(s) mapped to "
            "distinct, non-equal source identities; examples="
            + ", ".join(duplicate_failure_examples)
        )
    if duplicate_candidate_count:
        review_examples = duplicate_failure_examples + [
            item for item in duplicate_review_examples
            if item not in duplicate_failure_examples
        ]
        add_review(
            review,
            "__batch_identity__",
            "grouped near-identical decoded-content review: "
            f"{duplicate_candidate_count} candidate pair(s), "
            f"{duplicate_failure_count} mechanical failure pair(s), "
            f"{duplicate_review_only_count} review-only pair(s) "
            f"({duplicate_blank_cleanup_review_only_count} independently blank "
            "dirt-cleanup pair(s)), "
            f"{len(duplicate_affected_outputs)} affected output(s); examples="
            + ", ".join(review_examples[:MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES])
            + "; inspect retained duplicate_decisions for decisive similarities "
            "and source-equality evidence",
        )
    if duplicate_decisions_omitted:
        add_review(
            review,
            "__batch_identity__",
            "decisive duplicate-candidate evidence exceeded the retained limit; "
            "review the full batch for duplicate identities before approval",
        )
    safety_budgets["observed"]["performed_cross_match_comparisons"] = (
        performed_cross_match_comparisons
    )
    safety_budgets["observed"]["performed_duplicate_comparisons"] = (
        performed_duplicate_comparisons
    )

    geometry_summary: dict[str, object] = {}
    for axis, residuals in geometry_residuals.items():
        geometry_values = np.asarray([item[1] for item in residuals], dtype=float)
        geometry_median = float(np.median(geometry_values)) if geometry_values.size else None
        geometry_mad = (
            float(np.median(np.abs(geometry_values - geometry_median)))
            if geometry_values.size else None
        )
        geometry_outlier_threshold = (
            max(
                THRESHOLDS["geometry_residual_outlier_degrees"],
                geometry_median + 3 * 1.4826 * geometry_mad,
            )
            if geometry_values.size
            else THRESHOLDS["geometry_residual_outlier_degrees"]
        )
        geometry_outliers: list[dict[str, object]] = []
        for name, residual in residuals:
            if residual > geometry_outlier_threshold:
                geometry_outliers.append({"file": name, "residual_degrees": residual})
                add_review(
                    review, name,
                    f"{axis.replace('_', ' ')} residual outlier ({residual:.3f} degrees)",
                )
        geometry_summary[axis] = {
            "measurable_page_count": len(residuals),
            "median_output_residual_degrees": geometry_median,
            "mad_output_residual_degrees": geometry_mad,
            "outlier_threshold_degrees": geometry_outlier_threshold,
            "outliers": geometry_outliers,
        }

    visual_categories: dict[str, list[str]] = {
        "cover_or_first_last": [],
        "likely_blank": [],
        "text_heavy_candidate": [],
        "music_or_dense_structure_candidate": [],
        "beginning_middle_end": [],
        "metric_flags": [],
    }
    representative_outputs = [output for _, output in paired_paths] or outputs
    if representative_outputs:
        representative_indices = sorted(
            {0, len(representative_outputs) // 2, len(representative_outputs) - 1}
        )
        labels = {
            0: "beginning",
            len(representative_outputs) // 2: "middle",
            len(representative_outputs) - 1: "end",
        }
        for index in representative_indices:
            selected = representative_outputs[index]
            add_review(review, selected.name, f"representative {labels[index]} page")
            visual_categories["beginning_middle_end"].append(selected.name)
        for index, label in (
            (0, "first page or front cover"),
            (len(representative_outputs) - 1, "last page or back cover"),
        ):
            selected = representative_outputs[index]
            add_review(review, selected.name, label)
            if selected.name not in visual_categories["cover_or_first_last"]:
                visual_categories["cover_or_first_last"].append(selected.name)

    comparable_pairs = [pair for pair in pairs if pair.get("comparable")]
    blank_names = [str(pair["output_file"]) for pair in comparable_pairs if pair.get("likely_blank")]
    visual_categories["likely_blank"] = blank_names
    nonblank_pairs = [pair for pair in comparable_pairs if not pair.get("likely_blank")]
    if nonblank_pairs:
        text_candidate = max(
            nonblank_pairs, key=lambda pair: float(pair["source_ink_fraction"])
        )
        text_name = str(text_candidate["output_file"])
        add_review(review, text_name, "text-heavy candidate selected by source ink density")
        visual_categories["text_heavy_candidate"].append(text_name)
        dense_candidate = max(
            nonblank_pairs,
            key=lambda pair: int(
                output_by_path[
                    next(path for path in outputs if path.name == pair["output_file"])
                ]["structure"]["long_horizontal_structure_count"]
            ),
        )
        dense_name = str(dense_candidate["output_file"])
        dense_row = next(row for row in output_rows if row and row["file"] == dense_name)
        dense_count = int(dense_row["structure"]["long_horizontal_structure_count"])
        if dense_count >= 5:
            add_review(
                review,
                dense_name,
                f"music/dense-structure candidate ({dense_count} long horizontal structures)",
            )
            visual_categories["music_or_dense_structure_candidate"].append(dense_name)

    visual_review_pages = [
        {"file": page, "reasons": reasons}
        for page, reasons in sorted(review.items(), key=lambda item: natural_key(Path(item[0])))
    ]
    category_reasons = {
        reason
        for reasons in review.values()
        for reason in reasons
        if not reason.startswith("representative ")
        and "cover" not in reason
        and "first page" not in reason
        and "last page" not in reason
        and "candidate" not in reason
    }
    visual_categories["metric_flags"] = sorted(
        page
        for page, reasons in review.items()
        if any(reason in category_reasons for reason in reasons)
    )
    mechanical_pass = not failures
    visual_review_required = bool(visual_review_pages)
    pairing_record = {
        "strategy": pairing["strategy"],
        "positional_pairing": pairing["positional_pairing"],
        "issues": pairing_issues,
        "manifest": str(args.pairing_manifest) if args.pairing_manifest else None,
        "manifest_sha256": pairing.get("manifest_sha256"),
        "manifest_inventory": manifest_inventory,
        "input_order": [path.name for path in inputs],
        "output_order": [path.name for path in outputs],
        "map": [
            {"input": input_path.name, "output": output_path.name}
            for input_path, output_path in paired_paths
        ],
    }
    evidence_payload = {
        "schema_version": 19,
        "thresholds": THRESHOLDS,
        "safety_budgets": safety_budgets,
        "approval_run_argument_template": [
            "INPUT_DIR",
            "OUTPUT_DIR",
            *(
                ["--pairing-manifest", "PAIRING_MANIFEST"]
                if args.pairing_manifest is not None
                else []
            ),
            *(
                ["--dpi-workflow-note", args.dpi_workflow_note]
                if args.dpi_workflow_note is not None
                else []
            ),
            *custom_budget_arguments,
            "--evidence-report",
            "MECHANICAL_EVIDENCE.json",
            "--approval",
            "APPROVAL.json",
            "--final-report",
            "FINAL_REPORT.json",
        ],
        "candidate_evidence": candidate_evidence,
        "input_output_file_identity_aliases": file_identity_aliases,
        "pairing": pairing_record,
        "dpi_workflow_note": args.dpi_workflow_note,
        "input_count": len(inputs),
        "output_count": len(outputs),
        "output_dimensions": [list(item) for item in sorted(dimensions)],
        "input_pages": input_rows,
        "output_pages": output_rows,
        "failures": failures,
        "visual_review_pages": visual_review_pages,
        "visual_review_categories": visual_categories,
        "geometry_summary": geometry_summary,
        "pairs": pairs,
        "decoded_identity_summary": decoded_identity_summary,
        "identity_evidence_retention": {
            "all_cross_scores_evaluated": not comparison_budget_rejected,
            "full_cross_score_matrix_serialized": False,
            "maximum_candidates_per_output": (
                MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT
            ),
            "routine_candidate_scores_truncated": (
                identity_score_retention_truncated
            ),
            "routine_truncation_semantics": (
                "only nondecisive scores are omitted; the mapped candidate and "
                "strongest alternate used by the decision are retained"
            ),
            "exact_alternate_evidence_truncated": (
                exact_alternate_retention_truncated
            ),
            "exact_alternate_truncation_requires_review": True,
            "maximum_duplicate_decisions": (
                MAXIMUM_RETAINED_DUPLICATE_DECISIONS
            ),
            "retained_duplicate_decision_count": len(duplicate_decisions),
            "omitted_duplicate_decision_count": duplicate_decisions_omitted,
            "duplicate_decision_truncation_requires_review": True,
        },
        "duplicate_summary": {
            "candidate_pair_count": duplicate_candidate_count,
            "mechanical_failure_pair_count": duplicate_failure_count,
            "source_exactly_equal_review_only_pair_count": (
                duplicate_review_only_count
                - duplicate_blank_cleanup_review_only_count
            ),
            "blank_dirt_cleanup_review_only_pair_count": (
                duplicate_blank_cleanup_review_only_count
            ),
            "affected_output_count": len(duplicate_affected_outputs),
            "failure_examples": duplicate_failure_examples,
            "review_only_examples": duplicate_review_examples,
            "maximum_examples_per_category": MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES,
        },
        "duplicate_decisions": duplicate_decisions,
    }
    mechanical_evidence = {
        "schema_version": 19,
        "report_type": "mechanical_evidence",
        "mechanical_pass": mechanical_pass,
        "visual_review_required": visual_review_required,
        "passed": False,
        "evidence": evidence_payload,
    }
    try:
        evidence_hash = canonical_hash(
            mechanical_evidence, MAXIMUM_EVIDENCE_JSON_BYTES
        )
    except ValueError as error:
        print(f"mechanical evidence exceeds JSON safety budget: {error}", file=sys.stderr)
        close_immutable_files(manifest_capture, *image_captures.values())
        return 2
    approval_template = {
        "evidence_hash": evidence_hash,
        "reviewer": "",
        "note": "",
        "pages": [
            {
                "file": item["file"],
                "required_reasons": item["reasons"],
                "acknowledged_reasons": [],
            }
            for item in visual_review_pages
        ],
    }
    evidence_report = {
        **mechanical_evidence,
        "evidence_hash": evidence_hash,
        "approval_template": approval_template,
    }
    stable_inventories = {
        "input": candidate_inventory(args.input, **inventory_arguments),
        "output": candidate_inventory(args.output, **inventory_arguments),
    }
    if stable_inventories != candidate_evidence:
        print("batch inventory mutated while mechanical evidence was being collected", file=sys.stderr)
        close_immutable_files(manifest_capture, *image_captures.values())
        return 2
    if (
        not image_captures_still_published(image_captures)
        or not image_snapshots_match_paths(image_snapshots)
    ):
        print("image source path changed while mechanical evidence was being collected", file=sys.stderr)
        close_immutable_files(manifest_capture, *image_captures.values())
        return 2
    collection_snapshot = filesystem_snapshot(
        {"input": args.input, "output": args.output},
        candidate_evidence,
        paired_paths,
        None,
        args.max_inventory_total_bytes_hashed * 2,
        args.max_inventory_file_bytes,
    )
    if not paired_rows_match_snapshot(
        paired_paths, input_by_path, output_by_path, collection_snapshot
    ):
        print("paired image mutated while mechanical evidence was being collected", file=sys.stderr)
        close_immutable_files(manifest_capture, *image_captures.values())
        return 2
    if not approval_mode:
        def evidence_publication_inputs_stable() -> bool:
            return (
                image_captures_still_published(image_captures)
                and image_snapshots_match_paths(image_snapshots)
                and filesystem_snapshot(
                    {"input": args.input, "output": args.output},
                    candidate_evidence,
                    paired_paths,
                    None,
                    args.max_inventory_total_bytes_hashed * 2,
                    args.max_inventory_file_bytes,
                ) == collection_snapshot
                and {
                    "input": candidate_inventory(args.input, **inventory_arguments),
                    "output": candidate_inventory(args.output, **inventory_arguments),
                } == candidate_evidence
                and (
                    manifest_capture is None
                    or captured_file_still_published(manifest_capture)
                )
            )

        if not evidence_publication_inputs_stable():
            print("batch mutated immediately before evidence report publication", file=sys.stderr)
            close_immutable_files(manifest_capture, *image_captures.values())
            return 2
        try:
            atomic_create_json(
                Path(str(evidence_policy["target"])),
                evidence_report,
                MAXIMUM_EVIDENCE_JSON_BYTES,
            )
        except FileExistsError:
            print("--evidence-report collided with an existing file or hardlink", file=sys.stderr)
            close_immutable_files(manifest_capture, *image_captures.values())
            return 2
        except (OSError, ValueError) as error:
            print(f"cannot atomically publish --evidence-report: {error}", file=sys.stderr)
            close_immutable_files(manifest_capture, *image_captures.values())
            return 2
        close_immutable_files(manifest_capture, *image_captures.values())
        print(json.dumps({
            "mechanical_pass": mechanical_pass,
            "evidence_hash": evidence_hash,
            "evidence_report": str(args.evidence_report),
            "passed": False,
        }, allow_nan=False))
        return 2

    approval_errors: list[str] = []
    stored_evidence: dict[str, object] | None = None
    approval: dict[str, object] | None = None
    evidence_capture: ImmutableFile | None = None
    approval_capture: ImmutableFile | None = None
    try:
        evidence_capture = capture_regular_file(
            args.evidence_report, MAXIMUM_EVIDENCE_JSON_BYTES
        )
        stored_evidence = parse_json_bytes(
            evidence_capture.data, args.evidence_report
        )
        if not isinstance(stored_evidence, dict):
            raise ValueError("root must be an object")
    except (OSError, ValueError, json.JSONDecodeError) as error:
        approval_errors.append(f"cannot read evidence report: {error}")
    try:
        if not args.approval.is_file() or traverses_reparse_point(args.approval):
            raise ValueError("approval must be an existing regular non-reparse file")
        approval_capture = capture_regular_file(
            args.approval, MAXIMUM_APPROVAL_JSON_BYTES
        )
        approval = parse_json_bytes(approval_capture.data, args.approval)
        if not isinstance(approval, dict):
            raise ValueError("root must be an object")
    except (OSError, ValueError, json.JSONDecodeError) as error:
        approval_errors.append(f"cannot read approval JSON: {error}")

    if stored_evidence is not None:
        stored_hash = stored_evidence.get("evidence_hash")
        expected_report_keys = set(mechanical_evidence) | {"evidence_hash", "approval_template"}
        if set(stored_evidence) != expected_report_keys or not isinstance(stored_hash, str):
            approval_errors.append("evidence report has an invalid schema")
        else:
            stored_mechanical = {
                key: stored_evidence[key] for key in mechanical_evidence
            }
            recalculated_stored_hash = canonical_hash(stored_mechanical)
            if recalculated_stored_hash != stored_hash:
                approval_errors.append("entire canonical mechanical evidence report does not match its evidence_hash")
            if stored_evidence["approval_template"] != approval_template:
                approval_errors.append("derived approval template does not match the canonical mechanical evidence")
            if stored_mechanical != mechanical_evidence or stored_hash != evidence_hash:
                approval_errors.append("current mechanical evidence does not match the preserved evidence report")

    expected_pages = [
        {
            "file": item["file"],
            "required_reasons": item["reasons"],
            "acknowledged_reasons": item["reasons"],
        }
        for item in visual_review_pages
    ]
    if approval is not None:
        if approval.get("evidence_hash") != evidence_hash:
            approval_errors.append("approval evidence_hash does not match the preserved evidence")
        if not isinstance(approval.get("reviewer"), str) or not approval["reviewer"].strip():
            approval_errors.append("reviewer identity is required")
        if not isinstance(approval.get("note"), str) or not approval["note"].strip():
            approval_errors.append("nonempty review note is required")
        if approval.get("pages") != expected_pages:
            approval_errors.append("approval must exactly acknowledge every required page and reason")

    approved = not approval_errors
    passed = mechanical_pass and approved
    final_report = {
        "schema_version": 19,
        "report_type": "final_quality_control",
        "evidence_report": str(args.evidence_report.resolve()),
        "evidence_hash": evidence_hash,
        "approval_file": str(args.approval.resolve()),
        "approval": approval,
        "mechanical_pass": mechanical_pass,
        "visual_review_approved": approved,
        "approval_errors": approval_errors,
        "passed": passed,
    }
    def final_publication_inputs_stable() -> bool:
        return (
            evidence_capture is not None
            and approval_capture is not None
            and captured_file_still_published(evidence_capture)
            and captured_file_still_published(approval_capture)
            and image_captures_still_published(image_captures)
            and image_snapshots_match_paths(image_snapshots)
            and filesystem_snapshot(
                {"input": args.input, "output": args.output},
                candidate_evidence,
                paired_paths,
                None,
                args.max_inventory_total_bytes_hashed * 2,
                args.max_inventory_file_bytes,
            ) == collection_snapshot
            and {
                "input": candidate_inventory(args.input, **inventory_arguments),
                "output": candidate_inventory(args.output, **inventory_arguments),
            } == candidate_evidence
            and (
                manifest_capture is None
                or captured_file_still_published(manifest_capture)
            )
        )

    if not final_publication_inputs_stable():
        print(
            "batch, manifest, evidence report, or approval mutated immediately before final report publication",
            file=sys.stderr,
        )
        close_immutable_files(
            manifest_capture, evidence_capture, approval_capture,
            *image_captures.values(),
        )
        return 2
    try:
        atomic_create_json(
            Path(str(final_policy["target"])),
            final_report,
            MAXIMUM_FINAL_JSON_BYTES,
        )
    except FileExistsError:
        print("--final-report collided with an existing file or hardlink", file=sys.stderr)
        close_immutable_files(
            manifest_capture, evidence_capture, approval_capture,
            *image_captures.values(),
        )
        return 2
    except (OSError, ValueError) as error:
        print(f"cannot atomically publish --final-report: {error}", file=sys.stderr)
        close_immutable_files(
            manifest_capture, evidence_capture, approval_capture,
            *image_captures.values(),
        )
        return 2
    close_immutable_files(
        manifest_capture, evidence_capture, approval_capture,
        *image_captures.values(),
    )
    print(json.dumps({
        "mechanical_pass": mechanical_pass,
        "visual_review_approved": approved,
        "passed": passed,
        "final_report": str(args.final_report),
    }, allow_nan=False))
    return 0 if passed else 2


if __name__ == "__main__":
    raise SystemExit(main())
