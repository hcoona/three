# Markdown Hybrid Search MCP — Design Review (v1_7)

This review is based **only** on `v1.md`.

Update note: a small set of implementation decisions were made after this review to resolve ambiguities (e.g., oversampling constant, timestamp parsing strictness). Where relevant, this review reflects those decisions to keep the guidance actionable.

## Executive summary

The design is cohesive and intentionally minimal, with strong “fail-fast” operational guarantees (DuckDB capabilities self-test + structured-output rewrite preflight) and a clear snapshot-based consistency model.

Earlier versions of this review flagged several practical conflicts (filters vs ANN plan validation; strict schema vs optional fields; FTS object naming). Those are now resolvable with small, explicit conventions (notably: ANN oversampling + post-filtering; strict JSON schema with `keywords` as required; and a documented DuckDB FTS schema naming rule).

## What is strong / ready to implement

### Clear lifecycle + safety boundaries

- Ephemeral work directory, unique process-owned subdirectory, best-effort cleanup, and explicit “MUST NOT delete user-owned directories” is the correct safety framing.
- Snapshot semantics are clearly stated for both `search` and `get_document`.

### Deterministic identity and path correctness

- `doc_id = sha256(normalized_real_path)` is deterministic and collision-resistant.
- Root boundary enforcement using path-component comparisons (not naive string prefix) is explicitly required—good.
- Deduplication by normalized real path is correct given symlink following.

### Operational fail-fast contract

- DuckDB self-test that validates the _actual_ DDL + query templates and exits early if not supported is a good reliability pattern.
- The structured outputs preflight for rewrite similarly prevents hard-to-debug runtime errors.

### Hybrid retrieval strategy is appropriate for v1

- Candidate generation + RRF is a pragmatic approach that avoids score calibration.
- Snippet construction is simple and deterministic.

## Major issues (must address before implementation)

### 1) VSS filtering requirement vs HNSW plan validation

**Spec requires:** `roots/path_prefix/updated_after` must be applied during candidate generation, and the self-test must validate an ANN index access path (e.g., `HNSW_INDEX_SCAN`).

In many vector search engines (and likely DuckDB vss), adding predicates like `WHERE path LIKE ...` or `mtime_unix_ms > ...` can:

- force a full scan + filter, or
- disable HNSW index usage, or
- produce an execution plan that does not match the “validated shape”.

**Resolution (minimal, still v1):**

- Use an explicit **oversampling + post-filtering** pattern:
    - inner query: ANN top `k_vec * N` using HNSW,
    - outer query: apply `roots/path_prefix/updated_after` filters,
    - final limit: `k_vec`.
- Fix `N` as a constant in v1 and bake it into the validated query template.

Chosen constant: `N = 10`.

This satisfies “filters inside candidate query” while preserving ANN access and providing predictable behavior.

Implementation note: include a test case where filters exclude many docs to ensure oversampling still returns enough candidates, and ensure the plan validation still observes the intended ANN scan node.

### 2) DuckDB FTS query surface stability and schema naming

The design uses:

- `PRAGMA create_fts_index('docs', 'doc_id', 'title', 'text_all', ...)`
- Query:
  `SELECT doc_id, score FROM (SELECT *, fts_main_docs.match_bm25(doc_id, :q) AS score FROM docs) WHERE score IS NOT NULL ...`

Context7 confirmation (DuckDB stable docs): `PRAGMA create_fts_index(...)` creates the index under a schema derived from the input table name; for `main.docs` that schema is `fts_main_docs`, and the index build creates a `match_bm25` retrieval macro under that schema.

**Recommendation (keep v1 minimal):**

- Create `docs` under schema `main` explicitly to keep the generated schema name stable (`fts_main_docs`).
- Keep using the generated `match_bm25` macro as documented.
- Still validate the end-to-end query shape in the startup self-test (FTS result correctness + basic performance sanity), because FTS internals and plans can vary across versions.

### 3) Structured outputs schema strictness

The design says:

- strict schema (structured outputs subset), and
- “all fields required”, and
- `keywords` is optional.

These cannot all be simultaneously true.

**Resolution (simplest):**

- Make `keywords` **required** and allow it to be an empty array.
- Keep `additionalProperties: false`.

### 4) Azure OpenAI Responses API: fail-fast criteria and model identity

The design mandates:

- Responses API on `/openai/v1/` with `api-version=preview`,
- structured outputs “JSON schema, strict mode”,
- rewrite deployment must be GPT-5.2.

The fail-fast behavior is a good idea, but v1 should avoid depending on being able to prove which model a deployment is backed by.

**Recommendation:**

- Define “preflight pass/fail” criteria concretely:
    - request accepted,
    - output parses as JSON,
    - output validates against schema,
    - `language == "en"`,
    - length caps satisfied.
- Define “capability failure” buckets for logging:
    - auth/token failure,
    - endpoint/api-version mismatch,
    - structured output not supported,
    - schema rejected,
    - model output invalid.
- Do not require proving the backing model identity. Treat successful structured-output preflight (schema-valid JSON) as sufficient.

### 5) “No partial index” + “fatal on decode errors / one big file” is a sharp edge

Failing the entire server startup due to a single:

- > 10MB file,
- a single non-UTF-8 Markdown file,
- an embedding transient that exhausts retries,

may be acceptable for v1, but it should be explicitly justified as an operational choice (and surfaced early in logs).

**Recommendation:**

- Keep the strict policy for v1, but add:
    - very early preflight checks (file size, UTF-8 decode) before embeddings,
    - clear error messages listing the offending path(s).

## Medium priority issues (should address in v1 if possible)

### Query rewrite failure policy may be too strict for UX

Spec says: if rewrite fails, `search` returns a tool error and does not fall back to raw query.

That is consistent with the “required” rewrite stage, but expect frequent failure modes (auth expired, network, service throttling). Consider whether v1 wants:

- strict correctness (current), or
- graceful degradation (fallback to raw query) for availability.

If strict is retained, ensure error messages are actionable (“rewrite credential missing”, “structured output rejected”, etc.).

### Tokenization/locale mismatch

- FTS ignore pattern and tokenization are optimized for ASCII/English.
- Rewrite always translates query to English.

If the corpus contains non-English text, retrieval may degrade significantly.

**Recommendation:**

- Keep as-is for v1, but document clearly in `index_meta` and README: “optimized for English + code tokens; non-ASCII text may be dropped by FTS tokenization.”

### Path prefix semantics should define normalization

Spec says filters apply to normalized real path and use path-segment boundary rule.

**Recommendation:**

- Normalize `path_prefix` in the tool handler (including OS-native separators), and apply filters against the normalized real path representation used at index time.

### Snippet generation is naive but acceptable; define determinism

The snippet algorithm uses tokens from rewritten keyword query and finds first occurrence.

**Recommendation:**

- Define the token extraction rules (split on whitespace? reuse same ignore regex as FTS?).
- Otherwise snippets may feel inconsistent with FTS matches.

## Minor issues / nits

- `--root` validation: specify whether relative roots are allowed and how they are resolved (recommend: resolve to absolute, then normalized real path).
- `updated_after`: accept RFC3339 timestamps with a timezone (required); reject timestamps without timezone; normalize offsets to UTC.
- Consider storing a separate `path_display` if you want to preserve original user-facing path; currently path is normalized real path only.

## Security, privacy, and compliance considerations

- Index stores full `content_md` snapshots, which may contain secrets. The design should state:
    - the database lives under a process-owned temp dir,
    - file permissions should be restricted (best effort on POSIX),
    - logs must avoid emitting full content.
- Azure OpenAI calls transmit text content for embeddings and queries for rewrite; ensure the implementation follows the “data minimization principle” stated (send only what is necessary).

## Observability and diagnosability

The `index_meta` proposal is good. I recommend adding:

- counts of failures/retries during embedding,
- configured concurrency and retry policy parameters,
- the validated query template hash for _each_ candidate query (FTS template hash, VSS template hash),
- oversampling constant if introduced.

## Testability checklist (recommended)

1. **Path normalization and boundary tests**
    - symlink inside root, symlink pointing outside root, junction equivalent on Windows
    - casefold behavior on Windows
    - path-segment boundary correctness (`/a/b` vs `/a/b2`)

2. **DuckDB self-test**
    - validates extension load
    - validates FTS query returns expected doc
    - validates VSS query returns expected doc
    - validates plan includes approved ANN scan node

3. **Filter semantics**
    - `roots`, `path_prefix`, `updated_after` affect both FTS and VSS candidate sets

4. **Rewrite preflight**
    - schema enforcement actually rejects invalid outputs
    - deterministic output at temperature 0

## Bottom line

Proceeding with this design is reasonable, but **the VSS filtering + plan validation contradiction must be resolved explicitly** (oversampling + post-filter within the candidate query is the smallest fix). The structured output schema inconsistency should also be corrected before implementation begins.
