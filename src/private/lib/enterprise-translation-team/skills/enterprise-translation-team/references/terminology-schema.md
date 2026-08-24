# Terminology Schema

## Standard baseline

Terminology assets must be at least as expressive as TBX.
Use TBX / ISO 30042 as the standard exchange baseline and TBX-Basic
as the default interoperable export target.
TBX-Min is acceptable only as a fallback export profile,
not as the internal design target.

TMX is translation memory and XLIFF is localization-package exchange.
They may reference or consume terms,
but they are not the canonical termbase format for this skill.

## Termbase layers

Use a shared, scoped termbase plus job-local deltas.
Multiple translated documents should share the same applicable termbase layer
instead of creating isolated document glossaries.

Resolution order:

```text
document override -> job override -> project/product/domain -> client -> global
```

Global entries are only for universal, non-confidential concepts.
Client, domain, product, project,
and job terms must carry explicit scope
so terminology does not leak across customers or domains.

Jobs must not mutate approved global/client termbases directly.
They write `termbase.delta.jsonl`; approved deltas can later be promoted.

## Required files

| File                     | Role                                                                          |
| ------------------------ | ----------------------------------------------------------------------------- |
| `termbase.job.json`      | Canonical resolved concept-level termbase for the job.                        |
| `termbase.delta.jsonl`   | Append-only proposals, conflicts, overrides, waivers, and promotion requests. |
| `termbase.tbx`           | Standard exchange export generated from the canonical JSON.                   |
| `terminology-review.tsv` | Lossy flattened review view; never canonical.                                 |

`termbase.tbx` must export every canonical source term, preferred target,
allowed variant, and forbidden target as TBX term sections
where the selected profile permits it.
If the selected TBX profile cannot preserve workflow metadata or examples,
ship `termbase.tbx` plus the exact JSON sidecar path `termbase.job.json`;
`termbase.job.json` remains the canonical lossless job termbase.

## Canonical JSON contract

`termbase.job.json` must use BCP-47 language tags and concept-level entries:
The deterministic checker accepts the regular `langtag` grammar and
private-use tags. Producers must supply preferred replacements for
grandfathered tags; raw grandfathered tags are rejected.
Every entry's `source.language` and `target.language`
must match the job-level `source_locale` and `target_locale`, respectively.
Every entry scope must include `level` and `domain`;
client and project identifiers from the job brief must be preserved when
provided.
The job brief's domain describes the overall assignment; an entry's
`scope.domain` may narrow it to the term's applicable subject area.
Every supplied positive or negative example must be a non-empty object.
For every non-empty example list, the first positive example must include a
non-empty `target`, and the first negative example must include at least one
non-empty `correct_guidance`, `reason`, or `bad_target`.
Approved entries must provide both example lists.

```json
{
    "schema_version": "enterprise-termbase-v2",
    "standard_basis": {
        "primary": "TBX ISO 30042",
        "export_targets": ["TBX-Basic"],
        "lossless_for_key_fields": true
    },
    "termbase_id": "enterprise-zh-en",
    "source_locale": "zh-Hans",
    "target_locale": "en-US",
    "entries": [
        {
            "concept_id": "c-release-0001",
            "entry_id": "t-gray-release-zh-en",
            "status": "approved",
            "scope": {
                "level": "client",
                "client_id": "example-client",
                "domain": "enterprise-saas-release-management",
                "project_id": "admin-docs"
            },
            "source": {
                "term": "灰度发布",
                "language": "zh-Hans",
                "part_of_speech": "noun",
                "term_type": "fullForm"
            },
            "target": {
                "preferred": "phased rollout",
                "language": "en-US",
                "allowed_variants": ["phased release"],
                "forbidden": [
                    {
                        "term": "gray release",
                        "match_mode": "case_insensitive",
                        "reason": "Literal false friend in release-management context.",
                        "severity": "blocking"
                    }
                ]
            },
            "context": {
                "definition": "A controlled release to a limited population before broad availability.",
                "usage_note": "Use for software rollout strategy, not image processing.",
                "positive_examples": [
                    {
                        "source": "本次灰度发布仅面向内部员工。",
                        "target": "This phased rollout is limited to internal employees.",
                        "reason": "Software release context."
                    }
                ],
                "negative_examples": [
                    {
                        "source": "图片灰度处理完成后再发布。",
                        "bad_target": "phased rollout",
                        "correct_guidance": "This is image grayscale processing, not release management.",
                        "reason": "Same surface form, wrong domain."
                    }
                ]
            },
            "provenance": {
                "created_by": "translation-terminologist",
                "created_at": "2026-06-18",
                "evidence_refs": ["terminology-brief.json#terms[0]"]
            },
            "maintenance": {
                "revision": 1,
                "owner": "terminology",
                "reviewer": "customer-subject-matter-expert",
                "approval_status": "approved",
                "reliability": {
                    "code": 5,
                    "confidence": "high"
                },
                "last_reviewed_at": "2026-06-18"
            }
        }
    ],
    "conflicts": []
}
```

Allowed entry `status` values:

- `approved`
- `candidate`
- `conflict`
- `forbidden`
- `needs_confirmation`
- `deprecated`
- `rejected`

### Canonical conflict contract

Every item in top-level `conflicts` must use this shape:

```json
{
    "conflict_id": "conf-tenant-renter-0002",
    "concept_id": "c-tenant-0002",
    "source_term": "租户",
    "scope": "client:example-client/multi-tenant-cloud-service",
    "competing_targets": ["tenant", "renter"],
    "status": "open",
    "blocking": true,
    "evidence_refs": ["terminology-brief.json#terms[1]"]
}
```

`conflict_id` must be unique and stable.
`concept_id` must reference an entry,
`source_term` must equal that entry's source term,
and `competing_targets` must contain at least two unique non-empty targets.
Conflict `scope` is a non-empty serialized scope string,
and every `competing_targets` item is a target string rather than an object.
`status` must be `open` or `resolved`.
An open conflict must set `blocking` to `true`.
A resolved conflict must set `blocking` to `false`
and include non-empty `selected_target` and `resolution_ref`.
Each evidence reference must be non-empty.

## Delta JSONL contract

Each line in `termbase.delta.jsonl` is an immutable event.

Allowed operations:

- `propose_term`
- `approve_term`
- `reject_term`
- `add_forbidden`
- `raise_conflict`
- `resolve_conflict`
- `add_document_override`
- `waive_term_violation`
- `promote_to_global`
- `supersede_entry`

Required event fields:

- `event_id`
- `op`
- `job_id`
- `doc_id`
- `concept_id` or `source_term`
- `scope`
- `evidence_ref`
- `submitted_by`
- `status`

`raise_conflict` and `resolve_conflict` events must include the stable
`conflict_id` of the canonical conflict.
`raise_conflict` must include at least two unique `competing_targets`.
`resolve_conflict` must include non-empty `selected_target`
and `resolution_ref`.
`add_forbidden` must include the exact forbidden target as `forbidden_term`.
The event `concept_id`, source term, candidates, and resolution must agree with
the canonical conflict.

Merge by `concept_id` plus scope, not by source term alone.
Different targets in the same scope create a conflict.
Approved entries are superseded by new records, not overwritten in place.

## TBX mapping

| JSON field                    | TBX-compatible mapping                                 |
| ----------------------------- | ------------------------------------------------------ |
| `concept_id`                  | `conceptEntry/@id`                                     |
| `scope.domain` / `subject`    | `descrip type="subjectField"`                          |
| `context.definition`          | `descrip type="definition"`                            |
| source/target language blocks | `langSec xml:lang`                                     |
| term text                     | `termSec/term`                                         |
| `part_of_speech`              | `termNote type="partOfSpeech"`                         |
| `term_type`                   | `termNote type="termType"`                             |
| `status` / `approval_status`  | `admin` or TBX-compatible term note                    |
| `reliability.code`            | reliability data category                              |
| contexts/examples             | `descrip type="context"` or profile-compatible sidecar |
| provenance                    | `transacGrp`, `admin`, or note fields                  |

TBX-Basic exports must preserve each canonical concept identifier,
source and target term set, `scope.domain` as `subjectField`,
`context.definition` as `definition`,
and the source term's `part_of_speech` and `term_type`.
Workflow-only status, examples, and provenance may remain in the exact
`termbase.job.json` sidecar when the selected TBX profile cannot preserve them.
When a target term does carry an administrative status,
it must not contradict canonical JSON:
forbidden targets use a deprecated or superseded status,
while preferred, allowed, and unresolved conflict-candidate targets must not.

## Review TSV

`terminology-review.tsv` must use this header:

<!-- markdownlint-disable MD010 MD013 -->

```text
concept_id	entry_id	scope	status	source_term	preferred_target	allowed_variants	forbidden_targets	context_note	positive_example	negative_example	conflict_id	blocking	evidence_refs
```

<!-- markdownlint-enable MD010 MD013 -->

The TSV is for human review and diffs only.
Serialize scope as `level:client_id/domain/project_id`
with any missing trailing components omitted.
Separate multi-value cells with semicolons.
Set `context_note` to `context.usage_note` when present,
otherwise `context.definition`.
Set `positive_example` to the first positive target,
`negative_example` to the first negative `correct_guidance`, `reason`,
or `bad_target` in that order,
and `evidence_refs` to the canonical provenance reference set.
When a non-approved entry has no canonical example of a given type,
serialize that example cell as empty.
Serialize `blocking` as lowercase `true` or `false`.
TSV edits must become delta events;
do not merge TSV edits directly into canonical JSON.

## Blocking QA conditions

- Unscoped approved terms.
- Missing concept id, language, definition, context, examples, provenance,
  approval status, or reliability for approved terms.
- Conflicting preferred targets in the same scope.
- Forbidden target appears in the translation.
- Applicable approved term is missing without a waiver.
- `conflict`, `candidate`, or `needs_confirmation` terms affect final text.
- Job override lacks approval or explicit waiver.
- TBX/TSV exports do not match canonical JSON.
