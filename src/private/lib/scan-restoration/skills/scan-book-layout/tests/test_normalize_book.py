from __future__ import annotations

import importlib.util
import json
import io
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
import zlib
from pathlib import Path
from unittest import mock

import cv2
import numpy as np
from PIL import Image, PngImagePlugin


SKILL_DIR = Path(__file__).resolve().parents[1]
SCRIPT = SKILL_DIR / "scripts" / "normalize_book.py"
SPEC = importlib.util.spec_from_file_location("normalize_book", SCRIPT)
assert SPEC and SPEC.loader
normalize_book = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(normalize_book)


def write_image(path: Path, image: np.ndarray) -> None:
    ok, encoded = cv2.imencode(path.suffix, image)
    if not ok:
        raise AssertionError(f"could not encode test image {path}")
    encoded.tofile(path)


def add_scanner_strip(
    image: np.ndarray, y_start: int = 20, y_end: int = 280, x_start: int = 397
) -> None:
    image[y_start:y_end, x_start:] = 0
    variation = 8 if int(image.max()) <= 255 else 2048
    image[y_start + 1:y_end:2, x_start:] = variation
    image[y_start + 8:y_end:17, x_start:x_start + 2] = np.iinfo(
        image.dtype
    ).max


def write_dcx(path: Path) -> None:
    pcx = io.BytesIO()
    Image.new("1", (100, 120), 1).save(pcx, format="PCX")
    path.write_bytes(struct.pack("<III", 0x3ADE68B1, 12, 0) + pcx.getvalue())


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


def write_png_header(path: Path, width: int, height: int) -> None:
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", ihdr)
        + png_chunk(b"IEND", b"")
    )


def write_white_is_zero_tiff(
    path: Path,
    image: np.ndarray,
    bits: int,
    sample_format: int = 1,
    photometric: int = 0,
    fill_order: int = 1,
    orientation: int = 1,
) -> None:
    height, width = image.shape
    maximum = (1 << bits) - 1
    samples = (
        maximum - image.astype(np.uint16)
        if photometric == 0
        else image.astype(np.uint16)
    )
    if bits in (1, 2, 4):
        shifts = np.arange(bits - 1, -1, -1, dtype=np.uint8)
        sample_bits = (
            samples.astype(np.uint8)[..., None] >> shifts
        ) & 1
        data = np.packbits(
            sample_bits.reshape(height, width * bits),
            axis=1,
            bitorder="big",
        ).tobytes()
    elif bits == 8:
        data = samples.astype(np.uint8).tobytes()
    elif bits == 16:
        data = samples.astype("<u2").tobytes()
    else:
        raise AssertionError(f"unsupported test TIFF depth: {bits}")
    if fill_order == 2:
        reverse_bits = bytes(
            int(f"{value:08b}"[::-1], 2) for value in range(256)
        )
        data = data.translate(reverse_bits)
    elif fill_order != 1:
        raise AssertionError(f"unsupported test TIFF fill order: {fill_order}")

    tags = [
        (256, 4, 1, width),
        (257, 4, 1, height),
        (258, 3, 1, bits),
        (259, 3, 1, 1),
        (262, 3, 1, photometric),
        (266, 3, 1, fill_order),
        (273, 4, 1, 0),
        (274, 3, 1, orientation),
        (277, 3, 1, 1),
        (278, 4, 1, height),
        (279, 4, 1, len(data)),
        (284, 3, 1, 1),
        (339, 3, 1, sample_format),
    ]
    data_offset = 8 + 2 + 12 * len(tags) + 4
    tags[6] = (273, 4, 1, data_offset)
    encoded = bytearray(struct.pack("<2sHIH", b"II", 42, 8, len(tags)))
    for tag, field_type, count, value in tags:
        encoded.extend(struct.pack("<HHI", tag, field_type, count))
        if field_type == 3 and count == 1:
            encoded.extend(struct.pack("<H2x", value))
        else:
            encoded.extend(struct.pack("<I", value))
    encoded.extend(struct.pack("<I", 0))
    encoded.extend(data)
    path.write_bytes(encoded)


def write_tifffile_grayscale(
    path: Path,
    image: np.ndarray,
    bits: int,
    *,
    photometric: int = 1,
    byte_order: str = "<",
    bigtiff: bool = False,
    compression: int | None = None,
    orientation: int = 1,
) -> None:
    maximum = (1 << bits) - 1
    samples = maximum - image if photometric == 0 else image
    dtype = np.uint16 if bits == 16 else np.uint8
    normalize_book.tifffile.imwrite(
        path,
        samples.astype(dtype),
        bigtiff=bigtiff,
        byteorder=byte_order,
        compression=compression,
        photometric="miniswhite" if photometric == 0 else "minisblack",
        metadata=None,
        extratags=[(274, "H", 1, orientation, False)],
    )


class NormalizeBookTests(unittest.TestCase):
    def setUp(self) -> None:
        self.scratch = tempfile.TemporaryDirectory(dir=Path(__file__).parent)
        self.root = Path(self.scratch.name)
        self.input = self.root / "input"
        self.input.mkdir()

    def tearDown(self) -> None:
        self.scratch.cleanup()

    def run_cli(self, *arguments: object) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), *(str(argument) for argument in arguments)],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_staff_endings_connected_to_border_are_preserved(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 398:400] = 0
        for row in (80, 90, 100, 110, 120):
            image[row:row + 2, 250:400] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertFalse(candidate["accepted"])
        self.assertTrue(candidate["connected_to_long_horizontal"])
        self.assertIn("staff endings", candidate["decision"])

    def test_thin_artifact_preservation_does_not_change_nearby_content(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        image[100:200, 395] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 0)
        self.assertTrue(np.all(cleaned[20:280, 399] == 0))
        self.assertTrue(np.all(cleaned[100:200, 395] == 0))

    def test_separated_marginal_rule_is_preserved_and_reported(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        image[30:270, 394:396] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertTrue(np.all(cleaned[20:280, 399] == 0))
        self.assertTrue(np.all(cleaned[30:270, 394:396] == 0))
        marginal_rule = next(
            candidate
            for candidate in report["candidates"]
            if candidate["bounds"] == [394, 30, 2, 240]
        )
        self.assertEqual(marginal_rule["status"], "preserved/review")
        self.assertFalse(marginal_rule["touches_physical_border"])
        self.assertIn("internal or near-edge", marginal_rule["decision"])

    def test_internal_artifact_in_inspection_band_is_reported(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[100:180, 390:393] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(len(report["candidates"]), 1)
        candidate = report["candidates"][0]
        self.assertEqual(candidate["status"], "preserved/review")
        self.assertEqual(candidate["right_gap_pixels"], 7)
        self.assertIn("does not touch the actual outermost right column", candidate["decision"])

    def test_component_beyond_one_pixel_border_tolerance_is_not_deleted(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 398] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        self.assertEqual(report["physical_border_tolerance_pixels"], 0)
        self.assertFalse(report["candidates"][0]["touches_physical_border"])
        self.assertEqual(report["candidates"][0]["right_gap_pixels"], 1)
        self.assertTrue(report["candidates"][0]["review_required"])

    def test_short_staff_endings_and_ambiguous_attachments_are_preserved(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        image[100:102, 396:400] = 0
        image[180, 397:400] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertFalse(candidate["accepted"])
        self.assertTrue(candidate["meaningful_horizontal_branches"])
        self.assertTrue(candidate["attached_content"])
        self.assertIn("short staff endings", candidate["decision"])

    def test_tiny_ambiguous_attachment_is_preserved(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        image[150, 398] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["variable_horizontal_profile"])
        self.assertTrue(candidate["ambiguous_attachment"])
        self.assertTrue(candidate["attached_content"])
        self.assertFalse(candidate["eligible"])
        self.assertEqual(candidate["status"], "preserved/review")

    def test_uniform_two_pixel_flush_edge_rule_is_preserved_for_review(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 398:400] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["thin_flush_edge_component"])
        self.assertEqual(candidate["status"], "preserved/review")
        self.assertIn("genuine marginal rule", candidate["decision"])

    def test_uniform_three_pixel_flush_rule_is_preserved_for_review(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 397:400] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["uniform_rule_like"])
        self.assertFalse(candidate["eligible"])
        self.assertIn("marginal or page-frame rule", candidate["decision"])

    def test_noisy_flush_page_frame_rule_is_preserved(self) -> None:
        image = np.full((300, 400), 247, np.uint8)
        image[20:280, 395:400] = 18
        image[21:280:2, 395:400] = 25
        image[37:280:19, 395:400] = 14

        cleaned, report = normalize_book.clean_right_edge(image, 0.0)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["scanner_tonal_variation"])
        self.assertTrue(candidate["rule_like_geometry"])
        self.assertFalse(candidate["scanner_strip_specific_cues"])
        self.assertFalse(candidate["eligible"])
        self.assertIn("page-frame rule", candidate["decision"])

    def test_exact_five_pixel_noisy_irregular_page_frame_is_preserved(self) -> None:
        image = np.full((300, 400), 247, np.uint8)
        image[20:280, 395:400] = 18
        image[21:280:2, 395:400] = 25
        image[28:280:17, 395] = 247

        cleaned, report = normalize_book.clean_right_edge(image, 0.0)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertEqual(candidate["bounds"], [395, 20, 5, 260])
        self.assertTrue(candidate["ordinary_rule_boundary_variation"])
        self.assertTrue(candidate["rule_like_geometry"])
        self.assertFalse(candidate["scanner_boundary_cue"])
        self.assertFalse(candidate["eligible"])
        self.assertEqual(candidate["status"], "preserved/review")

    def test_tonally_variable_large_height_scanner_border_can_be_removed(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 1)
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 255))
        candidate = report["candidates"][0]
        self.assertTrue(candidate["low_variance_solid_strip"])
        self.assertTrue(candidate["scanner_tonal_variation"])
        self.assertFalse(candidate["uniform_rule_like"])
        self.assertFalse(candidate["ambiguous_attachment"])
        self.assertFalse(candidate["attached_content"])
        self.assertTrue(candidate["scanner_strip_specific_cues"])
        self.assertTrue(candidate["scanner_width_cue"])
        self.assertTrue(candidate["scanner_solidness_cue"])
        self.assertTrue(candidate["scanner_background_separation_cue"])
        self.assertTrue(candidate["scanner_boundary_cue"])
        self.assertEqual(candidate["maximum_boundary_recession"], 2)
        self.assertTrue(candidate["repeated_regular_deep_recessions"])
        self.assertFalse(candidate["ordinary_rule_boundary_variation"])
        self.assertTrue(candidate["scanner_attachment_absence_cue"])
        self.assertTrue(candidate["eligible"])

    def test_scanner_tonal_variation_rejection_has_explicit_reason(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 397:400] = 0
        image[28:280:17, 397:399] = 255

        cleaned, report = normalize_book.clean_right_edge(image, 0.0)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertFalse(candidate["scanner_tonal_variation"])
        self.assertFalse(candidate["eligible"])
        self.assertTrue(candidate["reasons"])
        self.assertIn("scanner tonal variation", candidate["decision"])

    def test_typical_width_scanner_strips_survive_local_blur_and_are_removed(
        self,
    ) -> None:
        for strip_width in (7, 20, 40):
            with self.subTest(strip_width=strip_width):
                image = np.full((600, 2600), 246, np.uint8)
                x_start = image.shape[1] - strip_width
                image[40:560, x_start:] = 12
                image[41:560:2, x_start:] = 20
                image[48:560:17, x_start:x_start + 2] = 246

                cleaned, report = normalize_book.clean_right_edge(image, 0.9)

                self.assertEqual(report["removed"], 1)
                self.assertTrue(np.all(cleaned[40:560, x_start:] >= 246))
                candidate = report["candidates"][0]
                self.assertEqual(candidate["bounds"], [x_start, 40, strip_width, 520])
                self.assertEqual(candidate["scanner_width_range_pixels"], [7, 40])
                self.assertTrue(candidate["scanner_width_cue"])
                self.assertTrue(candidate["scanner_strip_specific_cues"])
                self.assertTrue(candidate["eligible"])

    def test_typical_width_noisy_rule_is_preserved(self) -> None:
        image = np.full((600, 2600), 246, np.uint8)
        image[40:560, 2580:] = 12
        image[41:560:2, 2580:] = 20
        image[48:560:17, 2580] = 246

        cleaned, report = normalize_book.clean_right_edge(image, 0.0)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["rule_like_geometry"])
        self.assertFalse(candidate["scanner_boundary_cue"])
        self.assertFalse(candidate["eligible"])

    def test_typical_width_strip_with_dark_attachment_is_preserved(self) -> None:
        image = np.full((600, 2600), 246, np.uint8)
        image[40:560, 2580:] = 12
        image[41:560:2, 2580:] = 20
        image[48:560:17, 2580:2582] = 246
        image[180:420, 2350:2580] = 28

        cleaned, report = normalize_book.clean_right_edge(image, 0.0)

        np.testing.assert_array_equal(cleaned, image)
        candidate = next(
            candidate
            for candidate in report["candidates"]
            if candidate["touches_physical_border"]
        )
        self.assertTrue(candidate["connected_to_broad_content"])
        self.assertTrue(candidate["attached_content"])
        self.assertFalse(candidate["eligible"])

    def test_dark_page_content_attached_to_edge_strip_is_preserved(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        add_scanner_strip(image)
        image[80:220, 300:397] = 32

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["connected_to_broad_content"])
        self.assertTrue(candidate["attached_content"])
        self.assertFalse(candidate["eligible"])

        isolated = np.full((300, 400), 255, np.uint8)
        add_scanner_strip(isolated)
        isolated_cleaned, isolated_report = normalize_book.clean_right_edge(
            isolated, 0.9
        )
        self.assertEqual(isolated_report["removed"], 1)
        self.assertTrue(np.all(isolated_cleaned[20:280, 397:400] == 255))

    def test_245_right_margin_does_not_attach_three_pixel_scanner_strip(
        self,
    ) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[:, 336:] = 245
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 1)
        self.assertTrue(np.all(cleaned[:, :336] == 255))
        self.assertTrue(np.all(cleaned[:, 336:397] == 245))
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 255))
        candidate = report["candidates"][0]
        self.assertEqual(candidate["bounds"], [397, 20, 3, 260])
        self.assertFalse(candidate["connected_to_broad_content"])
        self.assertTrue(candidate["accepted"])

    def test_broad_dark_attachment_is_preserved_on_245_right_margin(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[:, 336:] = 245
        add_scanner_strip(image)
        image[80:220, 300:397] = 32

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["connected_to_broad_content"])
        self.assertTrue(candidate["attached_content"])
        self.assertFalse(candidate["eligible"])

    def test_off_white_249_paper_is_background_but_black_edge_strip_is_removed(
        self,
    ) -> None:
        image = np.full((300, 400), 249, np.uint8)
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 1)
        self.assertEqual(report["inspected_components"], 1)
        self.assertTrue(np.all(cleaned[:, :397] == 249))
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 255))

    def test_yellow_paper_is_background_but_black_edge_strip_is_removed(self) -> None:
        image = np.full((300, 400), 220, np.uint8)
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 1)
        self.assertEqual(report["inspected_components"], 1)
        self.assertTrue(np.all(cleaned[:, :397] == 220))
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 255))

    def test_dark_paper_band_below_legacy_absolute_limit_is_not_foreground(
        self,
    ) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[:, 336:] = 180

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["inspected_components"], 0)
        self.assertNotIn(
            "absolute_dark_connectivity_limit_8bit_equivalent", report
        )

    def test_locally_shaded_dark_yellow_paper_band_is_not_foreground(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        shading = np.linspace(150, 185, image.shape[0]).astype(np.uint8)
        image[:, 336:] = shading[:, None]

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["inspected_components"], 0)

    def test_dark_paper_band_still_exposes_true_scanner_strip(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[:, 336:] = 180
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["removed"], 1)
        self.assertEqual(report["inspected_components"], 1)
        self.assertTrue(np.all(cleaned[:, 336:397] == 180))
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 255))

    def test_broad_dark_attachment_is_preserved_on_dark_paper_band(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[:, 336:] = 180
        add_scanner_strip(image)
        image[80:220, 300:397] = 32

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = next(
            candidate
            for candidate in report["candidates"]
            if candidate["touches_physical_border"]
        )
        self.assertTrue(candidate["connected_to_broad_content"])
        self.assertTrue(candidate["attached_content"])
        self.assertFalse(candidate["eligible"])

    def test_faint_edge_content_attached_to_black_strip_is_preserved(self) -> None:
        image = np.full((300, 400), 249, np.uint8)
        image[20:280, 397:400] = 0
        image[150:152, 390:397] = 245

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        self.assertEqual(report["removed"], 0)
        candidate = report["candidates"][0]
        self.assertTrue(candidate["meaningful_horizontal_branches"])
        self.assertTrue(candidate["attached_content"])

    def test_wide_border_with_inward_branch_is_preserved(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 397:400] = 0
        image[150:152, 390:400] = 0

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        np.testing.assert_array_equal(cleaned, image)
        candidate = report["candidates"][0]
        self.assertFalse(candidate["accepted"])
        self.assertGreater(candidate["inward_branch_rows"], 0)

    def test_every_component_intersecting_right_band_is_reported(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        image[40:80, 390:392] = 0
        image[100:140, 383:385] = 0
        image[160:200, 382:384] = 0

        _, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(report["inspected_components"], 3)
        self.assertEqual(len(report["candidates"]), 3)
        self.assertEqual(
            {tuple(candidate["bounds"]) for candidate in report["candidates"]},
            {(399, 20, 1, 260), (390, 40, 2, 40), (384, 100, 1, 40)},
        )
        clipped = next(
            candidate
            for candidate in report["candidates"]
            if candidate["bounds"] == [384, 100, 1, 40]
        )
        self.assertTrue(clipped["reaches_inner_inspection_band_boundary"])
        self.assertTrue(clipped["connected_to_broad_content"])

    def test_component_labeling_is_limited_to_right_edge_band(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 397:400] = 0
        original = normalize_book.cv2.connectedComponentsWithStats

        with mock.patch.object(
            normalize_book.cv2,
            "connectedComponentsWithStats",
            wraps=original,
        ) as connected_components:
            normalize_book.clean_right_edge(image, 0.9)

        labeled = connected_components.call_args.args[0]
        self.assertEqual(labeled.shape, (300, 16))
        self.assertLess(labeled.size, image.size // 10)

    def test_confidence_varies_and_threshold_changes_eligible_decision(self) -> None:
        weak = np.full((300, 400), 255, np.uint8)
        add_scanner_strip(weak, 38, 263)
        strong = np.full((300, 400), 255, np.uint8)
        add_scanner_strip(strong)

        weak_removed, weak_low = normalize_book.clean_right_edge(weak, 0.88)
        weak_preserved, weak_high = normalize_book.clean_right_edge(weak, 0.90)
        _, strong_report = normalize_book.clean_right_edge(strong, 0.90)

        weak_candidate = weak_low["candidates"][0]
        self.assertTrue(weak_candidate["eligible"])
        self.assertTrue(weak_candidate["accepted"])
        self.assertGreaterEqual(weak_candidate["confidence"], 0.88)
        self.assertLess(weak_candidate["confidence"], 0.90)
        self.assertFalse(weak_high["candidates"][0]["accepted"])
        self.assertGreater(
            strong_report["candidates"][0]["confidence"],
            weak_candidate["confidence"],
        )
        self.assertTrue(strong_report["candidates"][0]["accepted"])
        self.assertTrue(np.all(weak_removed[38:263, 397:400] == 255))
        np.testing.assert_array_equal(weak_preserved, weak)

    def test_zero_threshold_still_preserves_thin_and_ambiguous_content(self) -> None:
        thin = np.full((300, 400), 255, np.uint8)
        thin[20:280, 399] = 0
        ambiguous = np.full((300, 400), 255, np.uint8)
        ambiguous[20:280, 397:400] = 0
        ambiguous[150, 396] = 0

        thin_cleaned, thin_report = normalize_book.clean_right_edge(thin, 0.0)
        ambiguous_cleaned, ambiguous_report = normalize_book.clean_right_edge(
            ambiguous, 0.0
        )

        np.testing.assert_array_equal(thin_cleaned, thin)
        np.testing.assert_array_equal(ambiguous_cleaned, ambiguous)
        self.assertFalse(thin_report["candidates"][0]["eligible"])
        self.assertFalse(ambiguous_report["candidates"][0]["eligible"])

    def test_no_edge_cleanup_cli_preserves_isolated_artifact(self) -> None:
        image = np.full((300, 400), 255, np.uint8)
        image[20:280, 399] = 0
        write_image(self.input / "page_001.png", image)
        output = self.root / "output"
        result = self.run_cli(
            self.input,
            output,
            "--auto-canvas",
            "--no-edge-cleanup",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        cleaned = normalize_book.read_gray(output / "page_001.png")
        np.testing.assert_array_equal(cleaned, image)
        report_path = output / "cleanup.json"
        report = json.loads(report_path.read_text(encoding="utf-8"))
        cleanup = report["pages"][0]["cleanup"]
        self.assertFalse(cleanup["enabled"])
        self.assertIn("edge cleanup disabled", cleanup["candidates"][0]["reasons"])

    def test_absent_requested_page_fails_without_output(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1, 2
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("requested page number(s) absent: 2", result.stderr)
        self.assertFalse(output.exists())

    def test_ambiguous_requested_page_fails(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        write_image(self.input / "page_001.png", image)
        write_image(self.input / "scan_001.png", image)

        result = self.run_cli(
            self.input, self.root / "output", "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("requested page number(s) ambiguous", result.stderr)

    def test_existing_output_collision_or_stale_file_fails(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"
        output.mkdir()
        stale = output / "stale.png"
        stale.write_bytes(b"old")

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("output directory must not exist", result.stderr)
        self.assertEqual(stale.read_bytes(), b"old")

    def test_existing_empty_output_directory_fails(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"
        output.mkdir()

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("output directory must not exist", result.stderr)
        self.assertTrue(output.is_dir())

    def test_duplicate_stems_fail_before_page_selection(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        write_image(self.input / "page_001.png", image)
        write_image(self.input / "page_001.jpg", image)

        result = self.run_cli(
            self.input, self.root / "output", "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duplicate input stems", result.stderr)

    def test_multipage_tiff_fails_without_output_or_report(self) -> None:
        page = np.full((120, 100), 255, np.uint8)
        tiff = self.input / "page_001.tiff"
        self.assertTrue(cv2.imwritemulti(str(tiff), [page, page]))
        output = self.root / "output"
        report = output / "cleanup.json"
        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("detected TIFF, 2 frames", result.stderr)
        self.assertFalse(output.exists())
        self.assertFalse(report.exists())

    def test_generated_16_bit_rgb_tiff_is_rejected_without_downconversion(self) -> None:
        image = np.empty((120, 100, 3), np.uint16)
        image[:, :, 0] = 1000
        image[:, :, 1] = 30000
        image[:, :, 2] = 60000
        source = self.input / "page_001.tiff"
        self.assertTrue(cv2.imwrite(str(source), image))

        with Image.open(source) as opened:
            self.assertEqual(opened.mode, "RGB")
            self.assertIn(";16", normalize_book.source_raw_mode(opened))
            self.assertEqual(tuple(opened.tag_v2[258]), (16, 16, 16))
            self.assertEqual(tuple(opened.tag_v2[339]), (1, 1, 1))

        with self.assertRaisesRegex(
            ValueError, "unsupported high-depth color/alpha TIFF"
        ):
            normalize_book.read_gray(source)

        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported high-depth color/alpha TIFF", result.stderr)
        self.assertFalse(output.exists())

    def test_8_bit_rgb_and_rgba_tiffs_convert_to_grayscale(self) -> None:
        rgb = np.full((12, 16, 3), 255, np.uint8)
        rgb[2:6, 3:8] = (255, 0, 0)
        rgba = np.zeros((12, 16, 4), np.uint8)
        rgba[:, :, 3] = 255
        rgba[2:6, 3:8, 3] = 0

        expected = {}
        for page, (mode, image) in enumerate(
            (("RGB", rgb), ("RGBA", rgba)), 1
        ):
            source = self.input / f"page_{page:03}.tiff"
            pillow_image = Image.fromarray(image)
            pillow_image.save(source)
            if mode == "RGBA":
                white = Image.new("RGBA", pillow_image.size, "white")
                expected[page] = np.asarray(
                    Image.alpha_composite(white, pillow_image).convert("L")
                )
            else:
                expected[page] = np.asarray(pillow_image.convert("L"))

            with Image.open(source) as opened:
                self.assertEqual(opened.mode, mode)
                self.assertTrue(
                    all(bits <= 8 for bits in opened.tag_v2[258])
                )
            np.testing.assert_array_equal(
                normalize_book.read_gray(source), expected[page]
            )

        output = self.root / "output"
        result = self.run_cli(
            self.input, output, "--auto-canvas", "--no-edge-cleanup"
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        for page in expected:
            np.testing.assert_array_equal(
                normalize_book.read_gray(output / f"page_{page:03}.png"),
                expected[page],
            )

    def test_signed_8_bit_tiff_is_rejected_before_conversion(self) -> None:
        image = np.full((120, 100), 255, np.uint16)
        source = self.input / "page_001.tiff"
        write_white_is_zero_tiff(
            source, image, 8, sample_format=2, photometric=1
        )

        with Image.open(source) as opened:
            self.assertEqual(tuple(opened.tag_v2[258]), (8,))
            self.assertEqual(tuple(opened.tag_v2[339]), (2,))

        with mock.patch.object(
            normalize_book, "image_to_gray_array", wraps=normalize_book.image_to_gray_array
        ) as convert:
            with self.assertRaisesRegex(
                ValueError, r"unsupported TIFF SampleFormat \(2,\)"
            ):
                normalize_book.read_gray(source)
            convert.assert_not_called()

        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported TIFF SampleFormat (2,)", result.stderr)
        self.assertFalse(output.exists())

    def test_report_option_is_rejected_and_report_location_is_fixed(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"
        report = self.root / "cleanup.json"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--report", report
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unrecognized arguments: --report", result.stderr)
        self.assertFalse(output.exists())
        self.assertFalse(report.exists())
        self.assertEqual(
            list(self.root.glob(".output.scan-book-layout-*")),
            [],
        )

    def test_default_report_is_published_with_output(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(report["processed"], 1)
        self.assertTrue((output / "page_001.png").is_file())
        self.assertEqual(
            report["top_level_file_inventory"][0]["classification"],
            "supported_image",
        )
        self.assertEqual(report["top_level_file_inventory"][0]["detected_format"], "PNG")
        self.assertEqual(report["top_level_file_inventory"][0]["detected_frames"], 1)
        self.assertEqual(report["output_paths_relative_to"], "output_root")
        self.assertEqual(report["output_root_from_report"], ".")

    def test_top_level_non_images_and_directories_are_explicitly_classified(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "notes.txt").write_text("notes", encoding="utf-8")
        (self.input / "reference").mkdir()
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        classifications = {
            item["name"]: item["classification"]
            for item in report["top_level_file_inventory"]
        }
        self.assertEqual(classifications["notes.txt"], "non_image")
        self.assertEqual(classifications["reference"], "directory")

    def test_non_image_inventory_probe_is_bounded_to_file_extent(self) -> None:
        notes = self.input / "notes.txt"
        notes.write_bytes(b"notes" * 20_000)

        with mock.patch.object(
            normalize_book,
            "read_bounded_image_candidate",
            wraps=normalize_book.read_bounded_image_candidate,
        ) as bounded_reader:
            self.assertIsNone(normalize_book.encoded_image_info(notes))
        bounded_reader.assert_called_once_with(notes)

        self.assertLess(
            len(normalize_book.read_bounded_header(notes)),
            notes.stat().st_size,
        )

    def test_candidate_reader_uses_exact_file_extent_not_maximum_budget(self) -> None:
        candidate = self.input / "candidate.bin"
        payload = b"x" * 37
        candidate.write_bytes(payload)
        requested: list[int] = []
        original_open = Path.open

        class TrackingReader(io.BytesIO):
            def read(self, size: int = -1) -> bytes:
                requested.append(size)
                return super().read(size)

        def tracked_open(path: Path, *args: object, **kwargs: object) -> object:
            if path == candidate:
                return TrackingReader(payload)
            return original_open(path, *args, **kwargs)

        with mock.patch.object(Path, "open", tracked_open):
            with normalize_book.read_bounded_image_candidate(candidate) as stream:
                self.assertEqual(stream.read(), b"x" * 37)

        self.assertEqual(requested, [37])

    def test_supported_unknown_suffix_is_detected_past_64k_metadata(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        ok, encoded = cv2.imencode(".jpg", image)
        self.assertTrue(ok)
        comment = b"\xff\xfe" + struct.pack(">H", 65535) + b"x" * 65533
        disguised = self.input / "page_001.payload"
        disguised.write_bytes(encoded[:2].tobytes() + comment + encoded[2:].tobytes())

        self.assertIsNone(
            normalize_book.header_image_format(
                normalize_book.read_bounded_header(disguised)
            )
        )
        self.assertEqual(
            normalize_book.encoded_image_info(disguised),
            ("JPEG", 1),
        )
        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((output / "page_001.png").is_file())

    def test_supported_unknown_suffix_is_detected_past_4mib_before_sof(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        ok, encoded = cv2.imencode(".jpg", image)
        self.assertTrue(ok)
        comment = b"\xff\xfe" + struct.pack(">H", 65535) + b"x" * 65533
        disguised = self.input / "page_001.payload"
        disguised.write_bytes(
            encoded[:2].tobytes() + comment * 65 + encoded[2:].tobytes()
        )
        self.assertGreater(
            disguised.stat().st_size, normalize_book.MAX_INVENTORY_PROBE_BYTES
        )

        self.assertEqual(
            normalize_book.encoded_image_info(disguised),
            ("JPEG", 1),
        )
        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((output / "page_001.png").is_file())

    def test_oversized_header_raster_fails_before_decode_or_copy(self) -> None:
        oversized = self.input / "page_001.png"
        write_png_header(oversized, 10_000, 8_001)

        with mock.patch.object(
            normalize_book,
            "image_to_gray_array",
            side_effect=AssertionError("raster decode must not start"),
        ), self.assertRaisesRegex(
            ValueError, "source raster pixel budget exceeded before decode"
        ):
            normalize_book.read_gray(oversized)

    def test_top_level_entry_budget_fails_before_inventory(self) -> None:
        (self.input / "notes-a.txt").write_text("a", encoding="utf-8")
        (self.input / "notes-b.txt").write_text("b", encoding="utf-8")
        output = self.root / "output"

        with mock.patch.object(normalize_book, "MAX_TOP_LEVEL_ENTRIES", 1):
            argv = [
                "normalize_book.py", str(self.input), str(output), "--auto-canvas"
            ]
            with mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
                normalize_book.main()

        self.assertFalse(output.exists())

    def test_image_candidate_file_byte_budget_fails_closed(self) -> None:
        candidate = self.input / "page_001.png"
        candidate.write_bytes(b"\x89PNG\r\n\x1a\n" + b"x" * 64)
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output), "--auto-canvas"
        ]

        with mock.patch.object(
            normalize_book, "MAX_IMAGE_FILE_BYTES", 16
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()
        self.assertFalse(output.exists())

    def test_aggregate_image_byte_budget_fails_closed(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        write_image(self.input / "page_002.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"
        one_file = (self.input / "page_001.png").stat().st_size
        argv = [
            "normalize_book.py", str(self.input), str(output), "--auto-canvas"
        ]

        with mock.patch.object(
            normalize_book, "MAX_AGGREGATE_IMAGE_BYTES", one_file
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_16_bit_grayscale_is_preserved_through_resize_pad_and_png_write(self) -> None:
        image = np.linspace(
            1000, 65000, num=120 * 100, dtype=np.uint16
        ).reshape(120, 100)
        Image.fromarray(image).save(self.input / "page_001.png")
        output = self.root / "output"

        result = self.run_cli(
            self.input,
            output,
            "--width",
            200,
            "--height",
            300,
            "--no-edge-cleanup",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        decoded = normalize_book.read_gray(output / "page_001.png")
        self.assertEqual(decoded.dtype, np.uint16)
        self.assertEqual(decoded.shape, (300, 200))
        self.assertGreater(int(decoded.max()), 255)
        self.assertEqual(int(decoded[0, 0]), 65535)
        content = decoded[30:270, :]
        self.assertLess(int(content.min()), 2000)
        self.assertGreater(int(content.max()), 64000)

    def test_explicit_canvas_dimension_limit_fails_before_inventory(self) -> None:
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", str(normalize_book.MAX_CANVAS_WIDTH + 1), "--height", "100",
        ]

        with mock.patch.object(
            normalize_book, "read_bounded_header",
            side_effect=AssertionError("inventory must not run"),
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_explicit_canvas_pixel_budget_fails_closed(self) -> None:
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", "1000", "--height", "1000",
        ]

        with mock.patch.object(
            normalize_book, "MAX_CANVAS_PIXELS", 999_999
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_explicit_canvas_working_memory_budget_fails_closed(self) -> None:
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", "100", "--height", "100",
        ]

        with mock.patch.object(
            normalize_book, "MAX_CANVAS_WORKING_BYTES", 59_999
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_80m_uint16_working_memory_preflight_avoids_huge_allocation(
        self,
    ) -> None:
        backing = np.empty(1, np.uint16)
        image = np.lib.stride_tricks.as_strided(
            backing,
            shape=(8_000, 10_000),
            strides=(0, 0),
        )

        estimated = normalize_book.processing_working_bytes(image, 10_000, 8_000)
        image8 = np.lib.stride_tricks.as_strided(
            np.empty(1, np.uint8),
            shape=image.shape,
            strides=(0, 0),
        )

        self.assertLessEqual(estimated, normalize_book.MAX_CANVAS_WORKING_BYTES)
        self.assertLess(
            normalize_book.processing_working_bytes(image8, 10_000, 8_000),
            estimated,
        )
        normalize_book.validate_processing_memory(image, 10_000, 8_000)
        with mock.patch.object(
            normalize_book, "MAX_CANVAS_WORKING_BYTES", estimated - 1
        ), self.assertRaisesRegex(ValueError, "page working-memory budget exceeded"):
            normalize_book.validate_processing_memory(image, 10_000, 8_000)
        self.assertEqual(image.nbytes, 160_000_000)
        self.assertEqual(backing.nbytes, 2)

    def test_72mp_uint16_decode_preflight_counts_materialization_copies(
        self,
    ) -> None:
        header = normalize_book.EncodedImageHeader(
            "TIFF", 1, 9_000, 8_000, 2, 2, 10
        )
        estimated = normalize_book.header_processing_working_bytes(
            header, 9_000, 8_000
        )

        self.assertGreater(estimated, normalize_book.MAX_CANVAS_WORKING_BYTES)
        with self.assertRaisesRegex(
            ValueError, "page working-memory budget exceeded before decode"
        ):
            normalize_book.validate_header_processing_memory(
                self.input / "page_001.tif", header, 9_000, 8_000
            )

    def test_metadata_heavy_jpeg_and_webp_include_encoded_decoder_buffers(
        self,
    ) -> None:
        jpeg = self.input / "page_001.jpg"
        encoded = io.BytesIO()
        Image.new("L", (100, 120), 255).save(encoded, format="JPEG")
        comment = b"\xff\xfe" + struct.pack(">H", 65535) + b"x" * 65533
        jpeg.write_bytes(encoded.getvalue()[:2] + comment * 16 + encoded.getvalue()[2:])

        webp = self.input / "page_002.webp"
        Image.new("L", (100, 120), 255).save(
            webp, format="WEBP", lossless=True, xmp=b"x" * (1024 * 1024)
        )

        for path in (jpeg, webp):
            with self.subTest(path=path.name):
                inspected = normalize_book.inspect_encoded_image(path)
                self.assertIsNotNone(inspected)
                assert inspected is not None
                encoded_reserve = (
                    path.stat().st_size
                    * normalize_book.DECODER_ENCODED_BUFFER_COPIES
                )
                self.assertEqual(inspected.encoded_bytes, path.stat().st_size)
                self.assertGreaterEqual(
                    normalize_book.header_processing_working_bytes(
                        inspected, inspected.width, inspected.height
                    ),
                    encoded_reserve
                    + min(
                        inspected.encoded_bytes,
                        normalize_book.INVENTORY_HEADER_BYTES,
                    )
                    + inspected.width
                    * inspected.height
                    * inspected.decode_working_bytes_per_pixel,
                )
                limit = normalize_book.header_processing_working_bytes(
                    inspected, inspected.width, inspected.height
                ) - 1
                with mock.patch.object(
                    normalize_book, "MAX_CANVAS_WORKING_BYTES", limit
                ), mock.patch.object(
                    normalize_book,
                    "image_to_gray_array",
                    side_effect=AssertionError("decode conversion must not start"),
                ), self.assertRaisesRegex(
                    ValueError, "page working-memory budget exceeded before decode"
                ):
                    normalize_book.read_gray(path)

    def test_large_encoded_webp_fails_before_pillow_header_parse(self) -> None:
        path = self.input / "page_001.webp"
        Image.new("L", (100, 120), 255).save(
            path, format="WEBP", lossless=True, xmp=b"x" * 4096
        )
        with mock.patch.object(
            normalize_book, "MAX_IMAGE_FILE_BYTES", path.stat().st_size - 1
        ), mock.patch.object(
            normalize_book.Image,
            "open",
            side_effect=AssertionError("oversized WebP reached Pillow"),
        ), self.assertRaisesRegex(ValueError, "byte file budget"):
            normalize_book.inspect_encoded_image(path)

    def test_auto_canvas_cross_product_fails_before_normalization(self) -> None:
        first = self.input / "page_001.png"
        second = self.input / "page_002.png"
        write_image(first, np.full((100, 1000), 255, np.uint8))
        write_image(second, np.full((1000, 100), 255, np.uint8))
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output), "--auto-canvas",
        ]

        with mock.patch.object(
            normalize_book, "MAX_CANVAS_PIXELS", 999_999
        ), mock.patch.object(
            normalize_book, "normalize",
            side_effect=AssertionError("unsafe canvas reached allocation"),
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_normalize_revalidates_canvas_before_opencv_or_numpy_allocation(
        self,
    ) -> None:
        image = np.full((100, 100), 255, np.uint8)

        with mock.patch.object(
            normalize_book, "MAX_CANVAS_PIXELS", 9_999
        ), mock.patch.object(
            normalize_book.cv2, "resize",
            side_effect=AssertionError("OpenCV allocation was attempted"),
        ), mock.patch.object(
            normalize_book.np, "full",
            side_effect=AssertionError("NumPy allocation was attempted"),
        ), self.assertRaisesRegex(ValueError, "canvas pixel budget exceeded"):
            normalize_book.normalize(image, 100, 100)

    def test_normalize_clamps_extreme_fitted_axis_to_one_pixel(self) -> None:
        image = np.full((1, 30_000), 255, np.uint8)
        original_resize = normalize_book.cv2.resize
        with mock.patch.object(
            normalize_book.cv2, "resize", wraps=original_resize
        ) as resize:
            resized = normalize_book.normalize(image, 100, 100)
        self.assertEqual(resized.shape, (100, 100))
        self.assertEqual(resize.call_args.args[1], (100, 1))

    def test_extreme_source_axis_fails_before_opencv_resize(self) -> None:
        image = np.empty((1, normalize_book.MAX_CANVAS_WIDTH + 1), np.uint8)
        with mock.patch.object(
            normalize_book.cv2,
            "resize",
            side_effect=AssertionError("OpenCV resize must not run"),
        ), self.assertRaisesRegex(ValueError, "extreme aspect"):
            normalize_book.normalize(image, 100, 100)

    def test_white_is_zero_tiffs_are_inverted_once_before_cleanup_and_output(
        self,
    ) -> None:
        maximums = {1: 1, 8: 255, 16: 65535}
        for page, bits in enumerate((1, 8, 16), 1):
            maximum = maximums[bits]
            image = np.full((300, 400), maximum, np.uint16)
            image[100:120, 100:120] = 0
            if bits == 1:
                image[20:280, 397:400] = 0
            else:
                add_scanner_strip(image)
            source = self.input / f"page_{page:03}.tiff"
            write_white_is_zero_tiff(source, image, bits)
            with Image.open(source) as opened:
                self.assertEqual(opened.tag_v2[262], 0)
                self.assertEqual(tuple(opened.tag_v2[258]), (bits,))

        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        for page, bits in enumerate((1, 8, 16), 1):
            maximum = 65535 if bits == 16 else 255
            decoded = normalize_book.read_gray(output / f"page_{page:03}.png")
            self.assertEqual(
                decoded.dtype, np.dtype(np.uint16 if bits == 16 else np.uint8)
            )
            self.assertEqual(int(decoded[0, 0]), maximum)
            self.assertEqual(int(decoded[110, 110]), 0)
            if bits == 1:
                self.assertTrue(np.all(decoded[20:280, 397:400] == 0))
            else:
                self.assertTrue(np.all(decoded[20:280, 397:400] == maximum))

        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(
            [page["cleanup"]["removed"] for page in report["pages"]], [0, 1, 1]
        )

    def test_fill_order_2_tiffs_preserve_white_and_black_output(self) -> None:
        page = 0
        expected_raw_modes = {
            (1, 0): "1;IR",
            (1, 1): "1;R",
            (8, 0): "L;IR",
            (8, 1): "L;R",
        }
        for bits in (1, 8):
            maximum = (1 << bits) - 1
            for photometric in (0, 1):
                page += 1
                image = np.full((120, 104), maximum, np.uint16)
                image[30:90, 26:78] = 0
                source = self.input / f"page_{page:03}.tiff"
                write_white_is_zero_tiff(
                    source,
                    image,
                    bits,
                    photometric=photometric,
                    fill_order=2,
                )
                with Image.open(source) as opened:
                    self.assertEqual(opened.tag_v2[266], 2)
                    self.assertEqual(
                        normalize_book.source_raw_mode(opened),
                        expected_raw_modes[(bits, photometric)],
                    )

        output = self.root / "output"
        result = self.run_cli(
            self.input, output, "--auto-canvas", "--no-edge-cleanup"
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        for page in range(1, 5):
            decoded = normalize_book.read_gray(output / f"page_{page:03}.png")
            self.assertEqual(int(decoded[0, 0]), 255)
            self.assertEqual(int(decoded[60, 52]), 0)

    def test_generated_16_bit_fill_order_2_tiffs_preserve_photometric_once(
        self,
    ) -> None:
        sources = []
        for page, photometric in enumerate((0, 1), 1):
            image = np.full((120, 104), 65535, np.uint16)
            image[30:90, 26:78] = 0
            source = self.input / f"page_{page:03}.tiff"
            write_white_is_zero_tiff(
                source,
                image,
                16,
                photometric=photometric,
                fill_order=2,
            )
            sources.append(source)
            with normalize_book.tifffile.TiffFile(source) as tiff:
                page_metadata = tiff.pages[0]
                self.assertEqual(page_metadata.bitspersample, 16)
                self.assertEqual(int(page_metadata.photometric), photometric)
                self.assertEqual(page_metadata.fillorder, 2)

        with mock.patch.object(
            normalize_book,
            "decode_tiff_grayscale_fallback",
            wraps=normalize_book.decode_tiff_grayscale_fallback,
        ) as fallback:
            for source in sources:
                decoded = normalize_book.read_gray(source)
                self.assertEqual(decoded.dtype, np.dtype(np.uint16))
                self.assertEqual(int(decoded[0, 0]), 65535)
                self.assertEqual(int(decoded[60, 52]), 0)
            self.assertGreaterEqual(fallback.call_count, 1)

        output = self.root / "output"
        result = self.run_cli(
            self.input, output, "--auto-canvas", "--no-edge-cleanup"
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        for page in (1, 2):
            decoded = normalize_book.read_gray(output / f"page_{page:03}.png")
            self.assertEqual(decoded.dtype, np.dtype(np.uint16))
            self.assertEqual(int(decoded[0, 0]), 65535)
            self.assertEqual(int(decoded[60, 52]), 0)

    def test_classic_grayscale_tiffs_use_direct_fallback_at_supported_depths(
        self,
    ) -> None:
        page = 0
        for bits in (1, 8, 16):
            maximum = (1 << bits) - 1
            for photometric in (0, 1):
                page += 1
                image = np.full((12, 16), maximum, np.uint16)
                image[3:9, 4:12] = 0
                source = self.input / f"page_{page:03}.tiff"
                write_white_is_zero_tiff(
                    source,
                    image,
                    bits,
                    photometric=photometric,
                    fill_order=photometric + 1,
                )

                decoded = normalize_book.decode_tiff_grayscale_fallback(
                    source.read_bytes(), source
                )

                expected = image.astype(
                    np.uint16 if bits == 16 else np.uint8
                )
                if bits == 1:
                    expected *= 255
                self.assertEqual(decoded.dtype, expected.dtype)
                self.assertEqual(decoded.shape, image.shape)
                np.testing.assert_array_equal(decoded, expected)

    def test_little_and_big_endian_bigtiff_depths_probe_inspect_and_decode(
        self,
    ) -> None:
        cases = [
            ("<", 8, 0, ".tiff"),
            ("<", 16, 1, ".tiff"),
            (">", 8, 1, ".payload"),
            (">", 16, 0, ".tiff"),
        ]
        for page, (byte_order, bits, photometric, suffix) in enumerate(cases, 1):
            with self.subTest(
                byte_order=byte_order,
                bits=bits,
                photometric=photometric,
                suffix=suffix,
            ):
                dtype = np.uint16 if bits == 16 else np.uint8
                maximum = (1 << bits) - 1
                image = (
                    np.arange(7 * 11, dtype=np.uint32).reshape(7, 11) * 997
                    % (maximum + 1)
                ).astype(dtype)
                source = self.input / f"page_{page:03}{suffix}"
                write_tifffile_grayscale(
                    source,
                    image,
                    bits,
                    photometric=photometric,
                    byte_order=byte_order,
                    bigtiff=True,
                )
                header = normalize_book.read_bounded_header(source)

                self.assertEqual(
                    normalize_book.tiff_header_byte_order(header), byte_order
                )
                self.assertTrue(
                    normalize_book.has_image_container_signature(header)
                )
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    self.assertEqual(
                        normalize_book.header_image_format(header), "TIFF"
                    )
                    inspected = normalize_book.inspect_encoded_image(source)
                    decoded = normalize_book.read_gray(source)

                self.assertIsNotNone(inspected)
                assert inspected is not None
                self.assertEqual(inspected.detected_format, "TIFF")
                self.assertEqual(inspected.frame_count, 1)
                self.assertEqual(
                    (inspected.width, inspected.height), (11, 7)
                )
                self.assertEqual(
                    inspected.grayscale_bytes_per_pixel,
                    2 if bits == 16 else 1,
                )
                self.assertEqual(decoded.dtype, np.dtype(dtype))
                self.assertEqual(decoded.shape, image.shape)
                np.testing.assert_array_equal(decoded, image)

    def test_malformed_bigtiff_headers_fail_all_pillow_paths_without_output(
        self,
    ) -> None:
        cases = [
            ("offset-size", slice(4, 6), 4, ".tiff"),
            ("reserved", slice(6, 8), 1, ".payload"),
        ]
        image = np.arange(35, dtype=np.uint8).reshape(5, 7)

        for name, field, value, suffix in cases:
            with self.subTest(field=name, suffix=suffix):
                input_dir = self.root / f"input-{name}"
                input_dir.mkdir()
                source = input_dir / f"page_001{suffix}"
                write_tifffile_grayscale(
                    source, image, 8, bigtiff=True, byte_order="<"
                )
                malformed = bytearray(source.read_bytes())
                malformed[field] = struct.pack("<H", value)
                source.write_bytes(malformed)
                header = normalize_book.read_bounded_header(source)

                with Image.open(source) as opened:
                    self.assertEqual(opened.format, "TIFF")
                    opened.load()
                self.assertIsNone(
                    normalize_book.tiff_header_byte_order(header)
                )
                self.assertFalse(
                    normalize_book.has_image_container_signature(header)
                )

                with self.assertRaises(ValueError) as raised:
                    normalize_book.validate_pillow_tiff_header("TIFF", header)
                self.assertEqual(str(raised.exception), "invalid TIFF header")
                with self.assertRaises(ValueError) as raised:
                    normalize_book.header_image_format(header)
                self.assertEqual(str(raised.exception), "invalid TIFF header")
                with mock.patch.object(
                    normalize_book, "header_image_format", return_value=None
                ), self.assertRaises(ValueError) as raised:
                    normalize_book.probe_image_format(source, header)
                self.assertEqual(
                    str(raised.exception), f"invalid TIFF header: {source}"
                )
                with self.assertRaises(ValueError) as raised:
                    normalize_book.inspect_encoded_image(source)
                self.assertEqual(
                    str(raised.exception), f"invalid TIFF header: {source}"
                )
                with self.assertRaises(ValueError) as raised:
                    normalize_book.read_gray(source)
                self.assertEqual(
                    str(raised.exception), f"invalid TIFF header: {source}"
                )

                output = self.root / f"output-{name}"
                result = self.run_cli(input_dir, output, "--auto-canvas")
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("invalid TIFF header", result.stderr)
                self.assertIn(source.name, result.stderr)
                self.assertNotIn("Traceback", result.stderr)
                self.assertEqual(result.stdout, "")
                self.assertFalse(output.exists())

    def test_2_and_4_bit_grayscale_tiffs_are_rejected_without_output(
        self,
    ) -> None:
        input_dirs = {
            photometric: self.root / f"input-photometric-{photometric}"
            for photometric in (0, 1)
        }
        for input_dir in input_dirs.values():
            input_dir.mkdir()

        for bits in (2, 4):
            maximum = (1 << bits) - 1
            image = (
                np.arange(5 * 8, dtype=np.uint8).reshape(5, 8)
                % (maximum + 1)
            )
            for photometric in (0, 1):
                source = input_dirs[photometric] / f"page_{bits:03}.tiff"
                write_white_is_zero_tiff(
                    source,
                    image,
                    bits,
                    photometric=photometric,
                )
                message = (
                    "unsupported TIFF single-channel grayscale depth "
                    f"({bits},) (PhotometricInterpretation {photometric}; "
                    "expected 1, 8, or 16 bits)"
                )

                with self.subTest(
                    bits=bits, photometric=photometric
                ), Image.open(source) as opened:
                    self.assertEqual(opened.mode, "L")
                    self.assertEqual(tuple(opened.tag_v2[258]), (bits,))
                    self.assertEqual(opened.tag_v2[262], photometric)
                with self.subTest(
                    bits=bits, photometric=photometric, operation="inspect"
                ), self.assertRaisesRegex(ValueError, re.escape(message)):
                    normalize_book.inspect_encoded_image(source)
                with self.subTest(
                    bits=bits, photometric=photometric, operation="decode"
                ), mock.patch.object(
                    normalize_book,
                    "image_to_gray_array",
                    wraps=normalize_book.image_to_gray_array,
                ) as convert, self.assertRaisesRegex(
                    ValueError, re.escape(message)
                ):
                    normalize_book.read_gray(source)
                convert.assert_not_called()

        for photometric, input_dir in input_dirs.items():
            with self.subTest(
                photometric=photometric, operation="cli"
            ):
                output = self.root / f"output-photometric-{photometric}"
                result = self.run_cli(input_dir, output, "--auto-canvas")
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(
                    "unsupported TIFF single-channel grayscale depth",
                    result.stderr,
                )
                self.assertNotIn("Traceback", result.stderr)
                self.assertFalse(output.exists())

    def test_signed_bigtiff_fallback_is_rejected(self) -> None:
        source = self.input / "page_001.tiff"
        normalize_book.tifffile.imwrite(
            source,
            np.arange(35, dtype=np.int8).reshape(5, 7),
            bigtiff=True,
            byteorder=">",
            photometric="minisblack",
            metadata=None,
        )

        with self.assertRaisesRegex(
            ValueError, "TIFF fallback requires unsigned single-channel"
        ):
            normalize_book.read_gray(source)

    def test_jpeg2000_tiff_fallback_decodes_and_applies_orientation_once(
        self,
    ) -> None:
        image = (
            np.arange(9 * 13, dtype=np.uint16).reshape(9, 13) * 2
        ).astype(np.uint8)
        source = self.input / "page_001.tiff"
        write_tifffile_grayscale(
            source,
            image,
            8,
            photometric=0,
            compression=34712,
            orientation=6,
        )

        with self.assertRaises(OSError):
            with Image.open(source) as opened:
                opened.load()
        inspected = normalize_book.inspect_encoded_image(source)
        decoded = normalize_book.read_gray(source)

        self.assertIsNotNone(inspected)
        assert inspected is not None
        self.assertEqual((inspected.width, inspected.height), (9, 13))
        self.assertEqual(decoded.dtype, np.dtype(np.uint8))
        self.assertEqual(decoded.shape, (13, 9))
        np.testing.assert_array_equal(
            decoded, normalize_book.orient_tiff_array(image, 6)
        )

    def test_corrupt_jpeg2000_tiff_fails_cleanly_without_output(self) -> None:
        source = self.input / "page_001.tiff"
        write_tifffile_grayscale(
            source,
            np.arange(64 * 64, dtype=np.uint8).reshape(64, 64),
            8,
            compression=34712,
        )
        with normalize_book.tifffile.TiffFile(source) as tiff:
            offset = tiff.pages[0].dataoffsets[0]
            byte_count = tiff.pages[0].databytecounts[0]
        encoded = source.read_bytes()
        source.write_bytes(encoded[: offset + byte_count // 2])

        with self.assertRaises(ValueError) as raised:
            normalize_book.read_gray(source)
        self.assertEqual(str(raised.exception), f"cannot decode image: {source}")
        self.assertIsInstance(raised.exception.__cause__, RuntimeError)

        output = self.root / "output"
        result = self.run_cli(self.input, output, "--auto-canvas")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("cannot decode image", result.stderr)
        self.assertIn(source.name, result.stderr)
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertFalse(output.exists())

    def test_tiff_fallback_checks_working_memory_before_decoder(self) -> None:
        source = self.input / "page_001.tiff"
        write_tifffile_grayscale(
            source, np.arange(35, dtype=np.uint8).reshape(5, 7), 8
        )

        with mock.patch.object(
            normalize_book, "MAX_CANVAS_WORKING_BYTES", 1
        ), mock.patch.object(
            normalize_book.tifffile.TiffPage,
            "asarray",
            side_effect=AssertionError("decoder must not run"),
        ) as decode, self.assertRaisesRegex(
            ValueError, "working-memory budget exceeded before decode"
        ):
            normalize_book.decode_tiff_grayscale_fallback(
                source, normalize_book.read_bounded_header(source)
            )
        decode.assert_not_called()

    def test_tiff_fallback_rejects_decoder_raster_disagreement(self) -> None:
        source = self.input / "page_001.tiff"
        write_white_is_zero_tiff(
            source, np.ones((5, 8), dtype=np.uint16), 1, photometric=1
        )
        invalid_rasters = [
            np.zeros((4, 8), dtype=np.bool_),
            np.zeros((5, 8), dtype=np.uint16),
            np.full((5, 8), 2, dtype=np.uint8),
        ]

        for raster in invalid_rasters:
            with (
                self.subTest(shape=raster.shape, dtype=raster.dtype),
                mock.patch.object(
                    normalize_book.tifffile.TiffPage,
                    "asarray",
                    return_value=raster,
                ),
                self.assertRaisesRegex(ValueError, "unexpected raster"),
            ):
                normalize_book.decode_tiff_grayscale_fallback(
                    source, normalize_book.read_bounded_header(source)
                )

    def test_tiff_fallback_does_not_normalize_project_type_errors(self) -> None:
        source = self.input / "page_001.tiff"
        write_tifffile_grayscale(
            source, np.arange(35, dtype=np.uint8).reshape(5, 7), 8
        )

        with mock.patch.object(
            normalize_book,
            "orient_tiff_array",
            side_effect=TypeError("project type error"),
        ), self.assertRaisesRegex(TypeError, "project type error"):
            normalize_book.decode_tiff_grayscale_fallback(
                source, normalize_book.read_bounded_header(source)
            )

    def test_non_square_16_bit_tiff_orientations_5_to_8_use_display_size(self) -> None:
        image = np.arange(80 * 120, dtype=np.uint16).reshape(80, 120)
        expected_by_orientation = {
            orientation: normalize_book.orient_tiff_array(image, orientation)
            for orientation in range(5, 9)
        }

        for orientation in range(5, 9):
            source = self.input / f"page_{orientation:03}.tiff"
            write_white_is_zero_tiff(
                source,
                image,
                16,
                photometric=1,
                orientation=orientation,
            )

            inspected = normalize_book.inspect_encoded_image(source)
            self.assertIsNotNone(inspected)
            self.assertEqual((inspected.width, inspected.height), (80, 120))

            with mock.patch.object(
                normalize_book.Image, "open", side_effect=OSError("forced fallback")
            ):
                fallback_header = normalize_book.inspect_encoded_image(source)
            self.assertIsNotNone(fallback_header)
            self.assertEqual(
                (fallback_header.width, fallback_header.height), (80, 120)
            )

            fallback = normalize_book.decode_tiff_grayscale_fallback(
                source.read_bytes(), source
            )
            np.testing.assert_array_equal(
                fallback, expected_by_orientation[orientation]
            )

        output = self.root / "output"
        result = self.run_cli(
            self.input, output, "--auto-canvas", "--no-edge-cleanup"
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(report["canvas"], [80, 120])
        for orientation in range(5, 9):
            decoded = normalize_book.read_gray(
                output / f"page_{orientation:03}.png"
            )
            self.assertEqual(decoded.shape, (120, 80))
            np.testing.assert_array_equal(
                decoded, expected_by_orientation[orientation]
            )
        self.assertEqual(
            [page["source_size"] for page in report["pages"]],
            [[80, 120]] * 4,
        )

    def test_16_bit_edge_cleanup_uses_native_white(self) -> None:
        image = np.full((300, 400), 65535, np.uint16)
        add_scanner_strip(image)

        cleaned, report = normalize_book.clean_right_edge(image, 0.9)

        self.assertEqual(cleaned.dtype, np.uint16)
        self.assertEqual(report["removed"], 1)
        self.assertTrue(np.all(cleaned[20:280, 397:400] == 65535))

    def test_exif_orientation_precedes_physical_right_edge_cleanup(self) -> None:
        image = np.full((400, 300), 255, np.uint8)
        image[0:3, 20:280] = 0
        image[0:3, 21:280:2] = 12
        image[1:3, 28:280:17] = 255
        source = Image.fromarray(image)
        exif = source.getexif()
        exif[274] = 6
        source.save(self.input / "page_001.png", exif=exif)
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        decoded = normalize_book.read_gray(output / "page_001.png")
        self.assertEqual(decoded.shape, (300, 400))
        self.assertTrue(np.all(decoded[20:280, 397:400] == 255))
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(report["pages"][0]["source_size"], [400, 300])
        self.assertEqual(report["pages"][0]["cleanup"]["removed"], 1)

    def test_transparent_png_and_webp_edges_are_composited_white(self) -> None:
        rgba = np.full((300, 400, 4), 255, np.uint8)
        rgba[20:280, 397:400, :3] = 0
        rgba[20:280, 397:400, 3] = 0

        for extension in (".png", ".webp"):
            with self.subTest(extension=extension):
                source = self.input / f"page_001{extension}"
                Image.fromarray(rgba, "RGBA").save(source, lossless=True)
                output = self.root / f"output-{extension[1:]}"

                result = self.run_cli(self.input, output, "--auto-canvas")

                self.assertEqual(result.returncode, 0, result.stderr)
                decoded = normalize_book.read_gray(output / "page_001.png")
                self.assertTrue(np.all(decoded[20:280, 397:400] == 255))
                report = json.loads(
                    (output / "cleanup.json").read_text(encoding="utf-8")
                )
                self.assertEqual(report["pages"][0]["cleanup"]["removed"], 0)
                source.unlink()

    def test_16_bit_png_transparency_is_composited_at_native_depth(self) -> None:
        image = np.full((120, 100), 50000, np.uint16)
        image[:, 99] = 1234
        Image.fromarray(image).save(
            self.input / "page_001.png", transparency=1234
        )

        decoded = normalize_book.read_gray(self.input / "page_001.png")

        self.assertEqual(decoded.dtype, np.uint16)
        self.assertTrue(np.all(decoded[:, 99] == 65535))
        self.assertTrue(np.all(decoded[:, :99] == 50000))

    def test_jpe_alias_and_disguised_supported_content_are_processed(self) -> None:
        image = np.full((120, 100), 251, np.uint8)
        ok, encoded = cv2.imencode(".jpg", image)
        self.assertTrue(ok)
        (self.input / "page_001.jpe").write_bytes(encoded.tobytes())
        (self.input / "page_002.data").write_bytes(encoded.tobytes())
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((output / "page_001.png").is_file())
        self.assertTrue((output / "page_002.png").is_file())
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(
            [item["detected_format"] for item in report["top_level_file_inventory"]],
            ["JPEG", "JPEG"],
        )

    def test_apng_is_rejected_by_detected_frame_count(self) -> None:
        first = Image.new("L", (100, 120), 255)
        second = Image.new("L", (100, 120), 250)
        first.save(
            self.input / "page_001.png",
            save_all=True,
            append_images=[second],
            duration=100,
            loop=0,
        )
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("detected APNG, 2 frames", result.stderr)
        self.assertFalse(output.exists())

    def test_static_png_text_named_like_apng_metadata_is_valid(self) -> None:
        text = PngImagePlugin.PngInfo()
        text.add_text("loop", "0")
        text.add_text("default_image", "1")
        Image.new("L", (100, 120), 255).save(
            self.input / "page_001.png", pnginfo=text
        )
        output = self.root / "output"

        self.assertEqual(
            normalize_book.encoded_image_info(self.input / "page_001.png"),
            ("PNG", 1),
        )
        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        self.assertEqual(
            report["top_level_file_inventory"][0]["detected_format"], "PNG"
        )
        self.assertTrue((output / "page_001.png").is_file())

    def test_corrupt_supported_suffix_fails_before_page_filtering(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "page_999.jpe").write_bytes(b"not an encoded image")
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("page_999.jpe (.jpe); detected unknown", result.stderr)
        self.assertFalse(output.exists())

    def test_truncated_jpeg_on_skipped_page_fails_before_page_filtering(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        skipped = self.input / "page_999.jpg"
        image = np.tile(np.arange(256, dtype=np.uint8), (256, 1))
        ok, encoded = cv2.imencode(".jpg", image)
        self.assertTrue(ok)
        skipped.write_bytes(encoded.tobytes()[:-128])
        self.assertEqual(normalize_book.encoded_image_info(skipped), ("JPEG", 1))
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("cannot decode image", result.stderr)
        self.assertIn("page_999.jpg", result.stderr)
        self.assertFalse(output.exists())

    def test_zero_bits_per_sample_tiff_on_skipped_page_fails_cleanly(
        self,
    ) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        malformed = self.input / "page_999.tiff"
        write_white_is_zero_tiff(
            malformed,
            np.full((120, 100), 255, np.uint16),
            8,
            photometric=1,
        )
        encoded = bytearray(malformed.read_bytes())
        bits_per_sample_entry = encoded.index(
            struct.pack("<HHI", 258, 3, 1)
        )
        struct.pack_into("<H", encoded, bits_per_sample_entry + 8, 0)
        malformed.write_bytes(encoded)
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("invalid TIFF dtype metadata", result.stderr)
        self.assertIn(malformed.name, result.stderr)
        self.assertNotIn("Traceback", result.stderr)
        self.assertFalse(output.exists())

    def test_complex_integer_tiff_on_skipped_page_fails_cleanly(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        complex_integer = self.input / "page_999.tiff"
        write_white_is_zero_tiff(
            complex_integer,
            np.full((120, 100), 65535, np.uint16),
            16,
            sample_format=5,
            photometric=1,
        )
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("cannot inspect TIFF metadata", result.stderr)
        self.assertIn(complex_integer.name, result.stderr)
        self.assertNotIn("data type 'E'", result.stderr)
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertFalse(output.exists())

    def test_complex_integer_tiff_decode_fallback_normalizes_type_error(
        self,
    ) -> None:
        complex_integer = self.input / "page_999.tiff"
        write_white_is_zero_tiff(
            complex_integer,
            np.full((120, 100), 65535, np.uint16),
            16,
            sample_format=5,
            photometric=1,
        )

        with self.assertRaises(ValueError) as raised:
            normalize_book.decode_tiff_grayscale_fallback(
                complex_integer,
                normalize_book.read_bounded_header(complex_integer),
            )

        self.assertEqual(
            str(raised.exception),
            f"cannot decode image: {complex_integer}",
        )
        self.assertIsInstance(raised.exception.__cause__, TypeError)

    def test_zero_page_tiff_fails_without_traceback_before_filtering(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "page_999.tiff").write_bytes(
            b"II*\x00\x00\x00\x00\x00"
        )
        output = self.root / "output"

        result = self.run_cli(
            self.input,
            output,
            "--auto-canvas",
            "--pages",
            1,
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("TIFF contains no image frames", result.stderr)
        self.assertNotIn("Traceback", result.stderr)
        self.assertFalse(output.exists())

    def test_apng_and_mpo_suffixes_are_in_image_inventory(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "page_998.apng").write_bytes(b"corrupt")
        (self.input / "page_999.mpo").write_bytes(b"corrupt")
        output = self.root / "output"

        result = self.run_cli(
            self.input, output, "--auto-canvas", "--pages", 1
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("page_998.apng (.apng); detected unknown", result.stderr)
        self.assertIn("page_999.mpo (.mpo); detected unknown", result.stderr)
        self.assertFalse(output.exists())

    def test_unsupported_encoded_format_with_unknown_suffix_is_rejected(self) -> None:
        image = Image.new("L", (100, 120), 255)
        disguised_bmp = self.input / "page_001.payload"
        image.save(disguised_bmp, format="BMP")
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("page_001.payload (.payload); detected BMP, 1 frames", result.stderr)
        self.assertFalse(output.exists())

    def test_renamed_dcx_is_detected_and_rejected(self) -> None:
        disguised_dcx = self.input / "page_001.payload"
        write_dcx(disguised_dcx)
        output = self.root / "output"

        self.assertEqual(
            normalize_book.encoded_image_info(disguised_dcx),
            ("DCX", 1),
        )
        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn(
            "page_001.payload (.payload); detected DCX, 1 frames",
            result.stderr,
        )
        self.assertFalse(output.exists())

    def test_mixed_unsupported_image_like_file_fails_explicitly(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "page_002.bmp").write_bytes(b"unsupported")
        (self.input / "notes.txt").write_text("batch notes", encoding="utf-8")
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported image-like top-level file(s)", result.stderr)
        self.assertIn("page_002.bmp (.bmp)", result.stderr)
        self.assertFalse(output.exists())

    def test_mixed_batch_dcx_fails_explicitly(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        (self.input / "page_002.dcx").write_bytes(b"unsupported")
        (self.input / "notes.txt").write_text("batch notes", encoding="utf-8")
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--auto-canvas")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported image-like top-level file(s)", result.stderr)
        self.assertIn("page_002.dcx (.dcx); detected unknown", result.stderr)
        self.assertFalse(output.exists())

    def test_explicit_canvas_streams_each_page_to_staging(self) -> None:
        first = self.input / "page_001.png"
        second = self.input / "page_002.png"
        write_image(first, np.full((120, 100), 255, np.uint8))
        write_image(second, np.full((120, 100), 251, np.uint8))
        output = self.root / "output"
        events: list[str] = []
        original_read = normalize_book.read_gray
        original_write = normalize_book.write_png

        def read(path: Path) -> np.ndarray:
            events.append(f"read:{path.name}")
            return original_read(path)

        def write(path: Path, image: np.ndarray) -> None:
            events.append(f"write:{path.name}")
            original_write(path, image)

        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", "100", "--height", "120",
        ]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            normalize_book, "read_gray", side_effect=read
        ), mock.patch.object(normalize_book, "write_png", side_effect=write):
            self.assertEqual(normalize_book.main(), 0)

        self.assertEqual(
            events,
            [
                "read:page_001.png", "read:page_002.png",
                "read:page_001.png", "write:page_001.png",
                "read:page_002.png", "write:page_002.png",
            ],
        )

    def test_frame_count_is_content_based_and_fails_closed(self) -> None:
        page = np.full((120, 100), 255, np.uint8)
        path = self.input / "page_001.tiff"
        self.assertTrue(cv2.imwritemulti(str(path), [page, page]))

        with self.assertRaisesRegex(ValueError, "exactly one decodable frame"):
            normalize_book.read_gray(path)

        opened = mock.MagicMock()
        opened.__enter__.return_value = opened
        opened.format = "TIFF"
        type(opened).n_frames = mock.PropertyMock(side_effect=OSError("count failed"))
        with mock.patch.object(normalize_book.Image, "open", return_value=opened):
            with self.assertRaisesRegex(ValueError, "cannot determine frame count"):
                normalize_book.read_gray(path)

    def test_decoder_uses_restricted_pillow_format_allowlist(self) -> None:
        path = self.input / "page_001.png"
        write_image(path, np.full((120, 100), 255, np.uint8))
        original_open = normalize_book.Image.open

        with mock.patch.object(
            normalize_book.Image,
            "open",
            wraps=original_open,
        ) as image_open:
            normalize_book.read_gray(path)

        self.assertEqual(
            image_open.call_args.kwargs["formats"],
            ("JPEG", "PNG", "TIFF", "WEBP"),
        )

    def test_inventory_uses_restricted_pillow_format_allowlist(self) -> None:
        path = self.input / "page_001.png"
        write_image(path, np.full((120, 100), 255, np.uint8))
        original_open = normalize_book.Image.open

        with mock.patch.object(
            normalize_book.Image,
            "open",
            wraps=original_open,
        ) as image_open:
            self.assertEqual(normalize_book.encoded_image_info(path), ("PNG", 1))

        formats = image_open.call_args.kwargs["formats"]
        self.assertEqual(formats, normalize_book.PILLOW_INVENTORY_ALLOWLIST)
        self.assertIn("BMP", formats)
        self.assertIn("DCX", formats)
        self.assertNotEqual(formats, tuple(Image.ID))

    def test_edge_component_count_budget_fails_without_truncating_candidates(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        image[10:20, 99] = 0
        image[40:50, 99] = 0

        with self.assertRaisesRegex(
            normalize_book.ReviewRequiredError,
            "2 candidates.*manual review",
        ):
            normalize_book.clean_right_edge(
                image, 0.9, max_reported_components=1
            )

    def test_batch_edge_component_budget_fails_without_output(self) -> None:
        first = np.full((120, 100), 255, np.uint8)
        second = first.copy()
        first[10:100, 99] = 0
        second[10:100, 99] = 0
        write_image(self.input / "page_001.png", first)
        write_image(self.input / "page_002.png", second)
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", "100", "--height", "120",
        ]

        with mock.patch.object(
            normalize_book, "MAX_RETAINED_EDGE_COMPONENTS_BATCH", 1
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_edge_report_memory_budget_fails_without_output(self) -> None:
        image = np.full((120, 100), 255, np.uint8)
        image[10:100, 99] = 0
        write_image(self.input / "page_001.png", image)
        output = self.root / "output"
        argv = [
            "normalize_book.py", str(self.input), str(output),
            "--width", "100", "--height", "120",
        ]

        with mock.patch.object(
            normalize_book, "MAX_RETAINED_EDGE_REPORT_BYTES_BATCH", 1
        ), mock.patch.object(sys, "argv", argv), self.assertRaises(SystemExit):
            normalize_book.main()

        self.assertFalse(output.exists())

    def test_report_is_serialized_once_and_streamed_to_stdout(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertNotIn("json.dumps(report", source)
        self.assertIn("json.dump(report", source)
        self.assertIn("shutil.copyfileobj", source)

    def test_apng_detection_uses_pillow_metadata_not_raw_chunk_scanning(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertNotIn('b"acTL"', source)
        self.assertIn("image.info", source)
        self.assertIn("n_frames", source)

    def test_stdout_is_flushed_before_atomic_publication(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"
        events: list[str] = []
        actual_commit = normalize_book.commit_transaction

        class RecordingStdout(io.StringIO):
            def write(self, value: str) -> int:
                events.append("write")
                return super().write(value)

            def flush(self) -> None:
                events.append("flush")
                super().flush()

        def commit(staging: Path, destination: Path) -> None:
            events.append("commit")
            actual_commit(staging, destination)

        argv = [
            "normalize_book.py", str(self.input), str(output), "--auto-canvas",
        ]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            sys, "stdout", RecordingStdout()
        ), mock.patch.object(
            normalize_book, "commit_transaction", side_effect=commit
        ):
            self.assertEqual(normalize_book.main(), 0)

        self.assertEqual(events[-3:], ["write", "flush", "commit"])
        self.assertTrue((output / "cleanup.json").is_file())

    def test_stdout_failure_prevents_publication_and_removes_staging(self) -> None:
        write_image(self.input / "page_001.png", np.full((120, 100), 255, np.uint8))
        output = self.root / "output"

        class FailingStdout:
            def write(self, value: str) -> int:
                raise BrokenPipeError("closed")

            def flush(self) -> None:
                raise AssertionError("flush should not follow failed write")

        argv = [
            "normalize_book.py", str(self.input), str(output), "--auto-canvas",
        ]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            sys, "stdout", FailingStdout()
        ):
            with self.assertRaises(BrokenPipeError):
                normalize_book.main()

        self.assertFalse(output.exists())
        self.assertEqual(list(self.root.glob(".output.scan-book-layout-*")), [])

    def test_runner_uses_practical_ephemeral_environment(self) -> None:
        runner = (SKILL_DIR / "scripts" / "run.ps1").read_text(encoding="utf-8")
        test_runner = (SKILL_DIR / "tests" / "run.ps1").read_text(
            encoding="utf-8"
        )

        self.assertIn('$pythonVersion = "3.12.10"', runner)
        self.assertNotIn("PIP_INDEX_URL", runner)
        self.assertNotIn("ado token", runner)
        self.assertIn('MISE_NO_CONFIG = "1"', runner)
        self.assertIn('MISE_CONFIG_DIR', runner)
        self.assertGreaterEqual(runner.count('--no-config'), 2)
        self.assertIn('.scan-book-layout-', runner)
        self.assertIn('Remove-Item -LiteralPath $sessionRoot -Recurse -Force', runner)
        self.assertIn('--require-hashes', runner)
        self.assertIn('--no-deps', runner)
        self.assertIn('& $python -I -B $program @ScriptArgs', runner)
        self.assertIn('$Script -ceq "normalize_book.py"', runner)
        self.assertIn(
            '$Script -ceq "tests/test_normalize_book.py"',
            runner,
        )
        self.assertIn(
            'Join-Path $skillRoot "tests\\test_normalize_book.py"',
            runner,
        )
        self.assertNotIn('startup_launcher', runner)
        self.assertNotIn('manifest.json', runner)
        self.assertNotIn('RECORD', runner)
        self.assertNotIn('Get-Acl', runner)

        requirements = (
            SKILL_DIR / "scripts" / "requirements.lock"
        ).read_text(encoding="utf-8")
        self.assertEqual(requirements.count("--hash=sha256:"), 5)
        for package in (
            "imagecodecs==2026.6.26",
            "numpy==2.2.6",
            "opencv-python-headless==4.12.0.88",
            "Pillow==12.3.0",
            "tifffile==2026.7.31",
        ):
            self.assertIn(package, requirements)

        removed = (
            "startup_launcher.exe",
            "startup_launcher.c",
            "python-runtime-manifest.json",
            "runtime_integrity.ps1",
            "verify_wheel_install.py",
            "check_runtime.py",
            "check_pip_runtime.py",
        )
        for name in removed:
            self.assertFalse((SKILL_DIR / "scripts" / name).exists(), name)

        self.assertIn(
            '& (Join-Path $skillRoot "scripts\\run.ps1")',
            test_runner,
        )
        self.assertIn('"tests/test_normalize_book.py" "-v"', test_runner)
        self.assertIn("Push-Location $skillRoot", test_runner)
        self.assertIn("Pop-Location", test_runner)
        self.assertNotIn("mise", test_runner)

    def test_skill_documents_network_required_every_invocation(self) -> None:
        documentation = (SKILL_DIR / "SKILL.md").read_text(encoding="utf-8")

        self.assertIn("Every invocation requires", documentation)
        self.assertIn("Network access is required on every invocation", documentation)

    def test_skill_documents_inventory_and_processing_decodes(self) -> None:
        documentation = (SKILL_DIR / "SKILL.md").read_text(encoding="utf-8")
        normalized = " ".join(documentation.split())

        self.assertIn(
            "Each supported image is then fully decoded during inventory",
            normalized,
        )
        self.assertIn(
            "Each selected image is decoded again during processing",
            normalized,
        )
        self.assertNotIn("before any full raster decode", normalized)
        self.assertNotIn("without a full raster decode", normalized)

    def test_page_filter_decodes_all_inventory_and_selected_processing(
        self,
    ) -> None:
        write_image(
            self.input / "page_001.png",
            np.full((120, 100), 255, np.uint8),
        )
        write_image(
            self.input / "page_002.png",
            np.full((100, 120), 240, np.uint8),
        )
        output = self.root / "output"
        decoded: list[str] = []
        original_read_gray = normalize_book.read_gray

        def recording_read_gray(path: Path) -> np.ndarray:
            decoded.append(path.name)
            return original_read_gray(path)

        argv = [
            "normalize_book.py",
            str(self.input),
            str(output),
            "--auto-canvas",
            "--pages",
            "1",
        ]
        with (
            mock.patch.object(sys, "argv", argv),
            mock.patch.object(
                normalize_book,
                "read_gray",
                side_effect=recording_read_gray,
            ),
            mock.patch.object(sys, "stdout", io.StringIO()),
        ):
            self.assertEqual(normalize_book.main(), 0)

        self.assertEqual(decoded.count("page_001.png"), 2)
        self.assertEqual(decoded.count("page_002.png"), 1)


if __name__ == "__main__":
    unittest.main()
