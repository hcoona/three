# Celesphonia Modifier Agent Workflow Institutionalization Plan

**Status:** Proposed execution plan; implementation requires exact-plan independent review

**Increment:** W0 - Scoped Agent Instructions

**Dependency:** A1 release record
`cdde3a0427765c9f2b969e3e678550e4f7d78edb`

## 1. Purpose

W0 converts the project's established working agreements into general workflow principles that
future agents load automatically from the repository. It creates one project-level instruction
file and one documentation-level instruction file:

- `src/private/app/celesphonia-modifier/AGENTS.md`; and
- `src/private/app/celesphonia-modifier/docs/AGENTS.md`.

The project-level file governs general product and engineering work in the subtree. The
documentation-level file adds rules for files under `docs/`, including `docs/.copilot/`.

These files are written for agents, not end-user documentation. They must state durable
meta-principles: how to reason about work, evidence, handoff, authority, and review. They must not
become an Atlas runbook, a W0 checklist, or a copy of one increment's commands and record fields.

## 2. Established decisions to preserve

The instructions must abstract the decisions established through the current project session and
the persisted operating model into reusable principles:

1. Copilot acts as the product lead, drives execution, coordinates subagents, and asks the user to
   resolve scope changes or material disagreements.
2. Suggestions, including user suggestions, are hypotheses to examine without confirmation bias.
   Review must be comprehensive but must not expand scope merely to appear comprehensive.
3. Outcomes, scope, exclusions, acceptance evidence, and stop conditions are explicit before
   material implementation begins.
4. Planned or material work is persisted before execution so another contributor can resume
   without conversation history. Governance remains proportional for small, local changes.
5. Reusable work follows the project's declared implementation policy from the start. Exceptions
   are explicit, disposable, and carry no hidden migration assumption.
6. Original and private data is separated from working data. Work uses preserved copies in
   protected, Git-ignored storage, minimizes access, and never operates on originals.
7. Every completed material increment remains in progress until a fresh independent subagent
   reviews the full exact candidate and returns `No findings`.
8. Every finding, including documentation and non-blocking findings, is resolved or covered by a
   separately approved and persisted scope change before re-review.
9. Claims, review decisions, and release decisions bind to immutable candidates and evidence rather
   than moving state. Detailed record mechanics remain in the operating model.
10. Document lifecycle uses explicit active, subordinate, superseded, historical, and archived
    states; age alone never causes archival.
11. Detailed mechanics live in the narrowest authoritative plan or operating document. The
    `AGENTS.md` files state principles and route agents to those details without duplicating them.

The two files collectively preserve these principles. The project file routes documentation work
to the documentation file; it does not repeat documentation lifecycle, provenance, or indexing
rules.

The new instructions inherit the root `AGENTS.md` and refer to it instead of copying changing tool
or layout snapshots. They must not broaden its Windows rule: C# projects use Windows runners in
GitHub workflows.

## 3. Scope and exclusions

### In scope

- General meta-principles for planning, execution, validation, review, release, handoff, and data
  safety in the Celesphonia Modifier subtree.
- Documentation meta-principles for authority, truth, lifecycle, provenance, privacy, validation,
  and indexing in the `docs/` subtree.
- Explicit instruction precedence and links to detailed normative documents.
- Fresh-context independent review requirements for material plans and exact candidates.

### Out of scope

- Changing product, Atlas, save-format, UI, or release architecture.
- Starting A2 or modifying A1 source, projects, tests, or lock files.
- Creating a repository-wide workflow outside this subproject.
- Copying conversation transcripts or private evidence into the repository.
- Restating technical requirements already governed by a detailed plan.
- Embedding increment names, commit identifiers, current status, file manifests, command scripts,
  package choices, or implementation-specific checklists in either `AGENTS.md`.
- Adding generated configuration, tooling, packages, scripts, or private artifacts.

## 4. Instruction allocation

Instruction hierarchy is cumulative:

1. The repository-root `AGENTS.md` applies first.
2. The project `AGENTS.md` adds or narrows rules for the Celesphonia Modifier subtree.
3. `docs/AGENTS.md` adds or narrows rules for the documentation subtree.
4. A narrower file may not weaken or contradict a parent instruction.

Normative plans and operating documents govern their declared subject matter, but they do not
override the `AGENTS.md` instruction hierarchy. An agent stops and resolves any conflict instead of
choosing whichever source is convenient.

### Project `AGENTS.md`

The project-level instructions must cover:

- scope and precedence;
- accountable product-lead behavior and material user decision points;
- evidence-first framing of outcomes, scope, acceptance, and stop conditions;
- proportional persistence and durable handoff;
- adherence to declared implementation policy with explicit disposable exceptions;
- original-data, private-data, and repository-boundary safety;
- small integrated increments and validation against the actual outcome;
- authorship and approval separation through clean-context independent review;
- immutable-candidate evidence and explicit release authority; and
- escalation when scope, evidence, authority, or safety is materially uncertain.

It must direct documentation work to `docs/AGENTS.md` rather than duplicating that file.

### Documentation `AGENTS.md`

The documentation-level instructions must cover:

- documents as a durable control plane rather than a conversation transcript;
- audience, purpose, authority, status, and precedence;
- one authoritative home for each decision and links instead of copied rules;
- plans as resumable intent, reviews as evidence and adjudication, and indexes as navigation;
- separation of current truth from history and unresolved hypotheses;
- provenance, rationale, uncertainty, and decision ownership;
- privacy-safe representation and data minimization;
- explicit lifecycle and supersession rather than age-based archival;
- link and index integrity;
- validation proportionate to the document's claims; and
- clean-context holistic review of material documentation.

It must link to the project operating model for concrete gate mechanics rather than copying the
full model.

## 5. Required review model

The final instructions express reviewer independence as a meta-principle. The W0 execution applies
that principle concretely: an independent reviewer:

- did not author or materially shape the artifact under review;
- starts from a fresh subagent context for that review cycle;
- receives the exact base, candidate commit, tree, governing plan, path set, and acceptance evidence;
- reads the persisted artifacts and complete diff rather than relying on a conversation summary;
- reviews correctness, omissions, contradictions, safety, privacy, scope, validation, and handoff;
- reports every actionable finding with evidence and remediation; and
- returns exactly `No findings` only when no finding remains.

Re-review examines the complete new candidate, not only the remediation diff. Configured subagent
models are not overridden unless the user explicitly requests it or a task requires a specific
available model. The final `AGENTS.md` files point to scoped plans for exact record shapes and Git
procedures instead of embedding this W0-specific protocol.

## 6. Implementation sequence

### W0.1 Persist and approve this plan

1. Add this plan and index it in `docs/.copilot/README.md`.
2. Commit and push the exact plan candidate.
3. Give a fresh independent subagent the persisted plan, root `AGENTS.md`,
   `project-operating-model.md`, and the lifecycle and privacy sections of
   `docs/.copilot/README.md`.
4. Resolve every finding and repeat with a fresh review context until `No findings`.
5. Persist `../reviews/agent-workflow-institutionalization-plan-review.md` as the only child change,
   then push and verify it.

The plan-review record must contain:

- the final plan candidate commit, tree, and completed A1 baseline;
- this plan path and the complete governing-input list from step 3;
- the reviewed path set and W0 plan acceptance criteria;
- reviewer identity and independence attestation;
- every review iteration, finding disposition, and final exact `No findings`;
- plan validation procedures and outcomes; and
- requirements that the record commit use the final plan candidate as its first parent and change
  only the plan-review record.

The verified plan-review record commit becomes the implementation diff base.

W0 implementation may not begin before W0.1 completes.

### W0.2 Write scoped instructions

1. Use the verified plan-review record commit as the implementation diff base.
2. Create the project `AGENTS.md`.
3. Create `docs/AGENTS.md`.
4. Check the files against the approved W0 plan, root instructions, and operating model.
5. Validate formatting, links, privacy boundaries, and instruction hierarchy.

### W0.3 Review and release

1. Commit and push an implementation candidate containing exactly the two `AGENTS.md` files.
2. Give a fresh independent subagent the complete candidate and all governing inputs.
3. Resolve every finding, commit and push a new candidate, and repeat with fresh context until
   `No findings`.
4. Persist `../reviews/agent-workflow-institutionalization-release-gate.md` as the only child change.
5. Mechanically verify candidate, tree, first parent, changed path, upstream equality, and clean
   tracked worktree.

## 7. Acceptance criteria

W0 is accepted only when:

1. both scoped `AGENTS.md` files exist at the exact paths in section 1;
2. their instructions do not weaken or conflict with the root `AGENTS.md`;
3. the two files collectively capture all eleven established principles in section 2 at the meta
   level, with documentation lifecycle, provenance, authority, and indexing owned only by
   `docs/AGENTS.md`;
4. the documentation file defines durable authority, truth, provenance, lifecycle, privacy,
   navigation, validation, and review principles without duplicating detailed plans;
5. instruction precedence between root, project, and documentation scopes is explicit;
6. implementation and documentation rules are separated without leaving a workflow gap;
7. the files contain only English, repository-safe content and no private evidence;
8. neither file contains increment-specific state, identifiers, manifests, commands, or runbooks;
9. concrete mechanics are linked from the narrowest authoritative document instead of copied;
10. lines are at most 100 characters with LF endings and one final newline;
11. EditorConfig, typo, Markdown lint, and Prettier checks pass;
12. the implementation candidate changes exactly the two planned `AGENTS.md` paths;
13. a fresh independent reviewer reports `No findings` for the full exact candidate;
14. the plan-review record binds the exact accepted plan and becomes the implementation diff base;
15. the release record contains every operating-model field and is the only child change; and
16. every required commit is reachable from upstream, while the expected tip equals upstream after
    each required push.

## 8. Validation procedures

After committing an implementation candidate, run from the repository root:

```powershell
$base = "<verified-plan-review-record-commit>"
$candidate = git rev-parse HEAD
$projectAgents = "src\private\app\celesphonia-modifier\AGENTS.md"
$docsAgents = "src\private\app\celesphonia-modifier\docs\AGENTS.md"
$expected = @($projectAgents, $docsAgents) | Sort-Object
$actual = @(git diff --name-only $base $candidate) | Sort-Object

if (Compare-Object $expected $actual) {
  throw "The implementation candidate changed an undeclared path."
}

mise exec -- hk check --check --no-progress --from-ref $base --to-ref $candidate
git --no-pager diff --check $base $candidate

$violations = foreach ($path in $expected) {
  $lineNumber = 0
  Get-Content -LiteralPath $path | ForEach-Object {
    $lineNumber++
    if ($_.Length -gt 100) {
      "{0}:{1}" -f $path, $lineNumber
    }
  }
}
if ($violations) {
  throw "AGENTS.md lines exceed 100 characters: $($violations -join ', ')"
}
```

The cumulative candidate diff from `$base` must contain exactly:

```text
src/private/app/celesphonia-modifier/AGENTS.md
src/private/app/celesphonia-modifier/docs/AGENTS.md
```

No .NET build or test is required because W0 changes only Markdown instructions. If a review
requires a code, project, package, schema, or generated-file change, stop and revise this plan.

## 9. Outputs and privacy

Repository-safe outputs:

- this plan and its index entry;
- `../reviews/agent-workflow-institutionalization-plan-review.md`;
- the two scoped `AGENTS.md` files; and
- `../reviews/agent-workflow-institutionalization-release-gate.md`.

Private outputs: none.

The work may consult repository-safe session decisions and persisted project documents. It must not
open, summarize, copy, or identify private workspace contents, saves, game files, hashes, account
metadata, or private evidence. If private data ever becomes necessary in a later task, use
preserved copies in protected, Git-ignored storage and never use originals.

## 10. Authority and stop conditions

Copilot may make editorial decisions that preserve this plan. Ask the user before changing scope,
weakening a gate, moving a rule to a different audience, creating a repository-wide policy, or
resolving a material disagreement between the session decisions and persisted documents.

Stop and revise this plan if:

- a third `AGENTS.md` or another implementation path is required;
- the instructions would change product or Atlas behavior;
- a root repository rule must change;
- private evidence is needed;
- a required decision cannot be stated measurably;
- the two instruction scopes overlap or leave an unresolved gap;
- validation requires new tooling; or
- any independent reviewer has an unresolved finding.

## 11. Handoff and resume procedure

To resume:

1. read the root `AGENTS.md`;
2. read this plan and `project-operating-model.md`;
3. inspect the W0 plan-review and release records, if present;
4. verify `HEAD`, upstream, the tracked worktree, and the recorded candidate relationships;
5. continue only from the first incomplete W0 step; and
6. do not infer approval from conversation history or local task state.

The current implementation state is `pending` until the exact plan has been pushed and its
independent plan-review record has passed the record-only commit checks.
