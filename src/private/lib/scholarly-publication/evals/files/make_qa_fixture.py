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

FONT_FAMILY = "QA Fixture Publication"
MISSING_ASSEMBLY_SCRIPT = "cannot locate assemble_print.py"
MISSING_PDF = "assembled fixture has no PDF"
UNTRACKED_PDF = "assembled fixture did not track its PDF"


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


def locate_assembly_script() -> Path:
    starts = [Path(__file__).resolve(), Path.cwd().resolve()]
    candidates: list[Path] = []
    for start in starts:
        for parent in (start, *start.parents):
            candidates.extend(
                (
                    parent
                    / "skills"
                    / "scholarly-print-assembly"
                    / "scripts"
                    / "assemble_print.py",
                    parent
                    / ".agents"
                    / "skills"
                    / "scholarly-print-assembly"
                    / "scripts"
                    / "assemble_print.py",
                    parent
                    / "scholarly-print-assembly"
                    / "scripts"
                    / "assemble_print.py",
                )
            )
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise FixtureError(MISSING_ASSEMBLY_SCRIPT)


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
            "psName": "QAFixturePublication-Regular",
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


def make_source_package(work: Path) -> Path:
    source = work / "inputs" / "source"
    pages = source / "pages" / "pdf-0001"
    pages.mkdir(parents=True)

    source_text = (
        "Clean source blocks provide enough text for the retained "
        "publication fixture."
    )
    blocks = pages / "blocks.json"
    write_json(
        blocks,
        {
            "schema_version": "1.0",
            "coordinate_space": "pdf-points-top-left",
            "page_id": "pdf-0001",
            "blocks": [
                {
                    "id": "pdf-0001-block-0001",
                    "source_order": 1,
                    "bbox": [36, 36, 324, 100],
                    "text": source_text,
                }
            ],
        },
    )
    svg = pages / "page.svg"
    svg.write_text(
        (
            '<svg xmlns="http://www.w3.org/2000/svg" '
            'width="360pt" height="480pt" viewBox="0 0 360 480">'
            '<rect x="36" y="180" width="288" height="120" '
            'fill="none" stroke="black"/>'
            "</svg>"
        ),
        encoding="utf-8",
    )

    source_identity = b"synthetic QA source identity\n"
    source_hash = hashlib.sha256(source_identity).hexdigest()
    manifest = source / "source-package.json"
    write_json(
        manifest,
        {
            "schema_version": "1.0",
            "source": {
                "sha256": source_hash,
                "bytes": len(source_identity),
                "rights_note": "Synthetic authorized QA evaluation fixture.",
                "page_count": 1,
            },
            "selection": {
                "pdf_pages": [1],
            },
            "coordinate_system": {
                "units": "pdf-points",
                "origin": "top-left",
                "bbox_order": "x0-y0-x1-y1",
            },
            "profiles": [],
            "pages": [
                {
                    "id": "pdf-0001",
                    "pdf_page": 1,
                    "width": 360,
                    "height": 480,
                    "rotation": 0,
                    "assets": {
                        "blocks": asset(source, blocks),
                        "svg": asset(source, svg),
                    },
                    "status": "pass",
                }
            ],
            "sections": None,
            "figure_map": None,
            "issues": [],
            "status": "pass",
        },
    )
    return manifest


def make_assembly_inputs(work: Path) -> Path:
    source_manifest = make_source_package(work)
    inputs = work / "inputs"
    title = "Clean Publication"
    paragraph = "Mechanical release evidence remains reviewable."
    visible_text = f"{title} {paragraph}"
    fragment = inputs / "fragments" / "chapter.html"
    fragment.parent.mkdir(parents=True, exist_ok=True)
    fragment.write_text(
        f"<h1>{title}</h1>\n<p>{paragraph}</p>\n",
        encoding="utf-8",
    )
    bundle = inputs / "translation-bundle.json"
    write_json(
        bundle,
        {
            "schema_version": "1.0",
            "source_package": asset(inputs, source_manifest),
            "target_language": "en",
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
                    "source_block_ids": ["pdf-0001-block-0001"],
                }
            ],
        },
    )

    font = work / "resources" / "fonts" / "fixture.ttf"
    build_font(font, visible_text)
    stylesheet = work / "resources" / "styles" / "additional.css"
    stylesheet.parent.mkdir(parents=True, exist_ok=True)
    stylesheet.write_text("p { line-height: 1.4; }\n", encoding="utf-8")

    spec = work / "assembly-spec.json"
    write_json(
        spec,
        {
            "schema_version": "1.0",
            "publication_id": "qa-fixture",
            "title": title,
            "title_language": "en",
            "language": "en",
            "source_package": "inputs/source/source-package.json",
            "translation_bundle": "inputs/translation-bundle.json",
            "fragment_order": ["chapter"],
            "figures": [],
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
            "stylesheets": ["resources/styles/additional.css"],
            "page": {
                "size": "letter",
                "margin_in": {
                    "top": 0.65,
                    "right": 0.7,
                    "bottom": 0.7,
                    "left": 0.75,
                },
            },
            "profiles": [],
        },
    )
    return spec


def update_pdf_binding(publication: Path) -> None:
    manifest_path = publication / "assembly-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    pdf_record = manifest["outputs"]["draft_pdf"]
    if not isinstance(pdf_record, dict):
        raise TypeError(MISSING_PDF)
    pdf = publication / pdf_record["path"]
    updated = asset(publication, pdf)
    manifest["outputs"]["draft_pdf"] = updated
    for index, record in enumerate(manifest["tracked_files"]):
        if record["path"] == updated["path"]:
            manifest["tracked_files"][index] = updated
            break
    else:
        raise FixtureError(UNTRACKED_PDF)
    write_json(manifest_path, manifest)


def canonicalize_pdf(publication: Path) -> None:
    manifest_path = publication / "assembly-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    pdf_record = manifest["outputs"]["draft_pdf"]
    if not isinstance(pdf_record, dict):
        raise TypeError(MISSING_PDF)
    pdf = publication / pdf_record["path"]
    canonical = pdf.with_name(".canonical-publication.pdf")

    source = fitz.open(pdf)
    target = fitz.open()
    for source_page in source:
        page = target.new_page(
            width=float(source_page.rect.width),
            height=float(source_page.rect.height),
        )
        page.show_pdf_page(page.rect, source, source_page.number)
    source.close()
    target.set_metadata(
        {
            "producer": "scholarly-publication QA fixture",
            "creationDate": "D:20000101000000Z",
            "modDate": "D:20000101000000Z",
        }
    )
    target.save(
        canonical,
        garbage=4,
        clean=True,
        deflate=True,
        no_new_id=True,
    )
    target.close()
    canonical.replace(pdf)
    update_pdf_binding(publication)


def replace_with_seeded_pdf(publication: Path) -> None:
    manifest_path = publication / "assembly-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    pdf_record = manifest["outputs"]["draft_pdf"]
    if not isinstance(pdf_record, dict):
        raise TypeError(MISSING_PDF)
    pdf = publication / pdf_record["path"]
    pdf.unlink()

    document = fitz.open()
    page = document.new_page(width=595.276, height=841.89)
    page.insert_text(
        fitz.Point(72, 96),
        "Seeded QA failure uses A4 geometry and an undeclared font.",
        fontsize=14,
    )
    page.insert_link(
        {
            "kind": fitz.LINK_URI,
            "from": fitz.Rect(72, 78, 430, 108),
            "uri": "https://example.invalid/release",
        }
    )
    document.set_metadata(
        {
            "producer": "scholarly-publication seeded QA fixture",
            "creationDate": "D:20000101000000Z",
            "modDate": "D:20000101000000Z",
        }
    )
    document.save(pdf, garbage=4, deflate=True, no_new_id=True)
    document.close()
    update_pdf_binding(publication)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--mode",
        choices=("clean", "seeded"),
        required=True,
    )
    args = parser.parse_args()

    output = args.output.resolve()
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    work = output / "_build"
    publication = output / "publication"
    try:
        spec = make_assembly_inputs(work)
        runtime = locate_assembly_script()
        run_runtime(
            runtime,
            [
                "build",
                "--spec",
                str(spec),
                "--output",
                str(publication),
            ],
        )
        run_runtime(
            runtime,
            [
                "render",
                "--html",
                str(publication / "index.html"),
                "--pdf",
                str(publication / "publication.pdf"),
            ],
        )
        if args.mode == "seeded":
            replace_with_seeded_pdf(publication)
        else:
            canonicalize_pdf(publication)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
