# Atlas V0 A0 Current Corpus Refresh

**Lifecycle:** Partially superseded; only the A0R3-imported policy sections remain operative

**Status:** Completed on first authorized safe refusal at `G0R1`

**Increment:** A0R1 - Current Corpus Refresh

**Decision owner:** Project leader

**Decision:** Reopen A0 and replace current corpus authority through a new exact private manifest

**Purpose:** Reconcile the approved A0 selection policy with the current installed tree, preserve
stable locator identities, and obtain project-leader approval of one exact refreshed baseline
manifest before any further A2 attempt.

**Implementation language:** Session-only C#

**Base:** `f4785dba8cd3a286af08ed804361a27c3a76144f`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-execution-plan.md`;
- `atlas-v0-a0-research-contract.md`; and
- `atlas-v0-a2-approved-manifest-authority-correction.md`.

**Trigger:** A2R13 completed its clean bootstrap but released discovery selected its safe-refusal
branch before pending-manifest publication. The repository-safe conclusion is only that the current
tree does not exactly reconcile with the approved baseline.

**Dependencies:** Verified shared A2R13 `G13`, the current strict approved A0 manifest and discovery
request, unchanged released Atlas source, approved `trusted-local-filesystem/v1`, independent plan
and source review, and explicit project-leader decisions over the refreshed corpus candidate and
final exact approved bytes.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-current-corpus-refresh-plan-review.md`

**Planned corpus-decision record:**
`../reviews/atlas-v0-a0-current-corpus-refresh-decision.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-current-corpus-refresh-completion.md`

> **Post-refusal governance correction**
> `atlas-v0-a0-current-corpus-refresh-governance-remediation.md` governed A0R1C1. The first authorized
> census consumed A0R1 authority on its safe-refusal branch. A later execution had no prospective
> authority and contributes no acceptance evidence. A0R1 is closed and all execution, publication,
> decision, finalization, approval, retry, and Git authority below is historical. A0R3 imports only
> section 2 corpus/privacy/threat-model constraints, section 4 metadata classification/selection/alias
> rules, and section 5 revision-3 pending schema/codec invariants.
>
> A0R2 completed at verified shared `G0R2` without a census or candidate.
> `atlas-v0-a0-approved-manifest-corpus-refresh.md` governs A0R3 prospectively. It does not reopen
> A0R1, diagnose A0R2, or authorize use of any prior execution result.

## 1. Authority correction and claim

The original A0 release remains immutable evidence for its observed tree and policy. A0R1 does not
edit that manifest, its workspace, or its historical release records. It creates a new current
authority candidate because A0 already requires any later root-entry difference to produce and
confirm a new discovery manifest.

The old approved manifest remains authoritative only for:

- the two save-root roles and root aliases;
- save classification rules;
- definition-group identifiers, selection rules, first-match order, and decisions;
- stable source aliases for locator identities still present;
- supported schema values, privacy policy, lifecycle policy, and terminal-status vocabulary; and
- the fixed survey alias and A0 approval provenance.

It is not authoritative for current membership or counts. The current observed tree supplies those
facts through one metadata-only census.

A0R1 claims only that one refreshed revision-3 baseline manifest:

1. is a complete terminal census of the current approved roots;
2. preserves every unchanged policy and stable locator alias;
3. allocates new aliases deterministically only for genuinely new locator identities;
4. omits no current candidate and invents no absent candidate;
5. has exact project-leader approval bound in protected evidence; and
6. can become the sole corpus input to a separately planned A2 authority rebind.

A0R1 does not authorize A2 discovery, source-content reads, copying, decoding, semantic scanning,
cleanup, or any original-data write.

## 2. Scope and exclusions

In scope:

- strict read-only loading of the current canonical discovery request and approved baseline manifest;
- metadata-only enumeration of the exact two save roots and installed-definition root;
- stable before-and-after directory-entry reconciliation;
- deterministic current membership, group assignment, decisions, counts, and alias continuity;
- one private pending corpus candidate;
- explicit project-leader review of exact candidate content;
- one protected approved-or-declined candidate decision;
- one repository-safe decision commit when a candidate reaches decision;
- one deterministic final approved manifest referencing that commit;
- explicit project-leader confirmation of the final exact bytes;
- one protected final approval record;
- synthetic tests, source review, and result-safe completion evidence.

Out of scope:

- reading any save, definition, executable, or installed-file content;
- reading or importing any historical inventory, state, backup, copy, evidence, decoded output,
  validation output, provenance record, or Agent envelope;
- modifying, moving, deleting, or reusing any prior workspace or manifest;
- changing save-root roles, definition selection rules, group order, group decisions, redaction,
  lifecycle, trusted-filesystem, or privacy policy;
- automatic approval, automatic scope narrowing, or guessing an ambiguous classification;
- public paths, filenames, counts, hashes, differences, or corpus content;
- production, CLI, schema, package, tracked-project, or test-project changes;
- A2 approval-commit rebinding or another A2 attempt; and
- hostile-local defense, historical identity, or simultaneous-tree snapshot claims.

## 3. Source and execution binding

Before any census or candidate-decision private read, `HEAD`, upstream, and the clean worktree must
equal verified shared `R0R1`. Before any approved-manifest finalization or final-byte-approval private
read, they must equal verified shared `D0R1`, whose direct parent is `R0R1`. Released Atlas source
must equal A2R8 `G` `4dc1572cc4439e6e5fade2827c3fa40230565ef2` in every phase.

Every session-utility mode receives the repository root and a fresh 32-character lowercase
hexadecimal run identifier. `--record-candidate-decision` additionally requires exactly
`--decision approved` or `--decision declined`; it rejects a missing or different value and never
prompts. The utility derives the historical canonical request and manifest without enumerating the
historical workspace. It validates only:

- the request and manifest as existing ordinary non-reparse files on fixed local drives;
- exact canonical request, project, workspace, and manifest path bindings;
- exact request-to-manifest SHA-256 equality;
- strict `atlas-intake/v2`, survey `survey-000001`, revision 3, `manual-a0`, approved confirmation,
  project-leader role, and the released A0 decision reference;
- public Steam app ID `1786790` and build ID `13624401`; and
- the two current save roots, definition root, and executable path through released live-source
  preflight.

The utility does not probe any other historical path. It writes only beneath its protected
session-owned A0R1 directory.

## 4. Metadata-only census

The utility captures complete directory-entry identities before enumeration and repeats them after
candidate construction. A change refuses the run. It reads attributes needed to classify type and
reparse state but never opens source content.

### 4.1 Save roots

Both save roots are enumerated non-recursively. The current manifest contains one entry for every
encountered root member.

Classification remains the closed A0 policy:

- `fileN.rpgsave` with a valid positive slot is `include-save`;
- `global.rpgsave` and `config.rpgsave` are `include-save`;
- `steam_autocloud.vdf` is `exclude-steam-autocloud`;
- an ordinary non-save file is `exclude-nonsave`;
- an unsupported type or reparse-backed entry is `unsupported`; and
- an unreadable entry is `unreadable`.

The project leader must explicitly narrow or stop on `unsupported` or `unreadable`; the utility never
turns either into an included or excluded success.

Root aliases, roles, active flags, and decisions remain exact because the approved relocation-plugin
rule has not changed. Root observed counts and top-level save counts are recomputed from current
entries.

### 4.2 Installed definitions

The utility preserves every approved definition group byte-for-byte except its observed count. It
enumerates the candidate universe defined by the ordered selection-rule union and assigns each
current path to the first matching group. Each entry inherits that group's existing include or
exclude decision.

Paths outside the approved selection-rule union remain outside the definition candidate universe.
A new path selected by an existing rule is not a scope expansion; a path requiring a new rule,
changed order, or changed decision stops A0R1 and requires a separately approved policy revision.

Every selected path must be an ordinary non-reparse file whose metadata can be read without opening
content. Missing prior paths disappear from current membership; newly selected paths enter current
membership. Group and top-level definition counts are recomputed exactly.

### 4.3 Stable aliases

Locator identity is:

- save: exact root alias plus normalized relative path; and
- definition: normalized relative path beneath the definition root.

An identity present in the prior manifest retains its exact source alias. Removed aliases are never
reassigned. New save identities receive monotonically increasing `save-source-NNNN` aliases after
the maximum prior save-source ordinal. New definition identities receive monotonically increasing
`definition-source-NNNNNN` aliases after the maximum prior definition-source ordinal.

Allocation sorts new locator identities ordinally before assigning aliases. The final manifest
requires unique aliases and normalized paths and preserves prior aliases for every surviving
identity.

## 5. Candidate manifest

The released Atlas revision policy freezes revision 3 to the original A0 approval commit, so it
cannot serialize or reload either an A0R1 pending candidate or an A0R1 final manifest. The session
utility therefore:

- uses the unchanged released reader to load the old approved baseline;
- uses the released manifest contract types and exact released JSON naming, ordering, escaping, and
  newline conventions for new bytes; and
- owns one bounded A0R1 codec that enforces the released general manifest invariants while replacing
  only the frozen revision-3 confirmation rule with the two A0R1 states below.

The local codec accepts exactly:

- a candidate with revision 3, `manual-a0`, pending confirmation, and no approver or decision
  reference; or
- a final manifest with revision 3, `manual-a0`, approved confirmation, project-leader role, and
  decision reference `commit:<D0R1-full-identifier>`.

It rejects every other revision-3 confirmation state. This exception is session-local and does not
change or weaken released Atlas. The final A0R1 manifest must not be supplied to released A2 until
the separately planned A2R14 rebinds production authority and its revision policy to `D0R1`.

The session utility serializes the census with that codec as a private revision-3 manifest with:

- the exact prior schema, survey alias, save roots, definition-group IDs, rules, order, purposes, and
  decisions;
- current entries, group counts, root counts, and top-level counts;
- validation method `manual-a0` and every reconciliation flag true; and
- pending confirmation with no approver or decision reference.

Revision 3 is deliberate: this is a fresh A0 baseline authority for a future isolated A2 lineage,
not revision 4 or 5 inside an A2 state sequence.

The utility strictly reloads the serialized bytes through the same bounded codec and independently
reconciles:

- both entry sets against fresh metadata enumeration;
- every preserved and newly allocated alias;
- every root/group membership and count;
- every terminal decision;
- definition first-match behavior;
- zero duplicate or case-colliding normalized paths; and
- unchanged directory-entry identities before and after.

The candidate is published atomically as one create-new protected record. Process output is one fixed
result-neutral recorded-or-not-recorded signal with empty standard error.

## 6. Project-leader decision and exact approval

The project leader reviews the exact private candidate locally. A protected decision mode writes one
create-new closed record binding the candidate SHA-256 and exactly `approved` or `declined`.

A repository-safe `D0R1` record may state only the decision and that protected evidence binds one
exact candidate. It contains no private path, hash, filename, count, entry, rule text, or difference.
Its staged bytes receive independent privacy review before commit.

If declined, no final manifest is produced. If approved:

1. `D0R1` is committed and pushed as the clean shared tip;
2. the session utility revalidates the candidate and protected approval;
3. it changes only confirmation to `approved`, project-leader role, and
   `commit:<D0R1-full-identifier>`;
4. it atomically publishes one create-new final manifest;
5. it proves every non-confirmation field equals the reviewed candidate; and
6. the project leader reviews and explicitly confirms the final exact bytes.

The final confirmation writes one protected create-new record binding the final SHA-256, the
candidate SHA-256, and `D0R1`. A final manifest without that record is not approved A0R1 authority.

## 7. Session utility and synthetic evidence

The session-only C# project lives beneath protected Copilot session state and references the
unchanged released Atlas project with the existing test friend-assembly name. It supports:

- `--self-test`;
- `--census`;
- `--record-candidate-decision`;
- `--finalize-approved-manifest`; and
- `--record-final-byte-approval`.

Synthetic tests use only owned temporary roots and cover:

- exact source request and prior-manifest binding;
- refusal to probe any historical artifact beyond request and manifest;
- complete save-root and definition-rule enumeration;
- before-and-after entry-set stability;
- every save classification and stop class;
- ordered first-match definition grouping;
- unchanged-group preservation and count recomputation;
- stable aliases, removed-alias nonreuse, new-alias order, and exhaustion;
- duplicate/case-collision refusal;
- the bounded codec's exact JSON compatibility and refusal of every A0R1 state except pending
  candidate and approved final;
- pending candidate exact shape and strict local reload;
- required `approved|declined` decision input, protected decision shapes, create-new publication, and
  candidate digest binding;
- approved final transition allowlist, strict local reload, and exact `D0R1`;
- final-byte approval binding;
- malformed, missing, substituted, outside, non-fixed, wrong-type, and reparse refusal;
- fixed stdout, empty stderr, and exit codes; and
- zero reads of synthetic source content.

The exact project, source, utility assembly, and linked Atlas assembly receive SHA-256 bindings
retained in protected state and the completion record. Every private phase verifies those bindings.
A fresh independent reviewer examines the complete exact source and every finding disposition until
`No findings`.

## 8. Git candidates and authority handoff

The `P0R1` plan line begins as the direct child of A2R13 `G13`. Until `R0R1`, plan-only corrections
may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R1` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-refresh-plan-review.md
```

If a candidate reaches decision, `D0R1` is the direct child of `R0R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-refresh-decision.md
```

Completion `G0R1` adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-refresh-completion.md
```

`G0R1` is the direct child of `D0R1` when a candidate reached decision; otherwise it is the direct
child of `R0R1`. Every record is independently reviewed as an exact staged blob and committed
unchanged.

The approved final manifest alone does not authorize A2. A separately persisted and reviewed A2R14
must bind production authority to `D0R1`, create another fresh A2 lineage, and receive its own
execution authority.

## 9. Acceptance criteria

A0R1 accepts one of three terminal branches:

- **safe refusal:** source binding or metadata census refuses; no candidate decision or final manifest
  follows;
- **declined:** one exact candidate is declined and no final manifest follows; or
- **approved:** one exact candidate receives protected approval, reviewed `D0R1`, deterministic final
  publication, and explicit protected final-byte approval.

A0R1 completes only when:

1. exact `P0R1` and record-only `R0R1` receive independent `No findings`, are pushed, and are verified;
2. every private phase binds clean shared `R0R1` or, after approval, exact clean shared `D0R1`;
3. released Atlas source equals A2R8 `G`;
4. the exact session utility passes formatting, warning-free Release build, comprehensive synthetic
   tests, source hashing, and independent `No findings`;
5. only the request and manifest are read from the historical A2 workspace, and no installed source
   content or other prior state is read;
6. current directory-entry sets are stable and completely terminally accounted;
7. policy, roots, definition groups, selection rules, order, decisions, and surviving aliases remain
   unchanged;
8. current membership and all counts equal the metadata census;
9. no unsupported, unreadable, ambiguous, duplicate, or case-colliding candidate is silently accepted;
10. an approved branch has exact protected candidate and final-byte decisions plus reviewed `D0R1`;
11. no private path, hash, filename, count, difference, or corpus content enters Git, subagent input,
    or process output;
12. every finding is adjudicated TP or FP, every TP is resolved, and every material FP receives
    independent concurrence;
13. the exact result-safe completion record receives independent `No findings`; and
14. verified `G0R1` is the clean shared branch tip with the required parent and path set.

## 10. Stop conditions and handoff

Stop without fallback when:

- the source request or manifest is malformed, substituted, noncanonical, or no longer approved;
- a root, game label, save-classification rule, definition rule, order, group decision, privacy rule,
  lifecycle rule, or trusted-filesystem assumption must change;
- source content would need to be opened;
- directory entries change during census;
- an entry is unreadable, unsupported, ambiguous, outside-root, duplicate, case-colliding, or
  reparse-backed;
- alias continuity or monotonic allocation cannot be proved;
- the project leader declines either candidate scope or final exact bytes;
- a production, CLI, schema, package, or tracked-code change becomes necessary;
- private data reaches Git, Agent input, or process output; or
- any independent finding remains unresolved.

To resume:

1. verify `G13`, the exact current `P0R1`/`R0R1` chain, upstream, and clean worktree;
2. verify released Atlas source and exact session-source bindings;
3. run only the current declared mode for the bound run identifier;
4. stop for project-leader review at both the candidate and final-byte gates;
5. require clean shared `D0R1` before finalization;
6. perform no A2 operation under A0R1 authority; and
7. release only the independently reviewed result-safe `G0R1`.
