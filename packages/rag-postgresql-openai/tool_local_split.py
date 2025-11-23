import logging
import os
import pathlib
import sys

import tiktoken
from html2text import html2text
from rag_postgresql_openai.specialized_splitter.caring_for_your_baby_and_young_child import (
    extract_boxes,
    split_section_to_subsections,
    split_subsection_into_paragraph_groups,
    split_top_level_document,
)
from slugify import slugify


class CaringForYourBabyAndYoungChildProcessor:
    def __init__(self):
        self._logger = logging.getLogger(__name__)

        self._encoding = tiktoken.get_encoding("cl100k_base")
        self._num_tokens_exceeding = {}

        root_path = os.path.dirname(os.path.abspath(__file__))
        if os.name == "nt":
            root_path = "\\\\?\\" + root_path

        self._output_directory_root = pathlib.Path(
            root_path,
            "ingestion_cache",
            "Caring for Your Baby and Young Child",
            "out",
        )

        self._output_directory_root.mkdir(parents=True, exist_ok=True)

    def run(self, text) -> None:
        """Process the text."""
        result = split_top_level_document(text)
        if not result.chunks:
            return

        if not result.metadata.title:
            raise ValueError("Top level document has no title.")

        if result.metadata.type_ == "front_matter":
            if len(result.chunks) != 1:
                raise ValueError("Front matter should have exactly one chunk.")
            self._write_files(
                self._output_directory_root,
                slugify(result.metadata.title),
                result.chunks[0],
            )
            return

        _output_directory_parent = self._output_directory_root / slugify(
            result.metadata.title
        )
        _output_directory_parent.mkdir(exist_ok=True)

        for i, section in enumerate(result.chunks):
            try:
                self._run_for_section(
                    section=section,
                    section_index=i,
                    output_parent=_output_directory_parent,
                )
            except ValueError as e:
                self._logger.error(
                    f"ValueError for section {i} in {result.metadata.title}: {e}, section: {section}"
                )
                raise

    def _run_for_section(
        self, section: str, section_index: int, output_parent: pathlib.Path
    ) -> None:
        try:
            result = split_section_to_subsections(section)
        except ValueError as e:
            self._logger.error(
                f"ValueError for section {section_index} in {output_parent}: {e}, section: {section}"
            )
            raise

        if not result.chunks:
            raise ValueError("Section has no chunks.")

        if not result.metadata.title:
            filename_base = f"section-{section_index}"
        else:
            filename_base = f"section-{section_index}_{slugify(result.metadata.title)}"

        section_output_directory = output_parent / filename_base
        section_output_directory.mkdir(exist_ok=True)

        for j, subsection in enumerate(result.chunks):
            self._run_for_subsection(
                subsection=subsection,
                subsection_index=j,
                output_parent=section_output_directory,
            )

    def _run_for_subsection(
        self, subsection: str, subsection_index: int, output_parent: pathlib.Path
    ) -> None:
        result = extract_boxes(subsection)

        if result.extracted_texts:
            for k, box in enumerate(result.extracted_texts):
                self._write_files(
                    output_directory=output_parent,
                    filename_base=f"subsection-{subsection_index}_box-{k}",
                    text=box,
                )

        result2 = split_subsection_into_paragraph_groups(result.remaining_text)
        if result2.metadata.title:
            filename_title = "_" + slugify(result2.metadata.title)
        else:
            filename_title = ""

        if len(result2.chunks) == 1:
            self._write_files(
                output_directory=output_parent,
                filename_base=f"subsection-{subsection_index}{filename_title}",
                text=result2.chunks[0],
            )
        else:
            for i, paragraph_group in enumerate(result2.chunks):
                self._write_files(
                    output_directory=output_parent,
                    filename_base=f"subsection-{subsection_index}{filename_title}_paragraph-group-{i}",
                    text=paragraph_group,
                )

    def _write_files(
        self, output_directory: pathlib.Path, filename_base: str, text: str
    ) -> None:
        with open(
            output_directory / f"{filename_base}.html",
            mode="w",
            encoding="utf-8",
        ) as out_file:
            out_file.write(text)

        with open(
            output_directory / f"{filename_base}.md",
            mode="w",
            encoding="utf-8",
        ) as out_file:
            content = html2text(text, bodywidth=sys.maxsize).strip() + "\n"
            out_file.write(content)

            num_tokens = len(self._encoding.encode(content))
            if num_tokens > 2048:
                name = str(
                    output_directory.relative_to(self._output_directory_root)
                    / f"{filename_base}.md"
                )
                self._num_tokens_exceeding[name] = num_tokens


def main():
    logger = logging.getLogger(__name__)
    this_directory = os.path.dirname(os.path.abspath(__file__))

    processor = CaringForYourBabyAndYoungChildProcessor()

    for i in range(11, 52):
        with open(
            os.path.join(
                this_directory,
                "ingestion_cache",
                "Caring for Your Baby and Young Child",
                "mobi8",
                "OEBPS",
                "Text",
                f"part00{i}.xhtml",
            ),
            mode="r",
            encoding="utf-8",
        ) as f:
            text = f.read()
            try:
                processor.run(text=text)
            except ValueError as e:
                logger.error(f"ValueError for part00{i}.xhtml: {e}")

    logger.info(
        f"Total sections exceeding 2048 tokens: {len(processor._num_tokens_exceeding)}"
    )
    for name, num_tokens in processor._num_tokens_exceeding.items():
        logger.info(f"{name}: {num_tokens} tokens")


if __name__ == "__main__":
    import warnings

    from bs4 import XMLParsedAsHTMLWarning

    warnings.filterwarnings("ignore", category=XMLParsedAsHTMLWarning)

    logging.basicConfig(level=logging.INFO)

    main()
