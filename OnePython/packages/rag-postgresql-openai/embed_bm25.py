import logging
import os

import psycopg
from dotenv import load_dotenv


def main() -> None:
    url = f"postgresql://{os.getenv('POSTGRES_USER')}:{os.getenv('POSTGRES_PASSWORD')}@localhost:5432/{os.getenv('POSTGRES_DB')}"
    with psycopg.connect(url) as conn:
        with conn.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO node_embedding_bm25 (node_id, embedding)
                SELECT
                    id,
                    tokenize(content, 'en_default')::bm25vector
                FROM
                    node
                WHERE
                    id NOT IN (SELECT node_id FROM node_embedding_bm25);
                """
            )
            conn.commit()


if __name__ == "__main__":
    load_dotenv()
    logging.basicConfig(level=logging.INFO)

    main()
