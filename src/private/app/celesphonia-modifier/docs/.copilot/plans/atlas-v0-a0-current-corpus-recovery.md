# Atlas V0 A0 Current Corpus Recovery

**Lifecycle:** Historical supporting after verified shared `G0R2`

**Status:** Completed on project-leader stop without census or candidate

**Increment:** A0R2 - Diagnostic-Gated Census Recovery

**Decision owner:** Project leader

**Decision:** Start from the latest corrected A0R1 technical source, diagnose the metadata-only
pipeline once with privacy-safe fixed classes, and permit at most one census only after an exact
project-leader decision and reviewed Git gate.

**Base G0R1:** `94d632ca59e44e9312e4691928091195e23a0d4c`

**Normative governing sources:**

- `project-operating-model.md`;
- the still-operative corpus-policy, metadata-only, and threat-model sections of
  `atlas-v0-a0-current-corpus-refresh.md`; and
- project and documentation `AGENTS.md`.

**Historical provenance and evidence:**

- `atlas-v0-a0-current-corpus-refresh-governance-remediation.md`; and
- `../reviews/atlas-v0-a0-current-corpus-refresh-completion.md`.

**Planned plan-review record:**
`../reviews/atlas-v0-a0-current-corpus-recovery-plan-review.md`

**Planned source-qualification record:**
`../reviews/atlas-v0-a0-current-corpus-recovery-source-qualification.md`

**Planned diagnostic decision record:**
`../reviews/atlas-v0-a0-current-corpus-recovery-diagnostic-decision.md`

**Planned completion record:**
`../reviews/atlas-v0-a0-current-corpus-recovery-completion.md`

> **A0R3 authority correction**
> A0R2 completed at verified shared `G0R2`
> `1f9fbcd369d893e8de88cfe195512936e4815f01`. Its diagnostic and decision attempts are consumed, and
> it grants no retry, census, or A2 authority.
> `atlas-v0-a0-approved-manifest-corpus-refresh.md` replaced its historical-input model and completed
> at verified shared `G0R3` on the no-candidate branch. The approved manifest remained corpus
> authority, the old request only a minimum baseline-byte anchor carrier, and current locators only
> fresh runtime input. A0R3 grants no retry or A2 authority.

## 1. Outcome and authority

A0R2 exists because a blind repetition cannot explain A0R1's first authorized refusal. It reuses no
result from the later unauthorized execution.

A0R2 has two prospectively bounded private phases:

1. one private diagnostic attempt after verified shared `S0R2`; and
2. at most one consuming private census attempt after an exact protected project-leader decision is
   represented by verified shared `D0R2`.

The diagnostic never publishes a manifest candidate. It executes the reviewed metadata-only pipeline
through in-memory candidate replay and reports only a fixed stage class. A project-leader decision is
mandatory even if the diagnostic reports `ready-for-census`.

The census is not authorized by `P0R2`, `R0R2`, `S0R2`, diagnostic success, source review, protected
artifact presence, or conversation alone. Only an `authorize-census` protected decision bound by
reviewed, committed, pushed, and verified `D0R2` grants one consuming private census attempt.

## 2. Preserved policy and threat model

A0R2 changes no A0 corpus policy:

- the approved historical A0 request and baseline remain the policy source;
- stable aliases, removed-alias nonreuse, definition rule order, roots, and role semantics remain
  unchanged;
- definition selection applies to files only; ordinary directories are traversal nodes;
- required device- or reparse-backed traversal refuses;
- only directory-entry metadata is read from current save and definition roots;
- no save content, definition content, `Game.exe`, patch metadata, installer data, or install history
  is read;
- no original or installed file is written; and
- no A2 authority is granted.

The threat model remains `trusted-local-filesystem/v1`. Malformed, missing, substituted, outside,
non-fixed, wrong-type, device, reparse, unreadable, unsupported, duplicate, case-colliding, or
unstable selected entries refuse. Hostile local actors and adversarial races remain out of scope.

## 3. Protected workspace and source derivation

After verified shared `R0R2`, create a new protected, Git-ignored A0R2 workspace. Copy only the latest
corrected A0R1 project and source as technical input. Do not copy A0R1 runtime state, attempt markers,
receipts, candidates, decisions, or other execution artifacts.

Before modification or build, the new workspace must contain exactly these copied files plus a new
empty `state` directory:

```text
Hcoona.CelesphoniaModifier.Atlas.A0R2.csproj
Program.cs
state/
```

The copied bytes must match the G0R1 technical bindings:

```text
project
  d3d92482d279f4c7afbdd8b0fbbcfbf2e04251feb1bffe1b19195ab79b3f43a8
Program.cs
  0eeefcf9d0c9d68dd1e58ac2271ac286f5d8527e149d0a50f1ea93ac7c5b37f9
```

The project file may be renamed as shown without changing its bytes. Reject any extra copied file or
directory. Build outputs may appear only after this allowlist and byte-identity check passes.

Before any private phase, the A0R2 utility must:

- bind exact clean shared `R0R2` for utility preparation;
- bind exact clean shared `S0R2`, whose direct parent is `R0R2`, for diagnostic execution;
- bind exact clean shared `D0R2` for census execution;
- bind the project file, source, Release utility assembly, and linked released Atlas assembly by
  SHA-256;
- prove released Atlas source and assembly identity against the accepted repository revision;
- begin from a new empty protected `state` directory and later accept only exact planned artifacts;
  and
- reject missing, pre-existing, substituted, wrong-type, outside-workspace, or reparse-backed state
  inputs.

After the final Release build and source review, create one immutable `source-bindings.json` beside the
project file, outside `state`. It contains only:

- exact `R0R2`;
- project and source relative names plus SHA-256;
- Release utility assembly relative name plus SHA-256;
- linked released Atlas assembly relative name plus SHA-256; and
- binding schema and tool revision.

The result-safe `S0R2` record binds every value and the complete binding-file SHA-256. Before any
private access, the utility must derive the canonical tracked `S0R2` record. Diagnosis proves current
`HEAD` is clean shared `S0R2`, whose direct parent is `R0R2` and whose only change is the planned source
record. Census proves current `HEAD` is clean shared `D0R2`, whose direct parent is `S0R2` and whose
only change is the planned decision record. Both modes strictly parse the `S0R2` binding values and
compare them with `source-bindings.json` plus freshly computed file hashes. The binding file is never
stored under the initially empty runtime `state`.

## 4. Utility modes

The A0R2 utility has exactly these noninteractive modes:

```text
--test
  --repository-root <repository-root>
  --workspace-root <a0r2-workspace-root>
  --run-id <run-id>

--diagnose-census
  --repository-root <repository-root>
  --workspace-root <a0r2-workspace-root>
  --run-id <run-id>

--record-diagnostic-decision
  --repository-root <repository-root>
  --workspace-root <a0r2-workspace-root>
  --run-id <run-id>
  --decision <authorize-census|stop>

--census
  --repository-root <repository-root>
  --workspace-root <a0r2-workspace-root>
  --run-id <run-id>
```

Unknown modes, missing required arguments, duplicate arguments, unexpected arguments, or invalid
decision values refuse before private access.

Each process invocation receives a fresh, never-reused 32-character lowercase hexadecimal `run-id`.
The diagnostic and census markers use their invocation's ID as the attempt identifier. The decision
binds its own invocation ID and the receipt's diagnostic attempt ID.

The utility canonicalizes the explicit repository and workspace roots without enumeration. It derives
the state root only as `<a0r2-workspace-root>\state` and derives the two approved historical inputs
exactly as A0R1 did, without enumerating their parent workspace. All derived paths remain beneath
their declared roots and pass the accepted fixed-drive, ordinary-path, and no-reparse checks.

All modes write exactly one fixed stdout line, keep stderr empty, and return:

| Outcome                          | Stdout                         | Exit                         |
| -------------------------------- | ------------------------------ | ---------------------------- |
| Synthetic tests pass             | `test-passed`                  | `0`                          |
| Synthetic tests fail             | `test-failed`                  | `2`                          |
| Mode or argument parsing refuses | `operation-refused`            | `2`                          |
| Diagnostic preflight refuses     | `diagnostic-preflight-refused` | `2`                          |
| Diagnostic reaches a class       | Exact section 5 class          | `0` for ready, otherwise `2` |
| Decision is recorded             | `decision-recorded`            | `0`                          |
| Decision input refuses           | `decision-refused`             | `2`                          |
| Census preflight refuses         | `census-preflight-refused`     | `2`                          |
| Census safely refuses            | `census-refused`               | `2`                          |
| Census publishes one candidate   | `candidate-published`          | `0`                          |

No mode writes exception text or a private-derived value to either stream.

`test` uses only synthetic temporary copies and reads zero synthetic source-content bytes. It creates
no protected A0R2 runtime state.

`diagnose-census` first verifies exact clean shared `S0R2`, reviewed source bindings, CLI syntax, and
an empty safe protected state. These preflight checks may refuse without consuming the private
attempt. After they pass and before any historical or current-tree read, it atomically creates:

```text
a0r2-diagnostic-attempt.json
```

If that path already exists, diagnosis refuses before private access. The attempt is consumed even if
the process stops unexpectedly. No plan authorizes a second private diagnostic attempt.

The marker contains only its schema, tool revision, attempt identifier, source-binding digest, and
exact `S0R2`.

The diagnostic writes exactly one create-new result when it reaches controlled completion:

```text
a0r2-diagnostic-receipt.json
```

The receipt contains only:

- schema `atlas-a0r2-diagnostic-receipt/v1` and tool revision;
- attempt identifier;
- exact source-binding digest;
- exact `S0R2`;
- one result class from section 5.

It contains no private path, filename, count, entry, hash, difference, content, or exception text.
Only a complete strict receipt with the declared schema, an attempt ID matching the marker, the exact
source-binding digest, exact `S0R2`, one allowed class, and no extra fields is valid.

`record-diagnostic-decision` requires exact clean shared `S0R2`, reviewed source bindings, the exact
receipt, and one explicit value:

```text
--decision authorize-census
--decision stop
```

It runs without current-tree access. It first verifies all inputs and planned state; these preflight
checks may refuse without consuming the decision-recording attempt. After preflight, it atomically
creates:

```text
a0r2-diagnostic-decision-attempt.json
```

The marker binds only schema, tool revision, its fresh run ID, source-binding digest, receipt digest,
requested decision, and exact `S0R2`. A pre-existing marker refuses and no second decision-recording
attempt is authorized.

It then atomically creates exactly one create-new:

```text
a0r2-diagnostic-decision.json
```

The decision uses schema `atlas-a0r2-diagnostic-decision/v1` and binds the complete receipt digest,
result class, exact `S0R2`, decision, and explicit project-leader role. `authorize-census` is valid
only for `ready-for-census`; every other result class permits only `stop`. A pre-existing decision path
refuses; no replacement is permitted.

Only a complete strict decision file is decision authority. If the attempt marker exists without a
complete valid decision, decision publication is incomplete: no `D0R2`, census, or retry is
authorized, and only result-safe completion under exact `S0R2` may follow.

`census` first verifies exact clean shared `D0R2`, the strict canonical `D0R2` authority block, an
`authorize-census` protected decision, reviewed source bindings, CLI syntax, safe planned state, and
no prior census attempt. These preflight checks may refuse without consuming the private attempt.
After they pass and before any historical or current-tree read it atomically creates:

```text
a0r2-census-attempt.json
```

If that path already exists, census refuses before private access. The attempt is consumed even if
the process stops unexpectedly. No plan authorizes a second private census attempt.

The marker contains only its schema, tool revision, attempt identifier, source-binding digest, exact
`D0R2`, and protected decision digest.

## 5. Fixed diagnostic classes

The diagnostic receipt and stdout may contain exactly one of:

```text
ready-for-census
historical-input-gate-refused
installed-root-gate-refused
save-metadata-gate-refused
definition-metadata-gate-refused
stability-gate-refused
candidate-construction-gate-refused
candidate-replay-gate-refused
diagnostic-internal-refused
```

These classes identify only the first pipeline boundary that did not complete. They do not identify
an entry, exception, private cause, or remediation. The utility must not classify by parsing exception
messages.

`ready-for-census` means only that the diagnostic completed the reviewed in-memory path on its
observed metadata snapshot. It does not approve a baseline, predict a later census result, or grant
census authority.

Any refusal class stops A0R2 private execution. Source analysis may use repository-safe code and
synthetic tests, but no correction, diagnostic retry, census, or extra private probe follows without a
new persisted and independently reviewed plan.

## 6. Diagnostic pipeline

After successful source, Git, CLI, and state preflight, the diagnostic executes these ordered
boundaries:

1. historical request and manifest structural validation;
2. installed-root metadata safety;
3. save directory-entry enumeration and terminal classification;
4. definition directory-entry enumeration with file-only selection;
5. second enumeration proving stable selected entry sets and metadata;
6. in-memory current-corpus candidate construction;
7. strict bounded-codec serialization, reload, and deterministic replay equality.

It suppresses candidate publication unconditionally. It does not invoke candidate-decision,
finalization, final-byte approval, or A2 code.

Each boundary maps controlled refusal to its fixed class. Unexpected exceptions map only to
`diagnostic-internal-refused`, with fixed stdout, empty stderr, and no exception details. The receipt
is create-new and written only after classification; the immutable attempt marker proves authority
consumption if no complete valid receipt exists.

## 7. Project-leader decision and D0R2

After one complete valid diagnostic receipt exists, present only its fixed result class and
repository-safe source binding identity to the project leader.

The project leader chooses:

- `authorize-census`, only for `ready-for-census`; or
- `stop`, for any class.

The protected decision is recorded by the reviewed utility. A result-safe `D0R2` candidate then binds:

- exact `P0R2` and `R0R2`;
- exact `S0R2`;
- the receipt digest and fixed class;
- the protected decision digest and decision;
- project-leader provenance;
- the reviewed source bindings; and
- the permitted next action.

The record contains exactly one canonical JSON authority block between
`<!-- atlas-a0r2-decision-authority:start -->` and
`<!-- atlas-a0r2-decision-authority:end -->`. The object has exactly:

```text
schema = atlas-a0r2-decision-authority/v1
p0r2
r0r2
s0r2
sourceBindingsSha256
diagnosticReceiptSha256
diagnosticResultClass
protectedDecisionSha256
decision
permittedNextAction
```

`permittedNextAction` is exactly `census-once` for `authorize-census` and `complete-only` for `stop`.
The utility strictly parses only that unique block and rejects missing, duplicate, extra, malformed,
non-lowercase-hex hash, inconsistent, or mismatched fields before census access.

`D0R2` receives independent exact-blob review before commit. It discloses no private corpus data.
`stop` makes completion the only next action. `authorize-census` grants exactly one consuming private
census attempt under exact clean shared `D0R2`.

If an attempt marker exists but no complete valid diagnostic receipt exists, the diagnostic did not
reach authoritative controlled completion. No protected decision or `D0R2` is possible, no retry is
authorized, and A0R2 may proceed only to result-safe completion under exact `S0R2`.

## 8. Census and terminal branches

The census re-executes the same metadata-only pipeline from fresh directory-entry snapshots. It never
uses diagnostic in-memory data as corpus evidence.

On controlled refusal:

- stdout is one fixed `census-refused` line;
- stderr is empty;
- no candidate or partial candidate is published; and
- the branch is terminal.

On success it atomically publishes exactly one create-new protected pending candidate:

```text
a0r2-current-corpus-manifest-candidate.json
```

The candidate uses the A0R1 bounded revision-3 pending shape, unchanged approved policy, stable alias
rules, and exact metadata-derived membership. Strict local reload and deterministic replay must pass
before publication. Stdout is one fixed `candidate-published` line and stderr is empty.

Candidate success grants no baseline approval, finalization, A2 authority, or second census. A future
separately planned increment must govern candidate approval or decline.

A0R2 has five terminal branches:

- **diagnostic incomplete:** an attempt exists without a complete valid receipt; no decision or census
  occurs;
- **decision incomplete:** a decision attempt exists without a complete valid decision; no `D0R2` or
  census occurs;
- **diagnostic stop:** protected decision is `stop`; no census occurs;
- **census no candidate:** a census marker exists without a complete candidate, whether the process
  refused or was interrupted; or
- **candidate published:** one pending candidate exists and no decision or final manifest follows.

## 9. Synthetic validation and source review

Before diagnostic execution, the exact utility must pass:

- formatting;
- warning-free Release build;
- all comprehensive synthetic tests;
- exact source and assembly hashing; and
- independent full-source review with every finding adjudicated TP or FP until `No findings`.

Synthetic tests must prove:

- all CLI argument and fixed-output contracts;
- every diagnostic class and first-boundary mapping without message parsing;
- `ready-for-census` suppresses candidate publication;
- diagnostic attempt create-new behavior before private access;
- missing or malformed receipt after an attempt cannot be retried;
- `authorize-census` is rejected for every refusal class;
- explicit project-leader decision and receipt-digest binding;
- decision-attempt create-new behavior and terminal handling of missing or malformed decision output;
- `D0R2` and its unique strict canonical authority block are required for census;
- census attempt create-new behavior before private access;
- no second census after success, refusal, or interruption;
- candidate create-new publication, strict reload, and deterministic replay;
- every safe path, metadata classification, alias, codec, duplicate, collision, and stability rule
  inherited from A0R1;
- file-only definition selection with ordinary-directory traversal;
- required device/reparse refusal;
- zero source-content reads; and
- zero use of A0R1 runtime state or the unauthorized execution result.

### Source-qualification gate

After complete source review returns `No findings`, generate the exact binding file without changing
the reviewed project, source, or assemblies. Author one result-safe source-qualification record that
contains:

- exact `P0R2` and `R0R2`;
- the initial-copy hash verification and allowlist result;
- the final project, source, utility assembly, linked Atlas assembly, and binding-file hashes;
- formatting, build, synthetic-test, and source-review outcomes;
- reviewer provenance and every TP/FP disposition; and
- the exact permitted next action: one diagnostic attempt.

The record contains exactly one canonical JSON authority block between
`<!-- atlas-a0r2-source-authority:start -->` and
`<!-- atlas-a0r2-source-authority:end -->`. The object has exactly:

```text
schema = atlas-a0r2-source-authority/v1
r0r2
sourceBindingsSha256
projectSha256
programSha256
utilityAssemblySha256
atlasAssemblySha256
```

The utility strictly parses only that unique block and rejects missing, duplicate, extra, malformed,
non-lowercase-hex, or mismatched fields.

A fresh independent staged-record reviewer verifies the complete record, binding file, source,
assemblies, Git facts, and absence of runtime state until `No findings`. Commit the record unchanged as
`S0R2`. The utility treats the tracked record at exact `S0R2`, not conversation or artifact presence,
as the external source-identity authority.

## 10. Git candidates

Plan candidate `P0R2` is the direct child of `G0R1` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-current-corpus-refresh.md
    atlas-v0-a0-current-corpus-refresh-governance-remediation.md
    atlas-v0-a0-current-corpus-recovery.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review `R0R2` is the direct child of the final reviewed plan-line tip and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-recovery-plan-review.md
```

Source qualification `S0R2` is the direct child of `R0R2` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-recovery-source-qualification.md
```

Diagnostic decision `D0R2` is the direct child of `S0R2` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-recovery-diagnostic-decision.md
```

Completion `G0R2` is the direct child of `D0R2` when a protected decision exists. For
`diagnostic-incomplete` or `decision-incomplete`, it is the direct child of `S0R2`. It adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a0-current-corpus-recovery-completion.md
```

Every candidate is independently reviewed as an exact staged blob and committed unchanged.

## 11. Acceptance criteria

A0R2 completes only when:

1. exact `P0R2` and record-only `R0R2` receive independent `No findings`, are committed, pushed, and
   verified;
2. the new protected A0R2 workspace contains no copied A0R1 runtime state or result;
3. the exact utility passes section 9, receives independent `No findings`, and is bound by exact
   independently reviewed, committed, pushed, and verified `S0R2`;
4. exactly one diagnostic attempt runs under exact clean shared `S0R2`;
5. controlled diagnostic completion produces exactly one complete strict receipt with one fixed class
   and repository-safe bindings, while diagnostic-incomplete has a marker without such a receipt;
6. a diagnostic with a valid receipt receives one explicit protected `authorize-census|stop`
   project-leader decision attempt; diagnostic-incomplete or decision-incomplete has no retry;
7. when a decision exists, exact result-safe `D0R2` receives independent `No findings`, is committed,
   pushed, and verified;
8. `stop`, diagnostic-incomplete, and decision-incomplete cause no census, while `authorize-census`
   permits exactly one consuming private census attempt under exact clean shared `D0R2`;
9. after a consuming private census attempt, exactly one complete pending candidate exists or the
   result-neutral no-candidate branch applies;
10. no private path, filename, count, entry, corpus hash, difference, content, exception text, or
    unauthorized execution result enters Git, subagent input, or process output;
11. no diagnostic, decision, or census retry occurs after its marker; a preflight-only refusal may be
    corrected and reinvoked with a fresh run ID because it performs no private access and creates no
    marker;
12. no baseline approval, final manifest, A2 operation, production change, or original-data write
    occurs; and
13. exact result-safe `G0R2` receives independent `No findings` and becomes the verified clean shared
    tip with the required parent and path set.

## 12. Stop conditions

Stop before private access unless exact `S0R2` and its tracked source-qualification record match every
source, assembly, binding-file, Git, state, argument, authority, and path requirement.

After the diagnostic marker exists, stop for result evaluation. Without a complete strict receipt,
author only diagnostic-incomplete closure. With a valid receipt, stop for project-leader decision; a
refusal class permits only `stop`.

After the decision-attempt marker exists, stop if no complete strict decision exists. Do not retry or
author `D0R2`.

After `authorize-census`, stop before census unless exact reviewed `D0R2` is the clean shared tip.

After the consuming private census attempt, stop on both refusal and candidate publication. Do not
retry, approve, decline, finalize, or run A2.

Any need for exception text, private-path disclosure, source-content access, changed policy, changed
threat model, new result class, utility correction after private diagnosis, or additional private
probe returns to planning.

## 13. Ordered resume procedure

1. Verify clean shared `G0R1`, stage only the five `P0R2` paths, complete independent plan review,
   commit the exact candidate, and publish record-only `R0R2`.
2. Under clean shared `R0R2`, create the new protected workspace, verify the exact initial allowlist
   and G0R1 source hashes, then modify only the copied project and source.
3. Format, build, run synthetic tests, bind exact source and assemblies, and complete independent
   full-source review until `No findings`. Author and independently review the exact source-
   qualification record, then commit and publish it unchanged as `S0R2`.
4. Reverify clean shared `S0R2` and invoke `--diagnose-census` with a fresh run ID. A preflight-only
   refusal may be corrected and reinvoked with another fresh ID; do not retry after the diagnostic
   marker exists.
5. If no complete strict receipt exists, author only the result-safe `G0R2` diagnostic-incomplete
   closure. Otherwise present only the fixed receipt class and repository-safe source identity to the
   project leader.
6. Record the explicit protected decision once. If no complete strict decision exists after its marker
   is created, author only the result-safe `G0R2` decision-incomplete closure. Otherwise author exact
   result-safe `D0R2` and publish it only after independent `No findings`.
7. For `stop`, author only `G0R2`. For `authorize-census`, reverify clean shared `D0R2` and invoke
   `--census` with a fresh run ID. A preflight-only refusal may be corrected and reinvoked because it
   reads no private input and creates no marker.
8. After the census marker exists, do not retry. Record only whether one complete pending candidate
   exists, author result-safe `G0R2`, and publish it after independent `No findings`.
