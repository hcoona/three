# Design Review — Markdown Hybrid Search MCP (v1.6)

<!-- markdownlint-disable MD013 -->

Date: 2026-01-13

This is a strict, independent design review of `v1.md`. It focuses on correctness, implementability, testability, operational safety, and hidden failure modes. (I intentionally did not read any `REVIEW_*.md` files.)

## Executive summary

The design is conceptually strong: a one-shot snapshot index for Markdown with hybrid retrieval (FTS + vectors) is a good fit, and the choice of RRF avoids brittle score calibration. The document is also unusually explicit about boundary rules (realpath roots), self-test invariants, and snapshot consistency.

However, a few items are underspecified or risky enough that they could derail v1 if not tightened:

- **DuckDB extension + query-shape contract is ambitious** and needs more concrete acceptance criteria around versioning and plan validation.
- **Azure OpenAI “Responses API + JSON schema”** requirement is implementable, and the decisions are now pinned (rewrite model = GPT-5.2; fail-fast at startup if structured outputs are unavailable). This reduces ambiguity, but increases the importance of a robust startup preflight.
- **Cross-platform path normalization** (Windows/UNC, case-folding, NFC) is correctly called out but needs a precise reference implementation strategy to avoid subtle root-boundary bypasses.
- **Fail-fast policy** (no partial index) combined with “>10MB file is fatal” and “unbounded discovery” could make the server fragile on real corpora.

The rest of this review lists concrete “must fix” items, “should improve” items, and a practical test plan.

## What’s strong / likely to work well

1. **Snapshot semantics are clear and consistent.** Returning data from the indexed snapshot (not live FS reads) is coherent and keeps behavior deterministic.

2. **Root boundary rule is a real security/UX improvement.** Following symlinks but requiring `realpath` to be within a provided root prevents accidental indexing outside intended corpora.

3. **RRF fusion is a good v1 choice.** It’s robust to incomparable score scales (BM25 vs cosine distance) and avoids manual weight tuning.

4. **Explicit self-test contract for DuckDB** is exactly the right idea in a “no fallback” design. If the runtime can’t reproduce the validated DDL/query templates, failing early is better than producing subtly-wrong results.

5. **Length imbalance is addressed without chunking.** The capped head+tail embedding input is a pragmatic compromise for v1.

## Must-fix design gaps / risks

### Update after clarifications (locked decisions)

The following v1 decisions were confirmed after the initial review questions:

- Rewrite model is **GPT-5.2** (`gpt-5.2`, version `2025-12-11`) via Azure OpenAI Responses API.
    - https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/responses?view=foundry-classic
- The server must **fail at startup** if **structured outputs** (JSON schema, strict mode) are not supported by the configured deployment/API.
    - https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs
- DuckDB is still “no fallback”; accept a **minimum version range** (not an exact pin), with the current minimum remaining `duckdb >= 1.4.3`.
- Markdown file decoding is **UTF-8**, and decode errors are **fatal** (fail the indexing run).
- A missing `--root` path is **fatal** at startup.
- Search filters (`roots/path_prefix/updated_after`) must be applied **inside the FTS/VSS candidate SQL**, not only post-fusion.

### 1) DuckDB “validated SQL invariant” needs a sharper compatibility envelope

The design requires:

- `duckdb >= 1.4.3`
- `INSTALL/LOAD fts` and `vss`
- fixed-size `FLOAT[3072]` embeddings
- HNSW index usage, verified via `EXPLAIN` containing an `HNSW_INDEX_SCAN` node

This is plausible, but **high risk** unless the design documents a clear compatibility contract. Since the chosen direction is a **minimum version range** (not an exact pin), this contract should include:

- The **tested DuckDB version set** (at least a known-good version, plus the declared minimum), and how version drift is detected.
- The **exact extension versions** (or at least the minimal versions known to work).
- The **exact function names and types** used in the validated query shapes (`array_cosine_distance` vs list/array variants; parameter casting rules; whether `FLOAT[3072]` is supported as a fixed-size array type in the chosen version).

**Recommendation (must):** In `index_meta`, record:

- DuckDB engine version
- extension load status
- a stable identifier for the validated SQL templates (hash)

And define a strict acceptance criterion for the “HNSW used” check:

- Prefer checking `EXPLAIN` output for a small set of allowed node labels (since plan node strings may change).
- Consider also validating that the query does not devolve to a full scan (e.g., by asserting the plan does _not_ contain a sequential scan over `docs` for the embedding query).

### 2) Azure OpenAI “Responses API + JSON schema only” needs a minimal supported API/version statement

The design mandates schema-constrained JSON output (not prompt-only) for the rewrite stage, and treats rewrite failure as a tool error with no fallback.

This is correct for determinism, but operationally brittle unless you pin (and validate):

- Minimal supported `--azure-openai-api-version` / endpoint mode for the rewrite stage.
- The rewrite model/deployment. This is now pinned to **GPT-5.2**.

Microsoft documentation currently states:

- Structured outputs support was first added in `2024-08-01-preview` and is available in later preview APIs and the latest GA API (`2024-10-21` as listed in the structured outputs doc).
    - https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs
- The Responses API model support list includes `gpt-5.2` (version `2025-12-11`), and notes that the v1 API is required for access to the latest features.
    - https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/responses?view=foundry-classic

Given these constraints, the design should state unambiguously which API mode it uses for the rewrite stage (GA `2024-10-21` vs v1 + `api-version=preview`) and then enforce it with a startup self-test.

**Recommendation (must):** Add a compatibility statement:

- “The rewrite deployment must support JSON schema response formatting”
- “The server will validate support during startup (a micro self-test) and fail fast with a clear error if unsupported.”

Also add a note that structured outputs have schema restrictions (for example: `additionalProperties: false` must be set for objects; all fields must be required), so the server must validate the schema it sends.

- https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs#getting-started

This avoids the unpleasant UX where indexing succeeds but every `search` fails at runtime due to missing structured output capability.

### 3) Path normalization + “within root” must be implemented with component semantics, not string tricks

The doc correctly says “path-component comparison (not naive prefix test).” To actually achieve that cross-platform, you need a precise strategy:

- Normalize to `realpath`
- Convert separators to `/`
- Windows case-fold full path
- NFC normalization

**Key risk:** Python’s `os.path.commonpath` and string prefix checks are insufficient due to:

- drive letters / UNC paths
- case-folding rules
- paths that share prefixes but not boundaries (e.g., `/root/a` vs `/root/ab`)

**Recommendation (must):** Specify an implementation outline in the design:

- Represent normalized paths as a sequence of components (e.g., split on `/` after normalization)
- “Within root” iff `root_parts == cand_parts[:len(root_parts)]`

Also define behavior for:

- Roots pointing to a file vs directory
- Non-existent roots (now decided: **fatal** at startup)

### 4) Fail-fast semantics + “unbounded discovery” + “>10MB fatal” is a fragility triple

The v1 policy is “no partial index” (good for correctness), but combining that with:

- No cap on discovered files
- Fatal error on a single file > 10MB
- Required embeddings over the entire discovered set

…means a single outlier file or a huge root can make the server unusable.

**Recommendation (must):** Decide one of the following (and document it explicitly):

- **Option A (strict):** keep fatal behavior, but add a **preflight phase** that checks size limits _before_ any costly embedding starts, and surfaces the first violating file(s) clearly.
- **Option B (pragmatic v1):** keep “no partial index” but allow an explicit `--allow-skip-large-files` (default false) to avoid hard failure in real corpora.

Given the stated “minimal v1,” Option A is probably more consistent, but it needs explicit preflight.

### 5) Text/Markdown parsing and encoding handling is underspecified

The design says “CommonMark-compatible parser” and stores raw Markdown snapshot. Missing details that affect correctness:

- How to read files (assume UTF-8? BOM handling? invalid bytes?)
- How to handle extremely long lines (logs) safely
- Whether to normalize newlines

**Recommendation (must):** Specify a file decoding policy (even a strict one) and make it deterministic. For example:

- Attempt UTF-8 with BOM support; on decode error, fail indexing with an error that includes file path.

Clarification decision: indexing uses **UTF-8** decoding and treats decode errors as **fatal**.

### 6) Filter semantics need a concrete SQL-level application point

`search` mentions optional filters (`roots`, `path_prefix`, `updated_after`). For correctness, clarify whether filters apply:

- before candidate generation (FTS/VSS queries include WHERE),
- after candidate generation (filter the union), or
- both.

**Recommendation (must):** Apply filters **as early as possible** (in the FTS/VSS candidate SQL) to avoid returning results outside constraints and to keep performance predictable.

Clarification decision: filters are applied **inside the DuckDB candidate queries** (FTS and VSS), not only after union/fusion.

Also, `path_prefix` should use the same “segment boundary” logic as roots (otherwise `/a/b` matches `/a/bad`).

## Should-improve items (not blockers, but high value)

1. **Snippet generation should use the same tokenization intent as FTS.** The ignore regex is developer-token friendly; snippet search should also preserve tokens like `C++`, `--flag`, `key=value`.

2. **Deterministic tie-breaking.** RRF can produce ties; define a stable secondary order (e.g., higher text rank wins, then lower vec distance, then `doc_id`).

3. **Index storage size.** Storing `content_md`, `text_all`, `text_no_code`, and `code_signals` duplicates data. It’s acceptable in v1, but at least record total size and doc counts in `index_meta` to spot blow-ups.

4. **Rate-limit behavior.** You call out retry with exponential backoff + jitter; add an explicit limit on concurrent embedding requests (even small, e.g., 4–8) and document it as a constant.

5. **Startup UX:** log an upfront “cost estimate” (doc count + approximate token/char counts for embedding input) before making Azure calls.

## Test plan (recommended)

### Unit tests

- `normalized_real_path` behavior:
    - separators, NFC normalization, Windows case-fold simulation
- “within root” component boundary cases:
    - `/r/a` vs `/r/ab`
    - symlinked path resolving outside root
- `doc_id` stability: same normalized path => same SHA-256
- Markdown extraction:
    - fenced code blocks, language info strings
    - YAML front matter stripping
- `code_signals` determinism:
    - token extraction, sorting, de-dup, caps/truncation flags
- embedding input capping via tokenizer:
    - head/tail budgeting with `cl100k_base`

### Integration tests

- DuckDB self-test reproduces:
    - extension install/load
    - FTS index creation + query
    - VSS index creation + VSS query
    - plan validation (HNSW usage)

### End-to-end (smoke)

- Start server against a tiny corpus with 3–5 markdown files and run:
    - `stats`
    - `search` with `updated_after`, `path_prefix`
    - `get_document` by `doc_id` and by `path`

## Recommended edits to the design doc (small, high impact)

1. Add a short “Compatibility” section:
    - minimal DuckDB version(s)
    - minimal Azure OpenAI API version and rewrite-model requirements for JSON schema

2. Add a short “Determinism and tie-breaking” note in Hybrid fusion.

3. Add a “Preflight validation” step before embeddings:
    - verify `fd` exists
    - enumerate + enforce root boundary + dedupe
    - enforce file-size threshold
    - estimate embedding input totals

4. Specify file decoding policy.

## Conclusion

The v1 design is solid and implementable, and it correctly prioritizes fail-fast correctness over silent degradation. The main risk is operational brittleness around DuckDB extension/query-shape compatibility and Azure structured output support. If you tighten the compatibility envelope and add a preflight phase, you’ll substantially reduce surprises without expanding scope.
