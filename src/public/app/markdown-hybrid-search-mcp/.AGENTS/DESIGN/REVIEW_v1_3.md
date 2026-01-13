# Design Review — Markdown Hybrid Search MCP (v1.3)

<!-- markdownlint-disable MD013 -->

Date: 2026-01-13

This review is based on:

- `.AGENTS/DESIGN/v1.md`
- `.AGENTS/DESIGN/CLARIFY_v1_0.md`

It is intentionally independent from any prior `REVIEW_*.md` documents.

User clarifications (after the initial review draft) were applied:

- `--reindex` is removed from v1; the index is fully ephemeral.
- `--work-dir` is a parent directory; the server creates a process-owned subdirectory and best-effort deletes it on exit.
- `get_document` uses a snapshot model.
- v1 may assume ASCII/English for FTS.
- v1 does not provide include/exclude glob flags; discovery uses `fd -e md`.
- LLM rewrite must translate queries to English.

## Executive summary

The v1 design is directionally solid: a single-process, ephemeral index; hybrid retrieval (FTS + vectors); strict failure policy; and a small tool surface. The choice to be document-level only and to address “long document dominance” up front is also correct for v1.

However, several parts are internally inconsistent or underspecified in ways that will likely cause implementation churn or incorrect behavior:

- The data model / schema does not support the `get_document` requirements (snapshot raw Markdown) nor the stated derived fields (`text_no_code`, `code_signals`) without additional storage decisions.
- DuckDB FTS/VSS specifics (vector type, function names) are marked illustrative, but the design relies on them for correctness; v1 needs an explicit “validated contract” section.
- The LLM rewrite stage is required and failure is fatal for `search`, yet the input/output contract (format, constraints, determinism) is not defined (including the “translate to English” requirement).

Net: with a small set of clarifications and a few targeted corrections, this can be a crisp and implementable v1.

## What the design gets right

1. **Document-level only for v1**
    - Avoids chunking complexity while still offering meaningful retrieval.

2. **Hybrid retrieval with rank fusion (RRF)**
    - RRF is a good default because it is scale-invariant across channels.

3. **Explicit handling of code blocks**
    - Indexing code for FTS but making embeddings “code-light” is a practical, high-signal tradeoff.

4. **Fail-fast storage contract + startup self-test**
    - This is critical when relying on DuckDB extensions whose availability varies by platform.

5. **Realpath boundary enforcement with symlink following**
    - This is a sensible security boundary: “follow, but do not escape roots.”

## Must-fix issues (blocking)

### 1) `get_document` snapshot requires raw Markdown storage, but the schema stores only derived text

`get_document` promises: “optionally raw Markdown content (or a content window).”

But the minimal schema proposes:

- `text_all VARCHAR` (rendered/normalized plain text)

Missing / unclear:

- Where is **raw Markdown** stored?
- If it is read from disk on demand:
    - what if the file changed after indexing?
    - does `get_document` reflect current disk or the indexed snapshot?
    - what happens if the file is deleted?

User clarification: v1 uses a **snapshot** model.

Therefore v1 must explicitly add a storage plan for raw Markdown (e.g., a `content_md` column) and define whether `text_all` is derived from the snapshot `content_md` (recommended) or produced independently.

### 2) Derived fields are described but not stored/used

The doc recommends:

- `text_no_code`
- `code_signals`
- code blocks with stable identifiers

But v1 schema does not include them. If they are not needed for v1’s behavior, remove them from the v1 data model. If they are needed (e.g., for explainability, `mode=mixed` later, or debug), define their storage now.

Minimal, consistent option:

- Store `content_md` (raw)
- Store `text_all` (plain)
- Store `text_no_code` (plain)
- Store `code_signals` (plain, compact)

Then define which columns are indexed by FTS and which are used for embeddings.

### 3) DuckDB “illustrative SQL” is not sufficient as a contract

The design correctly mandates a startup self-test. However, the rest of the design still relies on:

- the exact vector type (`<VECTOR_TYPE>`)
- the exact query functions (`fts_main_docs.match_bm25`, `array_distance` usage, casting `FLOAT[DIM]`)

In practice, DuckDB extension APIs evolve. To avoid design/implementation drift, v1 should include a short “Validated in self-test” contract section:

- exact DDL for the embedding column type
- exact index creation statement
- exact query templates used in production
- confirm that HNSW cosine requires pre-normalized vectors or not

If the contract cannot be stable across platforms, the design should say so and specify how the code adapts (e.g., runtime feature detection).

### 4) LLM query rewrite stage has no output schema

The design requires the rewrite stage and makes failures fatal for `search`.

Missing:

- the structured output contract (e.g., JSON with `keyword_query`, `semantic_query`, `keywords[]`, `language`, etc.)
- constraints (max length, forbidden content, no tool calls, etc.)
- determinism controls (temperature, seed, retry on invalid JSON)

Without a strict schema, you will spend time debugging “nearly-correct” LLM outputs.

User clarification: rewrite output MUST be in English.

Minimum needed for v1:

- define a JSON schema
- require the model to output JSON only
- validate JSON; retry with a repair prompt on parse failure
- cap lengths (recommended defaults: keyword query <= 256 chars, semantic query <= 512 chars)

Recommended v1 schema (strict JSON only):

- `keyword_query: string` (English)
- `semantic_query: string` (English)
- `keywords: string[]` (optional; English)
- `language: "en"` (constant)

## Non-blocking concerns and suggestions

### Document reading and encoding

The design does not specify:

- handling of non-UTF-8 files
- extremely large files
- IO errors

Recommendation for v1:

- read as UTF-8 with replacement (or detect encoding) and record a flag
- enforce a maximum file size to index (configurable), or at least cap the text fed to FTS/embedding

### Snippet quality

“First occurrence of any token” is easy but often produces poor snippets.

Small improvement without major complexity:

- prefer occurrences in headings first
- if using DuckDB FTS, consider using FTS-provided snippet/highlight features if available (but only if stable across platforms)

### Scoring and bias control

The optional length penalty is reasonable, but v1 should specify:

- whether the penalty is applied before or after RRF
- default `alpha`
- whether doc length is chars of `text_all` or `content_md`

If you keep it “optional”, explicitly state it is not implemented in v1 unless there is a milestone for it.

### Operational logging

Good idea to log `fd --version` and the exact invocation.

Also consider logging:

- DuckDB version
- extension versions (if exposed)
- embedding deployment name, returned dimension
- indexing duration breakdown (discovery, parse, embedding, DB insert)

## Security and privacy notes

- Root boundary enforcement on normalized real paths is the right boundary.
- If returning `path` as normalized real path, ensure it does not leak sensitive path prefixes unexpectedly. (This is typically acceptable for a local tool, but it is still worth noting.)
- If `work-dir` is user-provided, ensure deletion only removes process-owned directories.

## Recommended edits to v1.md (minimal delta)

1. **Define `--work-dir` semantics**
    - “The server will create a process-owned unique subdirectory under `--work-dir` and best-effort delete only that subdirectory on exit.”

2. **Remove `--reindex` from v1**
    - The index is ephemeral; no cross-run reuse.

3. **Specify where raw Markdown lives**
    - Add a `content_md` column (snapshot) and define snapshot consistency.

4. **Document the ASCII/English constraint explicitly**
    - If keeping an ASCII-focused tokenization strategy, state the limitation clearly.

5. **Remove include/exclude glob flags from v1**
    - Use `fd -e md` only for discovery.

6. **Add a concrete rewrite JSON schema**
    - Document the fields and validation strategy.

7. **Promote “illustrative SQL” to “validated contract”**
    - After the DuckDB self-test is implemented, the exact SQL used should be copied here (or referenced from a single source of truth).

## Suggested test plan (beyond the DuckDB self-test)

- Path normalization tests:
    - symlinks/junctions (where supported)
    - prefix boundary correctness (e.g., `/root/a` must not match `/root/ab`)
    - NFC normalization

- Markdown parsing tests:
    - fenced blocks, indented blocks, YAML front matter

- Retrieval tests:
    - FTS query returns expected docs
    - VSS query returns expected docs (synthetic vectors)
    - RRF fusion stable across missing candidates

- `get_document` tests:
    - windowing, caps, truncation flags

## Overall recommendation

Proceed with the design, but address the blocking items above before implementation begins. Most are small clarifications that will prevent rework and will make the v1 behavior consistent and testable.
