import asyncio  # noqa: D100
import json
import logging
import os
import sys

import psycopg
from psycopg.types.json import Json

if sys.platform == "win32":
    from asyncio import WindowsSelectorEventLoopPolicy

    asyncio.set_event_loop_policy(WindowsSelectorEventLoopPolicy())


def get_args():  # noqa: ANN201, D103
    import argparse  # noqa: PLC0415

    parser = argparse.ArgumentParser(
        description="Upload a file to the document table in PostgreSQL."
    )
    parser.add_argument("filepath", help="Path to the file to upload")
    parser.add_argument(
        "--name", help="Optional name for the document (without extension)"
    )
    parser.add_argument(
        "--metadata", help="Optional JSON metadata", default="{}"
    )
    parser.add_argument("--dbname", help="Database name")
    parser.add_argument("--user", help="Database user")
    parser.add_argument("--password", help="Database password")
    parser.add_argument("--host", default="localhost", help="Database host")
    parser.add_argument("--port", default="5432", help="Database port")

    return parser.parse_args()


async def main():  # noqa: ANN201, D103
    args = get_args()

    filepath = args.filepath
    if not os.path.isfile(filepath):  # noqa: PTH113
        raise FileNotFoundError(f"File not found: {filepath}")  # noqa: EM102, TRY003

    file_ext = os.path.splitext(filepath)[1][1:].lower()  # noqa: PTH122
    valid_types = ("epub", "azw3", "md", "txt", "html")
    if file_ext not in valid_types:
        raise ValueError(  # noqa: TRY003
            f"Invalid file type: {file_ext}. Must be one of {valid_types}"  # noqa: EM102
        )

    name = (
        args.name
        if args.name
        else os.path.splitext(os.path.basename(filepath))[0]  # noqa: PTH119, PTH122
    )

    with open(filepath, "rb") as f:  # noqa: PTH123
        content = f.read()

    metadata = json.loads(args.metadata)

    dbname = args.dbname or os.getenv("POSTGRES_DB")
    user = args.user or os.getenv("POSTGRES_USER")
    password = args.password or os.getenv("POSTGRES_PASSWORD")
    host = args.host
    port = args.port

    if not all([dbname, user, password]):
        raise ValueError(  # noqa: TRY003
            "Database credentials must be provided via command line or .env file"  # noqa: E501, EM101
        )

    url = f"postgresql://{user}:{password}@{host}:{port}/{dbname}"

    async with await psycopg.AsyncConnection.connect(url) as conn:  # noqa: SIM117
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO document (name, content, content_type, metadata)
                VALUES (%s, %s, %s, %s)
                """,
                (name, content, file_ext, Json(metadata)),
            )
            print("File uploaded successfully.")  # noqa: T201


if __name__ == "__main__":
    from dotenv import load_dotenv

    load_dotenv()

    logging.basicConfig(level=logging.INFO)

    asyncio.run(main())
