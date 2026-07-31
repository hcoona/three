# Celesphonia Modifier Agent Instructions

## Scope and precedence

These instructions apply throughout the Celesphonia Modifier subtree. Apply the repository-root
`AGENTS.md` first, then this file. Work under `docs/` also follows `docs/AGENTS.md`.

Instructions are cumulative. A narrower instruction may add or tighten a rule but may not weaken or
contradict a parent rule. Normative plans and operating documents govern their declared subject
matter without overriding this instruction hierarchy. Stop and resolve a conflict rather than
selecting whichever source is convenient.

## Zero-context bootstrap and delegation

A contributor without conversation or session history starts at
`docs/.copilot/README.md`. Follow its current handoff and reading order before proposing work.
Tracked instructions, plans, reviews, release records, and Git history are authoritative;
conversation summaries, local task state, and retained session artifacts are not.

Do not infer a next increment from historical roadmaps, numbered sections, or the presence of
unreleased ideas. If the current handoff grants no execution authority, stop and ask the user to
select the next outcome before drafting its persisted plan.

For this project, delegate only to general-purpose agents using the GPT-5.6 model family unless the
user explicitly changes that policy. Do not use project-specific custom subagents. Give each
delegated agent complete repository-safe context, preserve reviewer independence, and never provide
private or Git-ignored save content.

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

## Planning correction

Before remediating a review round, run a planning-drift gate against the accepted outcome, claim,
in-scope scenarios, threat model, exclusions, and acceptance evidence. For a formal execution
increment or material candidate, use its persisted plan as the baseline. Re-derive the ideal minimal
shape before considering the current implementation. Do not turn every conceivable failure into an
in-scope requirement.

Treat each review finding as a hypothesis, not an instruction:

1. Make mixed findings atomic, then classify every finding as a true positive (`TP`) or false
   positive (`FP`) with evidence and a proportionately documented rationale.
2. A `TP` demonstrates an in-scope violation or an unmet acceptance criterion. Resolve it by
   correcting the defect or narrowing the claim. If the resolution changes a persisted outcome,
   claim, scope, threat model, exclusion, acceptance criterion, stop condition, output, authority,
   risk, or resume procedure, obtain approval and persist the revised plan before execution
   continues. Prefer deleting machinery or narrowing a claim over adding controls.
3. An `FP` depends on a false premise, an out-of-scope threat, or behavior already covered by the
   candidate. Do not change the candidate merely to silence it. For a formal execution increment or
   material candidate, record the disposition and obtain independent concurrence. For ordinary
   local work, a reasoned disposition is sufficient unless it is material or disputed. Escalate
   unresolved disagreement to the user.

Stop patching and return to planning when remediation would introduce a new process, persistent
state, protocol, recovery path, trust boundary, or threat assumption not required by the accepted
plan. Also reset to the ideal minimal design after two consecutive review rounds with structural
findings, or whenever complexity grows without corresponding acceptance evidence. Persist and
review any resulting material scope or architecture change before resuming execution.

## Evidence and data safety

Separate observed facts, inferences, assumptions, and decisions. Bind consequential claims and
approval evidence to immutable candidates rather than moving worktree state.

Agent, research, test, and evidence work on original or private data uses preserved copies in
protected, Git-ignored storage, minimizes access, and never operates on originals. Do not place
private evidence in repository history or disclose it to agents that do not need it. Runtime writes
to original user data require explicit authority from the governing safety model; research access
does not confer write authority.

## Runtime validation hygiene

Agent-driven runtime validation uses generated synthetic data only. Track every launched
application, debugger, observer, temporary package identity, and helper by its exact PID, window
handle, package name, or resolved path.

After validation, close each exact application process, wait for exit, verify that no child process
remains, unregister temporary package identities, and remove only the specifically resolved
synthetic fixtures and evidence artifacts created for that validation. Restore any temporarily
changed operating-system accessibility or display setting in a `finally` path. Never use broad
process-name termination or wildcard recursive deletion as cleanup.

## Independent review and release

Authorship does not confer approval. Every execution increment remains in progress until a fresh,
independent subagent reviews the full exact candidate against its persisted plan and returns
`No findings`.

Do not apply findings before the required `TP`/`FP` adjudication. Resolve every adjudicated `TP`,
including documentation and non-blocking findings, or obtain a separately approved and persisted
scope change. Return independently concurred `FP` dispositions to the review loop without changing
the candidate merely to silence them. Re-review the complete candidate and dispositions, not only
the remediation diff, until the reviewer returns `No findings`. Preserve reviewer independence and
do not override configured reviewer models unless the user explicitly requests it or the task
requires a specific available model.

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
