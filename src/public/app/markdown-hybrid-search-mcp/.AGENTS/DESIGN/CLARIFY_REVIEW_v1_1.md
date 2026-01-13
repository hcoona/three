# Clarify Items — Design Review (v1.1)

Date: 2026-01-13

These items need a yes/no (or short) confirmation to make the v1 design implementation-ready.

## Confirmed answers (2026-01-13)

- (1) `answer` tool in v1: **B** (retrieval-only; remove `answer` and answer synthesis from v1).
- (2) `.gitignore` applicability: **always**.
- (3) Symlink/junction boundary policy: **Indexed**.
- (4) DuckDB: **v1.4.3**, extensions: **`fts`** and **`vss`**.
- (5) Tokenizer encoding for token counting: **`cl100k_base`**.
- (6) Embedding input algorithm and limits: **agree** (use the suggested defaults).
- (7) Retrieval defaults: **agree** (use the proposed defaults).
- (8.1) `snippet` max size: **agree** (use the suggested default).
- (8.2) `get_document` content format: **raw Markdown**.
- (8.3) `get_document` content window cap: **agree** (use the suggested default).

Additional decisions landed in `v1.md`:

- Hybrid fusion: RRF only (no configurable weights).
- Embedding input cap: 6000 tokens (head 5000 + tail 1000); no over-limit splitting logic in v1.
- Discovery implementation: use `fd` for Markdown enumeration.

## 1) Is `answer` (RAG synthesis) in scope for v1?

The design document says:

- `--azure-openai-chat-deployment` is required for query rewrite **and answer synthesis**, and
- an MCP tool `answer` is part of v1.

Please confirm one option:

- **A. Yes, include `answer` in v1** (and keep chat deployment required), or
- **B. No, v1 is retrieval-only** (remove `answer` tool; keep LLM only for query rewrite, or make chat deployment optional).

## 2) `.gitignore` applicability rule

Please confirm:

- Apply `.gitignore` rules **only when** the scanned root is inside a Git repository, or
- Apply `.gitignore` rules **always**, even if there is no `.git/` directory.

If “only when in Git”, please confirm how to detect repo presence on Windows where `.git` can be a _file_ (worktree) rather than a directory.

## 3) Symlink/junction boundary policy

Traversal follows symlinks/junctions. If a symlink points outside all provided `--root` folders, should the target file be:

- **Indexed** (treat as part of corpus), or
- **Skipped** (enforce roots as hard boundaries)?

## 4) DuckDB capability pinning

Please confirm the intended DuckDB approach for v1:

- Minimum DuckDB version to target.
- Exact extension(s) to use for:
    - Full-text search (FTS)
    - Vector similarity search (and whether approximate indexing like HNSW is required)

## 5) Embedding tokenization details

For `text-embedding-3-large` token counting, please confirm:

- Which `tiktoken` encoding to use (or whether the system may approximate with a character cap).

Note: Azure deployment names are user-defined, so the implementation needs a stable mapping to an encoding.

## 6) Embedding input algorithm and limits

Please confirm the concrete defaults/limits:

- Token cap for the “length-normalized representation” (e.g., 2k/3k tokens).
- Whether to include a tail segment (head + tail) and how large.
- Max number of newline-split segments allowed if still over 8191 tokens (e.g., 32).

## 7) Retrieval defaults

Please confirm defaults:

- `top_k` default (e.g., 10)
- Candidate pool sizes: `k_text`, `k_vec` (e.g., 50 each)
- RRF constant `k` (e.g., 60)
- Whether channel weighting is needed in v1.

## 8) Output size caps and formats

Please confirm:

- Max `snippet` size (chars).
- Whether `get_document` returns:
    - raw Markdown, or
    - normalized plain text, or
    - both (with flags).
- Max content window size for `get_document`.
