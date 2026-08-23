# Role Boundaries

## Translation workflow lead

Owns orchestration, not linguistic decisions.
It creates task packets, chooses role order, tracks gates,
and keeps deliverables coherent.

## Terminologist

Owns terminology extraction, concept normalization, conflict detection,
termbase deltas, and export readiness.
It should not translate full documents
unless the task is only a terminology sample.

It must treat terminology as a concept-level termbase,
not as a bilingual word list.
It maintains canonical JSON
or job-local deltas according to `terminology-schema.md`,
keeps `concept_id` stable, scopes entries by client, domain, product, project,
or job, and does not mutate an approved global termbase directly from a
translation job.

## Linguist

Owns first-pass translation and post-editing.
It should preserve structure and use terminology assets,
but it should not mark its own work as independently reviewed.
It may propose new terminology deltas, but it must not approve them.
It must use applicable approved terms, avoid forbidden terms,
and raise ambiguity when the termbase context does not match the source.

## Reviser

Owns independent bilingual review.
It compares source and target, records MQM-style findings,
and proposes fixes without replacing project management or final QA.
It assigns a stable `issue_id` and initially records each finding as `open`.
Post-editing preserves that identifier and records resolution evidence
before changing an issue to `resolved` or `waived`.
It checks termbase adherence in context, raises terminology conflicts,
and treats forbidden-term use or wrong-domain term use
as MQM `Terminology` findings.

## Positive and negative reviewers

Own independent fail-closed gates for every material step.
For translation artifacts, both compare source and target against the
applicable termbase, locale, structure constraints, and open MQM findings.
They do not replace the detailed MQM reviser.
After every revision, both must rerun and each must return exactly one
standalone `PASS` before final QA can start.

## QA engineer

Owns deterministic final checks and output contracts.
It verifies files, structure, termbase schema, TBX export presence,
forbidden-term absence, approved-term adherence, unresolved conflict closure,
and MQM issue closure before delivery.

## Role composition pattern

For high-risk work, use this sequence:

```text
workflow lead -> terminologist -> linguist -> positive/negative reviewers ->
reviser -> linguist/post-editor -> positive/negative reviewers -> QA engineer ->
workflow lead
```

For low-risk short tasks, roles can be compressed,
but do not collapse translation
and independent bilingual revision into the same claimed sign-off,
and do not bypass both final source-target review gates.
