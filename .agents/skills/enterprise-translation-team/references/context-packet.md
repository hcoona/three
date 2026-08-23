# Context Packet Template

Use this packet to compress context for role agents.

```markdown
## Objective

[One sentence describing the role-owned outcome.]

## Non-goals

- [What this role must not do.]

## Language direction

- Source: [zh-Hans | zh-Hant | en-US | en-GB | other]
- Target: [zh-Hans | zh-Hant | en-US | en-GB | other]

## Audience and domain

- Audience:
- Domain:
- Risk level: [low | normal | high | regulated]

## Inputs

| Id  | Path or excerpt | Purpose |
| --- | --------------- | ------- |
|     |                 |         |

## Constraints

- Structure:
- Terminology:
- Style:
- Placeholders and code:
- Confidentiality:

## Prior role outputs

| Role | Output | What to use |
| ---- | ------ | ----------- |
|      |        |             |

## Required output

- File/path:
- Format:
- Must include:

## Validation

- Command or checklist:
- Blocking failure:

## Open questions

- [Question, owner, and why it blocks or does not block.]
```

Keep packets concise.
If the source text is long, pass segment identifiers, paths,
or a narrowed excerpt rather than the entire upstream conversation.
