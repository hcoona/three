"""This script ingests sample data into a PostgreSQL database."""

import asyncio
import logging
import os
import sys

import psycopg
from html2text import html2text
from psycopg.types.json import Json
from rag_postgresql_openai.specialized_splitter.caring_for_your_baby_and_young_child import (
    extract_boxes,
    split_section_to_subsections,
    split_subsection_into_paragraph_groups,
    split_top_level_document,
)


class CaringForYourBabyAndYoungChildProcessor:
    """Processor for the book _Caring for Your Baby and Young Child_.

    It assume you have unpacked the book with KindleUnpack into a directory.
    """

    def __init__(self, document_id: int):
        self._logger = logging.getLogger(__name__)

        self._url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"
        self._document_id = document_id

    async def __aenter__(self):
        self._conn = await psycopg.AsyncConnection.connect(self._url)
        return self

    async def __aexit__(self, exc_type, exc_value, traceback):
        if hasattr(self, "_conn") and self._conn:
            await self._conn.close()
            self._conn = None

    async def run(self, text, metadata={}) -> None:
        """Process the text."""
        result = split_top_level_document(text)
        if not result.chunks:
            return

        if not result.metadata.title:
            raise ValueError("Top level document has no title.")

        if result.metadata.type_ == "front_matter":
            if len(result.chunks) != 1:
                raise ValueError("Front matter should have exactly one chunk.")
            await self._write_node_into_database(
                text=result.chunks[0],
                metadata={
                    "title": result.metadata.title,
                    "type": result.metadata.type_,
                    **metadata,
                },
            )
            return

        for i, section in enumerate(result.chunks):
            try:
                await self._run_for_section(
                    section=section,
                    metadata={
                        "chapter_title": result.metadata.title,
                        "section_index": i,
                        **metadata,
                    },
                )
            except ValueError as e:
                self._logger.error(
                    f"ValueError for section {i} in {result.metadata.title}: {e}, section: {section}"
                )
                raise

    async def _run_for_section(
        self,
        section: str,
        metadata: dict[str, str],
    ) -> None:
        try:
            result = split_section_to_subsections(section)
        except ValueError as e:
            self._logger.error(
                f"ValueError for section: {e}, section: {section}, metadata: {metadata}"
            )
            raise

        if not result.chunks:
            raise ValueError("Section has no chunks.")

        for j, subsection in enumerate(result.chunks):
            await self._run_for_subsection(
                subsection=subsection,
                metadata={
                    "section_title": result.metadata.title,
                    "subsection_index": j,
                    **metadata,
                },
            )

    async def _run_for_subsection(
        self, subsection: str, metadata: dict[str, str]
    ) -> None:
        result = extract_boxes(subsection)

        if result.extracted_texts:
            for k, box in enumerate(result.extracted_texts):
                await self._write_node_into_database(
                    text=box,
                    metadata={
                        "box_index": k,
                        **metadata,
                    },
                )

        result2 = split_subsection_into_paragraph_groups(result.remaining_text)
        if len(result2.chunks) == 1:
            await self._write_node_into_database(
                text=result2.chunks[0],
                metadata={
                    "subsection_title": result2.metadata.title,
                    **metadata,
                },
            )
        else:
            for i, paragraph_group in enumerate(result2.chunks):
                await self._write_node_into_database(
                    text=paragraph_group,
                    metadata={
                        "subsection_title": result2.metadata.title,
                        "paragraph_group_index": i,
                        **metadata,
                    },
                )

    async def _write_node_into_database(
        self, text: str, metadata: dict[str, str]
    ) -> None:
        async with self._conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO node (document_id, content, metadata)
                VALUES (%s, %s, %s)
                """,
                (
                    self._document_id,
                    html2text(text, bodywidth=sys.maxsize).strip() + "\n",
                    Json({k: v for k, v in metadata.items() if v is not None}),
                ),
            )
            await self._conn.commit()


async def main():
    logger = logging.getLogger(__name__)
    this_directory = os.path.dirname(os.path.abspath(__file__))

    processor = CaringForYourBabyAndYoungChildProcessor(document_id=1)

    async with processor:
        for i in range(11, 52):
            filename = f"part00{i}.xhtml"
            with open(
                os.path.join(
                    this_directory,
                    "ingestion_cache",
                    "Caring for Your Baby and Young Child",
                    "mobi8",
                    "OEBPS",
                    "Text",
                    filename,
                ),
                mode="r",
                encoding="utf-8",
            ) as f:
                text = f.read()
                try:
                    await processor.run(
                        text=text,
                        metadata={
                            "filename": filename,
                        },
                    )
                except ValueError as e:
                    logger.error(f"ValueError for {filename}: {e}")


if __name__ == "__main__":
    import platform
    import warnings

    from bs4 import XMLParsedAsHTMLWarning
    from dotenv import load_dotenv

    warnings.filterwarnings("ignore", category=XMLParsedAsHTMLWarning)

    load_dotenv()

    logging.basicConfig(level=logging.INFO)

    if platform.system() == "Windows":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    asyncio.run(main())
