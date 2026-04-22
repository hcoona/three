# Docs LLM Wiki Agent Contract

## Mission

Maintain `docs/` as a persistent, compounding knowledge base.
The human curates sources and asks questions.
The agent reads sources, updates the wiki, and keeps the structure coherent.

## Directory Layout

- `docs/sources/` stores human-curated source documents.
  Treat this directory as immutable input.
- `docs/raw/` stores immutable supporting assets such as images, PDFs, exports,
  or datasets referenced by source documents.
- `docs/wiki/` stores the LLM-maintained markdown wiki.
  This is the main working surface for synthesis.

The current wiki layout is:

- `docs/wiki/overview.md` for the current top-level synthesis.
- `docs/wiki/index.md` for the content-oriented catalog.
- `docs/wiki/log.md` for the append-only chronological log.
- `docs/wiki/sources/` for per-source digest pages.
- `docs/wiki/concepts/` for topic and theme pages.
- `docs/wiki/entities/` for people, organizations, tools, places, or other
  named entities.
- `docs/wiki/analyses/` for durable answers, comparisons, timelines, and other
  query outputs worth keeping.

## Non-Negotiable Rules

1. Write and update wiki content in English.
2. Do not modify files under `docs/sources/` or `docs/raw/` unless the user
   explicitly asks for it.
3. Prefer updating existing wiki pages over creating near-duplicate pages.
4. Use concise Markdown with descriptive headings and relative Markdown links.
5. Use kebab-case for new filenames.
6. Flag uncertainty explicitly instead of presenting speculation as fact.
7. When a newer source changes or contradicts an older claim, update the
   affected wiki pages and note the change.
8. Keep `docs/wiki/log.md` append-only.

## Source and Page Conventions

### Source Digests

Create one page per ingested source in `docs/wiki/sources/`.
Use a date-prefixed filename when it helps avoid collisions, for example
`2026-04-21-llm-wiki.md`.

Recommended sections:

- `# Title`
- `## Summary`
- `## Key Points`
- `## Important Claims`
- `## Related Pages`
- `## Open Questions`
- `## Source Location`

### Concept Pages

Use `docs/wiki/concepts/` for recurring topics that synthesize multiple
sources.
These pages should favor synthesis over quotation and should link to the
relevant source digests and entity pages.

### Entity Pages

Use `docs/wiki/entities/` for durable named entities that recur across the
wiki.
Examples include people, companies, products, books, methods, or places.

### Analysis Pages

Use `docs/wiki/analyses/` for durable outputs produced while answering
questions.
These pages can capture comparisons, timelines, checklists, open problems, or
working theses that should remain in the wiki after the chat ends.

## Operating Workflows

### Ingest

1. Read the new source from `docs/sources/` and any supporting material from
   `docs/raw/`.
2. Create or update a digest page in `docs/wiki/sources/`.
3. Update `docs/wiki/overview.md` if the source changes the current synthesis.
4. Update relevant pages in `docs/wiki/concepts/`, `docs/wiki/entities/`, and
   `docs/wiki/analyses/`.
5. Update `docs/wiki/index.md` so new or changed pages remain discoverable.
6. Append a new entry to `docs/wiki/log.md`.

### Query

1. Read `docs/wiki/index.md` first.
2. Read the most relevant wiki pages before consulting raw sources.
3. Answer using the wiki when possible, then cite or trace back to sources when
   needed.
4. If the answer produces a durable artifact, file it in
   `docs/wiki/analyses/`, update `docs/wiki/index.md`, and append to
   `docs/wiki/log.md`.

### Lint

Periodically health-check the wiki for:

- contradictions between pages;
- stale claims superseded by newer sources;
- orphan pages with weak or missing links;
- concepts or entities mentioned repeatedly but lacking their own page;
- gaps where a future source search would be valuable.

When linting finds an actionable issue, fix the wiki first and log the pass in
`docs/wiki/log.md`.

## Index Maintenance

`docs/wiki/index.md` is the main navigation file.
Keep it content-oriented and grouped by category.
Each entry should include a relative link and a one-line description.

## Log Maintenance

Each entry in `docs/wiki/log.md` should start with a consistent heading:

`## [YYYY-MM-DD] operation | title`

Use `operation` values such as `ingest`, `query`, `lint`, or `bootstrap`.
Keep entries brief but specific about what changed.
