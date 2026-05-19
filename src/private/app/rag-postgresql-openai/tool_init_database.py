import asyncio
import logging
import os
import sys

import psycopg
from psycopg import sql


async def main() -> None:
    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"
    embedding_dimension = os.environ["EMBEDDING_DIMENSION"]
    if not embedding_dimension.isdecimal():
        raise ValueError("EMBEDDING_DIMENSION must be a positive integer.")
    async with await psycopg.AsyncConnection.connect(url) as conn:
        async with conn.cursor() as cursor:
            await cursor.execute(
                "CREATE EXTENSION IF NOT EXISTS vchord CASCADE;"
            )
            await cursor.execute(
                "CREATE EXTENSION IF NOT EXISTS pg_tokenizer CASCADE;"
            )
            await cursor.execute(
                "CREATE EXTENSION IF NOT EXISTS vchord_bm25 CASCADE;"
            )
            await conn.commit()

        async with conn.cursor() as cursor:
            await cursor.execute(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'tokenizer_catalog'
                        AND table_name = 'tokenizer'
                    ) THEN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM tokenizer_catalog.tokenizer
                            WHERE name = 'en_default'
                        ) THEN
                            PERFORM create_tokenizer('en_default', $cfg$
                                model = "llmlingua2"
                            $cfg$);
                        END IF;
                    END IF;
                END $$;
                """
            )
            await conn.commit()

        async with conn.cursor() as cursor:
            await cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS document (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    content BYTEA NOT NULL,
                    content_type CHAR(10) CHECK (
                        content_type IN ('epub', 'azw3', 'md', 'txt', 'html')
                    ),
                    metadata JSONB,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
                """
            )
            await cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS node (
                    id SERIAL PRIMARY KEY,
                    document_id INTEGER NOT NULL REFERENCES document(id) ON DELETE CASCADE,
                    content TEXT NOT NULL,
                    metadata JSONB,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
                """
            )
            await cursor.execute(
                sql.SQL(
                    """
                CREATE TABLE IF NOT EXISTS node_embedding (
                    node_id INTEGER PRIMARY KEY REFERENCES node(id) ON DELETE CASCADE,
                    embedding vector({}) NOT NULL
                )
                """
                ).format(sql.Literal(int(embedding_dimension)))
            )
            await cursor.execute(
                """
                CREATE TABLE IF NOT EXISTS node_embedding_bm25 (
                    node_id INTEGER PRIMARY KEY REFERENCES node(id) ON DELETE CASCADE,
                    embedding bm25vector NOT NULL
                )
                """
            )
            await conn.commit()

        async with conn.cursor() as cursor:
            await cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS node_embedding_embedding_idx
                ON node_embedding USING vchordrq (embedding vector_cosine_ops)
                WITH (options = $$
                residual_quantization = false

                [build.internal]
                lists = [10]
                spherical_centroids = true
                $$);
                """
            )
            await conn.commit()

        async with conn.cursor() as cursor:
            await cursor.execute(
                """
                CREATE INDEX IF NOT EXISTS node_embedding_bm25_bm25vector_idx
                ON node_embedding_bm25 USING bm25 (embedding bm25_ops);
                """
            )
            await conn.commit()


if __name__ == "__main__":
    from dotenv import load_dotenv

    load_dotenv()

    if sys.platform == "win32":
        from asyncio import WindowsSelectorEventLoopPolicy

        asyncio.set_event_loop_policy(WindowsSelectorEventLoopPolicy())

    logging.basicConfig(level=logging.INFO)

    asyncio.run(main())
