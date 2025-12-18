"""This module contains specialized splitters for book Caring for Your Baby and Young Child."""

from typing import Callable, Iterable, TypeVar

from bs4 import BeautifulSoup
from bs4._typing import _OneElement
from pydantic import BaseModel, Field

T = TypeVar("T")


class Metadata(BaseModel):
    """Metadata extracted from the document."""

    title: str | None = Field(description="Title of the document.")
    type_: str | None = Field(description="Type of the document.")


class SplitResult(BaseModel):
    """Result of the split operation."""

    metadata: Metadata = Field(...)
    chunks: list[str] = Field(..., description="The splitted chunks of the document.")


class ExtractResult(BaseModel):
    """Result of the extraction operation."""

    remaining_text: str = Field(..., description="The remaining text after extraction.")
    extracted_texts: list[str] = Field(
        ..., description="The extracted texts from the document."
    )


def _split_by(elements: Iterable[T], predicate: Callable[[T], bool]) -> list[str]:
    """
    Split an iterable of elements into sections. A new section starts when predicate(el) is True.
    The matching element is included at the start of its section. Leading elements before
    the first match will form the first section if any.
    """
    sections: list[str] = []
    current: list = []
    for el in elements:
        if predicate(el):
            if current:
                sections.append("".join(str(e) for e in current))
            current = [el]
        else:
            current.append(el)

    if current:
        sections.append("".join(str(e) for e in current))

    return [section.strip() for section in sections if section.strip()]


def _split_front_matter(fm_title: _OneElement) -> SplitResult:
    """
    Extract the front matter title from the document.

    This is a special case for the front matter in the book.
    The front matter title is not a chapter title, but we still want to
    return it as the metadata.

    :param fm_title: The front matter title element.
    :return: A SplitResult object with the extracted metadata and sections.
    """
    title = fm_title.get_text().strip()
    parent = fm_title.parent
    fm_title.decompose()

    return SplitResult(
        metadata=Metadata(title=title, type_="front_matter"),
        chunks=[parent.decode_contents().strip()],
    )


def _extract_chapter_title(soup) -> tuple[str, _OneElement, _OneElement]:
    """
    Extract the chapter title from the document.

    This function merges the chapter title and chapter number.

    :param title: The chapter title element.
    :param title_no: The chapter number element.
    :return: The merged chapter title.
    """
    titles = list(soup.find_all("h1", class_="chapter-title"))
    if len(titles) == 0:
        raise ValueError("No <h1 class='chapter-title'> found.")
    if len(titles) > 1:
        raise ValueError("More than one <h1 class='chapter-title'> found.")

    title = titles[0]
    title_nor = title.find_previous("h1", class_="chapter-nor")
    if title_nor is None:
        # Fallback if not found.
        title_nor = title.find_previous("h1", class_="chapter-no")
        if title_nor is None:
            raise ValueError("No <h1 class='chapter-nor'> found.")

    chapter_title = title.get_text().strip()
    chapter_title_no = title_nor.get_text().strip()
    return f"{chapter_title_no} {chapter_title}", title, title_nor


def _split_chapter_to_sections(soup: BeautifulSoup) -> SplitResult:
    title_text, title_node, title_nor_node = _extract_chapter_title(soup)

    title_node.decompose()
    title_nor_node.decompose()

    sections = _split_by(
        soup.body.children,
        lambda el: getattr(el, "name", None) == "h1" and "sec" in el.get("class", []),
    )

    return SplitResult(
        metadata=Metadata(title=title_text, type_="chapter"),
        chunks=sections,
    )


def split_top_level_document(text: str) -> SplitResult:
    """
    Split at the top-level document.

    :param text: The HTML text content of the top level document.
    :return: SplitResult object containing metadata and sections.
    :raises ValueError: If the document does not contain a valid chapter title or does not contain a valid chapter number.
    """

    # Categorize the text
    # 1. Part separator with part number
    # 2. Front matter with front matter title
    # 3. Chapter otherwise

    soup = BeautifulSoup(text, "lxml")

    part_no = soup.find("h1", class_="part-no")
    if part_no:
        # Early return if we find a part number.
        #
        # This is a special case for the part divider in the book.
        # The part number is not a chapter title, but we still want to
        # return it as the metadata.
        return SplitResult(
            metadata=Metadata(title=part_no.get_text().strip(), type_="part_number"),
            chunks=[],
        )

    fm_title = soup.find("h1", class_="fm-title1")
    if fm_title:
        # Early return if we find a front matter title.
        #
        # This is a special case for the front matter in the book.
        # The front matter title is not a chapter title, but we still want to
        # return it as the metadata.
        return _split_front_matter(fm_title)

    return _split_chapter_to_sections(soup)


def split_section_to_subsections(text: str) -> SplitResult:
    """
    Split at the section level.

    :param text: The HTML text content of the section.
    :return: SplitResult object containing metadata and chunks.
    """
    soup = BeautifulSoup(text, "lxml")

    titles = list(soup.find_all("h1", class_="sec"))
    if len(titles) > 1:
        raise ValueError("More than one <h1 class='sec'> found.")

    if len(titles) == 0:
        title_text = None
    else:
        title = titles[0]
        title_text = title.get_text().strip()
        title.decompose()

    if not soup.body:
        raise ValueError(f"No <body> found in the document: {text}.")

    subsections = _split_by(
        soup.body.children,
        lambda el: getattr(el, "name", None) == "h1"
        and "sec1" in el.get("class", [])
        and el.get("id", None) is not None,
    )

    return SplitResult(
        metadata=Metadata(title=title_text, type_="section"),
        chunks=subsections,
    )


def extract_boxes(text: str) -> ExtractResult:
    """
    Extract out `<div class="box">`, `<div class="box1">`, and `<div class="boxnobor">`.

    :param text: The HTML text content of the top level document.
    :return: ExtractResult object containing metadata and chunks.
    """

    soup = BeautifulSoup(text, "lxml")

    # Extract out <div class="box">, <div class="box1">, and <div class="boxnobor">
    boxes = []
    for box in soup.find_all("div", class_=["box", "box1", "boxnobor"]):
        boxes.append(str(box).strip())
        box.decompose()

    return ExtractResult(
        remaining_text=soup.body.decode_contents().strip(),
        extracted_texts=boxes,
    )


def split_subsection_into_paragraph_groups(text: str) -> SplitResult | None:
    """
    Split at the level 4 file.

    Rules:

    1. Split at <p class="parast"> to get paragraph sections.

    :param text: The HTML text content of the top level document.
    :return: SplitResult object containing metadata and sections.
    """

    soup = BeautifulSoup(text, "lxml")

    headers = soup.find_all("h1", attrs={"class": "sec1", "id": True})
    if len(headers) > 1:
        raise ValueError("More than one <h1 class='sec1'> found.")

    if len(headers) == 0:
        title_text = None
    else:
        title = headers[0]
        title_text = title.get_text().strip()
        title.decompose()

    chunks = _split_by(
        soup.body.children,
        lambda el: getattr(el, "name", None) == "p" and "parast" in el.get("class", []),
    )

    return SplitResult(
        metadata=Metadata(title=title_text, type_="subsection"),
        chunks=chunks,
    )
