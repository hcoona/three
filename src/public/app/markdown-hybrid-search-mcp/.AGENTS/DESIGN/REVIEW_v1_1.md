# Design Review — Markdown Hybrid Search MCP (v1.1)

Date: 2026-01-13

Scope: Review of `v1.md` only (independent design review). This review focuses on correctness, feasibility, and “implementation-ready” specificity for a minimal v1.

## Executive summary

The design has a strong, minimal core: _document-level_ indexing, a _one-shot ephemeral_ local index, and _hybrid retrieval_ combining full-text and embeddings. The document is clear about non-goals and emphasizes bias control for long documents.

Status update: the previously identified “must-fix” items have been addressed directly in `v1.md` (RRF-only fusion, pinned DuckDB + concrete SQL/query shape, `fd`-based discovery with guardrails, simplified embedding input capping, explicit `doc_id` definition, snippet rules, and retrieval defaults).

## What’s solid

- **Clear v1 scope**: document-level only; no incremental updates; ephemeral index lifetime.
- **Pragmatic hybrid strategy**: candidate generation per channel + RRF fusion is robust and avoids score-scale headaches.
- **Code-aware stance**: indexing code blocks for full-text while keeping embeddings code-light is a good tradeoff for Markdown corpora.
- **Failure policy is explicit**: retry + fail-fast, no partial index.

## Must-fix issues (blocking / high risk)

### 1) LLM scope must remain minimal

Now that v1 is **retrieval-only** (no `answer` tool), the LLM usage should remain strictly **query rewrite/expansion**.

**Recommendation**: Keep the rewrite prompt contract tiny and deterministic (inputs/outputs, max tokens, and failure fallback behavior) to avoid introducing a second “ranking model” by accident.

### 2) DuckDB “FTS + vector” query shape still needs a concrete spec

The design assumes DuckDB can provide:

- **Full-text search** (BM25-style), and
- **Vector similarity search** (ideally indexed, not brute-force).

In practice, DuckDB capabilities depend on:

- DuckDB version,
- Which extension(s) are used (e.g., `fts`, `vss`, `hnsw`, etc.), and
- Platform packaging constraints (Windows in particular).

**Risk**: Implementation stalls late because the chosen extension combination is unavailable, unstable, or incompatible across platforms.

**Status update**: Addressed in `v1.md` with a concrete schema, `PRAGMA create_fts_index(...)` + `match_bm25(...)` query shape, and `vss` HNSW index/query shape.

### 3) Discovery semantics still need guardrails (symlink cycles)

`v1.md` says:

- Always ignore `.git/`.
- Respect `.gitignore` during traversal.
- Follow symlinks/junctions.
- Deduplicate by normalized real path.

Key remaining missing pieces:

- How to avoid cycles when following symlinks/junctions.
- Precedence between `.gitignore` and `--include-glob/--exclude-glob`.

**Status update**:

- `.gitignore` is always applied.
- If a symlink/junction points outside all provided roots, the target is still indexed.

**Status update**: Addressed in `v1.md` by specifying `fd`-based discovery with `--follow`, `.gitignore` always, `--exclude .git`, and a hard max-files guardrail.

### 4) Embedding input construction has conflicting mechanisms

The design includes both:

- **Length-normalized embedding input** (“do not embed entire file; cap tokens/chars”), and
- **Over-limit splitting** at newline boundaries + segment aggregation.

If length-normalization is effective, over-limit splitting should rarely trigger. If splitting is a core mechanism, “no chunking” needs clarification.

**Status update**: Addressed in `v1.md` by specifying a single deterministic embedding input builder with a fixed token cap (below the model limit), removing over-limit splitting logic for v1.

### 5) “Configurable weights” vs RRF

The goals mention “configurable weights”, but the hybrid section specifies RRF, which is fundamentally rank-based and does not require score weights.

**Status update**: v1 standardizes on RRF and removes the “weights” goal.

## Important but non-blocking issues (should address soon)

### MCP tool contracts need concrete limits

For `search` and `get_document`, v1 should specify:

- Max bytes/chars returned for `snippet` and for full content windows.
- Whether `get_document` returns raw Markdown or normalized text.
- `doc_id` format and stability guarantee.

**Status update**: `get_document` returns raw Markdown.

**Status update**: Addressed in `v1.md` with explicit output caps, windowing parameters, and a stable `doc_id` definition.

### Snippet generation is hand-wavy

“Snippet derived from `text_all` around best-matching region” is not enough to implement.

**Recommendation**:

- For FTS: use match offsets/snippet functionality if available.
- For vector: use a cheap heuristic (e.g., pick the paragraph with highest BM25 score for expanded keywords; or the first heading section).
- Define a deterministic fallback.

### Determinism and normalization details

- “Normalized real path” needs platform rules (case-folding on Windows, Unicode normalization, path separator canonicalization).
- Hashing scheme should specify hash function (recommend SHA-256) and encoding.

### Operational behavior: ephemeral deletion is best-effort

“Delete temporary storage automatically when the process exits” cannot be guaranteed on crashes or SIGKILL.

**Recommendation**: Document as “best-effort cleanup”, and optionally add a debug flag (e.g., `--keep-work-dir`) for troubleshooting.

### Privacy and prompt-injection

If `answer` is included:

- Retrieved documents may contain instructions; ensure the model is told to treat them as untrusted.
- Ensure only bounded snippets are sent.

**Recommendation**: Add a short “prompt safety” subsection if answer synthesis remains in scope.

## Suggested explicit defaults (to make v1 implementation-ready)

- Discovery:
    - Default include: `**/*.md`, `**/*.markdown`
    - Default exclude: `.git/**`
    - Max files indexed per run: e.g., 100k (configurable)

- Retrieval:
    - `top_k` default: 10
    - Candidate pools: `k_text = 50`, `k_vec = 50`
    - RRF constant: `k = 60`

- Embeddings:
    - Embedding input token cap for normalized representation: e.g., 2,000–3,000 tokens
    - Max segments when splitting: e.g., 32
    - Retry policy: max attempts (e.g., 6), exponential base (e.g., 2), jitter strategy

Note: the defaults have been written into `v1.md` (including `top_k`, `k_text`, `k_vec`, RRF `k`, snippet cap, and `get_document` cap).

## Testability checklist (recommend adding to v1)

- Deterministic corpus snapshot test: same inputs → same `doc_id` set.
- `.gitignore` behavior test matrix: repo root present vs absent; nested git repos; `.git` as file.
- Symlink cycle test: ensure traversal terminates and dedup works.
- Markdown parsing tests:
    - fenced code blocks
    - indented code blocks
    - front matter
    - tables/links
- Retrieval correctness:
    - keyword query hits code block identifiers
    - semantic query hits prose even with long docs

## Final verdict

Proceedable after tightening a few key decisions. The design intent is good and minimal, but v1 needs several explicit “hard edges” (DuckDB capability pinning, discovery semantics, embedding input algorithm, and MCP tool limits) to avoid implementation churn.
