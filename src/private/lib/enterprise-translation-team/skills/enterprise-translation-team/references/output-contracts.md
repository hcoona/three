# Output Contracts

## Translation output

Use `translation.md` unless the user requests a different format.

Requirements:

- Preserve Markdown heading levels, list nesting, table dimensions, links,
  images, inline code, code fences, placeholders, and file paths.
- Translate natural-language prose only.
- Do not leave source-language prose untranslated unless marked as protected
  text or unresolved.
- Add translator queries in a separate section only when ambiguity affects
  correctness.

## Terminology output

Terminology is a maintained termbase, not a two-column glossary.
Use `references/terminology-schema.md` as the detailed contract.

Default terminology package:

- `termbase.job.json`: canonical agent-native resolved termbase for the job.
- `termbase.delta.jsonl`: append-only job proposals, conflicts, overrides,
  waivers, and promotion requests.
- `termbase.tbx`: TBX-compatible exchange export.
- `terminology-review.tsv`: lossy flattened human review view only.

Do not use TSV as the canonical terminology asset.
TSV edits must re-enter as JSONL delta proposals.

`terminology-review.tsv` uses this header:

<!-- markdownlint-disable MD010 MD013 -->

```text
concept_id	entry_id	scope	status	source_term	preferred_target	allowed_variants	forbidden_targets	context_note	positive_example	negative_example	conflict_id	blocking	evidence_refs
```

<!-- markdownlint-enable MD010 MD013 -->

Allowed canonical entry status values:

- `approved`
- `candidate`
- `conflict`
- `forbidden`
- `needs_confirmation`
- `deprecated`
- `rejected`

Blocking final delivery conditions:

- An applicable `approved` term is missing without an explicit waiver.
- A `forbidden` target term appears in the translation.
- A `candidate`, `conflict`, or `needs_confirmation` term affects delivered
  text.
- A job override is used without approval or waiver.
- `termbase.tbx` or `terminology-review.tsv` no longer matches
  `termbase.job.json`.

## Review output

Use `review.json` for machine-checkable MQM findings:

```json
{
    "issues": [],
    "summary": {
        "major": 0,
        "minor": 0,
        "neutral": 0
    }
}
```

The top-level array must be named `issues`.
Do not use `findings`.
The `summary` object must include top-level lowercase numeric keys `major`,
`minor`, and `neutral`.
Do not replace them with nested `by_severity` counts.
Each issue category must be one of the exact values in
`references/mqm-taxonomy.md`; do not append subcategories such as
`Accuracy/Mistranslation`.
Each issue must have a unique stable `issue_id` and a `resolution_status` of
`open`, `resolved`, or `waived`.
The post-editor must preserve issue identifiers.
Resolved or waived issues require non-empty `resolution_evidence`;
waived issues also require `waiver_ref`.
Only open Major issues block final QA.

## QA output

Use `qa.md` with:

- Every checked package filename.
- Deterministic checks and pass/fail results.
- Unresolved major issues.
- Human or subject-matter expert sign-off still required.

Add exactly one `## Blocking failure records` section.
Put only records in that section,
using one exact line per unresolved delivery blocker:

```text
- [FAIL] forbidden-term | <term>
- [FAIL] missing-approved-term | <concept-id> | <preferred-target>
- [FAIL] open-conflict | <conflict-id>
- [FAIL] major-mqm | <issue-id> | <segment-id> | <category> | <target-quote>
- [FAIL] projection-mismatch | <filename>
- [FAIL] qa-check | <check-id> | <artifact> | <detail>
```

`[FAIL]` means unresolved and delivery-blocking.
Use `qa-check` only when no more specific record form applies,
including missing or empty files, structure or placeholder loss,
and unapproved overrides.
Each field must be non-empty, single-line text without `|`.
Do not emit `[PASS]` records in this section.
Put explanatory prose elsewhere.
Include this exact pending-approval sentence:

```text
Human or subject-matter expert sign-off still required.
```

Do not claim that human or subject-matter expert sign-off was completed.
