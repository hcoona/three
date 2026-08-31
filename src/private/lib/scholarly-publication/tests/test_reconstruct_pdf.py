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


class ReconstructPdfTests(unittest.TestCase):
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

    def assert_contracted_manifest_shape(
        self,
        manifest: dict[str, Any],
    ) -> None:
        self.assertEqual(
            {
                "schema_version",
                "source",
                "selection",
                "coordinate_system",
                "profiles",
                "pages",
                "sections",
                "figure_map",
                "issues",
                "status",
            },
            set(manifest),
        )
        self.assertEqual(
            {"sha256", "bytes", "rights_note", "page_count"},
            set(manifest["source"]),
        )
        self.assertEqual({"pdf_pages"}, set(manifest["selection"]))
        self.assertEqual(
            {"units", "origin", "bbox_order"},
            set(manifest["coordinate_system"]),
        )
        for page in manifest["pages"]:
            self.assertEqual(
                {
                    "id",
                    "pdf_page",
                    "width",
                    "height",
                    "rotation",
                    "assets",
                    "status",
                },
                set(page),
            )

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
                "selected_pages": 2,
                "published": True,
            },
            extracted.report,
        )
        manifest_path = output / "source-package.json"
        manifest = read_json(manifest_path)
        self.assert_contracted_manifest_shape(manifest)
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
            self.assertEqual(
                {"schema_version", "coordinate_space", "page_id", "blocks"},
                set(blocks),
            )
            for block in blocks["blocks"]:
                self.assertEqual(
                    {"id", "source_order", "bbox", "text"},
                    set(block),
                )
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
                lambda _package, manifest: manifest["source"].pop(
                    "rights_note"
                ),
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

    def test_validator_rejects_nonfinite_json_in_all_package_documents(
        self,
    ) -> None:
        self.make_happy_source()
        _, figures = self.write_maps()
        baseline = self.root / "nonfinite-baseline"
        extracted = self.extract(
            baseline,
            pages="1-2",
            maps=(None, figures),
            extra=("--profile", "music-notation"),
        )
        self.assertEqual(0, extracted.exit_code, extracted)

        marker = b'"__NONFINITE__"'
        for document in ("manifest", "source-blocks", "figure-map"):
            for token in ("NaN", "Infinity", "-Infinity", "1e999"):
                with self.subTest(document=document, token=token):
                    package = self.root / f"nonfinite-{document}-{token}"
                    copy_tree(baseline, package)
                    manifest_path = package / "source-package.json"
                    manifest = read_json(manifest_path)

                    if document == "manifest":
                        target = manifest_path
                        manifest["pages"][0]["width"] = "__NONFINITE__"
                        write_json(target, manifest)
                    elif document == "source-blocks":
                        target = (
                            package
                            / manifest["pages"][0]["assets"]["blocks"]["path"]
                        )
                        blocks = read_json(target)
                        blocks["blocks"][0]["bbox"][0] = "__NONFINITE__"
                        write_json(target, blocks)
                    else:
                        target = package / manifest["figure_map"]["path"]
                        figure_map = read_json(target)
                        figure_map["figures"][0]["parts"][0]["bbox"][0] = (
                            "__NONFINITE__"
                        )
                        write_json(target, figure_map)

                    content = target.read_bytes()
                    self.assertEqual(1, content.count(marker))
                    target.write_bytes(
                        content.replace(marker, token.encode("ascii"), 1)
                    )
                    if document == "source-blocks":
                        self.refresh_manifest_asset(
                            package,
                            manifest,
                            0,
                            "blocks",
                        )
                        write_json(manifest_path, manifest)
                    elif document == "figure-map":
                        manifest["figure_map"] = asset_record(package, target)
                        write_json(manifest_path, manifest)

                    result = self.validate(manifest_path)

                    self.assertEqual(1, result.exit_code, result)
                    self.assertFalse(result.report["valid"])
                    self.assertTrue(
                        any(
                            f"non-finite JSON number: {token}" in error
                            for error in result.report["errors"]
                        ),
                        result.report["errors"],
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

    def test_validator_rejects_page_and_bbox_bounds(self) -> None:
        self.make_happy_source()
        _, figures = self.write_maps()
        baseline = self.root / "bounds-baseline"
        extracted = self.extract(
            baseline,
            pages="1-2",
            maps=(None, figures),
            extra=("--profile", "music-notation"),
        )
        self.assertEqual(0, extracted.exit_code, extracted)

        def page_dimension(
            _package: Path,
            manifest: dict[str, Any],
        ) -> None:
            manifest["pages"][0]["width"] = 0

        def source_page_bound(
            _package: Path,
            manifest: dict[str, Any],
        ) -> None:
            manifest["source"]["page_count"] = 1

        def selection_duplicate(
            _package: Path,
            manifest: dict[str, Any],
        ) -> None:
            manifest["selection"]["pdf_pages"] = [1, 1]

        def page_order(
            _package: Path,
            manifest: dict[str, Any],
        ) -> None:
            manifest["pages"].reverse()

        def block_bbox(package: Path, manifest: dict[str, Any]) -> None:
            blocks_path = (
                package / manifest["pages"][0]["assets"]["blocks"]["path"]
            )
            blocks = read_json(blocks_path)
            blocks["blocks"][0]["bbox"] = [0, 0, 361, 20]
            write_json(blocks_path, blocks)
            self.refresh_manifest_asset(package, manifest, 0, "blocks")

        def figure_bbox(package: Path, manifest: dict[str, Any]) -> None:
            figure_path = package / manifest["figure_map"]["path"]
            figure_map = read_json(figure_path)
            figure_map["figures"][0]["parts"][0]["bbox"] = [0, 0, 361, 20]
            write_json(figure_path, figure_map)
            manifest["figure_map"] = asset_record(package, figure_path)

        cases = (
            ("positive-dimension", page_dimension, "minimum"),
            ("source-page-bound", source_page_bound, "source.page_count"),
            ("selection-duplicate", selection_duplicate, "non-unique"),
            ("page-order", page_order, "exactly follow"),
            ("block-bbox", block_bbox, "exceeds page geometry"),
            ("figure-bbox", figure_bbox, "exceeds page 1 bounds"),
        )
        for name, mutate, expected in cases:
            with self.subTest(case=name):
                package = self.root / f"bounds-{name}"
                copy_tree(baseline, package)
                manifest_path = package / "source-package.json"
                manifest = read_json(manifest_path)
                mutate(package, manifest)
                write_json(manifest_path, manifest)

                result = self.validate(manifest_path)

                self.assertEqual(1, result.exit_code, result)
                self.assertFalse(result.report["valid"])
                self.assertTrue(
                    any(expected in error for error in result.report["errors"]),
                    result.report["errors"],
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

    def test_any_existing_output_is_rejected_unchanged_before_staging(
        self,
    ) -> None:
        self.make_happy_source()
        for kind in ("file", "empty-directory", "directory", "dangling-link"):
            with self.subTest(kind=kind):
                output = self.root / f"existing-{kind}"
                link_target: Path | None = None
                if kind == "file":
                    output.write_bytes(b"unrelated regular file\n")
                elif kind == "empty-directory":
                    output.mkdir()
                elif kind == "directory":
                    output.mkdir()
                    (output / "sentinel.txt").write_text(
                        "must survive\n",
                        encoding="utf-8",
                    )
                else:
                    link_target = self.root / "missing-link-target"
                    try:
                        output.symlink_to(link_target)
                    except (NotImplementedError, OSError):
                        continue
                    link_target = output.readlink()

                before = tree_snapshot(self.root)
                with mock.patch.object(
                    reconstruct_pdf,
                    "create_candidate",
                    wraps=reconstruct_pdf.create_candidate,
                ) as create_candidate:
                    refused = self.extract(output, pages="1-2")

                self.assertEqual(2, refused.exit_code, refused)
                self.assertFalse(refused.report["valid"])
                self.assertTrue(
                    any(
                        "output already exists" in error
                        for error in refused.report["errors"]
                    )
                )
                create_candidate.assert_not_called()
                self.assertEqual(before, tree_snapshot(self.root))
                if kind == "file":
                    self.assertEqual(
                        b"unrelated regular file\n",
                        output.read_bytes(),
                    )
                elif kind == "empty-directory":
                    self.assertTrue(output.is_dir())
                    self.assertEqual([], list(output.iterdir()))
                elif kind == "directory":
                    self.assertEqual(
                        "must survive\n",
                        (output / "sentinel.txt").read_text(encoding="utf-8"),
                    )
                else:
                    self.assertTrue(output.is_symlink())
                    self.assertEqual(link_target, output.readlink())
                self.assertEqual(
                    [],
                    list(self.root.glob(f".{output.name}.build-*")),
                )

    def test_force_option_is_unrecognized(self) -> None:
        self.make_happy_source()
        output = self.root / "force-is-not-supported"

        result = self.extract(output, pages="1-2", extra=("--force",))

        self.assertEqual(2, result.exit_code, result)
        self.assertFalse(result.report["valid"])
        self.assertFalse(output.exists())
        self.assertTrue(
            any(
                "unrecognized arguments: --force" in error
                for error in result.report["errors"]
            )
        )

    def test_successful_publication_uses_one_rename_and_no_backup(self) -> None:
        self.make_happy_source()
        output = self.root / "one-rename-package"
        rename_calls: list[tuple[Path, Path]] = []
        original_rename = Path.rename

        def track_rename(path: Path, target: Path) -> Path:
            rename_calls.append((path, Path(target)))
            return original_rename(path, target)

        with mock.patch.object(Path, "rename", new=track_rename):
            result = self.extract(output, pages="1-2")

        self.assertEqual(0, result.exit_code, result)
        self.assertEqual(1, len(rename_calls))
        candidate, target = rename_calls[0]
        self.assertEqual(output, target)
        self.assertTrue(candidate.name.startswith(f".{output.name}.build-"))
        self.assertEqual([], list(self.root.glob(f".{output.name}.backup-*")))
        self.assertEqual([], list(self.root.glob(f".{output.name}.build-*")))

    def test_rename_failure_leaves_output_absent_and_candidate_cleaned(
        self,
    ) -> None:
        self.make_happy_source()
        output = self.root / "rename-failure-package"
        before = tree_snapshot(self.root)
        original_rename = Path.rename
        rename_calls = 0
        failure_message = "synthetic final rename failure"

        def fail_publication(path: Path, target: Path) -> Path:
            nonlocal rename_calls
            if Path(target) == output:
                rename_calls += 1
                raise PermissionError(failure_message)
            return original_rename(path, target)

        with mock.patch.object(Path, "rename", new=fail_publication):
            result = self.extract(output, pages="1-2")

        self.assertEqual(2, result.exit_code, result)
        self.assertFalse(result.report["valid"])
        self.assertEqual(1, rename_calls)
        self.assertFalse(output.exists())
        self.assertFalse(output.is_symlink())
        self.assertEqual(before, tree_snapshot(self.root))
        self.assertEqual([], list(self.root.glob(f".{output.name}.build-*")))
        self.assertEqual([], list(self.root.glob(f".{output.name}.backup-*")))
        self.assertTrue(
            any(failure_message in error for error in result.report["errors"])
        )

    def test_candidate_cleanup_failure_is_reported(self) -> None:
        self.make_happy_source()
        output = self.root / "cleanup-failure-package"
        build_failure = "synthetic build failure"
        cleanup_failure = "synthetic cleanup failure"

        def fail_build(_args: Any, _candidate: Path) -> Any:
            raise reconstruct_pdf.ContractError(build_failure)

        def fail_cleanup(_path: Path) -> None:
            raise PermissionError(cleanup_failure)

        with (
            mock.patch.object(
                reconstruct_pdf,
                "build_candidate",
                new=fail_build,
            ),
            mock.patch.object(
                reconstruct_pdf,
                "remove_candidate",
                new=fail_cleanup,
            ),
        ):
            result = self.extract(output, pages="1-2")

        candidates = list(self.root.glob(f".{output.name}.build-*"))
        try:
            self.assertEqual(2, result.exit_code, result)
            self.assertFalse(result.report["valid"])
            self.assertFalse(output.exists())
            self.assertEqual(1, len(candidates))
            self.assertTrue(
                any(
                    "cannot clean candidate directory: synthetic cleanup "
                    "failure" in error
                    for error in result.report["errors"]
                )
            )
        finally:
            for candidate in candidates:
                candidate.rmdir()

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
            reconstruct_pdf.PageReviewFacts(
                page_id="pdf-0001",
                page_number=1,
                text_characters=4,
                replacement_characters=0,
                has_image=False,
                hidden_text=False,
            ),
            reconstruct_pdf.PageReviewFacts(
                page_id="pdf-0002",
                page_number=2,
                text_characters=40,
                replacement_characters=1,
                has_image=False,
                hidden_text=False,
            ),
        ]
        issues = reconstruct_pdf.derive_issues(
            pages,
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
                reconstruct_pdf.PageReviewFacts(
                    page_id="pdf-0001",
                    page_number=1,
                    text_characters=0,
                    replacement_characters=0,
                    has_image=False,
                    hidden_text=False,
                )
            ],
            profiles=[],
            has_figures=False,
        )
        self.assertEqual(
            "fail", reconstruct_pdf.status_from_issues(zero_text_issues)
        )
        self.assertEqual(
            "pass", reconstruct_pdf.page_status(zero_text_issues, 1)
        )

        review_issues = reconstruct_pdf.derive_issues(
            [
                reconstruct_pdf.PageReviewFacts(
                    page_id="pdf-0001",
                    page_number=1,
                    text_characters=4,
                    replacement_characters=0,
                    has_image=True,
                    hidden_text=False,
                ),
                reconstruct_pdf.PageReviewFacts(
                    page_id="pdf-0002",
                    page_number=2,
                    text_characters=40,
                    replacement_characters=1,
                    has_image=False,
                    hidden_text=False,
                ),
                reconstruct_pdf.PageReviewFacts(
                    page_id="pdf-0003",
                    page_number=3,
                    text_characters=40,
                    replacement_characters=0,
                    has_image=False,
                    hidden_text=True,
                ),
                reconstruct_pdf.PageReviewFacts(
                    page_id="pdf-0004",
                    page_number=4,
                    text_characters=40,
                    replacement_characters=0,
                    has_image=False,
                    hidden_text=None,
                ),
            ],
            profiles=["music-notation"],
            has_figures=False,
        )
        self.assertEqual(
            {
                "pdf-0001.possible-scan",
                "pdf-0002.replacement-characters",
                "pdf-0003.hidden-or-nonpainting-text",
                "pdf-0004.trace-inspection-failed",
                "source.music-figure-map-missing",
            },
            {issue["id"] for issue in review_issues},
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
