# Clarifications Resolved — Design Review (v1.3)

Date: 2026-01-13

This document records the clarifications provided by the user in response to the v1.3 design review questions.

## Resolved decisions

1. **`--work-dir` ownership and deletion semantics**
    - The server MUST create a unique, process-owned subdirectory under `--work-dir`.
    - The server MUST attempt best-effort deletion of that process-owned subdirectory on exit.
    - The server MUST NOT delete user-owned directories.

2. **`--reindex`**
    - Remove `--reindex` from v1.
    - The v1 server behavior is a fully ephemeral index (build at startup; no cross-run reuse).

3. **`get_document` source of truth**
    - Use a **snapshot** model.
    - `get_document` returns content from the indexed snapshot (not live reads).

4. **Full-text tokenization / Unicode assumption**
    - v1 MAY assume the corpus and queries are **ASCII/English**.

5. **Discovery filters (`--include-glob` / `--exclude-glob`)**
    - Do not provide include/exclude glob parameters in v1.
    - Use `fd -e md` for discovery.

6. **LLM rewrite language**
    - The rewrite stage MUST translate the query to **English**.

## Estimated defaults (not user-confirmed)

The following values were not explicitly confirmed and are proposed as reasonable v1 defaults.

1. **Rewrite output format**
    - The rewrite stage returns JSON only (no surrounding prose).
    - Proposed schema:
        - `keyword_query: string` (max 256 chars)
        - `semantic_query: string` (max 512 chars)
        - `keywords: string[]` (0..16 items; each max 32 chars)
        - `language: "en"` (constant)

2. **Determinism and safety controls**
    - Set temperature to 0 (or the lowest supported value).
    - Validate JSON strictly; on invalid JSON, retry with a repair prompt up to 2 times.
    - Reject outputs exceeding the length caps.
