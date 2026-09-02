# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "fonttools==4.60.1",
#   "jsonschema==4.25.1",
#   "pymupdf==1.26.6",
# ]
# ///

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import unicodedata
from pathlib import Path
from typing import Any

import fitz
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

PAGE_WIDTH = 360
PAGE_HEIGHT = 480
FONT_FAMILY = "Fixture Publication"


class FixtureError(RuntimeError):
    """Report deterministic fixture generation failures."""


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def asset(root: Path, path: Path) -> dict[str, Any]:
    content = path.read_bytes()
    return {
        "path": path.relative_to(root).as_posix(),
        "sha256": hashlib.sha256(content).hexdigest(),
        "bytes": len(content),
    }


def locate_skill_script(skill: str, name: str) -> Path:
    starts = [Path(__file__).resolve(), Path.cwd().resolve()]
    candidates: list[Path] = []
    for start in starts:
        for parent in (start, *start.parents):
            candidates.extend(
                (
                    parent / "skills" / skill / "scripts" / name,
                    parent / ".agents" / "skills" / skill / "scripts" / name,
                    parent / skill / "scripts" / name,
                )
            )
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    message = f"cannot locate runtime script {skill}/{name}"
    raise FixtureError(message)


def run_runtime(script: Path, arguments: list[str]) -> None:
    completed = subprocess.run(  # noqa: S603 - executes a located local script.
        [sys.executable, str(script), *arguments],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip()
        message = f"{script.name} failed: {detail}"
        raise FixtureError(message)


def build_font(path: Path, text: str) -> None:
    codepoints = sorted(set(range(32, 127)) | {ord(char) for char in text})
    glyph_order = [".notdef"] + [
        f"uni{codepoint:04X}" if codepoint <= 0xFFFF else f"u{codepoint:X}"
        for codepoint in codepoints
    ]
    cmap = {
        codepoint: glyph_order[index + 1]
        for index, codepoint in enumerate(codepoints)
    }
    glyphs: dict[str, Any] = {}
    metrics: dict[str, tuple[int, int]] = {}
    for glyph_name in glyph_order:
        pen = TTGlyphPen(None)
        if glyph_name not in {".notdef", "uni0020"}:
            pen.moveTo((80, 80))
            pen.lineTo((520, 80))
            pen.lineTo((520, 700))
            pen.lineTo((80, 700))
            pen.closePath()
        glyphs[glyph_name] = pen.glyph()
        metrics[glyph_name] = (600, 0)

    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(glyph_order)
    builder.setupCharacterMap(cmap)
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable(
        {
            "familyName": FONT_FAMILY,
            "styleName": "Regular",
            "uniqueFontIdentifier": f"{FONT_FAMILY} Regular 1.0",
            "fullName": f"{FONT_FAMILY} Regular",
            "psName": "FixturePublication-Regular",
            "version": "Version 1.0",
        }
    )
    builder.setupOS2(
        sTypoAscender=800,
        sTypoDescender=-200,
        usWinAscent=800,
        usWinDescent=200,
    )
    builder.setupPost()
    builder.setupMaxp()
    builder.setupHead(created=2082844800, modified=2082844800)
    builder.font.recalcTimestamp = False
    path.parent.mkdir(parents=True, exist_ok=True)
    builder.save(path)


def visible_text_sha256(text: str) -> str:
    normalized = " ".join(unicodedata.normalize("NFC", text).split())
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def build_source_pdf(path: Path, *, pages: int, mixed: bool) -> None:
    document = fitz.open()
    for page_number in range(1, pages + 1):
        page = document.new_page(width=PAGE_WIDTH, height=PAGE_HEIGHT)
        if mixed:
            page.insert_text(
                fitz.Point(36, 54),
                f"Example {page_number}: retained voice leading label",
                fontsize=11,
                fontname="helv",
            )
            page.insert_text(
                fitz.Point(36, 82),
                f"第{page_number}页：混合文字与跨页谱例",
                fontsize=11,
                fontname="china-s",
            )
        else:
            page.insert_text(
                fitz.Point(36, 54),
                (
                    f"Approved source page {page_number} contains enough "
                    "extractable text for a passing source package."
                ),
                fontsize=11,
                fontname="helv",
            )
        page.draw_rect(
            fitz.Rect(54, 205, 306, 345),
            color=(0, 0, 0),
            width=1,
        )
        for offset in range(4):
            y = 230 + (offset * 24)
            page.draw_line(
                fitz.Point(72, y),
                fitz.Point(288, y),
                color=(0, 0, 0),
                width=0.8,
            )
    document.set_metadata(
        {
            "producer": "scholarly-publication assembly fixture",
            "creationDate": "D:20000101000000Z",
            "modDate": "D:20000101000000Z",
        }
    )
    document.save(path, garbage=4, deflate=True, no_new_id=True)
    document.close()


def build_source_package(root: Path, *, with_figure: bool) -> Path:
    source_pdf = root / "source.pdf"
    page_count = 2 if with_figure else 1
    build_source_pdf(source_pdf, pages=page_count, mixed=with_figure)

    arguments = [
        "extract",
        "--pdf",
        str(source_pdf),
        "--output",
        str(root / "inputs" / "source"),
        "--pages",
        "1-2" if with_figure else "1",
        "--rights-note",
        "Synthetic authorized assembly evaluation fixture.",
    ]
    if with_figure:
        figure_map = root / "figure-map.json"
        write_json(
            figure_map,
            {
                "schema_version": "1.0",
                "coordinate_space": "pdf-points-top-left",
                "figures": [
                    {
                        "id": "example-a",
                        "source_label": "Example A",
                        "profile": "music-notation",
                        "embedded_language_inventory": ["en", "zh-Hans"],
                        "parts": [
                            {
                                "id": "example-a-part-1",
                                "order": 1,
                                "pdf_page": 1,
                                "bbox": [54, 205, 306, 345],
                            },
                            {
                                "id": "example-a-part-2",
                                "order": 2,
                                "pdf_page": 2,
                                "bbox": [54, 205, 306, 345],
                            },
                        ],
                    }
                ],
            },
        )
        arguments.extend(
            (
                "--figure-map",
                str(figure_map),
                "--profile",
                "music-notation",
            )
        )

    run_runtime(
        locate_skill_script(
            "scholarly-pdf-reconstruction",
            "reconstruct_pdf.py",
        ),
        arguments,
    )
    return root / "inputs" / "source" / "source-package.json"


def first_block_ids(source_manifest: Path) -> list[str]:
    source = json.loads(source_manifest.read_text(encoding="utf-8"))
    identifiers: list[str] = []
    for page in source["pages"]:
        block_path = source_manifest.parent / page["assets"]["blocks"]["path"]
        blocks = json.loads(block_path.read_text(encoding="utf-8"))["blocks"]
        identifiers.append(blocks[0]["id"])
    return identifiers


def build_case(  # noqa: PLR0913 - explicit fixture inputs keep cases readable.
    root: Path,
    *,
    publication_id: str,
    title: str,
    language: str,
    fragment_html: str,
    visible_text: str,
    stylesheet: str | None,
    with_figure: bool,
) -> None:
    root.mkdir(parents=True, exist_ok=True)
    source_manifest = build_source_package(root, with_figure=with_figure)
    block_ids = first_block_ids(source_manifest)

    inputs = root / "inputs"
    fragment = inputs / "fragments" / "chapter.html"
    fragment.parent.mkdir(parents=True, exist_ok=True)
    fragment.write_text(fragment_html, encoding="utf-8")
    bundle = inputs / "translation-bundle.json"
    write_json(
        bundle,
        {
            "schema_version": "1.0",
            "source_package": asset(inputs, source_manifest),
            "target_language": language,
            "fragment_text_normalization": (
                "approved-fragment-visible-text-v1"
            ),
            "approval": {
                "status": "approved",
                "reference": "Synthetic offline evaluation approval.",
            },
            "fragments": [
                {
                    "id": "chapter",
                    **asset(inputs, fragment),
                    "visible_text_sha256": visible_text_sha256(visible_text),
                    "source_block_ids": block_ids,
                }
            ],
        },
    )

    font = root / "resources" / "fonts" / "fixture.ttf"
    build_font(
        font,
        (
            title
            + visible_text
            + " Example A part 1 part 2"
            + "谱例跨页混合文字与中文注释"
        ),
    )
    stylesheets: list[str] = []
    if stylesheet is not None:
        stylesheet_path = root / "resources" / "styles" / "additional.css"
        stylesheet_path.parent.mkdir(parents=True, exist_ok=True)
        stylesheet_path.write_text(stylesheet, encoding="utf-8")
        stylesheets.append("resources/styles/additional.css")

    figures: list[dict[str, Any]] = []
    profiles: list[str] = []
    if with_figure:
        figures.append(
            {
                "id": "example-a",
                "caption_html": ('<span lang="en">Example A</span> 跨页谱例'),
                "alt": "跨页谱例 Example A",
                "class": "music-example",
            }
        )
        profiles.append("music-notation")

    write_json(
        root / "assembly-spec.json",
        {
            "schema_version": "1.0",
            "publication_id": publication_id,
            "title": title,
            "title_language": "en",
            "language": language,
            "source_package": "inputs/source/source-package.json",
            "translation_bundle": "inputs/translation-bundle.json",
            "fragment_order": ["chapter"],
            "figures": figures,
            "font_roles": {
                "body-cjk": FONT_FAMILY,
                "body-latin": FONT_FAMILY,
            },
            "fonts": [
                {
                    "family": FONT_FAMILY,
                    "path": "resources/fonts/fixture.ttf",
                    "style": "normal",
                    "weight": 400,
                },
                {
                    "family": FONT_FAMILY,
                    "path": "resources/fonts/fixture.ttf",
                    "style": "normal",
                    "weight": 700,
                },
            ],
            "stylesheets": stylesheets,
            "page": {
                "size": "letter",
                "margin_in": {
                    "top": 0.65,
                    "right": 0.7,
                    "bottom": 0.7,
                    "left": 0.75,
                },
            },
            "profiles": profiles,
        },
    )


def build_external(root: Path) -> None:
    visible = "Approved chapter External approved text is retained."
    build_case(
        root,
        publication_id="approved-external",
        title="Approved External Bundle",
        language="en",
        fragment_html=(
            "<h1>Approved chapter</h1>\n"
            '<p class="chapter">External approved text is retained.</p>\n'
        ),
        visible_text=visible,
        stylesheet=".chapter { line-height: 1.4; }\n",
        with_figure=False,
    )


def build_policy_cases(root: Path) -> None:
    build_case(
        root / "fragment-input",
        publication_id="fragment-profile",
        title="Fragment Profile",
        language="en",
        fragment_html=(
            '<p>Approved prose.</p>\n<img src="unsupported.png" alt="">\n'
        ),
        visible_text="Approved prose.",
        stylesheet=None,
        with_figure=False,
    )
    build_case(
        root / "stylesheet-input",
        publication_id="stylesheet-profile",
        title="Stylesheet Profile",
        language="en",
        fragment_html="<p>Approved prose.</p>\n",
        visible_text="Approved prose.",
        stylesheet="p { display: none; }\n",
        with_figure=False,
    )


def build_figure(root: Path) -> None:
    visible = (
        "混合文字排版 中文正文保留 voice leading。 续页谱例说明保持为独立段落。"
    )
    build_case(
        root,
        publication_id="mixed-figure",
        title="Mixed-Script Figure",
        language="zh-Hans",
        fragment_html=(
            "<h1>混合文字排版</h1>\n"
            '<p>中文正文保留 <span lang="en">voice leading</span>。</p>\n'
            "<!-- figure:example-a -->\n"
            '<p class="keep-together">续页谱例说明保持为独立段落。</p>\n'
        ),
        visible_text=visible,
        stylesheet=(
            "p { line-break: strict; }\n"
            ".keep-together { break-inside: avoid; }\n"
        ),
        with_figure=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--mode",
        choices=("external", "policy", "figure"),
        required=True,
    )
    args = parser.parse_args()

    output = args.output.resolve()
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)

    if args.mode == "external":
        build_external(output)
    elif args.mode == "policy":
        build_policy_cases(output)
    else:
        build_figure(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
