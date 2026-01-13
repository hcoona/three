# Clarification Answers — Design Review Follow-ups (v1.0)

Date: 2026-01-12

This document records the user's answers to the review follow-up questions. All items below are now **resolved**.

## 1) Azure OpenAI LLM call in v1

Answer: **A (Required)**.

Practical implication: `--azure-openai-chat-deployment` is **mandatory**; the server should refuse to start (or fail during startup indexing) if chat configuration/auth is missing.

Note: Because we are building the first system, we should keep the LLM usage **minimal**. Unless explicitly expanded later, treat **query rewrite/expansion** as the required LLM feature and keep answer synthesis out of scope for v1.

## 2) Backend decision gate for Windows (DuckDB)

Answer: **Yes**, it is allowed to download/enable DuckDB extensions at runtime even if that requires network access. **No fallback**.

Practical implication: DuckDB is the **only** supported backend for v1. If DuckDB vector/FTS cannot be enabled/used, the server should exit with an error.

## 3) Discovery policy: symlinks/junctions, ignore rules, and dedup

Answer:

- Follow symlinks/junctions: **Yes**.
- Always ignore the **`.git/`** directory.
- Ignore behavior is delegated to the installed `fd` implementation.
- Enforce `--root` folders as hard boundaries on real paths: only index a file if its `realpath` is within at least one provided `--root`.
- Deduplication key: **normalized real path**.

## 4) Embedding model

Answer: Use **text-embedding-3-large**.

Practical implication: the configured Azure OpenAI embedding deployment must be backed by `text-embedding-3-large`, and the server should record this in index metadata.

Embedding context limit and scan result:

- `text-embedding-3-large` context length is **8191 tokens**.
- A heuristic scan over the previously sampled sets indicates there are Markdown files that are likely to exceed the limit (based on file size):
    - `C:\s\OneBranch-Customer-Wiki.v2`: 14 / 449 (first 5000) over the heuristic threshold
    - `C:\s\Azure-Express-Docs\src\documentation`: 9 / 532 (first 5000) over the heuristic threshold

Therefore v1 must handle over-limit inputs by splitting at newline boundaries and aggregating embeddings (rather than failing fast).

## 5) Failure policy during indexing

Answer: Retry several times; if it still fails, **exit with error**.

Practical implication: do not build a partial index for v1.

---

## Correction (2026-01-13)

The previous addendum about answer synthesis being required in v1 was incorrect.

Update: **Answer synthesis is out of scope for v1**.

Practical implication: the chat deployment (if configured/required) is used for query rewrite/expansion only.
