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
6. Original and private data is separated from working data. Agent, research, test, and evidence
   work uses preserved copies in protected, Git-ignored storage, minimizes access, and never
   operates on originals. Runtime writes to original user data require explicit governing safety
   authority.
7. Every completed material increment remains in progress until a fresh independent subagent
   reviews the full exact candidate and returns `No findings`.
8. Every finding, including documentation and non-blocking findings, is resolved or covered by a
   separately approved and persisted scope change before re-review.
9. Claims, review decisions, and release decisions bind to immutable candidates and evidence rather
   than moving state. Detailed record mechanics remain in the narrowest governing plan or operating
   document.
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
- receives the exact base, candidate commit, tree, governing plan, path set, and acceptance
  evidence;
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
5. Prepare `../reviews/agent-workflow-institutionalization-plan-review.md` as the sole staged
   change.
6. Give a fresh independent subagent the exact staged record blob and governing inputs. Resolve
   every finding and repeat until exact `No findings`.
7. Commit the reviewed blob unchanged as the only child change, verify it locally, push it, and
   verify the published state.

The plan-review record must contain:

- the final plan candidate commit, tree, and completed A1 baseline;
- this plan path and the complete governing-input list from step 3;
- the reviewed path set and W0 plan acceptance criteria;
- reviewer identity and independence attestation;
- every review iteration, finding disposition, and final exact `No findings`;
- plan validation procedures and outcomes; and
- requirements that the record commit use the final plan candidate as its first parent and change
  only the plan-review record.

The record has exactly these metadata lines in this order and no other `**Key:**` metadata line:

```text
**Increment:** W0 plan
**Outcome:** Execution ready
**Final independent result:** `No findings`
**Plan commit:** `<full-final-plan-commit>`
**Plan tree:** `<full-final-plan-tree>`
**Completed A1 baseline:** `<full-A1-release-record-commit>`
**Governing plan:** `../plans/agent-workflow-institutionalization-plan.md`
**Reviewed candidate path:** `../plans/agent-workflow-institutionalization-plan.md`
**Reviewed candidate path:** `../README.md`
**Independent reviewer:** `<fresh-subagent-identifier>`
**Reviewer independence:** Confirmed, including the staged record blob
**Review iterations and finding dispositions:** Recorded
**Acceptance decision:** Passed
**Acceptance evidence:** Recorded
**Validation decision:** Passed
**Validation evidence:** Recorded
**Private evidence:** None accessed or recorded.
```

The record has exactly one of each of these sections:

```text
## Exact-plan binding
## Reviewer independence
## Finding disposition
## Reviewed inputs and paths
## Acceptance evidence
## Validation evidence
## Private-evidence statement
## Execution decision
```

They provide the complete governing-input list, acceptance evidence, review iterations, finding
dispositions, validation outcomes, privacy explanation, and execution decision. The fixed fields
bind key claims mechanically; staged-blob review verifies their complete audit context.

Plan authorization is accepted only when:

1. the final plan candidate is a descendant of the completed A1 baseline and changes exactly this
   plan and its `.copilot` index;
2. the recorded candidate tree equals the tree resolved directly from the final plan candidate;
3. ref-bound HK and Git diff checks pass for the complete plan-candidate range;
4. this plan and its index contain only English, repository-safe content, use LF with one final
   newline, and have no line longer than 100 characters;
5. a fresh independent reviewer reports exact `No findings` for the complete candidate;
6. the plan-review record contains every required field above, has the final plan candidate as its
   first parent, and changes only the declared record path; and
7. the final plan candidate and record are reachable from upstream, the record equals the expected
   upstream tip after its push, and the tracked worktree is clean.

Section 8 defines the executable plan-candidate and record checks.

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
4. Prepare `../reviews/agent-workflow-institutionalization-release-gate.md` as the sole staged
   change.
5. Give a fresh independent subagent the exact staged record blob and governing inputs. Resolve
   every finding and repeat until exact `No findings`.
6. Commit the reviewed blob unchanged, verify it locally, push it, then verify candidate and record
   reachability, tree, first parent, changed path, upstream equality, and a clean tracked worktree.

The release record has exactly these metadata lines in this order and no other `**Key:**` metadata
line:

```text
**Increment:** W0 - Agent Workflow Institutionalization
**Outcome:** Passed
**Final independent result:** `No findings`
**Candidate commit:** `<full-implementation-candidate-commit>`
**Candidate tree:** `<full-implementation-candidate-tree>`
**Governing plan:** `../plans/agent-workflow-institutionalization-plan.md`
**Persisted plan commit:** `<full-final-plan-commit>`
**Plan-review record and implementation diff base:** `<full-plan-review-record-commit>`
**Reviewed candidate path:** `../../../AGENTS.md`
**Reviewed candidate path:** `../../AGENTS.md`
**Independent reviewer:** `<fresh-subagent-identifier>`
**Reviewer independence:** Confirmed, including the staged record blob
**Review iterations and finding dispositions:** Recorded
**Acceptance decision:** Passed
**Acceptance evidence:** Recorded
**Validation decision:** Passed
**Validation evidence:** Recorded
**Private evidence:** None accessed or recorded.
```

It uses the same exact section set as the plan-review record, with `## Exact-candidate binding`
replacing `## Exact-plan binding`. Those sections provide every remaining operating-model field and
the audit context for the machine-checkable claims.

## 7. Acceptance criteria

W0 is accepted only when:

1. both scoped `AGENTS.md` files exist at the exact paths in section 1;
2. their instructions do not weaken or conflict with the root `AGENTS.md`;
3. the two files collectively capture all eleven established principles in section 2 at the meta
   level, with documentation authority, lifecycle, provenance, and indexing owned only by
   `docs/AGENTS.md` while product and release authority remain at project scope;
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

### Plan authorization

After committing and pushing a plan candidate, run from the repository root:

```powershell
$planBase = "cdde3a0427765c9f2b969e3e678550e4f7d78edb"
$planCandidate = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the plan candidate." }
$planTree = git rev-parse "$planCandidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the plan tree." }
$planPath = "src/private/app/celesphonia-modifier/docs/.copilot/plans/" +
  "agent-workflow-institutionalization-plan.md"
$indexPath = "src/private/app/celesphonia-modifier/docs/.copilot/README.md"
$paths = @($planPath, $indexPath)
$expectedChanges = @("A`t$planPath", "M`t$indexPath") | Sort-Object -CaseSensitive

git merge-base --is-ancestor $planBase $planCandidate
if ($LASTEXITCODE -ne 0) { throw "The plan candidate is not based on completed A1." }
$upstream = git rev-parse '@{u}'
if ($LASTEXITCODE -ne 0 -or $upstream -ne $planCandidate) { throw "Plan is not published." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean." }
$actual = @(git diff --no-renames --name-status $planBase $planCandidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the plan candidate paths." }
$actual = $actual | Sort-Object -CaseSensitive
if (Compare-Object $expectedChanges $actual -CaseSensitive) {
  throw "The plan candidate changed an undeclared path."
}

mise exec -- hk check --check --no-progress --from-ref $planBase --to-ref $planCandidate
if ($LASTEXITCODE -ne 0) { throw "HK rejected the plan candidate." }
git --no-pager diff --check $planBase $planCandidate
if ($LASTEXITCODE -ne 0) { throw "Git rejected the plan candidate diff." }

$violations = foreach ($path in $paths) {
  $lineNumber = 0
  Get-Content -LiteralPath $path | ForEach-Object {
    $lineNumber++
    if ($_.Length -gt 100) {
      "{0}:{1}" -f $path, $lineNumber
    }
  }
}
if ($violations) {
  throw "Plan lines exceed 100 characters: $($violations -join ', ')"
}
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Validation changed the tracked worktree." }
```

After the final plan reviewer returns exact `No findings`, create and stage the plan-review record.
Verify it with this procedure:

```powershell
$recordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-plan-review.md"
$staged = @(git diff --cached --no-renames --name-status HEAD)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate staged record changes." }
if (Compare-Object @("A`t$recordPath") $staged -CaseSensitive) {
  throw "The staged plan-review record is not the sole added path."
}
$unstaged = @(git diff --no-renames --name-status)
if ($LASTEXITCODE -ne 0 -or $unstaged) { throw "Tracked unstaged changes exist." }
$reviewedRecordBlob = git rev-parse ":$recordPath"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the staged record blob." }
```

Give a fresh independent subagent that exact staged blob, its blob identifier, the final plan
candidate, and all governing inputs. Resolve every finding and repeat until the record reviewer
returns exact `No findings`. Re-run the procedure after each remediation and do not change the final
reviewed staged blob afterward.

Commit the reviewed blob unchanged. In a fresh PowerShell process, supply only the reviewed staged
blob identifier and record-reviewer identifier, then run:

```powershell
$planBase = "cdde3a0427765c9f2b969e3e678550e4f7d78edb"
$reviewedRecordBlob = "<full-reviewed-staged-blob-identifier>"
$recordReviewer = "<fresh-record-review-subagent-identifier>"
$planPath = "src/private/app/celesphonia-modifier/docs/.copilot/plans/" +
  "agent-workflow-institutionalization-plan.md"
$indexPath = "src/private/app/celesphonia-modifier/docs/.copilot/README.md"
$recordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-plan-review.md"
$record = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the plan-review record." }
$planCandidate = git rev-parse "$record^1"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the final plan candidate." }
$planTree = git rev-parse "$planCandidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the final plan tree." }
git merge-base --is-ancestor $planBase $planCandidate
if ($LASTEXITCODE -ne 0) { throw "The plan candidate is not based on completed A1." }
$planChanges = @(git diff --no-renames --name-status $planBase $planCandidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the final plan paths." }
$expectedPlanChanges = @("A`t$planPath", "M`t$indexPath") | Sort-Object -CaseSensitive
$planChanges = $planChanges | Sort-Object -CaseSensitive
if (Compare-Object $expectedPlanChanges $planChanges -CaseSensitive) {
  throw "The final plan candidate changed an undeclared path."
}
$actual = @(git diff --no-renames --name-status $planCandidate $record)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the plan-review record paths." }
if (Compare-Object @("A`t$recordPath") $actual -CaseSensitive) {
  throw "The plan-review record changed an undeclared path."
}
$committedRecordBlob = git rev-parse "${record}:$recordPath"
if ($LASTEXITCODE -ne 0 -or $committedRecordBlob -ne $reviewedRecordBlob) {
  throw "The committed plan-review record is not the independently reviewed blob."
}
$recordLines = @(git show "${record}:$recordPath")
if ($LASTEXITCODE -ne 0) { throw "Could not read the committed plan-review record." }
$expectedMetadata = @(
  '**Increment:** W0 plan',
  '**Outcome:** Execution ready',
  '**Final independent result:** `No findings`',
  ('**Plan commit:** `{0}`' -f $planCandidate),
  ('**Plan tree:** `{0}`' -f $planTree),
  ('**Completed A1 baseline:** `{0}`' -f $planBase),
  '**Governing plan:** `../plans/agent-workflow-institutionalization-plan.md`',
  '**Reviewed candidate path:** `../plans/agent-workflow-institutionalization-plan.md`',
  '**Reviewed candidate path:** `../README.md`',
  ('**Independent reviewer:** `{0}`' -f $recordReviewer),
  '**Reviewer independence:** Confirmed, including the staged record blob',
  '**Review iterations and finding dispositions:** Recorded',
  '**Acceptance decision:** Passed',
  '**Acceptance evidence:** Recorded',
  '**Validation decision:** Passed',
  '**Validation evidence:** Recorded',
  '**Private evidence:** None accessed or recorded.'
)
$actualMetadata = @($recordLines | Where-Object { $_ -match '^\*\*[^*]+:\*\*' })
if (Compare-Object $expectedMetadata $actualMetadata -CaseSensitive -SyncWindow 0) {
  throw "The plan-review record metadata is missing, reordered, duplicated, or incorrect."
}
$requiredHeadings = @(
  '## Exact-plan binding',
  '## Reviewer independence',
  '## Finding disposition',
  '## Reviewed inputs and paths',
  '## Acceptance evidence',
  '## Validation evidence',
  '## Private-evidence statement',
  '## Execution decision'
)
$actualHeadings = @($recordLines | Where-Object { $_ -match '^## ' })
if (Compare-Object $requiredHeadings $actualHeadings -CaseSensitive -SyncWindow 0) {
  throw "Plan-review record sections are missing, reordered, duplicated, or undeclared."
}
mise exec -- hk check --check --no-progress --from-ref $planCandidate --to-ref $record
if ($LASTEXITCODE -ne 0) { throw "HK rejected the plan-review record." }
git --no-pager diff --check $planCandidate $record
if ($LASTEXITCODE -ne 0) { throw "Git rejected the plan-review record diff." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean." }

git push
if ($LASTEXITCODE -ne 0) { throw "Could not publish the plan-review record." }
$upstream = git rev-parse '@{u}'
if ($LASTEXITCODE -ne 0 -or $upstream -ne $record) {
  throw "The plan-review record is not the published tip."
}
git merge-base --is-ancestor $planCandidate $upstream
if ($LASTEXITCODE -ne 0) { throw "The plan candidate is not reachable from upstream." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean after publication." }
```

### Implementation candidate

After committing an implementation candidate, run from the repository root:

```powershell
$planBase = "cdde3a0427765c9f2b969e3e678550e4f7d78edb"
$baseInput = "<verified-plan-review-record-commit>"
$base = git rev-parse --verify "$baseInput^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the full plan-review record commit." }
$planPath = "src/private/app/celesphonia-modifier/docs/.copilot/plans/" +
  "agent-workflow-institutionalization-plan.md"
$indexPath = "src/private/app/celesphonia-modifier/docs/.copilot/README.md"
$planRecordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-plan-review.md"
$candidate = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the implementation candidate." }
$candidateTree = git rev-parse "$candidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the implementation tree." }
$projectAgents = "src/private/app/celesphonia-modifier/AGENTS.md"
$docsAgents = "src/private/app/celesphonia-modifier/docs/AGENTS.md"
$paths = @($projectAgents, $docsAgents)
$expectedChanges = @("A`t$projectAgents", "A`t$docsAgents") | Sort-Object -CaseSensitive

$planCandidate = git rev-parse "$base^1"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the plan-review record parent." }
$baseChanges = @(git diff --no-renames --name-status $planCandidate $base)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the plan-review record paths." }
if (Compare-Object @("A`t$planRecordPath") $baseChanges -CaseSensitive) {
  throw "The implementation base is not a record-only child of its plan candidate."
}
git merge-base --is-ancestor $planBase $planCandidate
if ($LASTEXITCODE -ne 0) { throw "The persisted plan is not based on completed A1." }
$planChanges = @(git diff --no-renames --name-status $planBase $planCandidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the persisted plan paths." }
$expectedPlanChanges = @("A`t$planPath", "M`t$indexPath") | Sort-Object -CaseSensitive
$planChanges = $planChanges | Sort-Object -CaseSensitive
if (Compare-Object $expectedPlanChanges $planChanges -CaseSensitive) {
  throw "The persisted plan changed an undeclared path."
}
$planTree = git rev-parse "$planCandidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the persisted plan tree." }
$planRecordLines = @(git show "${base}:$planRecordPath")
if ($LASTEXITCODE -ne 0) { throw "Could not read the plan-review record." }
$requiredPlanBindings = @(
  '**Final independent result:** `No findings`',
  ('**Plan commit:** `{0}`' -f $planCandidate),
  ('**Plan tree:** `{0}`' -f $planTree),
  ('**Completed A1 baseline:** `{0}`' -f $planBase),
  '**Acceptance decision:** Passed',
  '**Validation decision:** Passed'
)
$missingPlanBindings = @($requiredPlanBindings | Where-Object {
    $planRecordLines -cnotcontains $_
  })
if ($missingPlanBindings) { throw "The implementation base is not a verified plan-review record." }
git merge-base --is-ancestor $base $candidate
if ($LASTEXITCODE -ne 0) { throw "The candidate is not based on the plan-review record." }
$upstream = git rev-parse '@{u}'
if ($LASTEXITCODE -ne 0 -or $upstream -ne $candidate) { throw "Candidate is not published." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean." }
$actual = @(git diff --no-renames --name-status $base $candidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the implementation paths." }
$actual = $actual | Sort-Object -CaseSensitive
if (Compare-Object $expectedChanges $actual -CaseSensitive) {
  throw "The implementation candidate changed an undeclared path."
}

mise exec -- hk check --check --no-progress --from-ref $base --to-ref $candidate
if ($LASTEXITCODE -ne 0) { throw "HK rejected the implementation candidate." }
git --no-pager diff --check $base $candidate
if ($LASTEXITCODE -ne 0) { throw "Git rejected the implementation candidate diff." }

$violations = foreach ($path in $paths) {
  $lineNumber = 0
  Get-Content -LiteralPath $path | ForEach-Object {
    $lineNumber++
    if ($_.Length -gt 100) {
      "{0}:{1}" -f $path, $lineNumber
    }
  }
}
if ($violations) {
  throw "Instruction lines exceed 100 characters: $($violations -join ', ')"
}
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Validation changed the tracked worktree." }
```

The cumulative candidate diff from `$base` must contain exactly:

```text
src/private/app/celesphonia-modifier/AGENTS.md
src/private/app/celesphonia-modifier/docs/AGENTS.md
```

After exact `No findings` for the implementation candidate, create and stage the release record.
Verify it with this procedure:

```powershell
$recordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-release-gate.md"
$staged = @(git diff --cached --no-renames --name-status HEAD)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate staged release-record changes." }
if (Compare-Object @("A`t$recordPath") $staged -CaseSensitive) {
  throw "The staged release record is not the sole added path."
}
$unstaged = @(git diff --no-renames --name-status)
if ($LASTEXITCODE -ne 0 -or $unstaged) { throw "Tracked unstaged changes exist." }
$reviewedRecordBlob = git rev-parse ":$recordPath"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the staged release-record blob." }
```

Give a fresh independent subagent that exact staged blob, its blob identifier, the implementation
candidate, and all governing inputs. Resolve every finding and repeat until the record reviewer
returns exact `No findings`. Re-run the procedure after each remediation and do not change the final
reviewed staged blob afterward.

Commit the reviewed blob unchanged. In a fresh PowerShell process, supply only the verified
plan-review record, reviewed staged blob, and record-reviewer identifiers, then run:

```powershell
$planBase = "cdde3a0427765c9f2b969e3e678550e4f7d78edb"
$baseInput = "<verified-plan-review-record-commit>"
$base = git rev-parse --verify "$baseInput^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the full plan-review record commit." }
$reviewedRecordBlob = "<full-reviewed-staged-blob-identifier>"
$recordReviewer = "<fresh-record-review-subagent-identifier>"
$planPath = "src/private/app/celesphonia-modifier/docs/.copilot/plans/" +
  "agent-workflow-institutionalization-plan.md"
$indexPath = "src/private/app/celesphonia-modifier/docs/.copilot/README.md"
$planRecordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-plan-review.md"
$projectAgents = "src/private/app/celesphonia-modifier/AGENTS.md"
$docsAgents = "src/private/app/celesphonia-modifier/docs/AGENTS.md"
$recordPath = "src/private/app/celesphonia-modifier/docs/.copilot/reviews/" +
  "agent-workflow-institutionalization-release-gate.md"
$record = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the release record." }
$candidate = git rev-parse "$record^1"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the implementation candidate." }
$candidateTree = git rev-parse "$candidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the implementation tree." }
$planCandidate = git rev-parse "$base^1"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the persisted plan candidate." }
$planTree = git rev-parse "$planCandidate^{tree}"
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the persisted plan tree." }
git merge-base --is-ancestor $planBase $planCandidate
if ($LASTEXITCODE -ne 0) { throw "The persisted plan is not based on completed A1." }
$planChanges = @(git diff --no-renames --name-status $planBase $planCandidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the persisted plan paths." }
$expectedPlanChanges = @("A`t$planPath", "M`t$indexPath") | Sort-Object -CaseSensitive
$planChanges = $planChanges | Sort-Object -CaseSensitive
if (Compare-Object $expectedPlanChanges $planChanges -CaseSensitive) {
  throw "The persisted plan changed an undeclared path."
}
$baseChanges = @(git diff --no-renames --name-status $planCandidate $base)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the plan-review record paths." }
if (Compare-Object @("A`t$planRecordPath") $baseChanges -CaseSensitive) {
  throw "The implementation base is not a record-only child of its plan candidate."
}
$planRecordLines = @(git show "${base}:$planRecordPath")
if ($LASTEXITCODE -ne 0) { throw "Could not read the plan-review record." }
$requiredPlanBindings = @(
  '**Final independent result:** `No findings`',
  ('**Plan commit:** `{0}`' -f $planCandidate),
  ('**Plan tree:** `{0}`' -f $planTree),
  ('**Completed A1 baseline:** `{0}`' -f $planBase),
  '**Acceptance decision:** Passed',
  '**Validation decision:** Passed'
)
$missingPlanBindings = @($requiredPlanBindings | Where-Object {
    $planRecordLines -cnotcontains $_
  })
if ($missingPlanBindings) { throw "The base is not the verified plan-review record." }
git merge-base --is-ancestor $base $candidate
if ($LASTEXITCODE -ne 0) { throw "The candidate is not based on the plan-review record." }
$candidateChanges = @(git diff --no-renames --name-status $base $candidate)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the implementation candidate paths." }
$expectedCandidateChanges = @(
  "A`t$projectAgents",
  "A`t$docsAgents"
) | Sort-Object -CaseSensitive
$candidateChanges = $candidateChanges | Sort-Object -CaseSensitive
if (Compare-Object $expectedCandidateChanges $candidateChanges -CaseSensitive) {
  throw "The implementation candidate changed an undeclared path."
}
$actual = @(git diff --no-renames --name-status $candidate $record)
if ($LASTEXITCODE -ne 0) { throw "Could not enumerate the release-record paths." }
if (Compare-Object @("A`t$recordPath") $actual -CaseSensitive) {
  throw "The release record changed an undeclared path."
}
$committedRecordBlob = git rev-parse "${record}:$recordPath"
if ($LASTEXITCODE -ne 0 -or $committedRecordBlob -ne $reviewedRecordBlob) {
  throw "The committed release record is not the independently reviewed blob."
}
$recordLines = @(git show "${record}:$recordPath")
if ($LASTEXITCODE -ne 0) { throw "Could not read the committed release record." }
$expectedMetadata = @(
  '**Increment:** W0 - Agent Workflow Institutionalization',
  '**Outcome:** Passed',
  '**Final independent result:** `No findings`',
  ('**Candidate commit:** `{0}`' -f $candidate),
  ('**Candidate tree:** `{0}`' -f $candidateTree),
  '**Governing plan:** `../plans/agent-workflow-institutionalization-plan.md`',
  ('**Persisted plan commit:** `{0}`' -f $planCandidate),
  ('**Plan-review record and implementation diff base:** `{0}`' -f $base),
  '**Reviewed candidate path:** `../../../AGENTS.md`',
  '**Reviewed candidate path:** `../../AGENTS.md`',
  ('**Independent reviewer:** `{0}`' -f $recordReviewer),
  '**Reviewer independence:** Confirmed, including the staged record blob',
  '**Review iterations and finding dispositions:** Recorded',
  '**Acceptance decision:** Passed',
  '**Acceptance evidence:** Recorded',
  '**Validation decision:** Passed',
  '**Validation evidence:** Recorded',
  '**Private evidence:** None accessed or recorded.'
)
$actualMetadata = @($recordLines | Where-Object { $_ -match '^\*\*[^*]+:\*\*' })
if (Compare-Object $expectedMetadata $actualMetadata -CaseSensitive -SyncWindow 0) {
  throw "The release record metadata is missing, reordered, duplicated, or incorrect."
}
$requiredHeadings = @(
  '## Exact-candidate binding',
  '## Reviewer independence',
  '## Finding disposition',
  '## Reviewed inputs and paths',
  '## Acceptance evidence',
  '## Validation evidence',
  '## Private-evidence statement',
  '## Execution decision'
)
$actualHeadings = @($recordLines | Where-Object { $_ -match '^## ' })
if (Compare-Object $requiredHeadings $actualHeadings -CaseSensitive -SyncWindow 0) {
  throw "Release-record sections are missing, reordered, duplicated, or undeclared."
}
mise exec -- hk check --check --no-progress --from-ref $candidate --to-ref $record
if ($LASTEXITCODE -ne 0) { throw "HK rejected the release record." }
git --no-pager diff --check $candidate $record
if ($LASTEXITCODE -ne 0) { throw "Git rejected the release-record diff." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean." }

git push
if ($LASTEXITCODE -ne 0) { throw "Could not publish the release record." }
$upstream = git rev-parse '@{u}'
if ($LASTEXITCODE -ne 0 -or $upstream -ne $record) {
  throw "The release record is not the published tip."
}
git merge-base --is-ancestor $candidate $upstream
if ($LASTEXITCODE -ne 0) { throw "The candidate is not reachable from upstream." }
git merge-base --is-ancestor $base $upstream
if ($LASTEXITCODE -ne 0) { throw "The plan-review record is not reachable from upstream." }
$status = git status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $status) { throw "Tracked worktree is not clean after publication." }
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
metadata, or private evidence. If private data becomes necessary for later agent, research, test, or
evidence work, use preserved copies in protected, Git-ignored storage and never use originals.

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
