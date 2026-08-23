from __future__ import annotations

import copy
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path
from typing import Callable
from unittest.mock import patch

import cv2
import numpy as np
from PIL import Image


SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))
import validate_book  # noqa: E402


def repository_fixture_root() -> Path:
    configured = os.environ.get("SCAN_RESTORATION_FIXTURE_ROOT")
    if configured:
        return Path(configured).expanduser().resolve()
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "mise.toml").is_file() and (candidate / "apm.yml").is_file():
            return candidate
    return Path(__file__).resolve().parent


class ValidateBookTests(unittest.TestCase):
    def setUp(self) -> None:
        validate_book.ACTIVE_COMPONENT_BUDGETS.update(
            {
                key: validate_book.SAFETY_BUDGET_DEFAULTS[key]
                for key in validate_book.ACTIVE_COMPONENT_BUDGETS
            }
        )
        self.temporary = tempfile.TemporaryDirectory(dir=Path(__file__).parent)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"
        self.output = self.root / "output"
        self.source.mkdir()
        self.output.mkdir()
        self.report = self.root / "evidence.json"
        self.final_report = self.root / "final.json"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def image() -> np.ndarray:
        return np.full((300, 220), 255, np.uint8)

    def write_page(
        self, path: Path, *, ink: bool = True, image: np.ndarray | None = None
    ) -> None:
        page = self.image() if image is None else image
        if ink and image is None:
            for y in range(60, 241, 30):
                cv2.line(page, (35, y), (185, y), 0, 3)
        success, encoded = cv2.imencode(".png", page)
        self.assertTrue(success)
        encoded.tofile(path)

    def test_image_snapshots_detect_mutation_without_retaining_buffers(self) -> None:
        page = self.source / "page.png"
        page.write_bytes(b"first")
        snapshots = {
            page: {
                "size": 5,
                "sha256": validate_book.hashlib.sha256(b"first").hexdigest(),
            }
        }

        self.assertTrue(validate_book.image_snapshots_match_paths(snapshots))
        page.write_bytes(b"other")
        self.assertFalse(validate_book.image_snapshots_match_paths(snapshots))

    def run_validator(self, *extra: str) -> tuple[int, dict[str, object]]:
        self.report.unlink(missing_ok=True)
        result = validate_book.main(
            [
                str(self.source),
                str(self.output),
                "--evidence-report",
                str(self.report),
                *extra,
            ]
        )
        return result, json.loads(self.report.read_text(encoding="utf-8"))

    def terminate_windows_processes(
        self, *pid_files: Path, timeout_seconds: float = 5.0
    ) -> None:
        deadline = time.monotonic() + timeout_seconds
        for pid_file in pid_files:
            if not pid_file.is_file():
                continue
            process_id = int(pid_file.read_text(encoding="utf-8").strip())
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                self.fail("Windows process cleanup exceeded its deadline.")
            subprocess.run(
                ["taskkill.exe", "/PID", str(process_id), "/T", "/F"],
                text=True,
                capture_output=True,
                check=False,
                timeout=remaining,
            )

    def racing_open(
        self, target: Path, action: Callable[[], None]
    ) -> tuple[Callable[..., object], threading.Event]:
        original_open = Path.open
        raced = threading.Event()

        class RacingStream:
            def __init__(self, stream: object) -> None:
                self.stream = stream

            def fileno(self) -> int:
                return self.stream.fileno()

            def read(self, size: int = -1) -> bytes:
                if not raced.is_set():
                    worker = threading.Thread(target=action)
                    worker.start()
                    worker.join()
                    raced.set()
                return self.stream.read(size)

            def close(self) -> None:
                self.stream.close()

        def open_with_race(path: Path, *args: object, **kwargs: object) -> object:
            stream = original_open(path, *args, **kwargs)
            if Path(path) == target.resolve() and args and args[0] == "rb":
                return RacingStream(stream)
            return stream

        return open_with_race, raced

    def approve(self, report: dict[str, object]) -> tuple[int, dict[str, object]]:
        approval = self.root / "approval.json"
        approval_data = {
            "evidence_hash": report["evidence_hash"],
            "reviewer": "Test Reviewer",
            "note": "Inspected every listed page for every listed reason.",
            "pages": [
                {
                    "file": item["file"],
                    "required_reasons": item["reasons"],
                    "acknowledged_reasons": item["reasons"],
                }
                for item in report["evidence"]["visual_review_pages"]
            ],
        }
        approval.write_text(json.dumps(approval_data), encoding="utf-8")
        self.final_report.unlink(missing_ok=True)
        result = validate_book.main(
            [
                str(self.source),
                str(self.output),
                "--evidence-report",
                str(self.report),
                "--approval",
                str(approval),
                "--final-report",
                str(self.final_report),
            ]
        )
        return result, json.loads(self.final_report.read_text(encoding="utf-8"))

    def test_numeric_pairing_and_complete_recorded_approval(self) -> None:
        for name in ("page10.png", "page2.png", "page1.png"):
            self.write_page(self.source / name)
            self.write_page(self.output / name)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertTrue(report["mechanical_pass"])
        self.assertFalse(report["passed"])
        self.assertEqual(
            ["page1.png", "page2.png", "page10.png"],
            report["evidence"]["pairing"]["input_order"],
        )

        result, report = self.approve(report)
        self.assertEqual(0, result)
        self.assertTrue(report["passed"])
        self.assertEqual([], report["approval_errors"])
        self.assertTrue(self.report.exists())
        self.assertNotEqual(self.report, self.final_report)

    def test_bare_or_incomplete_approval_is_rejected(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "bad.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "",
                    "note": "",
                    "pages": [],
                }
            ),
            encoding="utf-8",
        )
        self.final_report.unlink(missing_ok=True)
        result = validate_book.main(
            [
                str(self.source), str(self.output),
                "--evidence-report", str(self.report),
                "--approval", str(approval),
                "--final-report", str(self.final_report),
            ]
        )
        report = json.loads(self.final_report.read_text(encoding="utf-8"))
        self.assertEqual(2, result)
        self.assertFalse(report["visual_review_approved"])
        self.assertIn("reviewer identity is required", report["approval_errors"])
        self.assertIn("nonempty review note is required", report["approval_errors"])
        self.assertTrue(
            any("exactly acknowledge" in error for error in report["approval_errors"])
        )

    def test_duplicate_and_mismatched_identities_never_pair_positionally(self) -> None:
        for name in ("source-1-a.png", "source-1-b.png", "source-2.png"):
            self.write_page(self.source / name)
        for name in ("output-1.png", "output-3.png", "output-3-copy.png"):
            self.write_page(self.output / name)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertFalse(report["evidence"]["pairing"]["positional_pairing"])
        self.assertTrue(report["evidence"]["pairing"]["issues"])
        self.assertTrue(
            any("pairing identity failure" in failure for failure in report["evidence"]["failures"])
        )
        self.assertIn(
            "__batch_pairing__",
            [item["file"] for item in report["evidence"]["visual_review_pages"]],
        )

    def test_nonnumeric_pairing_requires_complete_manifest(self) -> None:
        for name in ("alpha.png", "beta.png"):
            self.write_page(self.source / name)
            self.write_page(self.output / name)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertFalse(report["evidence"]["pairing"]["positional_pairing"])
        self.assertTrue(any("pairing manifest" in issue for issue in report["evidence"]["pairing"]["issues"]))

        manifest = self.root / "pairs.json"
        manifest.write_text(
            json.dumps(
                {
                    "pairs": [
                        {"input": "alpha.png", "output": "alpha.png"},
                        {"input": "beta.png", "output": "beta.png"},
                    ]
                }
            ),
            encoding="utf-8",
        )
        _, report = self.run_validator("--pairing-manifest", str(manifest))
        self.assertEqual("exact explicit pairing manifest", report["evidence"]["pairing"]["strategy"])
        self.assertEqual(2, len(report["evidence"]["pairing"]["map"]))

    def test_sparse_nonblank_complete_erasure_fails(self) -> None:
        sparse = self.image()
        sparse[150, 90:130] = 0
        self.write_page(self.source / "page1.png", image=sparse)
        self.write_page(self.output / "page1.png", ink=False)
        partial_source = self.image()
        partial_source[140:142, 80:180] = 0
        partial_output = self.image()
        partial_output[140, 80:180] = 0
        self.write_page(self.source / "page2.png", image=partial_source)
        self.write_page(self.output / "page2.png", image=partial_output)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertFalse(report["evidence"]["pairs"][0]["likely_blank"])
        self.assertTrue(
            any("complete source ink erasure" in failure for failure in report["evidence"]["failures"])
        )
        self.assertTrue(
            any("material source ink loss: page2.png" in failure for failure in report["evidence"]["failures"])
        )

    def test_small_dirt_cleaned_from_likely_blank_is_review_only(self) -> None:
        dirty_blank = self.image()
        dirty_blank[120:124, 90:94] = 0
        dirty_blank[180:184, 150:154] = 0
        self.write_page(self.source / "page1.png", image=dirty_blank)
        self.write_page(self.output / "page1.png", ink=False)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertTrue(pair["likely_blank"])
        self.assertAlmostEqual(32 / dirty_blank.size, pair["source_raw_ink_fraction"])
        self.assertEqual(32, pair["source_raw_ink_pixel_count"])
        self.assertEqual(0, pair["source_fine_component_count"])
        self.assertTrue(report["mechanical_pass"])
        self.assertFalse(
            any(
                "structural mismatch" in failure or "source ink erasure" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        self.assertIn(
            "page1.png",
            report["evidence"]["visual_review_categories"]["likely_blank"],
        )

    def test_1089_isolated_two_by_two_marks_cannot_suppress_fine_loss(self) -> None:
        source = np.full((1000, 1000), 255, np.uint8)
        for y in range(5, 995, 30):
            for x in range(5, 995, 30):
                source[y:y + 2, x:x + 2] = 0
        self.assertEqual(1089 * 4, int(np.count_nonzero(source == 0)))
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(
            self.output / "page1.png",
            image=np.full_like(source, 255),
        )

        result, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        damage = pair["mapped_fine_component_damage"]
        self.assertEqual(2, result)
        self.assertAlmostEqual(0.004356, pair["source_raw_ink_fraction"])
        self.assertEqual(4356, pair["source_raw_ink_pixel_count"])
        self.assertEqual(1089, pair["source_fine_component_count"])
        self.assertEqual(4356, pair["source_fine_component_ink_pixel_count"])
        self.assertAlmostEqual(
            0.004356,
            pair["source_fine_component_ink_fraction"],
        )
        self.assertFalse(pair["likely_blank"])
        self.assertTrue(damage["failed"])
        self.assertFalse(damage["blank_page_suppression_applied"])
        self.assertEqual(1089, damage["missing_component_count"])
        self.assertTrue(
            any(
                "mapped fine notation/punctuation loss: page1.png "
                "(1089 isolated component(s))" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_proportional_white_padding_does_not_look_like_ink_loss(self) -> None:
        source = self.image()
        cv2.rectangle(source, (55, 75), (165, 225), 0, 3)
        padded = np.full((450, 330), 255, np.uint8)
        padded[75:375, 55:275] = source
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=padded)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertGreaterEqual(pair["ink_retention_ratio"], 0.99)
        self.assertFalse(
            any("ink loss" in failure for failure in report["evidence"]["failures"])
        )

    def test_deleting_exactly_half_the_content_reports_half_retention_and_fails(self) -> None:
        source = self.image()
        cv2.rectangle(source, (25, 75), (85, 225), 0, -1)
        cv2.rectangle(source, (135, 75), (195, 225), 0, -1)
        output = source.copy()
        output[:, 110:] = 255
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertLess(
            pair["geometry"]["foreground_registration"]["output_content_bounds"]["width"],
            pair["geometry"]["foreground_registration"]["source_content_bounds"]["width"],
        )
        self.assertAlmostEqual(0.5, pair["ink_retention_ratio"], delta=0.01)
        self.assertEqual(
            "verified identical-canvas transform",
            pair["ink_scale_registration"]["method"],
        )
        self.assertTrue(
            any("material source ink loss" in item
                for item in report["evidence"]["failures"])
        )

    def test_mapped_fine_dot_deletion_fails_but_antialiasing_does_not(self) -> None:
        source = np.full((600, 400), 255, np.uint8)
        for y in range(100, 501, 50):
            cv2.line(source, (50, y), (350, y), 0, 3)
        source[275:277, 200:202] = 0
        self.write_page(self.source / "page1.png", image=source)

        deleted = source.copy()
        deleted[272:280, 196:206] = 255
        self.write_page(self.output / "page1.png", image=deleted)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        pair = report["evidence"]["pairs"][0]
        self.assertTrue(pair["mapped_fine_component_damage"]["failed"])
        self.assertTrue(
            any(
                "mapped fine notation/punctuation loss" in failure
                for failure in report["evidence"]["failures"]
            )
        )

        antialiased = cv2.GaussianBlur(source, (3, 3), 0.55)
        self.write_page(self.output / "page1.png", image=antialiased)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertFalse(pair["mapped_fine_component_damage"]["failed"])

    def test_proportional_scaling_plus_padding_reports_full_retention(self) -> None:
        source = self.image()
        cv2.rectangle(source, (35, 60), (90, 230), 0, -1)
        cv2.rectangle(source, (130, 90), (185, 200), 0, -1)
        scaled = cv2.resize(source, (330, 450), interpolation=cv2.INTER_NEAREST)
        output = np.full((560, 460), 255, np.uint8)
        output[55:505, 65:395] = scaled
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertAlmostEqual(1.0, pair["ink_retention_ratio"], delta=0.02)
        self.assertFalse(
            any("ink loss" in failure for failure in report["evidence"]["failures"])
        )

    def test_two_x_upscale_cannot_mask_seventy_five_percent_ink_loss(self) -> None:
        source = self.image()
        cv2.rectangle(source, (30, 45), (190, 255), 0, 2)
        for y in range(60, 241, 12):
            cv2.line(source, (40, y), (180, y), 0, 5)
        output = cv2.resize(source, (440, 600), interpolation=cv2.INTER_NEAREST)
        for y in range(120, 481, 24):
            if ((y - 120) // 24) % 4:
                cv2.line(output, (80, y), (360, y), 255, 11)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertLess(pair["ink_retention_ratio"], 0.40)
        self.assertTrue(
            any("material source ink loss" in item
                for item in report["evidence"]["failures"])
        )
        self.assertNotIn("maximum of absolute", pair["ink_comparison_method"])

    def test_single_ambiguous_component_cannot_mask_86_8_percent_ink_loss(self) -> None:
        source = np.full((500, 500), 255, np.uint8)
        source[20:225, 20:225] = 0
        source[360:440, 360:440] = 0
        source_pixels = int(np.count_nonzero(source < 128))
        output = np.full((1000, 1000), 255, np.uint8)
        output[720:880, 720:880] = 0
        self.assertAlmostEqual(
            0.132,
            80 * 80 / source_pixels,
            delta=0.001,
        )
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)

        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertFalse(pair["ink_scale_registration"]["verified"])
        self.assertLess(pair["ink_retention_ratio"], 0.20)
        self.assertTrue(
            any(
                "material source ink loss" in item
                for item in report["evidence"]["failures"]
            )
        )
        self.assertTrue(
            any(
                "ink scale/registration is unverified" in reason
                for item in report["evidence"]["visual_review_pages"]
                if item["file"] == "page1.png"
                for reason in item["reasons"]
            )
        )

    def test_geometry_absolute_threshold_and_unmeasurable_comparison_review(self) -> None:
        for index in (1, 2):
            self.write_page(self.source / f"page{index}.png")
            self.write_page(self.output / f"page{index}.png")
        geometries = [
            {
                "horizontal": {"measurable": True, "line_count": 10, "residual_degrees": 0.55},
                "vertical_convergence_barline": {"measurable": True, "line_count": 10, "residual_degrees": 0.10},
            },
            {
                "horizontal": {"measurable": True, "line_count": 10, "residual_degrees": 0.20},
                "vertical_convergence_barline": {"measurable": True, "line_count": 10, "residual_degrees": 0.70},
            },
            {
                "horizontal": {"measurable": True, "line_count": 10, "residual_degrees": 0.60},
                "vertical_convergence_barline": {"measurable": False, "line_count": 0, "residual_degrees": None},
            },
            {
                "horizontal": {"measurable": False, "line_count": 0, "residual_degrees": None},
                "vertical_convergence_barline": {"measurable": False, "line_count": 0, "residual_degrees": None},
            },
        ]
        with patch.object(validate_book, "estimate_skew", side_effect=geometries):
            _, report = self.run_validator()
        reasons = {
            item["file"]: item["reasons"] for item in report["evidence"]["visual_review_pages"]
        }
        self.assertTrue(
            any("exceeds 0.50" in reason for reason in reasons["page1.png"])
        )
        self.assertTrue(
            any("unmeasurable" in reason for reason in reasons["page2.png"])
        )
        self.assertTrue(any("vertical convergence/barline" in reason for reason in reasons["page1.png"]))

    def test_new_scanner_strip_fails_while_unchanged_edge_content_is_review_only(self) -> None:
        dense = self.image()
        dense[:, :18] = 0
        self.write_page(self.source / "page1.png", image=dense.copy())
        self.write_page(self.output / "page1.png", image=dense.copy())
        _, report = self.run_validator()
        self.assertFalse(
            any("border contamination" in failure for failure in report["evidence"]["failures"])
        )
        self.assertTrue(report["evidence"]["pairs"][0]["border_severe_bands"])
        self.assertFalse(report["evidence"]["pairs"][0]["border_contamination_bands"])
        self.assertFalse(report["evidence"]["pairs"][0]["scanner_strip_failure_sides"])
        page1 = next(
            item
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.png"
        )
        self.assertTrue(
            any("source-consistent" in reason for reason in page1["reasons"])
        )

        clean = self.image()
        self.write_page(self.source / "page2.png", image=clean)
        self.write_page(self.output / "page2.png", image=dense.copy())
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertTrue(
            any(
                "new high-confidence black scanner strip" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        page2 = next(item for item in report["evidence"]["visual_review_pages"] if item["file"] == "page2.png")
        self.assertTrue(
            any("new source-relative broad connected edge contamination" in reason for reason in page2["reasons"])
        )

    def test_edge_component_detection_is_symmetric_for_1181_by_21_strips(self) -> None:
        size = 1181
        inset = 47
        source = np.zeros((size, size), dtype=bool)
        for side in ("top", "bottom", "left", "right"):
            output = source.copy()
            if side == "top":
                output[inset : inset + 21, :] = True
            elif side == "bottom":
                output[size - inset - 21 : size - inset, :] = True
            elif side == "left":
                output[:, inset : inset + 21] = True
            else:
                output[:, size - inset - 21 : size - inset] = True
            comparison = validate_book.compare_edge_component_analysis(
                validate_book.edge_component_analysis(source),
                validate_book.edge_component_analysis(output),
            )
            with self.subTest(side=side):
                self.assertIn(side, comparison["new_edge_contamination_sides"])
                self.assertIn(side, comparison["scanner_strip_failure_sides"])

    def test_one_pixel_border_bands_are_nonempty_and_json_finite(self) -> None:
        for shape in ((1, 37), (41, 1), (1, 1)):
            mask = np.zeros(shape, dtype=bool)
            metrics = {
                name: validate_book.analyze_band(mask[rows, columns])
                for name, (rows, columns) in validate_book.band_slices(
                    shape[0], shape[1]
                ).items()
            }
            with self.subTest(shape=shape):
                encoded = json.dumps(metrics, allow_nan=False)
                self.assertNotIn("NaN", encoded)
                for band in metrics.values():
                    self.assertTrue(
                        all(
                            np.isfinite(value)
                            for value in band.values()
                            if isinstance(value, float)
                        )
                    )

    def test_inset_low_fill_rectangular_scanner_frame_regression_and_controls(self) -> None:
        size = 1181
        inset = round(size * 0.09)
        thickness = 10
        clean = np.zeros((size, size), dtype=bool)
        framed_pixels = np.zeros((size, size), dtype=np.uint8)
        cv2.rectangle(
            framed_pixels,
            (inset, inset),
            (size - inset - 1, size - inset - 1),
            1,
            thickness,
        )
        framed = framed_pixels.astype(bool)

        analysis = validate_book.edge_component_analysis(framed)
        frames = analysis["rectangular_frames"]["candidates"]
        self.assertEqual(1, len(frames))
        self.assertLess(frames[0]["envelope_fill_fraction"], 0.60)
        for side in ("top", "bottom", "left", "right"):
            self.assertTrue(analysis[side]["side_projection_components"])
            self.assertEqual(0, analysis[side]["broad_connected_component_count"])

        introduced = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(clean), analysis
        )
        self.assertTrue(introduced["new_rectangular_frame"])
        self.assertTrue(introduced["scanner_frame_failure"])
        self.assertEqual([], introduced["scanner_strip_failure_sides"])

        preserved = validate_book.compare_edge_component_analysis(
            analysis, validate_book.edge_component_analysis(framed.copy())
        )
        self.assertFalse(preserved["new_rectangular_frame"])
        self.assertFalse(preserved["scanner_frame_failure"])
        self.assertTrue(preserved["source_consistent_rectangular_frame"])
        self.assertEqual(
            "direct",
            preserved["rectangular_frame_comparison"]["frame_comparisons"][0][
                "match_kind"
            ],
        )

        source_inset = round(size * 0.02)
        output_inset = round(size * 0.06)
        source_border_pixels = np.zeros((size, size), dtype=np.uint8)
        output_border_pixels = np.zeros((size, size), dtype=np.uint8)
        cv2.rectangle(
            source_border_pixels,
            (source_inset, source_inset),
            (size - source_inset - 1, size - source_inset - 1),
            1,
            thickness,
        )
        cv2.rectangle(
            output_border_pixels,
            (output_inset, output_inset),
            (size - output_inset - 1, size - output_inset - 1),
            1,
            thickness,
        )
        source_border = source_border_pixels.astype(bool)
        output_border = output_border_pixels.astype(bool)
        scale = (1.0 - 2.0 * output_inset / size) / (
            1.0 - 2.0 * source_inset / size
        )
        offset = output_inset / size - source_inset / size * scale
        transformed = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source_border),
            validate_book.edge_component_analysis(output_border),
            {
                "x_scale": scale,
                "y_scale": scale,
                "x_offset": offset,
                "y_offset": offset,
            },
        )
        self.assertFalse(transformed["scanner_frame_failure"])
        self.assertTrue(transformed["registration_adjusted_rectangular_frame"])

        source_page = np.full((size, size), 255, np.uint8)
        for y in range(300, 881, 80):
            cv2.line(source_page, (280, y), (900, y), 0, 4)
        output_page = source_page.copy()
        cv2.rectangle(
            output_page,
            (inset, inset),
            (size - inset - 1, size - inset - 1),
            0,
            thickness,
        )
        self.write_page(self.source / "page1.png", image=source_page)
        self.write_page(self.output / "page1.png", image=output_page)
        result, report = self.run_validator()
        self.assertEqual(2, result)
        pair = report["evidence"]["pairs"][0]
        self.assertTrue(pair["scanner_frame_failure"])
        self.assertTrue(pair["new_rectangular_frame"])
        self.assertTrue(
            any(
                "four-sided black scanner frame" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_staff_and_brace_projections_do_not_form_four_sided_frame(self) -> None:
        mask = np.zeros((1181, 1181), dtype=bool)
        for y in range(75, 126, 10):
            mask[y : y + 6, 80:1100] = True
        mask[70:1110, 90:100] = True
        analysis = validate_book.edge_component_analysis(mask)
        self.assertTrue(analysis["top"]["side_projection_components"])
        self.assertTrue(analysis["left"]["side_projection_components"])
        self.assertEqual([], analysis["rectangular_frames"]["candidates"])

    def test_dense_border_frame_search_is_bounded_and_truncated_is_review_only(self) -> None:
        size = 1400
        dense = np.zeros((size, size), dtype=bool)
        for coordinate in range(4, 164, 8):
            dense[coordinate : coordinate + 6, :] = True
            dense[size - coordinate - 6 : size - coordinate, :] = True
            dense[:, coordinate : coordinate + 6] = True
            dense[:, size - coordinate - 6 : size - coordinate] = True

        started = time.perf_counter()
        analysis = validate_book.edge_component_analysis(dense)
        elapsed = time.perf_counter() - started
        frames = analysis["rectangular_frames"]

        self.assertLess(elapsed, 5.0)
        self.assertTrue(frames["truncated"])
        self.assertLessEqual(
            frames["combination_evaluations"], frames["combination_budget"]
        )
        self.assertTrue(frames["projection_truncated_sides"])

        clean = np.zeros_like(dense)
        clean_pixels = clean.astype(np.uint8)
        cv2.rectangle(clean_pixels, (90, 90), (size - 91, size - 91), 1, 5)
        comparison = validate_book.compare_edge_component_analysis(
            analysis, validate_book.edge_component_analysis(clean_pixels.astype(bool))
        )
        self.assertTrue(comparison["frame_evidence_truncated"])
        self.assertFalse(comparison["scanner_frame_failure"])
        self.assertFalse(
            any(
                item["high_confidence_scanner_frame"]
                for item in comparison["rectangular_frame_comparison"][
                    "frame_comparisons"
                ]
            )
        )

    def test_smaller_new_strips_are_not_hidden_by_largest_source_component(self) -> None:
        size = 1000
        source = np.zeros((size, size), dtype=bool)
        source[10:40, 50:950] = True
        output = source.copy()
        output[65:80, 100:900] = True
        output[95:107, 150:850] = True

        source_analysis = validate_book.edge_component_analysis(source)
        output_analysis = validate_book.edge_component_analysis(output)
        comparison = validate_book.compare_edge_component_analysis(
            source_analysis, output_analysis
        )

        self.assertEqual(
            1, source_analysis["top"]["broad_connected_component_count"]
        )
        self.assertEqual(
            3, output_analysis["top"]["broad_connected_component_count"]
        )
        self.assertEqual(
            3, len(output_analysis["top"]["broad_connected_components"])
        )
        top_components = comparison["sides"]["top"]["component_comparisons"]
        added = [
            item
            for item in top_components
            if item["output"] is not None and item["source"] is None
        ]
        self.assertEqual(2, len(added))
        self.assertTrue(
            all(item["high_confidence_scanner_strip"] for item in added)
        )
        self.assertIn("top", comparison["new_edge_contamination_sides"])
        self.assertIn("top", comparison["scanner_strip_failure_sides"])

    def test_registration_shifted_strip_is_review_only(self) -> None:
        source = np.zeros((1000, 1000), dtype=bool)
        output = np.zeros_like(source)
        source[10:30, 100:900] = True
        output[60:80, 100:900] = True
        comparison = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source),
            validate_book.edge_component_analysis(output),
            {
                "x_scale": 1.0,
                "y_scale": 1.0,
                "x_offset": 0.0,
                "y_offset": 0.05,
            },
        )
        item = comparison["sides"]["top"]["component_comparisons"][0]
        self.assertTrue(item["uncertain_shifted_component"])
        self.assertEqual([], comparison["scanner_strip_failure_sides"])
        self.assertEqual([], comparison["new_edge_contamination_sides"])

    def test_crop_maps_line_from_fifteen_to_five_percent_but_new_strip_fails(self) -> None:
        source = np.zeros((1000, 1000), dtype=bool)
        output = np.zeros_like(source)
        source[150:170, 100:900] = True
        output[50:70, 100:900] = True
        output[95:110, 150:850] = True

        comparison = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source),
            validate_book.edge_component_analysis(output),
            {
                "x_scale": 1.0,
                "y_scale": 1.0,
                "x_offset": 0.0,
                "y_offset": -0.10,
            },
        )

        top = comparison["sides"]["top"]["component_comparisons"]
        shifted = next(item for item in top if item["output_index"] == 0)
        added = next(item for item in top if item["output_index"] == 1)
        self.assertEqual("outside_edge_zone", shifted["source_component_origin"])
        self.assertTrue(shifted["uncertain_shifted_component"])
        self.assertFalse(shifted["new_source_relative_component"])
        self.assertIsNone(added["source"])
        self.assertTrue(added["high_confidence_scanner_strip"])
        self.assertIn("top", comparison["scanner_strip_failure_sides"])

    def test_scaled_padded_edge_content_registers_outside_raw_edge_zone(self) -> None:
        source = np.zeros((1000, 1000), dtype=bool)
        preserved = np.zeros((2000, 2000), dtype=bool)
        source[80:100, 50:950] = True
        preserved[380:420, 550:1450] = True
        registration = {
            "x_scale": 0.5,
            "y_scale": 0.5,
            "x_offset": 0.25,
            "y_offset": 0.15,
            "source_canvas_width": 1000.0,
            "source_canvas_height": 1000.0,
            "output_canvas_width": 2000.0,
            "output_canvas_height": 2000.0,
        }

        comparison = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source),
            validate_book.edge_component_analysis(preserved),
            registration,
        )

        top = comparison["sides"]["top"]
        match = next(
            item
            for item in top["component_comparisons"]
            if item.get("preserved_source_edge_content")
        )
        self.assertEqual(
            "registration_adjusted_to_outside_edge_zone", match["match_kind"]
        )
        self.assertNotIn("top", comparison["potential_removed_content_sides"])
        self.assertNotIn("top", comparison["removed_content_failure_sides"])

        ambiguous = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source),
            validate_book.edge_component_analysis(preserved),
            {
                key: value
                for key, value in registration.items()
                if not key.startswith(("source_canvas_", "output_canvas_"))
            },
        )
        self.assertIn("top", ambiguous["removed_content_failure_sides"])
        self.assertFalse(any(
            item.get("preserved_source_edge_content")
            for item in ambiguous["sides"]["top"]["component_comparisons"]
        ))

        removed_registration = dict(registration, y_offset=0.05)
        removed = validate_book.compare_edge_component_analysis(
            validate_book.edge_component_analysis(source),
            validate_book.edge_component_analysis(np.zeros_like(preserved)),
            removed_registration,
        )
        removed_item = removed["sides"]["top"]["component_comparisons"][0]
        self.assertTrue(removed_item["registered_expected_in_edge_zone"])
        self.assertTrue(
            removed_item["registration_reliable_for_removal_confidence"]
        )
        self.assertTrue(removed_item["high_confidence_removed_content"])
        self.assertIn("top", removed["removed_content_failure_sides"])

    def test_pillow_limit_tracks_configured_budget_and_bomb_is_caught(self) -> None:
        image_path = self.source / "page1.png"
        image_path.write_bytes(b"not an image")

        def bomb(_: Path) -> object:
            self.assertEqual(12345, Image.MAX_IMAGE_PIXELS)
            raise Image.DecompressionBombError("too large")

        original_limit = Image.MAX_IMAGE_PIXELS
        with patch.object(validate_book.Image, "open", side_effect=bomb):
            with self.assertRaisesRegex(ValueError, "decoded pixel safety budget"):
                validate_book.probe_image_pixels(image_path, 12345)
            with self.assertRaisesRegex(ValueError, "decoded pixel safety budget"):
                validate_book.read_image(image_path, 12345)
        self.assertEqual(original_limit, Image.MAX_IMAGE_PIXELS)

    def test_legitimate_bottom_staff_and_page_number_stay_review_only(self) -> None:
        page = np.full((1181, 1181), 255, np.uint8)
        for y in range(1040, 1121, 16):
            cv2.line(page, (90, y), (1090, y), 0, 2)
        cv2.putText(page, "50", (555, 1160), cv2.FONT_HERSHEY_SIMPLEX, 0.8, 0, 2)
        self.write_page(self.source / "page1.png", image=page.copy())
        self.write_page(self.output / "page1.png", image=page.copy())
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertNotIn("bottom", pair["scanner_strip_failure_sides"])
        self.assertFalse(
            any(
                "new high-confidence black scanner strip" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        reasons = next(
            item["reasons"]
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.png"
        )
        self.assertTrue(any("edge" in reason for reason in reasons))

    def test_actual_page_050_bottom_strip_regression_when_available(self) -> None:
        repository = repository_fixture_root()
        source_path = (
            repository
            / "traditional_harmony"
            / "A concentrated course in traditional harmony_Page_050.jpg"
        )
        output_path = (
            repository
            / "traditional_harmony - Copy"
            / "output"
            / "A concentrated course in traditional harmony_Page_050.png"
        )
        if not source_path.exists() or not output_path.exists():
            self.skipTest("actual page 050 source/output example is not available")
        source_image = cv2.imread(str(source_path), cv2.IMREAD_GRAYSCALE)
        output_image = cv2.imread(str(output_path), cv2.IMREAD_GRAYSCALE)
        self.assertIsNotNone(source_image)
        self.assertIsNotNone(output_image)
        source_analysis = validate_book.edge_component_analysis(
            validate_book.scanner_dark_mask(source_image)
        )
        output_analysis = validate_book.edge_component_analysis(
            validate_book.scanner_dark_mask(output_image)
        )
        bottom = source_analysis["bottom"]["largest_broad_connected_component"]
        self.assertIsNotNone(bottom)
        self.assertGreater(bottom["length_fraction"], 0.75)
        self.assertGreater(bottom["thickness_fraction"], 0.01)
        self.assertIsNone(
            output_analysis["bottom"]["largest_broad_connected_component"]
        )

    def test_shifted_music_edge_is_review_not_mechanical_failure(self) -> None:
        source = self.image()
        for y in range(45, 256, 18):
            cv2.line(source, (12, y), (205, y), 0, 2)
        output = np.full_like(source, 255)
        output[:, :210] = source[:, 10:220]
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        self.assertFalse(any("border" in failure or "edge artifact" in failure for failure in report["evidence"]["failures"]))
        reasons = next(item["reasons"] for item in report["evidence"]["visual_review_pages"] if item["file"] == "page1.png")
        self.assertTrue(any("edge" in reason for reason in reasons))

    def test_rejects_multipage_tiff_and_unsupported_or_nested_candidates(self) -> None:
        page = self.image()
        self.assertTrue(cv2.imwritemulti(str(self.source / "page1.tiff"), [page, page]))
        self.write_page(self.output / "page1.png")
        success, encoded = cv2.imencode(".bmp", page)
        self.assertTrue(success)
        encoded.tofile(self.output / "extra.bmp")
        nested = self.output / "nested"
        nested.mkdir()
        self.write_page(nested / "page2.png")
        _, report = self.run_validator()
        self.assertTrue(any("not a single decoded frame" in failure for failure in report["evidence"]["failures"]))
        self.assertTrue(any("unsupported output image candidate" in failure for failure in report["evidence"]["failures"]))
        self.assertTrue(any("nested output image candidate" in failure for failure in report["evidence"]["failures"]))

    def test_detects_duplicated_substituted_and_rotated_pages(self) -> None:
        pages: list[np.ndarray] = []
        for index in range(4):
            page = self.image()
            cv2.putText(page, chr(ord("A") + index), (35 + index * 20, 170), cv2.FONT_HERSHEY_SIMPLEX, 3, 0, 8)
            cv2.rectangle(page, (20, 30 + index * 20), (70 + index * 30, 55 + index * 20), 0, -1)
            pages.append(page)
            self.write_page(self.source / f"page{index + 1}.png", image=page)
        self.write_page(self.output / "page1.png", image=pages[0].copy())
        self.write_page(
            self.output / "page2.png",
            image=cv2.rotate(pages[0], cv2.ROTATE_90_CLOCKWISE),
        )
        self.write_page(
            self.output / "page3.png",
            image=cv2.rotate(pages[2], cv2.ROTATE_90_CLOCKWISE),
        )
        self.write_page(
            self.output / "page4.png",
            image=cv2.rotate(pages[3], cv2.ROTATE_180),
        )
        _, report = self.run_validator()
        self.assertTrue(any("duplicated output content" in failure for failure in report["evidence"]["failures"]))
        self.assertTrue(any("probable substituted page" in failure for failure in report["evidence"]["failures"]))
        self.assertTrue(any("content orientation mismatch" in failure for failure in report["evidence"]["failures"]))
        self.assertTrue(any("180 degree rotation" in failure for failure in report["evidence"]["failures"]))

    def test_blank_signatures_compare_without_structural_failure(self) -> None:
        blank = self.image()
        self.write_page(self.source / "page1.png", image=blank)
        self.write_page(self.output / "page1.png", image=blank)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        identity = pair["content_identity"]
        self.assertTrue(pair["likely_blank"])
        self.assertEqual(0.0, pair["source_raw_ink_fraction"])
        self.assertEqual(0, pair["source_fine_component_count"])
        self.assertEqual(1.0, identity["expected_orientation_similarity"])
        self.assertFalse(
            any("structural mismatch" in failure for failure in report["evidence"]["failures"])
        )

    def test_decoded_duplicates_ignore_encoded_sha_and_similar_sources(self) -> None:
        first = self.image()
        second = self.image()
        cv2.putText(first, "SAME", (20, 160), cv2.FONT_HERSHEY_SIMPLEX, 1.5, 0, 5)
        cv2.putText(second, "OTHER", (5, 160), cv2.FONT_HERSHEY_SIMPLEX, 1.2, 0, 5)
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=first)
        success, encoded = cv2.imencode(".png", first, [cv2.IMWRITE_PNG_COMPRESSION, 9])
        self.assertTrue(success)
        encoded.tofile(self.output / "page2.png")
        _, report = self.run_validator()
        output_pages = [row for row in report["evidence"]["output_pages"] if row]
        self.assertNotEqual(output_pages[0]["sha256"], output_pages[1]["sha256"])
        self.assertTrue(
            any("decoded signatures" in failure for failure in report["evidence"]["failures"])
        )

    def test_isoluminant_color_pages_have_distinct_canonical_identity(self) -> None:
        red = np.full((300, 220, 3), 255, np.uint8)
        green = red.copy()
        red[60:240, 35:185] = (0, 0, 100)
        green[60:240, 35:185] = (0, 51, 0)
        self.assertTrue(np.array_equal(
            cv2.cvtColor(red, cv2.COLOR_BGR2GRAY),
            cv2.cvtColor(green, cv2.COLOR_BGR2GRAY),
        ))
        self.write_page(self.source / "page1.png", image=red)
        self.write_page(self.source / "page2.png", image=green)
        self.write_page(self.output / "page1.png", image=red)
        self.write_page(self.output / "page2.png", image=green)

        _, report = self.run_validator()

        rows = report["evidence"]["output_pages"]
        self.assertNotEqual(
            rows[0]["decoded_content_sha256"],
            rows[1]["decoded_content_sha256"],
        )
        self.assertFalse(any(
            "duplicated output content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_isoluminant_color_substitution_is_exactly_detected(self) -> None:
        red = np.full((300, 220, 3), 255, np.uint8)
        green = red.copy()
        red[60:240, 35:185] = (0, 0, 100)
        green[60:240, 35:185] = (0, 51, 0)
        self.write_page(self.source / "page1.png", image=red)
        self.write_page(self.source / "page2.png", image=green)
        self.write_page(self.output / "page1.png", image=green)
        self.write_page(self.output / "page2.png", image=red)

        _, report = self.run_validator()

        self.assertGreaterEqual(
            sum(
                "probable substituted page" in failure
                for failure in report["evidence"]["failures"]
            ),
            2,
        )
        for pair in report["evidence"]["pairs"]:
            self.assertTrue(
                pair["cross_page_identity"]["exact_decoded_alternate_inputs"]
            )

    def test_exact_uniform_rgb_blank_substitution_is_not_suppressed(self) -> None:
        first = np.full((300, 220, 3), (255, 255, 255), np.uint8)
        second = np.full((300, 220, 3), (255, 220, 220), np.uint8)
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=second)
        self.write_page(self.output / "page2.png", image=first)

        _, report = self.run_validator()

        pairs = report["evidence"]["pairs"]
        self.assertTrue(all(pair["likely_blank"] for pair in pairs))
        self.assertEqual(
            [["page2.png"], ["page1.png"]],
            [
                pair["cross_page_identity"]["exact_decoded_alternate_inputs"]
                for pair in pairs
            ],
        )
        self.assertEqual(
            2,
            sum(
                "probable substituted page" in failure
                for failure in report["evidence"]["failures"]
            ),
        )
        self.assertFalse(
            any(
                "duplicated output content" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        self.assertEqual(
            0,
            report["evidence"]["duplicate_summary"][
                "mechanical_failure_pair_count"
            ],
        )
        self.assertFalse(report["mechanical_pass"])

    def test_exact_duplicate_outputs_fail_for_different_sources_despite_coarse_layout(self) -> None:
        first = self.image()
        second = self.image()
        for y in range(60, 241, 30):
            cv2.line(first, (25, y), (195, y), 0, 3)
            cv2.line(second, (25, y), (195, y), 120, 3)
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=first)
        self.write_page(self.output / "page2.png", image=first)

        _, report = self.run_validator()

        source_structure = validate_book.signature_similarity(
            validate_book.content_signature(first),
            validate_book.content_signature(second),
        )
        self.assertGreaterEqual(
            source_structure,
            validate_book.THRESHOLDS["perceptual_duplicate_similarity"],
        )
        self.assertTrue(
            any(
                "duplicated output content" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_exact_duplicate_outputs_fail_for_sources_with_9x17_mark_difference(self) -> None:
        first = self.image()
        for y in range(60, 241, 30):
            cv2.line(first, (35, y), (185, y), 0, 3)
        second = first.copy()
        second[25:42, 15:24] = 230
        self.assertEqual(9 * 17, int(np.count_nonzero(first != second)))
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=first)
        self.write_page(self.output / "page2.png", image=first)

        _, report = self.run_validator()

        decision = report["evidence"]["duplicate_decisions"][0]
        self.assertTrue(decision["output_exact_decoded_match"])
        self.assertFalse(decision["source_exact_decoded_match"])
        self.assertGreaterEqual(
            decision["source_pixel_similarity"],
            validate_book.THRESHOLDS["decoded_pixel_duplicate_similarity"],
        )
        self.assertFalse(decision["source_equality_established"])
        self.assertIsNone(decision["source_equality_method"])
        self.assertTrue(any(
            "duplicated output content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_exact_duplicate_outputs_from_exact_sources_require_review_only(self) -> None:
        source = self.image()
        for y in range(60, 241, 30):
            cv2.line(source, (35, y), (185, y), 0, 3)
        for index in (1, 2):
            self.write_page(self.source / f"page{index}.png", image=source)
            self.write_page(self.output / f"page{index}.png", image=source)

        _, report = self.run_validator()

        self.assertTrue(report["mechanical_pass"])
        self.assertFalse(
            any(
                "duplicated output content" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        duplicate_review = next(
            item
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "__batch_identity__"
        )
        self.assertEqual(1, len(duplicate_review["reasons"]))
        self.assertIn("1 candidate pair(s)", duplicate_review["reasons"][0])
        self.assertEqual(
            1,
            report["evidence"]["duplicate_summary"][
                "source_exactly_equal_review_only_pair_count"
            ],
        )
        decision = report["evidence"]["duplicate_decisions"][0]
        self.assertTrue(decision["source_equality_established"])
        self.assertEqual(
            "canonical native decoded SHA-256",
            decision["source_equality_method"],
        )

    def test_dirty_blank_sources_cleaned_to_identical_white_require_review_only(self) -> None:
        first = self.image()
        second = self.image()
        first[80:84, 40:44] = 0
        first[210:214, 170:174] = 0
        second[55:59, 150:154] = 0
        second[175:179, 65:69] = 0
        white = self.image()
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=white)
        self.write_page(self.output / "page2.png", image=white)

        _, report = self.run_validator()

        self.assertTrue(report["mechanical_pass"])
        self.assertTrue(all(pair["likely_blank"] for pair in report["evidence"]["pairs"]))
        summary = report["evidence"]["duplicate_summary"]
        self.assertEqual(1, summary["candidate_pair_count"])
        self.assertEqual(0, summary["mechanical_failure_pair_count"])
        self.assertEqual(1, summary["blank_dirt_cleanup_review_only_pair_count"])
        decision = report["evidence"]["duplicate_decisions"][0]
        self.assertTrue(decision["output_exact_decoded_match"])
        self.assertFalse(decision["source_equality_established"])
        self.assertTrue(decision["blank_dirt_cleanup_review_exception"])
        self.assertFalse(any(
            "duplicated output content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_meaningful_raw_or_fine_ink_cannot_use_blank_duplicate_exception(self) -> None:
        first = self.image()
        second = self.image()
        cv2.putText(first, "A", (70, 180), cv2.FONT_HERSHEY_SIMPLEX, 2, 0, 5)
        cv2.putText(second, "B", (70, 180), cv2.FONT_HERSHEY_SIMPLEX, 2, 0, 5)
        white = self.image()
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=white)
        self.write_page(self.output / "page2.png", image=white)

        _, report = self.run_validator()

        self.assertTrue(any(not pair["likely_blank"] for pair in report["evidence"]["pairs"]))
        decision = report["evidence"]["duplicate_decisions"][0]
        self.assertFalse(decision["blank_dirt_cleanup_review_exception"])
        self.assertEqual(
            1,
            report["evidence"]["duplicate_summary"]["mechanical_failure_pair_count"],
        )
        self.assertTrue(any(
            "duplicated output content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_exact_nonblank_duplicate_remains_a_hard_failure(self) -> None:
        first = self.image()
        second = self.image()
        cv2.putText(first, "LEFT", (15, 165), cv2.FONT_HERSHEY_SIMPLEX, 1.4, 0, 5)
        cv2.putText(second, "RIGHT", (5, 165), cv2.FONT_HERSHEY_SIMPLEX, 1.2, 0, 5)
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=first)
        self.write_page(self.output / "page2.png", image=first)

        _, report = self.run_validator()

        decision = report["evidence"]["duplicate_decisions"][0]
        self.assertTrue(decision["output_exact_decoded_match"])
        self.assertFalse(decision["blank_dirt_cleanup_review_exception"])
        self.assertFalse(report["mechanical_pass"])
        self.assertTrue(any(
            "duplicated output content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_transparent_png_is_white_composited_and_requires_review(self) -> None:
        opaque = np.full((300, 220, 4), 255, np.uint8)
        transparent = opaque.copy()
        transparent[80:220, 60:160, :3] = 0
        transparent[80:220, 60:160, 3] = 0
        for path, page in (
            (self.source / "page1.png", opaque),
            (self.output / "page1.png", transparent),
        ):
            success, encoded = cv2.imencode(".png", page)
            self.assertTrue(success)
            encoded.tofile(path)
        _, report = self.run_validator()
        output = report["evidence"]["output_pages"][0]
        self.assertTrue(output["transparency"]["has_alpha"])
        self.assertGreater(output["transparency"]["nonopaque_fraction"], 0)
        self.assertEqual(0.0, output["ink_fraction"])
        reasons = next(
            item["reasons"]
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.png"
        )
        self.assertTrue(any("transparent pixels" in reason for reason in reasons))

    def test_png_trns_and_la_are_white_composited(self) -> None:
        grayscale = Image.new("L", (40, 30), 0)
        grayscale.save(self.source / "page1.png", transparency=0)
        grayscale.save(self.output / "page1.png", transparency=0)
        la = Image.new("LA", (40, 30), (0, 0))
        la.save(self.source / "page2.png")
        la.save(self.output / "page2.png")
        _, report = self.run_validator()
        for row in report["evidence"]["output_pages"]:
            self.assertTrue(row["transparency"]["has_alpha"])
            self.assertEqual(1.0, row["transparency"]["fully_transparent_fraction"])
            self.assertEqual(0.0, row["ink_fraction"])

    def test_equal_luminance_color_loss_requires_review(self) -> None:
        source = np.full((120, 160, 3), 255, np.uint8)
        source[25:95, 20:75] = (255, 0, 0)
        source[25:95, 85:140] = (0, 130, 0)
        luminance = cv2.cvtColor(source, cv2.COLOR_RGB2GRAY)
        Image.fromarray(source, "RGB").save(self.source / "page1.png")
        Image.fromarray(luminance, "L").save(self.output / "page1.png")
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertTrue(pair["color_fidelity"]["review_required"])
        self.assertTrue(
            any(
                "color-aware signature" in reason
                for reason in next(
                    item["reasons"]
                    for item in report["evidence"]["visual_review_pages"]
                    if item["file"] == "page1.png"
                )
            )
        )

    def test_equal_luminance_regional_hue_swap_requires_review(self) -> None:
        source = np.full((120, 160, 3), 255, np.uint8)
        source[25:95, 20:75] = (255, 0, 0)
        source[25:95, 85:140] = (0, 130, 0)
        output = source.copy()
        output[25:95, 20:75] = (0, 130, 0)
        output[25:95, 85:140] = (255, 0, 0)
        self.assertTrue(np.array_equal(
            cv2.cvtColor(source, cv2.COLOR_RGB2GRAY),
            cv2.cvtColor(output, cv2.COLOR_RGB2GRAY),
        ))
        Image.fromarray(source, "RGB").save(self.source / "page1.png")
        Image.fromarray(output, "RGB").save(self.output / "page1.png")
        _, report = self.run_validator()
        fidelity = report["evidence"]["pairs"][0]["color_fidelity"]
        self.assertGreater(fidelity["color_signature_similarity"], 0.99)
        self.assertLess(fidelity["spatial_color_signature_similarity"], 0.80)
        self.assertTrue(fidelity["review_required"])

    def test_nonrepresentative_dark_gray_paper_requires_review(self) -> None:
        for index in (1, 2, 3):
            source = self.image()
            output = self.image()
            for y in range(60, 241, 30):
                cv2.line(source, (35, y), (185, y), 0, 3)
                cv2.line(output, (35, y), (185, y), 0, 3)
            if index == 2:
                output[output != 0] = 190
            self.write_page(self.source / f"page{index}.png", image=source)
            self.write_page(self.output / f"page{index}.png", image=output)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][1]
        comparison = pair["background_tonal_comparison"]
        self.assertTrue(report["mechanical_pass"])
        self.assertEqual(pair["source_ink_fraction"], pair["output_ink_fraction"])
        self.assertTrue(comparison["darkened_review_required"])
        reasons = next(
            item["reasons"]
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page2.png"
        )
        self.assertTrue(any("materially darkened" in reason for reason in reasons))
        template = next(
            item
            for item in report["approval_template"]["pages"]
            if item["file"] == "page2.png"
        )
        self.assertEqual(reasons, template["required_reasons"])

    def test_lost_paper_highlight_range_requires_review(self) -> None:
        source = np.tile(
            np.linspace(205, 255, 220, dtype=np.uint8), (300, 1)
        )
        output = np.minimum(source, 225).astype(np.uint8)
        for page in (source, output):
            for y in range(60, 241, 30):
                cv2.line(page, (35, y), (185, y), 0, 3)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["background_tonal_comparison"]
        self.assertTrue(comparison["highlight_loss_review_required"])
        self.assertGreater(comparison["paper_p99_highlight_drop"], 20)
        self.assertTrue(
            any(
                "highlight range" in reason
                for reason in report["evidence"]["visual_review_pages"][0]["reasons"]
            )
        )

    def test_introduced_background_color_cast_requires_review(self) -> None:
        source = np.full((300, 220, 3), 250, np.uint8)
        output = np.full((300, 220, 3), (255, 235, 180), np.uint8)
        for page in (source, output):
            for y in range(60, 241, 30):
                cv2.line(page, (35, y), (185, y), (0, 0, 0), 3)
        Image.fromarray(source, "RGB").save(self.source / "page1.png")
        Image.fromarray(output, "RGB").save(self.output / "page1.png")
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["background_tonal_comparison"]
        self.assertTrue(comparison["color_cast_review_required"])
        self.assertIsNotNone(
            report["evidence"]["output_pages"][0]["background_tonal_quality"][
                "background_rgb_median"
            ]
        )
        self.assertTrue(
            any(
                "gained a color cast" in reason
                for reason in report["evidence"]["visual_review_pages"][0]["reasons"]
            )
        )

    def test_grayscale_source_tinted_output_uses_zero_cast_baseline(self) -> None:
        source = np.full((300, 220), 245, np.uint8)
        output = np.full((300, 220, 3), (255, 230, 175), np.uint8)
        for y in range(60, 241, 30):
            cv2.line(source, (35, y), (185, y), 0, 3)
            cv2.line(output, (35, y), (185, y), (0, 0, 0), 3)
        self.write_page(self.source / "page1.png", image=source)
        Image.fromarray(output, "RGB").save(self.output / "page1.png")
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["background_tonal_comparison"]
        self.assertEqual(
            0.0,
            comparison["source_background_color_cast_baseline_lab_chroma"],
        )
        self.assertGreater(
            comparison["background_color_cast_increase_lab_chroma"], 8.0
        )
        self.assertTrue(comparison["color_cast_review_required"])

    def test_grayscale_source_with_colored_ink_flags_introduced_color(self) -> None:
        source = self.image()
        output = np.full((300, 220, 3), 255, np.uint8)
        cv2.rectangle(source, (35, 65), (185, 235), 0, -1)
        cv2.rectangle(output, (35, 65), (185, 235), (220, 40, 40), -1)
        self.write_page(self.source / "page1.png", image=source)
        Image.fromarray(output, "RGB").save(self.output / "page1.png")
        _, report = self.run_validator()
        fidelity = report["evidence"]["pairs"][0]["color_fidelity"]
        self.assertTrue(fidelity["introduced_color_review_required"])
        self.assertTrue(fidelity["review_required"])
        reasons = report["evidence"]["visual_review_pages"][0]["reasons"]
        self.assertTrue(any("introduced color" in reason for reason in reasons))

    def test_png_jpeg_and_tiff_dpi_metadata_is_captured(self) -> None:
        page = Image.fromarray(self.image(), "L")
        for suffix in ("png", "jpg", "tiff"):
            path = self.source / f"page1.{suffix}"
            page.save(path, dpi=(300, 240))
            _, metadata = validate_book.read_image(path)
            resolution = metadata["physical_resolution"]
            self.assertTrue(resolution["reliable"], suffix)
            self.assertAlmostEqual(300, resolution["dpi_x"], delta=1.0)
            self.assertAlmostEqual(240, resolution["dpi_y"], delta=1.0)
            path.unlink()

    def test_missing_or_changed_dpi_fails_unless_documented_review_safe(self) -> None:
        page = Image.fromarray(self.image(), "L")
        page.save(self.source / "page1.png", dpi=(300, 300))
        page.save(self.output / "page1.png")
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["physical_resolution_comparison"]
        self.assertTrue(comparison["failure_required"])
        self.assertTrue(any("DPI/physical size" in failure for failure in report["evidence"]["failures"]))

        note = "Legacy processor strips PNG pHYs; physical size was checked externally."
        _, documented = self.run_validator("--dpi-workflow-note", note)
        comparison = documented["evidence"]["pairs"][0]["physical_resolution_comparison"]
        self.assertFalse(comparison["failure_required"])
        self.assertTrue(comparison["review_required"])
        self.assertEqual(note, documented["evidence"]["dpi_workflow_note"])
        self.assertIn("--dpi-workflow-note", documented["evidence"]["approval_run_argument_template"])

        page.save(self.output / "page1.png", dpi=(72, 72))
        _, changed = self.run_validator("--dpi-workflow-note", note)
        comparison = changed["evidence"]["pairs"][0]["physical_resolution_comparison"]
        self.assertTrue(comparison["failure_required"])
        self.assertGreater(comparison["physical_size_relative_changes"]["width"], 1.0)

    def test_introduced_local_background_unevenness_requires_review(self) -> None:
        source = self.image()
        ramp = np.linspace(205, 250, source.shape[1], dtype=np.uint8)
        output = np.tile(ramp, (source.shape[0], 1))
        for page in (source, output):
            for y in range(60, 241, 30):
                cv2.line(page, (35, y), (185, y), 0, 3)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["background_tonal_comparison"]
        self.assertTrue(comparison["unevenness_review_required"])
        self.assertTrue(
            any(
                "gained local tonal unevenness" in reason
                for reason in report["evidence"]["visual_review_pages"][0]["reasons"]
            )
        )

    def test_legitimate_paper_whitening_is_not_tonal_corruption(self) -> None:
        source = np.full((300, 220), 225, np.uint8)
        output = np.full((300, 220), 255, np.uint8)
        for page in (source, output):
            for y in range(60, 241, 30):
                cv2.line(page, (35, y), (185, y), 0, 3)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        result, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["background_tonal_comparison"]
        self.assertTrue(report["mechanical_pass"])
        self.assertEqual(2, result)
        self.assertFalse(
            any(
                comparison[key]
                for key in (
                    "darkened_review_required",
                    "highlight_loss_review_required",
                    "dark_clipping_review_required",
                    "unevenness_review_required",
                    "color_cast_review_required",
                )
            )
        )
        reasons = report["evidence"]["visual_review_pages"][0]["reasons"]
        self.assertFalse(
            any(
                "paper/background" in reason or "paper highlight" in reason
                for reason in reasons
            )
        )

    def test_yellow_paper_whitening_preserves_adaptive_ink_and_geometry(self) -> None:
        source = np.full((300, 220, 3), (244, 225, 165), np.uint8)
        output = np.full((300, 220, 3), 255, np.uint8)
        for page in (source, output):
            for y in range(55, 246, 28):
                cv2.line(page, (30, y), (190, y), (35, 28, 20), 3)
            cv2.putText(page, "12", (80, 180), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (25, 20, 15), 3)
        Image.fromarray(source, "RGB").save(self.source / "page1.png")
        Image.fromarray(output, "RGB").save(self.output / "page1.png")
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertGreater(pair["ink_retention_ratio"], 0.95)
        self.assertFalse(pair["geometry"]["anisotropic_stretch"]["failed"])
        self.assertFalse(
            any("ink loss" in failure or "anisotropic stretch" in failure
                for failure in report["evidence"]["failures"])
        )

    def test_specks_do_not_expand_quantile_content_bounds(self) -> None:
        clean = self.image()
        cv2.rectangle(clean, (55, 75), (165, 225), 0, 3)
        speckled = clean.copy()
        speckled[2, 2] = 0
        speckled[-3, -3] = 0
        clean_bounds = validate_book.content_bounds(clean)
        speckled_bounds = validate_book.content_bounds(speckled)
        for key in ("left_fraction", "top_fraction", "right_fraction", "bottom_fraction"):
            self.assertAlmostEqual(clean_bounds[key], speckled_bounds[key], places=3)
        self.assertIn("quantile", speckled_bounds["method"])

    def test_bilevel_png_is_valid_encoded_one_bit(self) -> None:
        page = Image.new("1", (80, 60), 1)
        for root in (self.source, self.output):
            page.save(root / "page1.png")
        _, report = self.run_validator()
        metadata = report["evidence"]["output_pages"][0]["transparency"]
        self.assertEqual(1, metadata["sample_depth_bits"])
        self.assertEqual(1, metadata["encoded_metadata"]["bit_depth_bits"])
        self.assertTrue(metadata["depth_supported"])

    def test_bilevel_png_trns_values_are_white_composited_at_one_bit(self) -> None:
        pixels = Image.new("1", (40, 20), 1)
        pixels.paste(0, (0, 0, 20, 20))
        for index, transparent_sample in enumerate((0, 1), start=1):
            for root in (self.source, self.output):
                pixels.save(
                    root / f"page{index}.png",
                    transparency=transparent_sample,
                )
        _, report = self.run_validator()
        rows = report["evidence"]["output_pages"]
        for row in rows:
            metadata = row["transparency"]
            self.assertTrue(metadata["encoded_metadata"]["has_trns"])
            self.assertTrue(metadata["has_alpha"])
            self.assertEqual(1, metadata["sample_depth_bits"])
            self.assertEqual(1, metadata["encoded_metadata"]["bit_depth_bits"])
            self.assertEqual(0.5, metadata["nonopaque_fraction"])
            self.assertEqual(0.5, metadata["fully_transparent_fraction"])
            self.assertTrue(metadata["depth_supported"])
        self.assertEqual(0.0, rows[0]["ink_fraction"])
        self.assertAlmostEqual(0.5, rows[1]["ink_fraction"], places=2)

    def test_rejects_16_bit_rgb_before_pillow_downconversion(self) -> None:
        page = np.full((30, 40, 3), 65535, np.uint16)
        for root in (self.source, self.output):
            self.assertTrue(cv2.imwrite(str(root / "page1.png"), page))
        _, report = self.run_validator()
        self.assertTrue(
            any(
                "greater-than-8-bit multi-channel image is unsupported" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_16_bit_grayscale_trns_is_composited_without_overflow(self) -> None:
        pixels = np.zeros((20, 40), np.uint16)
        pixels[:, 20:] = 65535
        for root in (self.source, self.output):
            Image.fromarray(pixels, mode="I;16").save(
                root / "page1.png", transparency=65535
            )
        _, report = self.run_validator()
        row = report["evidence"]["output_pages"][0]
        metadata = row["transparency"]
        self.assertTrue(metadata["encoded_metadata"]["has_trns"])
        self.assertEqual(16, metadata["sample_depth_bits"])
        self.assertTrue(metadata["depth_supported"])
        self.assertAlmostEqual(0.5, row["ink_fraction"], places=2)

    def test_unsupported_native_depth_fails_closed(self) -> None:
        pixels = np.full((30, 40), 100000, dtype=np.int32)
        for root in (self.source, self.output):
            Image.fromarray(pixels).save(root / "page1.tiff")
        _, report = self.run_validator()
        self.assertTrue(
            any(
                "unsupported greater-than-8-bit native decoded representation" in item
                for item in report["evidence"]["failures"]
            )
        )

    def test_16_bit_grayscale_trns_identity_preserves_native_samples(self) -> None:
        first = np.full((12, 16), 1000, dtype=np.uint16)
        second = np.full((12, 16), 1001, dtype=np.uint16)
        first_path = self.root / "first.png"
        second_path = self.root / "second.png"
        different_trns_path = self.root / "different-trns.png"
        Image.fromarray(first, mode="I;16").save(first_path, transparency=65535)
        Image.fromarray(second, mode="I;16").save(second_path, transparency=65535)
        Image.fromarray(first, mode="I;16").save(
            different_trns_path, transparency=1000
        )

        first_metric, first_metadata = validate_book.read_image(first_path)
        second_metric, second_metadata = validate_book.read_image(second_path)
        _, different_trns_metadata = validate_book.read_image(different_trns_path)

        self.assertTrue(np.array_equal(first_metric, second_metric))
        self.assertNotEqual(
            first_metadata["_decoded_content_sha256"],
            second_metadata["_decoded_content_sha256"],
        )
        self.assertNotEqual(
            first_metadata["_decoded_content_sha256"],
            different_trns_metadata["_decoded_content_sha256"],
        )

    def test_json_reads_are_bounded_before_parsing(self) -> None:
        document = self.root / "oversized.json"
        document.write_bytes(b" " * 17)
        with self.assertRaisesRegex(ValueError, "JSON file exceeds safety budget"):
            validate_book.load_json_file(document, 16)

    def test_evidence_hash_is_stable_for_30_windows_ctime_drifts(self) -> None:
        for directory in (self.source, self.output):
            self.write_page(directory / "alpha.png")
        manifest = self.root / "pairs.json"
        manifest.write_text(
            json.dumps(
                {"pairs": [{"input": "alpha.png", "output": "alpha.png"}]}
            ),
            encoding="utf-8",
        )
        real_fstat = os.fstat
        call_count = 0

        class DriftingCtime:
            def __init__(self, status: os.stat_result, ctime_ns: int) -> None:
                self._status = status
                self.st_ctime_ns = ctime_ns

            def __getattr__(self, name: str) -> object:
                return getattr(self._status, name)

        def drifting_fstat(descriptor: int) -> DriftingCtime:
            nonlocal call_count
            call_count += 1
            status = real_fstat(descriptor)
            return DriftingCtime(status, status.st_ctime_ns + call_count)

        evidence_hashes: list[str] = []
        with patch.object(validate_book.os, "fstat", side_effect=drifting_fstat):
            for _ in range(30):
                result, report = self.run_validator(
                    "--pairing-manifest", str(manifest)
                )
                self.assertEqual(2, result)
                self.assertTrue(report["mechanical_pass"])
                manifest_inventory = report["evidence"]["pairing"][
                    "manifest_inventory"
                ]
                self.assertNotIn("ctime_ns", manifest_inventory)
                evidence_hashes.append(str(report["evidence_hash"]))

        self.assertEqual(1, len(set(evidence_hashes)))

    def _removed_retained_capture_rejects_same_byte_replacement_identity(self) -> None:
        document = self.root / "identity.json"
        document.write_bytes(b'{"same":"bytes"}')
        captured = validate_book.capture_regular_file(document, 1024)
        document_absolute = document.resolve()
        real_stat = Path.stat

        class ReplacementIdentity:
            def __init__(self, status: os.stat_result) -> None:
                self._status = status
                self.st_ino = status.st_ino + 1

            def __getattr__(self, name: str) -> object:
                return getattr(self._status, name)

        def replacement_stat(path: Path, *args: object, **kwargs: object) -> object:
            status = real_stat(path, *args, **kwargs)
            if Path(path) == document_absolute:
                return ReplacementIdentity(status)
            return status

        try:
            with patch.object(Path, "stat", replacement_stat):
                self.assertFalse(
                    validate_book.captured_file_still_published(captured)
                )
        finally:
            captured.close()

    def _removed_image_capture_rejects_concurrent_growth_at_max_plus_one(self) -> None:
        image = self.root / "growing.png"
        image.write_bytes(b"x" * 64)

        def grow() -> None:
            descriptor = os.open(image, os.O_WRONLY | os.O_APPEND)
            try:
                os.write(descriptor, b"y")
            finally:
                os.close(descriptor)

        opened, raced = self.racing_open(image, grow)

        with patch.object(Path, "open", opened):
            with self.assertRaisesRegex(
                ValueError, "mutated while being captured|exceeds safety budget"
            ):
                validate_book.read_immutable_file(image, 64)

        self.assertTrue(raced.is_set())
        self.assertEqual(65, image.stat().st_size)

    def _removed_retained_handle_rehash_stops_at_snapshotted_size_plus_one(self) -> None:
        document = self.root / "growing.json"
        document.write_bytes(b"x" * 64)
        captured = validate_book.capture_regular_file(document, 64)
        real_read = validate_book.os.read
        requested: list[int] = []
        grown = False

        def bounded_read(descriptor: int, size: int) -> bytes:
            nonlocal grown
            requested.append(size)
            if not grown:
                grown = True
                with document.open("ab") as stream:
                    stream.write(b"y")
            return real_read(descriptor, size)

        try:
            with patch.object(validate_book.os, "read", side_effect=bounded_read):
                self.assertFalse(
                    validate_book.captured_file_still_published(captured)
                )
        finally:
            captured.close()

        self.assertEqual([65], requested)

    def _removed_capture_rehash_rejects_concurrent_growth_at_size_plus_one(self) -> None:
        document = self.root / "rehash-growth.json"
        document.write_bytes(b"x" * 64)
        real_read = validate_book.os.read
        requested: list[int] = []
        grown = False

        def grow_before_rehash(descriptor: int, size: int) -> bytes:
            nonlocal grown
            requested.append(size)
            if not grown:
                grown = True
                with document.open("ab") as stream:
                    stream.write(b"y")
            return real_read(descriptor, size)

        with patch.object(
            validate_book.os, "read", side_effect=grow_before_rehash
        ):
            with self.assertRaisesRegex(ValueError, "mutated while being captured"):
                validate_book.capture_regular_file(document, 64)

        self.assertEqual([65], requested)

    def _removed_cli_image_capture_rejects_concurrent_replacement(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        target = self.source / "page1.png"
        held = self.root / "held.png"
        replacement = self.root / "replacement.png"
        self.write_page(replacement, image=np.full((20, 20), 127, np.uint8))
        replacement_bytes = replacement.read_bytes()
        replacement.unlink()
        replacement_blocked = False

        def replace_path() -> None:
            nonlocal replacement_blocked
            try:
                os.replace(target, held)
                target.write_bytes(replacement_bytes)
            except PermissionError:
                replacement_blocked = True

        opened, raced = self.racing_open(target, replace_path)
        original_capture = validate_book.capture_image_file

        def capture_with_race(
            path: Path, maximum_bytes: int
        ) -> validate_book.ImmutableFile:
            if Path(path) != target:
                return original_capture(path, maximum_bytes)
            with patch.object(Path, "open", opened):
                return original_capture(path, maximum_bytes)

        with patch.object(
            validate_book, "capture_image_file", side_effect=capture_with_race
        ):
            result = validate_book.main(
                [
                    str(self.source),
                    str(self.output),
                    "--evidence-report",
                    str(self.report),
                    "--max-inventory-file-bytes",
                    str(target.stat().st_size),
                ]
            )

        self.assertTrue(raced.is_set())
        if replacement_blocked:
            report = json.loads(self.report.read_text(encoding="utf-8"))
            self.assertTrue(report["mechanical_pass"])
        else:
            self.assertEqual(2, result)
            self.assertFalse(self.report.exists())

    def test_json_hashing_snapshots_and_writes_are_bounded(self) -> None:
        document = self.root / "oversized.json"
        document.write_bytes(b" " * 17)
        with patch.object(
            validate_book, "sha256_file", side_effect=AssertionError("hashed")
        ):
            snapshot = validate_book.regular_file_snapshot(document, 16)
        self.assertTrue(snapshot["oversized"])
        self.assertNotIn("sha256", snapshot)

        with self.assertRaisesRegex(ValueError, "canonical JSON exceeds"):
            validate_book.canonical_hash({"value": "too large"}, 8)

        target = self.root / "bounded.json"
        with self.assertRaisesRegex(ValueError, "JSON output exceeds"):
            validate_book.atomic_create_json(target, {"value": "too large"}, 8)
        self.assertFalse(target.exists())
        self.assertEqual([], list(self.root.glob(".bounded.json.*.tmp")))

    def test_oversized_manifest_is_not_hashed(self) -> None:
        manifest = self.root / "pairing.json"
        manifest.write_bytes(
            b" " * (validate_book.MAXIMUM_MANIFEST_JSON_BYTES + 1)
        )
        with patch.object(
            validate_book, "sha256_file", side_effect=AssertionError("hashed")
        ):
            inventory = validate_book.pairing_manifest_inventory(manifest)
            pairing = validate_book.load_pairing_manifest(manifest, [], [])
        self.assertEqual("budget_rejected", inventory["kind"])
        self.assertIsNone(inventory["sha256"])
        self.assertIsNone(pairing["manifest_sha256"])

    def test_rejects_extension_mismatched_actual_format(self) -> None:
        page = Image.fromarray(self.image())
        page.save(self.source / "page1.png", format="JPEG")
        self.write_page(self.output / "page1.png")
        _, report = self.run_validator()
        self.assertTrue(
            any("extension-mismatched actual image format" in failure for failure in report["evidence"]["failures"])
        )

    def test_frame_count_is_checked_for_non_tiff_formats(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        original = validate_book.read_image

        def two_frames(
            path: Path,
            maximum_decoded_pixels: int = validate_book.SAFETY_BUDGET_DEFAULTS[
                "maximum_decoded_pixels_per_page"
            ],
            data: bytes | None = None,
        ) -> tuple[np.ndarray, dict[str, object]]:
            image, metadata = original(path, maximum_decoded_pixels, data)
            metadata["frame_count"] = 2
            return image, metadata

        with patch.object(validate_book, "read_image", side_effect=two_frames):
            _, report = self.run_validator()
        self.assertTrue(
            any("page1.png (2 frames)" in failure for failure in report["evidence"]["failures"])
        )

    def test_exif_orientation_is_applied_and_depth_is_recorded(self) -> None:
        oriented_pixels = np.full((30, 50, 3), 255, dtype=np.uint8)
        oriented_pixels[5:25, 5:15] = 0
        for path in (self.source / "page1.jpg", self.output / "page1.jpg"):
            image = Image.fromarray(oriented_pixels)
            exif = Image.Exif()
            exif[274] = 6
            image.save(path, exif=exif)
        deep_pixels = np.full((30, 50), 65535, dtype=np.uint16)
        deep_pixels[5:25, 5:15] = 0
        for path in (self.source / "page2.png", self.output / "page2.png"):
            Image.fromarray(deep_pixels, mode="I;16").save(path)
        _, report = self.run_validator()
        oriented_page, deep_page = report["evidence"]["output_pages"]
        self.assertEqual([30, 50], oriented_page["transparency"]["oriented_size"])
        self.assertTrue(oriented_page["transparency"]["exif_orientation_applied"])
        self.assertEqual(16, deep_page["transparency"]["sample_depth_bits"])
        self.assertTrue(deep_page["transparency"]["supported_16_bit_mode"])
        self.assertTrue(deep_page["transparency"]["depth_supported"])
        orientation_reasons = next(
            item["reasons"] for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.jpg"
        )
        depth_reasons = next(
            item["reasons"] for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page2.png"
        )
        self.assertTrue(any("EXIF orientation" in reason for reason in orientation_reasons))
        self.assertTrue(any("greater-than-8-bit" in reason for reason in depth_reasons))

    def test_anisotropic_stretch_fails_and_requires_review(self) -> None:
        source = self.image()
        for y in range(60, 241, 30):
            cv2.line(source, (35, y), (185, y), 0, 3)
        self.write_page(self.source / "page1.png", image=source)
        stretched = cv2.resize(source, (260, 300))
        self.write_page(self.output / "page1.png", image=stretched)
        _, report = self.run_validator()
        self.assertTrue(any("anisotropic stretch" in item for item in report["evidence"]["failures"]))
        comparison = report["evidence"]["pairs"][0]["geometry"]["anisotropic_stretch"]
        self.assertTrue(comparison["failed"])

    def test_clean_asymmetric_blank_margin_crop_is_review_not_stretch_failure(self) -> None:
        source = self.image()
        for y in range(70, 231, 32):
            cv2.line(source, (45, y), (175, y), 0, 3)
        cropped = source[10:290, 30:190]
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=cropped)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["geometry"]["anisotropic_stretch"]
        self.assertTrue(comparison["canvas_margin_change_review_required"])
        self.assertFalse(comparison["failed"])
        self.assertFalse(
            any("anisotropic stretch exceeds limit" in item
                for item in report["evidence"]["failures"])
        )
        self.assertTrue(any(
            "blank-margin crop or padding" in reason
            for page in report["evidence"]["visual_review_pages"]
            for reason in page["reasons"]
        ))

    def test_true_registered_anisotropic_stretch_still_fails(self) -> None:
        source = self.image()
        for y in range(60, 241, 30):
            cv2.line(source, (35, y), (185, y), 0, 3)
        stretched = cv2.resize(source, (275, 300), interpolation=cv2.INTER_NEAREST)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=stretched)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["geometry"]["anisotropic_stretch"]
        self.assertGreater(comparison["horizontal_content_scale"], 1.20)
        self.assertTrue(comparison["failed"])

    def test_same_canvas_foreground_anisotropic_stretch_fails(self) -> None:
        source = self.image()
        cv2.rectangle(source, (60, 70), (160, 230), 0, -1)
        output = self.image()
        stretched = cv2.resize(source[70:231, 60:161], (140, 161))
        output[70:231, 40:180] = stretched
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["geometry"]["anisotropic_stretch"]
        self.assertEqual(1.0, comparison["source_aspect_ratio"] / comparison["output_aspect_ratio"])
        self.assertGreater(comparison["horizontal_content_scale"], comparison["vertical_content_scale"])
        self.assertTrue(comparison["failed"])

    def test_same_bbox_internal_landmarks_detect_anisotropic_stretch(self) -> None:
        source = self.image()
        output = self.image()
        source[150, 25] = output[150, 25] = 0
        source[150, 195] = output[150, 195] = 0
        for x in (65, 95, 125, 155):
            cv2.rectangle(source, (x - 7, 90), (x + 7, 210), 0, -1)
        for x in (56, 92, 128, 164):
            cv2.rectangle(output, (x - 7, 90), (x + 7, 210), 0, -1)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        _, report = self.run_validator()
        comparison = report["evidence"]["pairs"][0]["geometry"]["anisotropic_stretch"]
        self.assertGreater(comparison["projection_landmark_residual_fraction"], 0.05)
        self.assertTrue(comparison["failed"])

    def test_repetitive_music_identity_ambiguity_is_review_not_failure(self) -> None:
        first = self.image()
        second = self.image()
        for page in (first, second):
            for y in range(55, 246, 20):
                cv2.line(page, (18, y), (202, y), 0, 2)
        cv2.circle(first, (85, 124), 4, 0, -1)
        cv2.circle(second, (90, 124), 4, 0, -1)
        for index, page in enumerate((first, second), start=1):
            self.write_page(self.source / f"page{index}.png", image=page)
            self.write_page(self.output / f"page{index}.png", image=page)
        _, report = self.run_validator()
        self.assertFalse(
            any("probable substituted page" in failure
                for failure in report["evidence"]["failures"])
        )
        reasons = [
            reason
            for page in report["evidence"]["visual_review_pages"]
            for reason in page["reasons"]
        ]
        self.assertTrue(any("ambiguous decoded-content identity" in reason for reason in reasons))

    def test_true_exact_substitution_and_rotation_remain_failures(self) -> None:
        first = self.image()
        second = self.image()
        cv2.putText(first, "LEFT", (20, 165), cv2.FONT_HERSHEY_SIMPLEX, 1.4, 0, 5)
        cv2.putText(second, "UP", (35, 125), cv2.FONT_HERSHEY_SIMPLEX, 2.2, 0, 8)
        cv2.rectangle(second, (30, 35), (190, 55), 0, -1)
        cv2.circle(second, (55, 225), 14, 0, -1)
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=second)
        self.write_page(
            self.output / "page2.png",
            image=cv2.rotate(second, cv2.ROTATE_90_CLOCKWISE),
        )
        _, report = self.run_validator()
        failures = "\n".join(report["evidence"]["failures"])
        self.assertIn("probable substituted page", failures)
        self.assertIn("content orientation mismatch", failures)

    def test_horizontal_vertical_and_transpose_mirrors_fail_orientation_gate(self) -> None:
        transforms = (
            ("horizontal", lambda page: cv2.flip(page, 1)),
            ("vertical", lambda page: cv2.flip(page, 0)),
            ("transpose", cv2.transpose),
        )
        for index, (label, transform) in enumerate(transforms, start=1):
            page = self.image()
            cv2.putText(page, label[:2].upper(), (20, 150), cv2.FONT_HERSHEY_SIMPLEX, 1.8, 0, 7)
            cv2.rectangle(page, (25, 35), (90, 55 + index * 8), 0, -1)
            cv2.circle(page, (175, 235 - index * 20), 10 + index, 0, -1)
            self.write_page(self.source / f"page{index}.png", image=page)
            self.write_page(self.output / f"page{index}.png", image=transform(page))
        _, report = self.run_validator()
        failures = "\n".join(report["evidence"]["failures"])
        self.assertGreaterEqual(failures.count("content orientation mismatch"), 3)
        for pair in report["evidence"]["pairs"]:
            self.assertIn(
                pair["content_identity"]["best_orientation"],
                {"mirror_horizontal", "mirror_vertical", "transpose", "transverse"},
            )
            self.assertEqual("failure", pair["content_identity"]["orientation_decision"])

    def test_foreground_shift_and_crop_require_review(self) -> None:
        source = self.image()
        cv2.rectangle(source, (50, 70), (170, 230), 0, -1)
        shifted = self.image()
        cv2.rectangle(shifted, (65, 80), (185, 240), 0, -1)
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=shifted)
        _, report = self.run_validator()
        registration = report["evidence"]["pairs"][0]["geometry"]["foreground_registration"]
        self.assertTrue(registration["review_required"])
        self.assertGreater(registration["normalized_centroid_delta"]["distance"], 0.02)
        reasons = report["evidence"]["visual_review_pages"]
        self.assertTrue(any(
            "foreground bbox/centroid/isotropic-scale shift or crop" in reason
            for page in reasons for reason in page["reasons"]
        ))

    def test_evidence_hash_is_independent_of_report_target(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, first = self.run_validator()
        second_path = self.root / "different-evidence-name.json"
        result = validate_book.main(
            [
                str(self.source), str(self.output),
                "--evidence-report", str(second_path),
            ]
        )
        self.assertEqual(2, result)
        second = json.loads(second_path.read_text(encoding="utf-8"))
        self.assertEqual(first["evidence_hash"], second["evidence_hash"])
        self.assertNotIn("pairs", first)
        self.assertEqual(
            first["evidence_hash"],
            validate_book.canonical_hash(
                {
                    key: first[key]
                    for key in (
                        "schema_version", "report_type", "mechanical_pass",
                        "visual_review_required", "passed", "evidence",
                    )
                }
            ),
        )

    def test_entire_mechanical_report_is_approval_bound(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        initial["mechanical_pass"] = not initial["mechanical_pass"]
        self.report.write_text(json.dumps(initial), encoding="utf-8")
        result = validate_book.main(
            [
                str(self.source), str(self.output),
                "--evidence-report", str(self.report),
                "--approval", str(approval),
                "--final-report", str(self.final_report),
            ]
        )
        self.assertEqual(2, result)
        final = json.loads(self.final_report.read_text(encoding="utf-8"))
        self.assertTrue(any("entire canonical" in error for error in final["approval_errors"]))

    def _removed_atomic_publish_leaves_no_target_or_temporary_on_link_failure(self) -> None:
        target = self.root / "atomic.json"
        with patch.object(validate_book.os, "link", side_effect=OSError("simulated")):
            with self.assertRaises(OSError):
                validate_book.atomic_create_json(target, {"complete": True})
        self.assertFalse(target.exists())
        self.assertEqual([], list(self.root.glob(".atomic.json.*.tmp")))

    def _removed_atomic_publish_removes_new_target_after_post_link_failure(self) -> None:
        target = self.root / "atomic.json"
        real_unlink = Path.unlink
        failed = False

        def fail_temporary_unlink(path: Path, *args: object, **kwargs: object) -> None:
            nonlocal failed
            if not failed and path.name.startswith(".atomic.json."):
                failed = True
                raise OSError("simulated post-link failure")
            real_unlink(path, *args, **kwargs)

        with patch.object(Path, "unlink", new=fail_temporary_unlink):
            with self.assertRaisesRegex(OSError, "post-link"):
                validate_book.atomic_create_json(target, {"passed": True})
        self.assertFalse(target.exists())
        self.assertEqual([], list(self.root.glob(".atomic.json.*.tmp")))

    def _removed_atomic_publish_does_not_remove_replacement_after_post_link_failure(self) -> None:
        target = self.root / "atomic.json"
        replacement = b'{"owner":"other"}'
        real_unlink = Path.unlink
        failed = False

        def replace_then_fail(path: Path, *args: object, **kwargs: object) -> None:
            nonlocal failed
            if not failed and path.name.startswith(".atomic.json."):
                failed = True
                real_unlink(target)
                target.write_bytes(replacement)
                raise OSError("simulated post-link replacement")
            real_unlink(path, *args, **kwargs)

        with patch.object(Path, "unlink", new=replace_then_fail):
            with self.assertRaisesRegex(OSError, "replacement"):
                validate_book.atomic_create_json(target, {"passed": True})
        self.assertEqual(replacement, target.read_bytes())

    def test_inventory_lists_all_entries_and_reviews_unrecognized_files(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        (self.source / "notes.txt").write_text("classification needed", encoding="utf-8")
        nested = self.output / "metadata"
        nested.mkdir()
        (nested / "record.bin").write_bytes(b"not an image")
        _, report = self.run_validator()
        source_inventory = report["evidence"]["candidate_evidence"]["input"]
        self.assertEqual(
            {"notes.txt", "page1.png"},
            {item["path"] for item in source_inventory["top_level_files"]},
        )
        self.assertEqual(
            {"metadata", "metadata\\record.bin"},
            {item["path"] for item in report["evidence"]["candidate_evidence"]["output"]["nested_entries"]},
        )
        for side in ("input", "output"):
            for item in report["evidence"]["candidate_evidence"][side]["all_entries"]:
                self.assertIn("is_symlink", item)
                self.assertIn("is_reparse_point", item)
        review_files = {item["file"] for item in report["evidence"]["visual_review_pages"]}
        self.assertIn("__input_inventory__", review_files)
        self.assertIn("__output_inventory__", review_files)

    def test_inventory_budgets_reject_oversized_tree_and_file_before_hashing(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        nested = self.source / "one" / "two"
        nested.mkdir(parents=True)
        (nested / "large.bin").write_bytes(b"x" * 256)
        _, report = self.run_validator(
            "--max-inventory-depth", "1",
            "--max-inventory-file-bytes", "128",
        )
        failures = report["evidence"]["failures"]
        self.assertTrue(any("recursion-depth safety budget" in item for item in failures))
        source_budget = report["evidence"]["safety_budgets"]["observed"]["inventory"]["input"]
        self.assertTrue(source_budget["rejected"])
        self.assertEqual(0, source_budget["bytes_hashed"])

        self.temporary.cleanup()
        self.setUp()
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        (self.source / "large.bin").write_bytes(b"x" * 256)
        _, report = self.run_validator("--max-inventory-file-bytes", "128")
        self.assertTrue(any(
            "per-file hashing safety budget" in item
            for item in report["evidence"]["failures"]
        ))
        self.assertEqual(
            0,
            report["evidence"]["safety_budgets"]["observed"]["inventory"]["input"]["bytes_hashed"],
        )

    def test_custom_budget_overrides_are_emitted_for_identical_approval_rerun(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, report = self.run_validator(
            "--max-pages", "7",
            "--max-cross-match-comparisons", "99",
        )
        template = report["evidence"]["approval_run_argument_template"]
        self.assertEqual(
            [
                "--max-pages", "7",
                "--max-cross-match-comparisons", "99",
            ],
            template[2:6],
        )
        self.assertIn("--approval", template)

    def test_inventory_entry_and_total_hash_budgets_fail_closed(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        (self.source / "notes.txt").write_text("inventory", encoding="utf-8")
        _, report = self.run_validator("--max-inventory-entries", "1")
        self.assertTrue(any(
            "entry-count safety budget" in item
            for item in report["evidence"]["failures"]
        ))

        self.temporary.cleanup()
        self.setUp()
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, report = self.run_validator("--max-inventory-total-bytes-hashed", "1")
        self.assertTrue(any(
            "total hashing safety budget" in item
            for item in report["evidence"]["failures"]
        ))
        for side in ("input", "output"):
            self.assertEqual(
                0,
                report["evidence"]["safety_budgets"]["observed"]["inventory"][side]["bytes_hashed"],
            )

    def test_many_pages_abort_streaming_enumeration_before_hashing_or_pairing(self) -> None:
        for index in range(100):
            (self.source / f"page{index:03}.png").write_bytes(b"not decoded")
            (self.output / f"page{index:03}.png").write_bytes(b"not decoded")

        original_scandir = os.scandir
        enumeration_counts: list[int] = []

        def counted_scandir(path: os.PathLike[str]) -> object:
            count_index = len(enumeration_counts)
            enumeration_counts.append(0)

            def entries() -> object:
                with original_scandir(path) as iterator:
                    for entry in iterator:
                        enumeration_counts[count_index] += 1
                        yield entry

            return entries()

        with (
            patch.object(validate_book.os, "scandir", side_effect=counted_scandir),
            patch.object(validate_book, "sha256_file") as sha256,
            patch.object(validate_book, "pair_pages") as pair_pages,
        ):
            result, report = self.run_validator("--max-pages", "3")

        self.assertEqual(2, result)
        self.assertTrue(report["evidence"]["safety_budgets"]["rejected"]["page_count"])
        self.assertTrue(enumeration_counts)
        self.assertTrue(all(count <= 4 for count in enumeration_counts))
        sha256.assert_not_called()
        pair_pages.assert_not_called()

    def test_many_inventory_entries_abort_before_exhaustive_collection(self) -> None:
        for index in range(100):
            (self.source / f"entry{index:03}.txt").write_text("x", encoding="utf-8")
            (self.output / f"entry{index:03}.txt").write_text("x", encoding="utf-8")

        original_scandir = os.scandir
        enumeration_counts: list[int] = []

        def counted_scandir(path: os.PathLike[str]) -> object:
            count_index = len(enumeration_counts)
            enumeration_counts.append(0)

            def entries() -> object:
                with original_scandir(path) as iterator:
                    for entry in iterator:
                        enumeration_counts[count_index] += 1
                        yield entry

            return entries()

        with (
            patch.object(validate_book.os, "scandir", side_effect=counted_scandir),
            patch.object(validate_book, "sha256_file") as sha256,
            patch.object(validate_book, "pair_pages") as pair_pages,
        ):
            result, report = self.run_validator("--max-inventory-entries", "5")

        self.assertEqual(2, result)
        self.assertTrue(report["evidence"]["safety_budgets"]["rejected"]["inventory"])
        self.assertTrue(enumeration_counts)
        self.assertTrue(all(count <= 6 for count in enumeration_counts))
        sha256.assert_not_called()
        pair_pages.assert_not_called()

    def test_oversized_unknown_entry_is_not_content_sniffed_or_hashed(self) -> None:
        oversized = self.source / "unknown"
        oversized.write_bytes(b"x" * 256)
        with (
            patch.object(validate_book, "looks_like_image") as looks_like_image,
            patch.object(validate_book, "sha256_file") as sha256,
        ):
            inventory = validate_book.candidate_inventory(
                self.source,
                maximum_file_bytes=128,
            )
        self.assertTrue(inventory["budget"]["rejected"])
        looks_like_image.assert_not_called()
        sha256.assert_not_called()

    def test_manifest_order_selects_representatives_and_covers(self) -> None:
        for name in ("alpha.png", "beta.png", "gamma.png"):
            self.write_page(self.source / name)
            self.write_page(self.output / name)
        manifest = self.root / "pairs.json"
        manifest.write_text(
            json.dumps(
                {
                    "pairs": [
                        {"input": "gamma.png", "output": "gamma.png"},
                        {"input": "alpha.png", "output": "alpha.png"},
                        {"input": "beta.png", "output": "beta.png"},
                    ]
                }
            ),
            encoding="utf-8",
        )
        _, report = self.run_validator("--pairing-manifest", str(manifest))
        categories = report["evidence"]["visual_review_categories"]
        self.assertEqual(
            ["gamma.png", "alpha.png", "beta.png"],
            categories["beginning_middle_end"],
        )
        self.assertEqual(["gamma.png", "beta.png"], categories["cover_or_first_last"])
        inventory = report["evidence"]["pairing"]["manifest_inventory"]
        self.assertEqual(manifest.resolve(), Path(inventory["path"]))
        self.assertEqual(report["evidence"]["pairing"]["manifest_sha256"], inventory["sha256"])

    def test_manifest_mutation_immediately_before_publication_aborts(self) -> None:
        for root in (self.source, self.output):
            self.write_page(root / "alpha.png")
        manifest = self.root / "pairs.json"
        manifest.write_text(
            json.dumps({"pairs": [{"input": "alpha.png", "output": "alpha.png"}]}),
            encoding="utf-8",
        )
        original = validate_book.filesystem_snapshot
        calls = 0

        def mutate_manifest(*args: object, **kwargs: object) -> dict[str, object]:
            nonlocal calls
            calls += 1
            snapshot = original(*args, **kwargs)
            if calls == 2:
                manifest.write_text('{"pairs":[]}', encoding="utf-8")
            return snapshot

        with patch.object(validate_book, "filesystem_snapshot", side_effect=mutate_manifest):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--pairing-manifest", str(manifest),
                    "--evidence-report", str(self.report),
                ]
            )
        self.assertEqual(2, result)
        self.assertFalse(self.report.exists())

    def _removed_mutation_during_atomic_create_json_removes_evidence_report(self) -> None:
        for root in (self.source, self.output):
            self.write_page(root / "alpha.png")
        original = validate_book.atomic_create_json

        def mutate_after_link(
            path: Path, document: dict[str, object], maximum_bytes: int
        ) -> validate_book.PublishedJSON:
            published = original(path, document, maximum_bytes)
            changed = self.image()
            cv2.circle(changed, (110, 150), 20, 0, 3)
            self.write_page(self.output / "alpha.png", image=changed)
            return published

        with patch.object(
            validate_book, "atomic_create_json", side_effect=mutate_after_link
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                ]
            )

        self.assertEqual(2, result)
        self.assertFalse(self.report.exists())

    def test_duplicate_json_keys_are_rejected(self) -> None:
        for root in (self.source, self.output):
            self.write_page(root / "alpha.png")
        manifest = self.root / "pairs.json"
        manifest.write_text(
            '{"pairs":[{"input":"alpha.png","output":"alpha.png"}],"pairs":[]}',
            encoding="utf-8",
        )
        _, report = self.run_validator("--pairing-manifest", str(manifest))
        self.assertTrue(
            any("duplicate JSON key" in issue for issue in report["evidence"]["pairing"]["issues"])
        )

    def test_rejects_cross_tree_hardlink_alias(self) -> None:
        self.write_page(self.source / "page1.png")
        os.link(self.source / "page1.png", self.output / "page1.png")
        _, report = self.run_validator()
        self.assertTrue(report["evidence"]["input_output_file_identity_aliases"])
        self.assertTrue(
            any("file-identity alias" in failure for failure in report["evidence"]["failures"])
        )

    def test_rejects_reparse_pairing_manifest(self) -> None:
        for root in (self.source, self.output):
            self.write_page(root / "alpha.png")
        manifest = self.root / "pairs.json"
        manifest.write_text(
            json.dumps({"pairs": [{"input": "alpha.png", "output": "alpha.png"}]}),
            encoding="utf-8",
        )
        original = validate_book.traverses_reparse_point
        with patch.object(
            validate_book,
            "traverses_reparse_point",
            side_effect=lambda path: Path(path).resolve() == manifest.resolve() or original(path),
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--pairing-manifest", str(manifest),
                    "--evidence-report", str(self.report),
                ]
            )
        self.assertEqual(2, result)
        self.assertFalse(self.report.exists())

    def test_large_edge_content_decrease_requires_review(self) -> None:
        source = self.image()
        source[:, :22] = 0
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=self.image())
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertTrue(pair["border_large_decrease_bands"])
        reasons = next(
            item["reasons"]
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.png"
        )
        self.assertTrue(any("large edge-content decrease" in reason for reason in reasons))

    def test_removed_connected_content_in_outer_eight_to_twelve_percent_fails(self) -> None:
        source = self.image()
        output = self.image()
        for page in (source, output):
            cv2.rectangle(page, (70, 45), (150, 255), 0, 2)
        source[27:31, 20:200] = 0
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=output)
        result, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertEqual(2, result)
        self.assertIn("top", pair["potential_removed_edge_content_sides"])
        self.assertIn("top", pair["removed_edge_content_failure_sides"])
        self.assertTrue(any(
            "high-confidence removed source edge content" in failure
            for failure in report["evidence"]["failures"]
        ))

    def test_physical_edge_crop_or_padding_remains_review_safe(self) -> None:
        source = self.image()
        for x in range(40, 181, 10):
            cv2.line(source, (x, 15), (x, 280), 0, 2)
        for y in range(50, 251, 20):
            cv2.line(source, (10, y), (210, y), 0, 2)
        source[:4, 10:210] = 0
        padded = source.copy()
        padded[:4, :] = 255
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=padded)
        _, report = self.run_validator()
        pair = report["evidence"]["pairs"][0]
        self.assertIn("top", pair["potential_removed_edge_content_sides"])
        self.assertNotIn("top", pair["removed_edge_content_failure_sides"])
        self.assertFalse(any(
            "high-confidence removed source edge content" in failure
            for failure in report["evidence"]["failures"]
        ))
        reasons = next(
            item["reasons"]
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "page1.png"
        )
        self.assertTrue(any("crop/padding versus removed page content" in reason
                            for reason in reasons))

    def test_report_target_policy_rejects_unsafe_or_existing_targets(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        bad_extension = self.root / "report.txt"
        self.assertEqual(
            2,
            validate_book.main(
                [str(self.source), str(self.output), "--evidence-report", str(bad_extension)]
            ),
        )
        self.assertFalse(bad_extension.exists())
        inside = self.output / "report.json"
        self.assertEqual(
            2,
            validate_book.main(
                [str(self.source), str(self.output), "--evidence-report", str(inside)]
            ),
        )
        self.assertFalse(inside.exists())
        self.report.write_text("occupied", encoding="utf-8")
        self.assertEqual(
            2,
            validate_book.main(
                [str(self.source), str(self.output), "--evidence-report", str(self.report)]
            ),
        )
        self.assertEqual("occupied", self.report.read_text(encoding="utf-8"))

    def test_approval_hash_becomes_stale_after_file_mutation(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Test Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        mutated = self.image()
        cv2.circle(mutated, (110, 150), 35, 0, 5)
        self.write_page(self.output / "page1.png", image=mutated)
        self.final_report.unlink(missing_ok=True)
        result = validate_book.main(
            [
                str(self.source), str(self.output),
                "--evidence-report", str(self.report),
                "--approval", str(approval),
                "--final-report", str(self.final_report),
            ]
        )
        self.assertEqual(2, result)
        report = json.loads(self.final_report.read_text(encoding="utf-8"))
        self.assertIn(
            "current mechanical evidence does not match the preserved evidence report",
            report["approval_errors"],
        )

    def test_transient_replace_restore_cannot_mix_evidence_bytes(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        original = (self.source / "page1.png").read_bytes()
        replacement_page = np.full((180, 140), 127, np.uint8)
        success, replacement = cv2.imencode(".png", replacement_page)
        self.assertTrue(success)
        original_metrics = validate_book.metrics
        replaced = False

        def replace_restore(
            path: Path,
            maximum_decoded_pixels: int = validate_book.SAFETY_BUDGET_DEFAULTS[
                "maximum_decoded_pixels_per_page"
            ],
            data: bytes | None = None,
        ) -> tuple[dict[str, object], dict[str, object]]:
            nonlocal replaced
            if path == self.source / "page1.png" and not replaced:
                replaced = True
                path.write_bytes(replacement.tobytes())
                try:
                    return original_metrics(path, maximum_decoded_pixels, data)
                finally:
                    path.write_bytes(original)
            return original_metrics(path, maximum_decoded_pixels, data)

        with patch.object(validate_book, "metrics", side_effect=replace_restore):
            result = validate_book.main(
                [
                    str(self.source),
                    str(self.output),
                    "--evidence-report",
                    str(self.report),
                ]
            )

        self.assertEqual(2, result)
        report = json.loads(self.report.read_text(encoding="utf-8"))
        source_row = report["evidence"]["input_pages"][0]
        self.assertEqual([220, 300], [source_row["width"], source_row["height"]])
        self.assertEqual(
            validate_book.hashlib.sha256(original).hexdigest(),
            source_row["sha256"],
        )

    def test_mutation_immediately_before_final_publication_aborts(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        original_snapshot = validate_book.filesystem_snapshot
        calls = 0

        def mutate_on_final(*args: object, **kwargs: object) -> dict[str, object]:
            nonlocal calls
            calls += 1
            if calls == 2:
                changed = self.image()
                cv2.circle(changed, (110, 150), 20, 0, 3)
                self.write_page(self.output / "page1.png", image=changed)
            return original_snapshot(*args, **kwargs)

        with patch.object(
            validate_book, "filesystem_snapshot", side_effect=mutate_on_final
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )
        self.assertEqual(2, result)
        self.assertFalse(self.final_report.exists())

    def _removed_mutation_during_atomic_create_json_removes_final_report(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        original = validate_book.atomic_create_json

        def mutate_after_link(
            path: Path, document: dict[str, object], maximum_bytes: int
        ) -> validate_book.PublishedJSON:
            published = original(path, document, maximum_bytes)
            approval.write_text('{"mutated":true}', encoding="utf-8")
            return published

        with patch.object(
            validate_book, "atomic_create_json", side_effect=mutate_after_link
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )

        self.assertEqual(2, result)
        self.assertFalse(self.final_report.exists())

    def _removed_concurrent_growth_value_error_after_passed_final_publication_removes_report(
        self,
    ) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        original_atomic_create = validate_book.atomic_create_json
        original_open = Path.open
        final_published = threading.Event()
        raced = threading.Event()
        target = (self.output / "page1.png").resolve()

        def grow() -> None:
            with original_open(target, "ab") as stream:
                stream.write(b"x")

        class GrowingStream:
            def __init__(self, stream: object) -> None:
                self.stream = stream

            def __enter__(self) -> GrowingStream:
                return self

            def __exit__(self, *args: object) -> None:
                self.stream.close()

            def read(self, size: int = -1) -> bytes:
                if not raced.is_set():
                    worker = threading.Thread(target=grow)
                    worker.start()
                    worker.join()
                    raced.set()
                return self.stream.read(size)

        def open_with_post_publication_growth(
            path: Path, *args: object, **kwargs: object
        ) -> object:
            stream = original_open(path, *args, **kwargs)
            if (
                final_published.is_set()
                and Path(path).resolve() == target
                and args
                and args[0] == "rb"
            ):
                return GrowingStream(stream)
            return stream

        def publish_then_enable_growth(
            path: Path, document: dict[str, object], maximum_bytes: int
        ) -> validate_book.PublishedJSON:
            published = original_atomic_create(path, document, maximum_bytes)
            if document.get("passed") is True:
                final_published.set()
            return published

        with (
            patch.object(Path, "open", new=open_with_post_publication_growth),
            patch.object(
                validate_book,
                "atomic_create_json",
                side_effect=publish_then_enable_growth,
            ),
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )

        self.assertTrue(final_published.is_set())
        self.assertTrue(raced.is_set())
        self.assertEqual(2, result)
        self.assertFalse(self.final_report.exists())

    def test_approval_mutation_immediately_before_final_publication_aborts(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval.write_text(
            json.dumps(
                {
                    "evidence_hash": initial["evidence_hash"],
                    "reviewer": "Reviewer",
                    "note": "Reviewed.",
                    "pages": [
                        {
                            "file": item["file"],
                            "required_reasons": item["reasons"],
                            "acknowledged_reasons": item["reasons"],
                        }
                        for item in initial["evidence"]["visual_review_pages"]
                    ],
                }
            ),
            encoding="utf-8",
        )
        original = validate_book.captured_file_still_published
        calls = 0

        def mutate_approval(captured: validate_book.ImmutableFile) -> bool:
            nonlocal calls
            calls += 1
            if calls == 2:
                approval.write_text('{"mutated":true}', encoding="utf-8")
            return original(captured)

        with patch.object(
            validate_book,
            "captured_file_still_published",
            side_effect=mutate_approval,
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )
        self.assertEqual(2, result)
        self.assertFalse(self.final_report.exists())

    def _removed_same_size_mtime_restored_in_place_approval_rewrite_aborts(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval_data = {
            "evidence_hash": initial["evidence_hash"],
            "reviewer": "Reviewer",
            "note": "Reviewed.",
            "pages": [
                {
                    "file": item["file"],
                    "required_reasons": item["reasons"],
                    "acknowledged_reasons": item["reasons"],
                }
                for item in initial["evidence"]["visual_review_pages"]
            ],
        }
        original_bytes = json.dumps(approval_data).encode("utf-8")
        approval.write_bytes(original_bytes)
        original = validate_book.captured_file_still_published
        rewritten = False

        def rewrite_in_place(captured: validate_book.ImmutableFile) -> bool:
            nonlocal rewritten
            if captured.path == approval.resolve() and not rewritten:
                rewritten = True
                status = approval.stat()
                replacement = original_bytes.replace(b"Reviewer", b"Attack!!", 1)
                self.assertEqual(len(original_bytes), len(replacement))
                with approval.open("r+b", buffering=0) as stream:
                    stream.write(replacement)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.utime(
                    approval,
                    ns=(status.st_atime_ns, status.st_mtime_ns),
                )
            return original(captured)

        with patch.object(
            validate_book,
            "captured_file_still_published",
            side_effect=rewrite_in_place,
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )
        self.assertTrue(rewritten)
        self.assertEqual(2, result)
        self.assertFalse(self.final_report.exists())

    def _removed_manifest_replace_read_restore_uses_one_immutable_capture(self) -> None:
        for name in ("alpha.png", "beta.png"):
            self.write_page(self.source / name)
            self.write_page(self.output / name)
        manifest = self.root / "pairs.json"
        original_bytes = json.dumps(
            {
                "pairs": [
                    {"input": "alpha.png", "output": "alpha.png"},
                    {"input": "beta.png", "output": "beta.png"},
                ]
            }
        ).encode()
        replacement_bytes = json.dumps(
            {
                "pairs": [
                    {"input": "alpha.png", "output": "beta.png"},
                    {"input": "beta.png", "output": "alpha.png"},
                ]
            }
        ).encode()
        manifest.write_bytes(original_bytes)
        original_capture = validate_book.capture_regular_file
        raced = False
        replacement_blocked = False

        def replace_read_restore(
            path: Path, maximum_bytes: int
        ) -> validate_book.ImmutableFile:
            nonlocal raced, replacement_blocked
            captured = original_capture(path, maximum_bytes)
            if Path(path) == manifest and not raced:
                raced = True
                held = self.root / "held-manifest.json"
                try:
                    os.replace(manifest, held)
                    manifest.write_bytes(replacement_bytes)
                    os.replace(held, manifest)
                except PermissionError:
                    replacement_blocked = True
            return captured

        with patch.object(
            validate_book, "capture_regular_file", side_effect=replace_read_restore
        ):
            _, report = self.run_validator(
                "--pairing-manifest", str(manifest)
            )

        self.assertTrue(replacement_blocked)
        self.assertEqual(
            [
                {"input": "alpha.png", "output": "alpha.png"},
                {"input": "beta.png", "output": "beta.png"},
            ],
            report["evidence"]["pairing"]["map"],
        )
        self.assertEqual(
            validate_book.hashlib.sha256(original_bytes).hexdigest(),
            report["evidence"]["pairing"]["manifest_sha256"],
        )

    def _removed_evidence_and_approval_replace_read_restore_use_captured_bytes(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        _, initial = self.run_validator()
        approval = self.root / "approval.json"
        approval_data = {
            "evidence_hash": initial["evidence_hash"],
            "reviewer": "Reviewer",
            "note": "Reviewed.",
            "pages": [
                {
                    "file": item["file"],
                    "required_reasons": item["reasons"],
                    "acknowledged_reasons": item["reasons"],
                }
                for item in initial["evidence"]["visual_review_pages"]
            ],
        }
        approval.write_text(json.dumps(approval_data), encoding="utf-8")
        original_capture = validate_book.capture_regular_file
        raced: set[Path] = set()
        blocked: set[Path] = set()

        def replace_read_restore(
            path: Path, maximum_bytes: int
        ) -> validate_book.ImmutableFile:
            captured = original_capture(path, maximum_bytes)
            target = Path(path)
            if target in {self.report, approval} and target not in raced:
                raced.add(target)
                held = self.root / f"held-{target.name}"
                try:
                    os.replace(target, held)
                    target.write_text('{"attacker":true}', encoding="utf-8")
                    os.replace(held, target)
                except PermissionError:
                    blocked.add(target)
            return captured

        with patch.object(
            validate_book, "capture_regular_file", side_effect=replace_read_restore
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                    "--approval", str(approval),
                    "--final-report", str(self.final_report),
                ]
            )

        self.assertEqual(0, result)
        self.assertEqual({self.report, approval}, blocked)
        final = json.loads(self.final_report.read_text(encoding="utf-8"))
        self.assertEqual(approval_data, final["approval"])
        self.assertEqual(initial["evidence_hash"], final["evidence_hash"])

    def test_compact_features_and_rotation_signatures_are_precomputed_once(self) -> None:
        page_count = 8
        for index in range(page_count):
            page = np.full((900, 700), 255, np.uint8)
            cv2.putText(page, str(index), (80, 450), cv2.FONT_HERSHEY_SIMPLEX, 5, 0, 12)
            self.write_page(self.source / f"page{index}.png", image=page)
            self.write_page(self.output / f"page{index}.png", image=page)
        original = validate_book.content_signature
        with patch.object(validate_book, "content_signature", wraps=original) as signature:
            self.run_validator()
        self.assertEqual(page_count * 2 * 8, signature.call_count)
        _, features = validate_book.metrics(self.source / "page0.png")
        arrays: list[np.ndarray] = []

        def collect(value: object) -> None:
            if isinstance(value, np.ndarray):
                arrays.append(value)
            elif isinstance(value, dict):
                for nested in value.values():
                    collect(nested)

        collect(features)
        self.assertTrue(arrays)
        self.assertLessEqual(max(array.size for array in arrays), 256 * 256)

    def test_foreground_masks_are_computed_once_per_resolution(self) -> None:
        page = np.full((1200, 900), 255, np.uint8)
        for y in range(80, 1120, 24):
            cv2.line(page, (70, y), (830, y), 0, 2)
        self.write_page(self.source / "page0.png", image=page)
        original = validate_book.adaptive_foreground_mask
        with patch.object(
            validate_book, "adaptive_foreground_mask", wraps=original
        ) as foreground:
            validate_book.metrics(self.source / "page0.png")
        self.assertEqual(2, foreground.call_count)

    def test_dense_foreground_cleanup_supports_72_page_batch_timeout(self) -> None:
        raw = np.zeros((1800, 1400), np.uint8)
        for y in range(0, raw.shape[0] - 5, 10):
            for x in range(0, raw.shape[1] - 5, 10):
                raw[y:y + 5, x:x + 5] = 1

        started = time.perf_counter()
        cleaned = validate_book.clean_foreground_components(raw)
        elapsed = time.perf_counter() - started

        self.assertTrue(np.array_equal(raw, cleaned))
        self.assertLess(elapsed, 5.0)
        self.assertLess(elapsed * 72 * 2, 1800.0)

    def test_rejects_page_and_decoded_workload_budgets_before_analysis(self) -> None:
        for index in range(2):
            self.write_page(self.source / f"page{index}.png")
            self.write_page(self.output / f"page{index}.png")
        result, report = self.run_validator("--max-pages", "1")
        self.assertEqual(2, result)
        budgets = report["evidence"]["safety_budgets"]
        self.assertTrue(budgets["rejected"]["page_count"])
        self.assertIn(
            "page-count safety budget exceeded before decoding",
            "\n".join(report["evidence"]["failures"]),
        )
        self.assertTrue(all(row is None for row in report["evidence"]["input_pages"]))

        result, report = self.run_validator(
            "--max-decoded-pixels-per-page", "1000"
        )
        self.assertEqual(2, result)
        self.assertTrue(
            report["evidence"]["safety_budgets"]["rejected"][
                "decoded_pixels_per_page"
            ]
        )
        self.assertIn(
            "decoded pixel safety budget exceeded before allocation",
            "\n".join(report["evidence"]["failures"]),
        )

        result, report = self.run_validator(
            "--max-total-compact-feature-pixels", "100000"
        )
        self.assertEqual(2, result)
        self.assertTrue(
            report["evidence"]["safety_budgets"]["rejected"][
                "total_compact_feature_workload"
            ]
        )

        result, report = self.run_validator("--max-retained-feature-bytes", "1")
        self.assertEqual(2, result)
        self.assertTrue(
            report["evidence"]["safety_budgets"]["rejected"][
                "retained_feature_memory"
            ]
        )
        self.assertEqual(
            0,
            report["evidence"]["safety_budgets"]["observed"][
                "retained_feature_bytes"
            ],
        )

    def test_encoded_pages_are_streamed_under_aggregate_peak_budget(self) -> None:
        encoded_sizes: list[int] = []
        for index in range(6):
            page = np.full((700, 500), 255, np.uint8)
            cv2.putText(
                page, str(index), (100, 420), cv2.FONT_HERSHEY_SIMPLEX, 5, 0, 10
            )
            for directory in (self.source, self.output):
                path = directory / f"page{index}.png"
                self.write_page(path, image=page)
                encoded_sizes.append(path.stat().st_size)

        original = validate_book.capture_image_file
        live_bytes = 0
        peak_live_bytes = 0

        def tracked_capture(path: Path, maximum_bytes: int) -> object:
            nonlocal live_bytes, peak_live_bytes
            captured = original(path, maximum_bytes)
            live_bytes += len(captured.data)
            peak_live_bytes = max(peak_live_bytes, live_bytes)
            original_close = captured.close
            closed = False

            def close() -> None:
                nonlocal live_bytes, closed
                if not closed:
                    closed = True
                    live_bytes -= len(captured.data)
                original_close()

            captured.close = close
            return captured

        with patch.object(
            validate_book, "capture_image_file", side_effect=tracked_capture
        ):
            _, report = self.run_validator(
                "--max-peak-encoded-buffer-bytes",
                str(validate_book.estimated_peak_encoded_buffer_bytes(max(encoded_sizes))),
            )

        budgets = report["evidence"]["safety_budgets"]
        self.assertEqual(0, live_bytes)
        self.assertLessEqual(peak_live_bytes, max(encoded_sizes))
        self.assertEqual(
            validate_book.estimated_peak_encoded_buffer_bytes(max(encoded_sizes)),
            budgets["observed"]["peak_encoded_buffer_bytes"],
        )
        self.assertFalse(budgets["rejected"]["peak_encoded_buffer_memory"])

    def test_near_cap_encoded_peak_estimator_rejects_decoder_copy_allowance(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        encoded_size = (self.source / "page1.png").stat().st_size
        result, report = self.run_validator(
            "--max-peak-encoded-buffer-bytes", str(encoded_size)
        )
        self.assertEqual(2, result)
        budgets = report["evidence"]["safety_budgets"]
        self.assertEqual(
            validate_book.estimated_peak_encoded_buffer_bytes(encoded_size),
            budgets["observed"]["peak_encoded_buffer_bytes"],
        )
        self.assertTrue(budgets["rejected"]["peak_encoded_buffer_memory"])

    def test_rejects_comparison_budgets_and_reports_indexing(self) -> None:
        for index in range(2):
            self.write_page(self.source / f"page{index}.png")
            self.write_page(self.output / f"page{index}.png")
        result, report = self.run_validator(
            "--max-cross-match-comparisons", "3",
            "--max-duplicate-comparisons", "1",
        )
        self.assertEqual(2, result)
        budgets = report["evidence"]["safety_budgets"]
        self.assertTrue(budgets["rejected"]["cross_match_comparisons"])
        self.assertFalse(budgets["rejected"]["duplicate_comparisons"])
        self.assertEqual([], report["evidence"]["decoded_identity_summary"])
        self.assertTrue(
            budgets["comparison_indexing"]["exact_decoded_sha256_index"]
        )

    def test_early_component_budget_error_preserves_pair_path_alignment(
        self,
    ) -> None:
        first = self.image()
        cv2.putText(
            first, "ONE", (35, 170), cv2.FONT_HERSHEY_SIMPLEX, 1.4, 0, 4
        )
        second = self.image()
        cv2.putText(
            second, "TWO", (35, 170), cv2.FONT_HERSHEY_SIMPLEX, 1.4, 0, 4
        )
        self.write_page(self.source / "page1.png", image=first)
        self.write_page(self.source / "page2.png", image=second)
        self.write_page(self.output / "page1.png", image=first)
        self.write_page(self.output / "page2.png", image=first)

        original_compare = validate_book.compare_edge_component_analysis
        comparison_count = 0

        def fail_first_comparison(*args: object, **kwargs: object) -> object:
            nonlocal comparison_count
            comparison_count += 1
            if comparison_count == 1:
                raise validate_book.ComponentBudgetError(
                    "forced first-pair edge comparison budget failure"
                )
            return original_compare(*args, **kwargs)

        with patch.object(
            validate_book,
            "compare_edge_component_analysis",
            side_effect=fail_first_comparison,
        ):
            result, report = self.run_validator()

        self.assertEqual(2, result)
        self.assertEqual(2, comparison_count)
        evidence = report["evidence"]
        self.assertEqual(
            [False, True],
            [pair["comparable"] for pair in evidence["pairs"]],
        )
        self.assertEqual(
            ("page2.png", "page2.png"),
            (
                evidence["pairs"][1]["input_file"],
                evidence["pairs"][1]["output_file"],
            ),
        )
        second_identity = next(
            item
            for item in evidence["decoded_identity_summary"]
            if item["output"] == "page2.png"
        )
        self.assertEqual("page2.png", second_identity["mapped_source"])
        self.assertEqual(
            evidence["output_pages"][0]["decoded_content_sha256"],
            evidence["output_pages"][1]["decoded_content_sha256"],
        )
        self.assertEqual(
            0,
            evidence["safety_budgets"]["observed"][
                "performed_duplicate_comparisons"
            ],
        )
        self.assertEqual(
            0, evidence["duplicate_summary"]["candidate_pair_count"]
        )

    def test_cross_identity_evidence_is_linear_and_keeps_decisive_candidates(self) -> None:
        page_count = 5
        for index in range(page_count):
            page = self.image()
            cv2.putText(
                page, str(index), (70, 180), cv2.FONT_HERSHEY_SIMPLEX, 2, 0, 4
            )
            self.write_page(self.source / f"page{index}.png", image=page)
            self.write_page(self.output / f"page{index}.png", image=page)

        _, report = self.run_validator()
        evidence = report["evidence"]
        self.assertNotIn("decoded_identity_matrix", evidence)
        self.assertEqual(page_count, len(evidence["decoded_identity_summary"]))
        for summary in evidence["decoded_identity_summary"]:
            self.assertEqual(page_count, summary["evaluated_source_count"])
            self.assertLessEqual(
                summary["retained_candidate_count"],
                validate_book.MAXIMUM_RETAINED_IDENTITY_CANDIDATES_PER_OUTPUT
                + 1,
            )
        for pair in evidence["pairs"]:
            identity = pair["cross_page_identity"]
            self.assertNotIn("all_source_scores", identity)
            self.assertIn("paired_similarity", identity)
            self.assertIn("strongest_alternate_similarity", identity)
        retention = evidence["identity_evidence_retention"]
        self.assertFalse(retention["full_cross_score_matrix_serialized"])
        self.assertTrue(retention["routine_candidate_scores_truncated"])

    def test_duplicate_evidence_truncation_requires_batch_review(self) -> None:
        pages = []
        for index in range(3):
            page = self.image()
            cv2.putText(
                page, str(index), (70, 180), cv2.FONT_HERSHEY_SIMPLEX, 2, 0, 4
            )
            pages.append(page)
            self.write_page(self.source / f"page{index}.png", image=page)
            self.write_page(self.output / f"page{index}.png", image=pages[0])

        with patch.object(
            validate_book, "MAXIMUM_RETAINED_DUPLICATE_DECISIONS", 1
        ):
            _, report = self.run_validator()
        retention = report["evidence"]["identity_evidence_retention"]
        self.assertGreater(retention["omitted_duplicate_decision_count"], 0)
        self.assertFalse(report["mechanical_pass"])
        batch_review = next(
            item
            for item in report["evidence"]["visual_review_pages"]
            if item["file"] == "__batch_identity__"
        )
        self.assertTrue(
            any("exceeded the retained limit" in reason for reason in batch_review["reasons"])
        )

    def test_duplicate_heavy_500_page_report_and_approval_stay_compact(self) -> None:
        self.write_page(self.source / "page0.png")
        self.write_page(self.output / "page0.png")
        _, report = self.run_validator()
        modeled = copy.deepcopy(report)
        evidence = modeled["evidence"]
        input_row = evidence["input_pages"][0]
        output_row = evidence["output_pages"][0]
        pair = evidence["pairs"][0]
        identity = evidence["decoded_identity_summary"][0]
        input_entry = evidence["candidate_evidence"]["input"]["top_level_files"][0]
        output_entry = evidence["candidate_evidence"]["output"]["top_level_files"][0]

        evidence["input_pages"] = [copy.deepcopy(input_row) for _ in range(500)]
        evidence["output_pages"] = [copy.deepcopy(output_row) for _ in range(500)]
        evidence["pairs"] = [copy.deepcopy(pair) for _ in range(500)]
        evidence["decoded_identity_summary"] = [
            copy.deepcopy(identity) for _ in range(500)
        ]
        evidence["candidate_evidence"]["input"]["top_level_files"] = [
            copy.deepcopy(input_entry) for _ in range(500)
        ]
        evidence["candidate_evidence"]["input"]["all_entries"] = [
            copy.deepcopy(input_entry) for _ in range(500)
        ]
        evidence["candidate_evidence"]["output"]["top_level_files"] = [
            copy.deepcopy(output_entry) for _ in range(500)
        ]
        evidence["candidate_evidence"]["output"]["all_entries"] = [
            copy.deepcopy(output_entry) for _ in range(500)
        ]
        evidence["duplicate_decisions"] = [
            {
                "left_output": "page000.png",
                "right_output": "page001.png",
                "source_equality_established": False,
                "output_structure_similarity": 1.0,
                "output_pixel_similarity": 1.0,
                "output_color_similarity": 1.0,
            }
            for _ in range(validate_book.MAXIMUM_RETAINED_DUPLICATE_DECISIONS)
        ]
        evidence["duplicate_summary"] = {
            "candidate_pair_count": 124750,
            "mechanical_failure_pair_count": 124750,
            "source_exactly_equal_review_only_pair_count": 0,
            "affected_output_count": 500,
            "failure_examples": [
                f"page000.png~page{index:03}.png"
                for index in range(1, 9)
            ],
            "review_only_examples": [],
            "maximum_examples_per_category": (
                validate_book.MAXIMUM_DUPLICATE_SUMMARY_EXAMPLES
            ),
        }
        evidence["visual_review_pages"] = [
            {
                "file": "__batch_identity__",
                "reasons": [
                    "grouped duplicate review: 124750 candidate pairs across "
                    "500 outputs; inspect retained decisive evidence"
                ],
            }
        ]
        evidence["pairing"]["input_order"] = [
            f"page{index:03}.png" for index in range(500)
        ]
        evidence["pairing"]["output_order"] = [
            f"page{index:03}.png" for index in range(500)
        ]
        evidence["pairing"]["map"] = [
            {
                "input": f"page{index:03}.png",
                "output": f"page{index:03}.png",
            }
            for index in range(500)
        ]
        modeled["approval_template"]["pages"] = [
            {
                "file": item["file"],
                "required_reasons": item["reasons"],
                "acknowledged_reasons": [],
            }
            for item in evidence["visual_review_pages"]
        ]
        encoded_size = len(
            (json.dumps(modeled, indent=2) + "\n").encode("utf-8")
        )
        self.assertLess(encoded_size, validate_book.MAXIMUM_EVIDENCE_JSON_BYTES)
        approval = {
            **modeled["approval_template"],
            "reviewer": "Regression Reviewer",
            "note": "Reviewed the grouped duplicate evidence.",
        }
        approval["pages"][0]["acknowledged_reasons"] = approval["pages"][0][
            "required_reasons"
        ]
        approval_size = len(
            (json.dumps(approval, indent=2) + "\n").encode("utf-8")
        )
        self.assertLess(approval_size, validate_book.MAXIMUM_APPROVAL_JSON_BYTES)
        final_size = len(
            (
                json.dumps(
                    {
                        "schema_version": 17,
                        "report_type": "final_quality_control",
                        "evidence_report": "mechanical-evidence.json",
                        "evidence_hash": modeled["evidence_hash"],
                        "approval_file": "approval.json",
                        "approval": approval,
                        "mechanical_pass": False,
                        "visual_review_approved": True,
                        "approval_errors": [],
                        "passed": False,
                    },
                    indent=2,
                )
                + "\n"
            ).encode("utf-8")
        )
        self.assertLess(final_size, validate_book.MAXIMUM_FINAL_JSON_BYTES)

    def test_runner_rejects_script_traversal_before_runtime_setup(self) -> None:
        skill_base = Path(__file__).resolve().parents[1]
        completed = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", str(skill_base / "scripts" / "run.ps1"),
                "..\\SKILL.md",
            ],
            cwd=skill_base,
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertNotEqual(0, completed.returncode)
        self.assertIn(
            "Script must be exactly validate_book.py or tests/test_validate_book.py",
            completed.stderr,
        )

    def test_runner_uses_practical_isolated_pinned_runtime(self) -> None:
        runner = (
            Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        ).read_text(encoding="utf-8")
        test_runner = (
            Path(__file__).resolve().parents[1] / "tests" / "run.ps1"
        ).read_text(encoding="utf-8")
        self.assertIn('Programs\\AzureAuth\\0.9.5\\azureauth.exe', runner)
        self.assertIn('".runtime-" + [Guid]::NewGuid()', runner)
        self.assertIn('$requiredPython = "3.12.11"', runner)
        self.assertIn("MISE_IGNORED_CONFIG_PATHS", runner)
        self.assertRegex(
            runner,
            (
                r"(?s)\$miseExecArguments\s*=\s*@\(\s*"
                r'"--no-config"\s*,\s*"exec"\s*,\s*'
                r'"python@\$requiredPython"\s*,\s*"--"\s*\)'
            ),
        )
        runtime_creation = runner[
            runner.index("$miseResult =") : runner.index(
                "if ($miseResult.ExitCode"
            )
        ]
        self.assertRegex(
            runtime_creation,
            (
                r"(?s)-ArgumentList\s*\(\s*\$miseExecArguments\s*\+\s*@\(\s*"
                r'"python"\s*,\s*"-I"\s*,\s*"-m"\s*,\s*"venv"\s*,\s*'
                r"\$runtime\s*\)\s*\)"
            ),
        )
        version_verification = runner[
            runner.index("$versionResult =") : runner.index(
                "if ($versionResult.ExitCode"
            )
        ]
        self.assertRegex(
            version_verification,
            r'(?s)-ArgumentList\s*@\(\s*"-I"\s*,\s*"-B"\s*,\s*"-c"\s*,',
        )
        self.assertIn("--require-hashes", runner)
        self.assertIn("--no-cache-dir", runner)
        self.assertIn("--no-deps", runner)
        self.assertIn("--only-binary=:all:", runner)
        requirements = (
            Path(__file__).resolve().parents[1] / "scripts" / "requirements.lock"
        ).read_text(encoding="utf-8")
        self.assertIn("Pillow==12.3.0", requirements)
        self.assertIn('$env:PIP_CONFIG_FILE = "nul"', runner)
        self.assertIn("Reset-PipEnvironment", runner)
        self.assertIn("-imatch '^PIP_'", runner)
        self.assertNotIn('"--index-url", $luciaIndex', runner)
        self.assertIn("-EnvironmentVariables @{ PIP_INDEX_URL = $luciaIndex }", runner)
        self.assertNotIn("$env:PIP_INDEX_URL =", runner)
        self.assertIn("$startInfo.EnvironmentVariables.Remove", runner)
        dependency_verification = runner[
            runner.index("$dependencyResult =") : runner.index(
                "if ($dependencyResult.ExitCode"
            )
        ]
        self.assertRegex(
            dependency_verification,
            (
                r"(?s)-ArgumentList\s*@\(\s*"
                r'"-I"\s*,\s*"-B"\s*,\s*"-c"\s*,\s*\$verifyDependencies\s*\)'
            ),
        )
        target_invocation = runner[
            runner.index("$validatorResult =") : runner.index(
                "if ($validatorResult.StdOut)"
            )
        ]
        self.assertRegex(
            target_invocation,
            (
                r"(?s)-ArgumentList\s*\(\s*@\(\s*"
                r'"-I"\s*,\s*"-B"\s*,\s*\$target\s*\)\s*\+\s*'
                r"\$ScriptArgs\s*\)"
            ),
        )
        self.assertIn(
            'Join-Path $env:SystemRoot "System32\\taskkill.exe"', runner
        )
        self.assertIn(
            "$taskkillProcess.StartInfo.FileName = $taskkill", runner
        )
        self.assertIn(
            '$Script -cnotin @("validate_book.py", "tests/test_validate_book.py")',
            runner,
        )
        self.assertIn(
            'Join-Path $skillBase "tests\\test_validate_book.py"',
            runner,
        )
        self.assertEqual(requirements.count("--hash=sha256:"), 3)
        self.assertIn(
            "c1f9540be57940698ed329904db803cf7a402f3fc200bfe599334c9bd84a40b2",
            requirements,
        )
        self.assertIn(
            "86b413bdd6c6bf497832e346cd5371995de148e579b9774f8eba686dee3f5528",
            requirements,
        )
        self.assertIn(
            "a2b55dd6b2a4c4b7d87ffa56bdb33fdc5fdb9a462173861a7bc097f17d91cb09",
            requirements,
        )
        self.assertIn("allow_nan=False", (
            Path(__file__).resolve().parents[1] / "scripts" / "validate_book.py"
        ).read_text(encoding="utf-8"))
        self.assertIn(
            '& (Join-Path $skillBase "scripts\\run.ps1")',
            test_runner,
        )
        self.assertIn('"tests/test_validate_book.py" "-v"', test_runner)
        self.assertIn("Push-Location $skillBase", test_runner)
        self.assertIn("Pop-Location", test_runner)
        self.assertNotIn("mise", test_runner)

    def test_runner_cleanup_is_limited_to_invocation_owned_paths(self) -> None:
        runner = (
            Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        ).read_text(encoding="utf-8")
        self.assertIn("Remove-Item -LiteralPath $runtime -Recurse -Force", runner)
        self.assertNotIn("$invocationTemp", runner)
        self.assertNotIn('-Filter "__pycache__"', runner)
        self.assertNotIn(
            "Get-ChildItem -LiteralPath $skillBase -Directory",
            runner,
        )

    def test_runner_timeout_path_has_only_bounded_cleanup_waits(self) -> None:
        runner = (
            Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        ).read_text(encoding="utf-8")
        helper_start = runner.index("function Invoke-ProcessWithTimeout")
        helper_end = runner.index("\nfunction Reset-PipEnvironment", helper_start)
        helper = runner[helper_start:helper_end]
        timeout_start = helper.index(
            "if (-not $process.WaitForExit($TimeoutSeconds * 1000))"
        )
        success_wait = helper.index(
            "\n        $process.WaitForExit()\n", timeout_start
        )
        timeout_path = helper[timeout_start:success_wait]

        self.assertIn("$timeoutCleanupGraceMilliseconds = 5000", timeout_path)
        self.assertIn("[Diagnostics.Stopwatch]::StartNew()", timeout_path)
        self.assertIn(
            "$taskkillProcess = [Diagnostics.Process]::new()", timeout_path
        )
        self.assertIn(
            "$taskkillProcess.WaitForExit(\n"
            "                                $taskkillWaitMilliseconds",
            timeout_path,
        )
        self.assertIn(
            "$process.WaitForExit(\n"
            "                    $remainingCleanupMilliseconds",
            timeout_path,
        )
        self.assertIn("$TargetProcess.Kill()", timeout_path)
        self.assertIn("& $stopProcess $taskkillProcess", timeout_path)
        self.assertIn("$process.WaitForExit(0)", timeout_path)
        self.assertIn(
            "process-tree termination could not be confirmed", timeout_path
        )
        self.assertNotIn("& $taskkill", timeout_path)
        self.assertNotIn("-ErrorAction SilentlyContinue", timeout_path)
        self.assertNotIn("$process.WaitForExit()", timeout_path)
        self.assertNotIn("$taskkillProcess.WaitForExit()", timeout_path)
        self.assertEqual(1, helper.count("$process.WaitForExit()"))

    def test_runner_timeout_helper_terminates_process(self) -> None:
        runner = Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        helper_test = self.root / "timeout-helper-test.ps1"
        parent_test = self.root / "timeout-parent-test.ps1"
        parent_pid_file = self.root / "timeout-parent.pid"
        child_pid_file = self.root / "timeout-child.pid"
        parent_test.write_text(
            """
param([string]$ParentPidFile, [string]$ChildPidFile)
Set-Content -LiteralPath $ParentPidFile -Value $PID
$child = Start-Process -FilePath "powershell.exe" `
    -ArgumentList @("-NoProfile", "-Command", "Start-Sleep -Seconds 30") `
    -PassThru
Set-Content -LiteralPath $ChildPidFile -Value $child.Id
Start-Sleep -Seconds 30
""".strip(),
            encoding="utf-8",
        )
        helper_test.write_text(
            """
param(
    [string]$Runner,
    [string]$ParentTest,
    [string]$ParentPidFile,
    [string]$ChildPidFile
)
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $Runner, [ref]$tokens, [ref]$errors
)
foreach ($name in @("ConvertTo-WindowsCommandLineArgument", "Invoke-ProcessWithTimeout")) {
    $function = $ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true) | Select-Object -First 1
    Invoke-Expression $function.Extent.Text
}
$parentId = $null
$childId = $null
try {
    try {
        Invoke-ProcessWithTimeout -FilePath "powershell.exe" `
            -ArgumentList @(
                "-NoProfile", "-File", $ParentTest,
                "-ParentPidFile", $ParentPidFile,
                "-ChildPidFile", $ChildPidFile
            ) `
            -TimeoutSeconds 3 -Description "test helper"
        throw "Timeout helper unexpectedly completed."
    }
    catch {
        if ($_.Exception.Message -notlike "*timed out after 3 seconds*") {
            throw
        }
    }
    if (-not (Test-Path -LiteralPath $ParentPidFile) -or
        -not (Test-Path -LiteralPath $ChildPidFile)) {
        throw "The timeout fixture did not record both process IDs."
    }
    $parentId = [int](Get-Content -LiteralPath $ParentPidFile)
    $childId = [int](Get-Content -LiteralPath $ChildPidFile)
    if (Get-Process -Id $parentId -ErrorAction SilentlyContinue) {
        throw "The timed-out parent process is still running."
    }
    if (Get-Process -Id $childId -ErrorAction SilentlyContinue) {
        throw "The timed-out child process is still running."
    }
}
finally {
    if ($null -eq $parentId -and
        (Test-Path -LiteralPath $ParentPidFile)) {
        $parentId = [int](Get-Content -LiteralPath $ParentPidFile)
    }
    if ($null -eq $childId -and
        (Test-Path -LiteralPath $ChildPidFile)) {
        $childId = [int](Get-Content -LiteralPath $ChildPidFile)
    }
    foreach ($processId in @($parentId, $childId)) {
        if ($null -ne $processId) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
    $cleanupTimer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $remaining = @(
            $parentId, $childId |
                Where-Object { $null -ne $_ } |
                ForEach-Object {
                    Get-Process -Id $_ -ErrorAction SilentlyContinue
                }
        )
        if ($remaining.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 50
    } while ($cleanupTimer.ElapsedMilliseconds -lt 3000)
}
exit 0
""".strip(),
            encoding="utf-8",
        )
        try:
            completed = subprocess.run(
                [
                    "powershell",
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", str(helper_test),
                    "-Runner", str(runner),
                    "-ParentTest", str(parent_test),
                    "-ParentPidFile", str(parent_pid_file),
                    "-ChildPidFile", str(child_pid_file),
                ],
                text=True,
                capture_output=True,
                check=False,
                timeout=20,
            )
        finally:
            if os.name == "nt":
                self.terminate_windows_processes(
                    parent_pid_file, child_pid_file
                )
        self.assertEqual(0, completed.returncode, completed.stderr)

    @unittest.skipUnless(
        os.name == "nt" and shutil.which("powershell"),
        "requires Windows PowerShell process fault injection",
    )
    def test_runner_timeout_helper_fails_when_tree_kill_is_unavailable(self) -> None:
        runner = Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        helper_test = self.root / "timeout-fault-helper-test.ps1"
        parent_test = self.root / "timeout-fault-parent-test.ps1"
        parent_pid_file = self.root / "timeout-fault-parent.pid"
        missing_taskkill = self.root / "missing-taskkill.exe"
        parent_test.write_text(
            """
param([string]$ParentPidFile)
Set-Content -LiteralPath $ParentPidFile -Value $PID
Start-Sleep -Seconds 30
""".strip(),
            encoding="utf-8",
        )
        helper_test.write_text(
            """
param(
    [string]$Runner,
    [string]$ParentTest,
    [string]$ParentPidFile,
    [string]$MissingTaskkill
)
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $Runner, [ref]$tokens, [ref]$errors
)
foreach ($name in @("ConvertTo-WindowsCommandLineArgument", "Invoke-ProcessWithTimeout")) {
    $function = $ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true) | Select-Object -First 1
    $functionText = $function.Extent.Text
    if ($name -eq "Invoke-ProcessWithTimeout") {
        $taskkillExpression = 'Join-Path $env:SystemRoot "System32\\taskkill.exe"'
        if (-not $functionText.Contains($taskkillExpression)) {
            throw "The taskkill fault-injection seam was not found."
        }
        $functionText = $functionText.Replace(
            $taskkillExpression,
            '$MissingTaskkill'
        )
    }
    Invoke-Expression $functionText
}
try {
    Invoke-ProcessWithTimeout -FilePath "powershell.exe" `
        -ArgumentList @(
            "-NoProfile", "-File", $ParentTest,
            "-ParentPidFile", $ParentPidFile
        ) `
        -TimeoutSeconds 1 -Description "fault-injected helper"
    throw "Timeout helper unexpectedly completed."
}
catch [System.Management.Automation.RuntimeException] {
    if (
        $_.Exception.Message -notlike
        "*process-tree termination could not be confirmed*"
    ) {
        throw
    }
}
if (-not (Test-Path -LiteralPath $ParentPidFile)) {
    throw "The timeout fixture did not record its process ID."
}
exit 0
""".strip(),
            encoding="utf-8",
        )

        started = time.monotonic()
        try:
            completed = subprocess.run(
                [
                    "powershell",
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", str(helper_test),
                    "-Runner", str(runner),
                    "-ParentTest", str(parent_test),
                    "-ParentPidFile", str(parent_pid_file),
                    "-MissingTaskkill", str(missing_taskkill),
                ],
                text=True,
                capture_output=True,
                check=False,
                timeout=12,
            )
        finally:
            self.terminate_windows_processes(parent_pid_file)
        self.assertLess(time.monotonic() - started, 12)
        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_runner_preserves_spaced_and_apostrophe_arguments(self) -> None:
        runner = Path(__file__).resolve().parents[1] / "scripts" / "run.ps1"
        helper_test = self.root / "argument helper test.ps1"
        output = self.root / "captured arguments.json"
        helper_test.write_text(
            """
param([string]$Runner, [string]$Output)
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $Runner, [ref]$tokens, [ref]$errors
)
foreach ($name in @("ConvertTo-WindowsCommandLineArgument", "Invoke-ProcessWithTimeout")) {
    $function = $ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true) | Select-Object -First 1
    Invoke-Expression $function.Extent.Text
}
$code = "import json,sys;open(sys.argv[1],'w').write(json.dumps(sys.argv[2:]))"
$result = Invoke-ProcessWithTimeout -FilePath $env:PYTHON_EXECUTABLE `
    -ArgumentList @("-c", $code, $Output, "path with spaces", "reader's copy", "") `
    -TimeoutSeconds 5 -Description "argument preservation test"
exit $result.ExitCode
""".strip(),
            encoding="utf-8",
        )
        environment = os.environ.copy()
        environment["PYTHON_EXECUTABLE"] = sys.executable
        completed = subprocess.run(
            [
                "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", str(helper_test), "-Runner", str(runner), "-Output", str(output),
            ],
            text=True, capture_output=True, check=False, timeout=15, env=environment,
        )
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(
            ["path with spaces", "reader's copy", ""],
            json.loads(output.read_text(encoding="utf-8")),
        )

    def test_rejects_reparse_input_or_output_roots(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        original = validate_book.is_reparse_point
        with patch.object(
            validate_book,
            "is_reparse_point",
            side_effect=lambda path: path in {self.source, self.output} or original(path),
        ):
            result = validate_book.main(
                [
                    str(self.source), str(self.output),
                    "--evidence-report", str(self.report),
                ]
            )
        self.assertEqual(2, result)
        self.assertFalse(self.report.exists())

    def test_required_visual_categories_are_reported(self) -> None:
        blank = self.image()
        music = self.image()
        for y in range(50, 251, 20):
            cv2.line(music, (15, y), (205, y), 0, 2)
        for index, page in enumerate((music, blank, music), start=1):
            self.write_page(self.source / f"page{index}.png", image=page.copy())
            self.write_page(self.output / f"page{index}.png", image=page.copy())
        _, report = self.run_validator()
        categories = report["evidence"]["visual_review_categories"]
        self.assertEqual(["page1.png", "page3.png"], categories["cover_or_first_last"])
        self.assertIn("page2.png", categories["likely_blank"])
        self.assertTrue(categories["text_heavy_candidate"])
        self.assertTrue(categories["music_or_dense_structure_candidate"])
        self.assertIn("page2.png", categories["metric_flags"])
        self.assertEqual(
            {"page1.png", "page2.png", "page3.png"},
            set(categories["beginning_middle_end"]),
        )

    def test_fails_for_count_and_nested_directories(self) -> None:
        self.write_page(self.source / "page1.png")
        result, report = self.run_validator()
        self.assertEqual(2, result)
        self.assertTrue(any("page count differs" in item for item in report["evidence"]["failures"]))

        nested = self.source / "processed"
        nested.mkdir()
        self.write_page(nested / "page1.png")
        self.report.unlink(missing_ok=True)
        result = validate_book.main(
            [str(self.source), str(nested), "--evidence-report", str(self.report)]
        )
        self.assertEqual(2, result)
        self.assertIn(
            "input and output must be distinct, non-nested directories",
            json.loads(self.report.read_text(encoding="utf-8"))["evidence"]["failures"],
        )


    def test_missing_unsupported_source_page_fails_inventory_completeness(self) -> None:
        self.write_page(self.source / "page1.png")
        self.write_page(self.output / "page1.png")
        (self.source / "page2.pdf").write_bytes(b"%PDF-1.7\n")

        result, report = self.run_validator()

        self.assertEqual(2, result)
        self.assertFalse(report["mechanical_pass"])
        self.assertEqual(
            ["page2.pdf"],
            [
                item["path"]
                for item in report["evidence"]["candidate_evidence"]["input"][
                    "unsupported_image_candidates"
                ]
            ],
        )
        self.assertTrue(
            any(
                "unsupported input image candidate: page2.pdf" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_introduced_trapezoid_worsens_vertical_convergence(self) -> None:
        source = np.full((600, 500), 255, np.uint8)
        for x in (60, 90, 120, 380, 410, 440):
            cv2.line(source, (x, 40), (x, 560), 0, 3)
        for y in range(80, 541, 40):
            cv2.line(source, (45, y), (455, y), 0, 2)
        transform = cv2.getPerspectiveTransform(
            np.float32([[0, 0], [499, 0], [499, 599], [0, 599]]),
            np.float32([[45, 0], [454, 0], [499, 599], [0, 599]]),
        )
        trapezoid = cv2.warpPerspective(
            source, transform, (500, 600), borderValue=255
        )
        self.write_page(self.source / "page1.png", image=source)
        self.write_page(self.output / "page1.png", image=trapezoid)

        result, report = self.run_validator()

        self.assertEqual(2, result)
        geometry = report["evidence"]["pairs"][0]["geometry"][
            "vertical_convergence_barline"
        ]
        self.assertTrue(geometry["source"]["measurable"])
        self.assertTrue(geometry["output"]["measurable"])
        self.assertTrue(geometry["worsened"])
        self.assertTrue(
            any(
                "vertical convergence/barline residual worsened" in failure
                for failure in report["evidence"]["failures"]
            )
        )

    def test_speckled_component_extraction_budget_fails_closed(self) -> None:
        speckled = np.full((300, 220), 255, np.uint8)
        speckled[10:290:5, 10:210:5] = 0
        self.write_page(self.source / "page1.png", image=speckled)
        self.write_page(self.output / "page1.png", image=speckled)

        result, report = self.run_validator(
            "--max-components-per-extraction", "100"
        )

        self.assertEqual(2, result)
        budgets = report["evidence"]["safety_budgets"]
        self.assertTrue(budgets["rejected"]["component_extraction_or_matching"])
        self.assertTrue(budgets["observed"]["component_budget_failures"])
        self.assertTrue(
            any(
                "connected-component extraction safety budget exceeded" in failure
                for failure in report["evidence"]["failures"]
            )
        )
        self.assertIn(
            "page1.png",
            [
                item["file"]
                for item in report["evidence"]["visual_review_pages"]
            ],
        )

    def test_100k_fine_components_use_bounded_spatial_neighbor_work(self) -> None:
        component_count = 100_000
        stats = np.zeros((component_count + 1, 5), np.int32)
        stats[1:, cv2.CC_STAT_WIDTH] = 1
        stats[1:, cv2.CC_STAT_HEIGHT] = 1
        stats[1:, cv2.CC_STAT_AREA] = 1
        centroids = np.zeros((component_count + 1, 2), np.float64)
        indices = np.arange(component_count)
        centroids[1:, 0] = (indices % 500) * 2
        centroids[1:, 1] = (indices // 500) * 2
        image = np.full((1000, 1000), 255, np.uint8)

        started = time.monotonic()
        with patch.object(
            validate_book,
            "foreground_contrast",
            return_value=(np.zeros_like(image, dtype=np.float32), 10.0),
        ), patch.object(
            validate_book,
            "connected_component_stats",
            return_value=(component_count + 1, None, stats, centroids),
        ):
            features = validate_book.fine_component_features(image)
        elapsed = time.monotonic() - started

        self.assertLess(elapsed, 5.0)
        self.assertEqual(component_count, features["component_count"])
        self.assertTrue(features["component_census_truncated"])

    def test_dense_fine_census_retains_deleted_small_notation_with_its_stats(
        self,
    ) -> None:
        size = 1024
        source = np.full((size, size), 255, np.uint8)
        for row in range(60):
            for column in range(70):
                y = 10 + row * 14
                x = 10 + column * 14
                source[y:y + 2, x:x + 2] = 0
        notation_x, notation_y = 900, 950
        source[notation_y, notation_x] = 0
        source[980:984, 980:984] = 0
        output = source.copy()
        output[notation_y, notation_x] = 255

        source_fine = validate_book.fine_component_features(source)
        output_fine = validate_book.fine_component_features(output)
        census = source_fine["component_census"]
        self.assertTrue(source_fine["component_census_truncated"])
        self.assertEqual(4096, len(census))

        notation = min(
            census,
            key=lambda component: (
                abs(float(component[0]) - notation_x / size)
                + abs(float(component[1]) - notation_y / size)
            ),
        )
        self.assertAlmostEqual(notation_x / size, float(notation[0]), places=6)
        self.assertAlmostEqual(notation_y / size, float(notation[1]), places=6)
        self.assertAlmostEqual(1.0 / size, float(notation[2]), places=6)
        self.assertAlmostEqual(1.0 / size, float(notation[3]), places=6)
        self.assertEqual(1, int(notation[4]))

        unmapped_bounds = {"measurable": False}
        damage = validate_book.mapped_fine_damage(
            {"content_bounds": unmapped_bounds},
            {"content_bounds": unmapped_bounds},
            {"fine_components": source_fine},
            {"fine_components": output_fine},
            {"verified": True},
        )
        self.assertTrue(damage["failed"])
        self.assertEqual(1, damage["missing_component_count"])
        self.assertEqual(1, damage["missing_examples"][0]["source_area_pixels"])

    def test_fine_component_neighbor_budget_fails_closed(self) -> None:
        centers = np.zeros((20, 2), np.float64)
        with patch.dict(
            validate_book.ACTIVE_COMPONENT_BUDGETS,
            {"maximum_component_match_comparisons": 10},
        ):
            with self.assertRaisesRegex(
                validate_book.ComponentBudgetError,
                "fine component neighbor comparison safety budget exceeded",
            ):
                validate_book.bounded_fine_component_isolations(
                    centers, list(range(len(centers)))
                )

    def test_exact_4000_square_center_speckles_preflight_before_allocation(self) -> None:
        speckled = np.zeros((4000, 4000), np.uint8)
        speckled[1000:3000:4, 1000:3000:4] = 1
        horizontal_speckles = np.zeros_like(speckled)
        horizontal_speckles[1000:3000:4, 1000:3000] = 1

        with patch.dict(
            validate_book.ACTIVE_COMPONENT_BUDGETS,
            {"maximum_components_per_extraction": 100},
        ), patch.object(
            validate_book.cv2,
            "connectedComponents",
            side_effect=AssertionError("large label allocation was attempted"),
        ), patch.object(
            validate_book.cv2,
            "connectedComponentsWithStats",
            side_effect=AssertionError("large stats allocation was attempted"),
        ):
            with self.assertRaisesRegex(
                validate_book.ComponentBudgetError,
                "bounded complexity preflight",
            ):
                validate_book.connected_component_stats(
                    speckled, "high-resolution speckle regression"
                )
            with self.assertRaisesRegex(
                validate_book.ComponentBudgetError,
                "bounded complexity preflight",
            ):
                validate_book.structure_metrics(
                    np.full(speckled.shape, 255, np.uint8),
                    horizontal_speckles,
                )

    def test_exact_4000_square_normal_pages_pass_streaming_preflight(self) -> None:
        horizontal_page = np.zeros((4000, 4000), np.uint8)
        for y in range(500, 3501, 300):
            horizontal_page[y : y + 5, 500:3500] = 1
        vertical_page = horizontal_page.T.copy()

        with patch.dict(
            validate_book.ACTIVE_COMPONENT_BUDGETS,
            {"maximum_components_per_extraction": 100},
        ), patch.object(
            validate_book.cv2,
            "connectedComponents",
            side_effect=AssertionError("large label allocation was attempted"),
        ):
            for name, page in (
                ("horizontal normal page", horizontal_page),
                ("vertical normal page", vertical_page),
            ):
                with self.subTest(name=name):
                    validate_book.preflight_component_complexity(page, name)


if __name__ == "__main__":
    unittest.main()
