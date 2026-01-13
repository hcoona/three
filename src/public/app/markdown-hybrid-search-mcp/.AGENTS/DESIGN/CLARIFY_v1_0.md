# Clarifications — Markdown Hybrid Search MCP (v1.0)

<!-- markdownlint-disable MD029 -->

This document records clarifications and decisions provided by the user.

## Decisions (resolved)

1. **Azure OpenAI (embeddings + LLM) and auth**
    - Use **Azure OpenAI embeddings**.
    - Use an Azure OpenAI **LLM model** (chat/completions).
    - Authentication is via **Microsoft Entra ID** (no API keys).
    - Preferred credential order: **Azure CLI** > **Interactive Browser** > **Device Code**.
    - The user will provide connection info via process args (e.g., endpoint, deployment).

    Expected arguments (names can change, intent should not):
    - `--azure-openai-endpoint <https://...>`
    - `--azure-openai-embedding-deployment <deployment>`
    - `--azure-openai-chat-deployment <deployment>`
    - `--azure-openai-api-version <YYYY-MM-DD>`
    - Optional: `--azure-tenant-id <tenant-guid>` if needed to force tenant selection.

2. **Local-only operation (except Azure OpenAI calls)**
    - All discovery, parsing, index building, and storage run locally.
    - Remote calls are limited to Azure OpenAI:
        - embeddings (required)
        - LLM (required)

3. **Single embedding model per run**
    - Use exactly one embedding model/deployment per server run.
    - No multi-model indexing.

4. **Corpus sampling statistics (from CLI sampling)**
    - Corpus roots provided by the user:
        - `C:/s/OneBranch-Customer-Wiki.v2/`
        - `C:/s/Azure-Express-Docs/src/documentation/`
    - Sampling method:
        - Enumerate up to the first 5,000 Markdown files (`*.md`, `*.markdown`) under each root.
        - Compute size stats over the enumerated files.
        - Randomly sample 200 files (from the enumerated set) and count occurrences of the Markdown fence marker ```.
        - This is a sample, not a full scan.

    - Sample results (Jan 12, 2026):
        - `C:/s/OneBranch-Customer-Wiki.v2/`
            - Sampled files: 449
            - Avg size: 8,329 bytes
            - P95 size: 26,520 bytes
            - Max size: 88,902 bytes
            - Fence-marker prevalence: 61.5% of sampled files contain at least one ```
            - Avg fence markers per sampled file: 7.2
        - `C:/s/Azure-Express-Docs/src/documentation/`
            - Sampled files: 532
            - Avg size: 5,934 bytes
            - P95 size: 19,567 bytes
            - Max size: 88,065 bytes
            - Fence-marker prevalence: 73.5% of sampled files contain at least one ```
            - Avg fence markers per sampled file: 6.35

5. **Index build & updates**
    - Build the index once at server startup.
    - Do not update/rebuild during subsequent queries.

6. **Concurrency**
    - No specific concurrency/QPS requirement.

7. **Index storage technology**
    - Acceptable options include SQLite, a specialized embedded vector DB, or DuckDB.
    - DuckDB is attractive because it may provide a more unified solution.

8. **Unified solution preference**
    - A unified solution is preferred (e.g., DuckDB for both text and vector search).
    - Split-store is acceptable if needed, but avoid it unless it materially simplifies packaging.

9. **Retrieval granularity**
    - Return results at **whole-document** level only.
    - Do not implement chunk-level indexing/retrieval for v1.

10. **Code blocks (agreed approach)**
    - **Full-text search**: index the full document text including code blocks so users can find API names, error messages, and commands.
    - **Vector search**:
        - Default: compute embeddings from a _code-light representation_ of the document (remove fenced code blocks, or strongly down-weight them).
        - Additionally store deterministic **code signals** extracted from code blocks (language, commands, flags, error strings, identifiers).
        - For code-heavy queries, allow an explicit query option (future MCP parameter) such as `mode: {"docs","code","mixed"}`.
            - `docs`: embed `text_no_code` only.
            - `mixed`: embed `text_no_code + code_signals`.
            - `code`: embed `code_signals` (and rely more on full-text for exact matches).
    - Do not replace code blocks with a generic placeholder only, because that tends to destroy useful semantic signals and can increase false similarity.

11. **Security / boundaries / lifecycle**
    - The index is **ephemeral**: store it under a temporary directory.
    - Delete the temporary storage automatically when the process exits.
    - The index is one-time and does not need cross-process reuse.

12. **LLM usage in v1 (minimal, but required)**
    - Azure OpenAI chat/completions is **required** for v1.
    - To keep the first system minimal, the required LLM feature is **query rewrite/expansion**.
    - Answer synthesis is out of scope for v1 unless explicitly added later.

13. **Storage backend decision (no fallback)**
    - Use **DuckDB** as the only supported storage backend for v1.
    - It is acceptable to download/enable DuckDB extensions at runtime if required.
    - If DuckDB FTS/vector capabilities cannot be enabled/used, the server should **exit with an error**.

14. **Discovery ignore rules and symlink policy**
    - Follow symlinks/junctions during traversal.
    - Always ignore the `.git/` directory.
    - Always respect ignore rules using Git ignore semantics (even if there is no `.git/` directory).
    - Deduplicate documents by **normalized real path**.

15. **Embedding model**
    - The embedding deployment must be backed by **text-embedding-3-large**.

16. **Index build failure policy**
    - Retry Azure OpenAI calls several times.
    - If indexing (e.g., embeddings) still fails after retries, **exit with error**.
    - Do not produce a partial index for v1.

17. **Embedding context limit and over-limit handling (v1)**
    - The embedding model context length is **8191 tokens**.
        - V1 avoids over-limit inputs by capping the embedding input to a fixed token budget below the model limit.
          Therefore, v1 does not require newline-boundary splitting and multi-segment aggregation.

18. **Retry policy detail (jitter)**
    - Add jitter to exponential backoff during retries.

19. **`.gitignore` applicability (minimal)**
    - Apply ignore rules always.

## Still-open items (need confirmation)

None.
