# Clarifications — Review Follow-ups (v1.2)

This file records the decisions provided by the user in response to the review follow-ups.

## 1) Symlink boundary policy

**Decision:** Option B.

- Follow symlinks/junctions during discovery.
- Only index a file if its `realpath` is within at least one provided `--root`.

**Path semantics:** use **real path**.

- The `path` stored and returned by the index should be the normalized real path (per the `normalized_real_path` rules).
- Filters such as `roots` and `path_prefix` apply to the normalized real path.

## 2) Ignore semantics scope

**Decision:** delegate ignore behavior to `fd`.

- The server should rely on `fd`'s ignore behavior (as implemented by the installed `fd` version) rather than re-implementing Git ignore semantics.
- The design/implementation should record `fd --version` (or equivalent) in logs for troubleshooting and reproducibility.

## 3) Query-time LLM rewrite failure policy

**Decision:** Option B.

- If the LLM query rewrite/expansion step fails for a query, `search` should fail the query and return a tool error.

## 4) DuckDB extension availability strategy

**Decision:** Option A.

- If DuckDB extensions cannot be downloaded/installed/loaded at runtime, the server should fail fast with a clear error.
- No offline mode is required for v1.

## 5) Embedding model enforcement

**Decision:** Option A.

- If embeddings returned by the deployment do not match the expected dimension (e.g., 3072 for `text-embedding-3-large`), v1 should fail fast.
