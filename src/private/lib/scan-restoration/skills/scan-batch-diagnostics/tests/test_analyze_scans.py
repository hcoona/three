from __future__ import annotations

import importlib.util
import io
import json
import os
import struct
import sys
import tempfile
import unittest
import warnings
from pathlib import Path
from unittest import mock

import cv2
import numpy as np
import tifffile
from PIL import Image, TiffImagePlugin


SKILL_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = SKILL_ROOT / "scripts" / "analyze_scans.py"
SPEC = importlib.util.spec_from_file_location("analyze_scans", SCRIPT_PATH)
assert SPEC and SPEC.loader
analyze_scans = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(analyze_scans)


def repository_fixture_root() -> Path:
    configured = os.environ.get("SCAN_RESTORATION_FIXTURE_ROOT")
    if configured:
        return Path(configured).expanduser().resolve()
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "mise.toml").is_file() and (candidate / "apm.yml").is_file():
            return candidate
    return Path(__file__).resolve().parent


def write_image(path: Path) -> None:
    image = np.full((120, 160), 245, np.uint8)
    cv2.line(image, (20, 45), (140, 45), 20, 2)
    cv2.line(image, (20, 75), (140, 75), 20, 2)
    assert cv2.imwrite(str(path), image)


class AnalyzeScansTests(unittest.TestCase):
    def run_main(self, input_dir: Path, output: Path) -> int:
        arguments = [
            "analyze_scans.py",
            str(input_dir.resolve()),
            "--output",
            str(output.resolve()),
        ]
        with mock.patch.object(sys, "argv", arguments):
            return analyze_scans.main()

    def test_hough_and_clustering_work_are_bounded(self) -> None:
        lines = np.asarray(
            [
                [[0, 20 + index % 160, 199, 20 + index % 160]]
                for index in range(analyze_scans.MAX_HOUGH_LINE_CANDIDATES + 100)
            ],
            dtype=np.int32,
        )
        mask = np.zeros((200, 200), np.uint8)
        mask[20:180, :] = 255

        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.horizontal_skew(mask)

        self.assertTrue(result["hough_line_limit_reached"])
        self.assertEqual(result["status"], "review_only_truncated")
        self.assertLessEqual(
            result["hough_line_count"], analyze_scans.MAX_CLUSTER_FRAGMENTS
        )

    def test_decode_budget_fails_before_pixel_load(self) -> None:
        path = mock.MagicMock(spec=Path)
        path.suffix.lower.return_value = ".png"
        data = b"bounded immutable bytes"
        image = mock.MagicMock()
        image.__enter__.return_value = image
        image.format = "PNG"
        image.mode = "RGBA"
        image.width = 9000
        image.height = 9000
        image.size = (9000, 9000)
        image.getbands.return_value = ("R", "G", "B", "A")
        image.getexif.return_value = {}
        with (
            mock.patch.object(analyze_scans, "image_frame_count", return_value=(1, None)),
            mock.patch.object(analyze_scans, "source_sample_encoding", return_value=(8, "unsigned_integer", False, None, None)),
            mock.patch.object(analyze_scans, "validate_sample_encoding"),
            mock.patch.object(analyze_scans.Image, "open", return_value=image),
            self.assertRaisesRegex(ValueError, "decode working memory"),
        ):
            analyze_scans.read_gray(path, data)
        image.load.assert_not_called()

    def test_16_bit_tiff_near_limit_uses_simultaneous_peak_estimate(self) -> None:
        width = 8000
        height = analyze_scans.MAX_DECODE_WORKING_BYTES // (7 * width)
        accepted = analyze_scans.estimate_tiff_uint16_working_bytes(
            width,
            height,
            orientation=1,
            photometric=1,
            max_dimension=analyze_scans.ANALYSIS_MAX_DIMENSION,
        )
        rejected = analyze_scans.estimate_tiff_uint16_working_bytes(
            width,
            height + 1,
            orientation=1,
            photometric=1,
            max_dimension=analyze_scans.ANALYSIS_MAX_DIMENSION,
        )

        self.assertLessEqual(accepted, analyze_scans.MAX_DECODE_WORKING_BYTES)
        self.assertGreater(rejected, analyze_scans.MAX_DECODE_WORKING_BYTES)
        analyze_scans.enforce_tiff_uint16_budget(
            width,
            height,
            orientation=1,
            photometric=1,
            max_dimension=analyze_scans.ANALYSIS_MAX_DIMENSION,
        )
        with self.assertRaisesRegex(ValueError, "decode working memory"):
            analyze_scans.enforce_tiff_uint16_budget(
                width,
                height + 1,
                orientation=1,
                photometric=1,
                max_dimension=analyze_scans.ANALYSIS_MAX_DIMENSION,
            )

    def test_rgb_orientation_1_near_limit_skips_unbudgeted_copy(self) -> None:
        width = 8000
        height = analyze_scans.MAX_DECODE_WORKING_BYTES // (6 * width)

        accepted = analyze_scans.estimate_decode_working_bytes(
            width,
            height,
            "RGB",
            has_transparency=False,
            orientation=1,
        )
        rejected = analyze_scans.estimate_decode_working_bytes(
            width,
            height + 1,
            "RGB",
            has_transparency=False,
            orientation=1,
        )

        self.assertLessEqual(accepted, analyze_scans.MAX_DECODE_WORKING_BYTES)
        self.assertGreater(rejected, analyze_scans.MAX_DECODE_WORKING_BYTES)
        analyze_scans.enforce_decode_budget(
            width,
            height,
            "RGB",
            has_transparency=False,
            orientation=1,
        )
        with self.assertRaisesRegex(ValueError, "decode working memory"):
            analyze_scans.enforce_decode_budget(
                width,
                height + 1,
                "RGB",
                has_transparency=False,
                orientation=1,
            )

        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "orientation-1.png"
            exif = Image.Exif()
            exif[274] = 1
            Image.new("RGB", (2, 3), (10, 20, 30)).save(path, exif=exif)

            with mock.patch.object(
                analyze_scans.ImageOps,
                "exif_transpose",
                side_effect=AssertionError("orientation 1 must not be copied"),
            ):
                gray, metadata = analyze_scans.read_gray(path)

        self.assertEqual(gray.shape, (3, 2))
        self.assertFalse(metadata["exif_orientation_applied"])
        self.assertEqual(
            (
                metadata["source_width"],
                metadata["source_height"],
                metadata["display_width"],
                metadata["display_height"],
            ),
            (2, 3, 2, 3),
        )

    def test_rgb_72mp_estimator_includes_pillow_storage_and_gray_copy_peak(
        self,
    ) -> None:
        pixels = 72_000_000

        estimate = analyze_scans.estimate_decode_working_bytes(
            9000,
            8000,
            "RGB",
            has_transparency=False,
            orientation=1,
        )

        self.assertEqual(estimate, 6 * pixels)
        self.assertGreater(estimate, analyze_scans.MAX_DECODE_WORKING_BYTES)
        with self.assertRaisesRegex(ValueError, "decode working memory"):
            analyze_scans.enforce_decode_budget(
                9000,
                8000,
                "RGB",
                has_transparency=False,
                orientation=1,
            )

    def test_encoded_size_limit_precedes_file_read(self) -> None:
        path = mock.MagicMock(spec=Path)
        path.stat.return_value.st_size = analyze_scans.MAX_ENCODED_FILE_BYTES + 1

        with self.assertRaisesRegex(ValueError, "encoded image size"):
            analyze_scans.read_encoded_file(path)

        path.open.assert_not_called()

    def test_encoded_growth_is_bounded_during_read(self) -> None:
        path = mock.MagicMock(spec=Path)
        path.stat.return_value.st_size = 1
        stream = mock.MagicMock()
        stream.__enter__.return_value = stream
        stream.read.return_value = b"x" * 9
        path.open.return_value = stream

        with (
            mock.patch.object(analyze_scans, "MAX_ENCODED_FILE_BYTES", 8),
            self.assertRaisesRegex(ValueError, "encoded image exceeds"),
        ):
            analyze_scans.read_encoded_file(path)

        stream.read.assert_called_once_with(9)

    def test_inventory_streams_once_and_preserves_natural_order(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            for name in ("page10.png", "notes.txt", "page2.png", "page1.png"):
                (root / name).write_bytes(b"x")

            real_scandir = os.scandir
            with mock.patch.object(
                analyze_scans.os, "scandir", wraps=real_scandir
            ) as scandir:
                candidates, unsupported = analyze_scans.inventory_directory(root)

        scandir.assert_called_once_with(root)
        self.assertEqual(
            [path.name for path in candidates],
            ["page1.png", "page2.png", "page10.png"],
        )
        self.assertEqual([path.name for path in unsupported], ["notes.txt"])

    def test_inventory_candidate_and_entry_limits_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            (root / "page1.png").write_bytes(b"x")
            (root / "page2.png").write_bytes(b"x")

            with (
                mock.patch.object(analyze_scans, "MAX_CANDIDATE_FILES", 1),
                self.assertRaisesRegex(ValueError, "candidate image count"),
            ):
                analyze_scans.inventory_directory(root)
            with (
                mock.patch.object(analyze_scans, "MAX_DIRECTORY_ENTRIES", 1),
                self.assertRaisesRegex(ValueError, "directory entry count"),
            ):
                analyze_scans.inventory_directory(root)

    def test_aggregate_encoded_budget_fails_before_any_candidate_read(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            (root / "page1.png").write_bytes(b"1234")
            (root / "page2.png").write_bytes(b"5678")
            output = root / "report.json"

            with (
                mock.patch.object(analyze_scans, "MAX_AGGREGATE_ENCODED_BYTES", 7),
                mock.patch.object(analyze_scans, "read_encoded_file") as read,
                self.assertRaises(SystemExit),
            ):
                self.run_main(root, output)

            read.assert_not_called()
            self.assertFalse(output.exists())

    def test_inventory_allows_normal_72_page_batch(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            for index in range(72, 0, -1):
                (root / f"page{index}.png").write_bytes(b"x")

            candidates, unsupported = analyze_scans.inventory_directory(root)

        self.assertEqual(len(candidates), 72)
        self.assertFalse(unsupported)
        self.assertEqual(candidates[0].name, "page1.png")
        self.assertEqual(candidates[-1].name, "page72.png")

    def test_horizontal_geometry_requires_real_line_evidence(self) -> None:
        mask = np.zeros((200, 200), np.uint8)
        mask[40:160, 70:130] = 255
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=None):
            result = analyze_scans.horizontal_skew(mask)
        self.assertEqual(result["status"], "no_line_evidence")
        self.assertEqual(result["hough_line_count"], 0)

    def test_sparse_convergence_evidence_is_low_confidence(self) -> None:
        lines = np.asarray(
            [
                [[20, 10, 20, 90]],
                [[24, 10, 24, 90]],
                [[75, 10, 75, 90]],
                [[79, 10, 79, 90]],
            ],
            dtype=np.int32,
        )
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.vertical_convergence(
                np.zeros((100, 100), np.uint8)
            )
        self.assertEqual(result["status"], "insufficient_structural_lines")
        self.assertLess(result["confidence"], 0.5)
        self.assertEqual(result["independent_line_count"], 2)

    def test_connected_staff_barlines_remain_independent(self) -> None:
        mask = np.zeros((300, 300), np.uint8)
        for y in (60, 72, 84, 180, 192, 204):
            cv2.line(mask, (20, y), (280, y), 255, 2)
        for x in (50, 110, 190, 250):
            cv2.line(mask, (x, 50), (x, 214), 255, 3)

        result = analyze_scans.vertical_convergence(mask)

        self.assertEqual(result["independent_line_count"], 4)
        self.assertEqual(result["left_line_count"], 2)
        self.assertEqual(result["right_line_count"], 2)
        self.assertEqual(result["status"], "ok")
        self.assertGreaterEqual(result["confidence"], 0.5)

    def test_four_system_barlines_keep_component_fitted_convergence(self) -> None:
        support = np.zeros((400, 300), np.uint8)
        lines = []
        for top in (10, 110, 210, 310):
            bottom = top + 70
            for center_x in (40, 100):
                cv2.line(
                    support,
                    (center_x - 3, top),
                    (center_x + 3, bottom),
                    255,
                    9,
                )
                lines.append([[center_x, top, center_x, bottom]])
            for center_x in (200, 260):
                cv2.line(
                    support,
                    (center_x + 3, top),
                    (center_x - 3, bottom),
                    255,
                    9,
                )
                lines.append([[center_x, top, center_x, bottom]])

        with (
            mock.patch.object(
                analyze_scans.cv2, "morphologyEx", return_value=support
            ),
            mock.patch.object(
                analyze_scans.cv2,
                "HoughLinesP",
                return_value=np.asarray(lines, dtype=np.int32),
            ),
        ):
            result = analyze_scans.vertical_convergence(support)

        self.assertEqual(result["independent_line_count"], 16)
        self.assertEqual(result["left_line_count"], 8)
        self.assertEqual(result["right_line_count"], 8)
        self.assertGreater(result["left_deviation_degrees"], 2.5)
        self.assertLess(result["right_deviation_degrees"], -2.5)
        self.assertLess(result["right_minus_left_degrees"], -5.0)
        self.assertEqual(result["status"], "ok")
        self.assertGreaterEqual(result["confidence"], 0.5)

    def test_normal_barlines_merge_duplicate_hough_fragments_per_stroke(self) -> None:
        support = np.zeros((300, 300), np.uint8)
        lines = []
        for center_x in (45, 110, 190, 255):
            cv2.line(support, (center_x, 20), (center_x, 280), 255, 7)
            lines.extend(
                [
                    [[center_x, 20, center_x, 170]],
                    [[center_x, 130, center_x, 280]],
                ]
            )

        with (
            mock.patch.object(
                analyze_scans.cv2, "morphologyEx", return_value=support
            ),
            mock.patch.object(
                analyze_scans.cv2,
                "HoughLinesP",
                return_value=np.asarray(lines, dtype=np.int32),
            ),
        ):
            result = analyze_scans.vertical_convergence(support)

        self.assertEqual(result["independent_line_count"], 4)
        self.assertEqual(result["left_line_count"], 2)
        self.assertEqual(result["right_line_count"], 2)
        self.assertAlmostEqual(result["right_minus_left_degrees"], 0.0, places=3)
        self.assertEqual(result["status"], "ok")
        self.assertGreaterEqual(result["confidence"], 0.5)

    def test_vertical_confidence_penalizes_angular_spread(self) -> None:
        lines = np.asarray(
            [
                [[10, 10, 10, 90]],
                [[28, 10, 40, 90]],
                [[60, 10, 60, 90]],
                [[90, 10, 78, 90]],
            ],
            dtype=np.int32,
        )
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.vertical_convergence(
                np.zeros((100, 100), np.uint8)
            )
        self.assertEqual(result["independent_line_count"], 4)
        self.assertEqual(result["status"], "low_confidence")
        self.assertLess(result["angular_consistency"], 0.5)
        self.assertLess(result["confidence"], 0.5)

    def test_vertical_confidence_is_independent_and_uses_weaker_side(self) -> None:
        lines = np.asarray(
            [
                [[10, 5, 10, 95]],
                [[35, 5, 35, 95]],
                [[65, 5, 65, 95]],
                [[90, 5, 78, 95]],
            ],
            dtype=np.int32,
        )
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.vertical_convergence(
                np.zeros((100, 100), np.uint8)
            )

        self.assertGreater(result["left_confidence"], result["right_confidence"])
        self.assertEqual(result["confidence"], result["right_confidence"])
        self.assertEqual(
            result["left_evidence"]["line_count"], result["left_line_count"]
        )
        self.assertIn("total_length_confidence", result["left_evidence"])
        self.assertIn("x_coverage_fraction", result["left_evidence"])
        self.assertIn("band_coverage_fraction", result["left_evidence"])
        self.assertIn("system_coverage_fraction", result["left_evidence"])
        self.assertIn("angular_consistency", result["left_evidence"])
        self.assertEqual(result["status"], "low_confidence")

    def test_grand_staff_barlines_can_be_trusted_but_note_stems_cannot(self) -> None:
        barlines = np.asarray(
            [
                [[15, 5, 15, 15]],
                [[35, 25, 35, 35]],
                [[65, 5, 65, 15]],
                [[85, 25, 85, 35]],
            ],
            dtype=np.int32,
        )
        stems = np.asarray(
            [
                [[15, 5, 15, 9]],
                [[35, 25, 35, 29]],
                [[65, 5, 65, 9]],
                [[85, 25, 85, 29]],
            ],
            dtype=np.int32,
        )
        mask = np.zeros((100, 100), np.uint8)
        with mock.patch.object(
            analyze_scans.cv2, "HoughLinesP", return_value=barlines
        ):
            structural = analyze_scans.vertical_convergence(mask)
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=stems):
            non_structural = analyze_scans.vertical_convergence(mask)

        self.assertEqual(structural["status"], "ok")
        self.assertGreaterEqual(structural["confidence"], 0.5)
        self.assertEqual(non_structural["status"], "insufficient_structural_lines")
        self.assertEqual(non_structural["independent_line_count"], 0)

    def test_aggregated_short_stems_are_review_only_but_real_barlines_are_trusted(
        self,
    ) -> None:
        height = width = 400

        def convergence_for(length: int) -> dict[str, object]:
            support = np.zeros((height, width), np.uint8)
            lines = []
            for y, x in zip(
                (10, 110, 210, 310, 10, 110, 210, 310),
                (30, 90, 140, 170, 230, 280, 330, 370),
            ):
                cv2.line(support, (x, y), (x, y + length), 255, 3)
                lines.append([[x, y, x, y + length]])
            with (
                mock.patch.object(
                    analyze_scans.cv2, "morphologyEx", return_value=support
                ),
                mock.patch.object(
                    analyze_scans.cv2,
                    "HoughLinesP",
                    return_value=np.asarray(lines, dtype=np.int32),
                ),
            ):
                return analyze_scans.vertical_convergence(support)

        short_stems = convergence_for(18)
        real_barlines = convergence_for(72)

        self.assertEqual(short_stems["left_evidence"]["confidence"], 1.0)
        self.assertEqual(short_stems["right_evidence"]["confidence"], 1.0)
        self.assertEqual(short_stems["length_confidence"], 0.0)
        self.assertEqual(short_stems["confidence"], 0.0)
        self.assertNotEqual(short_stems["status"], "ok")
        self.assertEqual(real_barlines["length_confidence"], 1.0)
        self.assertEqual(real_barlines["status"], "ok")
        self.assertEqual(real_barlines["confidence"], 1.0)

    def test_one_slanted_stroke_per_side_is_low_confidence(self) -> None:
        mask = np.zeros((300, 300), np.uint8)
        cv2.line(mask, (55, 20), (75, 280), 255, 14)
        cv2.line(mask, (245, 20), (225, 280), 255, 14)

        result = analyze_scans.vertical_convergence(mask)

        self.assertEqual(result["independent_line_count"], 2)
        self.assertEqual(result["left_line_count"], 1)
        self.assertEqual(result["right_line_count"], 1)
        self.assertEqual(result["status"], "insufficient_structural_lines")
        self.assertLess(result["confidence"], 0.5)

    def test_paper_relative_ink_metrics_work_on_yellow_and_dark_paper(self) -> None:
        fractions = []
        for paper, ink in ((205, 45), (105, 20)):
            page = np.full((200, 160), paper, np.uint8)
            cv2.rectangle(page, (30, 40), (130, 45), ink, -1)
            cv2.rectangle(page, (30, 100), (130, 105), ink, -1)
            _, metrics = analyze_scans.paper_relative_metrics(page)
            fractions.append(metrics["ink_fraction"])
            self.assertGreater(metrics["global_paper_level"], ink + 40)
            self.assertGreater(metrics["ink_contrast_threshold"], 0)
            self.assertGreater(metrics["ink_fraction"], 0.02)
            self.assertLess(metrics["ink_fraction"], 0.10)
            self.assertLess(metrics["border_contamination_fraction"], 0.01)
        self.assertLess(abs(fractions[0] - fractions[1]), 0.005)

    def test_duplicate_horizontal_fragments_are_one_spatial_band(self) -> None:
        lines = np.asarray(
            [
                [[10, 49, 190, 49]],
                [[12, 50, 188, 50]],
                [[15, 51, 185, 51]],
            ],
            dtype=np.int32,
        )
        mask = np.zeros((200, 200), np.uint8)
        mask[45:55, 10:190] = 255
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.horizontal_skew(mask)
        self.assertEqual(result["status"], "no_line_evidence")
        self.assertEqual(result["hough_line_count"], 3)
        self.assertEqual(result["independent_line_count"], 1)

    def test_single_thick_horizontal_stroke_is_one_physical_line(self) -> None:
        mask = np.zeros((240, 320), np.uint8)
        cv2.line(mask, (20, 120), (300, 120), 255, 18)

        result = analyze_scans.horizontal_skew(mask)

        self.assertGreaterEqual(result["hough_line_count"], 2)
        self.assertEqual(result["independent_line_count"], 1)
        self.assertEqual(result["status"], "no_line_evidence")
        self.assertLess(result["confidence"], 0.5)

    def test_connected_multi_staff_strokes_remain_independent(self) -> None:
        mask = np.zeros((240, 320), np.uint8)
        for y in (70, 82, 94, 130, 142, 154):
            cv2.line(mask, (30, y), (290, y), 255, 2)
        for x in (80, 240):
            cv2.line(mask, (x, 65), (x, 159), 255, 2)

        result = analyze_scans.horizontal_skew(mask)

        self.assertEqual(result["independent_line_count"], 6)
        self.assertEqual(result["status"], "ok")
        self.assertGreaterEqual(result["confidence"], 0.5)

    def test_collinear_sloped_horizontal_fragments_share_intercept_model(self) -> None:
        lines = np.asarray(
            [
                [[10, 31, 80, 38]],
                [[70, 37, 140, 44]],
                [[130, 43, 190, 49]],
            ],
            dtype=np.int32,
        )
        mask = np.zeros((200, 200), np.uint8)
        mask[20:60, :] = 255
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.horizontal_skew(mask)
        self.assertEqual(result["independent_line_count"], 1)
        self.assertEqual(result["status"], "no_line_evidence")

    def test_horizontal_angular_disagreement_reduces_confidence(self) -> None:
        lines = np.asarray(
            [
                [[10, 35, 190, 35]],
                [[10, 70, 190, 89]],
                [[10, 125, 190, 106]],
            ],
            dtype=np.int32,
        )
        mask = np.zeros((160, 200), np.uint8)
        for y in (35, 80, 115):
            cv2.line(mask, (10, y), (190, y), 255, 2)
        with mock.patch.object(analyze_scans.cv2, "HoughLinesP", return_value=lines):
            result = analyze_scans.horizontal_skew(mask)
        self.assertEqual(result["independent_line_count"], 3)
        self.assertLess(result["angular_consistency"], 0.5)
        self.assertLess(result["confidence"], 0.5)
        self.assertEqual(result["status"], "low_confidence")

    def test_tiny_geometry_reports_insufficient_evidence(self) -> None:
        mask = np.zeros((2, 2), np.uint8)
        self.assertEqual(
            analyze_scans.horizontal_skew(mask)["status"],
            "insufficient_evidence",
        )
        self.assertEqual(
            analyze_scans.vertical_convergence(mask)["status"],
            "insufficient_evidence",
        )

    def test_unknown_frame_count_fails_closed_and_keeps_position(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            write_image(root / "page1.png")
            (root / "page2.png").write_bytes(b"not an image")
            write_image(root / "page3.png")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(
                [item["file"] for item in report["candidate_sequence"]],
                ["page1.png", "page2.png", "page3.png"],
            )
            failure = report["candidate_sequence"][1]
            self.assertEqual(failure["status"], "unsupported_frame_count_unknown")
            self.assertIsNone(failure["frame_count"])
            self.assertTrue(failure["frame_count_error"])
            self.assertEqual(
                report["review_lists"]["representative"]["middle"], ["page1.png"]
            )
            self.assertEqual(
                report["review_lists"]["outliers"]["combined"],
                ["page1.png", "page2.png", "page3.png"],
            )
            self.assertIn("page2.png", report["review_lists"]["mandatory_visual_review"])
            entries = {
                item["file"]: item["reasons"]
                for item in report["review_lists"]["mandatory_visual_review_entries"]
            }
            self.assertTrue(
                any("unknown frame count" in reason for reason in entries["page2.png"])
            )

    def test_multi_frame_image_is_explicitly_unsupported(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            frames = [
                np.full((40, 50), 230, np.uint8),
                np.full((40, 50), 210, np.uint8),
            ]
            path = root / "book.tiff"
            if not hasattr(cv2, "imwritemulti") or not cv2.imwritemulti(
                str(path), frames
            ):
                self.skipTest("OpenCV multi-page TIFF writing unavailable")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 2)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(report["page_count"], 0)
            self.assertEqual(report["unsupported_candidate_count"], 1)
            self.assertEqual(
                report["candidate_sequence"][0]["status"],
                "unsupported_frame_count",
            )
            self.assertEqual(report["unsupported_candidates"][0]["frame_count"], 2)
            self.assertIn(
                "one image per file",
                report["unsupported_candidates"][0]["reason"],
            )

    def test_every_accepted_format_gets_header_frame_counting(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            names = ["a.jpg", "b.jpeg", "c.png", "d.tif", "e.tiff", "f.webp"]
            for name in names:
                (root / name).write_bytes(b"candidate")
            output = root / "report.json"
            row = {
                "width": 10,
                "height": 10,
                "aspect_ratio": 1.0,
                "median": 240.0,
                "ink_fraction": 0.01,
                "border_dark_fraction": 0.0,
                "horizontal_skew": {
                    "status": "ok",
                    "angle_degrees_clockwise": 0.0,
                    "confidence": 1.0,
                },
                "vertical_convergence": {
                    "status": "ok",
                    "right_minus_left_degrees": 0.0,
                    "confidence": 1.0,
                    "length_confidence": 1.0,
                },
            }
            with (
                mock.patch.object(
                    analyze_scans,
                    "image_frame_count",
                    return_value=(1, None),
                ) as inspect,
                mock.patch.object(
                    analyze_scans,
                    "page_metrics",
                    side_effect=lambda path, order, data: {
                        **row,
                        "file": path.name,
                        "order": order,
                    },
                ),
            ):
                self.assertEqual(self.run_main(root, output), 0)
            self.assertEqual(inspect.call_count, len(names))
            for call in inspect.call_args_list:
                self.assertIsInstance(call.args[0], Path)
                self.assertIsInstance(call.args[1], bytes)

    def test_frame_count_does_not_decode_pixels(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "book.tiff"
            path.write_bytes(b"header")
            image = mock.MagicMock()
            image.__enter__.return_value = image
            image.n_frames = 12
            image.format = "TIFF"
            with mock.patch.object(analyze_scans.Image, "open", return_value=image):
                count, error = analyze_scans.image_frame_count(path)
            self.assertEqual(count, 12)
            self.assertIsNone(error)
            image.load.assert_not_called()
            image.seek.assert_not_called()

    def test_unicode_filename_frame_count_uses_pillow_header(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "页面.png"
            write_image(path)
            count, error = analyze_scans.image_frame_count(path)
            self.assertEqual(count, 1)
            self.assertIsNone(error)

    def test_invalid_multiframe_decode_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "bad.png"
            path.write_bytes(b"not an image")
            with mock.patch.object(
                analyze_scans.Image,
                "open",
                side_effect=analyze_scans.UnidentifiedImageError("bad header"),
            ):
                count, error = analyze_scans.image_frame_count(path)
            self.assertIsNone(count)
            self.assertTrue(error)

    def test_header_bomb_warning_fails_candidate_closed(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "large.png"
            path.write_bytes(b"header")
            with mock.patch.object(
                analyze_scans.Image,
                "open",
                side_effect=analyze_scans.Image.DecompressionBombWarning("large"),
            ):
                count, error = analyze_scans.image_frame_count(path)
            self.assertIsNone(count)
            self.assertIn("DecompressionBombWarning", error or "")

    def test_transparent_black_is_composited_to_white(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "alpha.png"
            rgba = np.zeros((20, 30, 4), np.uint8)
            rgba[:, :, 3] = 0
            rgba[8:12, 10:20, :3] = 0
            rgba[8:12, 10:20, 3] = 255
            Image.fromarray(rgba).save(path)

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(int(gray[0, 0]), 255)
            self.assertEqual(int(gray[9, 12]), 0)
            self.assertEqual(metadata["transparency_composited_onto"], "white")
            self.assertEqual(metadata["source_mode"], "RGBA")
            self.assertEqual(metadata["source_bit_depth"], 8)

    def test_grayscale_trns_is_composited_to_white(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "grayscale-trns.png"
            image = Image.fromarray(
                np.asarray([[0, 64, 128, 255]], dtype=np.uint8)
            )
            image.save(path, transparency=64)

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.tolist(), [[0, 255, 128, 255]])
            self.assertEqual(metadata["transparency_composited_onto"], "white")
            self.assertEqual(metadata["source_mode"], "L")

    def test_rgb_trns_is_composited_to_white(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "rgb-trns.png"
            image = Image.new("RGB", (3, 1))
            image.putdata([(0, 0, 0), (255, 0, 0), (255, 255, 255)])
            image.save(path, transparency=(0, 0, 0))

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.tolist(), [[255, 76, 255]])
            self.assertEqual(metadata["transparency_composited_onto"], "white")
            self.assertEqual(metadata["source_mode"], "RGB")

    def test_transparent_dark_key_cannot_bleed_during_lanczos_resize(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "dark-key.png"
            pixels = np.zeros((96, 96, 3), dtype=np.uint8)
            pixels[36:60, 36:60] = 255
            Image.fromarray(pixels, mode="RGB").save(
                path, transparency=(0, 0, 0)
            )

            gray, metadata = analyze_scans.read_gray(path, max_dimension=12)

            self.assertEqual(gray.shape, (12, 12))
            self.assertTrue(np.all(gray == 255))
            self.assertEqual(metadata["transparency_composited_onto"], "white")

    def test_16_bit_grayscale_trns_uses_16_bit_sample_range(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "grayscale16-trns.png"
            image = Image.fromarray(
                np.asarray([[0, 257, 1000, 65535]], dtype=np.uint16)
            )
            image.save(path, transparency=1000)

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.tolist(), [[0, 1, 255, 255]])
            self.assertEqual(metadata["transparency_composited_onto"], "white")
            self.assertEqual(metadata["source_mode"], "I;16")
            self.assertEqual(metadata["source_bit_depth"], 16)

    def test_16_bit_grayscale_never_infers_range_from_page_maximum(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            paths = [root / "max255.png", root / "max256.png"]
            Image.fromarray(np.asarray([[0, 255]], dtype=np.uint16)).save(paths[0])
            Image.fromarray(np.asarray([[0, 256]], dtype=np.uint16)).save(paths[1])

            gray_255, metadata_255 = analyze_scans.read_gray(paths[0])
            gray_256, metadata_256 = analyze_scans.read_gray(paths[1])

            self.assertEqual(gray_255.tolist(), [[0, 1]])
            self.assertEqual(gray_256.tolist(), [[0, 1]])
            self.assertEqual(metadata_255["source_bit_depth"], 16)
            self.assertEqual(metadata_256["source_bit_depth"], 16)

    def test_bilevel_png_and_tiff_normalize_samples_to_full_grayscale(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            expected = [[0, 255, 0, 255]]
            bilevel = Image.fromarray(
                np.asarray([[0, 255, 0, 255]], dtype=np.uint8)
            ).convert("1", dither=Image.Dither.NONE)

            for name in ("bilevel.png", "bilevel.tiff"):
                with self.subTest(name=name):
                    path = root / name
                    bilevel.save(path)

                    gray, metadata = analyze_scans.read_gray(path)

                    self.assertEqual(gray.dtype, np.uint8)
                    self.assertEqual(gray.tolist(), expected)
                    self.assertEqual(metadata["source_mode"], "1")
                    self.assertEqual(metadata["source_bit_depth"], 1)

    def test_large_bilevel_thin_staff_and_text_survive_lanczos_resize(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            pixels = np.full((1600, 2800), 255, dtype=np.uint8)
            pixels[200:217:4, 200:2600] = 0
            pixels[600:1001, 400] = 0
            pixels[600:1001, 600] = 0
            pixels[800, 400:601] = 0
            pixels[600, 800:1001] = 0
            pixels[600:1001, 900] = 0
            pixels[1000, 800:1001] = 0
            source = Image.fromarray(pixels, mode="L")

            bilevel = source.convert("1", dither=Image.Dither.NONE)
            bilevel_path = root / "thin-content.png"
            bilevel.save(bilevel_path)

            white_is_zero_path = root / "thin-content-white-is-zero.tiff"
            tiffinfo = TiffImagePlugin.ImageFileDirectory_v2()
            tiffinfo[262] = 0
            bilevel.save(
                white_is_zero_path, tiffinfo=tiffinfo, compression="raw"
            )

            palette_path = root / "thin-content-palette.png"
            source.convert("P", palette=Image.Palette.ADAPTIVE, colors=2).save(
                palette_path
            )

            bilevel_gray, bilevel_metadata = analyze_scans.read_gray(
                bilevel_path, max_dimension=1400
            )
            white_is_zero_gray, white_is_zero_metadata = analyze_scans.read_gray(
                white_is_zero_path, max_dimension=1400
            )
            palette_gray, palette_metadata = analyze_scans.read_gray(
                palette_path, max_dimension=1400
            )

            self.assertEqual(bilevel_gray.shape, (800, 1400))
            self.assertTrue(np.array_equal(bilevel_gray, white_is_zero_gray))
            self.assertTrue(np.array_equal(bilevel_gray, palette_gray))
            self.assertLess(int(bilevel_gray[95:115, 100:1300].min()), 200)
            self.assertLess(int(bilevel_gray[295:505, 195:505].min()), 200)
            self.assertEqual(bilevel_metadata["source_mode"], "1")
            self.assertEqual(
                white_is_zero_metadata["source_photometric_interpretation"],
                "WhiteIsZero",
            )
            self.assertEqual(palette_metadata["source_mode"], "P")

    def test_white_is_zero_tiff_uses_pillows_actual_decoded_sample_semantics(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            cases = [
                (
                    "white-is-zero-1.tiff",
                    Image.fromarray(
                        np.asarray([[255, 0]], dtype=np.uint8)
                    ).convert("1", dither=Image.Dither.NONE),
                    [255, 0],
                    [[255, 0]],
                    1,
                ),
                (
                    "white-is-zero-8.tiff",
                    Image.fromarray(
                        np.asarray([[255, 0]], dtype=np.uint8)
                    ),
                    [255, 0],
                    [[255, 0]],
                    8,
                ),
                (
                    "white-is-zero-16.tiff",
                    Image.fromarray(
                        # These are encoded WhiteIsZero samples: zero is white
                        # and the maximum sample is black.
                        np.asarray([[0, 65535]], dtype=np.uint16)
                    ),
                    [0, 65535],
                    [[255, 0]],
                    16,
                ),
            ]

            for name, image, pillow_samples, expected, depth in cases:
                with self.subTest(name=name):
                    path = root / name
                    tiffinfo = TiffImagePlugin.ImageFileDirectory_v2()
                    tiffinfo[262] = 0
                    image.save(path, tiffinfo=tiffinfo, compression="raw")

                    with Image.open(io.BytesIO(path.read_bytes())) as decoded:
                        self.assertEqual(decoded.tag_v2.get(262), 0)
                        bits = decoded.tag_v2.get(258, 1)
                        if isinstance(bits, tuple):
                            self.assertEqual(set(bits), {depth})
                        else:
                            self.assertEqual(int(bits), depth)
                        self.assertEqual(list(decoded.getdata()), pillow_samples)

                    gray, metadata = analyze_scans.read_gray(path)

                    self.assertEqual(gray.tolist(), expected)
                    self.assertEqual(metadata["source_bit_depth"], depth)
                    self.assertEqual(
                        metadata["source_photometric_interpretation"],
                        "WhiteIsZero",
                    )

    def test_white_is_zero_changes_reported_metrics(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            pixels = np.zeros((120, 160), dtype=np.uint16)
            pixels[40:80, 60:100] = 65535
            tiffinfo = TiffImagePlugin.ImageFileDirectory_v2()
            tiffinfo[262] = 0
            Image.fromarray(pixels).save(
                root / "white-paper.tiff", tiffinfo=tiffinfo, compression="raw"
            )
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))
            page = report["pages"][0]

            self.assertEqual(page["median"], 255.0)
            self.assertEqual(page["p99"], 255.0)
            self.assertEqual(
                page["source_photometric_interpretation"], "WhiteIsZero"
            )

    def test_big_endian_uint16_white_is_zero_is_inverted_exactly_once(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "big-endian-white-is-zero.tiff"
            samples = np.asarray(
                [[0, 1, 256, 32768, 65535]], dtype=np.uint16
            )
            tifffile.imwrite(
                path,
                samples,
                byteorder=">",
                photometric="miniswhite",
                metadata=None,
            )

            self.assertEqual(path.read_bytes()[:2], b"MM")
            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.tolist(), [[255, 255, 254, 127, 0]])
            self.assertEqual(metadata["source_bit_depth"], 16)
            self.assertEqual(
                metadata["source_photometric_interpretation"], "WhiteIsZero"
            )

    def test_lzw_deflate_packbits_uint16_tiffs_decode_both_photometrics(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            samples = np.asarray(
                [[0, 1, 257, 32768, 65535], [65535, 32768, 257, 1, 0]],
                dtype=np.uint16,
            )
            compressions = ("lzw", "deflate", "packbits")
            photometrics = {
                "minisblack": (
                    "BlackIsZero",
                    [[0, 0, 1, 128, 255], [255, 128, 1, 0, 0]],
                ),
                "miniswhite": (
                    "WhiteIsZero",
                    [[255, 255, 254, 127, 0], [0, 127, 254, 255, 255]],
                ),
            }

            for compression in compressions:
                for photometric, (photometric_name, expected) in photometrics.items():
                    with self.subTest(
                        compression=compression, photometric=photometric
                    ):
                        path = root / f"{compression}-{photometric}.tiff"
                        tifffile.imwrite(
                            path,
                            samples,
                            compression=compression,
                            photometric=photometric,
                            metadata=None,
                        )

                        gray, metadata = analyze_scans.read_gray(path)

                        self.assertEqual(gray.tolist(), expected)
                        self.assertEqual(metadata["source_bit_depth"], 16)
                        self.assertEqual(
                            metadata["source_photometric_interpretation"],
                            photometric_name,
                        )

    def test_uint16_fallback_rejects_non_grayscale_photometrics_for_review(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            pixels = np.asarray([[0, 65535]], dtype=np.uint16)
            cases = {
                "palette.tiff": 3,
                "mask.tiff": 4,
                "other.tiff": 32803,
            }
            for name, photometric in cases.items():
                tiffinfo = TiffImagePlugin.ImageFileDirectory_v2()
                tiffinfo[262] = photometric
                Image.fromarray(pixels).save(
                    root / name, tiffinfo=tiffinfo, compression="raw"
                )

            for name in cases:
                with self.subTest(name=name):
                    with self.assertRaisesRegex(
                        ValueError,
                        "fallback metadata disagrees with the validated",
                    ):
                        analyze_scans.read_gray(root / name)

            output = root / "report.json"
            self.assertEqual(self.run_main(root, output), 2)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(report["page_count"], 0)
            self.assertEqual(report["failed_decode_count"], 3)
            self.assertEqual(
                report["review_lists"]["mandatory_visual_review"],
                ["mask.tiff", "other.tiff", "palette.tiff"],
            )
            self.assertTrue(
                all(
                    "fallback metadata disagrees with the validated" in item["error"]
                    for item in report["decode_failures"]
                )
            )

    def test_standard_rgb_tiff_photometric_name_is_reported(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "rgb8.tiff"
            Image.fromarray(
                np.asarray([[[255, 255, 255], [0, 0, 0]]], dtype=np.uint8)
            ).save(path, compression="raw")

            with Image.open(path) as decoded:
                self.assertEqual(decoded.tag_v2.get(262), 2)
                self.assertEqual(decoded.tag_v2.get(258), (8, 8, 8))
                self.assertEqual(list(decoded.getdata()), [(255, 255, 255), (0, 0, 0)])

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.tolist(), [[255, 0]])
            self.assertEqual(metadata["source_photometric_interpretation"], "RGB")

    def test_white_is_zero_unsupported_depth_fails_closed(self) -> None:
        with self.assertRaisesRegex(
            ValueError, "unsupported TIFF sample matrix"
        ):
            analyze_scans.validate_sample_encoding(
                "I;16",
                12,
                "unsigned_integer",
                True,
                "TIFF",
                0,
                1,
            )

    def test_tiff_supported_matrix_is_explicit_and_fail_closed(self) -> None:
        accepted = [
            ("1", 1, 0, 1),
            ("L", 8, 1, 1),
            ("I;16", 16, 0, 1),
            ("RGB", 8, 2, 3),
        ]
        for mode, depth, photometric, samples in accepted:
            with self.subTest(mode=mode, depth=depth):
                analyze_scans.validate_sample_encoding(
                    mode,
                    depth,
                    "unsigned_integer",
                    samples == 1,
                    "TIFF",
                    photometric,
                    samples,
                )

        rejected = [
            ("P", 8, 3, 1),
            ("CMYK", 8, 5, 4),
            ("YCbCr", 8, 6, 3),
            ("RGB", 9, 2, 3),
            ("RGB", 12, 2, 3),
            ("RGBA", 8, 2, 4),
            ("L", 8, None, 1),
        ]
        for mode, depth, photometric, samples in rejected:
            with self.subTest(
                mode=mode,
                depth=depth,
                photometric=photometric,
                samples=samples,
            ):
                with self.assertRaisesRegex(
                    ValueError, "unsupported TIFF sample matrix"
                ):
                    analyze_scans.validate_sample_encoding(
                        mode,
                        depth,
                        "unsigned_integer",
                        samples == 1,
                        "TIFF",
                        photometric,
                        samples,
                    )

    def test_bilevel_pages_receive_full_diagnostics_and_visual_review(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            pixels = np.zeros((120, 160), dtype=np.uint8)
            pixels[:, ::2] = 255
            bilevel = Image.fromarray(pixels).convert("1", dither=Image.Dither.NONE)
            bilevel.save(root / "page1.png")
            bilevel.save(root / "page2.tiff")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(report["schema_version"], 14)
            self.assertEqual(report["page_count"], 2)
            self.assertEqual(report["failed_decode_count"], 0)
            self.assertEqual(
                [item["status"] for item in report["candidate_sequence"]],
                ["decoded", "decoded"],
            )
            self.assertEqual(
                [page["source_bit_depth"] for page in report["pages"]], [1, 1]
            )
            for page in report["pages"]:
                self.assertEqual(page["source_mode"], "1")
                self.assertEqual(page["p01"], 0.0)
                self.assertEqual(page["p99"], 255.0)
                self.assertIn("horizontal_skew", page)
                self.assertIn("vertical_convergence", page)
            self.assertEqual(
                report["review_lists"]["representative"]["combined"],
                ["page1.png", "page2.tiff"],
            )
            self.assertEqual(
                report["review_lists"]["mandatory_visual_review"],
                ["page1.png", "page2.tiff"],
            )
            review_entries = {
                item["file"]: item["reasons"]
                for item in report["review_lists"]["mandatory_visual_review_entries"]
            }
            self.assertEqual(set(review_entries), {"page1.png", "page2.tiff"})

    def test_12_bit_grayscale_tiff_metadata_is_reported_as_unsupported(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            path = root / "grayscale12.tiff"
            output = root / "report.json"
            Image.fromarray(np.asarray([[0, 4095]], dtype=np.uint16)).save(path)

            data = bytearray(path.read_bytes())
            byte_order = "<" if data[:2] == b"II" else ">"
            ifd_offset = struct.unpack_from(f"{byte_order}I", data, 4)[0]
            entry_count = struct.unpack_from(f"{byte_order}H", data, ifd_offset)[0]
            for index in range(entry_count):
                entry_offset = ifd_offset + 2 + index * 12
                tag, value_type, value_count = struct.unpack_from(
                    f"{byte_order}HHI", data, entry_offset
                )
                if tag == 258:
                    self.assertEqual((value_type, value_count), (3, 1))
                    struct.pack_into(f"{byte_order}H", data, entry_offset + 8, 12)
                    break
            else:
                self.fail("generated TIFF has no BitsPerSample tag")
            path.write_bytes(data)

            with self.assertRaisesRegex(
                ValueError,
                "unsupported TIFF sample matrix .*BitsPerSample=12",
            ):
                analyze_scans.read_gray(path)
            self.assertEqual(self.run_main(root, output), 2)
            report = json.loads(output.read_text(encoding="utf-8"))
            self.assertIn(
                "unsupported TIFF sample matrix",
                report["decode_failures"][0]["error"],
            )
            self.assertIn(
                "grayscale12.tiff",
                report["review_lists"]["mandatory_visual_review"],
            )

    def test_16_bit_rgb_tiff_is_rejected_before_pillow_clips_it(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            path = root / "rgb16.tiff"
            output = root / "report.json"
            pixels = np.asarray([[[0, 256, 65535], [1, 2, 3]]], dtype=np.uint16)
            self.assertTrue(cv2.imwrite(str(path), pixels))

            with Image.open(path) as decoded:
                self.assertEqual(decoded.tag_v2.get(262), 2)
                self.assertEqual(decoded.tag_v2.get(258), (16, 16, 16))
            with self.assertRaisesRegex(ValueError, "unsupported TIFF sample matrix"):
                analyze_scans.read_gray(path)
            self.assertEqual(self.run_main(root, output), 2)
            report = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(report["candidate_sequence"][0]["status"], "decode_failed")
            self.assertIn(
                "unsupported TIFF sample matrix",
                report["decode_failures"][0]["error"],
            )
            self.assertIn(
                "rgb16.tiff", report["review_lists"]["mandatory_visual_review"]
            )

    def test_palette_and_cmyk_tiffs_enter_mandatory_review(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            Image.new("P", (16, 16), 0).save(root / "palette.tiff")
            Image.new("CMYK", (16, 16), (0, 0, 0, 0)).save(root / "cmyk.tiff")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 2)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(report["page_count"], 0)
            self.assertEqual(report["failed_decode_count"], 2)
            self.assertEqual(
                report["review_lists"]["mandatory_visual_review"],
                ["cmyk.tiff", "palette.tiff"],
            )
            self.assertTrue(
                all(
                    "unsupported TIFF sample matrix" in item["error"]
                    for item in report["decode_failures"]
                )
            )

    def test_signed_float_and_32_bit_tiff_modes_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            signed_path = root / "signed16.tiff"
            integer32_path = root / "integer32.tiff"
            float_path = root / "float.tiff"
            self.assertTrue(
                cv2.imwrite(
                    str(signed_path),
                    np.asarray([[-32768, 0, 32767]], dtype=np.int16),
                )
            )
            Image.fromarray(np.asarray([[0, 1, 2]], dtype=np.int32)).save(
                integer32_path
            )
            Image.fromarray(np.asarray([[0.0, 0.5, 1.0]], dtype=np.float32)).save(
                float_path
            )

            with self.assertRaisesRegex(ValueError, "unsupported signed sample mode I"):
                analyze_scans.read_gray(signed_path)
            with self.assertRaisesRegex(ValueError, "unsupported signed sample mode I"):
                analyze_scans.read_gray(integer32_path)
            with self.assertRaisesRegex(
                ValueError, "unsupported floating-point sample mode F"
            ):
                analyze_scans.read_gray(float_path)

    def test_exif_orientation_is_still_applied(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "oriented.tiff"
            exif = Image.Exif()
            exif[274] = 6
            Image.fromarray(np.arange(6, dtype=np.uint8).reshape(2, 3)).save(
                path, exif=exif
            )

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.shape, (3, 2))
            self.assertTrue(metadata["exif_orientation_applied"])

    def test_invalid_exif_orientation_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "invalid-orientation.tiff"
            exif = Image.Exif()
            exif[274] = 9
            Image.fromarray(np.arange(6, dtype=np.uint8).reshape(2, 3)).save(
                path, exif=exif
            )
            with self.assertRaisesRegex(ValueError, "expected an integer from 1 through 8"):
                analyze_scans.read_gray(path)

    def test_fractional_exif_orientation_fails_before_dimensions_are_applied(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "fractional-orientation.tiff"
            Image.fromarray(np.arange(6, dtype=np.uint8).reshape(2, 3)).save(path)

            with (
                mock.patch.object(Image.Image, "getexif", return_value={274: 6.5}),
                mock.patch.object(analyze_scans.ImageOps, "exif_transpose") as transpose,
                self.assertRaisesRegex(
                    ValueError, "expected an integer from 1 through 8"
                ),
            ):
                analyze_scans.read_gray(path)

            transpose.assert_not_called()

    def test_16_bit_tiff_orientation_uses_pillow_displayed_dimensions(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "oriented16.tiff"
            exif = Image.Exif()
            exif[274] = 6
            Image.fromarray(
                np.arange(6, dtype=np.uint16).reshape(2, 3)
            ).save(path, exif=exif)

            gray, metadata = analyze_scans.read_gray(path)

            self.assertEqual(gray.shape, (3, 2))
            self.assertTrue(metadata["exif_orientation_applied"])
            self.assertEqual(
                (
                    metadata["source_width"],
                    metadata["source_height"],
                    metadata["display_width"],
                    metadata["display_height"],
                    metadata["analysis_width"],
                    metadata["analysis_height"],
                ),
                (3, 2, 2, 3, 2, 3),
            )

    def test_large_16_bit_pages_keep_display_dimensions_in_summaries_and_outliers(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            sizes = [(1500, 1600), (1500, 1600), (1500, 1600), (1900, 1600)]
            suffixes = (".png", ".tiff", ".png", ".tiff")
            for index, ((width, height), suffix) in enumerate(
                zip(sizes, suffixes), start=1
            ):
                pixels = np.full((height, width), 60000, dtype=np.uint16)
                pixels[height // 3 : height // 3 + 12, width // 5 : width * 4 // 5] = 0
                Image.fromarray(pixels).save(root / f"page{index}{suffix}")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(
                report["dimensions"]["width"],
                {"min": 1500, "median": 1500, "max": 1900},
            )
            self.assertEqual(
                report["dimensions"]["height"],
                {"min": 1600, "median": 1600, "max": 1600},
            )
            self.assertEqual(report["dimensions"]["aspect_ratio"]["median"], 0.938)
            self.assertEqual(report["dimensions"]["aspect_ratio"]["max"], 1.188)
            self.assertEqual(
                report["review_lists"]["outliers"]["width"], ["page4.tiff"]
            )
            self.assertEqual(
                report["review_lists"]["outliers"]["aspect_ratio"], ["page4.tiff"]
            )
            for page, (width, height) in zip(report["pages"], sizes):
                self.assertEqual((page["width"], page["height"]), (width, height))
                self.assertEqual(
                    (page["display_width"], page["display_height"]), (width, height)
                )
                self.assertEqual(page["aspect_ratio"], round(width / height, 6))
                self.assertLessEqual(
                    max(page["analysis_width"], page["analysis_height"]),
                    analyze_scans.ANALYSIS_MAX_DIMENSION,
                )
                self.assertNotEqual(
                    (page["analysis_width"], page["analysis_height"]),
                    (width, height),
                )

    def test_pillow_decoder_is_restricted_to_documented_formats(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            path = Path(directory) / "page.png"
            path.write_bytes(b"candidate")
            image = mock.MagicMock()
            image.__enter__.return_value = image
            image.n_frames = 1
            image.format = "PNG"
            with mock.patch.object(
                analyze_scans.Image, "open", return_value=image
            ) as opened:
                self.assertEqual(analyze_scans.image_frame_count(path), (1, None))
            self.assertEqual(
                opened.call_args.kwargs["formats"],
                analyze_scans.PILLOW_FORMAT_ALLOWLIST,
            )

    def test_low_confidence_convergence_is_review_only_not_trusted_numeric_data(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            path = root / "page.png"
            path.write_bytes(b"candidate")
            output = root / "report.json"
            row = {
                "file": path.name,
                "order": 1,
                "width": 10,
                "height": 10,
                "aspect_ratio": 1.0,
                "median": 240.0,
                "ink_fraction": 0.01,
                "border_dark_fraction": 0.0,
                "horizontal_skew": {
                    "status": "ok",
                    "angle_degrees_clockwise": 0.0,
                    "confidence": 1.0,
                },
                "vertical_convergence": {
                    "status": "low_confidence",
                    "right_minus_left_degrees": 1.25,
                    "confidence": 0.333,
                },
            }
            with (
                mock.patch.object(
                    analyze_scans,
                    "image_frame_count",
                    return_value=(1, None),
                ),
                mock.patch.object(analyze_scans, "page_metrics", return_value=row),
            ):
                self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(
                report["geometry_summary"]["vertical_right_minus_left_degrees"]["count"],
                0,
            )
            self.assertNotIn(
                "page.png",
                report["review_lists"]["outliers"]["vertical_convergence"],
            )
            self.assertIn(
                "page.png", report["review_lists"]["outliers"]["geometry_uncertainty"]
            )
            self.assertIn(
                "page.png", report["review_lists"]["mandatory_visual_review"]
            )

    def test_zero_length_confidence_cannot_enter_trusted_convergence_summaries(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            path = root / "page.png"
            path.write_bytes(b"candidate")
            output = root / "report.json"
            row = {
                "file": path.name,
                "order": 1,
                "width": 10,
                "height": 10,
                "aspect_ratio": 1.0,
                "median": 240.0,
                "ink_fraction": 0.01,
                "border_dark_fraction": 0.0,
                "horizontal_skew": {
                    "status": "ok",
                    "angle_degrees_clockwise": 0.0,
                    "confidence": 1.0,
                },
                "vertical_convergence": {
                    "status": "ok",
                    "right_minus_left_degrees": 1.25,
                    "confidence": 1.0,
                    "length_confidence": 0.0,
                },
            }
            with (
                mock.patch.object(
                    analyze_scans, "image_frame_count", return_value=(1, None)
                ),
                mock.patch.object(analyze_scans, "page_metrics", return_value=row),
            ):
                self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))

            self.assertEqual(
                report["geometry_summary"]["vertical_right_minus_left_degrees"]["count"],
                0,
            )
            self.assertNotIn(
                "page.png",
                report["review_lists"]["outliers"]["vertical_convergence"],
            )
            self.assertIn(
                "page.png", report["review_lists"]["outliers"]["geometry_uncertainty"]
            )

    def test_absolute_quality_summary_warns_for_uniformly_bad_batch(self) -> None:
        rows = [
            {"median": 150.0, "ink_fraction": 0.5, "border_dark_fraction": 0.4},
            {"median": 155.0, "ink_fraction": 0.52, "border_dark_fraction": 0.42},
        ]
        brightness = analyze_scans.absolute_quality_summary(
            rows, "median", "brightness"
        )
        ink = analyze_scans.absolute_quality_summary(
            rows, "ink_fraction", "ink_fraction"
        )
        border = analyze_scans.absolute_quality_summary(
            rows, "border_dark_fraction", "border_contamination"
        )

        self.assertEqual(brightness["status"], "warning")
        self.assertEqual(ink["status"], "warning")
        self.assertEqual(border["status"], "warning")
        self.assertEqual(brightness["median"], 152.5)

    def test_absolute_quality_summary_uses_unrounded_boundary_ink_median(
        self,
    ) -> None:
        summary = analyze_scans.absolute_quality_summary(
            [{"ink_fraction": 0.0006}], "ink_fraction", "ink_fraction"
        )

        self.assertEqual(summary["median"], 0.001)
        self.assertEqual(summary["status"], "warning")
        self.assertTrue(
            any("batch median is below" in warning for warning in summary["warnings"])
        )

    def test_dimension_and_aspect_outliers_are_mandatory_review(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            output = root / "report.json"
            paths = [root / f"page{index}.png" for index in range(1, 5)]
            for path in paths:
                path.write_bytes(b"candidate")
            sizes = [(100, 200), (100, 200), (100, 200), (180, 200)]

            def metrics(path: Path, order: int, data: bytes) -> dict[str, object]:
                width, height = sizes[order - 1]
                return {
                    "file": path.name,
                    "order": order,
                    "format": "png",
                    "width": width,
                    "height": height,
                    "aspect_ratio": width / height,
                    "median": 240.0,
                    "ink_fraction": 0.01,
                    "border_dark_fraction": 0.0,
                    "horizontal_skew": {
                        "status": "ok",
                        "angle_degrees_clockwise": 0.0,
                        "confidence": 1.0,
                    },
                    "vertical_convergence": {
                        "status": "ok",
                        "right_minus_left_degrees": 0.0,
                        "confidence": 1.0,
                        "length_confidence": 1.0,
                    },
                }

            with (
                mock.patch.object(
                    analyze_scans, "image_frame_count", return_value=(1, None)
                ),
                mock.patch.object(analyze_scans, "page_metrics", side_effect=metrics),
            ):
                self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(report["review_lists"]["outliers"]["width"], ["page4.png"])
            self.assertEqual(
                report["review_lists"]["outliers"]["aspect_ratio"], ["page4.png"]
            )
            self.assertIn(
                "page4.png", report["review_lists"]["mandatory_visual_review"]
            )

    def test_non_finite_values_are_excluded_from_summaries(self) -> None:
        summary = analyze_scans.numeric_summary([1.0, float("nan"), float("inf")])
        self.assertEqual(summary, {"count": 1, "min": 1.0, "median": 1.0, "max": 1.0})

    def test_alias_inventory_and_unsupported_files_are_explicit(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            write_image(root / "a.jpg")
            write_image(root / "b.jpeg")
            write_image(root / "c.tif")
            write_image(root / "d.tiff")
            write_image(root / "ignored.bmp")
            output = root / "report.json"

            self.assertEqual(self.run_main(root, output), 0)
            report = json.loads(output.read_text(encoding="utf-8"))
            inventory = {
                item["format"]: item["candidate_count"]
                for item in report["format_inventory"]
            }

            self.assertEqual(inventory, {"jpeg": 2, "tiff": 2})
            self.assertEqual(
                [item["file"] for item in report["unsupported_files"]],
                ["ignored.bmp"],
            )
            self.assertIn("filename extension", report["format_detection"])
            self.assertIn("Pillow inspects headers", report["format_detection"])
            self.assertIn("allowlist", report["format_detection"])

    def test_repository_72_page_batch_has_plausible_real_measurements(self) -> None:
        batch = repository_fixture_root() / "input" / "out"
        pages = sorted(
            (
                path
                for path in batch.iterdir()
                if path.is_file()
                and path.suffix.lower() in analyze_scans.SUPPORTED_EXTENSIONS
            ),
            key=analyze_scans.natural_key,
        ) if batch.is_dir() else []
        if len(pages) != 72:
            self.skipTest("repository 72-page input batch is unavailable")

        rows = [
            analyze_scans.page_metrics(path, index)
            for index, path in enumerate(pages, start=1)
        ]
        ink = np.asarray([row["ink_fraction"] for row in rows], dtype=float)
        border = np.asarray(
            [row["border_dark_fraction"] for row in rows], dtype=float
        )
        trusted = [
            row
            for row in rows
            if row["vertical_convergence"]["status"] == "ok"
            and row["vertical_convergence"]["confidence"] >= 0.5
        ]

        self.assertGreater(float(np.median(ink)), 0.005)
        self.assertLess(float(np.median(ink)), 0.40)
        self.assertLess(float(np.mean(ink >= 0.98)), 0.05)
        self.assertLess(float(np.median(border)), 0.30)
        self.assertLess(float(np.mean(border >= 0.98)), 0.05)
        self.assertGreater(len(np.unique(np.round(ink, 4))), 10)
        self.assertGreater(len(trusted), 0)

    def test_existing_source_hardlink_output_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            source = root / "page1.png"
            write_image(source)
            output = root / "report.json"
            try:
                os.link(source, output)
            except OSError as error:
                self.skipTest(f"hard links unavailable: {error}")

            with self.assertRaises(ValueError):
                analyze_scans.validate_output_path(output.resolve(), [source])

    def test_output_parent_must_exist(self) -> None:
        with tempfile.TemporaryDirectory(dir=SKILL_ROOT) as directory:
            root = Path(directory)
            with self.assertRaises(ValueError):
                analyze_scans.validate_output_path(
                    root / "missing" / "report.json", []
                )


if __name__ == "__main__":
    unittest.main()
