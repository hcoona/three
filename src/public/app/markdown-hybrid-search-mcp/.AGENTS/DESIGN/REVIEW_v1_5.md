# Review v1.5 — Markdown Hybrid Search MCP (Design v1)

<!-- markdownlint-disable MD013 -->

> Independence note: This review was performed using only `v1.md` (and general engineering knowledge). It intentionally did **not** consult any existing `REVIEW_*.md` documents.

## Executive summary

The design is directionally solid for a minimal v1: document-level snapshotting, a single local ephemeral backend (DuckDB), and hybrid retrieval with RRF are all pragmatic choices.

However, several items are **blocking or high-risk** because they affect correctness, portability, and the ability to actually run the server in typical environments:

- The design hard-biases tokenization to **English/ASCII**. This is acceptable given the clarified assumption that the corpus is primarily English, but it must be stated as an explicit product constraint.
- Users may query in any language, so the rewrite stage must reliably translate multilingual queries into English keywords/semantic queries.
- DuckDB SQL / extension behavior is described as “validated by self-test”, but the doc still embeds multiple **unstable API details** (types, functions, FTS query shape) that vary across DuckDB versions/bindings.
- Time/filter semantics and several tool contracts are underspecified (e.g., `updated_after`, what `score` means, error surface).
- Operational assumptions are mostly reasonable (outbound network available; `fd` required; DuckDB extensions may download), but they should be made explicit and aligned with the clarified auth policy (Azure CLI > Browser > Device Code; no Managed Identity).

If these are clarified and tightened while keeping the rest minimal, the design is implementable and likely to work well for the intended “developer docs” use case.

## What is strong ✅

- **Ephemeral, process-owned work directory** with explicit non-deletion of user-owned directories: good safety posture.
- **Root boundary on normalized real paths** (segment-aware ancestor checks): correct and avoids naïve prefix bugs.
- **Document snapshot semantics** (`content_md` stored and used by `get_document`): ensures consistency and auditability.
- **Startup self-test contract**: excellent for de-risking DuckDB extension and SQL shape issues.
- **RRF fusion**: robust against score-scale mismatch; minimal and effective.
- **Explicit failure policy** (“no partial index”): simplifies correctness and avoids confusing UX.

## Blocking / high-risk issues (should be resolved in the design before implementation) 🚫

### 1) Multilingual queries vs English/ASCII indexing assumptions

The design mandates:

- Query rewrite MUST translate to **English**.
- FTS tokenization is tuned to ASCII/English and may drop non-ASCII.

With the clarifications, the intended contract becomes:

- The corpus is primarily English, so English-oriented indexing is acceptable.
- Users may submit queries in any language, but the server will translate/normalize them to English for both FTS and embeddings.

This is workable, but it increases the reliability burden on the rewrite stage:

- The rewrite model must handle multilingual input robustly.
- Failure modes must be explicit (tool errors with actionable diagnostics).

**Minimal fix (still v1):** explicitly state the product constraint: “v1 targets English developer Markdown. Queries may be any language, but will be rewritten to English before retrieval. Non-English corpora are out of scope for v1.”

Additionally, since `responses` + JSON output is the intended API surface, define the rewrite as a strict JSON schema response (not prompt-only) to improve determinism.

### 2) DuckDB extension + SQL shape is still too underspecified

The doc relies on the startup self-test, which is good, but it also hardcodes:

- `embedding FLOAT[3072]`
- `array_cosine_distance(embedding, :qvec::FLOAT[3072])`
- `fts_main_docs.match_bm25(doc_id, :q)`
- requirement that `EXPLAIN` shows `HNSW_INDEX_SCAN`

In practice, DuckDB function names/types/index scan node names can differ by version, platform, or binding. The “validated by self-test” contract should be elevated from a note to an **operational invariant**:

- The server must use **exactly the same DDL and query templates** as the self-test uses.
- The self-test should produce machine-readable “capabilities” (e.g., detected functions/types) and the runtime should fail if it cannot reproduce those capabilities.

**Minimal fix:** move the “illustrative SQL” into one canonical section labeled “Validated SQL shape (must match self-test)” and ensure there is only one source of truth.

### 3) Time and filter semantics are not defined

The tool interface mentions `updated_after` but:

- What timezone is `mtime` stored in?
- What is the input format for `updated_after`?
- Is `updated_after` compared to filesystem `mtime` at indexing time or some stored timestamp?

DuckDB `TIMESTAMP` is typically timezone-naive. If you accept ISO strings with timezone, you need a policy.

**Minimal fix:** store and filter on a monotonic numeric value such as `mtime_unix_ms BIGINT` (or `mtime_ns`) and define `updated_after` as Unix ms since epoch or RFC3339 in UTC.

Concrete recommendation: persist `mtime_unix_ms BIGINT` and accept `updated_after` as RFC3339 UTC (`...Z`), comparing `mtime_unix_ms > updated_after_unix_ms`.

### 4) “No fallback” on query rewrite makes availability brittle

`search` fails if query rewrite fails, even if:

- FTS and embeddings are healthy,
- The query is already English,
- The user just wants keyword search.

This is a product choice (and the clarified direction appears to keep rewrite required), but it has high operational cost: the system becomes dependent on the rewrite model even for simple keyword search.

**Minimal fix (still respects the requirement if it is truly hard):** keep “rewrite required” but define explicit error codes and diagnostics (e.g., `rewrite_failed`, `rewrite_invalid_json`, `rewrite_over_limit`) so clients can surface actionable UX.

If the requirement can be softened, the obvious v1 fallback is: “if rewrite fails, use raw query for FTS and for embedding input.”

### 5) Runtime downloads and external binary dependencies need explicit environment assumptions

- `fd` must exist on PATH.
- DuckDB extensions may be downloaded at runtime.
- Azure Entra auth may require interactive flows.

With clarifications:

- Outbound network is available.
- Headless environments are a first-class target.
- Auth preference is Azure CLI > Interactive Browser > Device Code.
- Managed Identity should not be attempted.

**Minimal fix:** explicitly document: “v1 requires `fd` installed; requires outbound network access at startup (DuckDB extensions + Azure OpenAI).” Also codify the auth order above and ensure it is observable via logs.

## Medium-risk / quality issues (address if time permits) ⚠️

### 1) Path normalization and NFC can merge distinct files

NFC normalization improves cross-platform consistency, but on Linux it is possible (though rare) to have two distinct filenames that differ only by Unicode normalization form. NFC would collapse them and the dedup rule would drop one.

**Minimal fix:** mention this caveat explicitly; optionally include a “collision detected” warning if two distinct real paths normalize to the same canonical string.

### 2) `code_signals` extraction needs size caps

Regex-driven token extraction from fenced code blocks can produce very large outputs on big logs. Storing it as a single `VARCHAR` risks bloating the DB and slowing inserts.

**Minimal fix:** cap the number of extracted tokens (e.g., 2k) and/or cap total characters stored, and record truncation.

### 3) Indexing needs an explicit max file size rule

The clarifications state that extremely large files (e.g., > 10MB) may fail-fast.

**Minimal fix:** specify a hard threshold (e.g., 10MB), define whether the threshold is on raw bytes or UTF-8 decoded text, and define the error as a fatal indexing error (since v1 is no-partial-index).

### 4) Markdown-to-text definition is underspecified

“CommonMark-compatible parser” is a good direction, but the behavior of:

- inline code,
- links (anchor text vs URL),
- HTML blocks,
- tables,
- YAML front matter removal,

will significantly affect both FTS and embeddings.

**Minimal fix:** specify a deterministic markdown-to-text policy (even a short bullet list) to ensure consistent indexing across implementations.

### 5) Snippet generation is fragile

Using “first occurrence of any token” can yield low-quality snippets (e.g., common tokens match early). Consider:

- stopword removal for snippet token set,
- prefer longer tokens,
- or pick the best window by counting token hits within windows.

This can remain “best effort” in v1, but the doc should acknowledge the limitation.

### 6) Scoring contract is unclear

The `search` output includes `score`, but it’s not defined whether:

- it is the RRF score,
- it is length-penalized,
- whether higher is always better,
- and whether it is stable across index rebuilds.

**Minimal fix:** define `score` as “final fused ranking score (higher is better), currently RRF(+optional penalties)” and keep channel diagnostics separate.

## Consistency checks / suggested tightening edits

These are small doc-level adjustments that reduce implementation ambiguity without increasing scope:

1. **Define tool schemas precisely** (JSON input/output), including error shapes.
2. **Define `index_meta` fields** (at least: duckdb_version, extensions + versions, embedding deployment, embedding dimensions, build timestamp, file count).
3. **Define concurrency defaults** for embedding calls (e.g., max in-flight requests) and retry policy parameters (max retries, base delay, jitter).
4. **Define maximum document size behavior** (clarified: fail-fast above threshold). Right now, `get_document` caps returned chars but indexing stores full content with no cap.
5. **Define “ignore semantics” explicitly**: `fd --hidden` + ignore file behavior can surprise users. State “`fd`-specific ignore rules apply; this server does not implement its own ignore logic.”
6. **Document implementation choices** (clarified): Python + FastMCP; Azure OpenAI Responses API with JSON output; auth order Azure CLI > Browser > Device Code; no Managed Identity; no external services beyond Azure OpenAI.

## Minimal acceptance criteria for v1 implementation

To ensure the design is implementable and robust, I recommend treating these as “must pass”:

- Startup self-test passes on supported platforms (Linux at minimum; Windows if claimed).
- Root boundary rule is validated with symlink cases.
- DuckDB schema + query templates are identical between self-test and runtime.
- Embedding dimension mismatch fails fast with a clear error.
- Query rewrite JSON contract is validated strictly with deterministic settings.
- Work directory cleanup never deletes user directories and is safe under failures.
- Index build fails-fast for > 10MB files with a clear fatal error.

## Overall recommendation

Proceed with implementation **after** tightening the design on:

1. language/corpus assumptions, 2) DuckDB validated SQL shape, 3) time/filter semantics, and 4) environment/auth assumptions.

These are the main sources of “looks good on paper, breaks in production.” Fixing them now is cheap and keeps v1 minimal.
