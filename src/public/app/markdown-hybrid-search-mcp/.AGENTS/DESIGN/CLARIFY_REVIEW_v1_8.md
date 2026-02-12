# Clarifications Requested — Markdown Hybrid Search MCP (v1.8)

This file lists items that require human confirmation because they materially affect correctness, UX, or the feasibility of the “validated SQL invariant”.

## 1) Vector candidate filtering behavior when oversampling is insufficient

When ANN oversampling is used, SQL-level filters (`roots`, `path_prefix`, `updated_after`) may drop many candidates.

**Question:** If fewer than `k_vec` remain after filtering, should v1:

- (A) Return fewer vector candidates (simplest), or
- (B) Increase oversampling and retry (potentially multiple queries), or
- (C) Fall back to a non-ANN prefilter (may break the “HNSW required” constraint).

**Recommendation:** (A) for v1, and document it explicitly.

## 2) CLI argument naming compatibility for rewrite deployment

`v1.md` specifies `--azure-openai-rewrite-deployment`. Some earlier notes use `--azure-openai-chat-deployment`.

**Question:** Should the implementation accept both flags (aliases) for backwards compatibility, or only the `rewrite` name?

**Recommendation:** Accept aliases if existing users/scripts are expected.

## 3) Relative paths for `--root` and `path_prefix`

**Question:** Should `--root` and `path_prefix` accept relative paths?

Options:

- (A) Reject relative inputs (require absolute), or
- (B) Accept relative and resolve against process CWD, then normalize.

**Recommendation:** (B) is user-friendly, but must be documented and covered by tests.

## 4) Large-file policy rigidity

`v1.md` recommends treating files >10MB as a fatal indexing error.

**Question:** Is the 10MB threshold a hard constant for v1, or should it be configurable via CLI?

**Recommendation:** Keep constant for v1 unless corpora are known to contain larger Markdown files.
