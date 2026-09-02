# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "pymupdf==1.26.6",
# ]
# ///

from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path

import fitz

PAGE_WIDTH = 360
PAGE_HEIGHT = 480


def write_json(path: Path, value: object) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def add_vector_example(page: fitz.Page, page_number: int) -> None:
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
    page.draw_circle(
        fitz.Point(120 + (page_number * 40), 266),
        5,
        color=(0, 0, 0),
        fill=(0, 0, 0),
    )


def add_mixed_text(page: fitz.Page, page_number: int) -> None:
    page.insert_text(
        fitz.Point(36, 54),
        f"Example {page_number}: vector cadence and source label",
        fontsize=11,
        fontname="helv",
    )
    page.insert_text(
        fitz.Point(36, 82),
        f"第{page_number}页：谱例、终止式与中文注释",
        fontsize=11,
        fontname="china-s",
    )
    page.insert_text(
        fitz.Point(36, 110),
        "Retained English term: voice leading.",
        fontsize=10,
        fontname="helv",
    )


def write_maps(output: Path) -> None:
    write_json(
        output / "sections.json",
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
        output / "figures.json",
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


def build_pdf(output: Path, mode: str) -> None:
    document = fitz.open()
    for page_number in (1, 2):
        page = document.new_page(width=PAGE_WIDTH, height=PAGE_HEIGHT)
        if mode == "sparse":
            page.insert_text(
                fitz.Point(36, 54),
                f"A{page_number}",
                fontsize=11,
                fontname="helv",
            )
        else:
            add_mixed_text(page, page_number)
        add_vector_example(page, page_number)

    if mode == "unsafe":
        document.embfile_add(
            "embedded-note.txt",
            b"Synthetic attachment for an unsafe-source evaluation.\n",
        )

    document.set_metadata(
        {
            "producer": "scholarly-publication evaluation fixture",
            "creationDate": "D:20000101000000Z",
            "modDate": "D:20000101000000Z",
        }
    )
    document.save(
        output / "source.pdf",
        garbage=4,
        deflate=True,
        no_new_id=True,
    )
    document.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--mode",
        choices=("mixed", "sparse", "unsafe", "mapped"),
        required=True,
    )
    args = parser.parse_args()

    output = args.output.resolve()
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)

    build_pdf(output, args.mode)
    if args.mode == "mapped":
        write_maps(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
