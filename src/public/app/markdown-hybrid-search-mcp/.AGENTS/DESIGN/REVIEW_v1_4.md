# Design Review — Markdown Hybrid Search MCP (v1.4)

<!-- markdownlint-disable MD013 MD036 -->

Date: 2026-01-13

Scope: This review evaluates `v1.md` only (plus general engineering constraints). It intentionally does **not** consider any prior `REVIEW_*.md` documents.

## Executive summary

The design is coherent and appropriately “minimal” for a v1: it chooses a single embedded backend (DuckDB), a single retrieval granularity (document-level), and a clear hybrid retrieval strategy (FTS + VSS + RRF). The decision to store an indexed snapshot of raw Markdown (`content_md`) is also correct for MCP ergonomics and reproducibility.

However, several items should be tightened before implementation to avoid costly rework:

- The DuckDB vector representation and query functions are underspecified; without a **fully validated** SQL/type contract, the VSS path is likely to break across platforms.
- The `fd` discovery + symlink boundary rule is directionally correct but needs a precise, testable definition of “within root” (path-segment boundary, trailing separators, Windows corner cases).
- The FTS tokenization configuration is likely to underperform for common developer tokens (notably `C++`, `C#`, `key=value`, `@decorator`) given the current `ignore` pattern and stemming/stopwords; this needs adjustment.
- Indexing can include up to 100,000 documents and embeddings are uncapped; therefore, embedding cost/time can be extremely large and should have explicit operational safety measures (visibility, progress, backoff) and clear failure modes.

With the changes recommended below, the design is implementable and should produce a robust v1.

## What is strong / low-risk

### Clear lifecycle and safety boundaries

- The “process-owned subdirectory under `--work-dir` / OS temp” rule is a good safety boundary and reduces the risk of deleting user data.
- Returning snapshot content from the index (not live reads) is the right consistency contract for MCP tools.

### Minimal-but-sufficient retrieval architecture

- Document-only indexing is a sensible v1 scope decision.
- Hybrid candidate generation + Reciprocal Rank Fusion avoids score-scale calibration problems.
- The embedding input cap (head+tail under a budget) is the simplest effective mitigation for length imbalance without chunking.

### Startup self-test requirement

- Requiring an end-to-end DuckDB self-test at startup is the correct way to avoid “it works on my machine” drift across DuckDB versions, extension availability, and SQL function shapes.

## Critical issues to resolve (should fix before coding)

### 1) DuckDB VSS/FTS contract is not concrete enough

The design correctly states that SQL is illustrative unless validated, but for v1 the implementation will quickly become brittle unless the self-test produces an authoritative, _exact_ contract that the server uses.

**Gaps / risks**

- `<VECTOR_TYPE>` is unspecified. DuckDB + `vss` typically expects a concrete vector representation (often a fixed-length array type such as `FLOAT[3072]`), but the exact type support and cast rules vary by version/extension.
- The example query uses `array_distance(embedding, :qvec::FLOAT[DIM])` while the index specifies `metric = 'cosine'`. If `array_distance` is Euclidean (L2) or otherwise not cosine-consistent, ranking may be wrong or the index may not be used.
- The design does not state how to ensure the vector index is actually used (e.g., EXPLAIN plan checks are not required).

**Recommendations**

- In the startup self-test, validate and record:
    - The exact column type used for `docs.embedding`.
    - The exact parameter binding approach for query vectors.
    - The exact distance function used that is consistent with the HNSW metric.
    - The exact SQL used to create the index and to query it.
- Make the running server reuse the _same SQL strings_ (or the same helper methods) that were validated in the self-test.
- Store the validated SQL/type/metric in `index_meta` for postmortem debugging.

### 2) Path normalization and root boundary enforcement need a precise, testable spec

The direction (“follow symlinks but enforce realpath within roots”) is correct, but correctness hinges on subtle path details.

**Gaps / risks**

- “Within root” must be path-segment-aware. A naive prefix check will misclassify `/root/a-b` as within `/root/a`.
- Roots may be provided with or without trailing separators. Normalization must handle both.
- Windows-specific path behaviors (drive letters, UNC paths, case folding, `\\?\` prefixes) can cause surprising `realpath` results.
- The `path_prefix` filter and returned `path` are defined as normalized real paths. That is consistent, but user experience may suffer (paths become machine-specific) and filters need careful semantics.

**Recommendations**

- Define “within root” as:
    - `candidate_real == root_real` OR
    - `candidate_real` has `root_real` as an ancestor directory boundary (segment boundary).
      In other words: compare path components, not raw strings.
- Normalize both roots and candidates through the same `normalized_real_path` function prior to any comparisons.
- Add tests for:
    - Trailing slash variants.
    - Prefix traps (`/a/b` vs `/a/b2`).
    - Symlinked roots and symlinked children.
    - Windows drive-letter case variants.

### 3) FTS tokenization likely harms developer-centric queries

The chosen `ignore` regex and English stemming/stopwords are plausible defaults, but the current settings conflict with the “code-aware” promise.

**Concrete concerns**

- `ignore = '([^0-9A-Za-z_\-\./:])+'` drops characters commonly needed for developer search:
    - `C++` becomes `C` (drops `+`).
    - `C#` becomes `C` (drops `#`).
    - `key=value` splits poorly (drops `=`).
    - `@decorator` loses `@`.
- Porter stemming may damage identifiers and error tokens (even if mild), and stopwords may remove meaningful short tokens in technical contexts.

**Recommendations**

- Re-evaluate FTS settings against the intended user query distribution (developer docs):
    - Expand allowed token characters to include at least `+`, `#`, `=`, and `@`.
    - Consider disabling stemming and/or stopwords for v1 if the primary goal is exact-ish developer token matching.
- If you keep stemming/stopwords, explicitly document the tradeoff and ensure snippet generation and query rewrite compensate.

### 4) Operational guardrails for embedding cost/time are incomplete

A hard discovery cap of 100,000 files is good, but it does not bound runtime/cost well enough for Azure embeddings.

**Gaps / risks**

- 100,000 embedding requests (even batched) can be extremely slow and expensive.
- The design mentions retries but does not address:
    - rate limiting,
    - maximum total time,
    - maximum total tokens,
    - partial progress reporting,
    - resume/retry semantics (v1 chooses fail-fast, which is OK but must be explicit).

**Recommendations**

Given the clarified decision to have **no embedding cap** in v1, the minimum viable safety net should shift from “hard limits” to “operator visibility + predictable behavior under pressure”:

- Make the cost visible up-front:
    - log discovered doc count before embedding starts,
    - log an estimate of total embedding input size (chars and/or approximate tokens).
- Add explicit concurrency controls:
    - max concurrent embedding requests,
    - explicit handling for rate limiting.
- Ensure logs and `stats` expose:
    - number of docs discovered,
    - number embedded successfully,
    - total embedding calls,
    - rate limit events / retries,
    - total indexing duration breakdown.

## Important improvements (high value, not strictly required)

### Clarify tool contracts and error surfaces

- `search`:
    - Define whether `top_k` is optional with a default.
    - Define the meaning of final `score` (e.g., RRF score after optional length penalty).
    - Define whether `roots/path_prefix/updated_after` are v1-supported or “future”. If v1 supports them, specify exact types and semantics (timezone handling for `updated_after`).
- `get_document`:
    - The `path` input should be defined as normalized real path; consider also accepting raw paths and normalizing them for lookup to reduce user friction.
    - Define what happens if both `doc_id` and `path` are provided.

### Determinism and reproducibility

- Store a schema/version marker in `index_meta`.
- Record:
    - DuckDB version,
    - extension versions (if available),
    - the validated SQL (see above),
    - embedding deployment name and expected dimension.

### Code block handling needs a minimal concrete spec

The design requires deterministic `code_signals` but does not define _how_.

Recommendation: define a minimal extraction rule set, for example:

- For fenced code blocks:
    - capture the info string (language),
    - extract tokens matching `[A-Za-z_][A-Za-z0-9_\-\.]*`,
    - retain common CLI flags `--[A-Za-z0-9\-]+`,
    - retain error-like patterns (e.g., `0x[0-9A-Fa-f]+`, `E\d+`, `Exception`, `Traceback`).
- Sort + deduplicate tokens to ensure stable output.

### Snippet quality

The “first token hit” window is acceptable for v1, but it will often produce weak snippets.

Recommendation: if feasible, prefer:

- Using FTS-provided match info / highlights (if available), or
- Scoring multiple candidate windows and choosing the best.

## Suggested validation / test plan

A v1 implementation should include at least:

- Unit tests for `normalized_real_path` and root containment.
- Integration tests for DuckDB self-test (FTS + VSS) running on CI.
- A small synthetic corpus test verifying:
    - code-heavy docs are discoverable via FTS,
    - prose-heavy docs are discoverable via VSS,
    - RRF produces stable merged ordering.

## Closing assessment

This is a solid v1 design with a good “minimal-but-complete” shape. Tightening the DuckDB vector contract, path boundary rules, and FTS tokenization will significantly reduce implementation risk and improve real-world retrieval quality without expanding scope.
