import asyncio
import logging
import os

import psycopg
from openai import AsyncOpenAI
from pgvector.psycopg import register_vector
from psycopg import sql
from psycopg.rows import dict_row

QUERY = "3岁儿童发烧怎么办？"


async def main() -> None:
    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"

    logger = logging.getLogger(__name__)

    openai_client = AsyncOpenAI(
        api_key=os.getenv("LITELLM_API_KEY"),
        base_url=os.getenv("LITELLM_API_BASE"),
    )

    result = await openai_client.embeddings.create(
        model=os.environ["EMBEDDING_MODEL"],
        input=QUERY,
    )
    query_embedding = result.data[0].embedding

    probe: int | None = 10

    with psycopg.connect(url) as conn:
        with conn.cursor(row_factory=dict_row) as cursor:
            register_vector(conn)
            cursor.execute(
                sql.SQL("SET LOCAL vchordrq.probes = {};").format(
                    sql.Literal(probe or "")
                )
            )
            cursor.execute(
                """
                SELECT n.id, n.content, n.metadata FROM node n
                JOIN node_embedding ne ON n.id = ne.node_id
                ORDER BY ne.embedding <=> %s::vector
                LIMIT %s
                """,
                (query_embedding, 10),
            )
            nodes = cursor.fetchall()

    for node in nodes:
        logger.info(f"Node ID: {node['id']}")
        logger.info(f"Node Content: {node['content']}")
        logger.info(f"Node Metadata: {node['metadata']}")
        logger.info("-----")

    logger.info("Done.")


if __name__ == "__main__":
    from dotenv import load_dotenv

    load_dotenv()

    logging.basicConfig(level=logging.INFO)
    logging.getLogger("httpx").setLevel(logging.WARNING)

    asyncio.run(main())
