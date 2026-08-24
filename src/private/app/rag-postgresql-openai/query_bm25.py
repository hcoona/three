import asyncio  # noqa: D100
import logging
import os

import psycopg
from dotenv import load_dotenv
from openai import AsyncOpenAI
from psycopg.rows import dict_row

PROMPT_TEMPLATE = """
Rewrite the following user question into a concise keyword-style search query that captures the core meaning and includes relevant synonyms or related terms. The output should be suitable for full-text search (e.g., BM25-based retrieval).

User question:
{0}

Rewritten search query in English:
"""  # noqa: E501
QUERY = "What should I do if a three-year-old child has a fever?"


async def main() -> None:  # noqa: D103
    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"

    logger = logging.getLogger(__name__)

    openai_client = AsyncOpenAI(
        api_key=os.getenv("LITELLM_API_KEY"),
        base_url=os.getenv("LITELLM_API_BASE"),
    )

    result = await openai_client.chat.completions.create(
        model="gpt-4o-mini",
        messages=[
            {
                "role": "user",
                "content": PROMPT_TEMPLATE.format(QUERY),
            }
        ],
    )
    rewritten_query_content = result.choices[0].message.content
    if rewritten_query_content is None:
        raise ValueError("OpenAI returned no rewritten query.")  # noqa: EM101, TRY003
    rewritten_query = rewritten_query_content.strip()
    logger.info(f"Rewritten query: {rewritten_query}")  # noqa: G004

    with psycopg.connect(url) as conn:  # noqa: SIM117
        with conn.cursor(row_factory=dict_row) as cursor:
            cursor.execute(
                """
                SELECT
                  n.id, n.content, n.metadata,
                  ne.embedding <&> to_bm25query('node_embedding_bm25_bm25vector_idx', tokenize(%s, 'en_default')) AS rank
                FROM node n
                JOIN node_embedding_bm25 ne ON n.id = ne.node_id
                ORDER BY rank
                LIMIT %s
                """,  # noqa: E501
                (rewritten_query, 10),
            )
            nodes = cursor.fetchall()

    for node in nodes:
        logger.info(f"Node ID: {node['id']}")  # noqa: G004
        logger.info(f"Node Content: {node['content']}")  # noqa: G004
        logger.info(f"Node Metadata: {node['metadata']}")  # noqa: G004
        logger.info("-----")

    logger.info("Done.")


if __name__ == "__main__":
    load_dotenv()
    logging.basicConfig(level=logging.INFO)
    logging.getLogger("httpx").setLevel(logging.WARNING)

    asyncio.run(main())
