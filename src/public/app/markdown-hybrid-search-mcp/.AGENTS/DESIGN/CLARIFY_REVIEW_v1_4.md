# Clarification Questions — Design Review Follow-ups (v1.4)

Date: 2026-01-13

These questions aim to resolve a few product/UX decisions that materially impact implementation details and retrieval quality.

Status: Resolved (2026-01-13)

## 1) File extensions to discover in v1

`v1.md` specifies discovery via `fd -e md` (i.e., `*.md` only).

- Confirm that v1 should **exclude** `.markdown`, `.mdx`, and other Markdown-like extensions.
- If inclusion is desired, specify the allowed extension set for v1.

Answer: V1 indexes `*.md` files only.

## 2) Full-text tokenization: developer tokens

The proposed FTS `ignore` pattern drops characters such as `+`, `#`, `=`, and `@`, which affects common developer terms:

- `C++`, `C#`
- `key=value`
- `@decorator`

Question: Should v1 prioritize preserving these tokens in FTS? If yes, which characters must be retained by the tokenizer configuration?

Answer: Yes. V1 should prioritize preserving developer-centric tokens in FTS. At minimum, the tokenizer must retain `+`, `#`, `=`, and `@`.

## 3) Embedding cost/time guardrails

The design caps discovery at 100,000 files, but that can still be prohibitively expensive/slow for embeddings.

- Is it acceptable for v1 to attempt embedding up to the discovery cap by default (fail-fast on any persistent embedding error)?
- Or should v1 enforce a lower default cap (e.g., 10k) unless an explicit “I understand the cost” flag is provided?

If a cap is desired, please specify the default maximum documents to embed.

Answer: No cap in v1.
