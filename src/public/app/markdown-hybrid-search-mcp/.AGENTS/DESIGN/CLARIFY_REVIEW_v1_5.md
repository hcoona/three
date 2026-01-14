# Clarifications — Review v1.5 (Design v1)

<!-- markdownlint-disable MD029 -->

This file listed clarifications that would materially affect correctness, portability, or scope.
All items are now resolved with author-provided answers.

## Resolved answers (provided by author)

## A) Corpus language / search expectations

1. Target corpus is **primarily English** developer documentation.

2. Users are allowed to query in **any language**.
    - The rewrite stage should translate queries to English (per design) to match the English corpus and the ASCII/English-oriented FTS tokenization.

## B) Implementation/runtime choices

3. Implementation language/runtime: **Python**.

4. MCP SDK/framework: **FastMCP**.

## C) Azure OpenAI API and auth constraints

5. Use Azure OpenAI **Responses API**, using **JSON output**.

6. Headless environments are a first-class target, with the following auth preference order:
    - Azure CLI > Interactive Browser > Device Code
    - Do **not** attempt Managed Identity.

7. Sending any data to Azure OpenAI is compliant. Do not use other external services.

## D) DuckDB + extensions operational assumptions

8. Outbound network is available.

9. DuckDB version is a **minimum** (not a strict pin).
    - Recommendation: keep the design's stated version (v1.4.3) as the documented minimum unless testing proves a lower minimum is safe.

## E) Data limits and failure policy

10. Very large files (e.g., > 10MB) can fail-fast during indexing.

11. Index build is strict **all-or-nothing** (no partial index).

## F) Filters and timestamps

12. Timestamp/filter input format (recommended):
    - Persist document mtime as `mtime_unix_ms BIGINT` (UTC), captured at indexing time from filesystem metadata.
    - Tool input `updated_after` should be an RFC3339 timestamp with timezone (recommend requiring UTC `Z`, e.g. `2026-01-13T12:34:56Z`).
    - The filter matches documents with `mtime_unix_ms > updated_after_unix_ms`.
    - Rationale: avoids DuckDB timezone-naive `TIMESTAMP` ambiguity while keeping a human-friendly API.
