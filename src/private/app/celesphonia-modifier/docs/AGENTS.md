# Celesphonia Modifier Documentation Agent Instructions

## Scope and precedence

These instructions apply under `docs/`. Apply the repository-root and project `AGENTS.md` files
first, then this file. The instruction hierarchy is cumulative; this file may add or tighten
documentation rules but may not weaken a parent rule.

Normative plans and operating documents govern their declared subject matter without overriding the
instruction hierarchy. Stop and resolve any conflict or unclear authority before editing.

## Documents as a control plane

Treat documentation as the durable control plane for intent, authority, evidence, decisions, and
handoff, not as a transcript of a conversation. State each document's audience, purpose, authority,
status, and relationship to more authoritative sources.

Give each decision or rule one authoritative home. Link to that source rather than copying it into
multiple documents. Keep durable principles in agent instructions and concrete mechanics in the
narrowest governing plan or operating document.

Use document roles consistently:

- plans preserve resumable intent, scope, acceptance, authority, risks, and stop conditions;
- reviews preserve evidence, independent judgment, findings, and their disposition;
- decision records preserve the rationale, owner, and consequences of a choice; and
- indexes provide navigation and lifecycle context but do not create authority by presence alone.

## Authority, truth, and provenance

Separate current normative truth from historical context, unresolved hypotheses, alternatives, and
superseded decisions. Do not present a proposal, planned artifact, or conversation statement as an
approved result.

Record enough provenance to evaluate a claim: its source, rationale, uncertainty, decision owner,
and immutable candidate or evidence when consequential. Preserve material disagreements and their
resolution rather than rewriting history to imply consensus.

Documentation authority belongs in this scope. Product scope and release authority remain
project-level decisions; documentation records those decisions faithfully but does not invent or
silently broaden them.

## Lifecycle and navigation

Use explicit lifecycle states: active normative, active subordinate, partially superseded,
historical supporting, and archived. Age alone never determines lifecycle.

Keep partially superseded material in place while any operative content remains. Mark the affected
scope prominently and link to the exact replacement. Archive only when no operative content remains,
while preserving stable provenance and a route to the replacement.

Keep links, indexes, titles, and authority descriptions consistent with the documents that exist.
Presence in an index is navigation, not proof that a gate passed.

Maintain a concise zero-context current handoff at the start of `docs/.copilot/README.md`. It must
identify the latest verified product gate, state whether any execution increment is currently
authorized, name the decision required from the user when work is blocked, and link to the exact
authoritative instructions, governing plan, and release evidence. It is a navigation layer and must
not duplicate or broaden their authority.

After a verified gate, supersession, or material stop decision, update that handoff in a separate
documentation-maintenance change when the gate's sole-path or immutable-record rules prevent the
index from changing in the gate commit. Do not rewrite historical plan or review lifecycle labels
merely because their named event later occurred; explain the resulting current state in the handoff
and bind it to immutable Git evidence.

## Privacy-safe representation

Apply data minimization. Repository documentation may contain only repository-safe abstractions,
aliases, aggregate facts, and evidence needed for its audience. Do not include private payloads,
paths, values, hashes, account metadata, or details that permit reconstruction of private evidence.

When private evidence informed a decision, record the safe conclusion, provenance class, and
limitations without exposing the evidence itself. Follow the project instruction for protected,
Git-ignored copies and original-data boundaries.

## Validation and independent review

Validate documentation in proportion to its claims. Check factual bindings, authority,
cross-references, lifecycle markings, privacy boundaries, formatting, and index integrity rather
than treating prose review as sufficient.

Material documentation receives holistic review from a fresh, independent context. The reviewer
examines the full exact candidate and governing sources, not only the latest edit or a conversation
summary. Resolve every finding or obtain an approved and persisted scope change, then repeat review
until `No findings`.

Use `docs/.copilot/plans/project-operating-model.md` and the narrowest governing plan for detailed
plan-persistence, release-gate, record, and Git procedures. Do not copy those mechanics into this
file.
