# Unresolved Design Questions and Review Evidence

This synthesis preserves decision-relevant questions from the January 2026
design reviews [v1.0][r0], [v1.1][r1], [v1.2][r2], [v1.3][r3], [v1.4][r4],
[v1.5][r5], [v1.6][r6], [v1.7][r7], [v1.8][r8], and the unanswered
[v1.8 clarification request][q8]. Those reviews concern successive forms of
the design; some contain later updates or recommendations contradicted by the
retained [source answers](../README.md#retained-source-inputs). Their assertions
of independence and readiness are historical claims, not migration review
results. The original texts remain recoverable at the linked Git baseline.

The owner and domain reviewers consume this record before implementing the
[design](../architecture/overview.md); project authors maintain it as decisions
and evidence change. It retains questions and proposed validation, not adopted
requirements, new work grants, or independently triaged findings. No DuckDB,
Azure, authentication, or corpus experiment was run for this migration.

## Resolved Index Lifetime and Distribution Boundary

The [supplied v1.3 answers](../../.AGENTS/DESIGN/CLARIFY_REVIEW_v1_3.md)
resolve index lifetime as a process-owned ephemeral snapshot built at startup,
without cross-run reuse. The package [README](../../README.md) routes to that
accepted design intent. Its former persistent-index wording is retained in
[Git](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/README.md),
not treated as an unanswered product choice. The server remains a placeholder.

The public source-directory location also differs from the package's
`Private :: Do Not Upload` classifier. This is the metadata question recorded
in the repository's [original package digest][metadata]. Neither directory
placement nor migration authorizes publication or a classifier change.

## Storage Compatibility and Candidate Queries

The reviews repeatedly ask for evidence that one exact set of DuckDB DDL and
candidate query templates works with the specified minimum version, extensions,
fixed-size vector representation, parameter binding, and cosine metric.
Illustrative SQL and an intended startup self-test are not that evidence.

The remaining questions include:

- Which engine/extension versions are actually tested, and how is drift detected?
- Do the runtime and self-test reuse the same table/index/query definitions,
  including the filters and vector casts? Does the metric require vector
  normalization?
- Which plan nodes demonstrate HNSW use? An allowed/denied operator set was
  suggested because exact plan strings may change. Tiny synthetic data may not
  establish behavior on a real corpus.
- Does the FTS `match_bm25` query use the intended index efficiently, or compute
  a score for every row? The reported stable-doc naming observation for
  `main.docs`/`fts_main_docs` remains documentation evidence, not a measured plan.
- How are SQL filters bound without weakening path-segment boundaries or
  accidentally interpreting `%` and `_` as pattern syntax?

The source answers choose ANN oversampling with `N = 10`, followed by filtering
and truncation in candidate generation. The v1.8 question remains: if fewer than
`k_vec` candidates survive, return fewer, increase oversampling/retry, or use a
different prefilter? The review recommends returning fewer for simplicity; no
answer is recorded. Do not infer a fallback that contradicts the design's HNSW
constraint. Validate FTS and VSS with restrictive filters and expected results,
including embedding-dimension checks both at indexing and query time.

## Paths, Discovery, and Timestamps

The source answers define normalized real paths, hard root boundaries, and
ignore handling delegated to `fd`. Review questions still cover:

- relative `--root` and `path_prefix` inputs: reject them or resolve against the
  process working directory? v1.8 recommends the latter without recording an
  answer;
- roots that are files, symlinked roots, outside-root targets, cycles, junctions,
  trailing separators, drive-letter case, UNC/device paths, and prefixes that
  need not exist on disk;
- NFC normalization potentially collapsing distinct filenames on Linux, and
  whether to report such collisions rather than silently drop a document;
- permission and I/O errors during discovery or reading;
- exact RFC3339 handling for `Z`, offsets, fractional seconds, and conversion to
  UTC milliseconds, while rejecting missing timezones and comparing the indexed
  snapshot's mtime;
- repeatable discovery tests for nested repositories and `.git` as a file,
  recording the installed `fd` version and invocation rather than replacing its
  ignore policy with an older review's recommendation.

Use the same normalization for discovery, identity, filters, and lookup.
Suggested tests include `/a/b` versus `/a/b2`, platform case rules, symlink
containment, and stable SHA-256 identity for the same normalized path.

## Text, Embedding Inputs, and Resource Behavior

The retained design chooses document granularity and capped head/tail embedding
input. Earlier review recommendations for chunk aggregation, broad extension
discovery, or a corpus cap are not additional requirements.

The unresolved implementation details include deterministic Markdown-to-text
handling for inline/fenced/indented code, unclosed or unusual fences, language
info strings, links versus URLs, HTML, tables, front matter, very long lines,
and newline normalization. Specify extraction order and short-document
head/tail behavior without exceeding the token budget. Check empty and
code-only files, `code_signals` ordering/deduplication, and any truncation flags.

UTF-8 decoding and no partial index are supplied decisions. BOM handling remains
a review recommendation. The v1.8 request asks whether the example 10 MB fatal
threshold is a fixed constant or configurable; it recommends a constant but
records no answer. Size/decoding preflight before costly embeddings and errors
identifying the offending path/size were proposed to reduce late whole-index
failure. They do not authorize silently skipping files.

Retry counts, backoff/jitter, timeouts, 429/503 treatment, maximum concurrent
embedding calls, and operator visibility still need a concrete design. The
reviews suggest logging discovered and embedded counts, input-size estimates,
retries/rate-limit events, and duration breakdowns before and during indexing.
Unbounded corpus discovery plus all-or-nothing indexing has startup cost and
availability consequences even when each document is capped.

## Rewrite Compatibility, Authentication, and Errors

The source answers select Responses API structured outputs and capability
verification without requiring proof of the deployment's backing model. Preserve
their dated API references and distinguish them from a successful runtime probe.
The exact request/API configuration, local schema validation, and preflight
acceptance/error categories remain implementation concerns.

Suggested diagnostic categories distinguish authentication/token failures,
endpoint/API mismatch, unsupported structured outputs, rejected schema, invalid
JSON, and output over-limit. The source answers require rewrite failure to
produce a tool error; historical raw-query fallback suggestions do not override
that choice. The API/schema restrictions and the feasibility of temperature or
other determinism settings need validation against the selected configuration.

The v1.8 request asks whether `--azure-openai-chat-deployment` should remain an
alias for `--azure-openai-rewrite-deployment`. Its alias recommendation is
conditional on actual user/script consumers; no compatibility decision is
recorded. The preference for Azure CLI, interactive browser, then device code
and exclusion of Managed Identity must remain visible in the resulting error
and headless-use design.

## Tool Contracts, Ranking, and Retrieval Quality

The reviews request precise `search`, `get_document`, and `stats` request,
response, and error contracts. Questions include upper bounds/defaults for
candidate counts, behavior when both `doc_id` and `path` are supplied, raw-path
lookup normalization, content-window caps/truncation flags, and snapshot
timestamps/roots/size visibility in `stats`.

RRF and channel diagnostics are described in the design. Stable tie-breaking,
score stability across rebuilds, and whether optional length penalties or
diversification are enabled remain distinct decisions. A review's proposed
penalty coefficient or MMR step is not an accepted default.

Snippet construction also has competing recommendations: tokenize
`keyword_query`, use the returned `keywords` array, prefer headings/longer
tokens, or score windows. v1.8 recommends `keywords` first with a fallback when
empty; this has no recorded answer. Preserve developer tokens such as `C++`,
`C#`, `key=value`, `@decorator`, and flags when evaluating consistency with FTS.
The 800-character first-hit strategy has known quality limits. ASCII-oriented
FTS and English rewrite can degrade non-English corpus retrieval even though
queries may be multilingual.

The reviews propose a small qualitative query corpus: code identifiers should
be discoverable through FTS, prose through semantic retrieval, long documents
should not dominate, and fusion should remain stable when a channel has no
candidate. These are validation proposals without measured acceptance results.

## Lifecycle, Privacy, and Validation Evidence

The source answers require best-effort cleanup of a unique process-owned
directory, never a user directory. The reviews retain practical concerns about
closing database handles before deletion, Windows/POSIX termination differences,
crash/SIGKILL limits, and cleanup after failure. A debug keep-directory flag was
only a suggestion.

Full Markdown snapshots may include sensitive data. Reviews suggest restrictive
temporary-file permissions where supported, avoiding full-content logs and
unexpected sensitive path exposure, and minimizing Azure-bound content. The
human answers permit Azure OpenAI use and runtime DuckDB extension downloads;
they do not grant new migration-time external effects. Prompt-safety discussion
about an `answer` tool was conditional on answer synthesis, which the supplied
correction excludes from v1.

Before a later implementation claims readiness, its authorized validation needs
actual results for the storage/self-test contract, Windows operation, root and
snapshot boundaries, deterministic extraction/fusion, strict rewrite handling,
and safe cleanup. Historical review recommendations to proceed, including
older Linux-only suggestions, do not replace the supplied Windows target or
independent review and owner disposition of remaining material questions.

[r0]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_0.md
[r1]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_1.md
[r2]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_2.md
[r3]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_3.md
[r4]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_4.md
[r5]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_5.md
[r6]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_6.md
[r7]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_7.md
[r8]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/REVIEW_v1_8.md
[q8]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/public/app/markdown-hybrid-search-mcp/.AGENTS/DESIGN/CLARIFY_REVIEW_v1_8.md
[metadata]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/sources/2026-04-21-markdown-hybrid-search-mcp-pyproject.md
