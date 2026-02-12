# Markdown Hybrid Search MCP — Clarifications / Decisions (for REVIEW v1_7)

This file tracks clarifications raised from `v1.md` and the subsequent decisions.

## Decisions (resolved)

### 1) Vector search filters vs HNSW usage

Decision: v1 will use an **oversampling** pattern to preserve ANN index usage.

- Candidate generation will run ANN first (HNSW), retrieving `k_vec * N` results.
- Filters (`roots`, `path_prefix`, `updated_after`) will then be applied within the same candidate-generation step.
- The final candidate list is truncated back to `k_vec`.

Rationale: this satisfies “filters are applied during candidate generation” while keeping a validated ANN plan node.

Fixed constant (v1): `N = 10`.

### 3) Structured outputs JSON schema: required fields

Decision: `keywords` is **required** and may be an empty array (`[]`).

This keeps the schema strict (all fields required; `additionalProperties: false`) while allowing “no keywords” cases.

### 4) `updated_after` strictness

Decision: v1 will accept any RFC3339 timestamp **as long as a timezone is present**.

- Inputs without timezone MUST be rejected.
- Inputs with an offset (e.g., `+08:00`) are accepted and normalized to UTC for comparison.

### 5) File size and decode failure policy

Decision: v1 is **fail-fast / no partial index**.

- Any file over the size threshold is a fatal indexing error.
- Any UTF-8 decode error is a fatal indexing error.

### 6) Azure OpenAI Responses API capability verification

Decision: v1 does **not** require proving the deployment is backed by GPT-5.2.

- A successful structured-output preflight (schema-validated JSON output) is sufficient.

### 7) Expected platform targets

Decision: v1 must support Windows from day 1.

### 8) `path_prefix` input normalization

Decision: the server will normalize inputs.

- The MCP tool may accept OS-native separators (e.g., `\` on Windows).
- The server will normalize to the same “normalized real path” representation used in the index before applying filters.

### DuckDB FTS query surface stability (confirmed)

Confirmed behavior (DuckDB stable docs):

- `PRAGMA create_fts_index(...)` creates the FTS index under a newly created schema.
- The schema name is derived from the input table name. Example: for table `main.docs`, the schema name is `fts_main_docs`.
- The index build creates a `match_bm25` retrieval macro under that schema.

Implications for v1:

- Referencing `fts_main_docs.match_bm25(...)` is consistent with documented behavior **as long as** the indexed table is `main.docs`.
- To keep this stable and simple in v1, the implementation should ensure the `docs` table is created in schema `main`.
