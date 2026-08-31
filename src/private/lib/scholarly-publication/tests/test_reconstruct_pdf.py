# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "jsonschema==4.25.1",
#   "pymupdf==1.26.6",
# ]
# ///

from __future__ import annotations

import importlib
import sys
import tempfile
import unittest
from pathlib import Path
from typing import TYPE_CHECKING, Any
from unittest import mock

if TYPE_CHECKING:
    from collections.abc import Callable

sys.dont_write_bytecode = True

PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
SKILL = PUBLICATION_ROOT / "skills" / "scholarly-pdf-reconstruction"
SUPPORT_ROOT = PUBLICATION_ROOT / "tests"
sys.path.insert(0, str(SUPPORT_ROOT))

publication_test_support: Any = importlib.import_module(
    "publication_test_support"
)
PdfPage = publication_test_support.PdfPage
add_pdf_attachment = publication_test_support.add_pdf_attachment
add_pdf_javascript = publication_test_support.add_pdf_javascript
asset_record = publication_test_support.asset_record
canonical_json = publication_test_support.canonical_json
copy_tree = publication_test_support.copy_tree
encrypt_pdf = publication_test_support.encrypt_pdf
import_by_path = publication_test_support.import_by_path
invoke_main = publication_test_support.invoke_main
read_json = publication_test_support.read_json
tree_snapshot = publication_test_support.tree_snapshot
windows_short_path = publication_test_support.windows_short_path
write_json = publication_test_support.write_json
write_pdf = publication_test_support.write_pdf

reconstruct_pdf = import_by_path(
    "scholarly_reconstruct_pdf_under_test",
    SKILL / "scripts" / "reconstruct_pdf.py",
)

LONG_TEXT = (
    "This deterministic scholarly source page contains enough extractable "
    "Unicode text for stable blocks, canonical SVG evidence, and replay. "
    "中文学术文本应在重建后保持可检索。"
)


def add_pdf_rendition_javascript(path: Path) -> None:
    """Add a catalog Rendition action carrying JavaScript."""
    fitz: Any = importlib.import_module("fitz")
    with fitz.open(path) as document:
        action_xref = document.get_new_xref()
        document.update_object(
            action_xref,
            "<< /Type /Action /S /Rendition /JS (app.alert\\(4\\)) >>",
        )
        document.xref_set_key(
            document.pdf_catalog(),
            "OpenAction",
            f"{action_xref} 0 R",
        )
        document.saveIncr()


def add_page_reachable_3d_javascript(path: Path) -> None:
    """Add page-reachable 3D artwork with an instantiation script."""
    fitz: Any = importlib.import_module("fitz")
    with fitz.open(path) as document:
        script_xref = document.get_new_xref()
        document.update_object(script_xref, "<< >>")
        document.update_stream(script_xref, b"app.alert(5)")

        artwork_xref = document.get_new_xref()
        document.update_object(
            artwork_xref,
            (f"<< /Type /3D /Subtype /U3D /OnInstantiate {script_xref} 0 R >>"),
        )
        document.update_stream(artwork_xref, b"U3D fixture")

        appearance_xref = document.get_new_xref()
        document.update_object(
            appearance_xref,
            (
                "<< /Type /XObject /Subtype /Form /BBox [0 0 288 240] "
                "/Resources << >> >>"
            ),
        )
        document.update_stream(appearance_xref, b"")

        annotation_xref = document.get_new_xref()
        document.update_object(
            annotation_xref,
            (
                "<< /Type /Annot /Subtype /3D /Rect [36 180 324 420] "
                f"/3DD {artwork_xref} 0 R "
                f"/AP << /N {appearance_xref} 0 R >> >>"
            ),
        )
        page_xref = document.load_page(0).xref
        document.xref_set_key(
            page_xref,
            "Annots",
            f"[{annotation_xref} 0 R]",
        )
        document.saveIncr()


class ReconstructPdfReplacementTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(
            prefix="scholarly-reconstruction-"
        )
        self.root = Path(self.temporary.name)
        self.source = self.root / "source.pdf"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write_maps(self) -> tuple[Path, Path]:
        sections = self.root / "sections.json"
        figures = self.root / "figures.json"
        write_json(
            sections,
            {
                "schema_version": "1.0",
                "sections": [
                    {
                        "id": "analysis",
                        "title": "Analysis",
                        "start_pdf_page": 1,
                        "end_pdf_page": 2,
                        "printed_folio_start": "1",
                    }
                ],
            },
        )
        write_json(
            figures,
            {
                "schema_version": "1.0",
                "coordinate_space": "pdf-points-top-left",
                "figures": [
                    {
                        "id": "example-a",
                        "source_label": "Example A",
                        "profile": "music-notation",
                        "embedded_language_inventory": ["en"],
                        "parts": [
                            {
                                "id": "example-a-part-1",
                                "order": 1,
                                "pdf_page": 1,
                                "bbox": [54, 220, 306, 340],
                            },
                            {
                                "id": "example-a-part-2",
                                "order": 2,
                                "pdf_page": 2,
                                "bbox": [54, 220, 306, 340],
                            },
                        ],
                    }
                ],
            },
        )
        return sections, figures

    def extract(
        self,
        output: Path,
        *,
        pages: str,
        maps: tuple[Path | None, Path | None] = (None, None),
        extra: tuple[str, ...] = (),
    ) -> Any:
        sections, figures = maps
        arguments = [
            "extract",
            "--pdf",
            str(self.source),
            "--output",
            str(output),
            "--pages",
            pages,
            "--rights-note",
            "Authorized deterministic test fixture.",
        ]
        if sections is not None:
            arguments.extend(["--sections", str(sections)])
        if figures is not None:
            arguments.extend(["--figure-map", str(figures)])
        arguments.extend(extra)
        return invoke_main(reconstruct_pdf, arguments)

    def validate(self, manifest: Path, *, source: Path | None = None) -> Any:
        arguments = ["validate", "--package", str(manifest)]
        if source is not None:
            arguments.extend(["--source", str(source)])
        return invoke_main(reconstruct_pdf, arguments)

    def make_happy_source(self, *, suffix: str = "") -> None:
        write_pdf(
            self.source,
            [
                PdfPage(text=f"Page one. {LONG_TEXT}{suffix}"),
                PdfPage(text=f"Page two. {LONG_TEXT}{suffix}"),
            ],
        )

    def make_baseline_package(self, name: str = "baseline") -> Path:
        self.make_happy_source()
        output = self.root / name
        result = self.extract(output, pages="1-2")
        self.assertEqual(0, result.exit_code, result)
        self.assertEqual("pass", result.report["status"])
        return output

    @staticmethod
    def refresh_manifest_asset(
        package: Path,
        manifest: dict[str, Any],
        page_index: int,
        role: str,
    ) -> None:
        record = manifest["pages"][page_index]["assets"][role]
        manifest["pages"][page_index]["assets"][role] = asset_record(
            package,
            package / record["path"],
        )

    def duplicate_raw_json_line(self, path: Path, line: bytes) -> None:
        content = path.read_bytes()
        self.assertEqual(1, content.count(line))
        path.write_bytes(content.replace(line, line * 2, 1))

    def test_happy_extract_standalone_validate_and_exact_source_replay(
        self,
    ) -> None:
        self.make_happy_source()
        sections, figures = self.write_maps()
        output = self.root / "package"

        extracted = self.extract(
            output,
            pages="1-2",
            maps=(sections, figures),
            extra=("--profile", "music-notation"),
        )

        self.assertEqual(0, extracted.exit_code)
        self.assertEqual(
            {
                "command": "extract",
                "valid": True,
                "status": "pass",
                "errors": [],
                "manifest": str(output / "source-package.json"),
                "package_id": extracted.report["package_id"],
                "selected_pages": 2,
                "published": True,
            },
            extracted.report,
        )
        manifest_path = output / "source-package.json"
        manifest = read_json(manifest_path)
        self.assertEqual([1, 2], manifest["selection"]["pdf_pages"])
        self.assertEqual(["music-notation"], manifest["profiles"])
        self.assertEqual("pass", manifest["status"])
        self.assertEqual([], manifest["issues"])
        self.assertEqual(
            asset_record(output, output / "maps" / "sections.json"),
            manifest["sections"],
        )
        self.assertEqual(
            asset_record(output, output / "maps" / "figures.json"),
            manifest["figure_map"],
        )
        packaged_sections = read_json(output / manifest["sections"]["path"])
        packaged_figures = read_json(output / manifest["figure_map"]["path"])
        self.assertEqual(
            [(1, 2)],
            [
                (
                    section["start_pdf_page"],
                    section["end_pdf_page"],
                )
                for section in packaged_sections["sections"]
            ],
        )
        self.assertEqual(
            "pdf-points-top-left",
            packaged_figures["coordinate_space"],
        )
        self.assertEqual(
            [
                (1, 1, [54, 220, 306, 340]),
                (2, 2, [54, 220, 306, 340]),
            ],
            [
                (part["order"], part["pdf_page"], part["bbox"])
                for part in packaged_figures["figures"][0]["parts"]
            ],
        )
        reconstructed_text: list[str] = []
        for page in manifest["pages"]:
            self.assertEqual(
                {"blocks", "svg"},
                set(page["assets"]),
            )
            for role, expected_name in (
                ("blocks", "blocks.json"),
                ("svg", "page.svg"),
            ):
                record = page["assets"][role]
                path = output / record["path"]
                self.assertEqual(expected_name, path.name)
                self.assertEqual(asset_record(output, path), record)
            blocks = read_json(output / page["assets"]["blocks"]["path"])
            reconstructed_text.extend(
                block["text"] for block in blocks["blocks"]
            )
        joined_text = " ".join(" ".join(reconstructed_text).split())
        self.assertIn("deterministic scholarly source", joined_text)
        self.assertIn("中文学术文本", joined_text)

        standalone = self.validate(manifest_path)
        replay = self.validate(manifest_path, source=self.source)
        self.assertEqual(0, standalone.exit_code)
        self.assertTrue(standalone.report["valid"])
        self.assertFalse(standalone.report["source_checked"])
        self.assertEqual(0, replay.exit_code)
        self.assertTrue(replay.report["valid"])
        self.assertTrue(replay.report["source_checked"])

        repeated = self.root / "repeated-package"
        self.assertEqual(
            0,
            self.extract(
                repeated,
                pages="1-2",
                maps=(sections, figures),
                extra=("--profile", "music-notation"),
            ).exit_code,
        )
        repeated_manifest = read_json(repeated / "source-package.json")
        for first_page, second_page in zip(
            manifest["pages"],
            repeated_manifest["pages"],
            strict=True,
        ):
            for role in ("blocks", "svg"):
                first_bytes = (
                    output / first_page["assets"][role]["path"]
                ).read_bytes()
                second_bytes = (
                    repeated / second_page["assets"][role]["path"]
                ).read_bytes()
                self.assertEqual(first_bytes, second_bytes)
                self.assertEqual(
                    first_page["assets"][role],
                    second_page["assets"][role],
                )

    def test_text_volume_and_possible_scan_status_scenarios(self) -> None:
        scenarios = (
            {
                "name": "sparse-positive",
                "page": PdfPage(text="Op. 1"),
                "status": "review_required",
                "page_status": "pass",
                "issues": {"source.sparse-text"},
            },
            {
                "name": "zero-text",
                "page": PdfPage(),
                "status": "fail",
                "page_status": "pass",
                "issues": {"source.no-text"},
            },
            {
                "name": "low-text-image",
                "page": PdfPage(text="Tiny", raster_image=True),
                "status": "review_required",
                "page_status": "review_required",
                "issues": {"pdf-0001.possible-scan", "source.sparse-text"},
            },
        )
        for scenario in scenarios:
            with self.subTest(scenario=scenario["name"]):
                write_pdf(self.source, [scenario["page"]])
                output = self.root / str(scenario["name"])
                extracted = self.extract(output, pages="1")

                self.assertEqual(0, extracted.exit_code)
                self.assertTrue(extracted.report["published"])
                self.assertEqual(scenario["status"], extracted.report["status"])
                manifest_path = output / "source-package.json"
                manifest = read_json(manifest_path)
                self.assertEqual(scenario["status"], manifest["status"])
                self.assertEqual(
                    scenario["page_status"],
                    manifest["pages"][0]["status"],
                )
                self.assertEqual(
                    scenario["issues"],
                    {issue["id"] for issue in manifest["issues"]},
                )
                validated = self.validate(manifest_path)
                self.assertEqual(0, validated.exit_code)
                self.assertTrue(validated.report["valid"])
                self.assertEqual(scenario["status"], validated.report["status"])

    def test_validator_rejects_schema_hash_and_path_tampering(
        self,
    ) -> None:
        baseline = self.make_baseline_package()
        cases: tuple[
            tuple[str, Callable[[Path, dict[str, Any]], None]],
            ...,
        ] = (
            (
                "schema",
                lambda _package, manifest: manifest.pop("package_id"),
            ),
            (
                "hash",
                lambda _package, manifest: manifest["pages"][0]["assets"][
                    "blocks"
                ].update({"sha256": "0" * 64}),
            ),
            (
                "traversal",
                lambda _package, manifest: manifest["pages"][0]["assets"][
                    "blocks"
                ].update({"path": "../blocks.json"}),
            ),
        )
        for name, mutate in cases:
            with self.subTest(tampering=name):
                package = self.root / f"tamper-{name}"
                copy_tree(baseline, package)
                manifest_path = package / "source-package.json"
                manifest = read_json(manifest_path)
                mutate(package, manifest)
                write_json(manifest_path, manifest)

                result = self.validate(manifest_path)

                self.assertEqual(1, result.exit_code)
                self.assertFalse(result.report["valid"])
                self.assertEqual("fail", result.report["status"])
                self.assertTrue(result.report["errors"])

    def test_validator_rejects_raw_duplicate_object_keys(self) -> None:
        self.make_happy_source()
        _, figures = self.write_maps()
        baseline = self.root / "duplicate-key-baseline"
        extracted = self.extract(
            baseline,
            pages="1-2",
            maps=(None, figures),
            extra=("--profile", "music-notation"),
        )
        self.assertEqual(0, extracted.exit_code)

        cases = (
            (
                "manifest",
                Path("source-package.json"),
                b'  "schema_version": "1.0",\n',
                None,
                "schema_version",
            ),
            (
                "figure-map",
                Path("maps/figures.json"),
                b'      "id": "example-a",\n',
                "figure_map",
                "id",
            ),
            (
                "source-blocks",
                Path("pages/pdf-0001/blocks.json"),
                b'      "id": "pdf-0001-block-0001",\n',
                "blocks",
                "id",
            ),
        )
        for name, relative_path, binding, asset_role, duplicate_key in cases:
            with self.subTest(duplicate_key=name):
                package = self.root / f"duplicate-key-{name}"
                copy_tree(baseline, package)
                manifest_path = package / "source-package.json"
                manifest = read_json(manifest_path)
                asset_path = package / relative_path
                self.duplicate_raw_json_line(asset_path, binding)
                if asset_role == "figure_map":
                    manifest["figure_map"] = asset_record(
                        package,
                        asset_path,
                    )
                    write_json(manifest_path, manifest)
                elif asset_role == "blocks":
                    self.refresh_manifest_asset(package, manifest, 0, "blocks")
                    write_json(manifest_path, manifest)

                result = self.validate(manifest_path)

                self.assertEqual(1, result.exit_code)
                self.assertFalse(result.report["valid"])
                self.assertEqual("fail", result.report["status"])
                self.assertTrue(
                    any(
                        (f"duplicate JSON object key: {duplicate_key}") in error
                        for error in result.report["errors"]
                    )
                )
                self.assertFalse(
                    any(
                        "byte length does not match" in error
                        or "SHA-256 does not match" in error
                        for error in result.report["errors"]
                    )
                )

    def test_extract_rejects_duplicate_figure_map_object_keys(self) -> None:
        self.make_happy_source()
        _, figures = self.write_maps()
        self.duplicate_raw_json_line(
            figures,
            b'      "id": "example-a",\n',
        )
        output = self.root / "duplicate-figure-map-package"

        result = self.extract(
            output,
            pages="1-2",
            maps=(None, figures),
            extra=("--profile", "music-notation"),
        )

        self.assertEqual(2, result.exit_code)
        self.assertFalse(result.report["valid"])
        self.assertEqual("fail", result.report["status"])
        self.assertFalse(output.exists())
        self.assertTrue(
            any(
                "duplicate JSON object key: id" in error
                for error in result.report["errors"]
            )
        )

    def test_extract_rejects_invalid_figure_profile_identifier(self) -> None:
        self.make_happy_source()
        _, figures = self.write_maps()
        figure_map = read_json(figures)
        figure_map["figures"][0]["profile"] = "Music Notation"
        write_json(figures, figure_map)
        output = self.root / "invalid-figure-profile-package"

        result = self.extract(
            output,
            pages="1-2",
            maps=(None, figures),
        )

        self.assertEqual(2, result.exit_code)
        self.assertFalse(result.report["valid"])
        self.assertEqual("fail", result.report["status"])
        self.assertFalse(output.exists())
        self.assertTrue(
            any(
                "profile" in error and "Music Notation" in error
                for error in result.report["errors"]
            )
        )

    def test_extract_rejects_incomplete_or_inconsistent_figure_facts(
        self,
    ) -> None:
        self.make_happy_source()
        cases = {
            "missing-source-label": (
                lambda figure: figure.pop("source_label"),
                "source_label",
            ),
            "blank-source-label": (
                lambda figure: figure.update({"source_label": "   "}),
                "source_label",
            ),
            "missing-profile": (
                lambda figure: figure.pop("profile"),
                "profile",
            ),
            "missing-language-inventory": (
                lambda figure: figure.pop("embedded_language_inventory"),
                "embedded_language_inventory",
            ),
            "duplicate-language": (
                lambda figure: figure.update(
                    {"embedded_language_inventory": ["en", "en"]}
                ),
                "embedded_language_inventory",
            ),
            "invalid-language": (
                lambda figure: figure.update(
                    {"embedded_language_inventory": ["not_a_language"]}
                ),
                "embedded_language_inventory",
            ),
            "undeclared-profile": (
                lambda figure: figure.update({"profile": "notation-review"}),
                "not declared by the source package",
            ),
        }
        for name, (mutate, expected) in cases.items():
            with self.subTest(case=name):
                _, figures = self.write_maps()
                figure_map = read_json(figures)
                mutate(figure_map["figures"][0])
                write_json(figures, figure_map)
                output = self.root / f"invalid-figure-facts-{name}"

                result = self.extract(
                    output,
                    pages="1-2",
                    maps=(None, figures),
                    extra=("--profile", "music-notation"),
                )

                self.assertEqual(2, result.exit_code)
                self.assertFalse(result.report["valid"])
                self.assertEqual("fail", result.report["status"])
                self.assertFalse(output.exists())
                self.assertTrue(
                    any(expected in error for error in result.report["errors"]),
                    result.report["errors"],
                )

    def test_validator_groups_unsafe_svg_cases(self) -> None:
        baseline = self.make_baseline_package()
        unsafe_bodies = {
            "script": "<script/>",
            "external-image": (
                '<image x="1" y="1" width="10" height="10" '
                'href="https://example.invalid/image.png"/>'
            ),
            "event-handler": (
                '<rect x="1" y="1" width="10" height="10" '
                'fill="#000" onclick="alert(1)"/>'
            ),
        }
        for name, body in unsafe_bodies.items():
            with self.subTest(unsafe=name):
                package = self.root / f"svg-{name}"
                copy_tree(baseline, package)
                manifest_path = package / "source-package.json"
                manifest = read_json(manifest_path)
                page = manifest["pages"][0]
                svg_path = package / page["assets"]["svg"]["path"]
                svg_path.write_text(
                    (
                        '<svg xmlns="http://www.w3.org/2000/svg" '
                        'width="360" height="480" viewBox="0 0 360 480">'
                        '<rect x="20" y="20" width="20" height="20" '
                        'fill="#000"/>'
                        f"{body}</svg>"
                    ),
                    encoding="utf-8",
                )
                self.refresh_manifest_asset(package, manifest, 0, "svg")
                write_json(manifest_path, manifest)

                result = self.validate(manifest_path)

                self.assertEqual(1, result.exit_code)
                self.assertFalse(result.report["valid"])
                self.assertEqual("fail", result.report["status"])
                self.assertTrue(result.report["errors"])

    def test_validator_rejects_noncanonical_page_svg_geometry(self) -> None:
        baseline = self.make_baseline_package()
        cases = {
            "dimensions": (
                'width="360.004" height="479.996" viewBox="0 0 360.004 479.996"'
            ),
            "viewbox": ('width="360" height="480" viewBox="0 0 360.004 480"'),
        }
        for name, geometry in cases.items():
            with self.subTest(case=name):
                package = self.root / f"svg-geometry-{name}"
                copy_tree(baseline, package)
                manifest_path = package / "source-package.json"
                manifest = read_json(manifest_path)
                svg_path = (
                    package / manifest["pages"][0]["assets"]["svg"]["path"]
                )
                svg_path.write_text(
                    (
                        '<svg xmlns="http://www.w3.org/2000/svg" '
                        f"{geometry}>"
                        '<rect x="20" y="20" width="20" height="20" '
                        'fill="#000"/></svg>'
                    ),
                    encoding="utf-8",
                )
                self.refresh_manifest_asset(package, manifest, 0, "svg")
                write_json(manifest_path, manifest)

                result = self.validate(manifest_path)

                self.assertEqual(1, result.exit_code, result)
                self.assertFalse(result.report["valid"])
                self.assertTrue(
                    any(
                        "page geometry" in error
                        for error in result.report["errors"]
                    )
                )

    def test_source_mismatch_and_exact_replay_drift_are_rejected(self) -> None:
        package = self.make_baseline_package()
        manifest_path = package / "source-package.json"
        alternate = self.root / "alternate.pdf"
        write_pdf(
            alternate,
            [
                PdfPage(text=f"Changed page one. {LONG_TEXT}"),
                PdfPage(text=f"Changed page two. {LONG_TEXT}"),
            ],
        )

        mismatch = self.validate(manifest_path, source=alternate)
        self.assertEqual(1, mismatch.exit_code)
        self.assertFalse(mismatch.report["valid"])
        self.assertTrue(mismatch.report["source_checked"])
        self.assertTrue(mismatch.report["errors"])

        for role in ("blocks", "svg"):
            with self.subTest(replay_drift=role):
                drift = self.root / f"replay-drift-{role}"
                copy_tree(package, drift)
                drift_manifest_path = drift / "source-package.json"
                manifest = read_json(drift_manifest_path)
                asset_path = (
                    drift / manifest["pages"][0]["assets"][role]["path"]
                )
                if role == "blocks":
                    blocks = read_json(asset_path)
                    text = blocks["blocks"][0]["text"]
                    index = next(
                        position
                        for position, character in enumerate(text)
                        if not character.isspace()
                    )
                    replacement = "X" if text[index] != "X" else "Y"
                    blocks["blocks"][0]["text"] = (
                        text[:index] + replacement + text[index + 1 :]
                    )
                    write_json(asset_path, blocks)
                else:
                    asset_path.write_bytes(asset_path.read_bytes() + b"\n")
                self.refresh_manifest_asset(drift, manifest, 0, role)
                write_json(drift_manifest_path, manifest)

                standalone = self.validate(drift_manifest_path)
                replay = self.validate(drift_manifest_path, source=self.source)
                self.assertEqual(0, standalone.exit_code)
                self.assertTrue(standalone.report["valid"])
                self.assertEqual(1, replay.exit_code)
                self.assertFalse(replay.report["valid"])
                self.assertTrue(replay.report["source_checked"])
                self.assertTrue(replay.report["errors"])

    def test_source_replay_rejects_manifest_page_count_mismatch(self) -> None:
        package = self.make_baseline_package()
        manifest_path = package / "source-package.json"
        manifest = read_json(manifest_path)
        manifest["source"]["page_count"] += 1
        write_json(manifest_path, manifest)

        result = self.validate(manifest_path, source=self.source)

        self.assertEqual(1, result.exit_code)
        self.assertFalse(result.report["valid"])
        self.assertEqual("fail", result.report["status"])
        self.assertTrue(result.report["source_checked"])
        self.assertTrue(
            any(
                "source PDF page count does not match manifest" in error
                for error in result.report["errors"]
            )
        )

    def test_force_replacement_and_rollback_preserve_expected_tree(
        self,
    ) -> None:
        self.make_happy_source()
        output = self.root / "force-package"
        self.assertEqual(0, self.extract(output, pages="1-2").exit_code)
        original = tree_snapshot(output)

        refused = self.extract(output, pages="1-2")
        self.assertEqual(2, refused.exit_code)
        self.assertEqual(original, tree_snapshot(output))

        self.make_happy_source(suffix=" Replacement revision.")
        replaced = self.extract(output, pages="1-2", extra=("--force",))
        self.assertEqual(0, replaced.exit_code)
        replacement = tree_snapshot(output)
        self.assertNotEqual(original, replacement)

        self.make_happy_source(suffix=" Rollback candidate.")
        original_replace = Path.replace
        parent_before = tree_snapshot(self.root)
        publish_failed = False
        failure_message = "synthetic publish failure"

        def fail_candidate_publish(path: Path, target: Path) -> Path:
            nonlocal publish_failed
            if not publish_failed and path != output and Path(target) == output:
                publish_failed = True
                raise PermissionError(failure_message)
            return original_replace(path, target)

        with mock.patch.object(Path, "replace", new=fail_candidate_publish):
            failed = self.extract(
                output,
                pages="1-2",
                extra=("--force",),
            )

        self.assertEqual(2, failed.exit_code)
        self.assertFalse(failed.report["valid"])
        self.assertEqual(replacement, tree_snapshot(output))
        self.assertEqual(parent_before, tree_snapshot(self.root))

    def test_backup_cleanup_failure_warns_after_committed_replacement(
        self,
    ) -> None:
        self.make_happy_source()
        output = self.root / "cleanup-warning-package"
        first = self.extract(output, pages="1-2")
        self.assertEqual(0, first.exit_code, first)
        original = tree_snapshot(output)

        self.make_happy_source(suffix=" Replacement revision.")
        original_remove = reconstruct_pdf.remove_path

        def fail_backup_cleanup(path: Path) -> None:
            if path.name.startswith(f".{output.name}.backup-"):
                raise PermissionError
            original_remove(path)

        with mock.patch.object(
            reconstruct_pdf,
            "remove_path",
            new=fail_backup_cleanup,
        ):
            replaced = self.extract(
                output,
                pages="1-2",
                extra=("--force",),
            )

        self.assertEqual(0, replaced.exit_code, replaced)
        self.assertTrue(replaced.report["valid"])
        self.assertTrue(replaced.report["published"])
        self.assertEqual([], replaced.report["errors"])
        self.assertIn(
            "warning: reconstruction committed but backup remains",
            replaced.stderr,
        )
        self.assertNotEqual(original, tree_snapshot(output))
        self.assertEqual(
            1,
            len(
                list(
                    self.root.glob(
                        f".{output.name}.backup-*",
                    )
                )
            ),
        )

    @unittest.skipUnless(
        sys.platform == "win32",
        "Win32 final-component aliases are Windows-specific.",
    )
    def test_windows_output_alias_cannot_replace_source_directory(self) -> None:
        output = self.root / "owned-package"
        output.mkdir()
        self.source = output / "source.pdf"
        self.make_happy_source()
        (output / "source-package.json").write_text(
            "owned previous package\n",
            encoding="utf-8",
        )
        before = tree_snapshot(output)

        refused = self.extract(
            output.with_name(f"{output.name}."),
            pages="1-2",
            extra=("--force",),
        )

        self.assertEqual(2, refused.exit_code, refused)
        self.assertTrue(
            any(
                "must not end in a dot or space" in error
                for error in refused.report["errors"]
            )
        )
        self.assertEqual(before, tree_snapshot(output))
        self.assertTrue(self.source.is_file())

    @unittest.skipUnless(
        sys.platform == "win32",
        "Win32 short-name aliases are Windows-specific.",
    )
    def test_windows_short_name_alias_cannot_replace_source_directory(
        self,
    ) -> None:
        self.make_happy_source()
        output = self.root / "owned publication package"
        first = self.extract(output, pages="1-2")
        self.assertEqual(0, first.exit_code, first)

        self.source = output / "nested-source.pdf"
        self.make_happy_source(suffix=" Nested source revision.")
        before = tree_snapshot(output)

        short_output = windows_short_path(output)
        if short_output is None:
            self.skipTest("Win32 short-name lookup is unavailable")
        if short_output.name.casefold() == output.name.casefold():
            self.skipTest("8.3 short names are disabled for this volume")
        self.assertTrue(output.samefile(short_output))

        refused = self.extract(
            short_output,
            pages="1-2",
            extra=("--force",),
        )

        self.assertEqual(2, refused.exit_code, refused)
        self.assertTrue(
            any(
                "source PDF must be outside the output directory" in error
                for error in refused.report["errors"]
            )
        )
        self.assertEqual(before, tree_snapshot(output))
        self.assertTrue(self.source.is_file())

    def test_existing_case_alias_cannot_replace_source_directory(self) -> None:
        output = self.root / "OwnedPackage"
        output.mkdir()
        self.source = output / "source.pdf"
        self.make_happy_source()
        (output / "source-package.json").write_text(
            "owned previous package\n",
            encoding="utf-8",
        )
        alias = output.with_name(output.name.swapcase())
        try:
            aliases_output = alias.samefile(output)
        except OSError:
            aliases_output = False
        if not aliases_output:
            self.skipTest("test filesystem is case-sensitive")
        before = tree_snapshot(output)

        refused = self.extract(
            alias,
            pages="1-2",
            extra=("--force",),
        )

        self.assertEqual(2, refused.exit_code, refused)
        self.assertTrue(
            any(
                "source PDF must be outside the output directory" in error
                for error in refused.report["errors"]
            )
        )
        self.assertEqual(before, tree_snapshot(output))
        self.assertTrue(self.source.is_file())

    def test_force_rejects_existing_regular_file_unchanged(self) -> None:
        self.make_happy_source()
        output = self.root / "regular-file-output"
        output.write_bytes(b"unrelated regular file\n")
        before = tree_snapshot(self.root)

        refused = self.extract(output, pages="1-2", extra=("--force",))

        self.assertEqual(2, refused.exit_code)
        self.assertTrue(
            any(
                "not an ordinary directory" in error
                for error in refused.report["errors"]
            )
        )
        self.assertEqual(before, tree_snapshot(self.root))

    def test_force_rejects_unowned_nonempty_directory_unchanged(self) -> None:
        self.make_happy_source()
        output = self.root / "unowned-directory"
        output.mkdir()
        sentinel = output / "unrelated-sentinel.txt"
        sentinel.write_text("must survive\n", encoding="utf-8")
        before = tree_snapshot(self.root)

        refused = self.extract(output, pages="1-2", extra=("--force",))

        self.assertEqual(2, refused.exit_code)
        self.assertTrue(
            any(
                "ownership marker" in error
                for error in refused.report["errors"]
            )
        )
        self.assertEqual(before, tree_snapshot(self.root))
        self.assertEqual("must survive\n", sentinel.read_text(encoding="utf-8"))

    def test_force_allows_empty_or_damaged_owned_directory(self) -> None:
        for name, damaged in (("empty", False), ("damaged-owned", True)):
            with self.subTest(case=name):
                self.make_happy_source()
                output = self.root / name
                output.mkdir()
                if damaged:
                    (output / "source-package.json").write_text(
                        "damaged older package\n",
                        encoding="utf-8",
                    )
                    (output / "stale.bin").write_bytes(b"stale")

                replaced = self.extract(
                    output,
                    pages="1-2",
                    extra=("--force",),
                )

                self.assertEqual(0, replaced.exit_code, replaced)
                self.assertEqual(
                    "pass",
                    read_json(output / "source-package.json")["status"],
                )
                self.assertFalse((output / "stale.bin").exists())

    def test_force_rejects_symlink_ownership_marker_unchanged(self) -> None:
        self.make_happy_source()
        output = self.root / "linked-marker-output"
        output.mkdir()
        external_marker = self.root / "external-source-package.json"
        external_marker.write_text("external marker\n", encoding="utf-8")
        marker = output / "source-package.json"
        try:
            marker.symlink_to(external_marker)
        except (NotImplementedError, OSError) as error:
            self.skipTest(f"file symlinks are unavailable: {error}")
        stale = output / "stale.bin"
        stale.write_bytes(b"stale")
        link_target = marker.readlink()
        before = tree_snapshot(self.root)

        refused = self.extract(output, pages="1-2", extra=("--force",))

        self.assertEqual(2, refused.exit_code)
        self.assertTrue(
            any(
                "regular non-symlink/non-reparse" in error
                for error in refused.report["errors"]
            )
        )
        self.assertTrue(marker.is_symlink())
        self.assertEqual(link_target, marker.readlink())
        self.assertEqual(before, tree_snapshot(self.root))
        self.assertEqual(
            "external marker\n",
            external_marker.read_text(encoding="utf-8"),
        )

    def test_encryption_attachments_and_javascript_are_rejected(self) -> None:
        cases: tuple[
            tuple[str, Callable[[Path], None]],
            ...,
        ] = (
            ("encrypted", encrypt_pdf),
            ("attachment", add_pdf_attachment),
            (
                "open-action",
                lambda path: add_pdf_javascript(path, "open-action"),
            ),
        )
        for name, mutate in cases:
            with self.subTest(feature=name):
                write_pdf(self.source, [PdfPage(text=LONG_TEXT)])
                mutate(self.source)
                output = self.root / f"unsafe-{name}"

                result = self.extract(output, pages="1")

                self.assertEqual(2, result.exit_code)
                self.assertFalse(result.report["valid"])
                self.assertEqual("fail", result.report["status"])
                self.assertFalse(output.exists())
                self.assertTrue(result.report["errors"])

    def test_rendition_action_javascript_is_rejected(self) -> None:
        write_pdf(self.source, [PdfPage(text=LONG_TEXT)])
        add_pdf_rendition_javascript(self.source)
        output = self.root / "unsafe-rendition-javascript"

        result = self.extract(output, pages="1")

        self.assertEqual(2, result.exit_code)
        self.assertFalse(result.report["valid"])
        self.assertEqual("fail", result.report["status"])
        self.assertEqual(
            ["embedded PDF JavaScript is not accepted"],
            result.report["errors"],
        )
        self.assertFalse(output.exists())

    def test_page_reachable_3d_javascript_is_rejected(self) -> None:
        write_pdf(self.source, [PdfPage(text=LONG_TEXT)])
        add_page_reachable_3d_javascript(self.source)
        output = self.root / "unsafe-3d-javascript"

        result = self.extract(output, pages="1")

        self.assertEqual(2, result.exit_code)
        self.assertFalse(result.report["valid"])
        self.assertEqual("fail", result.report["status"])
        self.assertEqual(
            ["embedded PDF JavaScript is not accepted"],
            result.report["errors"],
        )
        self.assertFalse(output.exists())

    def test_page_range_parsing_boundaries_include_500_and_reject_501(
        self,
    ) -> None:
        accepted = {
            "single": ("1", 1, [1]),
            "mixed": ("1,3-5", 5, [1, 3, 4, 5]),
            "limit": ("1-500", 500, list(range(1, 501))),
        }
        for name, (expression, page_count, expected) in accepted.items():
            with self.subTest(accepted=name):
                self.assertEqual(
                    expected,
                    reconstruct_pdf.parse_pages(expression, page_count),
                )

        rejected = {
            "empty": ("", 10),
            "zero": ("0", 10),
            "descending": ("3-2", 10),
            "outside": ("11", 10),
            "duplicate": ("1,1", 10),
            "over-limit": ("1-501", 501),
        }
        for name, (expression, page_count) in rejected.items():
            with (
                self.subTest(rejected=name),
                self.assertRaises(reconstruct_pdf.ContractError),  # noqa: PT027
            ):
                reconstruct_pdf.parse_pages(
                    expression,
                    page_count,
                )

    def test_severity_status_and_page_status_derivation(self) -> None:
        issue_sets = (
            ("pass", [], "pass"),
            (
                "review",
                [
                    reconstruct_pdf.issue_record(
                        "pdf-0001.review",
                        "review_required",
                        "Review.",
                        1,
                    )
                ],
                "review_required",
            ),
            (
                "fail-dominates",
                [
                    reconstruct_pdf.issue_record(
                        "pdf-0001.review",
                        "review_required",
                        "Review.",
                        1,
                    ),
                    reconstruct_pdf.issue_record(
                        "source.failure",
                        "fail",
                        "Failure.",
                    ),
                ],
                "fail",
            ),
        )
        for name, issues, expected in issue_sets:
            with self.subTest(issue_set=name):
                self.assertEqual(
                    expected,
                    reconstruct_pdf.status_from_issues(issues),
                )

        pages = [
            {
                "id": "pdf-0001",
                "pdf_page": 1,
                "text_characters": 4,
                "replacement_characters": 0,
                "image_count": 0,
            },
            {
                "id": "pdf-0002",
                "pdf_page": 2,
                "text_characters": 40,
                "replacement_characters": 1,
                "image_count": 0,
            },
        ]
        issues = reconstruct_pdf.derive_issues(
            pages,
            hidden_pages=set(),
            trace_failure_pages=set(),
            profiles=[],
            has_figures=False,
        )
        self.assertEqual(
            "review_required", reconstruct_pdf.status_from_issues(issues)
        )
        self.assertEqual("pass", reconstruct_pdf.page_status(issues, 1))
        self.assertEqual(
            "review_required",
            reconstruct_pdf.page_status(issues, 2),
        )

        zero_text_issues = reconstruct_pdf.derive_issues(
            [
                {
                    "id": "pdf-0001",
                    "pdf_page": 1,
                    "text_characters": 0,
                    "replacement_characters": 0,
                    "image_count": 0,
                }
            ],
            hidden_pages=set(),
            trace_failure_pages=set(),
            profiles=[],
            has_figures=False,
        )
        self.assertEqual(
            "fail", reconstruct_pdf.status_from_issues(zero_text_issues)
        )
        self.assertEqual(
            "pass", reconstruct_pdf.page_status(zero_text_issues, 1)
        )

    def test_confined_relative_asset_paths_reject_unsafe_forms(self) -> None:
        package = self.root / "path-package"
        asset = package / "pages" / "pdf-0001" / "blocks.json"
        asset.parent.mkdir(parents=True)
        asset.write_bytes(canonical_json({"blocks": []}))
        self.assertEqual(
            asset.resolve(),
            reconstruct_pdf.resolve_asset_path(
                package,
                "pages/pdf-0001/blocks.json",
            ),
        )

        unsafe = (
            "../outside.json",
            "/absolute.json",
            "C:/drive.json",
            r"pages\pdf-0001\blocks.json",
            "https://example.invalid/asset.json",
        )
        for value in unsafe:
            with (
                self.subTest(path=value),
                self.assertRaises(reconstruct_pdf.ContractError),  # noqa: PT027
            ):
                reconstruct_pdf.resolve_asset_path(
                    package,
                    value,
                )

        outside = self.root / "outside.json"
        outside.write_text("outside", encoding="utf-8")
        link = package / "escape.json"
        try:
            link.symlink_to(outside)
        except OSError:
            original_is_symlink = Path.is_symlink

            def report_fixture_symlink(path: Path) -> bool:
                return path == link or original_is_symlink(path)

            with (
                mock.patch.object(
                    Path,
                    "is_symlink",
                    new=report_fixture_symlink,
                ),
                self.assertRaises(reconstruct_pdf.ContractError),  # noqa: PT027
            ):
                reconstruct_pdf.resolve_asset_path(
                    package,
                    "escape.json",
                )
        else:
            with self.assertRaises(  # noqa: PT027
                reconstruct_pdf.ContractError
            ):
                reconstruct_pdf.resolve_asset_path(
                    package,
                    "escape.json",
                )


if __name__ == "__main__":
    unittest.main()
