"""Scenario tests for splitting the parenting book into retrieval chunks."""

from bs4 import BeautifulSoup
from rag_postgresql_openai.specialized_splitter.caring_for_your_baby_and_young_child import (  # noqa: E501
    extract_boxes,
    split_section_to_subsections,
    split_subsection_into_paragraph_groups,
    split_top_level_document,
)

# A trimmed chapter in the markup of the book's EPUB export.
CHAPTER = """
<html><body>
<h1 class="chapter-nor">CHAPTER 1</h1>
<h1 class="chapter-title">Preparing for a New Baby</h1>
<p>Few events are as exciting as a new baby.</p>
<h1 class="sec">Choosing a Pediatrician</h1>
<p>Start looking during the second trimester.</p>
<h1 class="sec1" id="questions">Questions to Ask</h1>
<p class="parast">Ask about office hours.</p>
<p>Evening visits help working parents.</p>
<div class="box"><p>Tip: Bring your insurance card.</p></div>
<p class="parast">Ask who covers emergencies.</p>
<h1 class="sec1" id="visit">The First Visit</h1>
<p class="parast">Bring your questions.</p>
<h1 class="sec">Breastfeeding</h1>
<p>Breast milk is the best food for babies.</p>
</body></html>
"""


def _text(html: str) -> str:
    return BeautifulSoup(html, "lxml").get_text(" ", strip=True)


def test_chapter_splits_down_to_paragraph_groups_and_boxes() -> None:
    """Split a chapter into sections, subsections, boxes and paragraphs."""
    chapter = split_top_level_document(CHAPTER)
    assert chapter.metadata.type_ == "chapter"
    assert chapter.metadata.title == "CHAPTER 1 Preparing for a New Baby"
    assert [_text(chunk) for chunk in chapter.chunks] == [
        "Few events are as exciting as a new baby.",
        (
            "Choosing a Pediatrician Start looking during the second "
            "trimester. Questions to Ask Ask about office hours. "
            "Evening visits help working parents. "
            "Tip: Bring your insurance card. Ask who covers emergencies. "
            "The First Visit Bring your questions."
        ),
        "Breastfeeding Breast milk is the best food for babies.",
    ]

    section = split_section_to_subsections(chapter.chunks[1])
    assert section.metadata.title == "Choosing a Pediatrician"
    assert [_text(chunk) for chunk in section.chunks] == [
        "Start looking during the second trimester.",
        (
            "Questions to Ask Ask about office hours. "
            "Evening visits help working parents. "
            "Tip: Bring your insurance card. Ask who covers emergencies."
        ),
        "The First Visit Bring your questions.",
    ]

    boxes = extract_boxes(section.chunks[1])
    assert [_text(box) for box in boxes.extracted_texts] == [
        "Tip: Bring your insurance card."
    ]

    paragraphs = split_subsection_into_paragraph_groups(boxes.remaining_text)
    assert paragraphs.metadata.title == "Questions to Ask"
    assert [_text(chunk) for chunk in paragraphs.chunks] == [
        "Ask about office hours. Evening visits help working parents.",
        "Ask who covers emergencies.",
    ]


def test_part_dividers_and_front_matter_are_not_split_as_chapters() -> None:
    """Keep part dividers empty and front matter as a single chunk."""
    part = split_top_level_document(
        '<html><body><h1 class="part-no">PART I</h1></body></html>'
    )
    assert part.metadata.type_ == "part_number"
    assert part.metadata.title == "PART I"
    assert part.chunks == []

    front_matter = split_top_level_document(
        '<html><body><div><h1 class="fm-title1">Foreword</h1>'
        "<p>Welcome to parenthood.</p></div></body></html>"
    )
    assert front_matter.metadata.type_ == "front_matter"
    assert front_matter.metadata.title == "Foreword"
    assert [_text(chunk) for chunk in front_matter.chunks] == [
        "Welcome to parenthood."
    ]
