# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "cssselect2==0.8.0",
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

import importlib
import sys
import tempfile
import time
import unittest
from pathlib import Path
from typing import TYPE_CHECKING, Any
from unittest import mock

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
apply_profile_mutation = publication_test_support.apply_profile_mutation
asset_record = publication_test_support.asset_record
copy_tree = publication_test_support.copy_tree
detect_browser = publication_test_support.detect_browser
import_by_path = publication_test_support.import_by_path
invoke_main = publication_test_support.invoke_main
read_json = publication_test_support.read_json
sha256_bytes = publication_test_support.sha256_bytes
tree_snapshot = publication_test_support.tree_snapshot
visible_text_sha256 = publication_test_support.visible_text_sha256
windows_short_path = publication_test_support.windows_short_path
write_json = publication_test_support.write_json
write_test_font = publication_test_support.write_test_font

validate_package = import_by_path(
    "scholarly_validate_package_for_assembly_tests",
    PUBLICATION_ROOT / "scripts" / "validate_package.py",
)
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
                "source_block_number": 0,
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
        text = source_blocks(number)["blocks"][0]["text"]
        pages.append(
            {
                "id": page_id,
                "pdf_page": number,
                "printed_folio": str(number),
                "width": 360,
                "height": 480,
                "rotation": 0,
                "crop_box": [0, 0, 360, 480],
                "media_box": [0, 0, 360, 480],
                "assets": {
                    "blocks": asset_record(source_root, blocks_path),
                    "svg": asset_record(source_root, svg_path),
                },
                "block_count": 1,
                "text_characters": len("".join(text.split())),
                "replacement_characters": 0,
                "image_count": 0,
                "vector_drawing_count": 1,
                "link_count": 0,
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
            "package_id": "source-1111111111111111-1-3",
            "generator": {
                "name": "reconstruct_pdf.py",
                "version": "0.1.0",
                "runtime": "python-3.12.11",
                "parser": "PyMuPDF-1.26.6",
            },
            "source": {
                "file_name": "source.pdf",
                "sha256": "1" * 64,
                "bytes": 12345,
                "rights_note": "Authorized deterministic test fixture.",
                "page_count": 3,
                "encrypted": False,
                "attachments": [],
                "embedded_javascript": False,
            },
            "selection": {
                "pdf_pages": [1, 2, 3],
                "first_pdf_page": 1,
                "last_pdf_page": 3,
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
        self.cases = read_json(SUPPORT_ROOT / "publication-profile-cases.json")[
            "mutations"
        ]

    def test_package_validator_profile_shape_mutations(self) -> None:
        for case in self.cases:
            with (
                self.subTest(case=case["id"]),
                tempfile.TemporaryDirectory(
                    prefix="scholarly-package-profile-"
                ) as temporary,
            ):
                skills = Path(temporary)
                write_json(
                    skills
                    / "scholarly-print-assembly"
                    / "assets"
                    / "publication-profile.json",
                    apply_profile_mutation(self.profile, case),
                )
                with mock.patch.object(
                    validate_package,
                    "SKILLS_ROOT",
                    skills,
                ):
                    if case["accepted"]:
                        validate_package.validate_publication_profile()
                    else:
                        with self.assertRaises(  # noqa: PT027
                            validate_package.ValidationError
                        ):
                            validate_package.validate_publication_profile()

    def test_assembly_loader_profile_shape_mutations(self) -> None:
        for case in self.cases:
            with (
                self.subTest(case=case["id"]),
                tempfile.TemporaryDirectory(
                    prefix="scholarly-assembly-profile-"
                ) as temporary,
            ):
                assets = Path(temporary)
                write_json(
                    assets / "publication-profile.json",
                    apply_profile_mutation(self.profile, case),
                )
                with mock.patch.object(assemble_print, "ASSETS", assets):
                    assemble_print.load_profile.cache_clear()
                    try:
                        if case["accepted"]:
                            assemble_print.load_profile()
                        else:
                            with self.assertRaises(  # noqa: PT027
                                assemble_print.ContractError
                            ):
                                assemble_print.load_profile()
                    finally:
                        assemble_print.load_profile.cache_clear()


class AssemblePrintReplacementTests(unittest.TestCase):
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
        force: bool = False,
        output: Path | None = None,
    ) -> MainResult:
        arguments = [
            "build",
            "--spec",
            str(workspace / "assembly-spec.json"),
            "--output",
            str(output or workspace / "publication"),
        ]
        if force:
            arguments.append("--force")
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
        force: bool = False,
    ) -> MainResult:
        arguments = [
            "render",
            "--html",
            str(publication / "index.html"),
            "--pdf",
            str(publication / "draft.pdf"),
            "--browser",
            str(browser),
        ]
        if force:
            arguments.append("--force")
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
        tracked = [record["path"] for record in manifest["tracked_files"]]
        self.assertEqual(expected, tracked)
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
            result = self.build(workspace, force=True, output=output)
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
            result = self.build(workspace, force=True, output=output)
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
            result = self.build(workspace, force=True, output=output)
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

    def test_cli_rejects_deeply_nested_json_without_mutating_output(
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

        result = self.build(workspace, force=True, output=output)

        self.assertEqual(2, result.exit_code, result)
        self.assertEqual({}, result.report)
        self.assertEqual("", result.stdout)
        self.assertIn(
            "error: assembly specification is not valid JSON:",
            result.stderr,
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

        result = self.build(workspace, force=True, output=output)

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

        def reconstruction_counts(workspace: Path) -> None:
            self.update_json(
                workspace / "source" / "source-package.json",
                lambda source: source["pages"][0].update(
                    {
                        "block_count": 0,
                        "text_characters": 0,
                        "replacement_characters": 1,
                    }
                ),
            )
            self.refresh_bundle_source(workspace)

        cases = (
            ("unconsumed-blocks", unconsumed_blocks),
            ("reconstruction-counts", reconstruction_counts),
        )
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

        cases = (
            (
                "invalid",
                lambda workspace: (
                    workspace / "fonts" / "fixture.ttf"
                ).write_bytes(b"not a font"),
            ),
            (
                "undercoverage",
                lambda workspace: write_test_font(
                    workspace / "fonts" / "fixture.ttf",
                    characters=" A",
                    family="Fixture Serif",
                ),
            ),
        )
        for name, mutation in cases:
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                mutation(workspace)
                self.assert_build_rejected(workspace)

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

    def test_build_force_replacement_and_late_failure_rollback(self) -> None:
        workspace = self.fresh_workspace()
        first = self.build(workspace)
        self.assertEqual(0, first.exit_code, first)
        output = workspace / "publication"
        original = tree_snapshot(output)

        refused = self.build(workspace)
        self.assertEqual(2, refused.exit_code)
        self.assertEqual({}, refused.report)
        self.assertEqual(original, tree_snapshot(output))

        self.refresh_fragment(
            workspace,
            RICH_FRAGMENT.replace(
                "代表性中文段落",
                "中文段落代表性",
            ),
        )
        replaced = self.build(workspace, force=True)
        self.assertEqual(0, replaced.exit_code, replaced)
        replacement = tree_snapshot(output)
        self.assertNotEqual(original, replacement)

        self.refresh_fragment(
            workspace,
            RICH_FRAGMENT.replace(
                "代表性中文段落",
                "段落中文代表性",
            ),
        )
        original_replace = Path.replace
        parent_before = tree_snapshot(workspace)
        publish_failed = False
        failure_message = "synthetic publish failure"

        def fail_late(path: Path, target: Path) -> Path:
            nonlocal publish_failed
            if not publish_failed and path != output and Path(target) == output:
                publish_failed = True
                raise PermissionError(failure_message)
            return original_replace(path, target)

        with mock.patch.object(Path, "replace", new=fail_late):
            failed = self.build(workspace, force=True)

        self.assertEqual(2, failed.exit_code)
        self.assertEqual({}, failed.report)
        self.assertEqual(replacement, tree_snapshot(output))
        self.assertEqual(parent_before, tree_snapshot(workspace))

    @unittest.skipUnless(
        sys.platform == "win32",
        "Windows alias suffix behavior is platform-specific.",
    )
    def test_windows_output_alias_suffixes_are_rejected_without_replacement(
        self,
    ) -> None:
        workspace = self.fresh_workspace()
        output = workspace / "publication"
        initial = self.build(workspace)
        self.assertEqual(0, initial.exit_code, initial)
        before = tree_snapshot(output)

        for suffix in (".", " "):
            with self.subTest(suffix=repr(suffix)):
                result = self.build(
                    workspace,
                    force=True,
                    output=workspace / f"publication{suffix}",
                )

                self.assertEqual(2, result.exit_code, result)
                self.assertEqual(before, tree_snapshot(output))
                self.assertIn(
                    "must not end in a dot or space",
                    result.stderr,
                )
                self.assertFalse(
                    any(
                        path.name.startswith(
                            (
                                f".{output.name}.staging-",
                                f".{output.name}.backup-",
                            )
                        )
                        for path in workspace.iterdir()
                    )
                )

    @unittest.skipUnless(
        sys.platform == "win32",
        "Windows short-name aliases are platform-specific.",
    )
    def test_existing_short_name_alias_preserves_canonical_output_name(
        self,
    ) -> None:
        workspace = self.fresh_workspace()
        output = workspace / "owned publication directory"
        initial = self.build(workspace, output=output)
        self.assertEqual(0, initial.exit_code, initial)
        before = tree_snapshot(output)

        short_output = windows_short_path(output)
        if short_output is None:
            self.skipTest("Win32 short-name lookup is unavailable")
        if short_output.name.casefold() == output.name.casefold():
            self.skipTest("8.3 short names are disabled for this volume")
        self.assertTrue(output.samefile(short_output))

        self.refresh_fragment(
            workspace,
            RICH_FRAGMENT.replace(
                "代表性中文段落",
                "中文段落代表性",
            ),
        )
        replaced = self.build(
            workspace,
            force=True,
            output=short_output,
        )

        self.assertEqual(0, replaced.exit_code, replaced)
        self.assertTrue(output.is_dir())
        self.assertTrue(output.samefile(short_output))
        self.assertNotEqual(before, tree_snapshot(output))
        self.assertEqual(
            str(output.resolve() / "assembly-manifest.json"),
            replaced.report["manifest"],
        )
        self.assertIn(output.name, {path.name for path in workspace.iterdir()})
        self.assertNotIn(
            short_output.name,
            {path.name for path in workspace.iterdir()},
        )

    def test_build_force_requires_assembly_ownership_marker(self) -> None:
        workspace = self.fresh_workspace()
        output = workspace / "publication"
        output.mkdir()
        sentinel = output / "unrelated-sentinel.txt"
        sentinel.write_text("must survive\n", encoding="utf-8")
        before = tree_snapshot(output)

        refused = self.build(workspace, force=True)

        self.assertEqual(2, refused.exit_code)
        self.assertIn("assembly ownership marker", refused.stderr)
        self.assertEqual(before, tree_snapshot(output))
        self.assertEqual("must survive\n", sentinel.read_text(encoding="utf-8"))

    def test_build_force_rejects_final_output_links(self) -> None:
        workspace = self.fresh_workspace("directory-link")
        output = workspace / "publication"
        external = self.case_root / "external-publication"
        external.mkdir()
        (external / "assembly-manifest.json").write_text(
            "damaged owned output\n",
            encoding="utf-8",
        )
        sentinel = external / "external-sentinel.txt"
        sentinel.write_text("must survive\n", encoding="utf-8")
        external_before = tree_snapshot(external)
        try:
            output.symlink_to(external, target_is_directory=True)
        except (NotImplementedError, OSError) as error:
            self.skipTest(f"directory symlinks are unavailable: {error}")
        link_target = output.readlink()

        refused = self.build(workspace, force=True)

        self.assertEqual(2, refused.exit_code)
        self.assertEqual({}, refused.report)
        self.assertEqual("", refused.stdout)
        self.assertIn(
            "error: assembly output exists and is not a regular directory",
            refused.stderr,
        )
        self.assertTrue(output.is_symlink())
        self.assertEqual(link_target, output.readlink())
        self.assertEqual(external_before, tree_snapshot(external))
        self.assertEqual("must survive\n", sentinel.read_text(encoding="utf-8"))

        dangling_workspace = self.fresh_workspace("dangling-link")
        dangling_output = dangling_workspace / "publication"
        missing_target = self.case_root / "missing-publication"
        missing_target.mkdir()
        dangling_output.symlink_to(missing_target, target_is_directory=True)
        dangling_link_target = dangling_output.readlink()
        missing_target.rmdir()

        dangling_refused = self.build(dangling_workspace, force=True)

        self.assertEqual(2, dangling_refused.exit_code)
        self.assertEqual({}, dangling_refused.report)
        self.assertEqual("", dangling_refused.stdout)
        self.assertIn(
            "error: assembly output exists and is not a regular directory",
            dangling_refused.stderr,
        )
        self.assertTrue(dangling_output.is_symlink())
        self.assertEqual(dangling_link_target, dangling_output.readlink())
        self.assertFalse(missing_target.exists())
        self.assertEqual(external_before, tree_snapshot(external))

    def test_build_force_allows_empty_or_damaged_owned_directory(
        self,
    ) -> None:
        for name, populate in (
            ("empty", False),
            ("damaged-owned", True),
        ):
            with self.subTest(case=name):
                workspace = self.fresh_workspace(name)
                output = workspace / "publication"
                output.mkdir()
                if populate:
                    (output / "assembly-manifest.json").write_text(
                        "damaged older manifest\n",
                        encoding="utf-8",
                    )
                    (output / "stale.bin").write_bytes(b"stale")

                result = self.build(workspace, force=True)

                self.assertEqual(0, result.exit_code, result)
                self.assertEqual(
                    "assembled",
                    read_json(output / "assembly-manifest.json")["status"],
                )
                self.assertFalse((output / "stale.bin").exists())

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
        self.assertEqual(str(BROWSER.resolve()), rendered.report["browser"])
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
                manifest = read_json(publication / "assembly-manifest.json")
                self.assertIsNone(manifest["outputs"]["draft_pdf"])

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

    def test_retained_tree_identity_unit(self) -> None:
        publication = self.fresh_publication()
        manifest = read_json(publication / "assembly-manifest.json")
        inventory = assemble_print.validate_manifest_inventory(
            publication,
            manifest,
        )
        self.assertEqual(
            {record["path"] for record in manifest["tracked_files"]}
            | {"assembly-manifest.json"},
            set(inventory),
        )

        rogue = self.fresh_publication("rogue")
        (rogue / "rogue.txt").write_text("rogue", encoding="utf-8")
        with self.assertRaises(  # noqa: PT027
            assemble_print.ContractError
        ):
            assemble_print.validate_manifest_inventory(
                rogue,
                read_json(rogue / "assembly-manifest.json"),
            )

        unsorted = self.fresh_publication("unsorted")
        unsorted_manifest = read_json(unsorted / "assembly-manifest.json")
        unsorted_manifest["tracked_files"].reverse()
        with self.assertRaises(  # noqa: PT027
            assemble_print.ContractError
        ):
            assemble_print.validate_manifest_inventory(
                unsorted,
                unsorted_manifest,
            )


if __name__ == "__main__":
    unittest.main(verbosity=2)
