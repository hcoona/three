# Review — Markdown Hybrid Search MCP Design (v1)

Date: 2026-01-13

This review evaluates `v1.md` as a design specification for a minimal v1 implementation. It is intentionally strict and focuses on correctness, feasibility, and hidden operational risks.

## Overall verdict

**Verdict: Needs revisions before implementation.**

The design has a clear v1 scope (document-level, one-shot index, DuckDB-only backend, hybrid retrieval via FTS + vector) and sensible high-level defaults. However, several parts are underspecified or likely incorrect at the level of **DuckDB extension APIs and data types**, which are foundational to the system. If those are wrong, the project will fail late (during integration) rather than early.

## What is strong

- **Crisp v1 boundaries**: document-only indexing; no incremental updates; no answer synthesis.
- **Good retrieval decomposition**: FTS for exact strings (errors/commands) + embeddings for semantic recall.
- **Length-imbalance awareness**: bounding the embedding input is the right v1 move (much simpler than chunking).
- **Pragmatic fusion choice**: RRF is robust across heterogeneous score scales and avoids manual weight tuning.
- **Operational clarity**: ephemeral index lifecycle and explicit failure policy (exit on missing FTS/VSS).

## Blocking issues (must fix / clarify)

### 1) DuckDB FTS/VSS SQL shapes are likely not portable as written

The specification hard-codes several SQL details:

- `PRAGMA create_fts_index(...)` arguments and options
- `fts_main_docs.match_bm25(doc_id, :q)`
- `embedding FLOAT[DIM]`
- `array_distance(embedding, :qvec::FLOAT[DIM])`
- `CREATE INDEX ... USING HNSW (embedding) WITH (metric = 'cosine')`

These details are highly version- and extension-dependent. A strict v1 needs **one authoritative, tested query plan**. Today the design reads more like pseudocode than a contract.

**Why this is blocking:** storage/query correctness is the core of the system; any mismatch between design and DuckDB reality will force a redesign when code already exists.

**Recommended change to the design:**

- Add a short “DuckDB compatibility contract” section:
    - exact DuckDB version and extension versions/availability expectations
    - exact table schema types that are known to work (including the vector column type)
    - exact SQL queries used for candidate generation
    - an explicit statement that the implementation will run a **startup self-test** (create schema, build minimal index with 1–2 docs, run both FTS and VSS queries) and abort if any step fails.

### 2) Embedding storage type and dimension enforcement are underspecified

The design says:

- store `embedding FLOAT[DIM]`
- expect `DIM=3072` for `text-embedding-3-large`

**Concerns:**

- Storing a fixed-size array type may not be supported in the intended way in DuckDB for the VSS extension (this is implementation-specific).
- “Assert/record dimension” needs a concrete mechanism:
    - how to detect returned embedding length
    - what to do if it differs (fatal error vs warning)

**Recommended change:**

- Specify the exact storage representation (e.g., `FLOAT[]`, `LIST<FLOAT>`, or whichever is confirmed compatible).
- Make the dimension mismatch policy explicit:
    - default for v1 should be **fatal** (fail-fast) because the VSS index and query binding depend on dimensionality.

### 3) `.gitignore` semantics via `fd` need to be nailed down

The design states “always respect `.gitignore` using Git ignore semantics” and uses `fd`.

**Potential mismatch:**

- `fd` typically respects `.gitignore`, `.ignore`, and `.fdignore` (and sometimes global ignore rules) depending on flags and environment.
- “Always respect `.gitignore` even if there is no `.git/` directory” is ambiguous: does it mean “respect ignore files even outside a git repo”, or “apply git’s ignore semantics but only for `.gitignore` files present in the tree”?

**Recommended change:**

- Explicitly define which ignore sources are honored:
    - local `.gitignore` files under the roots (yes/no)
    - `.ignore` and `.fdignore` (yes/no)
    - global gitignore (yes/no)
- Add an acceptance test description (not code): e.g., a small fixture tree where an ignored `*.md` is not discovered.

### 4) Symlink boundary rule has security and UX implications

Design rule: “If a symlink/junction points outside all provided `--root` folders, the target is still indexed.”

**Implications:**

- A corpus root can inadvertently index sensitive content outside the intended boundary (e.g., a repo contains a symlink to a secrets directory).
- This also complicates filters like `roots` and `path_prefix`—what counts as “under a root” when real paths are outside?

**Recommended change:**

- Keep the behavior only if it is a deliberate product choice. Otherwise, prefer a safer default:
    - follow symlinks but **only index targets whose realpath is within at least one root**.
- If the current rule is kept, document:
    - how results report paths (original vs real)
    - how filters behave (filter on original path, real path, or both)

### 5) Failure policy is clear for indexing, unclear for query-time LLM rewrite

Indexing failure policy: retry + exit error (no partial index). Good.

But query-time behavior is not specified:

- If LLM query rewrite fails (rate limit, auth, transient), does `search` fail hard, or fall back to raw query?

**Recommended change:**

- For v1 robustness, query rewrite should be **best-effort**:
    - if rewrite fails, proceed with raw user query for both FTS and embeddings
    - record in logs that rewrite failed and was bypassed
- Only fail the query if embeddings cannot be computed (since vector retrieval depends on it).

## Non-blocking improvements (should fix)

### Duplicate line in “Unified storage”

The storage section repeats the same bullet twice. Remove duplication to avoid ambiguity during implementation.

### Snippet algorithm is too brittle for punctuation/CJK and code-like queries

Current rule: tokenize the rewritten keyword query, find first occurrence, return a centered 800-char window.

**Suggested v1 tweak:**

- Define a minimal tokenization rule:
    - split on whitespace and punctuation, keep tokens length ≥ 2
    - for CJK, consider using substring matching on the full query as a fallback
- Prefer matching against `text_all` but cap scanning cost (e.g., first N chars).

### Return contract of `search` needs precision

The response includes:

- `score` plus per-channel scores

But RRF is rank-based and does not naturally provide a “vector score” and “text score” on the same scale.

**Suggestion:**

- Define:
    - `score`: final fused score (e.g., RRF value after optional penalties)
    - `text_rank`, `text_bm25` (nullable)
    - `vec_rank`, `vec_distance` (nullable)

This makes results interpretable without pretending scores are comparable.

### Length-aware penalty is optional but not integrated

The design proposes a penalty formula but does not specify:

- where it is applied (before/after fusion)
- default $\alpha$

**Suggestion:**

- Either remove it from v1 (keep only capped embedding input), or specify:
    - apply after RRF on final score
    - set a conservative default (e.g., $\alpha \in [0.05, 0.2]$) and record it in `index_meta`.

### Embedding input construction needs deterministic rules

“Prefer title/headings, then prose blocks” is good but ambiguous.

**Suggestion:**

- Define a deterministic extraction order (e.g., H1/H2 headings + their following paragraphs) and a fallback (first N tokens of plain text).
- Specify whether links/URLs are kept or normalized.

## Observability and testability checklist

To keep v1 minimal but reliable, the design should require the following checks:

1. **Startup self-test**
    - Verify `fd` exists and is runnable.
    - Create DuckDB DB, install/load extensions, create schema.
    - Insert 1 synthetic doc + embedding; run one FTS query and one VSS query.

2. **Deterministic doc identity**
    - Define and test `normalized_real_path` normalization rules (including Windows case-folding and NFC).

3. **Edge-case docs**
    - Empty file
    - Very large file (ensure embedding input is capped)
    - File with only fenced code
    - YAML front matter

4. **Exit/cleanup**
    - Ensure temp directory is cleaned on normal exit and SIGINT/SIGTERM (best effort).

## Suggested edits to `v1.md` (high impact, low scope)

- Replace the current “DuckDB schema and query shape” with a **verified** minimal SQL set (or explicitly label it as “pseudocode; subject to validation”).
- Add a short “Query-time failure handling” section (especially LLM rewrite failure).
- Clarify ignore semantics and symlink policy as described above.
- Remove the duplicated “Unified storage” bullet.

## Recommendation

Proceed with implementation only after the DuckDB FTS/VSS contract is validated and the ignore/symlink/query-failure behaviors are explicitly specified. These changes do not expand scope but significantly reduce the risk of rework.
