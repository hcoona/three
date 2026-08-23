from __future__ import annotations

import importlib.util
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

import cv2
import numpy as np
from PIL import Image, TiffImagePlugin


SKILL_DIR = Path(__file__).resolve().parents[1]
SCRIPT = SKILL_DIR / "scripts" / "restore_tone.py"
RUNNER = SKILL_DIR / "scripts" / "run.ps1"
LOCK = SKILL_DIR / "scripts" / "requirements.lock"


def repository_fixture_root() -> Path:
    configured = os.environ.get("SCAN_RESTORATION_FIXTURE_ROOT")
    if configured:
        return Path(configured).expanduser().resolve()
    for candidate in Path(__file__).resolve().parents:
        if (candidate / "mise.toml").is_file() and (candidate / "apm.yml").is_file():
            return candidate
    return Path(__file__).resolve().parent


REPOSITORY_INPUT = repository_fixture_root() / "input"


def load_script():
    spec = importlib.util.spec_from_file_location("restore_tone_under_test", SCRIPT)
    if spec is None or spec.loader is None:
        raise AssertionError("could not load restore_tone.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_image(path: Path, image: np.ndarray) -> None:
    ok, encoded = cv2.imencode(path.suffix, image)
    if not ok:
        raise AssertionError(f"could not encode test image {path}")
    encoded.tofile(path)


def write_low_bit_tiff(path: Path, bit_depth: int, width: int = 32, height: int = 24) -> None:
    entry_count = 9
    pixel_offset = 8 + 2 + entry_count * 12 + 4
    row_bytes = (width * bit_depth + 7) // 8
    pixels = b"\xff" * (row_bytes * height)
    entries = [
        (256, 4, 1, width),
        (257, 4, 1, height),
        (258, 3, 1, bit_depth),
        (259, 3, 1, 1),
        (262, 3, 1, 1),
        (273, 4, 1, pixel_offset),
        (277, 3, 1, 1),
        (278, 4, 1, height),
        (279, 4, 1, len(pixels)),
    ]
    encoded = bytearray(b"II*\x00\x08\x00\x00\x00")
    encoded += struct.pack("<H", entry_count)
    for tag, field_type, count, value in entries:
        encoded += struct.pack("<HHI", tag, field_type, count)
        if field_type == 3 and count == 1:
            encoded += struct.pack("<H", value) + b"\x00\x00"
        else:
            encoded += struct.pack("<I", value)
    encoded += b"\x00\x00\x00\x00" + pixels
    path.write_bytes(encoded)


def write_grayscale_tiff(
    path: Path,
    image: np.ndarray,
    bit_depth: int,
    *,
    photometric: int,
    fill_order: int,
    orientation: int = 1,
) -> None:
    height, width = image.shape
    maximum = (1 << bit_depth) - 1
    samples = (
        maximum - image.astype(np.uint16)
        if photometric == 0
        else image.astype(np.uint16)
    )
    if bit_depth == 1:
        data = np.packbits(samples.astype(np.uint8), axis=1, bitorder="big").tobytes()
    elif bit_depth == 8:
        data = samples.astype(np.uint8).tobytes()
    elif bit_depth == 16:
        data = samples.astype("<u2").tobytes()
    else:
        raise AssertionError(f"unsupported test TIFF depth: {bit_depth}")
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
        (258, 3, 1, bit_depth),
        (259, 3, 1, 1),
        (262, 3, 1, photometric),
        (266, 3, 1, fill_order),
        (273, 4, 1, 0),
        (274, 3, 1, orientation),
        (277, 3, 1, 1),
        (278, 4, 1, height),
        (279, 4, 1, len(data)),
        (284, 3, 1, 1),
        (339, 3, 1, 1),
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


class RestoreToneTests(unittest.TestCase):
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

    def default_restore(self, image: np.ndarray):
        module = load_script()
        return module.restore_with_status(
            image,
            background_scale=34,
            min_background_sigma=24,
            paper_level=247,
            black_percentile=0.35,
            black_floor=32,
            black_ceiling=98,
            white_percentile=88,
            white_floor=215,
            min_tone_range=40,
            whiten_start=178,
            whiten_width=58,
            white_clip=251,
        )

    def test_broad_dark_block_is_retained_while_yellow_paper_is_whitened(self) -> None:
        image = np.full((400, 600), 210, np.uint8)
        image[80:320, 120:480] = 35

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored[:60].mean()), 248)
        self.assertLess(float(restored[120:280, 180:420].mean()), 70)
        self.assertEqual(status["status"], "foreground_protected")
        self.assertGreater(status["protected_fraction"], 0.30)

    def test_embedded_photograph_gradient_retains_tonal_structure(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        horizontal = np.linspace(45, 200, 240)
        vertical = np.linspace(-20, 20, 180)[:, np.newaxis]
        photograph = np.clip(horizontal + vertical, 20, 220).astype(np.uint8)
        image[100:280, 180:420] = photograph

        restored, status = self.default_restore(image)
        restored_photo = restored[100:280, 180:420]

        self.assertGreater(float(restored[:60].mean()), 248)
        self.assertGreater(float(restored_photo.std()), float(photograph.std()) * 0.70)
        self.assertGreater(
            float(np.percentile(restored_photo, 90) - np.percentile(restored_photo, 10)),
            80,
        )
        self.assertEqual(status["status"], "foreground_protected")

    def test_embedded_photograph_has_no_restored_paper_halo(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        yy, xx = np.indices((180, 240), dtype=np.float32)
        photograph = np.clip(
            42.0 + xx * 0.62 + yy * 0.16 + 5.0 * np.sin((xx + yy) / 8.0),
            20,
            220,
        ).astype(np.uint8)
        image[100:280, 180:420] = photograph

        restored, status = self.default_restore(image)

        outer = np.zeros(image.shape, dtype=bool)
        outer[84:296, 164:436] = True
        outer[100:280, 180:420] = False
        halo_values = restored[outer]
        photo_core = restored[104:276, 184:416]

        self.assertGreater(float(np.percentile(halo_values, 5)), 248)
        self.assertLess(float(np.mean((halo_values >= 212) & (halo_values <= 218))), 0.01)
        self.assertLess(
            float(np.mean(np.abs(
                photo_core.astype(np.int16)
                - photograph[4:-4, 4:-4].astype(np.int16)
            ))),
            1.0,
        )
        self.assertEqual(status["status"], "foreground_protected")

    def test_bounded_horizontal_gradient_is_protected(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        illustration = np.tile(
            np.linspace(180, 240, 240, dtype=np.float32), (180, 1)
        ).astype(np.uint8)
        image[110:290, 180:420] = illustration

        restored, status = self.default_restore(image)
        restored_illustration = restored[110:290, 180:420]
        tonal_range = float(
            np.percentile(restored_illustration, 90)
            - np.percentile(restored_illustration, 10)
        )

        self.assertGreater(float(restored[:60].mean()), 248)
        self.assertLess(float(np.mean(restored_illustration == 255)), 0.05)
        self.assertGreater(tonal_range, 45)
        self.assertEqual(status["status"], "foreground_protected")

    def test_bounded_vertical_gradient_is_protected(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        illustration = np.tile(
            np.linspace(180, 240, 180, dtype=np.float32)[:, np.newaxis],
            (1, 240),
        ).astype(np.uint8)
        image[110:290, 180:420] = illustration

        restored, status = self.default_restore(image)
        restored_illustration = restored[110:290, 180:420]
        tonal_range = float(
            np.percentile(restored_illustration, 90)
            - np.percentile(restored_illustration, 10)
        )

        self.assertGreater(float(restored[:60].mean()), 248)
        self.assertLess(float(np.mean(restored_illustration == 255)), 0.05)
        self.assertGreater(tonal_range, 45)
        self.assertEqual(status["status"], "foreground_protected")

    def test_full_width_bounded_height_gradient_is_protected(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        illustration = np.tile(
            np.linspace(180, 240, 600, dtype=np.float32), (140, 1)
        ).astype(np.uint8)
        image[130:270, :] = illustration

        restored, status = self.default_restore(image)
        restored_illustration = restored[130:270, :]
        tonal_range = float(
            np.percentile(restored_illustration, 90)
            - np.percentile(restored_illustration, 10)
        )

        self.assertGreater(float(restored[:80].mean()), 248)
        self.assertLess(float(np.mean(restored_illustration == 255)), 0.05)
        self.assertGreater(tonal_range, 45)
        self.assertEqual(status["status"], "foreground_protected")

    def test_full_height_bounded_width_gradient_is_protected(self) -> None:
        image = np.full((400, 600), 215, np.uint8)
        illustration = np.tile(
            np.linspace(180, 240, 400, dtype=np.float32)[:, np.newaxis],
            (1, 180),
        ).astype(np.uint8)
        image[:, 210:390] = illustration

        restored, status = self.default_restore(image)
        restored_illustration = restored[:, 210:390]
        tonal_range = float(
            np.percentile(restored_illustration, 90)
            - np.percentile(restored_illustration, 10)
        )

        self.assertGreater(float(restored[:, :120].mean()), 248)
        self.assertLess(float(np.mean(restored_illustration == 255)), 0.05)
        self.assertGreater(tonal_range, 45)
        self.assertEqual(status["status"], "foreground_protected")

    def test_bright_continuous_tone_photo_is_copied_unchanged(self) -> None:
        height, width = 360, 540
        yy, xx = np.indices((height, width), dtype=np.float32)
        image = (
            220.0
            + 18.0 * np.sin(xx / 27.0)
            + 11.0 * np.cos(yy / 19.0)
            + 6.0 * np.sin((xx + yy) / 8.0)
        )
        image = np.clip(image, 178, 252).astype(np.uint8)

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "bright_continuous_tone_content_requires_preservation",
        )

    def test_textured_highlights_are_preserved_on_both_sides_of_paper(self) -> None:
        image = np.full((360, 540), 218, np.uint8)
        yy, xx = np.indices((180, 260), dtype=np.float32)
        illustration = (
            219.0
            + 13.0 * np.sin(xx / 12.0)
            + 9.0 * np.cos(yy / 9.0)
            + 4.0 * np.sin((xx + yy) / 3.5)
        )
        illustration = np.clip(illustration, 191, 245).astype(np.uint8)
        image[90:270, 140:400] = illustration

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored[:60].mean()), 248)
        np.testing.assert_array_equal(restored[92:268, 142:398], illustration[2:-2, 2:-2])
        self.assertLess(int(restored[90:270, 140:400].max()), 255)
        self.assertEqual(status["status"], "foreground_protected")

    def test_continuous_tone_detection_is_resolution_invariant(self) -> None:
        module = load_script()
        image = np.full((400, 600), 218, np.uint8)
        yy, xx = np.indices((180, 260), dtype=np.float32)
        illustration = np.clip(
            219.0
            + 13.0 * np.sin(xx / 12.0)
            + 9.0 * np.cos(yy / 9.0)
            + 4.0 * np.sin((xx + yy) / 3.5),
            191,
            245,
        ).astype(np.uint8)
        image[110:290, 170:430] = illustration
        scaled = cv2.resize(image, (2400, 1600), interpolation=cv2.INTER_NEAREST)

        base_mask, _ = module.continuous_tone_protection(
            image, image.astype(np.float32) / 255.0
        )
        scaled_mask, _ = module.continuous_tone_protection(
            scaled, scaled.astype(np.float32) / 255.0
        )
        scaled_mask_at_base = cv2.resize(
            scaled_mask.astype(np.uint8),
            (600, 400),
            interpolation=cv2.INTER_NEAREST,
        ).astype(bool)

        self.assertGreater(float(np.mean(base_mask[110:290, 170:430])), 0.90)
        self.assertGreater(
            float(np.mean(scaled_mask_at_base[110:290, 170:430])),
            0.90,
        )
        self.assertLess(
            float(np.mean(scaled_mask_at_base ^ base_mask)),
            0.02,
        )

    def test_large_embedded_photo_refines_proxy_halo_at_full_resolution(self) -> None:
        height, width = 1600, 2400
        image = np.full((height, width), 223, np.uint8)
        photo_y0, photo_y1 = 440, 1160
        photo_x0, photo_x1 = 720, 1680
        yy, xx = np.indices(
            (photo_y1 - photo_y0, photo_x1 - photo_x0), dtype=np.float32
        )
        photograph = np.clip(
            48.0
            + xx * 0.13
            + yy * 0.055
            + 17.0 * np.sin(xx / 31.0)
            + 11.0 * np.cos(yy / 23.0),
            25,
            222,
        ).astype(np.uint8)
        image[photo_y0:photo_y1, photo_x0:photo_x1] = photograph

        restored, status = self.default_restore(image)

        adjacent_paper = np.concatenate(
            (
                restored[photo_y0 - 12:photo_y0, photo_x0:photo_x1].ravel(),
                restored[photo_y1:photo_y1 + 12, photo_x0:photo_x1].ravel(),
                restored[photo_y0:photo_y1, photo_x0 - 12:photo_x0].ravel(),
                restored[photo_y0:photo_y1, photo_x1:photo_x1 + 12].ravel(),
            )
        )
        photo_core = restored[
            photo_y0 + 6:photo_y1 - 6, photo_x0 + 6:photo_x1 - 6
        ]

        self.assertGreater(float(np.percentile(adjacent_paper, 5)), 249)
        self.assertLess(
            float(np.mean((adjacent_paper >= 223) & (adjacent_paper <= 249))),
            0.01,
        )
        self.assertLess(
            float(np.mean(np.abs(
                photo_core.astype(np.int16)
                - photograph[6:-6, 6:-6].astype(np.int16)
            ))),
            1.0,
        )
        self.assertEqual(status["status"], "foreground_protected")

    def test_plain_yellow_color_paper_still_whitens(self) -> None:
        image = np.full((320, 480, 3), (170, 215, 230), np.uint8)

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored.mean()), 248)
        self.assertEqual(status["status"], "normalized")

    def test_staff_and_text_strokes_survive_paper_tone_normalization(self) -> None:
        image = np.tile(
            np.linspace(185, 225, 600, dtype=np.float32), (400, 1)
        ).astype(np.uint8)
        for row in (100, 112, 124, 136, 148):
            image[row : row + 2, 60:540] = 48
        image[190:193, 90:260] = 62
        image[205:208, 90:300] = 62

        restored, _ = self.default_restore(image)

        paper = restored[40:80]
        staff = restored[100:102, 100:500]
        text = restored[190:193, 110:240]
        self.assertGreater(float(paper.mean()), 245)
        self.assertLess(float(paper.std()), 8)
        self.assertLess(float(staff.mean()), 45)
        self.assertLess(float(text.mean()), 65)

    def test_repository_page_37_underexposed_paper_is_restored(self) -> None:
        if not REPOSITORY_INPUT.is_dir():
            self.skipTest("repository scan fixture is not available")
        pages = sorted(REPOSITORY_INPUT.glob(
            "Fundamentals of Piano Theory Level 2_Page_*.jpg"
        ))
        self.assertEqual(len(pages), 72)
        image = cv2.imread(
            str(REPOSITORY_INPUT / "Fundamentals of Piano Theory Level 2_Page_37.jpg"),
            cv2.IMREAD_UNCHANGED,
        )
        self.assertIsNotNone(image)

        restored, status = self.default_restore(image)
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

        self.assertNotEqual(status["status"], "copied_unchanged")
        self.assertGreater(float(np.percentile(restored, 85)), 245)
        source_dark = gray <= np.percentile(gray, 5)
        self.assertLess(
            float(np.percentile(restored[source_dark], 90)),
            float(np.percentile(restored, 85)) - 20,
        )

    def test_repository_page_40_is_not_mostly_continuous_tone(self) -> None:
        if not REPOSITORY_INPUT.is_dir():
            self.skipTest("repository scan fixture is not available")
        image = cv2.imread(
            str(REPOSITORY_INPUT / "Fundamentals of Piano Theory Level 2_Page_40.jpg"),
            cv2.IMREAD_UNCHANGED,
        )
        self.assertIsNotNone(image)
        module = load_script()
        gray = module.to_grayscale(image).astype(np.float32) / 255.0

        _, _, continuous_tone, _, _, paper_fraction = (
            module.foreground_protection(image, gray)
        )
        restored, status = self.default_restore(image)

        self.assertGreater(paper_fraction, 0.80)
        self.assertLess(float(np.mean(continuous_tone)), 0.20)
        self.assertNotEqual(status["status"], "copied_unchanged")
        source_dark = gray <= np.percentile(gray, 5)
        self.assertLess(
            float(np.percentile(restored[source_dark], 90)),
            float(np.percentile(restored, 85)) - 20,
        )

    def test_level_200_two_pixel_stroke_survives_level_210_paper(self) -> None:
        image = np.full((240, 480), 210, np.uint8)
        image[119:121, 40:440] = 200

        restored, status = self.default_restore(image)

        paper = float(np.median(restored[80:100, 80:400]))
        stroke = float(np.max(restored[119:121, 80:400]))
        self.assertLess(stroke, paper)
        self.assertLess(stroke, 255)
        self.assertGreaterEqual(paper - stroke, 2)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_antialias_ramp_remains_ordered_below_whitened_paper(self) -> None:
        image = np.full((240, 480), 210, np.uint8)
        levels = np.array([210, 208, 206, 204, 202, 200], np.uint8)
        for offset, level in enumerate(levels):
            image[110 + offset:111 + offset, 40:440] = level

        restored, _ = self.default_restore(image)

        output_levels = restored[110:116, 200].astype(np.int16)
        self.assertTrue(np.all(np.diff(output_levels) <= 0), output_levels)
        self.assertLess(int(output_levels[-1]), int(output_levels[0]))
        self.assertLess(int(output_levels[-1]), 255)

    def test_218_bounded_region_on_level_220_paper_is_not_whitened(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[70:170, 140:340] = 218

        restored, status = self.default_restore(image)

        paper = int(np.median(restored[20:50, 80:400]))
        region = restored[75:165, 145:335]
        self.assertLess(int(region.max()), paper)
        self.assertLess(int(region.max()), 255)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_level_218_full_width_band_on_220_paper_is_not_whitened(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[95:145, :] = 218

        restored, status = self.default_restore(image)

        paper = int(np.median(restored[30:70, 80:400]))
        band = restored[100:140, 40:440]
        self.assertLess(int(band.max()), paper)
        self.assertLess(int(band.max()), 255)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_full_height_two_level_gradient_band_is_not_whitened(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[:, 190:290] = np.rint(
            np.linspace(218, 219, 100, dtype=np.float32)
        ).astype(np.uint8)

        restored, status = self.default_restore(image)

        paper = int(np.median(restored[40:200, 40:140]))
        band = restored[20:220, 195:285]
        self.assertLess(int(band.max()), paper)
        self.assertLess(int(band.max()), 255)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_ambiguous_near_paper_band_at_page_edge_is_copied(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[:60, :] = 218

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "unsafe_foreground_background_separation_requires_review",
        )

    def test_near_paper_edge_shading_is_not_a_coherent_band(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[:, :80] = np.rint(
            np.linspace(218, 220, 80, dtype=np.float32)
        ).astype(np.uint8)
        module = load_script()

        protected = module.coherent_near_paper_components(
            image.astype(np.float32) / 255.0, 220.0 / 255.0
        )
        restored, status = self.default_restore(image)

        self.assertFalse(np.any(protected))
        self.assertGreater(float(restored.mean()), 248)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_sparse_near_paper_edge_noise_is_not_a_coherent_band(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        rng = np.random.default_rng(45)
        rows = rng.choice(image.shape[0], size=48, replace=False)
        image[rows, 0] = rng.integers(218, 220, size=rows.size, dtype=np.uint8)
        module = load_script()

        protected = module.coherent_near_paper_components(
            image.astype(np.float32) / 255.0, 220.0 / 255.0
        )
        restored, status = self.default_restore(image)

        self.assertFalse(np.any(protected))
        self.assertGreater(float(restored.mean()), 248)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_antialias_microcontrast_below_level_220_paper_is_preserved(self) -> None:
        image = np.full((240, 480), 220, np.uint8)
        image[110:114, 40:440] = np.array(
            [219, 218, 218, 219], np.uint8
        )[:, np.newaxis]

        restored, status = self.default_restore(image)

        paper = int(np.median(restored[80:100, 80:400]))
        output_levels = restored[110:114, 200].astype(np.int16)
        self.assertTrue(np.all(output_levels < paper), output_levels)
        self.assertLess(int(output_levels[1]), int(output_levels[0]))
        self.assertEqual(int(output_levels[1]), int(output_levels[2]))
        self.assertGreater(int(output_levels[3]), int(output_levels[2]))
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_faded_staff_lines_keep_contrast_and_antialias_edges(self) -> None:
        image = np.full((300, 600), 214, np.uint8)
        for row in (80, 96, 112, 128, 144):
            image[row - 1, 50:550] = 211
            image[row:row + 2, 50:550] = 204
            image[row + 2, 50:550] = 209

        restored, status = self.default_restore(image)

        paper = int(np.median(restored[40:60, 100:500]))
        cores = restored[[80, 96, 112, 128, 144], 100:500]
        edges = restored[[79, 95, 111, 127, 143], 100:500]
        self.assertTrue(np.all(cores < edges))
        self.assertTrue(np.all(edges < paper))
        self.assertGreaterEqual(paper - int(np.max(cores)), 2)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_ambiguous_broad_faint_region_is_copied_for_review(self) -> None:
        image = np.full((240, 480), 210, np.uint8)
        image[:, :240] = 200

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "unsafe_foreground_background_separation_requires_review",
        )

    def test_level_200_content_covering_75_percent_of_level_210_paper_is_copied(self) -> None:
        image = np.full((240, 480), 210, np.uint8)
        image[:, :360] = 200

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "unsafe_foreground_background_separation_requires_review",
        )

    def test_low_contrast_regions_from_one_to_ten_percent_are_copied(self) -> None:
        shapes = {
            0.01: (40, 50),
            0.05: (100, 100),
            0.10: (100, 200),
        }
        for expected_fraction, (region_height, region_width) in shapes.items():
            with self.subTest(expected_fraction=expected_fraction):
                image = np.full((400, 500), 210, np.uint8)
                top = (image.shape[0] - region_height) // 2
                left = (image.shape[1] - region_width) // 2
                image[
                    top : top + region_height,
                    left : left + region_width,
                ] = 200

                restored, status = self.default_restore(image)

                self.assertAlmostEqual(
                    region_height * region_width / image.size,
                    expected_fraction,
                )
                np.testing.assert_array_equal(restored, image)
                self.assertEqual(status["status"], "copied_unchanged")
                self.assertEqual(
                    status["reason"],
                    "unsafe_foreground_background_separation_requires_review",
                )

    def test_ten_percent_level_200_rectangle_on_220_document_is_copied(self) -> None:
        image = np.full((400, 500), 220, np.uint8)
        image[150:250, 150:350] = 200
        for row in range(35, 115, 10):
            image[row:row + 2, 50:450] = 45
        module = load_script()
        gray = image.astype(np.float32) / 255.0
        *_, paper, paper_fraction = module.foreground_protection(image, gray)

        self.assertAlmostEqual(float(np.mean(image == 200)), 0.10)
        self.assertTrue(
            module.is_high_coverage_document_page(gray, paper, paper_fraction)
        )
        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "unsafe_foreground_background_separation_requires_review",
        )

    def test_actual_batch_preserves_defect_and_normalizes_controls(self) -> None:
        defect = np.full((400, 500), 220, np.uint8)
        defect[150:250, 150:350] = 200
        for row in range(35, 115, 10):
            defect[row:row + 2, 50:450] = 45
        ordinary_paper = np.full((400, 500), 220, np.uint8)
        staff = ordinary_paper.copy()
        for row in (120, 140, 160, 180, 200):
            staff[row - 1, 50:450] = 205
            staff[row:row + 2, 50:450] = 45
            staff[row + 2, 50:450] = 195
        write_image(self.input / "page_001.png", defect)
        write_image(self.input / "page_002.png", ordinary_paper)
        write_image(self.input / "page_003.png", staff)

        result = self.run_cli(self.input, self.root / "batch")

        self.assertEqual(result.returncode, 0, result.stderr)
        summary = json.loads(result.stdout.strip().splitlines()[-1])
        statuses = {page["input"]: page["status"] for page in summary["pages"]}
        self.assertEqual(statuses["page_001.png"], "copied_unchanged")
        self.assertEqual(statuses["page_002.png"], "normalized")
        self.assertNotEqual(statuses["page_003.png"], "copied_unchanged")
        np.testing.assert_array_equal(
            cv2.imread(str(self.root / "batch" / "page_001.png"), cv2.IMREAD_GRAYSCALE),
            defect,
        )
        restored_paper = cv2.imread(
            str(self.root / "batch" / "page_002.png"), cv2.IMREAD_GRAYSCALE
        )
        restored_staff = cv2.imread(
            str(self.root / "batch" / "page_003.png"), cv2.IMREAD_GRAYSCALE
        )
        self.assertGreater(float(restored_paper.mean()), 248)
        paper_level = int(np.median(restored_staff[40:80, 80:420]))
        self.assertGreater(paper_level, 248)
        self.assertLess(int(restored_staff[160, 250]), paper_level - 20)
        self.assertLess(int(restored_staff[159, 250]), paper_level)

    def test_isolated_single_pixel_noise_does_not_trigger_copy(self) -> None:
        image = np.full((400, 500), 210, np.uint8)
        noise_points = [
            (25, 31),
            (63, 417),
            (118, 204),
            (199, 87),
            (266, 352),
            (374, 468),
        ]
        for row, column in noise_points:
            image[row, column] = 200

        restored, status = self.default_restore(image)

        self.assertNotEqual(status["status"], "copied_unchanged")
        for row, column in noise_points:
            self.assertLess(restored[row, column], restored[row, column + 1])

    def test_large_pale_illustration_is_copied_without_losing_structure(self) -> None:
        image = np.full((320, 480), 210, np.uint8)
        illustration = image[24:296, 24:456]
        illustration[:] = 200
        yy, xx = np.indices(illustration.shape)
        illustration[(xx // 24 + yy // 20) % 7 == 0] = 196
        cv2.ellipse(illustration, (216, 136), (145, 92), 0, 0, 360, 204, 3)
        cv2.line(illustration, (55, 210), (370, 55), 194, 2)

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "unsafe_foreground_background_separation_requires_review",
        )

    def test_uneven_yellow_paper_is_flattened_and_whitened(self) -> None:
        image = np.tile(
            np.linspace(188, 226, 640, dtype=np.float32), (360, 1)
        ).astype(np.uint8)

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored.mean()), 245)
        self.assertLess(float(restored.std()), float(image.std()) * 0.65)
        self.assertEqual(status["status"], "normalized")

    def test_180_to_230_smooth_paper_ramp_is_flattened_and_whitened(self) -> None:
        image = np.tile(
            np.linspace(180, 230, 720, dtype=np.float32), (420, 1)
        ).astype(np.uint8)

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored.mean()), 247)
        self.assertLess(float(restored.std()), 5)
        self.assertEqual(status["status"], "normalized")

    def test_two_axis_paper_wide_gradient_is_flattened_and_whitened(self) -> None:
        yy, xx = np.indices((420, 720), dtype=np.float32)
        image = np.clip(180.0 + 34.0 * xx / 719.0 + 16.0 * yy / 419.0, 0, 255)
        image = image.astype(np.uint8)

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored.mean()), 247)
        self.assertLess(float(restored.std()), 5)
        self.assertEqual(status["status"], "normalized")

    def test_smooth_edge_vignette_and_shadow_are_treated_as_paper(self) -> None:
        height, width = 420, 640
        yy, xx = np.indices((height, width), dtype=np.float32)
        edge_distance = np.minimum.reduce((xx, width - 1 - xx, yy, height - 1 - yy))
        vignette = 50.0 * np.exp(-edge_distance / 52.0)
        shadow = 18.0 / (1.0 + np.exp((xx - 125.0) / 28.0))
        image = np.clip(230.0 - vignette - shadow, 180, 230).astype(np.uint8)

        restored, status = self.default_restore(image)

        self.assertGreater(float(restored.mean()), 245)
        self.assertLess(float(restored.std()), float(image.std()) * 0.45)
        self.assertNotEqual(status["status"], "copied_unchanged")

    def test_edge_connected_gutter_ramp_does_not_consume_internal_rectangle(self) -> None:
        image = np.tile(
            np.linspace(80, 230, 600, dtype=np.float32), (400, 1)
        ).astype(np.uint8)
        cv2.rectangle(image, (85, 90), (255, 245), 40, -1)

        restored, status = self.default_restore(image)

        self.assertGreater(float(np.median(restored[20:70, 20:560])), 245)
        self.assertLess(float(np.median(restored[120:215, 115:225])), 80)
        self.assertEqual(status["status"], "foreground_protected")

    def test_half_width_level_40_block_on_level_225_paper_is_protected(self) -> None:
        image = np.full((400, 600), 225, np.uint8)
        image[:, :300] = 40

        restored, status = self.default_restore(image)

        self.assertGreater(float(np.median(restored[:, 340:560])), 248)
        self.assertLess(float(np.median(restored[:, 40:260])), 70)
        self.assertEqual(status["status"], "foreground_protected")
        self.assertGreater(status["protected_fraction"], 0.45)

    def test_text_inside_edge_connected_shadow_is_protected(self) -> None:
        image = np.tile(
            np.linspace(80, 230, 600, dtype=np.float32), (400, 1)
        ).astype(np.uint8)
        cv2.putText(
            image, "Shadow text", (45, 210), cv2.FONT_HERSHEY_SIMPLEX,
            1.15, 40, 2, cv2.LINE_AA,
        )

        restored, status = self.default_restore(image)
        text_region = restored[170:220, 40:275]

        self.assertGreater(float(np.median(restored[30:70, 20:560])), 245)
        self.assertLess(float(np.percentile(text_region, 2)), 100)
        self.assertEqual(status["status"], "foreground_protected")

    def test_faint_box_text_and_staff_are_protected_on_smooth_ramp(self) -> None:
        image = np.tile(
            np.linspace(184, 228, 720, dtype=np.float32), (420, 1)
        ).astype(np.uint8)
        cv2.rectangle(image, (70, 55), (650, 180), 202, 2)
        cv2.putText(
            image, "Faint text", (110, 145), cv2.FONT_HERSHEY_SIMPLEX,
            1.0, 199, 2, cv2.LINE_AA,
        )
        for row in (245, 258, 271, 284, 297):
            image[row:row + 2, 80:640] = 204

        restored, status = self.default_restore(image)
        nearby_paper = float(np.median(restored[205:225, 100:620]))

        self.assertGreater(nearby_paper, 245)
        self.assertLess(float(np.median(restored[55:57, 100:620])), nearby_paper - 2)
        self.assertLess(float(np.percentile(restored[110:150, 110:300], 1)), nearby_paper - 2)
        self.assertLess(float(np.median(restored[271:273, 100:620])), nearby_paper - 2)
        self.assertEqual(status["status"], "foreground_protected")

    def test_high_resolution_faint_text_and_staff_survive_scaled_blur(self) -> None:
        height, width = 1500, 2200
        paper = (
            216.0
            + np.linspace(0, 22, width, dtype=np.float32)[np.newaxis, :]
            + np.linspace(0, 8, height, dtype=np.float32)[:, np.newaxis]
        )
        text = np.zeros((height, width), np.uint8)
        cv2.rectangle(text, (250, 260), (1950, 650), 255, 3)
        cv2.putText(
            text, "Faint text", (390, 540), cv2.FONT_HERSHEY_SIMPLEX,
            3.0, 255, 5, cv2.LINE_AA,
        )
        staff = np.zeros((height, width), bool)
        for row in (900, 925, 950, 975, 1000):
            staff[row:row + 2, 300:1900] = True
        ink = (text > 0) | staff
        image = np.rint(paper).astype(np.uint8)
        image[ink] = np.maximum(
            0, image[ink].astype(np.int16) - 3
        ).astype(np.uint8)

        module = load_script()
        restored, status = module.restore_with_status(
            image,
            background_scale=34,
            min_background_sigma=300,
            paper_level=247,
            black_percentile=0.35,
            black_floor=32,
            black_ceiling=98,
            white_percentile=88,
            white_floor=215,
            min_tone_range=40,
            whiten_start=178,
            whiten_width=58,
            white_clip=251,
        )
        nearby_paper = float(np.median(restored[760:820, 400:1800]))
        glyph_pixels = restored[400:560, 390:1300][
            text[400:560, 390:1300] > 0
        ]

        self.assertGreater(nearby_paper, 248)
        self.assertLess(
            float(np.median(restored[260:263, 400:1800])),
            nearby_paper - 2,
        )
        self.assertLess(float(np.percentile(glyph_pixels, 10)), nearby_paper - 2)
        self.assertLess(
            float(np.median(restored[950:952, 400:1800])),
            nearby_paper - 2,
        )
        self.assertEqual(status["status"], "foreground_protected")

    def test_solid_dark_page_is_copied_unchanged(self) -> None:
        image = np.full((400, 600), 72, np.uint8)

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "page_has_no_sufficiently_bright_paper_background",
        )

    def test_dark_cover_with_light_text_is_copied_unchanged(self) -> None:
        image = np.full((400, 600), 48, np.uint8)
        image[70:105, 100:500] = 228
        image[145:175, 150:450] = 205
        image[310:325, 220:380] = 190

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "page_has_no_sufficiently_bright_paper_background",
        )

    def test_dark_full_bleed_cover_with_broad_light_design_is_copied(self) -> None:
        image = np.full((400, 600), 45, np.uint8)
        image[:, 420:] = 225
        image[80:115, 80:340] = 205
        image[170:190, 110:320] = 180

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "foreground_or_illustration_covers_most_of_page",
        )

    def test_exact_60_40_dark_cover_is_copied_unchanged(self) -> None:
        image = np.full((400, 600), 45, np.uint8)
        image[:, 360:] = 225

        restored, status = self.default_restore(image)

        self.assertEqual(float(np.mean(image == 45)), 0.60)
        self.assertEqual(float(np.mean(image == 225)), 0.40)
        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "foreground_or_illustration_covers_most_of_page",
        )

    def test_full_bleed_dark_photo_with_internal_contrast_is_copied(self) -> None:
        height, width = 400, 600
        image = np.full((height, width), 155, np.float32)
        image[:100] = 220
        for row in range(100, 180):
            image[row] = 220.0 - (row - 99) * (65.0 / 80.0)
        cv2.ellipse(image, (170, 285), (105, 65), 0, 0, 360, 135, -1)
        cv2.rectangle(image, (330, 225), (540, 350), 175, -1)
        cv2.line(image, (30, 370), (570, 205), 140, 8)
        image = image.astype(np.uint8)

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "foreground_or_illustration_covers_most_of_page",
        )

    def test_low_key_illustration_is_copied_unchanged(self) -> None:
        x = np.linspace(28, 168, 600, dtype=np.float32)
        y = np.linspace(-25, 25, 400, dtype=np.float32)[:, np.newaxis]
        image = np.clip(x + y, 12, 176).astype(np.uint8)

        restored, status = self.default_restore(image)

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"],
            "page_has_no_sufficiently_bright_paper_background",
        )

    def test_normal_yellow_paper_with_text_is_suitable(self) -> None:
        image = np.full((400, 600, 3), (170, 215, 230), np.uint8)
        image[90:94, 80:520] = (45, 45, 45)
        image[130:134, 100:480] = (55, 55, 55)
        image[170:174, 120:500] = (50, 50, 50)

        restored, status = self.default_restore(image)

        self.assertNotEqual(status["status"], "copied_unchanged")
        self.assertGreater(float(restored[:60].mean()), 245)
        self.assertLess(float(restored[90:94, 100:500].mean()), 70)

    def test_full_page_continuous_tone_gradient_is_copied_unchanged(self) -> None:
        image = np.tile(
            np.linspace(25, 225, 600, dtype=np.float32), (400, 1)
        ).astype(np.uint8)
        write_image(self.input / "page_001.png", image)

        result = self.run_cli(self.input, self.root / "output")

        self.assertEqual(result.returncode, 0, result.stderr)
        restored = cv2.imdecode(
            np.fromfile(self.root / "output" / "page_001.png", np.uint8),
            cv2.IMREAD_UNCHANGED,
        )
        np.testing.assert_array_equal(restored, image)
        page = json.loads(result.stdout)["pages"][0]
        self.assertEqual(page["status"], "copied_unchanged")
        self.assertEqual(
            page["reason"],
            "continuous_tone_page_has_no_reliable_paper_background",
        )

    def test_16_bit_faint_detail_remains_16_bit(self) -> None:
        ramp = np.linspace(42000, 58000, 1024, dtype=np.uint16)
        image = np.repeat(ramp[np.newaxis, :], 256, axis=0)
        image[120:124, 300:724] -= 24
        write_image(self.input / "page_001.png", image)
        output = self.root / "output"

        result = self.run_cli(
            self.input,
            output,
            "--whiten-start",
            254,
            "--white-clip",
            255,
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        restored = cv2.imdecode(
            np.fromfile(output / "page_001.png", np.uint8), cv2.IMREAD_UNCHANGED
        )
        self.assertEqual(restored.dtype, np.uint16)
        self.assertTrue(np.any(restored.astype(np.uint32) % 257 != 0))
        self.assertLess(
            float(restored[121, 500]),
            (float(restored[117, 500]) + float(restored[127, 500])) / 2,
        )
        summary = json.loads(result.stdout)
        self.assertEqual(summary["pages"][0]["source_bit_depth"], 16)
        self.assertEqual(summary["pages"][0]["output_bit_depth"], 16)

    def test_low_bit_png_and_tiff_report_encoded_and_output_depths(self) -> None:
        for encoded_format in ("PNG", "TIFF"):
            bit_depths = (1, 2, 4) if encoded_format == "PNG" else (1,)
            for bit_depth in bit_depths:
                with self.subTest(encoded_format=encoded_format, bit_depth=bit_depth):
                    suffix = ".png" if encoded_format == "PNG" else ".tiff"
                    path = self.input / f"page_{bit_depth:03}{suffix}"
                    if encoded_format == "PNG":
                        image = Image.new("P", (32, 24), (1 << bit_depth) - 1)
                        palette = []
                        for value in range(256):
                            level = min(
                                255,
                                round(value * 255 / ((1 << bit_depth) - 1)),
                            )
                            palette.extend((level, level, level))
                        image.putpalette(palette)
                        image.save(path, bits=bit_depth)
                    else:
                        write_low_bit_tiff(path, bit_depth)

                    result = self.run_cli(
                        self.input, self.root / f"output-{encoded_format}-{bit_depth}"
                    )

                    self.assertEqual(result.returncode, 0, result.stderr)
                    page = json.loads(result.stdout)["pages"][0]
                    self.assertEqual(page["source_bit_depth"], bit_depth)
                    self.assertEqual(page["output_bit_depth"], 8)
                    path.unlink()

    def test_tiff_photometric_and_fill_order_preserve_native_polarity(self) -> None:
        module = load_script()
        for bit_depth in (1, 8, 16):
            dtype = np.uint16 if bit_depth == 16 else np.uint8
            maximum = (1 << bit_depth) - 1
            expected = np.full((12, 16), maximum, dtype)
            expected[3:9, 4:12] = 0
            if bit_depth == 1:
                expected *= 255
            for photometric in (0, 1):
                for fill_order in (1, 2):
                    with self.subTest(
                        bit_depth=bit_depth,
                        photometric=photometric,
                        fill_order=fill_order,
                    ):
                        path = self.input / (
                            f"page_{bit_depth}_{photometric}_{fill_order}.tiff"
                        )
                        encoded_values = expected // 255 if bit_depth == 1 else expected
                        write_grayscale_tiff(
                            path,
                            encoded_values,
                            bit_depth,
                            photometric=photometric,
                            fill_order=fill_order,
                        )

                        decoded = module.read_image(path)

                        self.assertEqual(decoded.dtype, expected.dtype)
                        np.testing.assert_array_equal(decoded, expected)
                        path.unlink()

    def test_unsupported_tiff_grayscale_depth_fails_closed(self) -> None:
        write_low_bit_tiff(self.input / "page_001.tiff", 4)

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported TIFF sample matrix", result.stderr)
        self.assertFalse((self.root / "output").exists())

    def test_all_exif_orientations_are_applied_explicitly(self) -> None:
        module = load_script()
        source = np.array(
            [
                [[10, 20, 30], [40, 50, 60], [70, 80, 90]],
                [[100, 110, 120], [130, 140, 150], [160, 170, 180]],
            ],
            dtype=np.uint8,
        )
        expected = {
            1: source,
            2: np.flip(source, axis=1),
            3: np.rot90(source, 2),
            4: np.flip(source, axis=0),
            5: np.swapaxes(source, 0, 1),
            6: np.rot90(source, 3),
            7: np.flip(np.swapaxes(source, 0, 1), axis=(0, 1)),
            8: np.rot90(source, 1),
        }
        for orientation in range(1, 9):
            with self.subTest(orientation=orientation):
                path = self.input / f"page_{orientation:03}.png"
                exif = Image.Exif()
                exif[274] = orientation
                Image.fromarray(source, "RGB").save(path, exif=exif)

                decoded = module.read_image(path)

                np.testing.assert_array_equal(decoded[:, :, ::-1], expected[orientation])

    def test_exif_orientation_preserves_16_bit_pixels(self) -> None:
        module = load_script()
        source = np.array([[1000, 2000, 3000], [4000, 5000, 6000]], np.uint16)
        path = self.input / "page_001.png"
        exif = Image.Exif()
        exif[274] = 6
        Image.fromarray(source, "I;16").save(path, exif=exif)

        decoded = module.read_image(path)

        self.assertEqual(decoded.dtype, np.uint16)
        np.testing.assert_array_equal(decoded, np.rot90(source, 3))

    def test_tiff_orientation_is_applied_exactly_once(self) -> None:
        module = load_script()
        source = np.array(
            [[[10, 20, 30], [40, 50, 60], [70, 80, 90]],
             [[100, 110, 120], [130, 140, 150], [160, 170, 180]]],
            dtype=np.uint8,
        )
        path = self.input / "page_001.tiff"
        exif = Image.Exif()
        exif[274] = 6
        Image.fromarray(source, "RGB").save(path, exif=exif)

        decoded = module.read_image(path)

        np.testing.assert_array_equal(decoded[:, :, ::-1], np.rot90(source, 3))

    def test_any_existing_output_path_is_rejected(self) -> None:
        write_image(self.input / "page_001.png", np.full((64, 64), 220, np.uint8))
        for directory, filename in (("empty", None), ("stale", "old.json")):
            with self.subTest(directory=directory):
                output = self.root / directory
                output.mkdir()
                stale = output / filename if filename else None
                if stale:
                    stale.write_bytes(b"stale")

                result = self.run_cli(self.input, output)

                self.assertNotEqual(result.returncode, 0)
                self.assertIn("output path must not exist", result.stderr)
                if stale:
                    self.assertEqual(stale.read_bytes(), b"stale")

    def test_missing_page_fails_without_creating_output(self) -> None:
        write_image(self.input / "page_001.png", np.full((64, 64), 220, np.uint8))
        output = self.root / "output"

        result = self.run_cli(self.input, output, "--pages", 1, 2)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("requested page number(s) missing: 2", result.stderr)
        self.assertFalse(output.exists())

    def test_duplicate_stems_fail_before_page_selection(self) -> None:
        image = np.full((64, 64), 220, np.uint8)
        write_image(self.input / "page_001.png", image)
        write_image(self.input / "page_001.tif", image)

        result = self.run_cli(self.input, self.root / "output", "--pages", 1)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duplicate input stems", result.stderr)

    def test_duplicate_nonpositive_and_ambiguous_pages_fail(self) -> None:
        image = np.full((64, 64), 220, np.uint8)
        write_image(self.input / "page_001.png", image)
        write_image(self.input / "scan_001.png", image)

        duplicate = self.run_cli(
            self.input, self.root / "duplicate", "--pages", 1, 1
        )
        nonpositive = self.run_cli(
            self.input, self.root / "nonpositive", "--pages", 0
        )
        ambiguous = self.run_cli(
            self.input, self.root / "ambiguous", "--pages", 1
        )

        self.assertIn("duplicate page numbers", duplicate.stderr)
        self.assertIn("positive page numbers", nonpositive.stderr)
        self.assertIn("requested page number(s) ambiguous", ambiguous.stderr)

    def test_multipage_tiff_is_rejected(self) -> None:
        path = self.input / "page_001.tiff"
        frames = [
            np.full((64, 64), 220, np.uint8),
            np.full((64, 64), 180, np.uint8),
        ]
        self.assertTrue(cv2.imwritemulti(str(path), frames))
        output = self.root / "output"

        result = self.run_cli(self.input, output)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("exactly one image frame is required", result.stderr)
        self.assertFalse(output.exists())

    def test_transaction_rolls_back_when_a_later_input_is_invalid(self) -> None:
        write_image(self.input / "page_001.png", np.full((64, 64), 220, np.uint8))
        (self.input / "page_002.png").write_bytes(b"not an image")
        output = self.root / "output"

        result = self.run_cli(self.input, output)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("image-like input is not a decodable image", result.stderr)
        self.assertFalse(output.exists())
        self.assertEqual(list(self.root.glob(".output.staging-*")), [])

    def test_non_allowlisted_encoded_format_is_rejected_even_with_png_suffix(self) -> None:
        Image.new("RGB", (8, 8), (20, 40, 60)).save(
            self.input / "page_001.png", format="BMP"
        )

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported encoded image format BMP", result.stderr)
        self.assertFalse((self.root / "output").exists())

    def test_psd_svg_pdf_candidates_fail_without_partial_publication(self) -> None:
        signatures = {
            "PSD": b"8BPS\x00\x01" + b"\x00" * 32,
            "SVG": b'<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg"/>',
            "PDF": b"%PDF-1.7\nsynthetic",
        }
        for encoded_format, content in signatures.items():
            with self.subTest(encoded_format=encoded_format, source="signature"):
                case_input = self.root / f"input-{encoded_format}-signature"
                case_input.mkdir()
                write_image(
                    case_input / "page_001.png",
                    np.full((32, 32), 220, np.uint8),
                )
                (case_input / "page_002.dat").write_bytes(content)
                output = self.root / f"output-{encoded_format}-signature"

                result = self.run_cli(case_input, output)

                self.assertNotEqual(result.returncode, 0)
                self.assertIn(
                    f"unsupported encoded image format {encoded_format}",
                    result.stderr,
                )
                self.assertFalse(output.exists())

    def test_svg_preamble_with_many_comments_is_detected_without_backtracking(
        self,
    ) -> None:
        module = load_script()
        header = (
            b"\xef\xbb\xbf \n<?xml version='1.0'?>"
            + b"<!-- scan metadata -->" * 150
            + b"<!DOCTYPE svg PUBLIC '-//W3C//DTD SVG 1.1//EN'>"
            + b"<svg xmlns='http://www.w3.org/2000/svg'>"
        )

        started = time.perf_counter()
        self.assertEqual(module.sniff_encoded_format(header), "SVG")
        self.assertLess(time.perf_counter() - started, 1.0)
        self.assertIsNone(
            module.sniff_encoded_format(b"<!--" + b"--><!--" * 400)
        )

    def test_modern_image_candidate_suffixes_fail_in_mixed_batches(self) -> None:
        for suffix in (".jxl", ".exr", ".qoi", ".jpm", ".fits", ".raw"):
            with self.subTest(suffix=suffix):
                case_input = self.root / f"input-{suffix[1:]}"
                case_input.mkdir()
                write_image(
                    case_input / "page_001.png",
                    np.full((32, 32), 220, np.uint8),
                )
                (case_input / f"page_002{suffix}").write_bytes(b"not decoded")
                output = self.root / f"output-{suffix[1:]}"

                result = self.run_cli(case_input, output)

                self.assertNotEqual(result.returncode, 0)
                self.assertIn("image-like input is not a decodable image", result.stderr)
                self.assertFalse(output.exists())
                self.assertEqual(
                    list(self.root.glob(f".{output.name}.staging-*")), []
                )

        for suffix in (".psd", ".svg", ".pdf"):
            with self.subTest(suffix=suffix, source="suffix"):
                case_input = self.root / f"input-{suffix[1:]}-suffix"
                case_input.mkdir()
                write_image(
                    case_input / "page_001.png",
                    np.full((32, 32), 220, np.uint8),
                )
                (case_input / f"page_002{suffix}").write_bytes(b"not an image")
                output = self.root / f"output-{suffix[1:]}-suffix"

                result = self.run_cli(case_input, output)

                self.assertNotEqual(result.returncode, 0)
                self.assertIn(
                    "image-like input is not a decodable image", result.stderr
                )
                self.assertFalse(output.exists())

    def test_inventory_sniffs_before_opening_and_uses_decoder_allowlist(self) -> None:
        module = load_script()
        invalid = self.input / "page_001.png"
        invalid.write_bytes(b"not an image")
        with mock.patch.object(module.Image, "open") as image_open:
            with self.assertRaisesRegex(ValueError, "not a decodable image"):
                module.inventory_inputs(
                    self.input,
                    max_encoded_bytes=1024,
                    max_pixels_per_page=1000,
                )
        image_open.assert_not_called()

        invalid.unlink()
        Image.new("L", (8, 8), 220).save(invalid)
        original_open = module.Image.open
        with mock.patch.object(module.Image, "open", wraps=original_open) as image_open:
            module.inventory_inputs(
                self.input,
                max_encoded_bytes=1024,
                max_pixels_per_page=1000,
            )
        self.assertEqual(image_open.call_args.kwargs["formats"], list(module.ENCODED_FORMATS))

    def test_oversized_unknown_suffix_is_rejected_before_read_or_pillow(self) -> None:
        module = load_script()
        oversized = self.input / "page_001.unknown"
        oversized.write_bytes(b"x" * 64)

        with mock.patch.object(Path, "read_bytes") as read_bytes, mock.patch.object(
            module.Image, "open"
        ) as image_open:
            with self.assertRaisesRegex(ValueError, "exceeds --max-encoded-bytes"):
                module.inventory_inputs(
                    self.input,
                    max_encoded_bytes=32,
                    max_pixels_per_page=1000,
                )

        read_bytes.assert_not_called()
        image_open.assert_not_called()

    def test_ignored_non_image_uses_only_bounded_header_inspection(self) -> None:
        module = load_script()
        ignored = self.input / "notes.txt"
        ignored.write_bytes(b"not an image" * 100)

        with mock.patch.object(
            module, "read_encoded_snapshot", wraps=module.read_encoded_snapshot
        ) as snapshot:
            inventory = module.inventory_inputs(
                self.input,
                max_encoded_bytes=4096,
                max_pixels_per_page=1000,
            )

        self.assertEqual(inventory, [])
        snapshot.assert_not_called()

    def test_inventory_count_and_aggregate_preflight_before_file_reads(self) -> None:
        module = load_script()
        (self.input / "one.txt").write_bytes(b"1" * 8)
        (self.input / "two.txt").write_bytes(b"2" * 8)

        with mock.patch.object(module, "read_stable_header") as header:
            with self.assertRaisesRegex(ValueError, "max-inventory-entries"):
                module.inventory_inputs(
                    self.input,
                    max_encoded_bytes=1024,
                    max_pixels_per_page=1000,
                    max_inventory_entries=1,
                )
        header.assert_not_called()

        with mock.patch.object(module, "read_stable_header") as header:
            with self.assertRaisesRegex(ValueError, "max-inventory-bytes"):
                module.inventory_inputs(
                    self.input,
                    max_encoded_bytes=1024,
                    max_pixels_per_page=1000,
                    max_inventory_bytes=15,
                )
        header.assert_not_called()

    def test_inventory_entry_limit_stops_enumeration_before_sorting(self) -> None:
        module = load_script()
        paths = [self.input / f"entry-{index}.txt" for index in range(4)]
        for path in paths:
            path.write_bytes(b"x")
        yielded = 0
        scandir_calls = 0
        original_scandir = module.os.scandir
        test_case = self

        class BoundedScandir:
            def __init__(self, path: os.PathLike[str]) -> None:
                self.iterator = original_scandir(path)

            def __enter__(self):
                self.iterator.__enter__()
                return self

            def __exit__(self, *args: object):
                return self.iterator.__exit__(*args)

            def __iter__(self):
                return self

            def __next__(self):
                nonlocal yielded
                entry = next(self.iterator)
                yielded += 1
                if yielded > 3:
                    test_case.fail(
                        "inventory enumerated beyond the first over-limit entry"
                    )
                return entry

        def bounded_scandir(path: os.PathLike[str]):
            nonlocal scandir_calls
            scandir_calls += 1
            return BoundedScandir(path)

        with mock.patch.object(
            module.os, "scandir", side_effect=bounded_scandir
        ), mock.patch.object(
            module.os,
            "listdir",
            side_effect=AssertionError("eager os.listdir enumeration is forbidden"),
        ), mock.patch.object(
            Path,
            "iterdir",
            side_effect=AssertionError("Path.iterdir enumeration is forbidden"),
        ), mock.patch.object(module, "natural_key") as natural_key, mock.patch.object(
            module, "read_stable_header"
        ) as header:
            with self.assertRaisesRegex(
                ValueError, r"max-inventory-entries \(3 > 2\)"
            ):
                module.inventory_inputs(
                    self.input,
                    max_encoded_bytes=1024,
                    max_pixels_per_page=1000,
                    max_inventory_entries=2,
                )

        self.assertEqual(scandir_calls, 1)
        self.assertEqual(yielded, 3)
        natural_key.assert_not_called()
        header.assert_not_called()

    def test_pillow_pixel_limit_matches_custom_page_budget_exactly(self) -> None:
        module = load_script()
        path = self.input / "page_001.png"
        Image.new("L", (20, 20), 220).save(path)
        previous_limit = module.Image.MAX_IMAGE_PIXELS

        accepted = module.inventory_inputs(
            self.input,
            max_encoded_bytes=1024,
            max_pixels_per_page=400,
        )
        self.assertEqual(accepted[0].pixels, 400)
        with self.assertRaisesRegex(
            ValueError, r"exceeds --max-pixels-per-page \(400 > 399\)"
        ):
            module.inventory_inputs(
                self.input,
                max_encoded_bytes=1024,
                max_pixels_per_page=399,
            )
        self.assertEqual(module.Image.MAX_IMAGE_PIXELS, previous_limit)

    def test_animated_png_and_webp_are_rejected_when_codec_supports_them(self) -> None:
        frames = [
            np.full((32, 32, 3), 220, np.uint8),
            np.full((32, 32, 3), 180, np.uint8),
        ]
        supported: list[Path] = []
        for name in ("animated_png.png", "animated_webp.webp"):
            path = self.input / name
            animation = cv2.Animation()
            animation.frames = frames
            animation.durations = [100, 100]
            try:
                written = cv2.imwriteanimation(str(path), animation)
            except cv2.error:
                written = False
            if written and cv2.imcount(str(path)) == 2:
                supported.append(path)
            else:
                path.unlink(missing_ok=True)
        if not supported:
            self.skipTest("OpenCV build cannot encode/detect APNG or animated WebP")

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("exactly one image frame is required", result.stderr)
        self.assertFalse((self.root / "output").exists())

    def test_alpha_is_composited_on_white_at_native_precision(self) -> None:
        module = load_script()
        for dtype in (np.uint8, np.uint16):
            with self.subTest(dtype=dtype):
                maximum = np.iinfo(dtype).max
                image = np.array(
                    [[[0, 0, 0, 0], [0, 0, 0, maximum // 2], [0, 0, 0, maximum]]],
                    dtype=dtype,
                )
                composited = module.composite_alpha_on_white(image)
                self.assertEqual(composited.dtype, dtype)
                np.testing.assert_array_equal(composited[0, 0], [maximum] * 3)
                np.testing.assert_allclose(
                    composited[0, 1], [maximum // 2 + 1] * 3, atol=1
                )
                np.testing.assert_array_equal(composited[0, 2], [0] * 3)

    def test_translucent_png_uses_straight_alpha_without_double_multiplication(self) -> None:
        module = load_script()
        pixel = [40, 100, 200, 128]
        path = self.input / "page_001.png"
        write_image(path, np.array([[pixel]], dtype=np.uint8))

        decoded = module.read_image(path)
        composited = module.composite_alpha_on_white(decoded)
        expected = [
            round(channel * pixel[3] / 255 + 255 - pixel[3])
            for channel in pixel[:3]
        ]

        self.assertEqual(decoded.dtype, np.uint8)
        np.testing.assert_allclose(composited[0, 0], expected, atol=1)

    def test_16_bit_color_and_alpha_are_rejected_without_silent_downconversion(self) -> None:
        for channels in (3, 4):
            with self.subTest(channels=channels):
                path = self.input / f"page_{channels:03}.png"
                write_image(
                    path,
                    np.full((2, 2, channels), 40000, dtype=np.uint16),
                )

                result = self.run_cli(self.input, self.root / f"output-{channels}")

                self.assertNotEqual(result.returncode, 0)
                self.assertIn("only 16-bit grayscale is supported", result.stderr)

    def test_lab_is_not_misclassified_as_alpha(self) -> None:
        path = self.input / "page_001.tiff"
        Image.new("LAB", (8, 8), (20, 128, 128)).save(path)

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unsupported Pillow image mode LAB", result.stderr)
        self.assertNotIn("TIFF alpha is unsupported", result.stderr)

    def test_inspection_and_decode_use_one_immutable_byte_buffer(self) -> None:
        module = load_script()
        path = self.input / "page_001.png"
        source = np.array([[10, 20], [30, 40]], dtype=np.uint8)
        Image.fromarray(source, "L").save(path)
        original_read_bytes = Path.read_bytes

        def read_then_replace(candidate: Path) -> bytes:
            encoded = original_read_bytes(candidate)
            if candidate == path:
                Image.new("RGB", (2, 2), (1, 2, 3)).save(candidate, format="BMP")
            return encoded

        with mock.patch.object(Path, "read_bytes", read_then_replace):
            decoded = module.read_image(path)

        np.testing.assert_array_equal(decoded, source)

    def test_windows_open_handle_ctime_drift_does_not_report_input_mutation(
        self,
    ) -> None:
        path = self.input / "page_001.png"
        write_image(path, np.full((8, 8), 220, np.uint8))
        expected = path.read_bytes()
        module = load_script()
        original_fstat = os.fstat
        call_count = 0

        def fstat_with_drifting_ctime(file_descriptor: int) -> mock.Mock:
            nonlocal call_count
            stat_result = original_fstat(file_descriptor)
            call_count += 1
            return mock.Mock(
                st_size=stat_result.st_size,
                st_mtime_ns=stat_result.st_mtime_ns,
                st_ctime_ns=stat_result.st_ctime_ns + call_count,
                st_dev=stat_result.st_dev,
                st_ino=stat_result.st_ino,
            )

        with mock.patch.object(module.os, "name", "nt"), mock.patch.object(
            module.os, "fstat", side_effect=fstat_with_drifting_ctime
        ):
            encoded, _, _ = module.read_encoded_snapshot(
                path, max_encoded_bytes=1024
            )

        self.assertEqual(encoded, expected)
        self.assertEqual(call_count, 2)

    def test_tiff_alpha_is_rejected_before_processing(self) -> None:
        path = self.input / "page_001.tiff"
        Image.new("RGBA", (8, 8), (20, 40, 60, 128)).save(path)
        output = self.root / "output"

        result = self.run_cli(self.input, output)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("TIFF alpha is unsupported", result.stderr)
        self.assertFalse(output.exists())

    def test_tiff_alpha_policy_uses_encoded_format_not_suffix(self) -> None:
        path = self.input / "page_001.png"
        Image.new("RGBA", (8, 8), (20, 40, 60, 128)).save(path, format="TIFF")

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("TIFF alpha is unsupported", result.stderr)

    def test_png_alpha_policy_uses_encoded_format_not_suffix(self) -> None:
        module = load_script()
        path = self.input / "page_001.tiff"
        Image.new("RGBA", (2, 2), (20, 40, 60, 128)).save(path, format="PNG")

        decoded = module.read_image(path)

        self.assertEqual(decoded.shape, (2, 2, 4))

    def test_png_transparency_info_is_converted_to_real_alpha(self) -> None:
        module = load_script()
        path = self.input / "page_001.png"
        Image.fromarray(np.array([[20, 30]], dtype=np.uint8), "L").save(
            path, transparency=20
        )

        decoded = module.read_image(path)

        self.assertEqual(decoded.shape, (1, 2, 4))
        self.assertEqual(int(decoded[0, 0, 3]), 0)
        self.assertEqual(int(decoded[0, 1, 3]), 255)

    def test_16_bit_grayscale_png_trns_is_rejected(self) -> None:
        path = self.input / "page_001.png"
        Image.fromarray(
            np.array([[1000, 2000], [3000, 4000]], dtype=np.uint16), "I;16"
        ).save(path, transparency=2000)

        result = self.run_cli(self.input, self.root / "output")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("16-bit grayscale PNG transparency (tRNS)", result.stderr)
        self.assertFalse((self.root / "output").exists())

    def test_signed_8_and_16_bit_tiff_are_rejected_from_sample_format(self) -> None:
        for dtype, mode in ((np.uint8, "L"), (np.uint16, "I;16")):
            with self.subTest(dtype=dtype):
                path = self.input / f"page_{np.dtype(dtype).itemsize:03}.tiff"
                tiffinfo = TiffImagePlugin.ImageFileDirectory_v2()
                tiffinfo[339] = (2,)
                Image.fromarray(
                    np.array([[0, 1], [2, 3]], dtype=dtype), mode
                ).save(path, tiffinfo=tiffinfo)

                result = self.run_cli(
                    self.input, self.root / f"output-{np.dtype(dtype).itemsize}"
                )

                self.assertNotEqual(result.returncode, 0)
                self.assertIn("unsupported TIFF SampleFormat (2,)", result.stderr)
                self.assertFalse(
                    (self.root / f"output-{np.dtype(dtype).itemsize}").exists()
                )
                path.unlink()

    def test_streams_pages_and_rolls_back_on_out_of_memory(self) -> None:
        first = self.input / "page_001.png"
        second = self.input / "page_002.png"
        write_image(first, np.full((8, 8), 220, np.uint8))
        write_image(second, np.full((8, 8), 210, np.uint8))
        output = self.root / "output"
        module = load_script()
        events: list[str] = []
        original_write = module.write_png

        def read(path: Path, **_: object) -> np.ndarray:
            events.append(f"read:{path.name}")
            if path == second:
                raise MemoryError("simulated out of memory")
            return np.full((8, 8), 220, np.uint8)

        def write(path: Path, image: np.ndarray) -> None:
            events.append(f"write:{path.name}")
            original_write(path, image)

        argv = ["restore_tone.py", str(self.input), str(output)]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            module, "read_image", side_effect=read
        ), mock.patch.object(module, "restore", side_effect=lambda image, **_: image), \
            mock.patch.object(module, "write_png", side_effect=write):
            with self.assertRaises(SystemExit):
                module.main()

        self.assertEqual(
            events,
            ["read:page_001.png", "write:page_001.png", "read:page_002.png"],
        )
        self.assertFalse(output.exists())
        self.assertEqual(list(self.root.glob(".output.staging-*")), [])

    def test_replacement_immediately_before_processing_aborts_and_cleans_staging(
        self,
    ) -> None:
        path = self.input / "page_001.png"
        write_image(path, np.full((8, 8), 220, np.uint8))
        output = self.root / "output"
        module = load_script()
        original_read = module.read_image

        def replace_then_read(candidate: Path, **kwargs: object) -> np.ndarray:
            Image.new("RGB", (8, 8), (1, 2, 3)).save(candidate, format="BMP")
            return original_read(candidate, **kwargs)

        argv = ["restore_tone.py", str(self.input), str(output)]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            module, "read_image", side_effect=replace_then_read
        ):
            with self.assertRaises(SystemExit):
                module.main()

        self.assertFalse(output.exists())
        self.assertEqual(list(self.root.glob(".output.staging-*")), [])

    def test_replacement_before_publication_aborts_and_cleans_staging(self) -> None:
        path = self.input / "page_001.png"
        write_image(path, np.full((8, 8), 220, np.uint8))
        output = self.root / "output"
        module = load_script()
        original_write = module.write_png

        def write_then_replace(destination: Path, image: np.ndarray) -> None:
            original_write(destination, image)
            Image.new("RGB", (8, 8), (1, 2, 3)).save(path, format="BMP")

        argv = ["restore_tone.py", str(self.input), str(output)]
        with mock.patch.object(sys, "argv", argv), mock.patch.object(
            module, "write_png", side_effect=write_then_replace
        ):
            with self.assertRaises(SystemExit):
                module.main()

        self.assertFalse(output.exists())
        self.assertEqual(list(self.root.glob(".output.staging-*")), [])

    def test_skill_repeats_approved_preview_options_for_full_batch(self) -> None:
        skill = (SKILL_DIR / "SKILL.md").read_text(encoding="utf-8")
        options = (
            '"--paper-level" "244" "--whiten-start" "188" '
            '"--whiten-width" "62" "--white-clip" "253"'
        )
        self.assertEqual(skill.count(options), 2)
        self.assertIn("Record the exact\n   approved tuning options", skill)
        self.assertIn("does not inherit preview", skill)
        self.assertIn("Omitting one restores its default", skill)

        write_image(
            self.input / "page_001.png",
            np.tile(np.linspace(185, 225, 160, dtype=np.uint8), (120, 1)),
        )
        preview = self.run_cli(
            self.input, self.root / "preview", "--pages", 1,
            "--paper-level", 244, "--whiten-start", 188,
            "--whiten-width", 62, "--white-clip", 253,
        )
        batch = self.run_cli(
            self.input, self.root / "batch",
            "--paper-level", 244, "--whiten-start", 188,
            "--whiten-width", 62, "--white-clip", 253,
        )
        self.assertEqual(preview.returncode, 0, preview.stderr)
        self.assertEqual(batch.returncode, 0, batch.stderr)
        self.assertEqual(
            (self.root / "preview" / "page_001.png").read_bytes(),
            (self.root / "batch" / "page_001.png").read_bytes(),
        )

    def test_jpe_alias_is_processed_and_odd_suffix_image_fails(self) -> None:
        Image.new("L", (32, 24), 220).save(
            self.input / "page_001.jpe", format="JPEG"
        )
        accepted = self.run_cli(self.input, self.root / "accepted")
        self.assertEqual(accepted.returncode, 0, accepted.stderr)
        self.assertTrue((self.root / "accepted" / "page_001.png").is_file())

        (self.input / "page_001.jpe").unlink()
        Image.new("L", (32, 24), 220).save(
            self.input / "page_002.scan", format="PNG"
        )
        rejected = self.run_cli(self.input, self.root / "rejected")
        self.assertNotEqual(rejected.returncode, 0)
        self.assertIn("valid PNG image has an unsupported filename suffix", rejected.stderr)

    def test_valid_jfif_is_processed_in_mixed_batch(self) -> None:
        write_image(
            self.input / "page_001.png", np.full((24, 32), 215, np.uint8)
        )
        Image.new("L", (32, 24), 220).save(
            self.input / "page_002.jfif", format="JPEG"
        )
        output = self.root / "output"

        result = self.run_cli(self.input, output)

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((output / "page_001.png").is_file())
        self.assertTrue((output / "page_002.png").is_file())

    def test_corrupt_jfif_fails_closed_in_mixed_batch(self) -> None:
        write_image(
            self.input / "page_001.png", np.full((24, 32), 215, np.uint8)
        )
        (self.input / "page_002.jfif").write_bytes(b"not an encoded image")
        output = self.root / "output"

        result = self.run_cli(self.input, output)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("image-like input is not a decodable image", result.stderr)
        self.assertIn("page_002.jfif", result.stderr)
        self.assertFalse(output.exists())

    def test_text_heavy_component_analysis_completes_in_linear_time(self) -> None:
        image = np.full((1000, 1400), 214, np.uint8)
        for row in range(24, 980, 18):
            for column in range(20, 1380, 34):
                cv2.rectangle(image, (column, row), (column + 12, row + 7), 202, 1)

        started = time.perf_counter()
        restored, _ = self.default_restore(image)
        elapsed = time.perf_counter() - started

        self.assertEqual(restored.shape, image.shape)
        self.assertLess(elapsed, 8.0)

    def test_resource_budgets_fail_before_output_allocation(self) -> None:
        encoded = self.input / "page_001.png"
        encoded.write_bytes(b"x" * 64)
        too_many_bytes = self.run_cli(
            self.input, self.root / "bytes", "--max-encoded-bytes", 32
        )
        self.assertIn("exceeds --max-encoded-bytes", too_many_bytes.stderr)
        self.assertFalse((self.root / "bytes").exists())

        encoded.unlink()
        write_image(encoded, np.full((20, 20), 220, np.uint8))
        too_many_pixels = self.run_cli(
            self.input, self.root / "pixels", "--max-pixels-per-page", 399
        )
        self.assertIn("exceeds --max-pixels-per-page", too_many_pixels.stderr)

        write_image(
            self.input / "page_002.png", np.full((20, 20), 220, np.uint8)
        )
        too_many_pages = self.run_cli(
            self.input, self.root / "pages", "--max-page-count", 1
        )
        self.assertIn("exceeds --max-page-count", too_many_pages.stderr)
        too_many_total_pixels = self.run_cli(
            self.input, self.root / "total", "--max-total-pixels", 799
        )
        self.assertIn("exceed --max-total-pixels", too_many_total_pixels.stderr)
        too_much_memory = self.run_cli(
            self.input,
            self.root / "memory",
            "--max-working-bytes-per-page",
            1,
        )
        self.assertIn(
            "exceeds --max-working-bytes-per-page", too_much_memory.stderr
        )
        too_much_work = self.run_cli(
            self.input,
            self.root / "work",
            "--max-work-units-per-page",
            1,
        )
        self.assertIn("exceeds --max-work-units-per-page", too_much_work.stderr)

    def test_ten_million_by_one_background_kernel_is_bounded_without_allocation(
        self,
    ) -> None:
        module = load_script()
        shape = (1, 10_000_000)

        self.assertLessEqual(
            shape[0] * shape[1], module.DEFAULT_MAX_PIXELS_PER_PAGE
        )
        self.assertEqual(
            module.bounded_background_kernel(shape, 1000.0),
            (module.MAX_EXTREME_ASPECT_KERNEL_DIMENSION, 1),
        )
        self.assertEqual(
            module.bounded_morphology_iterations(shape, 1_000_000),
            0,
        )
        self.assertEqual(
            module.bounded_morphology_iterations((400, 600), 12),
            12,
        )
        self.assertEqual(
            module.bounded_morphology_iterations((10_000, 20_000), 1_000_000),
            module.MAX_MORPHOLOGY_ITERATIONS,
        )
        self.assertEqual(
            module.bounded_morphology_kernel_dimension(1_000_001),
            module.MAX_MORPHOLOGY_KERNEL_DIMENSION,
        )

    def test_ordinary_high_resolution_sigma_is_scaled_not_truncated(self) -> None:
        module = load_script()
        shape = (2900, 4100)
        sigma = min(shape) / module.DEFAULT_BACKGROUND_SCALE

        plan = module.background_blur_plan(shape, sigma)

        self.assertLess(plan.size[0], shape[1])
        self.assertLess(plan.size[1], shape[0])
        self.assertLessEqual(
            max(plan.kernel), module.MAX_BACKGROUND_KERNEL_DIMENSION
        )
        self.assertAlmostEqual(
            plan.sigma_x * shape[1] / plan.size[0], sigma, places=6
        )
        self.assertAlmostEqual(
            plan.sigma_y * shape[0] / plan.size[1], sigma, places=6
        )

    def test_unrepresentable_background_sigma_fails_closed(self) -> None:
        module = load_script()
        image = np.full((400, 600), 220, np.uint8)

        with self.assertRaises(module.UnsupportedBackgroundSigma):
            module.background_blur_plan((2, 2), 1000.0)

        with mock.patch.object(
            module,
            "background_blur_plan",
            side_effect=module.UnsupportedBackgroundSigma("unrepresentable"),
        ):
            restored, status = module.restore_with_status(
                image,
                background_scale=34,
                min_background_sigma=24,
                paper_level=247,
                black_percentile=0.35,
                black_floor=32,
                black_ceiling=98,
                white_percentile=88,
                white_floor=215,
                min_tone_range=40,
                whiten_start=178,
                whiten_width=58,
                white_clip=251,
            )

        np.testing.assert_array_equal(restored, image)
        self.assertEqual(status["status"], "copied_unchanged")
        self.assertEqual(
            status["reason"], "requested_background_sigma_cannot_be_honored"
        )

    def test_runner_is_practical_isolated_and_token_safe(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        lock = LOCK.read_text(encoding="utf-8")
        self.assertIn('"MISE_CONFIG_FILE"', runner)
        self.assertIn('"MISE_GLOBAL_CONFIG_FILE"', runner)
        self.assertIn('"MISE_SYSTEM_CONFIG_FILE"', runner)
        self.assertIn('Set-IsolatedEnvironmentVariable "PIP_CONFIG_FILE" "nul"', runner)
        self.assertIn("https://azureauth:{0}@pkgs.dev.azure.com", runner)
        self.assertNotIn("--index-url", runner)
        pip_command = next(
            line for line in runner.splitlines() if "-m pip install" in line
        )
        self.assertNotIn("token", pip_command.casefold())
        self.assertNotIn("index", pip_command.casefold())
        self.assertIn('$pillowVersion = "12.3.0"', runner)
        self.assertIn("Pillow==12.3.0", lock)
        self.assertIn('$imagecodecsVersion = "2026.6.26"', runner)
        self.assertIn("imagecodecs==2026.6.26", lock)
        self.assertIn('$tifffileVersion = "2026.7.31"', runner)
        self.assertIn("tifffile==2026.7.31", lock)
        self.assertIn(
            "sha256:a2b55dd6b2a4c4b7d87ffa56bdb33fdc5fdb9a462173861a7bc097f17d91cb09",
            lock,
        )
        self.assertIn("--no-cache-dir --require-hashes", runner)
        self.assertIn("--only-binary=:all: --no-deps", runner)
        self.assertIn('Programs\\AzureAuth\\0.9.5\\azureauth.exe', runner)
        self.assertIn("$token = $null", runner)
        self.assertIn('"PIP_INDEX_URL", $null, "Process"', runner)
        self.assertIn(
            "providers = metadata.packages_distributions().get(package, ())",
            runner,
        )
        self.assertIn(
            "distribution.casefold() not in "
            "{provider.casefold() for provider in providers}",
            runner,
        )
        self.assertNotIn("!= [distribution.lower()]", runner)
        self.assertNotIn("Get-AuthenticodeSignature", runner)
        self.assertNotIn("startup_launcher", runner)
        self.assertNotIn("DirectoryLock", runner)
        self.assertIn('".runtime-" + [Guid]::NewGuid()', runner)
        self.assertIn("Remove-Item -LiteralPath $runtime -Recurse -Force", runner)
        self.assertNotIn('Join-Path $PSScriptRoot ".runtime"', runner)
        self.assertIn("& $python -I -B $scriptPath @ScriptArgs", runner)

    def test_runner_passes_runtime_path_to_python_via_argv(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        match = re.search(
            r"\$verifyDependencies = @'\r?\n(.*?)\r?\n'@",
            runner,
            re.DOTALL,
        )
        self.assertIsNotNone(match)
        verification_source = match.group(1)

        self.assertNotIn("$runtime", verification_source)
        self.assertIn("root = Path(sys.argv[1]).resolve()", verification_source)
        self.assertIn(
            "& $python -I -B -c $verifyDependencies `\n"
            "        $runtime $imagecodecsVersion $numpyVersion $opencvVersion `\n"
            "        $pillowVersion $tifffileVersion",
            runner.replace("\r\n", "\n"),
        )

        runtime = self.root / "runtime parent's directory" / "runtime"
        runtime.mkdir(parents=True)
        result = subprocess.run(
            [
                sys.executable,
                "-I",
                "-B",
                "-c",
                "from pathlib import Path; import sys; print(Path(sys.argv[1]).resolve())",
                str(runtime),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(Path(result.stdout.strip()), runtime.resolve())

    def test_runner_pip_null_device_disables_global_and_site_configs(self) -> None:
        from pip._internal.configuration import Configuration, get_configuration_files
        from pip._internal.configuration import kinds

        runner = RUNNER.read_text(encoding="utf-8")
        match = re.search(
            r'Set-IsolatedEnvironmentVariable "PIP_CONFIG_FILE" "([^"]+)"',
            runner,
        )
        self.assertIsNotNone(match)
        configured_null_device = match.group(1)
        self.assertEqual(configured_null_device, "nul")
        if sys.platform == "win32":
            self.assertEqual(configured_null_device, os.devnull)

        global_config = self.root / "global.ini"
        site_config = self.root / "site.ini"
        global_config.write_text("[global]\ntimeout = 17\n", encoding="utf-8")
        site_config.write_text("[global]\nretries = 4\n", encoding="utf-8")
        config_files = get_configuration_files()
        config_files[kinds.GLOBAL] = [str(global_config)]
        config_files[kinds.USER] = []
        config_files[kinds.SITE] = [str(site_config)]

        with mock.patch.dict(
            os.environ, {"PIP_CONFIG_FILE": configured_null_device}, clear=True
        ), mock.patch("pip._internal.configuration.get_configuration_files",
                      return_value=config_files), mock.patch(
            "pip._internal.configuration.os.devnull", "nul"
        ):
            configuration = Configuration(isolated=False)
            configuration.load()

        self.assertEqual(configuration._parsers[kinds.GLOBAL], [])
        self.assertEqual(configuration._parsers[kinds.SITE], [])


if __name__ == "__main__":
    unittest.main()
