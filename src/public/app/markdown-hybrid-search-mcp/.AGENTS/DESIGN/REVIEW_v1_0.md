# Design Review — Markdown Hybrid Search MCP (v1.0)

Date: 2026-01-12
Reviewer: GitHub Copilot (GPT-5.2)

This document is a **strict design review** of `v1.md`, incorporating the user-confirmed decisions in `CLARIFY_v1_0.md`.

> Repository rule reminder: this review is written in English (repo policy).

## Executive summary

The design sets a clear v1 scope (document-level, one-shot, local ephemeral index) and correctly identifies key risks (Windows packaging, code blocks, length imbalance). The biggest issues to resolve before implementation are:

1. **Critical operational details are missing:** lifecycle cleanup, retry/backoff, token/char budgets, file discovery edge cases, deterministic IDs.
2. **Search quality knobs lack acceptance criteria:** RRF is reasonable, but there are no defined defaults, validation methodology, or “good enough” criteria.

Note: The earlier open questions have now been clarified (LLM required, DuckDB-only/no fallback, traversal respects `.gitignore` and ignores `.git/`, embedding model, and failure policy). The remaining work is primarily about specifying the minimal concrete behavior needed to implement v1 without ambiguity.

## Alignment with `CLARIFY_v1_0.md`

### Resolved decisions coverage

| Clarified decision                      | Status in `v1.md` | Notes                                                                                                          |
| --------------------------------------- | ----------------: | -------------------------------------------------------------------------------------------------------------- |
| Azure OpenAI embeddings + LLM           |       **Aligned** | LLM is required (minimal query rewrite/expansion); embeddings use `text-embedding-3-large`.                    |
| Entra ID auth (no API keys)             |     **Mentioned** | Good, but credential chain details and error UX need spec.                                                     |
| Local-only except AOAI                  |       **Aligned** | Ensure no telemetry/remote DB, and avoid auto-downloading extensions at runtime if it pulls from the internet. |
| Single embedding model per run          |       **Aligned** | Needs explicit meta + enforcement on query.                                                                    |
| Build index once at startup; no updates |       **Aligned** | `--reindex` flag still acceptable, but semantics need to be precise.                                           |
| Whole-document retrieval only           |       **Aligned** | Good.                                                                                                          |
| Code blocks approach                    |       **Aligned** | Needs a concrete extraction spec for `code_signals` and embedding text budgets.                                |
| Ephemeral index in temp; delete on exit |       **Aligned** | `v1.md` includes an explicit lifecycle requirement; implementation still needs concrete cleanup mechanics.     |

## Must-fix issues (blocking)

### 1) Index lifecycle: ephemeral delete-on-exit

Clarification requires:

- Index stored under a temp dir
- Temp storage **deleted automatically when process exits**

`v1.md` mentions “temporary directory for the lifetime of the process” but does not specify cleanup.

**Required change:** specify lifecycle precisely:

- Use an owned temp directory (e.g., `TemporaryDirectory`) and keep it alive for the process lifetime
- Register cleanup for normal exit and handle termination signals best-effort (Windows + POSIX differences)
- Ensure file handles (DuckDB/SQLite connections) are closed before delete

### 2) Embedding input budget and truncation strategy

Design says “cap tokens/chars” and “keep headings + first N tokens (optionally plus tail)”, but does not define:

- The actual cap (chars or tokens)
- How to treat YAML frontmatter
- How to avoid pathological cases (very long code fences, minified content)

**Required change:** define a deterministic “embedding text builder” algorithm with:

- Fixed char limit (simple) or token limit (more correct)
- Clear inclusion order (title/headings → prose blocks → tail excerpt)
- Code handling rules consistent with `code_signals`

Status update: the embedding context limit is clarified as **8191 tokens** for `text-embedding-3-large`, and `v1.md` now specifies newline-boundary splitting + aggregation for over-limit inputs. The remaining work is to pick concrete defaults (segment cap, aggregation rule, and the exact tokenizer/encoding).

### 3) Query-time and build-time reliability requirements

AOAI calls need:

- Retries with exponential backoff
- Handling 429/503
- Timeouts
- Clear error messages and partial failure behavior

**Required change:** specify error policy, especially because indexing happens at startup. Decide:

- Retry strategy (count/backoff/jitter/timeouts)
- Fail-fast behavior after retries (exit with error)
- What is reported via `stats` when startup fails or succeeds

## Should-fix issues (highly recommended)

### A) Search semantics and defaults

You propose hybrid candidate generation + RRF fusion. That is reasonable, but must specify defaults:

- `k_text`, `k_vec`, `top_k`
- RRF smoothing constant `k` (e.g., 60)
- Tie-breaking rules
- Optional length penalty: pick whether v1 uses it, and if yes define default $\alpha$

Also define minimal acceptance criteria:

- Example queries + expected qualitative behavior
- A small evaluation harness for regression (even manual)

### B) Snippet generation definition

“Snippet around best-matching region” is underspecified.

At minimum define:

- Whether snippet is derived from FTS match positions, simple keyword windowing, or regex
- Maximum snippet length
- Highlighting format (plain text? markdown?)

### C) Code block parsing robustness

Markdown fences are prevalent per your corpus sample. Specify handling for:

- Unclosed fences
- Nested fences or odd backtick counts
- Indented code blocks (not fenced)
- Language identifiers after ```

### D) File discovery boundaries

Specify:

- Encoding (UTF-8 with fallback?)
- Max file size to read
- Handling of permission errors
- Skipping `.git/`, `node_modules/`, build outputs (default excludes)
- Whether to follow symlinks / junctions (Windows)

Addendum: traversal policy has been clarified as “follow symlinks/junctions, respect `.gitignore`, always ignore `.git/`, deduplicate by normalized real path”. The remaining spec work is to define how `.gitignore` is located/applied (per-root vs repo-level, multiple ignore files, etc.) while keeping v1 minimal.

Update: ignore handling is now clarified as “apply ignore rules only when a `.git/` directory exists (i.e., the root is in a Git repo); otherwise do not apply ignore rules”.

### E) Observability: what is logged and what is surfaced via MCP

Good start, but define:

- Structured log fields (timings, counts, failures)
- `stats` payload schema
- Whether to expose build warnings/errors

## Nice-to-have (non-blocking)

- MMR diversification: keep optional; consider implementing after baseline quality is stable.
- `mode: {docs, mixed, code}`: marked future; OK.
- RAG answer synthesis: out of scope for v1.

## Recommended spec additions to `v1.md` (concrete)

1. **Backend decision section**
    - “Primary backend: X (exact packages). Fallback: Y. Gate: run on Windows Python 3.12 with offline install.”

2. **Text transformations**
    - Exact definitions for `text_all`, `text_no_code`, `code_signals`.

3. **Embedding builder**
    - Deterministic algorithm and cap.

4. **Failure handling**
    - Startup behavior, partial failures, retries.

5. **MCP tool schema**
    - Exact JSON schema for inputs/outputs (even if names are placeholders).

## Risks (tracked)

- **Windows packaging risk** for DuckDB extensions / vector similarity.
- **Startup latency risk** due to embedding all docs at once.
- **Quality risk** at document-level only (no chunking): mitigated by good embedding builder + strong FTS.
- **Credential UX risk** (Entra auth): must present actionable guidance when auth fails.

## Minimal acceptance checklist for v1 implementation

A v1 implementation can be considered ready when:

- Index builds successfully on Windows with Python 3.12.
- Index directory is created under temp and **deleted on exit**.
- Entra ID auth works with preferred chain (Azure CLI → Interactive Browser → Device Code).
- Whole-document search returns stable results and includes:
    - `path`, `title` (best effort), `snippet`, combined score and per-channel scores.
- Code blocks are searchable via full-text, but do not dominate default embeddings.
- `stats` surfaces doc count, build time, embedding deployment and dimensions, and failure counts.
