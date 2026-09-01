# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "fonttools==4.60.1",
#   "html5lib==1.1",
#   "jsonschema==4.25.1",
#   "playwright==1.56.0",
#   "pymupdf==1.26.6",
#   "tinycss2==1.4.0",
# ]
# ///

from __future__ import annotations

import asyncio
import copy
import importlib
import json
import os
import sys
import tempfile
import time
import unittest
from pathlib import Path
from typing import TYPE_CHECKING, Any
from unittest import mock

import fitz

if TYPE_CHECKING:
    from collections.abc import Callable

sys.dont_write_bytecode = True

PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
SKILL = PUBLICATION_ROOT / "skills" / "scholarly-print-assembly"
SUPPORT_ROOT = PUBLICATION_ROOT / "tests"
sys.path.insert(0, str(SUPPORT_ROOT))

publication_test_support: Any = importlib.import_module(
    "publication_test_support"
)
MainResult = publication_test_support.MainResult
asset_record = publication_test_support.asset_record
copy_tree = publication_test_support.copy_tree
detect_browser = publication_test_support.detect_browser
import_by_path = publication_test_support.import_by_path
invoke_main = publication_test_support.invoke_main
read_json = publication_test_support.read_json
sha256_bytes = publication_test_support.sha256_bytes
tree_snapshot = publication_test_support.tree_snapshot
visible_text_sha256 = publication_test_support.visible_text_sha256
write_json = publication_test_support.write_json
write_test_font = publication_test_support.write_test_font

assemble_print = import_by_path(
    "scholarly_assemble_print_under_test",
    SKILL / "scripts" / "assemble_print.py",
)

RICH_FRAGMENT = """\
<h1 id="opening">开篇</h1>
<p class="lead">代表性中文段落保留 <span lang="en">voice leading</span>，
并包含 <strong>重点</strong>、<em>强调</em>、<q>引文</q>和
<a href="#terms">内部链接</a>。</p>
<blockquote>A short scholarly quotation.</blockquote>
<ol start="2" type="1"><li>Ordered item</li></ol>
<ul><li>Unordered item</li></ul>
<dl id="terms"><dt>Term</dt><dd>Definition</dd></dl>
<table>
  <caption>Compact table</caption>
  <thead><tr><th scope="col">Column</th></tr></thead>
  <tbody><tr><td>Value</td></tr></tbody>
</table>
<p lang="zh-Hans"><ruby><rb>漢</rb><rt>han</rt></ruby></p>
<!-- figure: sample-figure -->
"""

UNUSED_FRAGMENT = "<p>Unused approved material.</p>\n"

VALID_STYLESHEET = """\
h1, strong, th {
  font-weight: 400;
}
em {
  font-style: normal;
}
p, blockquote {
  color: #111;
  line-height: 1.4;
  margin-top: 1em;
}
ol {
  list-style-type: decimal;
}
ul {
  list-style-type: disc;
}
ruby {
  ruby-position: over;
}
table {
  border-collapse: collapse;
}
"""

FONT_CHARACTERS = (
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    "abcdefghijklmnopqrstuvwxyz"
    "0123456789"
    " .,;:!?-()[]/'\""
    "•“”‘’，。、漢开篇代表性中文段落保留并包含重点强调引文和内部链接合成章节"
)

BROWSER = detect_browser()


def invoke(arguments: list[str]) -> MainResult:
    """Invoke the assembly CLI main and capture its structured output."""
    return invoke_main(assemble_print, arguments, require_report=False)


def structural_pdf(objects: list[bytes]) -> bytes:
    """Create a minimal PDF from explicit indirect objects."""
    chunks = [b"%PDF-1.4\n"]
    offsets: list[int] = []
    for number, content in enumerate(objects, start=1):
        offsets.append(sum(len(chunk) for chunk in chunks))
        chunks.append(f"{number} 0 obj\n".encode() + content + b"\nendobj\n")
    xref_offset = sum(len(chunk) for chunk in chunks)
    chunks.extend(
        [
            f"xref\n0 {len(objects) + 1}\n".encode(),
            b"0000000000 65535 f \n",
            *[f"{offset:010d} 00000 n \n".encode() for offset in offsets],
            (
                f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
                f"startxref\n{xref_offset}\n%%EOF\n"
            ).encode(),
        ]
    )
    return b"".join(chunks)


ZERO_PAGE_PDF = structural_pdf(
    [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [] /Count 0 >>",
    ]
)


def page_svg(page_number: int) -> bytes:
    """Return one safe deterministic page SVG."""
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" '
        'width="360" height="480" viewBox="0 0 360 480">'
        f'<rect id="page-{page_number}" x="20" y="20" width="40" '
        'height="30" fill="#111"/>'
        "</svg>\n"
    ).encode()


def source_blocks(page_number: int) -> dict[str, Any]:
    """Return one structurally current source-block document."""
    page_id = f"pdf-{page_number:04d}"
    return {
        "schema_version": "1.0",
        "coordinate_space": "pdf-points-top-left",
        "page_id": page_id,
        "blocks": [
            {
                "id": f"{page_id}-block-0001",
                "source_order": 1,
                "bbox": [36, 36, 324, 96],
                "text": f"Deterministic source block for page {page_number}.",
            }
        ],
    }


def nested_span_markup(depth: int, text: str = "Nested content.") -> str:
    """Return deterministic allowed markup at an exact element depth."""
    return "<span>" * depth + text + "</span>" * depth


def create_fixture(root: Path) -> None:
    """Author an independent source, bundle, and assembly recipe fixture."""
    source_root = root / "source"
    pages: list[dict[str, Any]] = []
    for number in (1, 2, 3):
        page_id = f"pdf-{number:04d}"
        page_root = source_root / "pages" / page_id
        blocks_path = page_root / "blocks.json"
        svg_path = page_root / "page.svg"
        write_json(blocks_path, source_blocks(number))
        svg_path.write_bytes(page_svg(number))
        pages.append(
            {
                "id": page_id,
                "pdf_page": number,
                "width": 360,
                "height": 480,
                "rotation": 0,
                "assets": {
                    "blocks": asset_record(source_root, blocks_path),
                    "svg": asset_record(source_root, svg_path),
                },
                "status": "pass",
            }
        )

    figure_map_path = source_root / "maps" / "figures.json"
    write_json(
        figure_map_path,
        {
            "schema_version": "1.0",
            "coordinate_space": "pdf-points-top-left",
            "figures": [
                {
                    "id": "sample-figure",
                    "source_label": "Example 1",
                    "profile": "music-notation",
                    "embedded_language_inventory": ["zh-Hans", "en"],
                    "parts": [
                        {
                            "id": "sample-figure-part-1",
                            "order": 1,
                            "pdf_page": 1,
                            "bbox": [54, 220, 306, 340],
                        },
                        {
                            "id": "sample-figure-part-2",
                            "order": 2,
                            "pdf_page": 2,
                            "bbox": [60, 210, 300, 330],
                        },
                    ],
                },
                {
                    "id": "unused-figure",
                    "source_label": "Unused",
                    "profile": None,
                    "embedded_language_inventory": [],
                    "parts": [
                        {
                            "id": "unused-figure-part-1",
                            "order": 1,
                            "pdf_page": 3,
                            "bbox": [50, 200, 310, 350],
                        }
                    ],
                },
            ],
        },
    )
    source_path = source_root / "source-package.json"
    write_json(
        source_path,
        {
            "schema_version": "1.0",
            "source": {
                "sha256": "1" * 64,
                "bytes": 12345,
                "rights_note": "Authorized deterministic test fixture.",
                "page_count": 3,
            },
            "selection": {
                "pdf_pages": [1, 2, 3],
            },
            "coordinate_system": {
                "units": "pdf-points",
                "origin": "top-left",
                "bbox_order": "x0-y0-x1-y1",
            },
            "profiles": ["music-notation"],
            "pages": pages,
            "sections": None,
            "figure_map": asset_record(source_root, figure_map_path),
            "issues": [],
            "status": "pass",
        },
    )

    fragment_path = root / "fragments" / "section-one.html"
    unused_path = root / "fragments" / "unused.html"
    fragment_path.parent.mkdir(parents=True)
    fragment_path.write_text(RICH_FRAGMENT, encoding="utf-8")
    unused_path.write_text(UNUSED_FRAGMENT, encoding="utf-8")
    bundle_path = root / "translation-bundle.json"
    write_json(
        bundle_path,
        {
            "schema_version": "1.0",
            "source_package": asset_record(root, source_path),
            "target_language": "zh-Hans",
            "fragment_text_normalization": (
                "approved-fragment-visible-text-v1"
            ),
            "approval": {
                "status": "approved",
                "reference": "external-review-2026-08-28",
            },
            "fragments": [
                {
                    "id": "section-one",
                    **asset_record(root, fragment_path),
                    "visible_text_sha256": visible_text_sha256(RICH_FRAGMENT),
                    "source_block_ids": [
                        "pdf-0001-block-0001",
                        "pdf-0002-block-0001",
                    ],
                },
                {
                    "id": "unused",
                    **asset_record(root, unused_path),
                    "visible_text_sha256": visible_text_sha256(UNUSED_FRAGMENT),
                    "source_block_ids": ["pdf-0003-block-0001"],
                },
            ],
        },
    )

    font_path = root / "fonts" / "fixture.ttf"
    write_test_font(
        font_path,
        characters=FONT_CHARACTERS,
        family="Fixture Serif",
    )
    stylesheet_path = root / "styles" / "publication.css"
    stylesheet_path.parent.mkdir(parents=True)
    stylesheet_path.write_text(VALID_STYLESHEET, encoding="utf-8")
    write_json(
        root / "assembly-spec.json",
        {
            "schema_version": "1.0",
            "publication_id": "fixture-publication",
            "title": "合成章节",
            "title_language": "zh-Hans",
            "language": "zh-Hans",
            "source_package": "source/source-package.json",
            "translation_bundle": "translation-bundle.json",
            "fragment_order": ["section-one"],
            "figures": [
                {
                    "id": "sample-figure",
                    "caption_html": (
                        '<span class="figure-label">Example 1.</span> '
                        "A continued figure."
                    ),
                    "alt": "Representative figure",
                    "class": "keep-together",
                }
            ],
            "font_roles": {
                "body-cjk": "Fixture Serif",
                "body-latin": "Fixture Serif",
            },
            "fonts": [
                {
                    "family": "Fixture Serif",
                    "path": "fonts/fixture.ttf",
                    "style": "normal",
                    "weight": 400,
                }
            ],
            "stylesheets": ["styles/publication.css"],
            "page": {
                "size": "letter",
                "margin_in": {
                    "top": 0.75,
                    "right": 0.75,
                    "bottom": 0.75,
                    "left": 0.75,
                },
            },
            "profiles": ["music-notation"],
        },
    )


class PublicationProfileContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.profile = read_json(SKILL / "assets" / "publication-profile.json")

    def load_profile(self, profile: dict[str, Any]) -> None:
        with tempfile.TemporaryDirectory(
            prefix="scholarly-assembly-profile-"
        ) as temporary:
            assets = Path(temporary)
            write_json(assets / "publication-profile.json", profile)
            with mock.patch.object(assemble_print, "ASSETS", assets):
                assemble_print.load_profile.cache_clear()
                try:
                    assemble_print.load_profile()
                finally:
                    assemble_print.load_profile.cache_clear()

    def test_loader_accepts_profile_owned_structure(self) -> None:
        narrowed = copy.deepcopy(self.profile)
        del narrowed["fragment_html"]["elements"]["abbr"]
        narrowed["untrusted_stylesheet"]["properties"].remove("color")
        expanded = copy.deepcopy(self.profile)
        expanded["fragment_html"]["elements"]["article"] = []

        self.load_profile(self.profile)
        self.load_profile(narrowed)
        self.load_profile(expanded)

    def test_loader_rejects_fields_without_value_policies(self) -> None:
        global_attribute = copy.deepcopy(self.profile)
        global_attribute["fragment_html"]["global_attributes"].append(
            "data-extra"
        )
        css_property = copy.deepcopy(self.profile)
        css_property["untrusted_stylesheet"]["properties"].append("display")
        for name, profile in (
            ("global-attribute", global_attribute),
            ("css-property", css_property),
        ):
            with (
                self.subTest(case=name),
                self.assertRaises(assemble_print.ContractError),  # noqa: PT027
            ):
                self.load_profile(profile)

    def test_assembly_loader_rejects_malformed_profile_shape(self) -> None:
        malformed = copy.deepcopy(self.profile)
        malformed["fragment_html"]["global_attributes"] = "id"

        with self.assertRaises(assemble_print.ContractError):  # noqa: PT027
            self.load_profile(malformed)


class AssemblePrintScenarioTests(unittest.TestCase):
    suite_temporary: tempfile.TemporaryDirectory[str]
    suite_root: Path
    baseline_input: Path
    baseline_output: Path
    browser_used: str | None = None
    browser_duration_seconds: float | None = None

    @classmethod
    def setUpClass(cls) -> None:
        cls.suite_temporary = tempfile.TemporaryDirectory(
            prefix="scholarly-assembly-suite-"
        )
        cls.suite_root = Path(cls.suite_temporary.name)
        cls.baseline_input = cls.suite_root / "baseline-input"
        create_fixture(cls.baseline_input)
        cls.baseline_output = cls.suite_root / "baseline-publication"
        result = invoke(
            [
                "build",
                "--spec",
                str(cls.baseline_input / "assembly-spec.json"),
                "--output",
                str(cls.baseline_output),
            ]
        )
        if result.exit_code != 0:
            message = f"cannot create assembly baseline: {result.stderr}"
            raise AssertionError(message)

    @classmethod
    def tearDownClass(cls) -> None:
        cls.suite_temporary.cleanup()

    def setUp(self) -> None:
        self.case_root = self.suite_root / self._testMethodName
        self.case_root.mkdir()

    def fresh_workspace(self, name: str = "workspace") -> Path:
        workspace = self.case_root / name
        copy_tree(self.baseline_input, workspace)
        return workspace

    def fresh_publication(self, name: str = "publication") -> Path:
        publication = self.case_root / name
        copy_tree(self.baseline_output, publication)
        return publication

    @staticmethod
    def build(
        workspace: Path,
        *,
        output: Path | None = None,
    ) -> MainResult:
        arguments = [
            "build",
            "--spec",
            str(workspace / "assembly-spec.json"),
            "--output",
            str(output or workspace / "publication"),
        ]
        return invoke(arguments)

    @staticmethod
    def validate(publication: Path) -> MainResult:
        return invoke(
            [
                "validate",
                "--manifest",
                str(publication / "assembly-manifest.json"),
            ]
        )

    @staticmethod
    def render(
        publication: Path,
        browser: Path,
        *,
        pdf: Path | None = None,
    ) -> MainResult:
        arguments = [
            "render",
            "--html",
            str(publication / "index.html"),
            "--pdf",
            str(pdf or publication / "draft.pdf"),
            "--browser",
            str(browser),
        ]
        return invoke(arguments)

    @staticmethod
    def update_json(
        path: Path,
        mutation: Callable[[dict[str, Any]], None],
    ) -> None:
        value = read_json(path)
        mutation(value)
        write_json(path, value)

    @staticmethod
    def refresh_bundle_source(workspace: Path) -> None:
        source_path = workspace / "source" / "source-package.json"
        bundle_path = workspace / "translation-bundle.json"
        bundle = read_json(bundle_path)
        bundle["source_package"] = asset_record(workspace, source_path)
        write_json(bundle_path, bundle)

    @staticmethod
    def refresh_source_asset(
        workspace: Path,
        page_index: int,
        role: str,
    ) -> None:
        source_root = workspace / "source"
        source_path = source_root / "source-package.json"
        source = read_json(source_path)
        path = source_root / source["pages"][page_index]["assets"][role]["path"]
        source["pages"][page_index]["assets"][role] = asset_record(
            source_root,
            path,
        )
        write_json(source_path, source)

    @classmethod
    def refresh_figure_map(cls, workspace: Path) -> None:
        source_root = workspace / "source"
        source_path = source_root / "source-package.json"
        figure_path = source_root / "maps" / "figures.json"
        source = read_json(source_path)
        source["figure_map"] = asset_record(source_root, figure_path)
        write_json(source_path, source)
        cls.refresh_bundle_source(workspace)

    @staticmethod
    def refresh_fragment(workspace: Path, content: str) -> None:
        fragment_path = workspace / "fragments" / "section-one.html"
        fragment_path.write_text(content, encoding="utf-8")
        bundle_path = workspace / "translation-bundle.json"
        bundle = read_json(bundle_path)
        record = next(
            item for item in bundle["fragments"] if item["id"] == "section-one"
        )
        record.update(asset_record(workspace, fragment_path))
        record["visible_text_sha256"] = visible_text_sha256(content)
        write_json(bundle_path, bundle)

    @staticmethod
    def refresh_output_asset(publication: Path, key: str) -> None:
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        record = manifest["outputs"][key]
        if not isinstance(record, dict):
            message = f"manifest output {key!r} is not an asset record"
            raise TypeError(message)
        output = publication.joinpath(*Path(record["path"]).parts)
        manifest["outputs"][key] = asset_record(publication, output)
        write_json(manifest_path, manifest)

    @staticmethod
    def write_valid_pdf(
        _html: Path,
        output: Path,
        _browser: Path,
        _allowed: set[Path],
    ) -> None:
        document = fitz.open()
        document.new_page(width=612, height=792)
        document.save(output)
        document.close()

    def assert_build_rejected(self, workspace: Path) -> None:
        result = self.build(workspace)
        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertFalse((workspace / "publication").exists())

    def test_externally_authored_approved_bundle_builds_and_validates(
        self,
    ) -> None:
        workspace = self.fresh_workspace()

        built = self.build(workspace)
        self.assertEqual(0, built.exit_code, built)
        self.assertEqual("assembled", built.report["status"])
        self.assertEqual(1, built.report["fragments"])
        self.assertEqual(1, built.report["figures"])
        self.assertEqual(2, built.report["crops"])

        publication = workspace / "publication"
        validated = self.validate(publication)
        self.assertEqual(0, validated.exit_code, validated)
        self.assertEqual("valid", validated.report["status"])
        self.assertIsNone(validated.report["pdf"])

        html = (publication / "index.html").read_text(encoding="utf-8")
        for token in (
            "<h1",
            "<blockquote",
            "<ol",
            "<ul",
            "<dl",
            "<table",
            "<ruby",
            'href="#terms"',
            '<span lang="en">voice leading</span>',
            'data-figure-id="sample-figure"',
        ):
            with self.subTest(token=token):
                self.assertIn(token, html)

        manifest = read_json(publication / "assembly-manifest.json")
        self.assertEqual(
            {
                "title": "合成章节",
                "title_language": "zh-Hans",
                "language": "zh-Hans",
            },
            manifest["document"],
        )
        self.assertEqual(["music-notation"], manifest["profiles"])
        figure = manifest["figures"][0]
        self.assertEqual("sample-figure", figure["id"])
        self.assertEqual(
            {
                "source_label": "Example 1",
                "profile": "music-notation",
                "embedded_language_inventory": ["zh-Hans", "en"],
            },
            {
                key: figure[key]
                for key in (
                    "source_label",
                    "profile",
                    "embedded_language_inventory",
                )
            },
        )
        self.assertEqual(
            [
                (
                    1,
                    1,
                    [54, 220, 306, 340],
                    asset_record(
                        publication,
                        publication / "assets/pages/pdf-0001.svg",
                    ),
                ),
                (
                    2,
                    2,
                    [60, 210, 300, 330],
                    asset_record(
                        publication,
                        publication / "assets/pages/pdf-0002.svg",
                    ),
                ),
            ],
            [
                (
                    part["order"],
                    part["pdf_page"],
                    part["bbox"],
                    part["source_svg"],
                )
                for part in figure["parts"]
            ],
        )
        self.assertLess(
            html.index('data-crop-id="sample-figure-part-1"'),
            html.index('data-crop-id="sample-figure-part-2"'),
        )
        expected = [
            "assets/fonts/font-001.ttf",
            "assets/pages/pdf-0001.svg",
            "assets/pages/pdf-0002.svg",
            "assets/print.css",
            "assets/stylesheets/stylesheet-001.css",
            "fragments/section-one.html",
            "index.html",
            "inputs/assembly-spec.json",
            "inputs/source-package.json",
            "inputs/translation-bundle.json",
        ]
        self.assertEqual(
            expected,
            sorted(
                path.relative_to(publication).as_posix()
                for path in publication.rglob("*")
                if path.is_file() and path.name != "assembly-manifest.json"
            ),
        )
        self.assertFalse((publication / "assets/pages/pdf-0003.svg").exists())
        self.assertFalse((publication / "fragments/unused.html").exists())
        self.assertFalse((publication / "maps").exists())

    def test_validate_uses_manifest_figure_profile_without_recipe_replay(
        self,
    ) -> None:
        workspace = self.fresh_workspace()
        built = self.build(workspace)
        self.assertEqual(0, built.exit_code, built)
        publication = workspace / "publication"
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["figures"][0]["profile"] = "notation-review"
        manifest["profiles"].append("notation-review")
        write_json(manifest_path, manifest)

        validated = self.validate(publication)

        self.assertEqual(0, validated.exit_code, validated)

    def test_validate_treats_retained_lineage_assets_as_opaque(
        self,
    ) -> None:
        publication = self.fresh_publication()
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        payloads = {
            "source_package": b"{not source JSON",
            "translation_bundle": b"\xffopaque bundle snapshot",
            "assembly_spec": b"not a recipe",
        }
        for role, payload in payloads.items():
            record = manifest["inputs"][role]
            path = publication.joinpath(
                *Path(record["path"]).parts,
            )
            path.write_bytes(payload)
            record.update(asset_record(publication, path))
        retained = (
            (manifest["fragments"][0]["asset"], b"opaque approved fragment"),
            (manifest["stylesheets"][0], b"opaque retained stylesheet"),
        )
        for record, payload in retained:
            path = publication.joinpath(*Path(record["path"]).parts)
            path.write_bytes(payload)
            record.update(asset_record(publication, path))
        source_svg = manifest["figures"][0]["parts"][0]["source_svg"]
        svg_path = publication.joinpath(*Path(source_svg["path"]).parts)
        svg_path.write_bytes(
            svg_path.read_bytes().replace(
                b"</svg>",
                b"<!-- retained lineage note --></svg>",
                1,
            )
        )
        source_svg.update(asset_record(publication, svg_path))
        manifest["generator"]["runtime"] = "python-3.12.10"
        write_json(manifest_path, manifest)

        validated = self.validate(publication)

        self.assertEqual(0, validated.exit_code, validated)

    def test_markup_nesting_limit_accepts_fragment_and_caption_boundary(
        self,
    ) -> None:
        profile, _content = assemble_print.load_profile()
        content = nested_span_markup(assemble_print.MARKUP_NESTING_LIMIT)
        cases = (
            ("approved fragment section-one", True),
            ("figure sample-figure caption", False),
        )
        for context, allow_figure_markers in cases:
            with self.subTest(context=context):
                markup = assemble_print.parse_markup(
                    content,
                    profile,
                    context,
                    allow_figure_markers=allow_figure_markers,
                )
                self.assertEqual(
                    content,
                    assemble_print.serialize_markup(markup),
                )

    def test_build_rejects_fragment_and_caption_beyond_markup_nesting_limit(
        self,
    ) -> None:
        content = nested_span_markup(assemble_print.MARKUP_NESTING_LIMIT + 1)
        for kind, context in (
            ("fragment", "approved fragment section-one"),
            ("caption", "figure sample-figure caption"),
        ):
            with self.subTest(kind=kind):
                workspace = self.fresh_workspace(kind)
                if kind == "fragment":
                    self.refresh_fragment(
                        workspace,
                        f"{content}\n<!-- figure: sample-figure -->\n",
                    )
                else:
                    self.update_json(
                        workspace / "assembly-spec.json",
                        lambda spec: spec["figures"][0].update(
                            {"caption_html": content}
                        ),
                    )

                result = self.build(workspace)

                self.assertEqual(2, result.exit_code, result)
                self.assertEqual({}, result.report)
                self.assertEqual("", result.stdout)
                self.assertIn(context, result.stderr)
                self.assertIn(
                    "exceeds the markup nesting limit of "
                    f"{assemble_print.MARKUP_NESTING_LIMIT}",
                    result.stderr,
                )
                self.assertNotIn("Traceback", result.stderr)
                self.assertFalse((workspace / "publication").exists())

    def test_cli_rejects_oversized_fragment_start_without_creating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("oversized-fragment-start")
        oversized = "9" * 5000
        self.refresh_fragment(
            workspace,
            RICH_FRAGMENT.replace(
                'start="2"',
                f'start="{oversized}"',
                1,
            ),
        )
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.build(workspace)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertEqual(
            "error: approved fragment section-one <ol> start is outside "
            "the allowed range\n",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertFalse((workspace / "publication").exists())

    def test_cli_rejects_oversized_caption_colspan_without_mutating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("oversized-caption-colspan")
        oversized = "9" * 5000
        self.update_json(
            workspace / "assembly-spec.json",
            lambda spec: spec["figures"][0].update(
                {
                    "caption_html": (
                        "<table><tbody><tr>"
                        f'<td colspan="{oversized}">Caption</td>'
                        "</tr></tbody></table>"
                    )
                }
            ),
        )
        output = self.fresh_publication("oversized-caption-output")
        before = tree_snapshot(output)
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.build(workspace, output=output)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertEqual(
            "error: figure sample-figure caption <td> colspan must be an "
            "integer from 1 through 100\n",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(before, tree_snapshot(output))

    def test_cli_rejects_oversized_caption_character_reference_without_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace(
            "oversized-caption-character-reference"
        )
        oversized = "9" * 5000
        self.update_json(
            workspace / "assembly-spec.json",
            lambda spec: spec["figures"][0].update(
                {"caption_html": f"&#{oversized};"}
            ),
        )
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.build(workspace)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertEqual(
            "error: figure sample-figure caption is not valid HTML: "
            "Exceeds the limit (640 digits) for integer string conversion: "
            "value has 5000 digits; use sys.set_int_max_str_digits() to "
            "increase the limit\n",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertFalse((workspace / "publication").exists())

    def test_cli_rejects_oversized_stylesheet_number_without_mutating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("oversized-stylesheet-number")
        oversized = "9" * 5000
        (workspace / "styles" / "publication.css").write_text(
            f"p {{ margin-top: {oversized}px; }}\n",
            encoding="utf-8",
        )
        output = self.fresh_publication("oversized-stylesheet-output")
        before = tree_snapshot(output)
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.build(workspace, output=output)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertEqual(
            "error: stylesheet 1 is not valid CSS: Exceeds the limit "
            "(640 digits) for integer string conversion: value has 5000 "
            "digits; use sys.set_int_max_str_digits() to increase the limit\n",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(before, tree_snapshot(output))

    def test_standalone_validate_translates_generated_html_errors(self) -> None:
        publication = self.fresh_publication("numeric-reference")
        html_path = publication / "index.html"
        content = html_path.read_text(encoding="utf-8")
        marker = "</body>"
        self.assertIn(marker, content)
        html_path.write_text(
            content.replace(marker, f"&#{'9' * 5000};{marker}", 1),
            encoding="utf-8",
        )
        self.refresh_output_asset(publication, "html")

        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.validate(publication)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertIn(
            "error: generated HTML is not valid HTML: Exceeds the limit",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)

    def test_standalone_validate_translates_generated_css_parser_errors(
        self,
    ) -> None:
        cases = (
            (
                "numeric-value",
                f"\n:root {{ --oversized: {'9' * 5000}; }}\n",
                640,
                "Exceeds the limit",
            ),
            (
                "nested-functions",
                "\na { x: " + "f(" * 2000 + "x" + ")" * 2000 + "; }\n",
                None,
                "resource nesting limit",
            ),
        )
        for name, addition, integer_limit, message in cases:
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                css_path = publication / "assets" / "print.css"
                with css_path.open("a", encoding="utf-8") as stream:
                    stream.write(addition)
                self.refresh_output_asset(publication, "css")

                previous_limit = sys.get_int_max_str_digits()
                try:
                    if integer_limit is not None:
                        sys.set_int_max_str_digits(integer_limit)
                    result = self.validate(publication)
                finally:
                    sys.set_int_max_str_digits(previous_limit)

                self.assertEqual(2, result.exit_code, result)
                self.assertEqual({}, result.report)
                self.assertEqual("", result.stdout)
                self.assertIn(message, result.stderr)
                self.assertNotIn("Traceback", result.stderr)

    def test_standalone_validate_translates_retained_svg_parser_errors(
        self,
    ) -> None:
        publication = self.fresh_publication("retained-svg-parser-error")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        source_svg = manifest["figures"][0]["parts"][0]["source_svg"]
        svg_path = publication.joinpath(*Path(source_svg["path"]).parts)
        svg_path.write_bytes(
            b'<?xml version="1.0" encoding="bogus"?>' + svg_path.read_bytes()
        )
        source_svg.update(asset_record(publication, svg_path))
        write_json(manifest_path, manifest)

        rejected = self.validate(publication)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn(
            "is not safe XML: unknown encoding: bogus",
            rejected.stderr,
        )
        self.assertNotIn("Traceback", rejected.stderr)

    def test_cli_rejects_oversized_json_integer_without_mutating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("oversized-json")
        output = self.fresh_publication("oversized-json-output")
        before = tree_snapshot(output)
        spec_path = workspace / "assembly-spec.json"
        spec_text = spec_path.read_text(encoding="utf-8")
        old_value = '"top": 0.75'
        self.assertIn(old_value, spec_text)
        spec_path.write_text(
            spec_text.replace(
                old_value,
                f'"top": {"9" * 641}',
                1,
            ),
            encoding="utf-8",
        )
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            result = self.build(workspace, output=output)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertIn(
            "assembly specification contains an invalid JSON number",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(before, tree_snapshot(output))

    def test_cli_rejects_deeply_nested_invalid_spec_without_mutating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("deeply-nested-json")
        spec_path = workspace / "assembly-spec.json"
        depth = 5_000
        spec_path.write_text(
            '{"nested":' + "[" * depth + "null" + "]" * depth + "}\n",
            encoding="utf-8",
        )
        output = self.fresh_publication("deeply-nested-json-output")
        before = tree_snapshot(output)

        result = self.build(workspace, output=output)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertRegex(
            result.stderr,
            (
                r"^error: assembly specification "
                r"(?:is not valid JSON:|violates assembly-spec\.schema\.json:)"
            ),
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(before, tree_snapshot(output))

    def test_cli_rejects_unrepresentable_integer_without_mutating_output(
        self,
    ) -> None:
        workspace = self.fresh_workspace("unrepresentable-integer")
        source_path = workspace / "source" / "source-package.json"
        source = read_json(source_path)
        source["pages"][0]["width"] = int("9" * 400)
        write_json(source_path, source)
        self.refresh_bundle_source(workspace)
        output = self.fresh_publication("unrepresentable-integer-output")
        before = tree_snapshot(output)

        result = self.build(workspace, output=output)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertIn(
            "page width must be representable as a finite number",
            result.stderr,
        )
        self.assertNotIn("Traceback", result.stderr)
        self.assertEqual(before, tree_snapshot(output))

    def test_profile_rejects_representative_fragment_markup(self) -> None:
        cases = (
            ("undeclared-element", "<video>media</video>"),
            ("undeclared-attribute", '<p data-extra="x">text</p>'),
            ("assembler-owned-class", '<p class="figure-part">text</p>'),
        )
        for name, markup in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                self.refresh_fragment(
                    workspace,
                    f"{markup}\n<!-- figure: sample-figure -->\n",
                )
                self.assert_build_rejected(workspace)

    def test_profile_rejects_representative_stylesheet_constructs(self) -> None:
        cases = (
            ("property", "p { display: none; }"),
            ("value", "p { color: red; }"),
        )
        for name, stylesheet in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                (workspace / "styles" / "publication.css").write_text(
                    stylesheet,
                    encoding="utf-8",
                )
                self.assert_build_rejected(workspace)

    def test_source_hash_status_and_block_mismatches(
        self,
    ) -> None:
        def refresh_blocks(workspace: Path) -> None:
            self.refresh_source_asset(workspace, 0, "blocks")
            self.refresh_bundle_source(workspace)

        def source_hash(workspace: Path) -> None:
            self.update_json(
                workspace / "translation-bundle.json",
                lambda bundle: bundle["source_package"].update(
                    {"sha256": "0" * 64}
                ),
            )

        def source_status(workspace: Path) -> None:
            self.update_json(
                workspace / "source" / "source-package.json",
                lambda source: source.update({"status": "review_required"}),
            )
            self.refresh_bundle_source(workspace)

        def block_page(workspace: Path) -> None:
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0001" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks.update({"page_id": "pdf-9999"}),
            )
            refresh_blocks(workspace)

        def block_id(workspace: Path) -> None:
            identifier = "pdf-0001-block-9999"
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0001" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks["blocks"][0].update({"id": identifier}),
            )
            self.update_json(
                workspace / "translation-bundle.json",
                lambda bundle: bundle["fragments"][0][
                    "source_block_ids"
                ].__setitem__(0, identifier),
            )
            refresh_blocks(workspace)

        def block_order(workspace: Path) -> None:
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0001" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks["blocks"][0].update({"source_order": 2}),
            )
            refresh_blocks(workspace)

        def block_text(workspace: Path) -> None:
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0001" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks["blocks"][0].update({"text": " "}),
            )
            refresh_blocks(workspace)

        def block_bbox(workspace: Path) -> None:
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0001" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks["blocks"][0].update(
                    {"bbox": [0, 0, 361, 20]}
                ),
            )
            refresh_blocks(workspace)

        cases = (
            ("hash", source_hash),
            ("status", source_status),
            ("block-page", block_page),
            ("block-id", block_id),
            ("block-order", block_order),
            ("block-text", block_text),
            ("block-bbox", block_bbox),
        )
        for name, mutation in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                mutation(workspace)
                self.assert_build_rejected(workspace)

    def test_consumed_block_validation_respects_stage_boundaries(
        self,
    ) -> None:
        def unconsumed_blocks(workspace: Path) -> None:
            blocks_path = (
                workspace / "source" / "pages" / "pdf-0003" / "blocks.json"
            )
            self.update_json(
                blocks_path,
                lambda blocks: blocks["blocks"][0].update({"source_order": 2}),
            )
            self.refresh_source_asset(workspace, 2, "blocks")
            self.refresh_bundle_source(workspace)

        cases = (("unconsumed-blocks", unconsumed_blocks),)
        for name, mutation in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                mutation(workspace)

                result = self.build(workspace)

                self.assertEqual(0, result.exit_code, result)

    def test_figure_mismatch_rejections(self) -> None:
        def absent_figure(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            self.update_json(
                figure_path,
                lambda value: value["figures"][0].update(
                    {"id": "different-figure"}
                ),
            )
            self.refresh_figure_map(workspace)

        def part_order(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            self.update_json(
                figure_path,
                lambda value: value["figures"][0]["parts"][0].update(
                    {"order": 2}
                ),
            )
            self.refresh_figure_map(workspace)

        def bbox(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            self.update_json(
                figure_path,
                lambda value: value["figures"][0]["parts"][0].update(
                    {"bbox": [10, 10, 10, 20]}
                ),
            )
            self.refresh_figure_map(workspace)

        def profile_identifier(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            self.update_json(
                figure_path,
                lambda value: value["figures"][0].update(
                    {"profile": "Music Notation"}
                ),
            )
            self.refresh_figure_map(workspace)

        def missing_source_facts(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            figure_map = read_json(figure_path)
            figure_map["figures"][0].pop("embedded_language_inventory")
            write_json(figure_path, figure_map)
            self.refresh_figure_map(workspace)

        def undeclared_source_profile(workspace: Path) -> None:
            figure_path = workspace / "source" / "maps" / "figures.json"
            self.update_json(
                figure_path,
                lambda value: value["figures"][0].update(
                    {"profile": "notation-review"}
                ),
            )
            self.refresh_figure_map(workspace)
            self.update_json(
                workspace / "assembly-spec.json",
                lambda value: value["profiles"].append("notation-review"),
            )

        def caption_class(workspace: Path) -> None:
            self.update_json(
                workspace / "assembly-spec.json",
                lambda value: value["figures"][0].update(
                    {
                        "caption_html": (
                            '<span class="figure-parts">Caption</span>'
                        )
                    }
                ),
            )

        cases = (
            ("figure", absent_figure),
            ("part-order", part_order),
            ("bbox", bbox),
            ("profile-identifier", profile_identifier),
            ("missing-source-facts", missing_source_facts),
            ("undeclared-source-profile", undeclared_source_profile),
            ("caption-class", caption_class),
        )
        for name, mutation in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                mutation(workspace)
                self.assert_build_rejected(workspace)

    def test_figure_geometry_is_canonical_and_page_svg_must_align(
        self,
    ) -> None:
        workspace = self.fresh_workspace("fractional-bbox")
        figure_path = workspace / "source" / "maps" / "figures.json"
        self.update_json(
            figure_path,
            lambda value: value["figures"][0]["parts"][0].update(
                {
                    "bbox": [
                        10.0004,
                        20.0004,
                        110.0006,
                        120.0006,
                    ]
                }
            ),
        )
        self.refresh_figure_map(workspace)

        built = self.build(workspace)

        self.assertEqual(0, built.exit_code, built)
        publication = workspace / "publication"
        manifest = read_json(publication / "assembly-manifest.json")
        self.assertEqual(
            [10.0, 20.0, 110.001, 120.001],
            manifest["figures"][0]["parts"][0]["bbox"],
        )
        html = (publication / "index.html").read_text(encoding="utf-8")
        self.assertIn(
            'viewBox="10.000 20.000 100.001 100.001"',
            html,
        )
        validated = self.validate(publication)
        self.assertEqual(0, validated.exit_code, validated)

        part = manifest["figures"][0]["parts"][0]
        retained_svg = publication.joinpath(
            *Path(part["source_svg"]["path"]).parts
        )
        retained_svg.write_bytes(
            retained_svg.read_bytes().replace(
                b'width="360" height="480" viewBox="0 0 360 480"',
                b'width="400" height="500" viewBox="0 0 400 500"',
                1,
            )
        )
        part["source_svg"] = asset_record(publication, retained_svg)
        html_path = publication / "index.html"
        html_path.write_text(
            html_path.read_text(encoding="utf-8").replace(
                'width="360.000" height="480.000"></image>',
                'width="400.000" height="500.000"></image>',
                1,
            ),
            encoding="utf-8",
        )
        manifest["outputs"]["html"] = asset_record(publication, html_path)
        write_json(publication / "assembly-manifest.json", manifest)
        retained_dimensions = self.validate(publication)
        self.assertEqual(0, retained_dimensions.exit_code, retained_dimensions)

        manifest["figures"][0]["parts"][0]["bbox"] = [
            10.0001,
            20.0001,
            110.0011,
            120.0011,
        ]
        write_json(publication / "assembly-manifest.json", manifest)
        noncanonical = self.validate(publication)
        self.assertEqual(2, noncanonical.exit_code, noncanonical)
        self.assertIn("bbox is not canonical", noncanonical.stderr)

        mismatch = self.fresh_workspace("page-svg-mismatch")
        svg_path = mismatch / "source" / "pages" / "pdf-0001" / "page.svg"
        svg_path.write_bytes(
            b'<svg xmlns="http://www.w3.org/2000/svg" '
            b'width="360.004" height="479.996" '
            b'viewBox="0 0 360.004 479.996">'
            b'<rect id="page-1" x="20" y="20" width="40" '
            b'height="30" fill="#111"/>'
            b"</svg>\n"
        )
        self.refresh_source_asset(mismatch, 0, "svg")
        self.refresh_bundle_source(mismatch)

        rejected = self.build(mismatch)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn(
            "dimensions do not match the source package page",
            rejected.stderr,
        )
        self.assertFalse((mismatch / "publication").exists())

    def test_local_font_acceptance_and_rejection_scenarios(self) -> None:
        accepted = self.fresh_workspace("accepted")
        result = self.build(accepted)
        self.assertEqual(0, result.exit_code, result)
        manifest = read_json(
            accepted / "publication" / "assembly-manifest.json"
        )
        self.assertEqual("Fixture Serif", manifest["fonts"][0]["family"])
        self.assertEqual(
            "FixtureSerif-Regular",
            manifest["fonts"][0]["postscript_name"],
        )

        limited = self.fresh_workspace("limited-coverage")
        write_test_font(
            limited / "fonts" / "fixture.ttf",
            characters=" A",
            family="Fixture Serif",
        )
        limited_result = self.build(limited)
        self.assertEqual(0, limited_result.exit_code, limited_result)
        self.assertEqual(
            0,
            self.validate(limited / "publication").exit_code,
        )

        invalid = self.fresh_workspace("invalid")
        (invalid / "fonts" / "fixture.ttf").write_bytes(b"not a font")
        self.assert_build_rejected(invalid)

    def test_font_names_reject_css_unsafe_characters(self) -> None:
        cases = (
            ("control", "Fixture\u0001Serif"),
            ("format", "Fixture\u200bSerif"),
        )
        for name, family in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                spec_path = workspace / "assembly-spec.json"
                spec = read_json(spec_path)
                spec["fonts"][0]["family"] = family
                spec["font_roles"] = {
                    "body-cjk": family,
                    "body-latin": family,
                }
                write_json(spec_path, spec)

                rejected = self.build(workspace)

                self.assertEqual(2, rejected.exit_code, rejected)
                self.assertIn("CSS-unsafe", rejected.stderr)
                self.assertFalse((workspace / "publication").exists())

    def test_validate_rejects_generated_css_external_resource(self) -> None:
        resources = {
            "url": 'url("https://example.test/url.png")',
            "image-set": 'image-set("https://example.test/standard.png" 1x)',
            "webkit-image-set": (
                '-webkit-image-set("https://example.test/webkit.png" 2x)'
            ),
        }
        for name, resource in resources.items():
            with self.subTest(case=name):
                publication = self.fresh_publication(
                    f"external-css-resource-{name}"
                )
                css_path = publication / "assets" / "print.css"
                with css_path.open("a", encoding="utf-8") as stream:
                    stream.write(
                        f"\nbody {{ background-image: {resource}; }}\n"
                    )
                self.refresh_output_asset(publication, "css")

                rejected = self.validate(publication)

                self.assertEqual(2, rejected.exit_code, rejected)
                self.assertIn("nonlocal resource URL", rejected.stderr)

    def test_validate_binds_crop_geometry_semantically(self) -> None:
        mutations = {
            "viewbox": lambda html: html.replace('viewBox="', 'viewBox="1 ', 1),
            "viewbox-separators": lambda html: html.replace(
                'viewBox="54.000 220.000 252.000 120.000"',
                'viewBox="54.000,,220.000,,252.000,,120.000"',
                1,
            ),
            "aspect-ratio": lambda html: html.replace(
                'preserveAspectRatio="xMidYMid meet"',
                'preserveAspectRatio="none"',
                1,
            ),
            "image-offset": lambda html: html.replace(
                'x="0" y="0"',
                'x="1" y="0"',
                1,
            ),
            "crop-size": lambda html: html.replace(
                ' width="',
                ' width="1',
                1,
            ),
            "image-size": lambda html: html.replace(
                'x="0" y="0" width="',
                'x="0" y="0" width="1',
                1,
            ),
        }
        for name, mutate in mutations.items():
            with self.subTest(case=name):
                publication = self.fresh_publication(f"geometry-{name}")
                html_path = publication / "index.html"
                html_path.write_text(
                    mutate(html_path.read_text(encoding="utf-8")),
                    encoding="utf-8",
                )
                self.refresh_output_asset(publication, "html")
                self.assertEqual(2, self.validate(publication).exit_code)

        equivalent = self.fresh_publication("geometry-equivalent")
        html_path = equivalent / "index.html"
        html = html_path.read_text(encoding="utf-8")
        value_start = html.index('viewBox="') + len('viewBox="')
        value_end = html.index('"', value_start)
        equivalent_viewbox = ", ".join(
            f"{float(value):.4f}"
            for value in html[value_start:value_end].split()
        )
        html = html.replace(' preserveAspectRatio="xMidYMid meet"', "", 1)
        html = html.replace(' x="0" y="0"', "", 1)
        html_path.write_text(
            f"{html[:value_start]}{equivalent_viewbox}{html[value_end:]}",
            encoding="utf-8",
        )
        self.refresh_output_asset(equivalent, "html")
        self.assertEqual(0, self.validate(equivalent).exit_code)

        for name, viewbox_width, expected_exit in (
            ("minimum-equivalent", "1e-3", 0),
            ("minimum-collapsed", "0", 2),
        ):
            with self.subTest(case=name):
                publication = self.fresh_publication(name)
                manifest_path = publication / "assembly-manifest.json"
                manifest = read_json(manifest_path)
                manifest["figures"][0]["parts"][0]["bbox"] = [
                    54.0,
                    220.0,
                    54.001,
                    340.0,
                ]
                write_json(manifest_path, manifest)
                html_path = publication / "index.html"
                original_geometry = (
                    'viewBox="54.000 220.000 252.000 120.000" width="252.000"'
                )
                html = html_path.read_text(encoding="utf-8")
                self.assertIn(original_geometry, html)
                html_path.write_text(
                    html.replace(
                        original_geometry,
                        f'viewBox="54 220 {viewbox_width} 120" width=".001"',
                        1,
                    ),
                    encoding="utf-8",
                )
                self.refresh_output_asset(publication, "html")
                self.assertEqual(
                    expected_exit,
                    self.validate(publication).exit_code,
                )

        publication = self.fresh_publication("retained-viewbox-separators")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        source_svg = manifest["figures"][0]["parts"][0]["source_svg"]
        svg_path = publication.joinpath(*Path(source_svg["path"]).parts)
        svg_path.write_bytes(
            svg_path.read_bytes().replace(
                b'viewBox="0 0 360 480"',
                b'viewBox="0,,0,,360,,480"',
                1,
            )
        )
        source_svg.update(asset_record(publication, svg_path))
        write_json(manifest_path, manifest)
        self.assertEqual(2, self.validate(publication).exit_code)

    def test_validate_binds_authored_text_and_caption_semantics(self) -> None:
        cases = (
            (
                "fragment",
                "A short scholarly quotation.",
                "A changed scholarly quotation.",
                "fragment section section-one authored text",
            ),
            (
                "caption",
                "A continued figure.",
                "A changed figure caption.",
                "figure sample-figure caption text",
            ),
        )
        for name, original, replacement, expected_error in cases:
            with self.subTest(case=name):
                publication = self.fresh_publication(f"text-binding-{name}")
                html_path = publication / "index.html"
                html = html_path.read_text(encoding="utf-8")
                self.assertIn(original, html)
                html_path.write_text(
                    html.replace(original, replacement, 1),
                    encoding="utf-8",
                )
                self.refresh_output_asset(publication, "html")

                rejected = self.validate(publication)

                self.assertEqual(2, rejected.exit_code, rejected)
                self.assertIn(expected_error, rejected.stderr)

        publication = self.fresh_publication("caption-parser-error")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        oversized = "9" * 5000
        caption = f"&#{oversized};"
        manifest["figures"][0]["caption_html"] = caption
        manifest["figures"][0]["caption_sha256"] = sha256_bytes(
            caption.encode()
        )
        write_json(manifest_path, manifest)
        previous_limit = sys.get_int_max_str_digits()
        try:
            sys.set_int_max_str_digits(640)
            rejected = self.validate(publication)
        finally:
            sys.set_int_max_str_digits(previous_limit)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn(
            "manifest figure sample-figure caption is not valid HTML",
            rejected.stderr,
        )
        self.assertNotIn("Traceback", rejected.stderr)

    def test_validate_closes_generated_topology_and_resource_attributes(
        self,
    ) -> None:
        insertions = {
            "unbound-figure": "<figure></figure>",
            "orphan-caption": "<figcaption>orphan</figcaption>",
            "unbound-svg": "<svg></svg>",
            "active-element": "<script>void 0</script>",
            "inline-stylesheet": "<style>body { color: red; }</style>",
            "metadata-refresh": (
                '<meta http-equiv="refresh" '
                'content="0;url=https://example.test/refresh">'
            ),
            "preload-resource": (
                '<link rel="preload" as="image" '
                'imagesrcset="https://example.test/preload.png">'
            ),
            "secondary-url": (
                '<a href="#opening" ping="https://example.test/audit">ping</a>'
            ),
        }
        for name, insertion in insertions.items():
            with self.subTest(case=name):
                publication = self.fresh_publication(f"topology-{name}")
                html_path = publication / "index.html"
                html = html_path.read_text(encoding="utf-8")
                html_path.write_text(
                    html.replace("</section>", f"{insertion}</section>", 1),
                    encoding="utf-8",
                )
                self.refresh_output_asset(publication, "html")
                self.assertEqual(2, self.validate(publication).exit_code)

        publication = self.fresh_publication("topology-template")
        html_path = publication / "index.html"
        html = html_path.read_text(encoding="utf-8")
        section = '<section data-fragment-id="section-one">'
        self.assertIn(section, html)
        html_path.write_text(
            html.replace(section, f"{section}<template>", 1).replace(
                "</section>",
                "</template></section>",
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_output_asset(publication, "html")
        rejected = self.validate(publication)
        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn("active element <template>", rejected.stderr)

        publication = self.fresh_publication("topology-nested-figure")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        nested_figure = copy.deepcopy(manifest["figures"][0])
        nested_figure["id"] = "nested-figure"
        nested_figure["dom_id"] = "nested-figure"
        for part in nested_figure["parts"]:
            part["id"] = part["id"].replace(
                "sample-figure",
                "nested-figure",
                1,
            )
            part["dom_selector"] = f'[data-crop-id="{part["id"]}"]'
        manifest["figures"].append(nested_figure)
        write_json(manifest_path, manifest)

        html_path = publication / "index.html"
        html = html_path.read_text(encoding="utf-8")
        figure_start = html.index("<figure ")
        figure_end = html.index("</figure>", figure_start) + len("</figure>")
        figure_markup = html[figure_start:figure_end]
        nested_markup = figure_markup.replace(
            "sample-figure",
            "nested-figure",
        )
        caption_end = figure_markup.rindex("</figcaption>")
        nested_outer = (
            f"{figure_markup[:caption_end]}"
            f"{nested_markup}"
            f"{figure_markup[caption_end:]}"
        )
        html_path.write_text(
            f"{html[:figure_start]}{nested_outer}{html[figure_end:]}",
            encoding="utf-8",
        )
        self.refresh_output_asset(publication, "html")
        self.assertEqual(2, self.validate(publication).exit_code)

        publication = self.fresh_publication("topology-svg-resource")
        html_path = publication / "index.html"
        html_path.write_text(
            html_path.read_text(encoding="utf-8").replace(
                '<svg class="figure-part"',
                '<svg class="figure-part" filter="url(https://example.test/filter)"',
                1,
            ),
            encoding="utf-8",
        )
        self.refresh_output_asset(publication, "html")
        self.assertEqual(2, self.validate(publication).exit_code)

        for name, marker, replacement in (
            (
                "parts-text",
                '<div class="figure-parts">',
                '<div class="figure-parts">UNBOUND',
            ),
            (
                "crop-tail",
                "</svg>",
                "</svg>UNBOUND",
            ),
            (
                "image-subtree",
                "</image>",
                "<title>UNBOUND</title></image>",
            ),
        ):
            with self.subTest(case=name):
                publication = self.fresh_publication(f"topology-{name}")
                html_path = publication / "index.html"
                html_path.write_text(
                    html_path.read_text(encoding="utf-8").replace(
                        marker,
                        replacement,
                        1,
                    ),
                    encoding="utf-8",
                )
                self.refresh_output_asset(publication, "html")
                self.assertEqual(2, self.validate(publication).exit_code)

    def test_build_rejects_every_existing_output_entry_unchanged(self) -> None:
        workspace = self.fresh_workspace("rerun")
        first = self.build(workspace)
        self.assertEqual(0, first.exit_code, first)
        output = workspace / "publication"
        before = tree_snapshot(output)

        rerun = self.build(workspace)

        self.assertEqual(2, rerun.exit_code, rerun)
        self.assertIn("assembly output already exists", rerun.stderr)
        self.assertEqual(before, tree_snapshot(output))

        directory_workspace = self.fresh_workspace("existing-directory")
        directory_output = directory_workspace / "publication"
        directory_output.mkdir()
        sentinel = directory_output / "sentinel.txt"
        sentinel.write_text("unchanged\n", encoding="utf-8")
        directory_before = tree_snapshot(directory_output)

        directory_result = self.build(directory_workspace)

        self.assertEqual(2, directory_result.exit_code, directory_result)
        self.assertEqual(directory_before, tree_snapshot(directory_output))

        file_workspace = self.fresh_workspace("existing-file")
        file_output = file_workspace / "publication"
        file_output.write_bytes(b"unchanged")

        file_result = self.build(file_workspace)

        self.assertEqual(2, file_result.exit_code, file_result)
        self.assertEqual(b"unchanged", file_output.read_bytes())

    def test_rejects_unrepresentable_windows_output_paths(self) -> None:
        if os.name != "nt":
            self.skipTest("Windows path normalization does not apply")
        for suffix in (".", " "):
            with self.subTest(suffix=repr(suffix)):
                for label, relative in (
                    ("final", Path(f"publication{suffix}")),
                    ("parent", Path(f"nested{suffix}") / "publication"),
                ):
                    workspace = self.fresh_workspace(
                        f"windows-output-{label}-{ord(suffix)}"
                    )
                    before = tree_snapshot(workspace)
                    rejected = self.build(
                        workspace,
                        output=workspace / relative,
                    )
                    self.assertEqual(2, rejected.exit_code, rejected)
                    self.assertIn(
                        "must not end in a dot or space",
                        rejected.stderr,
                    )
                    self.assertEqual(before, tree_snapshot(workspace))

                for label, component in (
                    ("missing", f"nested{suffix}"),
                    ("existing", f"assets{suffix}"),
                ):
                    publication = self.fresh_publication(
                        f"windows-render-{label}-{ord(suffix)}"
                    )
                    before = tree_snapshot(publication)
                    rejected = self.render(
                        publication,
                        self.case_root / "unused-browser.exe",
                        pdf=publication / component / "draft.pdf",
                    )
                    self.assertEqual(2, rejected.exit_code, rejected)
                    self.assertIn(
                        "must not end in a dot or space",
                        rejected.stderr,
                    )
                    self.assertEqual(before, tree_snapshot(publication))

    def test_build_uses_one_final_rename_and_cleans_rename_failure(
        self,
    ) -> None:
        workspace = self.fresh_workspace("rename-success")
        output = workspace / "publication"
        original_rename = Path.rename
        calls: list[tuple[Path, Path]] = []

        def recording_rename(path: Path, target: Path) -> Path:
            calls.append((path, Path(target)))
            return original_rename(path, target)

        with mock.patch.object(Path, "rename", new=recording_rename):
            result = self.build(workspace)

        self.assertEqual(0, result.exit_code, result)
        self.assertEqual(1, len(calls))
        candidate, target = calls[0]
        self.assertEqual(output, target)
        self.assertEqual(output.parent, candidate.parent)
        self.assertNotEqual(output, candidate)

        failed_workspace = self.fresh_workspace("rename-failure")
        failed_output = failed_workspace / "publication"
        failure_message = "synthetic final rename failure"
        failed_calls: list[tuple[Path, Path]] = []

        def failing_rename(path: Path, target: Path) -> Path:
            failed_calls.append((path, Path(target)))
            self.assertEqual(failed_output.parent, path.parent)
            self.assertNotEqual(failed_output, path)
            self.assertEqual(failed_output, Path(target))
            raise PermissionError(failure_message)

        with mock.patch.object(Path, "rename", new=failing_rename):
            failed = self.build(failed_workspace)

        self.assertEqual(2, failed.exit_code, failed)
        self.assertIn(failure_message, failed.stderr)
        self.assertFalse(failed_output.exists())
        self.assertEqual(1, len(failed_calls))
        self.assertFalse(failed_calls[0][0].exists())

    def test_actual_edge_render_then_validate(self) -> None:
        if BROWSER is None:
            self.skipTest(
                "No Chromium-family browser found; render is skipped."
            )
        workspace = self.fresh_workspace()
        built = self.build(workspace)
        self.assertEqual(0, built.exit_code, built)
        publication = workspace / "publication"

        started = time.perf_counter()
        rendered = self.render(publication, BROWSER)
        duration = time.perf_counter() - started
        type(self).browser_used = str(BROWSER)
        type(self).browser_duration_seconds = duration

        self.assertEqual(0, rendered.exit_code, rendered)
        self.assertEqual("rendered", rendered.report["status"])
        pdf = publication / "draft.pdf"
        self.assertTrue(pdf.is_file())
        self.assertGreater(pdf.stat().st_size, 0)

        validated = self.validate(publication)
        self.assertEqual(0, validated.exit_code, validated)
        self.assertEqual("draft.pdf", validated.report["pdf"])

    def test_render_rejects_malformed_and_zero_page_pdfs(self) -> None:
        cases = (
            ("malformed", b"%PDF-1.7\nbroken\n%%EOF\n"),
            ("zero-page", ZERO_PAGE_PDF),
        )
        for name, content in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                built = self.build(workspace)
                self.assertEqual(0, built.exit_code, built)
                publication = workspace / "publication"
                manifest_path = publication / "assembly-manifest.json"
                manifest_before = manifest_path.read_bytes()
                browser = workspace / "browser.exe"
                browser.write_bytes(b"fixture browser")

                def write_invalid(
                    _html: Path,
                    output: Path,
                    _browser: Path,
                    _allowed: set[Path],
                    *,
                    payload: bytes = content,
                ) -> None:
                    output.write_bytes(payload)

                with mock.patch.object(
                    assemble_print,
                    "render_pdf",
                    side_effect=write_invalid,
                ):
                    result = self.render(publication, browser)

                self.assertEqual(2, result.exit_code)
                self.assertEqual({}, result.report)
                self.assertFalse((publication / "draft.pdf").exists())
                self.assertEqual(manifest_before, manifest_path.read_bytes())
                manifest = read_json(manifest_path)
                self.assertIsNone(manifest["outputs"]["draft_pdf"])

    def test_file_url_path_preserves_percent_escape_like_component(
        self,
    ) -> None:
        publication = self.fresh_publication("%41-publication")
        html = publication / "index.html"

        self.assertEqual(
            html.resolve(), assemble_print.file_url_path(html.as_uri())
        )

    def test_render_pdf_enforces_fixed_deadline_and_cleans_browser(
        self,
    ) -> None:
        html_path = self.case_root / "index.html"
        html_path.write_text("<!doctype html><title>Timeout</title>")
        output_path = self.case_root / "draft.pdf"
        browser_path = self.case_root / "browser.exe"
        browser_path.write_bytes(b"fixture browser")

        async def wait_past_deadline(**_kwargs: Any) -> None:
            await asyncio.sleep(60)

        page = mock.Mock()
        page.goto = mock.AsyncMock()
        page.emulate_media = mock.AsyncMock()
        page.pdf = mock.AsyncMock(side_effect=wait_past_deadline)
        context = mock.Mock()
        context.route = mock.AsyncMock()
        context.new_page = mock.AsyncMock(return_value=page)
        context.close = mock.AsyncMock()
        browser = mock.Mock()
        browser.new_context = mock.AsyncMock(return_value=context)
        browser.close = mock.AsyncMock()
        chromium = mock.Mock()
        chromium.launch = mock.AsyncMock(return_value=browser)
        playwright = mock.Mock(chromium=chromium)
        manager = mock.MagicMock()
        manager.__aenter__ = mock.AsyncMock(return_value=playwright)
        manager.__aexit__ = mock.AsyncMock(return_value=None)

        with (
            mock.patch(
                "playwright.async_api.async_playwright",
                return_value=manager,
            ),
            mock.patch.object(assemble_print, "PDF_TIMEOUT_MS", 1),
            self.assertRaisesRegex(  # noqa: PT027
                assemble_print.ContractError,
                "fixed 1 ms deadline",
            ),
        ):
            assemble_print.render_pdf(
                html_path,
                output_path,
                browser_path,
                {html_path.resolve()},
            )

        browser.new_context.assert_awaited_once_with(
            java_script_enabled=False,
            service_workers="block",
        )
        context.route.assert_awaited_once()
        page.goto.assert_awaited_once_with(
            html_path.as_uri(),
            wait_until="networkidle",
            timeout=assemble_print.NAVIGATION_TIMEOUT_MS,
        )
        page.pdf.assert_awaited_once()
        context.close.assert_awaited_once_with()
        browser.close.assert_awaited_once_with()
        self.assertFalse(output_path.exists())

    def test_validate_rejects_pdf_over_page_ceiling(self) -> None:
        publication = self.fresh_publication("oversized-pdf")
        pdf = publication / "draft.pdf"
        document = fitz.open()
        for _page in range(assemble_print.MAX_PDF_PAGES + 1):
            document.new_page(width=612, height=792)
        document.save(pdf)
        document.close()
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["outputs"]["draft_pdf"] = asset_record(publication, pdf)
        write_json(manifest_path, manifest)

        rejected = self.validate(publication)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn("page validation ceiling", rejected.stderr)

    def test_manifest_rejects_blank_figure_alternative(self) -> None:
        publication = self.fresh_publication("blank-figure-alt")
        manifest_path = publication / "assembly-manifest.json"
        manifest = read_json(manifest_path)
        manifest["figures"][0]["alt"] = "   "
        write_json(manifest_path, manifest)

        rejected = self.validate(publication)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn("does not match", rejected.stderr)

    def test_render_is_fresh_only_and_rolls_back_manifest_commit_failure(
        self,
    ) -> None:
        browser = self.case_root / "browser.exe"
        browser.write_bytes(b"fixture browser")

        existing = self.fresh_publication("existing-destination")
        destination = existing / "draft.pdf"
        destination.write_bytes(b"must remain")
        existing_before = tree_snapshot(existing)
        refused = self.render(existing, browser)
        self.assertEqual(2, refused.exit_code, refused)
        self.assertIn("destination already exists", refused.stderr)
        self.assertEqual(existing_before, tree_snapshot(existing))

        publication = self.fresh_publication("successful-render")
        requested_pdf = (
            publication
            / ("ASSETS" if os.name == "nt" else "assets")
            / "draft.pdf"
        )
        with mock.patch.object(
            assemble_print,
            "render_pdf",
            side_effect=self.write_valid_pdf,
        ):
            rendered = self.render(
                publication,
                browser,
                pdf=requested_pdf,
            )
        self.assertEqual(0, rendered.exit_code, rendered)
        self.assertEqual(
            "assets/draft.pdf",
            read_json(publication / "assembly-manifest.json")["outputs"][
                "draft_pdf"
            ]["path"],
        )
        committed = tree_snapshot(publication)
        manifest_bytes = (publication / "assembly-manifest.json").read_bytes()

        alternate = publication / "alternate.pdf"
        second = self.render(publication, browser, pdf=alternate)
        self.assertEqual(2, second.exit_code, second)
        self.assertIn("assembly already has a draft PDF", second.stderr)
        self.assertEqual(committed, tree_snapshot(publication))
        self.assertFalse(alternate.exists())
        self.assertEqual(
            manifest_bytes,
            (publication / "assembly-manifest.json").read_bytes(),
        )

        rollback = self.fresh_publication("commit-failure")
        rollback_manifest = rollback / "assembly-manifest.json"
        before = rollback_manifest.read_bytes()
        original_write_atomic = assemble_print.write_atomic
        failed = False
        failure_message = "synthetic manifest commit failure"

        def fail_at_manifest_publication(path: Path, content: bytes) -> None:
            nonlocal failed
            if path == rollback_manifest and content != before and not failed:
                failed = True
                pending_manifest = json.loads(content)
                pending_record = pending_manifest["outputs"]["draft_pdf"]
                self.assertIsInstance(pending_record, dict)
                final_pdf = rollback.joinpath(
                    *Path(pending_record["path"]).parts
                )
                self.assertTrue(final_pdf.is_file())
                self.assertEqual(
                    pending_record,
                    asset_record(rollback, final_pdf),
                )
                original_write_atomic(path, content)
                raise PermissionError(failure_message)
            original_write_atomic(path, content)

        with (
            mock.patch.object(
                assemble_print,
                "render_pdf",
                side_effect=self.write_valid_pdf,
            ),
            mock.patch.object(
                assemble_print,
                "write_atomic",
                side_effect=fail_at_manifest_publication,
            ),
        ):
            rejected = self.render(rollback, browser)

        self.assertEqual(2, rejected.exit_code, rejected)
        self.assertIn(failure_message, rejected.stderr)
        self.assertFalse((rollback / "draft.pdf").exists())
        self.assertEqual(before, rollback_manifest.read_bytes())

    def test_visible_text_normalization_unit(self) -> None:
        cases = (
            (["  Cafe\u0301", "\n\ttext  "], "Café text"),
            (["alpha", " ", "beta"], "alpha beta"),
            ([" \n\t "], ""),
        )
        for parts, normalized in cases:
            with self.subTest(parts=parts):
                self.assertEqual(
                    normalized,
                    assemble_print.normalize_visible_text(parts),
                )
                self.assertEqual(
                    sha256_bytes(normalized.encode()),
                    assemble_print.visible_text_sha256(parts),
                )

    def test_shared_publication_policy_conformance_corpus(self) -> None:
        profile, _content = assemble_print.load_profile()
        corpus = read_json(SUPPORT_ROOT / "publication-policy-conformance.json")
        families = {
            value.casefold() for value in corpus["declared_font_families"]
        }
        for case in corpus["fragment_cases"]:
            with self.subTest(fragment=case["id"]):
                if case["accepted"]:
                    assemble_print.parse_markup(
                        case["content"],
                        profile,
                        case["id"],
                        allow_figure_markers=True,
                    )
                else:
                    with self.assertRaises(  # noqa: PT027
                        assemble_print.ContractError
                    ):
                        assemble_print.parse_markup(
                            case["content"],
                            profile,
                            case["id"],
                            allow_figure_markers=True,
                        )
        for case in corpus["stylesheet_cases"]:
            with self.subTest(stylesheet=case["id"]):
                if case["accepted"]:
                    assemble_print.validate_and_scope_stylesheet(
                        case["content"],
                        profile,
                        families,
                        case["id"],
                    )
                else:
                    with self.assertRaises(  # noqa: PT027
                        assemble_print.ContractError
                    ):
                        assemble_print.validate_and_scope_stylesheet(
                            case["content"],
                            profile,
                            families,
                            case["id"],
                        )

    def test_manifest_asset_projection_and_tree_closure(self) -> None:
        shared = self.fresh_publication("shared")
        shared_manifest_path = shared / "assembly-manifest.json"
        shared_manifest = read_json(shared_manifest_path)
        first_source = copy.deepcopy(
            shared_manifest["figures"][0]["parts"][0]["source_svg"]
        )
        second_part = shared_manifest["figures"][0]["parts"][1]
        old_path = second_part["source_svg"]["path"]
        second_part["source_svg"] = first_source
        html_path = shared / "index.html"
        html_path.write_text(
            html_path.read_text(encoding="utf-8").replace(
                f'href="{old_path}"',
                f'href="{first_source["path"]}"',
                1,
            ),
            encoding="utf-8",
        )
        shared_manifest["outputs"]["html"] = asset_record(shared, html_path)
        shared.joinpath(*Path(old_path).parts).unlink()
        write_json(shared_manifest_path, shared_manifest)
        shared_result = self.validate(shared)
        self.assertEqual(0, shared_result.exit_code, shared_result)

        conflicting = self.fresh_publication("conflicting")
        conflicting_manifest = read_json(conflicting / "assembly-manifest.json")
        conflicting_manifest["stylesheets"][0]["path"] = conflicting_manifest[
            "outputs"
        ]["css"]["path"]
        write_json(
            conflicting / "assembly-manifest.json",
            conflicting_manifest,
        )
        conflict = self.validate(conflicting)
        self.assertEqual(2, conflict.exit_code, conflict)
        self.assertIn(
            "conflicting semantic asset declarations",
            conflict.stderr,
        )

        rogue = self.fresh_publication("rogue")
        (rogue / "rogue.txt").write_text("rogue", encoding="utf-8")
        self.assertEqual(2, self.validate(rogue).exit_code)

        missing = self.fresh_publication("missing")
        (missing / "fragments" / "section-one.html").unlink()
        self.assertEqual(2, self.validate(missing).exit_code)

        for name, field, value in (
            ("hash-drift", "sha256", "0" * 64),
            ("byte-drift", "bytes", 1),
        ):
            with self.subTest(case=name):
                drift = self.fresh_publication(name)
                manifest_path = drift / "assembly-manifest.json"
                drift_manifest = read_json(manifest_path)
                drift_manifest["outputs"]["html"][field] = value
                write_json(manifest_path, drift_manifest)
                self.assertEqual(2, self.validate(drift).exit_code)

        linked = self.fresh_publication("linked")
        link = linked / "linked-index.html"
        try:
            link.symlink_to(linked / "index.html")
        except (NotImplementedError, OSError):
            pass
        else:
            self.assertEqual(2, self.validate(linked).exit_code)


if __name__ == "__main__":
    unittest.main(verbosity=2)
