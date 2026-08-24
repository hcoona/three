from __future__ import annotations

import importlib.util
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock

import numpy as np


SCRIPT = Path(__file__).parents[1] / "scripts" / "rectify_pages.py"
SPEC = importlib.util.spec_from_file_location("rectify_pages", SCRIPT)
assert SPEC and SPEC.loader
rectify_pages = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(rectify_pages)


def repository_fixture_root() -> Path:
    configured = os.environ.get("SCAN_RESTORATION_FIXTURE_ROOT")
    if configured:
        return Path(configured).expanduser().resolve()
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "mise.toml").is_file() and (candidate / "apm.yml").is_file():
            return candidate
    return Path(__file__).resolve().parent


def encoded_uint16_tiff(
    values: np.ndarray,
    *,
    byte_order: str,
    compression: int,
    photometric: int,
    orientation: int,
) -> bytes:
    endian = "little" if byte_order == "II" else "big"
    marker = b"II" if endian == "little" else b"MM"
    samples = np.asarray(values, dtype=np.uint16)
    raw = samples.astype("<u2" if endian == "little" else ">u2").tobytes()
    strip = rectify_pages.zlib.compress(raw) if compression == 8 else raw
    tags = [
        (256, 4, samples.shape[1]),
        (257, 4, samples.shape[0]),
        (258, 3, 16),
        (259, 3, compression),
        (262, 3, photometric),
        (273, 4, 0),
        (274, 3, orientation),
        (277, 3, 1),
        (278, 4, samples.shape[0]),
        (279, 4, len(strip)),
        (284, 3, 1),
        (339, 3, 1),
    ]
    strip_offset = 8 + 2 + len(tags) * 12 + 4
    tags[5] = (273, 4, strip_offset)

    def integer(value: int, length: int) -> bytes:
        return value.to_bytes(length, endian)

    encoded = bytearray(marker + integer(42, 2) + integer(8, 4))
    encoded += integer(len(tags), 2)
    for tag, field_type, value in tags:
        encoded += integer(tag, 2) + integer(field_type, 2) + integer(1, 4)
        encoded += integer(value, 2 if field_type == 3 else 4)
        if field_type == 3:
            encoded += b"\0\0"
    encoded += integer(0, 4)
    encoded += strip
    return bytes(encoded)


class RectificationRegressionTests(unittest.TestCase):
    def test_low_confidence_horizontal_evidence_never_rotates(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(rectify_pages, "horizontal_angle", return_value=(1.0, 9)),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(rectify_pages.cv2, "warpAffine") as warp,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        warp.assert_not_called()
        self.assertEqual(metrics["horizontal_status"], "low_confidence")
        self.assertFalse(metrics["horizontal_reverted"])
        self.assertIs(metrics["review_required"], False)

    def test_horizontal_candidate_reverts_when_convergence_worsens(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        horizontal_results = [
            (1.0, 20),
            (0.1, 20),
            (1.0, 20),
            (1.0, 20),
        ]
        vertical_results = [
            (np.array([0.0, 0.2]), 20, None),
            (np.array([0.0, 0.8]), 20, None),
            (None, 0, "insufficient"),
            (None, 0, "insufficient"),
        ]
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", side_effect=horizontal_results
            ),
            mock.patch.object(
                rectify_pages, "vertical_model", side_effect=vertical_results
            ),
            mock.patch.object(
                rectify_pages.cv2, "warpAffine", return_value=image.copy()
            ),
        ):
            _, metrics = rectify_pages.rectify(image)

        self.assertTrue(metrics["horizontal_reverted"])
        self.assertEqual(
            metrics["status"],
            "review_required",
        )
        self.assertEqual(
            metrics["horizontal_validation_vertical_before_right_angle"], 0.1
        )
        self.assertEqual(
            metrics["horizontal_validation_vertical_after_right_angle"], 0.4
        )
        self.assertEqual(
            metrics["horizontal_validation_convergence_differential_before"], 0.2
        )
        self.assertEqual(
            metrics["horizontal_validation_convergence_differential_after"], 0.8
        )

    def test_common_tilt_improvement_cannot_mask_convergence_worsening(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=[
                    (1.0, 20, None, 1.0, 10, None),
                    (0.1, 20, None, 0.1, 10, None),
                    (0.0, 20, None, 0.0, 10, None),
                    (0.0, 20, None, 0.0, 10, None),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                side_effect=[
                    (np.array([1.0, 0.2]), 20, None),
                    (np.array([0.0, 0.3]), 20, None),
                    (None, 0, "insufficient"),
                    (None, 0, "insufficient"),
                ],
            ),
            mock.patch.object(
                rectify_pages.cv2, "warpAffine", return_value=image.copy()
            ),
        ):
            _, metrics = rectify_pages.rectify(image)

        self.assertTrue(metrics["horizontal_reverted"])
        self.assertEqual(
            metrics["horizontal_reason"],
            "remeasured_convergence_residual_worsened",
        )

    def test_vertical_fit_is_recomputed_after_last_outlier_pass(self) -> None:
        segments = []
        for index in range(12):
            x = 10.0 + index * 8.0
            segments.append([x, 0.0, x, 80.0])
        original_lstsq = rectify_pages.np.linalg.lstsq
        with (
            mock.patch.object(
                rectify_pages, "line_segments", return_value=np.asarray(segments)
            ),
            mock.patch.object(
                rectify_pages.np.linalg,
                "lstsq",
                wraps=original_lstsq,
            ) as lstsq,
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((100, 120), 255, np.uint8)
            )

        self.assertIsNotNone(model)
        self.assertEqual(samples, 12)
        self.assertIsNone(reason)
        self.assertEqual(lstsq.call_count, 5)

    def test_vertical_fragments_do_not_count_as_independent_structures(self) -> None:
        fragments = [
            [20.0, float(y), 20.0, float(y + 18)]
            for y in range(0, 72, 8)
        ]
        fragments.append([80.0, 0.0, 80.0, 90.0])
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(fragments),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((100, 100), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(samples, 2)
        self.assertEqual(reason, "insufficient_independent_vertical_structures")

    def test_vertical_fragments_with_angle_disagreement_are_one_vote(self) -> None:
        fragments = []
        for y, angle in zip(range(0, 80, 8), (-0.28, 0.24) * 5):
            dy = 18.0
            dx = float(np.tan(np.radians(angle)) * dy)
            fragments.append([20.0, float(y), 20.0 + dx, float(y) + dy])
        fragments.extend([[float(x), 0.0, float(x), 90.0] for x in (80, 90)])
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(fragments),
        ):
            angles, positions, _ = rectify_pages.clustered_vertical_structures(
                np.full((100, 100), 255, np.uint8)
            )

        self.assertEqual(len(angles), 3)
        self.assertEqual(len(positions), 3)

    def test_split_staff_lines_support_short_barline_detection(self) -> None:
        segments = []
        for y in (400.0, 408.0, 416.0, 424.0, 432.0):
            segments.extend(
                ([40.0, y, 490.0, y], [510.0, y, 960.0, y])
            )
        segments.append([500.0, 398.0, 500.0, 434.0])
        foreground = np.full((1000, 1000), 255, np.uint8)
        for y in (400, 408, 416, 424, 432):
            rectify_pages.cv2.line(foreground, (40, y), (960, y), 80, 2)
        rectify_pages.cv2.line(foreground, (500, 398), (500, 434), 0, 2)
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            angles, positions, _ = rectify_pages.clustered_vertical_structures(
                foreground
            )

        self.assertEqual(len(angles), 1)
        self.assertAlmostEqual(float(positions[0]), 0.5)

    def test_remote_collinear_fragments_cannot_fabricate_staff_crossings(
        self,
    ) -> None:
        segments = []
        for y in (400.0, 408.0, 416.0, 424.0, 432.0):
            segments.extend(
                ([40.0, y, 200.0, y], [800.0, y, 960.0, y])
            )
        segments.append([500.0, 398.0, 500.0, 434.0])
        foreground = np.full((1000, 1000), 255, np.uint8)
        for y in (400, 408, 416, 424, 432):
            rectify_pages.cv2.line(foreground, (40, y), (200, y), 80, 2)
            rectify_pages.cv2.line(foreground, (800, y), (960, y), 80, 2)
        rectify_pages.cv2.line(foreground, (500, 398), (500, 434), 0, 2)
        references = rectify_pages.horizontal_structural_references(
            np.asarray(segments), 1000, 1000
        )

        self.assertEqual(len(references), 10)
        self.assertEqual(
            rectify_pages.staff_intersection_coordinates(
                (500.0, 398.0, 500.0, 434.0),
                references,
                1000,
                1000,
            ),
            [],
        )
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            angles, _, _ = rectify_pages.clustered_vertical_structures(
                foreground
            )

        self.assertEqual(len(angles), 0)

    def test_nearby_fragmented_staff_occupancy_supports_crossings(self) -> None:
        segments = []
        for y in (400.0, 408.0, 416.0, 424.0, 432.0):
            segments.extend(
                ([40.0, y, 490.0, y], [510.0, y, 960.0, y])
            )
        references = rectify_pages.horizontal_structural_references(
            np.asarray(segments), 1000, 1000
        )

        crossings = rectify_pages.staff_intersection_coordinates(
            (500.0, 398.0, 500.0, 434.0),
            references,
            1000,
            1000,
        )

        self.assertEqual(len(references), 5)
        self.assertEqual(len(crossings), 5)
        self.assertTrue(rectify_pages.spans_complete_staff_system(crossings))

    def test_split_vertical_fragments_are_gated_after_assembly(self) -> None:
        segments = []
        for y in (400.0, 408.0, 416.0, 424.0, 432.0):
            segments.extend(
                ([40.0, y, 490.0, y], [510.0, y, 960.0, y])
            )
        segments.extend(
            [
                [500.0, 398.0, 500.0, 403.0],
                [500.0, 405.0, 500.0, 411.0],
                [500.0, 413.0, 500.0, 419.0],
                [500.0, 421.0, 500.0, 427.0],
                [500.0, 429.0, 500.0, 434.0],
            ]
        )
        foreground = np.full((1000, 1000), 255, np.uint8)
        for y in (400, 408, 416, 424, 432):
            rectify_pages.cv2.line(foreground, (40, y), (960, y), 80, 2)
        rectify_pages.cv2.line(foreground, (500, 398), (500, 434), 0, 2)
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            angles, positions, _ = rectify_pages.clustered_vertical_structures(
                foreground
            )

        self.assertEqual(len(angles), 1)
        self.assertAlmostEqual(float(positions[0]), 0.5)

    def test_real_page_48_note_morphology_cannot_supply_vertical_model(
        self,
    ) -> None:
        page = (
            repository_fixture_root()
            / "input"
            / "Fundamentals of Piano Theory Level 2_Page_48.jpg"
        )
        if not page.exists():
            self.skipTest("Page_48 real-page fixture is unavailable")
        image = rectify_pages.cv2.imread(
            str(page), rectify_pages.cv2.IMREAD_GRAYSCALE
        )
        self.assertIsNotNone(image)
        height, width = image.shape
        y, x = np.indices(image.shape, dtype=np.float32)
        map_x = x + (y - (height - 1) / 2) * np.tan(
            np.radians(0.60 * (x / (width - 1) - 0.5))
        )
        injected = rectify_pages.cv2.remap(
            image,
            map_x,
            y,
            rectify_pages.cv2.INTER_CUBIC,
            borderMode=rectify_pages.cv2.BORDER_CONSTANT,
            borderValue=255,
        )

        model, samples, reason = rectify_pages.vertical_model(injected)

        self.assertIsNone(model)
        self.assertEqual(
            reason,
            "insufficient_independent_vertical_structures",
        )
        self.assertLess(samples, rectify_pages.MINIMUM_VERTICAL_STRUCTURES)

    def test_consensus_must_retain_bilateral_distribution(self) -> None:
        positions = np.asarray(
            [0.08, 0.16, 0.24, 0.32, 0.38, 0.42, 0.46,
             0.54, 0.60, 0.68, 0.76, 0.84, 0.90, 0.94]
        )
        angles = np.zeros(len(positions))
        weights = np.ones(len(positions))
        clusters = [
            [(position, 0.0, 80.0, 0.0, 1.0, y, y + 0.35)]
            for position, y in zip(
                positions,
                np.resize(np.asarray((0.05, 0.30, 0.55)), len(positions)),
            )
        ]
        consensus = np.asarray(
            [True, True, True, True, True, True, True,
             True, True, True, False, False, False, False]
        )
        with (
            mock.patch.object(
                rectify_pages,
                "clustered_vertical_structures",
                return_value=(angles, positions, weights, clusters),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_mode",
                return_value=(np.zeros(2), consensus, 0.0),
            ),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((1000, 1000), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(samples, 10)
        self.assertEqual(
            reason,
            "insufficient_independent_structures_on_both_sides_"
            "after_consensus_filtering",
        )

    def test_vertical_structures_must_be_balanced_across_both_sides(self) -> None:
        segments = [
            [float(x), 0.0, float(x), 90.0]
            for x in (10, 20, 30, 40, 50, 60, 70, 80, 90, 120, 140, 160, 180)
        ]
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((100, 200), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(samples, 13)
        self.assertEqual(reason, "unbalanced_vertical_structure_counts")

    def test_near_even_vertical_modes_are_conflicting_evidence(self) -> None:
        height, width = 1000, 1000
        segments = []
        for index, position in enumerate(np.linspace(0.1, 0.9, 12)):
            angle = 0.2 if index % 2 == 0 else 1.4
            center_x = position * width
            offset = np.tan(np.radians(angle)) * 450
            segments.append(
                [center_x - offset, 50.0, center_x + offset, 950.0]
            )
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((height, width), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(samples, 6)
        self.assertEqual(reason, "conflicting_evidence")

    def test_overlapping_50_50_vertical_angle_modes_remain_separate(self) -> None:
        height, width = 1000, 1000
        segments = []
        for position in np.linspace(0.08, 0.92, 10):
            for angle in (0.2, 1.0):
                center_x = position * width
                offset = np.tan(np.radians(angle)) * 450
                segments.append(
                    [center_x - offset, 50.0, center_x + offset, 950.0]
                )
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            angles, _, _ = rectify_pages.clustered_vertical_structures(
                np.full((height, width), 255, np.uint8)
            )
            model, samples, reason = rectify_pages.vertical_model(
                np.full((height, width), 255, np.uint8)
            )

        self.assertEqual(len(angles), 20)
        self.assertEqual(np.count_nonzero(np.isclose(angles, 0.2, atol=0.03)), 10)
        self.assertEqual(np.count_nonzero(np.isclose(angles, 1.0, atol=0.03)), 10)
        self.assertIsNone(model)
        self.assertEqual(samples, 10)
        self.assertEqual(reason, "conflicting_evidence")

    def vertical_mode_fixture(
        self, alternative_indexes: np.ndarray, alternative_angles: np.ndarray
    ) -> tuple[np.ndarray | None, int, str | None]:
        positions = np.linspace(0.08, 0.92, 100)
        angles = np.full(100, 0.2)
        angles[alternative_indexes] = alternative_angles
        weights = np.ones(100)
        clusters = [
            [(position, angle, 80.0, 0.0, 1.0, 0.05, 0.40)]
            for position, angle in zip(positions, angles)
        ]
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            return rectify_pages.vertical_model(
                np.full((1000, 1000), 255, np.uint8)
            )

    def test_exact_70_30_coherent_vertical_modes_conflict(self) -> None:
        alternative_indexes = np.linspace(0, 99, 30, dtype=int)

        model, samples, reason = self.vertical_mode_fixture(
            alternative_indexes, np.full(30, 1.4)
        )

        self.assertIsNone(model)
        self.assertEqual(samples, 70)
        self.assertEqual(reason, "conflicting_evidence")

    def test_exact_70_30_x_aligned_physical_modes_conflict(self) -> None:
        base_positions = np.linspace(0.08, 0.92, 10)
        positions = np.repeat(base_positions, 10)
        angles = np.tile(np.asarray([0.2] * 7 + [1.4] * 3), 10)
        weights = np.ones(100)
        clusters = [
            [(position, angle, 80.0, 0.0, 1.0, y, y + 0.08)]
            for position, angle, y in zip(
                positions,
                angles,
                np.tile(np.linspace(0.02, 0.83, 10), 10),
            )
        ]
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((1000, 1000), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(samples, 70)
        self.assertEqual(reason, "conflicting_evidence")

    def test_60_40_crossing_vertical_modes_conflict(self) -> None:
        positions = np.linspace(0.08, 0.92, 100)
        first_mode = 3.0 * (positions - 0.5)
        second_mode = -3.0 * (positions - 0.5)
        angles = second_mode.copy()
        dominant_indexes = np.linspace(0, 99, 60, dtype=int)
        angles[dominant_indexes] = first_mode[dominant_indexes]
        weights = np.ones(100)
        clusters = [
            [(position, angle, 80.0, 0.0, 1.0, y, y + 0.30)]
            for position, angle, y in zip(
                positions,
                angles,
                np.resize(np.asarray((0.03, 0.35, 0.65)), 100),
            )
        ]
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            model, _, reason = rectify_pages.vertical_model(
                np.full((1000, 1000), 255, np.uint8)
            )

        self.assertIsNone(model)
        self.assertEqual(reason, "conflicting_evidence")

    def test_exact_70_30_crossing_modes_conflict_inside_keep_tolerance(
        self,
    ) -> None:
        base_positions = np.linspace(0.05, 0.95, 10)
        positions = np.repeat(base_positions, 10)
        dominant = 0.8 * (positions - 0.5)
        minority = -0.8 * (positions - 0.5)
        angles = dominant.copy()
        minority_mask = np.zeros(100, dtype=bool)
        for offset in range(10):
            minority_mask[offset * 10 + 7 : offset * 10 + 10] = True
        angles[minority_mask] = minority[minority_mask]
        weights = np.ones(100)

        model, keep, _, reason = rectify_pages.vertical_consensus(
            angles, positions, weights
        )

        self.assertGreater(np.count_nonzero(keep), 70)
        self.assertTrue(np.any(keep & minority_mask))
        self.assertLess(
            np.median(np.abs(dominant - (model[0] + model[1] * (positions - 0.5)))),
            0.02,
        )
        self.assertEqual(reason, "conflicting_evidence")

    def test_balanced_70_30_modes_conflict_when_center_votes_hide_separation(
        self,
    ) -> None:
        base_positions = np.asarray(
            (0.05, 0.10, 0.45, 0.47, 0.49, 0.51, 0.53, 0.55, 0.90, 0.95)
        )
        positions = np.repeat(base_positions, 10)
        dominant = 0.8 * (positions - 0.5)
        minority = -0.8 * (positions - 0.5)
        angles = dominant.copy()
        minority_mask = np.zeros(100, dtype=bool)
        for offset in range(10):
            minority_mask[offset * 10 + 7 : offset * 10 + 10] = True
        angles[minority_mask] = minority[minority_mask]

        _, keep, _, reason = rectify_pages.vertical_consensus(
            angles, positions, np.ones(100)
        )

        self.assertGreater(np.count_nonzero(keep), 70)
        self.assertTrue(np.any(keep & minority_mask))
        self.assertLess(np.median(np.abs(dominant - minority)), 0.35)
        self.assertEqual(reason, "conflicting_evidence")

    def test_exact_14_6_equal_weight_opposing_modes_conflict(self) -> None:
        positions = np.asarray(
            (
                0.05,
                0.05,
                0.10,
                0.10,
                0.22,
                0.22,
                0.40,
                0.40,
                0.60,
                0.60,
                0.78,
                0.78,
                0.90,
                0.90,
                0.95,
                0.95,
                0.15,
                0.35,
                0.65,
                0.85,
            )
        )
        minority = np.asarray(
            (
                False,
                True,
                False,
                False,
                True,
                False,
                False,
                False,
                False,
                False,
                False,
                False,
                False,
                True,
                True,
                False,
                True,
                True,
                False,
                False,
            )
        )
        dominant_angles = 0.9 * (positions - 0.5)
        minority_angles = -0.9 * (positions - 0.5)
        angles = np.where(minority, minority_angles, dominant_angles)
        clusters = [
            [(position, angle, 80.0, 0.0, 1.0, 0.05, 0.40)]
            for position, angle in zip(positions, angles)
        ]

        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, np.ones(20), clusters),
        ):
            model, _, reason = rectify_pages.vertical_model(
                np.full((1000, 1000), 255, np.uint8)
            )

        self.assertEqual(np.count_nonzero(~minority), 14)
        self.assertEqual(np.count_nonzero(minority), 6)
        self.assertIsNone(model)
        self.assertEqual(reason, "conflicting_evidence")

    def test_exact_70_30_opposing_mode_mixtures_always_conflict(self) -> None:
        for total, slope, seed in (
            (20, 0.8, 3),
            (40, 1.1, 7),
            (100, 1.6, 11),
        ):
            with self.subTest(total=total, slope=slope, seed=seed):
                positions = np.linspace(0.05, 0.95, total)
                minority = np.zeros(total, dtype=bool)
                minority[np.linspace(0, total - 1, total * 3 // 10, dtype=int)] = True
                permutation = np.random.default_rng(seed).permutation(total)
                positions = positions[permutation]
                minority = minority[permutation]
                dominant_angles = 0.1 + slope * (positions - 0.5)
                minority_angles = 0.1 - slope * (positions - 0.5)
                angles = np.where(minority, minority_angles, dominant_angles)
                weights = 0.8 + 0.4 * np.random.default_rng(seed + 1).random(total)
                weights[minority] *= (
                    weights[~minority].sum() * 3 / 7
                ) / weights[minority].sum()

                _, _, _, reason = rectify_pages.vertical_consensus(
                    angles, positions, weights
                )

                self.assertEqual(reason, "conflicting_evidence")

    def test_exact_60_to_100_left_side_mode_worsening_conflicts(self) -> None:
        positions = np.linspace(0.0, 0.5, 100, endpoint=False)
        minority = np.zeros(100, dtype=bool)
        minority[np.linspace(0, 99, 30, dtype=int)] = True
        distance_from_center = 1.0 - 2.0 * positions
        dominant_angles = 0.60 * distance_from_center
        worsened_angles = 1.00 * distance_from_center
        angles = np.where(minority, worsened_angles, dominant_angles)

        _, _, _, reason = rectify_pages.vertical_consensus(
            angles, positions, np.ones(100)
        )

        self.assertEqual(float(dominant_angles[0]), 0.60)
        self.assertEqual(float(worsened_angles[0]), 1.00)
        self.assertEqual(reason, "conflicting_evidence")

    def test_point_282_to_point_575_mode_regression_conflicts(self) -> None:
        positions = np.linspace(0.0, 0.5, 100, endpoint=False)
        minority = np.zeros(100, dtype=bool)
        minority[np.linspace(0, 99, 30, dtype=int)] = True
        distance_from_center = 1.0 - 2.0 * positions
        angles = np.where(
            minority,
            0.575 * distance_from_center,
            0.282 * distance_from_center,
        )

        _, _, _, reason = rectify_pages.vertical_consensus(
            angles, positions, np.ones(100)
        )

        self.assertEqual(reason, "conflicting_evidence")

    def test_randomized_70_30_regressions_conflict_across_sides(self) -> None:
        for side in ("left", "right"):
            for seed in range(12):
                with self.subTest(side=side, seed=seed):
                    rng = np.random.default_rng(seed)
                    positions = rng.uniform(0.01, 0.49, 100)
                    if side == "right":
                        positions = 1.0 - positions
                    minority = np.zeros(100, dtype=bool)
                    minority[rng.choice(100, 30, replace=False)] = True
                    distance_from_center = np.abs(2.0 * positions - 1.0)
                    angles = np.where(
                        minority,
                        0.575 * distance_from_center,
                        0.282 * distance_from_center,
                    )
                    weights = 0.8 + 0.4 * rng.random(100)
                    weights[minority] *= (
                        weights[~minority].sum() * 3 / 7
                    ) / weights[minority].sum()

                    _, _, _, reason = rectify_pages.vertical_consensus(
                        angles, positions, weights
                    )

                    self.assertEqual(reason, "conflicting_evidence")

    def test_unilateral_70_30_worsening_modes_always_conflict(self) -> None:
        for side, total, before, after, seed in (
            ("left", 20, 0.45, 0.85, 3),
            ("left", 40, 0.70, 1.10, 7),
            ("right", 20, 0.50, 0.90, 11),
            ("right", 100, 0.80, 1.20, 13),
        ):
            with self.subTest(
                side=side, total=total, before=before, after=after
            ):
                positions = np.linspace(0.0, 0.5, total, endpoint=False)
                if side == "right":
                    positions = 1.0 - positions
                minority = np.zeros(total, dtype=bool)
                minority[
                    np.linspace(0, total - 1, total * 3 // 10, dtype=int)
                ] = True
                permutation = np.random.default_rng(seed).permutation(total)
                positions = positions[permutation]
                minority = minority[permutation]
                distance_from_center = np.abs(2.0 * positions - 1.0)
                angles = np.where(
                    minority,
                    after * distance_from_center,
                    before * distance_from_center,
                )
                weights = (
                    0.8
                    + 0.4
                    * np.random.default_rng(seed + 1).random(total)
                )
                weights[minority] *= (
                    weights[~minority].sum() * 3 / 7
                ) / weights[minority].sum()

                _, _, _, reason = rectify_pages.vertical_consensus(
                    angles, positions, weights
                )

                self.assertEqual(reason, "conflicting_evidence")

    def test_single_normal_convergence_mode_still_converges(self) -> None:
        positions = np.linspace(0.05, 0.95, 40)
        expected = np.asarray((0.06, 0.52))
        angles = (
            expected[0]
            + expected[1] * (positions - 0.5)
            + 0.015 * np.sin(np.arange(len(positions)))
        )

        model, keep, mad, reason = rectify_pages.vertical_consensus(
            angles, positions, np.ones(len(positions))
        )

        self.assertIsNone(reason)
        self.assertTrue(np.all(keep))
        self.assertLess(mad, 0.02)
        np.testing.assert_allclose(model, expected, atol=0.01)

    def test_71_29_vertical_split_accepts_dominant_mode(self) -> None:
        alternative_indexes = np.linspace(0, 99, 29, dtype=int)

        model, samples, reason = self.vertical_mode_fixture(
            alternative_indexes, np.full(29, 1.4)
        )

        self.assertIsNotNone(model)
        self.assertEqual(samples, 100)
        self.assertIsNone(reason)
        self.assertAlmostEqual(float(model[0]), 0.2)

    def test_dominant_vertical_mode_accepts_no_coherent_alternative(self) -> None:
        alternative_indexes = np.linspace(0, 99, 30, dtype=int)
        alternative_angles = np.resize(np.asarray((1.4, 2.4)), 30)

        model, samples, reason = self.vertical_mode_fixture(
            alternative_indexes, alternative_angles
        )

        self.assertIsNotNone(model)
        self.assertEqual(samples, 100)
        self.assertIsNone(reason)
        self.assertAlmostEqual(float(model[0]), 0.2)

    def test_dominant_vertical_mode_is_not_averaged_with_minor_mode(self) -> None:
        height, width = 1000, 1000
        segments = []
        for index, position in enumerate(np.linspace(0.06, 0.94, 24)):
            angle = 1.4 if index % 4 == 1 else 0.2
            center_x = position * width
            offset = np.tan(np.radians(angle)) * 450
            segments.append(
                [center_x - offset, 50.0, center_x + offset, 950.0]
            )
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            model, samples, reason = rectify_pages.vertical_model(
                np.full((height, width), 255, np.uint8)
            )

        self.assertIsNotNone(model)
        self.assertEqual(samples, 24)
        self.assertIsNone(reason)
        self.assertAlmostEqual(float(model[0]), 0.2, delta=0.03)

    def test_vertical_conflict_requires_review_without_transform(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", return_value=(0.0, 20)
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 6, "conflicting_evidence"),
            ),
            mock.patch.object(rectify_pages.cv2, "remap") as remap,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        remap.assert_not_called()
        self.assertEqual(metrics["vertical_status"], "review_required")
        self.assertEqual(metrics["vertical_reason"], "conflicting_evidence")
        self.assertTrue(metrics["review_required"])

    def test_large_scan_corridor_thickness_is_in_original_pixels(self) -> None:
        height, width = 2800, 2000
        scale = 0.5
        segments = [
            [position * width * scale, 0.0, position * width * scale, height * scale]
            for position in np.linspace(0.08, 0.92, 16)
        ]
        with (
            mock.patch.object(
                rectify_pages, "line_segments", return_value=np.asarray(segments)
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_segment_half_width",
                return_value=20.0,
            ),
        ):
            _, _, _, fitted, _ = rectify_pages.vertical_model(
                np.full((height, width), 255, np.uint8),
                detailed=True,
            )

        self.assertGreater(len(fitted), 0)
        np.testing.assert_allclose(fitted[:, 2], 20.0 / scale)

    def test_large_scan_fitted_corridors_cannot_self_validate(self) -> None:
        height, width = 2800, 2000
        fitted_positions = np.linspace(0.1, 0.9, 12)
        detected_positions = fitted_positions + 60.0 / width
        angles = np.zeros(12)
        weights = np.ones(12)
        clusters = [
            [(position, 0.0, 1000.0, 0.0, 40.0, 0.05, 0.95)]
            for position in detected_positions
        ]
        fitted = np.column_stack(
            (
                fitted_positions,
                np.zeros(12),
                np.full(12, 40.0),
                np.zeros(12),
                np.full(12, 0.35),
            )
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, detected_positions, weights, clusters),
        ):
            residual, samples, reason = rectify_pages.vertical_region_validation(
                np.full((height, width), 255, np.uint8),
                fitted,
                set(range(rectify_pages.VERTICAL_X_BAND_COUNT)),
            )

        self.assertIsNone(residual)
        self.assertEqual(samples, 0)
        self.assertEqual(
            reason, "insufficient_grouped_vertical_structures_on_both_sides"
        )

    def test_even_multimode_holdout_cannot_median_select_one_mode(self) -> None:
        positions = np.linspace(0.08, 0.92, 16)
        angles = np.resize(np.asarray((0.2, 1.4)), 16)
        weights = np.ones(16)
        clusters = [
            [(position, angle, 80.0, 0.0, 1.0, y, y + 0.30)]
            for position, angle, y in zip(
                positions,
                angles,
                np.resize(np.asarray((0.03, 0.35, 0.65)), 16),
            )
        ]
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            residual, samples, reason = rectify_pages.vertical_region_validation(
                np.full((1000, 1000), 255, np.uint8),
                holdout_bands=set(range(rectify_pages.VERTICAL_X_BAND_COUNT)),
            )

        self.assertIsNone(residual)
        self.assertEqual(samples, 8)
        self.assertEqual(reason, "conflicting_evidence")

    def test_only_note_stems_never_apply_vertical_correction(self) -> None:
        height, width = 900, 700
        image = np.full((height, width), 255, np.uint8)
        for staff_top in range(100, 800, 140):
            for offset in range(5):
                y = staff_top + offset * 10
                rectify_pages.cv2.line(image, (45, y), (width - 45, y), 80, 2)
            for x in range(70, width - 60, 24):
                stem_top = staff_top + 8 + (x // 24 % 2) * 8
                rectify_pages.cv2.line(
                    image,
                    (x, stem_top),
                    (x, stem_top + 34),
                    0,
                    2,
                )

        corrected, metrics = rectify_pages.rectify(image)

        self.assertTrue(np.array_equal(corrected, image))
        self.assertFalse(metrics["vertical_applied"], metrics)
        self.assertEqual(metrics["vertical_before_convergence_differential"], 0.0)

        y, x = np.indices(image.shape, dtype=np.float32)
        map_x = x + (y - (height - 1) / 2) * np.tan(
            np.radians(0.60 * (x / (width - 1) - 0.5))
        )
        converged_stems = rectify_pages.cv2.remap(
            image,
            map_x,
            y,
            rectify_pages.cv2.INTER_CUBIC,
            borderMode=rectify_pages.cv2.BORDER_CONSTANT,
            borderValue=255,
        )
        _, converged_metrics = rectify_pages.rectify(converged_stems)

        self.assertFalse(
            converged_metrics["vertical_applied"], converged_metrics
        )
        self.assertLess(
            converged_metrics["vertical_samples"],
            rectify_pages.MINIMUM_VERTICAL_STRUCTURES,
        )

    def test_slanted_noteheaded_stems_never_apply_but_barlines_are_detected(
        self,
    ) -> None:
        height, width = 900, 700
        stems = np.full((height, width), 255, np.uint8)
        staff_tops = range(80, 820, 145)
        for staff_top in staff_tops:
            for offset in range(5):
                y = staff_top + offset * 9
                rectify_pages.cv2.line(stems, (35, y), (width - 35, y), 70, 2)
            for x in range(65, width - 50, 28):
                angle = -0.65 + 1.30 * x / (width - 1)
                stem_bottom = staff_top + 43
                stem_top = staff_top - 4
                dx = round(np.tan(np.radians(angle)) * (stem_bottom - stem_top))
                rectify_pages.cv2.line(
                    stems,
                    (x - dx, stem_top),
                    (x, stem_bottom),
                    0,
                    2,
                )
                rectify_pages.cv2.ellipse(
                    stems,
                    (x + 5, stem_bottom - 2),
                    (7, 5),
                    -18,
                    0,
                    360,
                    0,
                    -1,
                )

        corrected, metrics = rectify_pages.rectify(stems)

        self.assertTrue(np.array_equal(corrected, stems))
        self.assertFalse(metrics["vertical_applied"], metrics)
        self.assertLess(
            metrics["vertical_samples"],
            rectify_pages.MINIMUM_VERTICAL_STRUCTURES,
        )

        barlines = np.full((height, width), 255, np.uint8)
        for staff_top in staff_tops:
            for offset in range(5):
                y = staff_top + offset * 9
                rectify_pages.cv2.line(
                    barlines, (35, y), (width - 35, y), 70, 2
                )
        first_staff, second_staff = list(staff_tops)[:2]
        for x in np.linspace(55, width - 55, 14, dtype=int):
            rectify_pages.cv2.line(
                barlines,
                (int(x), first_staff - 2),
                (int(x), second_staff + 38),
                0,
                2,
            )

        angles, _, _ = rectify_pages.clustered_vertical_structures(barlines)

        self.assertGreater(len(angles), 0)

    def test_yellow_paper_stems_are_rejected_but_barlines_are_detected(
        self,
    ) -> None:
        height, width = 900, 700
        paper = np.linspace(218, 198, width, dtype=np.uint8)
        stems = np.broadcast_to(paper, (height, width)).copy()
        staff_tops = list(range(80, 820, 145))
        for staff_top in staff_tops:
            for offset in range(5):
                y = staff_top + offset * 9
                rectify_pages.cv2.line(stems, (35, y), (width - 35, y), 85, 2)
            for x in range(65, width - 50, 28):
                stem_bottom = staff_top + 43
                stem_top = staff_top - 4
                rectify_pages.cv2.line(
                    stems, (x, stem_top), (x, stem_bottom), 25, 2
                )
                rectify_pages.cv2.ellipse(
                    stems,
                    (x + 5, stem_bottom - 2),
                    (7, 5),
                    -18,
                    0,
                    360,
                    25,
                    -1,
                )

        stem_angles, _, _ = rectify_pages.clustered_vertical_structures(stems)

        self.assertLess(
            len(stem_angles), rectify_pages.MINIMUM_VERTICAL_STRUCTURES
        )

        barlines = np.broadcast_to(paper, (height, width)).copy()
        for staff_top in staff_tops:
            for offset in range(5):
                y = staff_top + offset * 9
                rectify_pages.cv2.line(
                    barlines, (35, y), (width - 35, y), 85, 2
                )
        for x in np.linspace(55, width - 55, 14, dtype=int):
            rectify_pages.cv2.line(
                barlines,
                (int(x), staff_tops[0] - 2),
                (int(x), staff_tops[1] + 38),
                25,
                2,
            )

        barline_angles, _, _ = rectify_pages.clustered_vertical_structures(
            barlines
        )

        self.assertGreater(len(barline_angles), 0)

    def test_common_vertical_tilt_without_convergence_requires_review(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        model = np.array([0.3, 0.05])
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", return_value=(0.0, 20)
            ),
            mock.patch.object(
                rectify_pages, "vertical_model", return_value=(model, 20, None)
            ),
            mock.patch.object(rectify_pages.cv2, "remap") as remap,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        remap.assert_not_called()
        self.assertEqual(metrics["vertical_status"], "review_required")
        self.assertEqual(metrics["status"], "review_required")

    def test_material_improvement_requires_epsilon_and_ratio(self) -> None:
        self.assertFalse(rectify_pages.materially_improved(0.30, 0.285))
        self.assertFalse(rectify_pages.materially_improved(0.30, 0.275))
        self.assertTrue(rectify_pages.materially_improved(0.30, 0.26))

    def test_rotation_clipping_detects_transformed_out_source_ink(self) -> None:
        source = np.full((80, 80), 172, np.uint8)
        source[:, :2] = 0
        matrix = np.array([[1.0, 0.0, -3.0], [0.0, 1.0, 0.0]])
        clipped, transformed_out, _ = rectify_pages.clipping_metrics(
            source, source.copy(), source_to_destination=matrix
        )
        self.assertTrue(clipped)
        self.assertGreater(transformed_out, 0)

    def test_equal_global_ink_cannot_hide_spatial_source_content_loss(self) -> None:
        source = np.full((100, 100), 255, np.uint8)
        source[20:80, 5:8] = 0
        candidate = np.full_like(source, 255)
        candidate[20:80, 70:73] = 0
        identity = np.array([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0]])

        clipped, transformed_out, ink_loss = rectify_pages.clipping_metrics(
            source,
            candidate,
            source_to_destination=identity,
        )

        self.assertTrue(clipped)
        self.assertEqual(transformed_out, 0.0)
        self.assertGreater(ink_loss, 0.99)

    def test_paper_tone_at_edge_is_not_treated_as_clipped_content(self) -> None:
        source = np.full((120, 100), 174, np.uint8)
        source[:, :8] = 168
        source[35:85, 35:65] = 45
        matrix = np.array([[1.0, 0.0, -3.0], [0.0, 1.0, 0.0]])
        candidate = rectify_pages.cv2.warpAffine(
            source,
            matrix,
            (source.shape[1], source.shape[0]),
            borderValue=174,
        )

        clipped, transformed_out, _ = rectify_pages.clipping_metrics(
            source,
            candidate,
            source_to_destination=matrix,
        )

        self.assertFalse(clipped)
        self.assertEqual(transformed_out, 0.0)

    def test_edge_content_clipping_reverts_an_otherwise_valid_rotation(self) -> None:
        source = np.full((160, 160), 174, np.uint8)
        source[:, :3] = 20
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=[(1.0, 20), (0.1, 20), (1.0, 20), (1.0, 20)],
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "warpAffine",
                return_value=source.copy(),
            ),
        ):
            corrected, metrics = rectify_pages.rectify(source)

        self.assertIs(corrected, source)
        self.assertTrue(metrics["horizontal_reverted"])
        self.assertTrue(metrics["horizontal_candidate_clipped"])
        self.assertEqual(
            metrics["horizontal_candidate_rejection_reason"],
            "candidate_would_clip_source_ink",
        )

    def test_foreground_mask_cleans_specks_but_preserves_thin_staff(self) -> None:
        source = np.full((120, 160), 176, np.uint8)
        source[60, 15:145] = 80
        source[20, 20] = 20
        source[90:94, 100:104] = 35

        foreground = rectify_pages.foreground_mask(source)

        self.assertFalse(foreground[20, 20])
        self.assertTrue(np.all(foreground[60, 15:145]))
        self.assertTrue(np.all(foreground[90:94, 100:104]))

    def test_forward_clipping_has_no_dilated_coverage_exception(self) -> None:
        source = np.full((20, 20), 255, np.uint8)
        source[10:12, 10:12] = 0
        forward_x = np.broadcast_to(
            np.arange(20, dtype=np.float32)[None, :], source.shape
        ).copy()
        forward_y = np.broadcast_to(
            np.arange(20, dtype=np.float32)[:, None], source.shape
        ).copy()
        forward_x[10, 10] = -0.01
        clipped, transformed_out, _ = rectify_pages.clipping_metrics(
            source,
            source.copy(),
            forward_x=forward_x,
            forward_y=forward_y,
        )
        self.assertTrue(clipped)
        self.assertEqual(transformed_out, 0.25)

    def test_common_tilt_is_not_part_of_convergence_residual(self) -> None:
        self.assertAlmostEqual(
            rectify_pages.convergence_residual(np.array([0.7, 0.1])),
            0.1,
        )

    def test_horizontal_fragments_from_one_line_are_not_independent(self) -> None:
        segments = np.asarray(
            [[index * 5.0, 20.0, index * 5.0 + 20.0, 20.0] for index in range(12)]
        )
        with mock.patch.object(
            rectify_pages, "line_segments", return_value=segments
        ):
            angle, samples = rectify_pages.horizontal_angle(
                np.full((100, 100), 255, np.uint8)
            )
        self.assertEqual((angle, samples), (0.0, 0))

    def test_reversed_horizontal_line_endpoints_have_same_angle(self) -> None:
        rise = float(np.tan(np.radians(1.0)) * 70.0)
        forward = np.asarray(
            [[10.0, y, 80.0, y + rise] for y in np.linspace(10.0, 80.0, 12)]
        )
        reversed_segments = forward[:, [2, 3, 0, 1]]
        image = np.full((100, 100), 255, np.uint8)
        with mock.patch.object(
            rectify_pages, "line_segments", return_value=forward
        ):
            forward_angle, forward_samples = rectify_pages.horizontal_angle(image)
        with mock.patch.object(
            rectify_pages, "line_segments", return_value=reversed_segments
        ):
            reversed_angle, reversed_samples = rectify_pages.horizontal_angle(image)

        self.assertEqual(forward_samples, 12)
        self.assertEqual(reversed_samples, 12)
        self.assertAlmostEqual(forward_angle, 1.0)
        self.assertAlmostEqual(reversed_angle, forward_angle)

    def test_conflicting_horizontal_modes_require_review_without_rotation(self) -> None:
        image = np.full((1000, 800), 255, np.uint8)
        segments = []
        for index, angle in enumerate([0.5] * 11 + [-0.5] * 10):
            y = 70.0 + index * 42.0
            rise = float(np.tan(np.radians(angle)) * 600.0)
            segments.append([100.0, y, 700.0, y + rise])
        with (
            mock.patch.object(
                rectify_pages,
                "line_segments",
                return_value=np.asarray(segments),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(rectify_pages.cv2, "warpAffine") as warp,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        warp.assert_not_called()
        self.assertEqual(metrics["horizontal_status"], "review_required")
        self.assertEqual(metrics["horizontal_reason"], "conflicting_horizontal_modes")
        self.assertTrue(metrics["review_required"])

    def test_opposing_discontinuous_fragments_in_same_center_bands_conflict(
        self,
    ) -> None:
        image = np.full((1000, 1000), 255, np.uint8)
        segments = []
        for center_y in np.linspace(80.0, 920.0, 10):
            for x1, x2, angle in (
                (50.0, 420.0, 0.5),
                (580.0, 950.0, -0.5),
            ):
                slope = float(np.tan(np.radians(angle)))
                y1 = center_y - (500.0 - x1) * slope
                y2 = y1 + (x2 - x1) * slope
                segments.append([x1, y1, x2, y2])
        with mock.patch.object(
            rectify_pages,
            "line_segments",
            return_value=np.asarray(segments),
        ):
            measurement = rectify_pages.horizontal_angle(image, detailed=True)

        self.assertEqual(measurement[0], 0.0)
        self.assertEqual(measurement[2], "conflicting_horizontal_modes")

    def test_horizontal_candidate_requires_held_out_band_improvement(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        measurements = [
            (0.5, 20, None, 0.5, 8, None),
            (0.02, 20, None, 0.48, 8, None),
            (0.5, 20, None, 0.5, 8, None),
            (0.5, 20, None, 0.5, 8, None),
        ]
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=measurements,
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "warpAffine",
                return_value=image.copy(),
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        self.assertTrue(metrics["horizontal_reverted"])
        self.assertEqual(
            metrics["horizontal_candidate_rejection_reason"],
            "held_out_horizontal_residual_did_not_materially_improve",
        )

    def test_sparse_horizontal_holdout_never_falls_back_to_all_structures(
        self,
    ) -> None:
        positions = np.asarray([0.05] * 5 + [0.18] * 2 + [0.55] * 5 + [0.68] * 2)
        angles = np.full(len(positions), 0.5)
        weights = np.ones(len(positions))
        with mock.patch.object(
            rectify_pages,
            "clustered_horizontal_structures",
            return_value=(angles, positions, weights),
        ):
            measurement = rectify_pages.horizontal_angle(
                np.full((100, 100), 255, np.uint8),
                detailed=True,
            )

        self.assertEqual(measurement[0], 0.0)
        self.assertEqual(
            measurement[2], "insufficient_holdout_horizontal_evidence"
        )
        self.assertIsNone(measurement[3])

    def test_horizontal_fit_and_holdout_are_disjoint_band_estimators(self) -> None:
        positions = np.asarray(
            [0.05] * 5 + [0.18] * 5 + [0.55] * 5 + [0.68] * 5
        )
        angles = np.asarray([0.45] * 5 + [0.55] * 5 + [0.45] * 5 + [0.55] * 5)
        weights = np.ones(len(positions))
        with mock.patch.object(
            rectify_pages,
            "clustered_horizontal_structures",
            return_value=(angles, positions, weights),
        ):
            measurement = rectify_pages.horizontal_angle(
                np.full((100, 100), 255, np.uint8),
                detailed=True,
            )

        self.assertAlmostEqual(measurement[0], 0.45)
        self.assertAlmostEqual(measurement[3], 0.55)
        self.assertEqual(measurement[1], 20)
        self.assertEqual(measurement[4], 10)

    def test_missing_band_cannot_swap_horizontal_fit_and_holdout_roles(self) -> None:
        structures = tuple(
            rectify_pages.HorizontalStructure(
                0.5,
                position,
                60.0,
                0.1,
                0.7,
                band,
                role,
            )
            for band, role, positions in (
                (0, "fit", (0.05, 0.06, 0.07)),
                (1, "holdout", (0.18, 0.19, 0.20)),
                (2, "fit", (0.30, 0.31, 0.32)),
                (3, "holdout", (0.43, 0.44, 0.45)),
                (4, "fit", (0.55, 0.56, 0.57)),
                (5, "holdout", (0.68, 0.69, 0.70)),
            )
            for position in positions
        )
        selection = rectify_pages.HorizontalSelection(100, 100, structures)
        surviving = [
            structure
            for structure in structures
            if not (structure.role == "holdout" and structure.band == 1)
        ]
        result = rectify_pages.tracked_horizontal_measurement(
            np.asarray([0.02] * len(surviving)),
            np.asarray([structure.position for structure in surviving]),
            np.asarray([structure.weight for structure in surviving]),
            [(structure.left, structure.right) for structure in surviving],
            selection,
        )

        self.assertEqual(result[2], "missing_tracked_holdout_horizontal_evidence")
        self.assertIsNone(result[3])

    def test_ambiguous_horizontal_identity_fails_closed(self) -> None:
        structures = tuple(
            rectify_pages.HorizontalStructure(
                0.5,
                position,
                60.0,
                0.1,
                0.7,
                band,
                role,
            )
            for band, role, positions in (
                (0, "fit", (0.05, 0.06, 0.07)),
                (1, "holdout", (0.18, 0.19, 0.20)),
                (2, "fit", (0.30, 0.31, 0.32)),
                (3, "holdout", (0.43, 0.44, 0.45)),
                (4, "fit", (0.55, 0.56, 0.57)),
                (5, "holdout", (0.68, 0.69, 0.70)),
            )
            for position in positions
        )
        selection = rectify_pages.HorizontalSelection(100, 100, structures)
        positions = [structure.position for structure in structures]
        ambiguous_index = next(
            index
            for index, structure in enumerate(structures)
            if structure.role == "holdout"
        )
        positions.append(structures[ambiguous_index].position + 0.001)
        extents = [(structure.left, structure.right) for structure in structures]
        extents.append(extents[ambiguous_index])

        result = rectify_pages.tracked_horizontal_measurement(
            np.asarray([0.02] * len(positions)),
            np.asarray(positions),
            np.asarray([60.0] * len(positions)),
            extents,
            selection,
        )

        self.assertEqual(result[2], "missing_tracked_holdout_horizontal_evidence")
        self.assertIsNone(result[3])

    def test_final_horizontal_validation_reuses_physical_identities(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        structures = tuple(
            rectify_pages.HorizontalStructure(
                0.5,
                position,
                60.0,
                0.1,
                0.8,
                band,
                role,
            )
            for band, role, positions in (
                (0, "fit", (0.05, 0.06, 0.07)),
                (1, "holdout", (0.18, 0.19, 0.20)),
                (2, "fit", (0.30, 0.31, 0.32)),
                (3, "holdout", (0.43, 0.44, 0.45)),
            )
            for position in positions
        )
        selection = rectify_pages.HorizontalSelection(60, 80, structures)
        calls = []

        def horizontal_measurement(
            _image: np.ndarray,
            tracked: object | None = None,
            transform: np.ndarray | None = None,
        ) -> tuple[object, ...]:
            calls.append((tracked, transform))
            if len(calls) == 1:
                return (0.5, 20, None, 0.5, 10, None, selection)
            return (0.02, 20, None, 0.02, 10, None, tracked)

        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_measurement",
                side_effect=horizontal_measurement,
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_measurement",
                return_value=(None, 0, "insufficient", np.empty(0), set()),
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "warpAffine",
                return_value=image.copy(),
            ),
            mock.patch.object(
                rectify_pages,
                "clipping_metrics",
                return_value=(False, 0.0, 0.0),
            ),
        ):
            _, metrics = rectify_pages.rectify(image)

        self.assertTrue(metrics["horizontal_applied"])
        self.assertIsNotNone(calls[-1][0])
        self.assertIsNone(calls[-1][1])

    def test_tracked_independent_horizontal_holdout_normally_improves(self) -> None:
        structures = tuple(
            rectify_pages.HorizontalStructure(
                0.5,
                position,
                60.0,
                0.1,
                0.7,
                band,
                role,
            )
            for band, role, positions in (
                (0, "fit", (0.05, 0.06, 0.07)),
                (1, "holdout", (0.18, 0.19, 0.20)),
                (2, "fit", (0.30, 0.31, 0.32)),
                (3, "holdout", (0.43, 0.44, 0.45)),
                (4, "fit", (0.55, 0.56, 0.57)),
                (5, "holdout", (0.68, 0.69, 0.70)),
            )
            for position in positions
        )
        selection = rectify_pages.HorizontalSelection(100, 100, structures)
        result = rectify_pages.tracked_horizontal_measurement(
            np.asarray([0.02] * len(structures)),
            np.asarray([structure.position for structure in structures]),
            np.asarray([structure.weight for structure in structures]),
            [(structure.left, structure.right) for structure in structures],
            selection,
        )

        self.assertIsNone(result[2])
        self.assertAlmostEqual(result[0], 0.02)
        self.assertIsNone(result[5])
        self.assertAlmostEqual(result[3], 0.02)
        self.assertTrue(rectify_pages.materially_improved(0.5, abs(result[3])))

    def test_missing_horizontal_holdout_cannot_self_validate_transform(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                return_value=(
                    0.5,
                    20,
                    "insufficient_holdout_horizontal_evidence",
                    None,
                    0,
                    "insufficient_holdout_horizontal_evidence",
                ),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(rectify_pages.cv2, "warpAffine") as warp,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        warp.assert_not_called()
        self.assertEqual(metrics["horizontal_status"], "low_confidence")
        self.assertFalse(metrics["horizontal_applied"])

    def test_candidate_with_missing_post_transform_holdout_is_reverted(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        measurements = [
            (0.5, 20, None, 0.5, 10, None),
            (
                0.02,
                20,
                "insufficient_holdout_horizontal_evidence",
                None,
                2,
                "insufficient_holdout_horizontal_evidence",
            ),
            (0.5, 20, None, 0.5, 10, None),
            (0.5, 20, None, 0.5, 10, None),
        ]
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=measurements,
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "warpAffine",
                return_value=image.copy(),
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        self.assertTrue(metrics["horizontal_reverted"])
        self.assertFalse(metrics["horizontal_applied"])
        self.assertEqual(
            metrics["horizontal_candidate_rejection_reason"],
            "remeasurement_had_insufficient_horizontal_evidence:"
            "insufficient_holdout_horizontal_evidence",
        )

    def test_fitted_vertical_structures_cannot_validate_themselves(self) -> None:
        image = np.full((600, 800), 255, np.uint8)
        fitted_positions = np.asarray([0.15, 0.35, 0.65, 0.85])
        for position in fitted_positions:
            x = round(position * (image.shape[1] - 1))
            rectify_pages.cv2.line(image, (x, 30), (x, 570), 0, 3)

        residual, samples, reason = rectify_pages.vertical_region_validation(
            image,
            fitted_positions,
            set(range(rectify_pages.VERTICAL_X_BAND_COUNT)),
        )

        self.assertIsNone(residual)
        self.assertEqual(samples, 0)
        self.assertIn(reason, {
            "insufficient_grouped_vertical_structures_on_both_sides",
            "insufficient_system_spanning_vertical_evidence_on_both_sides",
        })

    def test_slanted_fitted_structures_cannot_enter_validation_bands(self) -> None:
        height, width = 700, 900
        image = np.full((height, width), 255, np.uint8)
        structures = []
        for position, angle in ((0.18, -2.0), (0.38, 1.7), (0.62, -1.8), (0.82, 2.0)):
            center_x = position * (width - 1)
            offset = (height - 1) / 2 * np.tan(np.radians(angle))
            rectify_pages.cv2.line(
                image,
                (round(center_x - offset), 0),
                (round(center_x + offset), height - 1),
                0,
                3,
            )
            structures.append((position, angle, 2.0, 0.001, 0.35))

        residual, samples, reason = rectify_pages.vertical_region_validation(
            image,
            np.asarray(structures),
            set(range(rectify_pages.VERTICAL_X_BAND_COUNT)),
        )

        self.assertIsNone(residual)
        self.assertEqual(samples, 0)
        self.assertIn(reason, {
            "insufficient_grouped_vertical_structures_on_both_sides",
            "insufficient_system_spanning_vertical_evidence_on_both_sides",
        })

    def test_thick_fitted_structures_cannot_enter_validation_bands(self) -> None:
        height, width = 600, 800
        image = np.full((height, width), 255, np.uint8)
        structures = []
        for position in (0.16, 0.36, 0.64, 0.84):
            x = round(position * (width - 1))
            rectify_pages.cv2.line(image, (x, 0), (x, height - 1), 0, 21)
            structures.append((position, 0.0, 11.0, 0.001, 0.35))

        residual, samples, reason = rectify_pages.vertical_region_validation(
            image,
            np.asarray(structures),
            set(range(rectify_pages.VERTICAL_X_BAND_COUNT)),
        )

        self.assertIsNone(residual)
        self.assertEqual(samples, 0)
        self.assertIn(reason, {
            "insufficient_grouped_vertical_structures_on_both_sides",
            "insufficient_system_spanning_vertical_evidence_on_both_sides",
        })

    def test_applied_horizontal_residual_above_final_limit_requires_review(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        selection = rectify_pages.HorizontalSelection(
            60,
            80,
            (
                rectify_pages.HorizontalStructure(
                    0.5, 0.5, 40.0, 0.1, 0.9, 0, "fit"
                ),
            ),
        )
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=[
                    (0.5, 20, None, 0.5, 10, None, selection),
                    (0.05, 20, None, 0.05, 10, None, selection),
                    (0.05, 20, None, 0.05, 10, None, selection),
                    (0.05, 20, None, 0.05, 10, None, selection),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(
                rectify_pages.cv2, "warpAffine", return_value=image.copy()
            ),
        ):
            _, metrics = rectify_pages.rectify(image)
        self.assertTrue(metrics["horizontal_applied"])
        self.assertEqual(metrics["horizontal_status"], "review_required")
        self.assertEqual(metrics["status"], "review_required")
        self.assertIs(metrics["review_required"], True)

    def test_applied_convergence_above_final_limit_requires_review(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        models = [
            (np.array([0.0, 0.5]), 20, None),
            (np.array([0.0, 0.5]), 20, None),
            (np.array([0.0, 0.21]), 20, None),
            (np.array([0.0, 0.21]), 20, None),
        ]
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", return_value=(0.0, 20)
            ),
            mock.patch.object(
                rectify_pages, "vertical_model", side_effect=models
            ),
            mock.patch.object(
                rectify_pages.cv2, "remap", return_value=image.copy()
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_region_validation",
                side_effect=[(0.5, 200, None), (0.1, 200, None)],
            ),
        ):
            _, metrics = rectify_pages.rectify(image)
        self.assertTrue(metrics["vertical_applied"])
        self.assertEqual(metrics["vertical_status"], "review_required")
        self.assertEqual(metrics["status"], "review_required")

    def test_vertical_candidate_cannot_self_confirm_against_structural_holdout(
        self,
    ) -> None:
        image = np.full((80, 60), 255, np.uint8)
        models = [
            (np.array([0.0, 0.4]), 20, None),
            (np.array([0.0, 0.4]), 20, None),
            (np.array([0.0, 0.05]), 20, None),
            (np.array([0.0, 0.4]), 20, None),
        ]
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", return_value=(0.0, 20)
            ),
            mock.patch.object(
                rectify_pages, "vertical_model", side_effect=models
            ),
            mock.patch.object(
                rectify_pages.cv2, "remap", return_value=image.copy()
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_region_validation",
                side_effect=[(0.30, 200, None), (0.31, 200, None)],
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        self.assertTrue(metrics["vertical_reverted"])
        self.assertEqual(
            metrics["vertical_validation_before_convergence_differential"],
            0.30,
        )
        self.assertEqual(
            metrics["vertical_validation_after_convergence_differential"],
            0.31,
        )
        self.assertEqual(
            metrics["vertical_candidate_rejection_reason"],
            "independent_structural_convergence_did_not_materially_improve",
        )

    def _vertical_identity_selection(
        self,
        *,
        widened_band: int | None = None,
    ) -> object:
        structures = []
        for band in range(rectify_pages.VERTICAL_X_BAND_COUNT):
            for system, (offset, top, bottom) in enumerate(
                ((-0.018, 0.10, 0.30), (0.018, 0.65, 0.85))
            ):
                if band == widened_band:
                    offset = (-0.045, 0.045)[system]
                position = (band + 0.5) / rectify_pages.VERTICAL_X_BAND_COUNT + offset
                structures.append(
                    rectify_pages.VerticalStructure(
                        0.4 * (position - 0.5),
                        position,
                        60.0,
                        1.5,
                        0.002,
                        0.20,
                        top,
                        bottom,
                        band,
                        "fit" if band % 2 == 0 else "holdout",
                        system,
                    )
                )
        return rectify_pages.VerticalSelection(1000, 1000, tuple(structures))

    def _vertical_consensus_rejecting_band_on_call(
        self,
        rejected_call: int,
        rejected_band: int,
        maximum_rejections: int | None = None,
    ) -> object:
        original_consensus = rectify_pages.vertical_consensus
        consensus_call = 0

        def controlled_consensus(
            values: np.ndarray,
            positions: np.ndarray,
            weights: np.ndarray,
            *,
            public_mode: bool = True,
        ) -> tuple[np.ndarray, np.ndarray, float, str | None]:
            nonlocal consensus_call
            consensus_call += 1
            result = original_consensus(
                values,
                positions,
                weights,
                public_mode=public_mode,
            )
            if consensus_call != rejected_call:
                return result
            bands = np.clip(
                (positions * rectify_pages.VERTICAL_X_BAND_COUNT).astype(int),
                0,
                rectify_pages.VERTICAL_X_BAND_COUNT - 1,
            )
            keep = bands != rejected_band
            if maximum_rejections is not None:
                keep = np.ones(len(bands), dtype=bool)
                rejected = np.flatnonzero(bands == rejected_band)[
                    :maximum_rejections
                ]
                keep[rejected] = False
            return result[0], keep, result[2], None

        return controlled_consensus

    def test_fit_consensus_cannot_remove_required_vertical_x_band(self) -> None:
        with mock.patch.object(
            rectify_pages,
            "vertical_consensus",
            side_effect=self._vertical_consensus_rejecting_band_on_call(1, 0),
        ):
            result = rectify_pages.selected_vertical_measurement(
                self._vertical_identity_selection(widened_band=2)
            )

        self.assertIsNone(result[0])
        self.assertEqual(result[1], 6)
        self.assertEqual(
            result[2],
            "insufficient_independent_vertical_x_bands_on_both_sides"
            "_after_consensus_filtering",
        )

    def test_fit_count_reports_only_retained_consensus_evidence(self) -> None:
        with mock.patch.object(
            rectify_pages,
            "vertical_consensus",
            side_effect=self._vertical_consensus_rejecting_band_on_call(
                1,
                0,
                maximum_rejections=1,
            ),
        ):
            result = rectify_pages.selected_vertical_measurement(
                self._vertical_identity_selection()
            )

        self.assertIsNotNone(result[0])
        self.assertEqual(result[1], 7)
        self.assertIsNone(result[2])

    def test_below_threshold_rectify_reports_final_retained_fit_samples(
        self,
    ) -> None:
        image = np.full((80, 60), 255, np.uint8)
        before_selection = self._vertical_identity_selection()
        final_selection = before_selection._replace(
            structures=before_selection.structures[:-1]
        )
        before_fitted = rectify_pages.VerticalFitStructures(
            np.empty((0, 5)),
            before_selection,
        )
        final_fitted = rectify_pages.VerticalFitStructures(
            np.empty((0, 5)),
            final_selection,
        )
        raw_before = len(before_selection.structures)
        raw_after = len(final_selection.structures)
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_measurement",
                return_value=(0.0, 20, None, 0.0, 10, None, None),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_measurement",
                side_effect=[
                    (np.array([0.0, 0.1]), raw_before, None, before_fitted, set()),
                    (np.array([0.0, 0.1]), raw_before, None, before_fitted, set()),
                    (np.array([0.0, 0.1]), raw_after, None, final_fitted, set()),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "selected_vertical_measurement",
                side_effect=[
                    (np.array([0.0, 0.1]), 7, None, 0.1, 8, None),
                    (np.array([0.0, 0.1]), 6, None, 0.1, 7, None),
                ],
            ) as selected_measurement,
            mock.patch.object(rectify_pages.cv2, "remap") as remap,
            mock.patch.object(
                rectify_pages,
                "clipping_metrics",
                return_value=(False, 0.0, 0.0),
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        remap.assert_not_called()
        self.assertEqual(metrics["status"], "unchanged")
        self.assertEqual(metrics["vertical_status"], "not_needed")
        self.assertFalse(metrics["vertical_applied"])
        self.assertFalse(metrics["vertical_reverted"])
        self.assertEqual(metrics["vertical_samples"], 7)
        self.assertEqual(metrics["vertical_samples_after"], 6)
        self.assertLess(metrics["vertical_samples_after"], raw_after)
        self.assertIs(
            selected_measurement.call_args_list[0].args[0],
            before_selection,
        )
        self.assertIs(
            selected_measurement.call_args_list[1].args[0],
            final_selection,
        )

    def test_vertical_report_measurement_keeps_raw_no_selection_fallbacks(
        self,
    ) -> None:
        image = np.full((80, 60), 255, np.uint8)
        selection = self._vertical_identity_selection()
        selected_fitted = rectify_pages.VerticalFitStructures(
            np.empty((0, 5)),
            selection,
        )
        for selected_population, fitted_structures in (
            (False, selected_fitted),
            (True, np.empty((0, 5))),
        ):
            with (
                self.subTest(selected_population=selected_population),
                mock.patch.object(
                    rectify_pages,
                    "vertical_measurement",
                    return_value=(
                        np.array([0.0, 0.1]),
                        12,
                        None,
                        fitted_structures,
                        set(),
                    ),
                ),
                mock.patch.object(
                    rectify_pages,
                    "selected_vertical_measurement",
                ) as selected_measurement,
            ):
                measurement = rectify_pages.vertical_report_measurement(
                    image,
                    selected_population=selected_population,
                )

            self.assertEqual(measurement[1], 12)
            selected_measurement.assert_not_called()

    def test_rectify_metrics_report_only_retained_vertical_fit_samples(
        self,
    ) -> None:
        image = np.full((80, 60), 255, np.uint8)
        selection = self._vertical_identity_selection()
        fitted = rectify_pages.VerticalFitStructures(np.empty((0, 5)), selection)
        raw_samples = len(selection.structures)
        tracked_measurement = (
            np.array([0.0, 0.05]),
            6,
            None,
            0.02,
            8,
            None,
            selection,
        )
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_measurement",
                return_value=(0.0, 20, None, 0.0, 10, None, None),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_measurement",
                return_value=(
                    np.array([0.0, 0.4]),
                    raw_samples,
                    None,
                    fitted,
                    set(),
                ),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_consensus",
                side_effect=self._vertical_consensus_rejecting_band_on_call(
                    1,
                    0,
                    maximum_rejections=1,
                ),
            ),
            mock.patch.object(
                rectify_pages,
                "tracked_vertical_measurement",
                return_value=tracked_measurement,
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "remap",
                return_value=image.copy(),
            ),
            mock.patch.object(
                rectify_pages,
                "clipping_metrics",
                return_value=(False, 0.0, 0.0),
            ),
        ):
            _, metrics = rectify_pages.rectify(image)

        self.assertTrue(metrics["vertical_applied"])
        self.assertLess(metrics["vertical_samples"], raw_samples)
        self.assertEqual(metrics["vertical_samples"], 7)
        self.assertEqual(metrics["vertical_samples_after"], 6)

    def test_cumulative_revert_reports_restored_retained_fit_samples(
        self,
    ) -> None:
        image = np.full((80, 60), 255, np.uint8)
        before_selection = self._vertical_identity_selection()
        restored_selection = before_selection._replace(
            structures=before_selection.structures[:-1]
        )
        before_fitted = rectify_pages.VerticalFitStructures(
            np.empty((0, 5)),
            before_selection,
        )
        restored_fitted = rectify_pages.VerticalFitStructures(
            np.empty((0, 5)),
            restored_selection,
        )
        raw_before = len(before_selection.structures)
        raw_after = len(restored_selection.structures)
        tracked_measurement = (
            np.array([0.0, 0.05]),
            6,
            None,
            0.05,
            8,
            None,
            before_selection,
        )
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_measurement",
                return_value=(0.0, 20, None, 0.0, 10, None, None),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_measurement",
                side_effect=[
                    (np.array([0.0, 0.4]), raw_before, None, before_fitted, set()),
                    (np.array([0.0, 0.4]), raw_before, None, before_fitted, set()),
                    (
                        np.array([0.0, 0.4]),
                        raw_after,
                        None,
                        restored_fitted,
                        set(),
                    ),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "selected_vertical_measurement",
                side_effect=[
                    (np.array([0.0, 0.4]), 7, None, 0.3, 8, None),
                    (np.array([0.0, 0.4]), 5, None, 0.3, 7, None),
                ],
            ) as selected_measurement,
            mock.patch.object(
                rectify_pages,
                "tracked_vertical_measurement",
                return_value=tracked_measurement,
            ),
            mock.patch.object(
                rectify_pages.cv2,
                "remap",
                return_value=image.copy(),
            ) as remap,
            mock.patch.object(
                rectify_pages,
                "clipping_metrics",
                side_effect=[(False, 0.0, 0.0), (True, 0.01, 0.02)],
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        remap.assert_called_once()
        self.assertFalse(metrics["vertical_applied"])
        self.assertTrue(metrics["vertical_reverted"])
        self.assertTrue(metrics["cumulative_clipping_reverted"])
        self.assertIsNone(metrics["vertical_candidate_rejection_reason"])
        self.assertEqual(metrics["status"], "review_required")
        self.assertEqual(metrics["vertical_samples"], 7)
        self.assertEqual(metrics["vertical_samples_after"], 5)
        self.assertLess(metrics["vertical_samples_after"], raw_after)
        self.assertIs(
            selected_measurement.call_args_list[0].args[0],
            before_selection,
        )
        self.assertIs(
            selected_measurement.call_args_list[1].args[0],
            restored_selection,
        )

    def test_holdout_consensus_cannot_remove_required_vertical_x_band(self) -> None:
        with mock.patch.object(
            rectify_pages,
            "vertical_consensus",
            side_effect=self._vertical_consensus_rejecting_band_on_call(2, 1),
        ):
            result = rectify_pages.selected_vertical_measurement(
                self._vertical_identity_selection(widened_band=3)
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[4], 6)
        self.assertEqual(
            result[5],
            "insufficient_independent_vertical_x_bands_on_both_sides"
            "_after_consensus_filtering",
        )

    def test_side_consensus_cannot_remove_required_vertical_x_band(self) -> None:
        with mock.patch.object(
            rectify_pages,
            "vertical_consensus",
            side_effect=self._vertical_consensus_rejecting_band_on_call(3, 1),
        ):
            result = rectify_pages.selected_vertical_measurement(
                self._vertical_identity_selection(widened_band=3)
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[4], 6)
        self.assertEqual(
            result[5],
            "insufficient_independent_vertical_x_bands_on_both_sides"
            "_after_consensus_filtering",
        )

    def test_single_system_vertical_holdout_cannot_validate_page_warp(self) -> None:
        selection = self._vertical_identity_selection()
        structures = tuple(
            structure._replace(top=0.38, bottom=0.58, system=0)
            if structure.role == "holdout"
            else structure
            for structure in selection.structures
        )

        result = rectify_pages.selected_vertical_measurement(
            selection._replace(structures=structures)
        )

        self.assertIsNone(result[3])
        self.assertEqual(
            result[5],
            "insufficient_vertical_system_diversity_on_both_sides",
        )

    def test_multi_system_vertical_holdout_validates_distributed_coverage(self) -> None:
        result = rectify_pages.selected_vertical_measurement(
            self._vertical_identity_selection()
        )

        self.assertIsNotNone(result[3])
        self.assertIsNone(result[5])

    def test_tracked_holdout_systems_collapsing_to_one_region_fail_closed(self) -> None:
        selection = self._vertical_identity_selection()
        selection = selection._replace(
            structures=tuple(
                structure._replace(
                    top=0.20 if structure.system == 0 else 0.40,
                    bottom=0.40 if structure.system == 0 else 0.60,
                )
                if structure.role == "holdout"
                else structure
                for structure in selection.structures
            )
        )
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        angles, positions, weights, clusters = self._detected_vertical_selection(
            projected
        )
        clusters = [
            [
                (
                    item[0],
                    item[1],
                    item[2],
                    item[3],
                    item[4],
                    0.25 if structure.system == 0 else 0.32,
                    0.48 if structure.system == 0 else 0.55,
                )
                for item in cluster
            ]
            if structure.role == "holdout"
            else cluster
            for structure, cluster in zip(projected.structures, clusters)
        ]
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(
            result[5],
            "insufficient_vertical_system_diversity_on_both_sides",
        )

    def _detected_vertical_selection(
        self,
        selection: object,
        *,
        missing_index: int | None = None,
        switched_index: int | None = None,
        switched_angle: float = 0.8,
    ) -> tuple[np.ndarray, np.ndarray, np.ndarray, list[list[tuple[float, ...]]]]:
        structures = [
            structure
            for index, structure in enumerate(selection.structures)
            if index != missing_index
        ]
        angles = np.asarray(
            [
                structure.angle
                + (switched_angle if index == switched_index else 0.0)
                for index, structure in enumerate(selection.structures)
                if index != missing_index
            ]
        )
        positions = np.asarray([structure.position for structure in structures])
        weights = np.asarray([structure.weight for structure in structures])
        clusters = [
            [
                (
                    structure.position,
                    angle,
                    structure.weight,
                    structure.position_uncertainty,
                    structure.half_width,
                    structure.top,
                    structure.bottom,
                )
            ]
            for structure, angle in zip(structures, angles)
        ]
        return angles, positions, weights, clusters

    def test_disappearing_vertical_holdout_identity_fails_closed(self) -> None:
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        holdout_index = next(
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        )
        detected = self._detected_vertical_selection(
            projected,
            missing_index=holdout_index,
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[5], "missing_tracked_holdout_vertical_evidence")

    def test_switched_vertical_mode_identity_fails_closed(self) -> None:
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        holdout_index = next(
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        )
        detected = self._detected_vertical_selection(
            projected,
            switched_index=holdout_index,
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[5], "missing_tracked_holdout_vertical_evidence")

    def test_point_40_degree_holdout_mode_switch_fails_closed(self) -> None:
        self.assertLessEqual(
            rectify_pages.VERTICAL_TRACK_ANGLE_TOLERANCE_DEGREES,
            rectify_pages.VERTICAL_CONFLICT_SEPARATION_DEGREES,
        )
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        holdout_index = next(
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        )
        detected = self._detected_vertical_selection(
            projected,
            switched_index=holdout_index,
            switched_angle=0.40,
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[5], "missing_tracked_holdout_vertical_evidence")

    def test_minor_vertical_holdout_angle_jitter_preserves_identity(self) -> None:
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        holdout_index = next(
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        )
        detected = self._detected_vertical_selection(
            projected,
            switched_index=holdout_index,
            switched_angle=0.10,
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNotNone(result[3])
        self.assertIsNone(result[5])

    def test_ambiguous_vertical_holdout_identity_fails_closed(self) -> None:
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        holdout_index = next(
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        )
        angles, positions, weights, clusters = self._detected_vertical_selection(
            projected
        )
        angles = np.append(angles, angles[holdout_index])
        positions = np.append(positions, positions[holdout_index] + 0.001)
        weights = np.append(weights, weights[holdout_index])
        clusters.append(
            [
                (
                    positions[-1],
                    angles[-1],
                    weights[-1],
                    projected.structures[holdout_index].position_uncertainty,
                    projected.structures[holdout_index].half_width,
                    projected.structures[holdout_index].top,
                    projected.structures[holdout_index].bottom,
                )
            ]
        )
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=(angles, positions, weights, clusters),
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[5], "missing_tracked_holdout_vertical_evidence")

    def test_unilateral_minority_vertical_holdout_mode_fails_closed(self) -> None:
        selection = self._vertical_identity_selection()
        left_holdout = [
            index
            for index, structure in enumerate(selection.structures)
            if structure.role == "holdout" and structure.position < 0.5
        ][:2]
        structures = tuple(
            structure._replace(angle=structure.angle + 0.8)
            if index in left_holdout
            else structure
            for index, structure in enumerate(selection.structures)
        )

        result = rectify_pages.selected_vertical_measurement(
            selection._replace(structures=structures)
        )

        self.assertIsNone(result[3])
        self.assertEqual(result[4], 6)
        self.assertEqual(result[5], "conflicting_evidence")

    def test_normal_vertical_holdout_sides_retain_consensus(self) -> None:
        result = rectify_pages.selected_vertical_measurement(
            self._vertical_identity_selection()
        )

        self.assertIsNotNone(result[3])
        self.assertEqual(result[4], 8)
        self.assertIsNone(result[5])

    def test_tracked_vertical_holdout_excludes_side_consensus_outlier(self) -> None:
        selection = self._vertical_identity_selection()
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        angles, positions, weights, clusters = self._detected_vertical_selection(
            projected
        )
        holdout_indices = [
            index
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout"
        ]
        rejected_index = holdout_indices[0]
        angles[rejected_index] += 0.34
        clusters[rejected_index][0] = (
            clusters[rejected_index][0][0],
            angles[rejected_index],
            *clusters[rejected_index][0][2:],
        )
        original_consensus = rectify_pages.vertical_consensus
        consensus_call = 0

        def controlled_consensus(
            values: np.ndarray,
            side_positions: np.ndarray,
            side_weights: np.ndarray,
            *,
            public_mode: bool = True,
        ) -> tuple[np.ndarray, np.ndarray, float, str | None]:
            nonlocal consensus_call
            consensus_call += 1
            result = original_consensus(
                values,
                side_positions,
                side_weights,
                public_mode=public_mode,
            )
            if consensus_call == 2:
                return result[0], np.ones(len(values), dtype=bool), result[2], None
            if consensus_call == 3:
                keep = np.ones(len(values), dtype=bool)
                keep[0] = False
                return result[0], keep, result[2], None
            return result

        with (
            mock.patch.object(
                rectify_pages,
                "clustered_vertical_structures",
                return_value=(angles, positions, weights, clusters),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_consensus",
                side_effect=controlled_consensus,
            ),
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        measured_holdout = [
            structure
            for index, structure in enumerate(projected.structures)
            if structure.role == "holdout" and index != rejected_index
        ]
        expected_sides = [
            np.average(
                [
                    structure.angle
                    for structure in measured_holdout
                    if side(structure)
                ],
                weights=[
                    structure.weight
                    for structure in measured_holdout
                    if side(structure)
                ],
            )
            for side in (
                lambda structure: structure.position < 0.5,
                lambda structure: structure.position > 0.5,
            )
        ]
        self.assertAlmostEqual(result[3], abs(expected_sides[1] - expected_sides[0]))
        self.assertEqual(result[4], 7)
        self.assertIsNone(result[5])

    def test_tracked_vertical_holdout_side_conflict_fails_closed(self) -> None:
        selection = self._vertical_identity_selection()
        left_holdout = [
            index
            for index, structure in enumerate(selection.structures)
            if structure.role == "holdout" and structure.position < 0.5
        ][:2]
        selection = selection._replace(
            structures=tuple(
                structure._replace(angle=structure.angle + 0.8)
                if index in left_holdout
                else structure
                for index, structure in enumerate(selection.structures)
            )
        )
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        detected = self._detected_vertical_selection(projected)
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            result = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(result[3])
        self.assertEqual(result[4], 6)
        self.assertEqual(result[5], "conflicting_evidence")

    def test_tracked_vertical_identities_normally_converge(self) -> None:
        selection = self._vertical_identity_selection()
        before = rectify_pages.selected_vertical_measurement(selection)
        projected = rectify_pages.project_vertical_selection(selection, 0.4)
        detected = self._detected_vertical_selection(projected)
        with mock.patch.object(
            rectify_pages,
            "clustered_vertical_structures",
            return_value=detected,
        ):
            after = rectify_pages.tracked_vertical_measurement(
                np.full((1000, 1000), 255, np.uint8),
                selection,
                0.4,
            )

        self.assertIsNone(after[2])
        self.assertIsNone(after[5])
        self.assertEqual(after[4], 8)
        self.assertTrue(
            rectify_pages.materially_improved(
                rectify_pages.convergence_residual(before[0]),
                rectify_pages.convergence_residual(after[0]),
            )
        )
        self.assertTrue(rectify_pages.materially_improved(before[3], after[3]))

    def test_conflicting_vertical_holdout_never_attempts_transform(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(
                rectify_pages, "horizontal_angle", return_value=(0.0, 20)
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(np.array([0.0, 0.4]), 20, None),
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_region_validation",
                return_value=(None, 6, "conflicting_evidence"),
            ),
            mock.patch.object(rectify_pages.cv2, "remap") as remap,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        remap.assert_not_called()
        self.assertFalse(metrics["vertical_applied"])
        self.assertFalse(metrics["vertical_reverted"])
        self.assertEqual(metrics["vertical_status"], "review_required")
        self.assertEqual(metrics["vertical_reason"], "conflicting_evidence")

    def test_true_barline_convergence_converges(
        self,
    ) -> None:
        height, width = 900, 700
        image = np.full((height, width), 255, np.uint8)
        for x in np.linspace(70, width - 70, 18):
            angle = 0.30 * (x / (width - 1) - 0.5)
            offset = np.tan(np.radians(angle)) * (height - 160) / 2
            cv2 = rectify_pages.cv2
            cv2.line(
                image,
                (round(x - offset), 80),
                (round(x + offset), height - 80),
                0,
                3,
                cv2.LINE_AA,
            )
        for y in range(120, height - 100, 55):
            rectify_pages.cv2.line(image, (45, y), (width - 45, y), 80, 2)
        image = rectify_pages.cv2.GaussianBlur(image, (0, 0), 1.6)

        corrected, metrics = rectify_pages.rectify(image)
        residual, _, reason = rectify_pages.vertical_region_validation(corrected)

        self.assertTrue(metrics["vertical_applied"], metrics)
        self.assertIsNone(reason)
        self.assertIsNotNone(residual)
        self.assertLess(residual, 0.12)
        self.assertLessEqual(
            metrics["vertical_after_convergence_differential"],
            metrics["vertical_before_convergence_differential"],
        )

    def test_cumulative_clipping_reverts_to_original(self) -> None:
        image = np.full((80, 60), 255, np.uint8)
        with (
            mock.patch.object(
                rectify_pages,
                "horizontal_angle",
                side_effect=[
                    (0.5, 20),
                    (0.01, 20),
                    (0.01, 20),
                    (0.01, 20),
                    (0.5, 20),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "vertical_model",
                return_value=(None, 0, "insufficient"),
            ),
            mock.patch.object(
                rectify_pages.cv2, "warpAffine", return_value=image.copy()
            ),
            mock.patch.object(
                rectify_pages,
                "clipping_metrics",
                side_effect=[(False, 0.0, 0.0), (True, 0.01, 0.02)],
            ),
        ):
            corrected, metrics = rectify_pages.rectify(image)
        self.assertIs(corrected, image)
        self.assertTrue(metrics["horizontal_reverted"])
        self.assertTrue(metrics["cumulative_clipping_reverted"])
        self.assertEqual(metrics["cumulative_ink_loss_ratio"], 0.02)


class OutputSafetyRegressionTests(unittest.TestCase):
    work = Path(__file__).parent / ".work"

    def setUp(self) -> None:
        shutil.rmtree(self.work, ignore_errors=True)
        self.input = self.work / "input"
        self.output = self.work / "output"
        self.input.mkdir(parents=True)

    def tearDown(self) -> None:
        shutil.rmtree(self.work, ignore_errors=True)

    def run_main(self, *arguments: str) -> SystemExit:
        if "--report" not in arguments:
            arguments = (*arguments, "--report", str(self.output / "report.json"))
        with mock.patch.object(
            sys, "argv", ["rectify_pages.py", *arguments]
        ):
            with self.assertRaises(SystemExit) as raised:
                rectify_pages.main()
        return raised.exception

    def test_report_is_mandatory(self) -> None:
        self.write_page("page.jpg")
        with mock.patch.object(
            sys,
            "argv",
            ["rectify_pages.py", str(self.input), str(self.output)],
        ):
            with self.assertRaises(SystemExit) as raised:
                rectify_pages.main()
        self.assertEqual(raised.exception.code, 2)
        self.assertFalse(self.output.exists())

    def write_page(self, name: str) -> Path:
        path = self.input / name
        ok, encoded = rectify_pages.cv2.imencode(
            Path(name).suffix, np.full((10, 10), 255, np.uint8)
        )
        self.assertTrue(ok)
        path.write_bytes(encoded.tobytes())
        return path

    def test_duplicate_input_stems_are_rejected(self) -> None:
        self.write_page("page.jpg")
        self.write_page("page.png")
        error = self.run_main(str(self.input), str(self.output))
        self.assertEqual(error.code, 2)
        self.assertFalse(self.output.exists())

    def test_existing_output_directory_is_rejected(self) -> None:
        self.write_page("page.jpg")
        self.output.mkdir()
        error = self.run_main(str(self.input), str(self.output))
        self.assertEqual(error.code, 2)

    def test_report_requires_json_suffix(self) -> None:
        self.write_page("page.jpg")
        error = self.run_main(
            str(self.input),
            str(self.output),
            "--report",
            str(self.work / "report.txt"),
        )
        self.assertEqual(error.code, 2)
        self.assertFalse(self.output.exists())

    def test_external_report_is_rejected(self) -> None:
        self.write_page("page.jpg")
        report = self.work / "report.json"
        result = self.run_main(
            str(self.input),
            str(self.output),
            "--report",
            str(report),
        )
        self.assertEqual(result.code, 2)
        self.assertFalse(self.output.exists())

    def test_ntfs_alternate_data_stream_outputs_are_rejected(self) -> None:
        self.write_page("page.jpg")
        for output, report in (
            (
                f"{self.output}:stream",
                str(self.output / "report.json"),
            ),
            (
                str(self.output),
                f"{self.output / 'report.json'}:stream",
            ),
            (
                str(self.output),
                str(self.output / "nested" / "report.json"),
            ),
        ):
            with self.subTest(output=output, report=report):
                error = self.run_main(
                    str(self.input),
                    output,
                    "--report",
                    report,
                )
                self.assertEqual(error.code, 2)
                self.assertFalse(self.output.exists())

    def test_pages_must_be_positive_and_unique(self) -> None:
        self.write_page("page_1.jpg")
        for values in (("0",), ("1", "1")):
            with self.subTest(values=values):
                error = self.run_main(
                    str(self.input), str(self.output), "--pages", *values
                )
                self.assertEqual(error.code, 2)
                self.assertFalse(self.output.exists())

    def test_pages_fail_when_missing_or_ambiguous(self) -> None:
        self.write_page("left_1.jpg")
        self.write_page("right_1.jpg")
        ambiguous = self.run_main(
            str(self.input), str(self.output), "--pages", "1"
        )
        self.assertEqual(ambiguous.code, 2)
        missing = self.run_main(
            str(self.input), str(self.output), "--pages", "2"
        )
        self.assertEqual(missing.code, 2)
        self.assertFalse(self.output.exists())

    def test_frame_count_is_checked_for_every_format(self) -> None:
        path = self.input / "page.png"
        frames = [
            rectify_pages.Image.new("L", (10, 10), 255),
            rectify_pages.Image.new("L", (10, 10), 0),
        ]
        frames[0].save(path, save_all=True, append_images=frames[1:])
        error = self.run_main(str(self.input), str(self.output))
        self.assertEqual(error.code, 2)
        self.assertFalse(self.output.exists())

    def test_frame_inspection_and_decode_share_one_immutable_buffer(self) -> None:
        content = b"immutable image bytes"
        frame = np.full((10, 10), 255, np.uint8)
        path = self.input / "page.png"
        path.write_bytes(content)

        def inspect(buffer: bytes, path: Path):
            self.assertEqual(buffer, content)
            self.assertIsInstance(buffer, bytes)
            return frame

        with (
            mock.patch.object(
                rectify_pages, "decode_single_frame", side_effect=inspect
            ) as decode,
        ):
            image, digest = rectify_pages.read_page(path)

        self.assertIs(image, frame)
        self.assertEqual(digest, hashlib.sha256(content).hexdigest())
        decode.assert_called_once()

    def test_encoded_byte_limit_is_checked_before_read_allocation(self) -> None:
        path = self.input / "page.png"
        with (
            mock.patch.object(
                Path,
                "stat",
                return_value=mock.Mock(
                    st_size=rectify_pages.MAX_ENCODED_BYTES + 1
                ),
            ),
            mock.patch.object(Path, "open") as open_file,
            self.assertRaisesRegex(ValueError, "encoded byte limit exceeded"),
        ):
            rectify_pages.read_page(path)
        open_file.assert_not_called()

    def test_decoded_pixel_and_working_memory_limits_fail_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "decoded pixel limit exceeded"):
            rectify_pages.validate_image_limits(
                rectify_pages.MAX_DECODED_PIXELS + 1,
                1,
                "L",
                Path("page.png"),
            )
        pixels = rectify_pages.MAX_WORKING_MEMORY_BYTES // (
            rectify_pages.WORKING_MEMORY_BYTES_PER_PIXEL
        ) + 1
        with self.assertRaisesRegex(ValueError, "working-memory limit exceeded"):
            rectify_pages.validate_image_limits(
                pixels,
                1,
                "L",
                Path("page.png"),
            )

    def test_33000_by_100_page_fails_closed_with_review_metrics(self) -> None:
        image = np.full((100, 33_000), 255, np.uint8)
        with (
            mock.patch.object(rectify_pages.cv2, "resize") as resize,
            mock.patch.object(rectify_pages.cv2, "remap") as remap,
            mock.patch.object(rectify_pages.cv2, "warpAffine") as warp,
        ):
            corrected, metrics = rectify_pages.rectify(image)

        self.assertIs(corrected, image)
        self.assertEqual(metrics["status"], "review_required")
        self.assertTrue(metrics["review_required"])
        self.assertIn(
            "opencv_dimension_limit_exceeded",
            metrics["horizontal_reason"],
        )
        self.assertTrue(metrics["unchanged"])
        resize.assert_not_called()
        remap.assert_not_called()
        warp.assert_not_called()

    def test_33000_by_100_page_publishes_unchanged_review_report(self) -> None:
        page = self.write_page("page.png")
        image = np.full((100, 33_000), 255, np.uint8)
        digest = hashlib.sha256(page.read_bytes()).hexdigest()
        with (
            mock.patch.object(
                rectify_pages,
                "read_page",
                return_value=(image, digest),
            ),
            mock.patch.object(
                rectify_pages,
                "file_sha256",
                return_value=digest,
            ),
            mock.patch.object(
                sys,
                "argv",
                [
                    "rectify_pages.py",
                    str(self.input),
                    str(self.output),
                    "--report",
                    str(self.output / "report.json"),
                ],
            ),
        ):
            self.assertEqual(rectify_pages.main(), 0)

        report = json.loads(
            (self.output / "report.json").read_text(encoding="utf-8")
        )
        self.assertEqual(report[0]["status"], "review_required")
        self.assertTrue(report[0]["review_required"])
        self.assertTrue(report[0]["unchanged"])
        self.assertEqual(
            rectify_pages.cv2.imread(
                str(self.output / "page.png"),
                rectify_pages.cv2.IMREAD_UNCHANGED,
            ).shape,
            image.shape,
        )

    def test_floating_point_tiff_is_rejected(self) -> None:
        encoded = io.BytesIO()
        rectify_pages.Image.fromarray(
            np.array([[0.0, 0.5, 1.0]], dtype=np.float32)
        ).save(encoded, format="TIFF")
        with self.assertRaisesRegex(ValueError, "floating-point images are unsupported"):
            rectify_pages.decode_single_frame(
                encoded.getvalue(), Path("page.tiff")
            )

    def test_png_and_webp_alpha_are_composited_onto_white(self) -> None:
        image = rectify_pages.Image.new("RGBA", (3, 1))
        image.putdata(
            [
                (0, 0, 0, 0),
                (0, 0, 0, 128),
                (0, 0, 0, 255),
            ]
        )
        for image_format in ("PNG", "WEBP"):
            with self.subTest(image_format=image_format):
                encoded = io.BytesIO()
                image.save(encoded, format=image_format, lossless=True)
                decoded = rectify_pages.decode_single_frame(
                    encoded.getvalue(), Path(f"page.{image_format.lower()}")
                )
                self.assertEqual(decoded[0, 0], 255)
                self.assertIn(int(decoded[0, 1]), range(126, 129))
                self.assertEqual(decoded[0, 2], 0)

    def test_exif_orientation_is_applied_from_png_bytes(self) -> None:
        image = rectify_pages.Image.fromarray(
            np.array([[10, 20], [30, 40], [50, 60]], dtype=np.uint8)
        )
        exif = image.getexif()
        exif[274] = 6
        encoded = io.BytesIO()
        image.save(encoded, format="PNG", exif=exif)

        decoded = rectify_pages.decode_single_frame(
            encoded.getvalue(), Path("oriented.png")
        )

        np.testing.assert_array_equal(
            decoded,
            np.array([[50, 30, 10], [60, 40, 20]], dtype=np.uint8),
        )

    def test_16_bit_png_and_tiff_preserve_real_ink_values(self) -> None:
        source = np.full((64, 80), 65535, np.uint16)
        source[20:44, 39:42] = 0
        source[31:34, 12:68] = 12345
        for image_format in ("PNG", "TIFF"):
            with self.subTest(image_format=image_format):
                encoded = io.BytesIO()
                rectify_pages.Image.fromarray(source).save(
                    encoded, format=image_format
                )
                decoded = rectify_pages.decode_single_frame(
                    encoded.getvalue(), Path(f"page.{image_format.lower()}")
                )
                self.assertEqual(decoded.dtype, np.uint16)
                np.testing.assert_array_equal(decoded, source)

        input_path = self.input / "page.png"
        rectify_pages.Image.fromarray(source).save(input_path)
        with mock.patch.object(
            sys,
            "argv",
            [
                "rectify_pages.py",
                str(self.input),
                str(self.output),
                "--report",
                str(self.output / "report.json"),
            ],
        ):
            self.assertEqual(rectify_pages.main(), 0)
        output = rectify_pages.cv2.imread(
            str(self.output / "page.png"), rectify_pages.cv2.IMREAD_UNCHANGED
        )
        self.assertEqual(output.dtype, np.uint16)
        self.assertEqual(int(output[32, 20]), 12345)
        report = json.loads((self.output / "report.json").read_text())
        self.assertEqual(report[0]["input_bit_depth"], 16)
        self.assertEqual(report[0]["output_bit_depth"], 16)

    def test_unsigned_16_bit_tiff_photometric_polarity_is_honored_once(
        self,
    ) -> None:
        expected = np.array(
            [[0, 1000, 32000], [40000, 65000, 65535]],
            dtype=np.uint16,
        )
        oriented_expected = np.rot90(expected, k=3)
        for byte_order in ("II", "MM"):
            for compression in (1, 8):
                for photometric in (0, 1):
                    with self.subTest(
                        byte_order=byte_order,
                        compression=compression,
                        photometric=photometric,
                    ):
                        stored = (
                            np.bitwise_not(expected)
                            if photometric == 0
                            else expected
                        )
                        encoded = encoded_uint16_tiff(
                            stored,
                            byte_order=byte_order,
                            compression=compression,
                            photometric=photometric,
                            orientation=6,
                        )

                        decoded = rectify_pages.decode_single_frame(
                            encoded,
                            Path("page.tiff"),
                        )

                        self.assertEqual(decoded.dtype, np.uint16)
                        np.testing.assert_array_equal(decoded, oriented_expected)

    def test_16_bit_grayscale_png_trns_composites_at_uint16_depth(self) -> None:
        source = np.array(
            [[0, 1000, 32768, 65534, 65535]],
            dtype=np.uint16,
        )
        encoded = io.BytesIO()
        rectify_pages.Image.fromarray(source).save(
            encoded,
            format="PNG",
            transparency=32768,
        )

        decoded = rectify_pages.decode_single_frame(
            encoded.getvalue(),
            Path("transparent-16.png"),
        )

        self.assertEqual(decoded.dtype, np.uint16)
        np.testing.assert_array_equal(
            decoded,
            np.array([[0, 1000, 65535, 65534, 65535]], dtype=np.uint16),
        )

    def test_png_trns_with_corrupt_crc_fails_closed(self) -> None:
        source = np.array([[0, 12345, 65535]], dtype=np.uint16)
        encoded = io.BytesIO()
        rectify_pages.Image.fromarray(source).save(
            encoded,
            format="PNG",
            transparency=12345,
        )
        content = bytearray(encoded.getvalue())
        trns = content.index(b"tRNS")
        content[trns + 4] ^= 1

        with self.assertRaisesRegex(ValueError, "PNG chunk CRC mismatch"):
            rectify_pages.decode_single_frame(
                bytes(content),
                Path("corrupt-transparent-16.png"),
            )

    def test_premultiplied_alpha_mode_is_rejected(self) -> None:
        image = rectify_pages.Image.new("RGBA", (1, 1)).convert(
            rectify_pages.PREMULTIPLIED_RGBA_MODE
        )
        with self.assertRaisesRegex(ValueError, "unsupported premultiplied alpha"):
            rectify_pages.composite_to_grayscale(image, Path("page.png"))

    def test_processing_failure_publishes_nothing(self) -> None:
        self.write_page("page_1.jpg")
        self.write_page("page_2.jpg")
        with (
            mock.patch.object(
                rectify_pages,
                "read_page",
                side_effect=[
                    (np.full((10, 10), 255, np.uint8), "first"),
                    RuntimeError("decode failure"),
                ],
            ),
            mock.patch.object(
                rectify_pages,
                "rectify",
                return_value=(
                    np.full((10, 10), 255, np.uint8),
                    {"status": "unchanged"},
                ),
            ),
            mock.patch.object(
                sys,
                "argv",
                [
                    "rectify_pages.py",
                    str(self.input),
                    str(self.output),
                    "--report",
                    str(self.output / "report.json"),
                ],
            ),
        ):
            with self.assertRaisesRegex(RuntimeError, "decode failure"):
                rectify_pages.main()
        self.assertFalse(self.output.exists())
        self.assertFalse(
            any(".staging" in path.name for path in self.work.rglob("*"))
        )

    def test_whole_directory_is_published_with_one_rename(self) -> None:
        self.write_page("page.jpg")
        real_rename = rectify_pages.os.rename
        with (
            mock.patch.object(
                rectify_pages,
                "read_page",
                return_value=(np.full((10, 10), 255, np.uint8), "hash"),
            ),
            mock.patch.object(rectify_pages, "file_sha256", return_value="hash"),
            mock.patch.object(
                rectify_pages,
                "rectify",
                return_value=(
                    np.full((10, 10), 255, np.uint8),
                    {"status": "unchanged"},
                ),
            ),
            mock.patch.object(
                rectify_pages.os, "rename", wraps=real_rename
            ) as rename,
            mock.patch.object(
                sys,
                "argv",
                [
                    "rectify_pages.py",
                    str(self.input),
                    str(self.output),
                    "--report",
                    str(self.output / "report.json"),
                ],
            ),
        ):
            self.assertEqual(rectify_pages.main(), 0)
        rename.assert_called_once()
        self.assertTrue((self.output / "page.png").is_file())
        self.assertTrue((self.output / "report.json").is_file())

    def test_input_change_before_publication_aborts_batch(self) -> None:
        self.write_page("page.jpg")
        with (
            mock.patch.object(
                rectify_pages,
                "read_page",
                return_value=(np.full((10, 10), 255, np.uint8), "original"),
            ),
            mock.patch.object(rectify_pages, "file_sha256", return_value="changed"),
            mock.patch.object(
                rectify_pages,
                "rectify",
                return_value=(
                    np.full((10, 10), 255, np.uint8),
                    {"status": "unchanged"},
                ),
            ),
            mock.patch.object(
                sys,
                "argv",
                [
                    "rectify_pages.py",
                    str(self.input),
                    str(self.output),
                    "--report",
                    str(self.output / "report.json"),
                ],
            ),
        ):
            with self.assertRaisesRegex(ValueError, "input changed"):
                rectify_pages.main()
        self.assertFalse(self.output.exists())
        self.assertFalse(
            any(".staging" in path.name for path in self.work.rglob("*"))
        )

    def test_report_records_source_hash(self) -> None:
        self.write_page("page.png")
        with mock.patch.object(
            sys,
            "argv",
            [
                "rectify_pages.py",
                str(self.input),
                str(self.output),
                "--report",
                str(self.output / "report.json"),
            ],
        ):
            self.assertEqual(rectify_pages.main(), 0)
        report = rectify_pages.json.loads(
            (self.output / "report.json").read_text(encoding="utf-8")
        )
        self.assertRegex(report[0]["source_sha256"], r"^[0-9a-f]{64}$")


class DocumentationRegressionTests(unittest.TestCase):
    def test_maintainable_runner_under_windows_powershell(self) -> None:
        powershell = shutil.which("powershell.exe")
        if powershell is None:
            self.skipTest("Windows PowerShell is not installed")
        test_script = Path(__file__).with_name("test_runner.ps1")
        subprocess.run(
            [
                powershell,
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(test_script),
            ],
            check=True,
            capture_output=True,
            text=True,
        )

    def test_all_report_review_flags_are_mandatory_visual_review(self) -> None:
        skill = (Path(__file__).parents[1] / "SKILL.md").read_text(encoding="utf-8")
        self.assertIn(
            "every report page whose `review_required` field is `true`",
            skill,
        )
        self.assertIn("Do not\n   sample or omit any `review_required: true` page.", skill)

    def test_runner_is_ephemeral_hash_locked_and_fail_closed(self) -> None:
        root = Path(__file__).parents[1]
        runner = (root / "scripts" / "run.ps1").read_text(encoding="utf-8")
        lock = (root / "scripts" / "requirements.lock").read_text(
            encoding="utf-8"
        )
        self.assertIn('".session-{0}-{1}"', runner)
        self.assertIn('$miseVersion = "2026.8.8"', runner)
        self.assertIn('$pythonVersion = "3.12.10"', runner)
        self.assertRegex(runner, r'MISE_CONFIG_FILE\s*=\s*"NUL"')
        self.assertIn("MISE_CONFIG_DIR", runner)
        self.assertIn("MISE_INSTALLS_DIR", runner)
        self.assertIn("MISE_CACHE_DIR", runner)
        self.assertIn('$_.Name -like "PIP_*"', runner)
        self.assertIn('$_.Name -like "PYTHON*"', runner)
        self.assertNotIn("PIP_INDEX_URL", runner)
        self.assertNotIn("ado token", runner)
        self.assertIn("--require-hashes", runner)
        self.assertIn("--only-binary=:all:", runner)
        self.assertIn("--no-deps", runner)
        self.assertIn("-m pip check", runner)
        self.assertIn("Remove-Item -LiteralPath $sessionRoot -Recurse -Force", runner)
        self.assertEqual(lock.count("--hash=sha256:"), 3)
        self.assertNotIn("runtimeManifest", runner)
        self.assertNotIn("AccessControl", runner)
        self.assertNotIn("FileShare", runner)

    def test_network_is_required_for_every_invocation(self) -> None:
        skill = (Path(__file__).parents[1] / "SKILL.md").read_text(encoding="utf-8")
        self.assertIn("network access on every invocation", skill)

    def test_documented_runner_invocations_use_no_profile(self) -> None:
        skill = (Path(__file__).parents[1] / "SKILL.md").read_text(encoding="utf-8")
        invocations = [
            line
            for line in skill.splitlines()
            if "scripts\\run.ps1" in line
            and "rectify_pages.py" in line
        ]
        self.assertEqual(len(invocations), 2)
        self.assertTrue(all("powershell.exe -NoProfile -File" in line for line in invocations))
        self.assertNotIn("startup_launcher", skill)
        self.assertNotIn("invoke_trusted_launcher", skill)


if __name__ == "__main__":
    unittest.main()
