# Design Review — Markdown Hybrid Search MCP (v1.8)

<!-- markdownlint-disable MD013 -->

This review evaluates `v1.md` as a standalone design document.

- I did **not** read any prior `REVIEW_*.md` documents.
- I treated `CLARIFY_v1_0.md` as optional background only.

## Executive summary

The design is directionally strong: it is intentionally minimal, document-level only, and has a clear “fail fast” posture around storage capabilities (DuckDB FTS/VSS) and LLM structured outputs. The core architecture (fd discovery → snapshot parse → (FTS + VSS) → RRF fusion) is reasonable for a first version.

However, there are several **spec gaps and internal tensions** that will likely cause rework during implementation unless resolved in the design now:

1. **“Validated SQL invariant” is underspecified.** The document requires that runtime DDL/query templates exactly match the startup self-test, but it does not define the canonical SQL templates (especially with filters) in a machine-copyable way.
2. **Filter-in-candidate requirement conflicts with ANN oversampling semantics unless the exact query shape is defined.** The design must spell out the precise DuckDB SQL templates that preserve HNSW usage while still applying `roots/path_prefix/updated_after` inside the candidate query.
3. **FTS query shape may accidentally compute scores for all documents.** Depending on the DuckDB FTS extension API, the suggested pattern could degrade to a scan or a “score-every-row” execution. The design should lock in a tested query shape and define a plan validation strategy.
4. **Path and timestamp normalization rules need sharper, implementable definitions.** The design is close, but it must clarify several Windows and RFC3339 parsing edge cases to avoid “works on my machine” bugs.

If the above are addressed, the design is implementable and a good v1 baseline.

## What is working well

- **Clear scope control:** document-level only; no incremental updates; no chunking; no multimodal.
- **Fail-fast philosophy:** startup self-test for DuckDB extensions and a structured-output preflight for rewrite.
- **Reasonable hybrid strategy:** candidate generation + RRF avoids score-scale tuning.
- **Snapshot consistency:** `get_document` returns indexed content rather than live reads.
- **Symlink boundary rule:** explicitly defined and avoids naive prefix bugs.

## Major issues (must address before implementation)

### 1) “Validated SQL invariant” needs a concrete, authoritative set of templates

The design requires:

- the startup self-test validates DDL and query shapes, and
- the running server must use the exact same templates.

But the document currently provides “illustrative” SQL fragments embedded inline, and **does not specify**:

- the exact table DDL (including constraints and types that are known to work),
- the exact FTS index creation statement, including schema/table naming conventions,
- the exact FTS candidate query template with filters,
- the exact VSS candidate query template with oversampling and filters,
- how parameters are bound (positional vs named),
- how the server verifies that the actual query uses HNSW index access.

**Recommendation:** Add a dedicated section “Validated DDL & Query Templates (v1)” containing:

- Canonical `CREATE TABLE` for `main.docs` and `main.index_meta`.
- Canonical `PRAGMA create_fts_index(...)` statement.
- Canonical `CREATE INDEX ... USING HNSW` statement.
- Canonical FTS candidate query template with filters.
- Canonical VSS candidate query template with oversampling and filters.

This section should be copy/pasteable as strings, because these templates become part of the compatibility contract.

### 2) Filter semantics vs ANN oversampling require an explicit query shape

The design mandates:

- HNSW usage, and
- `roots/path_prefix/updated_after` applied “inside both candidate queries”.

ANN oversampling inherently does “ANN first, then filter”, but that can still be “inside SQL” if the filtering occurs in an outer query over the oversampled set.

**What must be specified:** the exact shape that the self-test validates.

Example requirements to resolve in the design (not necessarily in code yet):

- Whether filters are applied:
    - **after** ANN oversampling (common and index-friendly), or
    - **before** ANN selection (often breaks ANN index usage), or
    - via some hybrid method.
- If filters are applied after oversampling, define what happens if filters drop results below `k_vec`:
    - increase oversample factor and re-run (loop), or
    - accept fewer candidates, or
    - prefilter via a “candidate set” table and accept scan.

**Recommendation:** For v1, keep it deterministic and simple:

- Validate a single query template: ANN oversample → SQL-level filter → re-order by distance → `LIMIT k_vec`.
- If fewer than `k_vec` remain, return fewer vector candidates (and rely on FTS + fusion). Document this explicitly.

### 3) FTS query template may be inefficient or planner-dependent

The current candidate query sketch:

- computes `match_bm25(doc_id, :q)` inside a subquery over `main.docs`, then filters where score is not null.

This is risky because it _can_ devolve into “compute score for every row” depending on how the FTS extension is implemented.

**Recommendation:**

- In the self-test, validate not just correctness but also an execution strategy that actually uses the FTS index.
- If DuckDB’s FTS extension provides a query function that returns matches directly (instead of scoring a full scan), use that and lock it in as the validated template.
- If filters must be applied, define how they are applied without forcing a full score-every-row pass.

At minimum, the design should declare which plan nodes / operators are considered acceptable for FTS (“index-backed match”), and which indicate an unacceptable scan.

### 4) Path normalization and boundary checks need sharper edge-case handling

The design is explicit about normalized real paths, but it should clarify:

- How to handle Windows UNC paths and device paths.
- Whether to normalize trailing slashes for roots and prefixes.
- How to handle case-folding on Windows: `normcase` is referenced, but define the exact behavior (e.g., full-path lowercasing vs OS-driven case normalization).
- How to compare paths segment-wise in SQL:
    - `starts_with(path, prefix || '/')` is preferable to `LIKE` because it avoids escaping `%` and `_`.

**Recommendation:**

- Define a single function-like specification (pseudocode is fine) for:
    - `normalized_real_path(path: str) -> str`
    - `is_within_root(candidate: str, root: str) -> bool`
    - `normalize_prefix(prefix: str) -> str` (may not exist on disk)

…and require both index-time and query-time normalization to use these exact semantics.

### 5) RFC3339 parsing details should be pinned down

The design requires timestamps with timezone and normalization to UTC, which is good.

But implementation pitfalls are common:

- “Z” suffix handling (`2026-01-13T12:34:56Z`).
- Fractional seconds.
- Strict rejection of timestamps without timezone.

**Recommendation:** Specify one strict parsing strategy (library or custom) that:

- accepts RFC3339 including `Z` and offsets,
- rejects missing timezone,
- normalizes to UTC unix ms.

## Medium issues (should address soon)

### 1) Snippet construction should use `keywords` output rather than re-tokenizing keyword query

The rewrite stage already returns a bounded list `keywords: string[]`.

Using that list for snippet matching is:

- more deterministic,
- less sensitive to punctuation/tokenization,
- naturally bounded (0..16).

**Recommendation:** Define snippet matching as “first hit of any keyword in `keywords` (case-insensitive)” and fall back to tokenizing `keyword_query` only if `keywords` is empty.

### 2) Encoding policy is strict; consider a pragmatic UTF-8 BOM allowance

The design mandates UTF-8 decoding and fatal errors on decode failure. That is consistent with fail-fast, but real-world Markdown corpora sometimes include UTF-8 with BOM.

**Recommendation:** Accept UTF-8 with BOM (strip BOM) while still failing on other decode errors.

### 3) Large-file fatal threshold should be explicitly justified and observable

A hard “>10MB is fatal” rule is acceptable for v1, but it needs:

- explicit justification (cost control + memory), and
- clear error messages that identify the offending path and size.

Optionally, document whether the threshold is a constant or a CLI parameter (v1 can keep it constant).

### 4) Vector dimension contract should include explicit runtime checks

The design states 3072 dimensions.

**Recommendation:** Require:

- embedding response length check at index time,
- query embedding length check at query time,
- a fatal startup error if the self-test validates a different dimension.

### 5) “No incremental updates” implies snapshot staleness; surface it in `stats`

Users will otherwise assume results reflect live files.

**Recommendation:** Add explicit `indexed_at` and `roots` to `stats`, and include doc count + total bytes.

## Minor issues / editorial improvements

- The document mixes “v1”, “v1.0”, and “v1 (design intent)”. Consider using a single label consistently.
- CLI arguments in `v1.md` use `--azure-openai-rewrite-deployment`, while the older clarification doc mentions `--azure-openai-chat-deployment`. If compatibility is desired, document accepted aliases.
- Define sensible bounds for `top_k`, `k_text`, `k_vec` (e.g., max 100 or 200) to avoid user-triggered heavy queries.
- Clarify whether `--root` accepts relative paths and how they are resolved (recommend: resolve to absolute real path at startup).

## Suggested concrete changes to `v1.md`

1. Add “Validated DDL & Query Templates (v1)” section with exact SQL.
2. Replace FTS candidate-query sketch with a self-test-validated, index-backed template.
3. Add a normative VSS candidate query template that includes oversampling + SQL-level filters + final distance sort.
4. Add normative pseudocode for path normalization and boundary checks (both Python-level and SQL-level).
5. Tighten RFC3339 parsing rules and accepted formats.
6. Define `search`, `get_document`, `stats` tool request/response JSON schemas (even if minimal).

## Final verdict

Proceeding to implementation is reasonable **only after** the design locks down the validated SQL templates and the filter + ANN query shapes. Without that, the “fail-fast self-test” contract will not actually prevent runtime surprises.
