# Atlas V0 A2 Baseline Authority Diagnosis

**Lifecycle:** Historical supporting; retained as reviewed A2R9 evidence

**Status:** Blocked because prior request SHA-256 evidence is unavailable

**Increment:** A2R9 - Baseline Authority Diagnosis

**Decision owner:** Project leader

**Audience:** Project leader, diagnostic implementers, independent reviewers, and future resumers

**Purpose:** Qualify preserved baseline authority before planning any continuation, without retrying
discovery, changing production code, or exposing private evidence.

**Implementation language:** Session-only C# on the repository-pinned .NET 10 SDK

**Base:** `4dc1572cc4439e6e5fade2827c3fa40230565ef2`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-intake-safety-plan.md`;
- `atlas-v0-a2-safe-failure-stage-diagnostics.md`;
- `atlas-v0-a2-approved-manifest-authority-correction.md`; and
- `../reviews/atlas-v0-a2-approved-manifest-authority-correction-release-gate.md`.

**Dependencies:** Verified shared A2R8 `G`, reviewed Atlas source, preserved existing request and
baseline inputs, and independent review of the plan and exact local diagnostic.

**Plan-review record:**
`../reviews/atlas-v0-a2-baseline-authority-diagnosis-plan-review.md`

**Completion record:** Not produced; A2R9 stopped at its pre-read gate

> **Current baseline observation**
> Verified shared A2R9 `R` reached this plan's pre-read request-identity gate. Protected execution
> evidence retained the request-path derivation but no prior request SHA-256. No private request was
> opened. `atlas-v0-a2-current-baseline-observation.md` now governs. This plan and `R` remain
> review evidence and authorize no diagnostic process start.

## 1. Observed boundary and decision

The one metadata-only discovery authority granted by A2R8 is consumed. Repository evidence
establishes neither its disposition nor its outcome, and A2R9 does not rely on or reproduce either.
Before any continuation is planned, A2R9 conservatively qualifies these preserved-input and
baseline-authority operations from public control flow:

1. strict baseline manifest reading;
2. manifest approval status, bound digest, and revision;
3. strict current inventory reading;
4. inventory digest and conditional backup transition;
5. discovery artifact-alias recovery;
6. the strict revision-3 manifest inventory row; and
7. parsing the next discovery destination artifact ordinal.

This scope is an independent prerequisite for future planning, not a claim about the consumed
attempt's stage or result. It cannot decide whether production code, request binding, or a private
inventory artifact needs correction. Repeating discovery is not authorized.

A2R9 creates one session-only, read-only, fixed-output C# diagnostic. It invokes exact released
operations against preserved inputs, stops at the first operation that does not complete, and emits
one token from a closed vocabulary. It does not repair anything.

## 2. Scope

In scope:

- bind the diagnostic repository state to exact verified shared A2R9 `R`;
- prove the Atlas source and project inputs are unchanged from verified shared A2R8 `G`;
- bind the exact A2R9 `R` Atlas DLL by SHA-256 after the repository build;
- require `HEAD`, upstream, index, and worktree to match `R`;
- privately bind the canonical request path and SHA-256 used by the consumed A2R8 attempt;
- read the existing canonical discovery request with the released strict reader;
- read only the request-selected baseline manifest, current inventory, and conditionally required
  discovery inventory backup;
- invoke released read, selection, inventory-load, alias, manifest-row, and ordinal logic;
- emit one fixed token and a fixed exit code;
- independently review the exact diagnostic bytes before one execution;
- preserve the actual token only in the protected current operator session; and
- route the result to a separately persisted correction or remediation plan.

Out of scope:

- another `intake-discover` attempt;
- production, test, schema, package, project, or CLI changes;
- creating, rewriting, repairing, moving, backing up, or deleting a private artifact;
- reading a game, save root, definition root, executable, or generated A2 output other than the
  current inventory and conditional inventory backup allowed in section 3.2;
- enumerating live sources or validating corpus membership;
- printing an exception message, stack trace, path, name, value, hash, count, list, fragment, or
  dynamic type or member name;
- confirmation, copying, cleanup, decoding, semantic research, or live-save writes; and
- deciding the final repair before the diagnostic result exists.

## 3. Exact diagnostic contract

### 3.1 Environment binding

The diagnostic must fail closed before private reads unless:

1. the supplied repository root is an absolute DOS path;
2. `HEAD` and upstream both equal the exact plan-review record `R` bound into the diagnostic;
3. `R` descends from A2R8 `G`
   `4dc1572cc4439e6e5fade2827c3fa40230565ef2`;
4. the `G..R` diff contains only the A2R9 plan and plan-review path set in section 4;
5. the tracked and untracked worktree is empty;
6. the exact Atlas DLL exists at its reviewed Debug `net10.0` path;
7. the DLL SHA-256 equals the independently reviewed diagnostic binding; and
8. the running .NET major version is 10.

Environment refusal emits only `diagnostic-environment-invalid`.

### 3.2 Read boundary

After environment binding, the diagnostic may read:

- the canonical existing discovery request whose path and SHA-256 equal the protected bindings from
  the consumed A2R8 attempt;
- the baseline manifest selected by that request;
- the current inventory selected by that request; and
- the canonical discovered-inventory backup only when released transition logic requires it.

It may read public Git and assembly metadata needed for environment binding. It must not dereference
or probe the request's game executable, save roots, definition root, output paths, or any other
private path.

The prior request path and SHA-256 must already exist in protected session execution evidence. They
are supplied through inherited process environment, not command-line arguments, diagnostic bytes, or
repository state. The diagnostic canonicalizes the supplied request path, performs a read-only
SHA-256 check before parsing it, and fails closed if either binding is absent or mismatched. It does
not derive a replacement identity from the current request. If the consumed identity is unavailable,
A2R9 stops without reading any private file.

All private files are opened read-only. Referenced documents use the released strict readers. The
diagnostic creates no directory, file, stream opened for writing, temporary copy, log, dump, cache,
or serialized document.

### 3.3 Fixed output

Standard output must contain exactly one ASCII token plus `\n`. Standard error must be empty. The
closed token vocabulary is:

```text
diagnostic-environment-invalid
diagnostic-request-identity-invalid
baseline-request-read-refused
baseline-manifest-read-refused
baseline-manifest-selection-refused
baseline-inventory-load-refused
baseline-inventory-discovery-aliases-refused
baseline-inventory-manifest-row-refused
baseline-inventory-next-ordinal-refused
baseline-authority-valid
diagnostic-indeterminate
```

Tokens identify only a fixed binding or released-operation boundary. They do not encode an artifact
value, difference, cardinality, path, identity, or proposed disposition.

Exit code `0` is reserved for `baseline-authority-valid`. Each refusal token has a unique, reviewed
nonzero exit code. An absent or mismatched consumed request identity maps only to
`diagnostic-request-identity-invalid`. A failure thrown after a bound released operation begins maps
only to that operation's refusal token, regardless of cause. Other failures outside an active
operation map to `diagnostic-indeterminate`. No exception text is emitted.

### 3.4 Exact released operations

The diagnostic must reuse the exact A2R8 assembly behavior rather than reimplementing JSON or
contract rules. It may use narrowly bound reflection because the required members are internal, but
must bind exact declaring types, member names, parameter types, and return shapes before private
reads.

The diagnostic executes these released operation boundaries in order:

1. `AtlasIntakeContracts.ReadDiscoveryRequestAsync`;
2. `AtlasIntakeContracts.ReadManifestAsync`;
3. the single baseline-selection block at `AtlasDiscovery.cs:65-81`, including
   `AtlasDiscovery.EnsureDigestMatches`;
4. `TrustedLocalCopy.LoadPhaseInventoryAsync` as one indivisible operation;
5. `AtlasDiscovery.ResolveDiscoveryAliases` as one indivisible operation;
6. `AtlasDiscovery.FindManifestArtifactAlias`; and
7. `AtlasDiscovery.GetNextDiscoveryDestinationArtifactOrdinal`.

The baseline-selection block uses only the exact loaded members, released constants, ordinal
equality, and released digest helper present in that source block. It introduces no JSON, approval,
revision, digest, transition, alias, row, or ordinal rule. The inventory loader and alias resolver
must not be decomposed: all failures within each operation map to its one operation token.

Reflection metadata binding or dispatch failure before an operation begins is indeterminate. After
an operation begins, every thrown failure maps to that operation's token. The diagnostic never
inspects exception types, messages, inner exceptions, or stack traces.

## 4. Repository and session candidates

Plan candidate `P1` is the direct child of base `B` and may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-baseline-authority-diagnosis.md
    atlas-v0-a2-intake-safety-plan.md
```

Any plan remediation candidates form a direct-child chain and may change only those three paths.
Final plan candidate `P` is the last independently reviewed plan commit.

Plan-review record `R` is the direct child of final `P` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-authority-diagnosis-plan-review.md
```

After verified shared `R`, the diagnostic and its disposable synthetic harness may exist only at:

```text
<session-state>/files/diagnose-atlas-a2-baseline-authority.cs
<session-state>/files/test-atlas-a2-baseline-authority-diagnostic.cs
```

The exact diagnostic and harness receive SHA-256 bindings and independent review. They are never
committed. The harness may create and remove only its own resolved temporary synthetic directory. It
must not receive or derive any private path.

One-shot authority is procedural, not an anti-replay claim. A2R9 creates no launcher, marker, lock,
or consumption file because those would add write authority. The first diagnostic process start is
recorded only in protected operating-session state and consumes the authority regardless of output,
exit code, interruption, or completion. No second process start is authorized.

Completion candidate `G` is the direct child of `R` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-baseline-authority-diagnosis-completion.md
```

The completion record contains the Git chain, diagnostic hash, review identity, fixed procedure,
privacy attestation, and repository-safe route class from section 6. Every route class contains at
least two possible tokens, so the record does not reveal the observed token by equivalence. It
contains no private value. The exact staged completion record must receive independent `No findings`
before being committed unchanged as `G`.

No rename is allowed in `B..G`. No private artifact, diagnostic output, or session script is a
repository candidate.

## 5. Acceptance criteria

A2R9 may execute the diagnostic only when:

1. `P` is committed, pushed, and independently reviewed with `No findings`;
2. record-only `R` is independently reviewed, committed, pushed, and verified;
3. the diagnostic parses and compiles with the repository-pinned .NET 10 SDK;
4. static inspection finds no write-capable file API, discovery command, live-root access, dynamic
   output, exception output, or second diagnostic attempt;
5. the exact session harness proves every operation classification and output mapping with in-memory
   or temporary synthetic inputs, including exact bytes, exit codes, and no dynamic output;
6. protected session evidence supplies the exact consumed request path and SHA-256 without placing
   either value in arguments, diagnostic bytes, Git, review input, or output;
7. synthetic proof covers absent, mismatched, and valid request-identity bindings before parsing;
8. the exact diagnostic and DLL hashes are recorded in protected session state;
9. a fresh independent reviewer returns `No findings` for the exact diagnostic bytes; and
10. the repository and private inputs remain unchanged before execution.

A2R9 completes only when:

1. the reviewed diagnostic is executed exactly once;
2. output is exactly one allowed token and standard error is empty;
3. no private input is modified and no diagnostic output artifact is created;
4. discovery is not retried;
5. the result selects exactly one repository-safe route class from section 6;
6. a repository-safe completion record omits the actual token and private evidence;
7. the exact completion record receives independent `No findings`; and
8. record-only `G` is pushed and verified as a clean shared tip.

## 6. Result routes

The local token selects one route class:

- **Early-boundary investigation:** Covers request identity, request read, manifest read, manifest
  selection, or inventory load. It determines whether a separate production, request-binding, or
  break-glass plan is warranted.
- **Late-boundary investigation:** Covers alias resolution, manifest row, next ordinal, or baseline
  valid. It distinguishes a released-operation refusal from environmental drift without live-source
  reads.
- **Diagnostic redesign:** Covers environment invalid or indeterminate. It corrects only the
  diagnostic design or execution environment.

The completion record names only the route class, never the token or possible-token item. A
follow-up plan is outside A2R9 and `R..G`; it starts a separate reviewed chain after verified shared
`G`. If later evidence selects private remediation, that plan must preserve the original immutable
artifact and use append-only before/after audit; it must never overwrite it.

No route class decides the defect, authorizes repair, or authorizes another discovery attempt.

## 7. Validation and review

Repository validation for `P`, `R`, and `G` includes:

- Markdown formatting and linting;
- UTF-8 without BOM, LF-only, and lines of at most 100 characters;
- exact path, no-renames, direct-parent, tree, blob, upstream, index, and worktree checks; and
- ref-bound HK for the exact candidate.

Diagnostic validation uses only synthetic or public inputs until the one reviewed execution. It must
prove the closed output bytes, environment refusal, operation-boundary classification, indeterminate
fallback outside an active operation, request-identity refusal, no-write boundary, and single-run
entry-point behavior. It makes no technical anti-replay claim.

Every material candidate and the exact session diagnostic receives a fresh independent review.
Findings are remediated only in a new direct-child repository candidate or a new diagnostic hash.
Review repeats over the complete new candidate until `No findings`.

## 8. Privacy, authority, and stop conditions

The diagnostic may process private bytes locally only after verified shared `R` and exact diagnostic
review. Private bytes, paths, hashes, names, counts, values, differences, exception text, and
document content do not enter Git, an agent or reviewer prompt, a review record, or a generated
artifact. The fixed token may be observed only by the project leader and the current operating
Copilot session; it must not be sent to a subagent or persisted in Git.

Stop without execution if:

- the plan or diagnostic review has a finding;
- exact `R`, A2R8 source, DLL, diagnostic, path, or clean-worktree binding fails;
- safe classification requires reading a live source or generated output outside the two inventory
  inputs allowed in section 3.2;
- any output must be dynamic;
- any private file would be written, copied, moved, or deleted;
- the diagnostic cannot invoke the released strict behavior exactly; or
- the consumed request identity is absent from protected session evidence; or
- another discovery attempt or repair would be required.

The project leader confirms any later change to corpus authority, trusted-local-filesystem scope,
private remediation, or retry authority. This plan authorizes none of them.

## 9. Resume procedure

To resume from a clean checkout:

1. verify base `B` is the shared branch tip;
2. review and persist `P1..P`, then the record-only `R`;
3. at exact shared `R`, build the Atlas tests project through `mise exec -- dotnet`;
4. confirm the consumed request path and SHA-256 exist in protected session evidence;
5. create the session-only diagnostic with exact `R`, A2R8 source, and DLL bindings;
6. prove its fixed outputs with the exact session harness;
7. independently review the exact diagnostic and harness bytes until `No findings`;
8. supply the consumed request bindings through inherited environment and start the diagnostic once;
9. mark authority consumed at process start and retain only the token in protected session state;
10. select one section 6 route class without performing it or persisting a follow-up plan;
11. create and review the repository-safe completion record; and
12. commit and verify record-only `G`.
