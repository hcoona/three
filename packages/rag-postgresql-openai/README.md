# RAG PostgreSQL OpenAI

This project illustrate how to use RAG with PostgreSQL and OpenAI.

Ingestion:

1. Extract metadata from the origin document.
2. Convert the origin document into suitable format.
3. Break the document into nodes.
4. Extract the pure text from the nodes.

Ingestion TODO:

Special handling for image, table, code, etc. Extract and leave a description. Or maybe OCR and then process.

Retrieval:

1. Spawn multiple similar queries from the origin query.
2. Make BM25 search with the queries.
3. Make vector search with the origin query.
4. Re-rank results.
