# MQM Review Contract

Use this compact MQM-style taxonomy for bilingual review findings.

| Category          | Use when                                                                                  |
| ----------------- | ----------------------------------------------------------------------------------------- |
| `Accuracy`        | The target changes, omits, adds, or misrepresents source meaning.                         |
| `Fluency`         | The target has grammar, readability, punctuation, or unnatural phrasing problems.         |
| `Terminology`     | A required glossary term, product name, acronym, or domain term is wrong or inconsistent. |
| `Style`           | Register, brand voice, tone, or audience fit is wrong.                                    |
| `Locale`          | Date, number, currency, punctuation, unit, or regional convention is wrong.               |
| `Non-translation` | The target is too garbled or unrelated to inspect reliably.                               |

Severity values:

| Severity  | Meaning                                                                            |
| --------- | ---------------------------------------------------------------------------------- |
| `Major`   | Meaning, compliance, safety, legal, or publication quality is materially affected. |
| `Minor`   | Quality is degraded but the intended meaning remains recoverable.                  |
| `Neutral` | Preference, note, or non-blocking observation.                                     |

Required issue fields:

```json
{
    "issue_id": "mqm-S1-001",
    "segment_id": "S1",
    "category": "Accuracy",
    "severity": "Major",
    "source_quote": "...",
    "target_quote": "...",
    "explanation": "...",
    "proposed_fix": "...",
    "resolution_status": "open"
}
```

`issue_id` must be unique and stable across revision.
`resolution_status` must be `open`, `resolved`, or `waived`.
The reviser initially records findings as `open`.
Changing a finding to `resolved` requires non-empty `resolution_evidence`
that identifies the applied change.
Changing a finding to `waived` requires non-empty `resolution_evidence`
and `waiver_ref`.

Every `Major` finding must include a concrete source quote, target quote,
and proposed fix.
If a finding depends on domain knowledge,
mark the required human or subject-matter expert confirmation.
