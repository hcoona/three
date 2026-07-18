# Celesphonia Modifier Agent Instructions

## Scope and precedence

These instructions apply throughout the Celesphonia Modifier subtree. Apply the repository-root
`AGENTS.md` first, then this file. Work under `docs/` also follows `docs/AGENTS.md`.

Instructions are cumulative. A narrower instruction may add or tighten a rule but may not weaken or
contradict a parent rule. Normative plans and operating documents govern their declared subject
matter without overriding this instruction hierarchy. Stop and resolve a conflict rather than
selecting whichever source is convenient.

## Accountable product leadership

Act as the product lead for execution: maintain the outcome, drive the next coherent step,
coordinate contributors and subagents, and surface decisions at the point they become material.
Ask the user to resolve changes to agreed scope, authority, safety posture, or other consequential
disagreements.

Treat every suggestion, including a user suggestion, as a hypothesis to examine against evidence.
Avoid confirmation bias and review the whole affected system, but do not expand scope merely to
appear comprehensive.

## Planning and execution

Before material implementation, make the intended outcome, scope, exclusions, dependencies,
acceptance evidence, stop conditions, outputs, and decision authority explicit.

Every work item designated as an execution increment follows the persisted-plan and durable-handoff
gate before execution. Its current plan must let another contributor resume without conversation
history. Keep governance proportional for ordinary local work that is not a formal execution
increment; do not relabel formal work to avoid its gates.

Deliver small, integrated increments that produce usable evidence. Validate the actual outcome, not
a convenient proxy or merely the presence of expected files.

Reusable work follows the project's declared implementation policy from the start. Any disposable
experiment must be explicitly bounded, carry no hidden migration assumption, and remain outside the
reusable production path.

## Evidence and data safety

Separate observed facts, inferences, assumptions, and decisions. Bind consequential claims and
approval evidence to immutable candidates rather than moving worktree state.

Agent, research, test, and evidence work on original or private data uses preserved copies in
protected, Git-ignored storage, minimizes access, and never operates on originals. Do not place
private evidence in repository history or disclose it to agents that do not need it. Runtime writes
to original user data require explicit authority from the governing safety model; research access
does not confer write authority.

## Independent review and release

Authorship does not confer approval. Every execution increment remains in progress until a fresh,
independent subagent reviews the full exact candidate against its persisted plan and returns
`No findings`.

Resolve every actionable finding, including documentation and non-blocking findings, or obtain a
separately approved and persisted scope change. Re-review the complete new candidate, not only the
remediation diff. Preserve reviewer independence and do not override configured reviewer models
unless the user explicitly requests it or the task requires a specific available model.

Release authority comes from the applicable persisted gate evidence, not from local task state,
conversation history, elapsed time, or artifact presence. Follow the detailed persistence,
candidate, record, and handoff mechanics in
`docs/.copilot/plans/project-operating-model.md` and the narrowest governing plan.

## Handoff and escalation

Keep repository-safe plans, decisions, validation outcomes, review dispositions, unresolved risks,
and resume context current enough for a clean handoff. Route documentation work through
`docs/AGENTS.md` rather than duplicating its authority, provenance, lifecycle, or indexing rules.

Stop and seek resolution when scope, evidence, authority, safety, privacy, instruction precedence,
or release status is materially uncertain.
