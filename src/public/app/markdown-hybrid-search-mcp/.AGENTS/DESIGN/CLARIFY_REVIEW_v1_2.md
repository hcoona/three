# Clarify Questions — Review Follow-ups (v1.2)

These items require a human decision because they affect user expectations, security boundaries, or long-term compatibility.

## 1) Symlink boundary policy

Current design states: symlink/junction targets **outside** all `--root` folders are still indexed.

- Is this intentional as the default behavior?
    - Option A (current): index everything reachable via symlinks.
    - Option B (safer): index only if `realpath(target)` is within at least one root.

If Option A is kept, clarify which path is shown and used for filtering:

- Show `path` as: original discovered path vs real path?
- Filters like `roots` and `path_prefix` apply to: original path vs real path?

## 2) Ignore semantics scope

The design says “always respect `.gitignore` using Git ignore semantics” via `fd`.

Please confirm which ignore sources should be honored:

- Local `.gitignore` files under the scanned roots: yes/no
- `.ignore` and `.fdignore` files: yes/no
- Global gitignore (user-level): yes/no

## 3) Query-time LLM rewrite failure policy

LLM rewrite is required in v1, but transient failures will happen.

- If rewrite fails for a query, should `search`:
    - Option A: fall back to raw user query and continue
    - Option B: fail the query (return tool error)

Recommendation for v1 stability: Option A.

## 4) DuckDB extension availability strategy

The design allows downloading/installing extensions at runtime.

- In environments with restricted network access, should the server:
    - Option A (current): fail fast with a clear error
    - Option B: support an “offline mode” with pre-bundled extensions (still DuckDB-only)

If Option A, please confirm that this is acceptable for the intended deployment environments.

## 5) Embedding model enforcement

The design expects `text-embedding-3-large` with dimension 3072.

- If the deployment returns embeddings with a different dimension, should v1:
    - Option A: fail fast (recommended)
    - Option B: accept and record dimension dynamically

If Option B, clarify whether the VSS index should be rebuilt dynamically based on the observed dimension.
