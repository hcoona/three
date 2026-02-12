# Clarifications — Design Review v1.6

Date: 2026-01-13
Status: Resolved (review follow-up)

This file records clarifications that materially affect correctness or v1 operability.

## Resolved decisions

1. **Azure OpenAI structured output compatibility (rewrite stage)**
    - **Rewrite model**: GPT-5.2 (`gpt-5.2`, version `2025-12-11`).
        - Reference: Azure OpenAI Responses API model support list.
          https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/responses?view=foundry-classic
    - **Minimum API requirement**:
        - The rewrite stage uses the **Azure OpenAI v1 Responses API**. The docs show `base_url=.../openai/v1/` and `api-version=preview` for Responses API calls.
            - Reference (example uses `default_query={"api-version": "preview"}`):
              https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/responses#retrieve-a-response
        - For structured outputs, Microsoft documents that support was first added in `2024-08-01-preview`, and is available in later preview APIs and the latest GA API (doc currently lists `2024-10-21` and/or `v1` depending on the documentation view).
            - Reference:
              https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs
    - **Fail-fast requirement**: the server **must fail at startup** if schema-constrained JSON output (structured outputs via `response_format: { type: "json_schema", json_schema: { strict: true, ... } }`) is not supported by the provided deployment / API configuration.
        - Reference (mechanics + limitations, e.g. `additionalProperties: false`):
          https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs#getting-started

2. **DuckDB compatibility envelope**
    - Accept a **minimum version range** (not an exact pin). For v1, the minimum remains:
        - **DuckDB >= v1.4.3** (as stated in `v1.md`).
    - Offline/air-gapped support is **out of scope**. Runtime extension installation/loading is acceptable.
    - Fail-fast remains: if `fts`/`vss` cannot be enabled/used, the server exits with an error.

3. **File decoding policy for Markdown**
    - Markdown files are decoded as **UTF-8**.
    - Decode errors are treated as **fatal** (fail the indexing run rather than silently altering bytes).

4. **Root validation**
    - If a provided `--root` path does not exist, startup **fails fast**.

5. **Filter application point**
    - `roots/path_prefix/updated_after` filters are applied **inside the DuckDB candidate queries** (FTS and VSS), not only after union/fusion.
