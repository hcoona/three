import asyncio  # noqa: D100
import logging
import os

import psycopg
from openai import AsyncOpenAI
from psycopg.rows import dict_row


def _get_nodes_without_embedding(conn):  # noqa: ANN001, ANN202
    with conn.cursor(row_factory=dict_row) as cursor:
        cursor.execute(
            """
            SELECT id, content FROM node
            WHERE id NOT IN (SELECT node_id FROM node_embedding)
            """
        )
        return cursor.fetchall()


async def main() -> None:  # noqa: D103
    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"
    logger = logging.getLogger(__name__)

    openai_client = AsyncOpenAI(
        api_key=os.getenv("LITELLM_API_KEY"),
        base_url=os.getenv("LITELLM_API_BASE"),
    )

    with psycopg.connect(url) as conn:
        nodes = _get_nodes_without_embedding(conn)

    if not nodes:
        logger.info("No nodes to process.")
        return

    logger.info(f"Found {len(nodes)} nodes without embeddings.")  # noqa: G004

    embeddings = {}
    embedding_model = os.environ["EMBEDDING_MODEL"]
    for index, node in enumerate(nodes):
        logger.info(f"Processing node {node['id']}...")  # noqa: G004
        result = await openai_client.embeddings.create(
            model=embedding_model,
            input=node["content"],
        )

        embedding = result.data[0].embedding
        embeddings[node["id"]] = embedding

        if (index + 1) % 50 == 0:
            logger.info(f"Processed {index + 1} nodes so far...")  # noqa: G004

    logger.info("Inserting generated embeddings into the database...")

    with psycopg.connect(url) as conn, conn.cursor() as cursor:
        batch = []
        for node_id, embedding in embeddings.items():
            batch.append((node_id, embedding))
            if len(batch) == 50:  # noqa: PLR2004
                cursor.executemany(
                    """
                        INSERT INTO node_embedding (node_id, embedding)
                        VALUES (%s, %s)
                        """,
                    batch,
                )
                conn.commit()
                logger.info(
                    f"Inserted {len(batch)} records into the database."  # noqa: G004
                )
                batch = []

        # Insert any remaining records
        if batch:
            cursor.executemany(
                """
                    INSERT INTO node_embedding (node_id, embedding)
                    VALUES (%s, %s)
                    """,
                batch,
            )
            conn.commit()
            logger.info(f"Inserted {len(batch)} records into the database.")  # noqa: G004


if __name__ == "__main__":
    from dotenv import load_dotenv

    load_dotenv()

    logging.basicConfig(level=logging.INFO)
    logging.getLogger("httpx").setLevel(logging.WARNING)

    asyncio.run(main())
