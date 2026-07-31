# Atlas V0 A2 Local Definition Intake Simplification

**Lifecycle:** Conditional: proposed governing plan before verified shared `R15`; active normative
after verified shared `R15`

**Increment:** A2R15 - Local Definition Intake Simplification

**Decision owner:** Project leader

**Base:** Exact verified shared `R14`

**Purpose:** Replace the disproportionate A2R14 protected-operation protocol with a maintainable
single-user local definition-copy workflow that protects original data and detects ordinary copy
corruption without attempting to defend against a malicious local user or administrator.

> **No authority by presence**
> This file changes no active authority until its exact persisted candidate is independently reviewed,
> committed, pushed, and activated by verified shared `R15`.

**Conditional A6R6 extension correction:** Before verified shared `T6R6`, this plan's
`.js`/`.json` source and destination extension rule remains unchanged. After verified shared `T6R6`,
the extension correction in `atlas-v0-a6-gold-writable-domain-evidence.md` partially supersedes only
that rule and the matching receipt destination pattern by also permitting the historically included
`.html` web entry. All other A2R15 scope, safety, fidelity, recovery, and contract requirements
remain active.

## 1. Context and threat model

Celesphonia Modifier is a local game save editor intended for one user on their own machine. The
accepted environment trusts the local user, the local administrator, the repository checkout, the
installed .NET runtime, and the application binaries selected by that user.

In scope are realistic local failures:

- accidental writes, renames, or deletions against original game data;
- copying from the wrong configured definition root;
- path escape from an owned output directory;
- source changes while a copy is running;
- interrupted writes, partial output, and corrupt copied bytes;
- accidentally including saves, `Game.exe`, or unsupported files; and
- ordinary malformed input or stale historical definition metadata.

Out of scope are adversarial local substitution and self-protection against the machine owner:

- malicious modification of requests, receipts, assemblies, Git history, or private workspace files;
- a hostile administrator replacing binaries or redirecting filesystem paths after validation;
- self-verifying executable hashes, exact branch/upstream state, or clean-worktree enforcement;
- exact Windows path casing as an identity or authorization boundary; and
- cryptographic binding of every generated document to every other generated document.

Security and review decisions must be proportional to this threat model. A proposed control is not
required merely because it can detect a theoretically possible local mutation. It must prevent a
credible in-scope data-loss, wrong-source, corrupt-output, or original-write failure at a maintenance
cost proportionate to that failure.

## 2. Supersession

After verified shared `R15`, this plan supersedes these A2R14 requirements:

- protected authorization candidates and `grant`/`not-granted` promotion;
- the protected session harness and its embedded self-test framework;
- runtime `G14` branch, upstream, clean-worktree, source-hash, and execution-file checks;
- discovery/copy r1 and r2 state documents;
- root-map, copy-plan, inventory, inventory-backup, and artifact-alias protocols;
- SHA-256 binding graphs between generated control documents;
- exact canonical JSON byte equality;
- repeated control-workspace topology censuses and drive-root ancestry walks;
- exact path spelling or casing checks on Windows;
- one-logical-attempt governance and terminal-precedence machinery;
- hidden diagnostic output and fixed success/refusal token matrices; and
- separate discovery and copy commands whose only purpose is protocol gating.

The following A2R14 boundaries remain:

- definition-only intake;
- no save discovery, traversal, copying, decoding, or qualification;
- no `Game.exe` content or metadata access;
- no writes, renames, or deletions against original game or save paths;
- historical definition authority remains read-only and is not rewritten or copied;
- `Game.exe`, `save`, and `www/save` are excluded before child metadata inspection or recursion;
- output is written only beneath a dedicated private workspace; and
- WinUI, networking, telemetry, distribution, and editor write-back remain out of scope.

## 3. Outcome and claim

A successful A2R15 operation may claim only that:

1. the configured historical definition authority matched its approved digest before parsing;
2. the configured live definition root reconciled with the historical included definition set before
   copying;
3. each included definition was copied read-only to a newly created private incomplete directory;
4. each destination matched the bytes read from its source handle;
5. the live definition root still reconciled after copying;
6. a receipt describing the copied files was written inside the incomplete directory; and
7. the incomplete directory was promoted to the final directory only after all checks completed.

This is a trusted-local, per-file copy claim. It is not hostile-local proof, a simultaneous filesystem
snapshot, semantic validation of definition content, save authority, or permission to modify original
game data.

## 4. Minimal product surface

A2R15 adds one CLI command:

```text
definition-intake <request-path>
```

The command accepts one explicit JSON request. It performs the complete definition intake in one
invocation. It does not discover inputs from the current directory, environment, registry, Steam,
network, or ambient workspace state.

The active contract set contains only:

- `atlas-definition-intake-request/v1`; and
- `atlas-definition-copy-receipt/v1`.

The request contains only:

- `schemaVersion`;
- `repositoryRoot`;
- `runId`;
- `definitionRoot`;
- `expectedHistoricalAuthoritySha256`;
- `expectedHistoricalAuthorityRevision`;
- integer `applicationId`;
- and integer `buildId`.

The receipt contains only:

- `schemaVersion`;
- `historicalAuthoritySha256`;
- `historicalAuthorityRevision`;
- integer `applicationId`;
- integer `buildId`;
- `runId`;
- `definitionRoot`;
- `finalCopyRoot`;
- and one entry per copied definition containing `sourceAlias`, `destinationRelativePath`, `length`,
  and `sha256`.

Contracts remain strict JSON with required properties, duplicate-property rejection, bounded depth,
and rejection of unknown properties. Finalized documents are validated semantically; they are not
required to retain one exact serializer byte representation.

## 5. Path and original-data safety

The operation normalizes paths with the platform path APIs and compares Windows paths
case-insensitively. It rejects path escape and case-colliding source entries but does not require the
caller to reproduce on-disk casing exactly.

`runId` is exactly 32 lowercase hexadecimal characters. The operation derives all writable paths:

```text
workspace root
  <repositoryRoot>/src/private/app/celesphonia-modifier/.private/
    atlas-definition-intake/<runId>
incomplete copy root
  <workspaceRoot>/definition-snapshot.incomplete
final copy root
  <workspaceRoot>/definition-snapshot
receipt
  <copyRoot>/definition-copy-receipt.json
```

The request cannot supply alternate writable paths. Before any mutation, the operation requires the
derived workspace root to be under that exact private parent and requires the incomplete root, final
root, and receipt leaf names to match exactly.

The definition root and workspace root must be distinct and neither may contain the other.

Targeted reparse checks remain on:

- the definition root;
- the workspace root;
- the incomplete and final copy roots when present; and
- every traversed source entry.

The operation does not walk or attest every ancestor to the drive root and does not census unrelated
control directories.

Original game and save paths are read-only. Source files are opened for reading without write or
delete access by this operation. All creates, writes, replacements, renames, and deletions are limited
to the exact operation-owned incomplete or final output roots beneath the private workspace.

## 6. Historical ingress

Historical ingress derives the fixed historical authority location from the repository layout,
verifies its approved SHA-256 before parsing, and reads only the definition projection needed by this
operation:

- survey alias;
- integer application ID;
- integer build ID;
- historical authority revision;
- definition groups and entries required for matching.

Save roots, save entries, executable policy, output paths, and historically inert request fields do
not participate in A2R15 validation. The historical authority bytes remain unchanged and are never
written into the new workspace.

## 7. Discovery and copy

The operation performs one complete pre-copy definition traversal. Before reading attributes or
recursing into a candidate, it excludes these root-relative paths case-insensitively:

```text
Game.exe
save
www/save
```

For all other entries it rejects path escape, reparses, unsupported types, duplicates, and
case-collisions. It applies the existing ordered definition rules and requires two-way reconciliation
with the historical definition projection.

The copy plan is an in-memory value, not a persisted contract. Included entries are ordered by source
alias. Each source extension comes from its validated historical relative path, must be `.js` or
`.json` case-insensitively, and is normalized to lowercase. Destination paths are deterministic:

```text
definitions/<source-alias>.js
definitions/<source-alias>.json
```

For each included source, the operation:

1. opens the source read-only;
2. captures source length and last-write metadata;
3. creates a new destination beneath the incomplete directory;
4. streams source bytes to the destination while computing SHA-256;
5. flushes and closes the destination;
6. reopens and hashes the destination;
7. requires source/destination length and digest equality; and
8. requires the held source metadata to remain stable.

After all files are copied, one complete post-copy traversal must reconcile with the same historical
projection and the pre-copy result. Only then is the receipt written and the incomplete directory
promoted to final.

## 8. Recovery

Recovery is directory-based:

- **Final directory absent, incomplete absent:** start a new copy.
- **Final directory present with a valid receipt and matching copied files:** return success.
- **Incomplete directory present with a valid receipt and matching copied files:** promote it to final
  and return success.
- **Incomplete directory present without a valid receipt:** delete only that exact operation-owned
  incomplete directory and restart the copy.
- **Final and incomplete directories both present:** refuse without modifying either.
- **Final directory present with a missing or invalid receipt:** refuse without overwriting it.

No inventory replacement, r1/r2 state, receipt staging protocol, changed-input recovery graph, or
source recapture prohibition marker is required. A restart after an incomplete copy is safe because
the operation deletes only its own incomplete output and never modifies the source.

A receipt is valid only when:

- its historical authority digest, revision, application ID, and build ID equal the values validated
  for the current request;
- its `runId`, definition root, and derived final copy root equal the current request and canonical
  layout;
- its entries are in deterministic source-alias order;
- its destination paths equal the in-memory mapping derived from the current historical projection;
- it lists every expected included definition exactly once and no other file; and
- each listed destination exists under the candidate copy root with the recorded length and SHA-256.

Any mismatch is invalid receipt evidence. Semantic equality is sufficient; serializer whitespace or
property formatting is not a recovery boundary.

## 9. CLI behavior

The command uses ordinary process semantics:

- exit `0` after a valid final receipt exists;
- return the existing CLI invalid-argument result for malformed invocation;
- return cancellation when cancellation is requested; and
- return a nonzero failure for validation, I/O, or output errors.

Useful concise diagnostics may identify the failing phase or invalid request field. They must not emit
definition contents, historical authority contents, or save data. A console-write failure remains a
process failure; it does not require special terminal-precedence recovery logic.

## 10. Acceptance evidence

Repository-safe synthetic tests must cover:

- strict request and receipt parsing;
- historical authority digest-before-parse and minimal definition projection;
- rejection of save/executable fields in active contracts;
- `Game.exe`, `save`, and `www/save` exclusion before child metadata or recursion;
- source/workspace overlap and path escape rejection;
- targeted root and traversed-entry reparse rejection;
- duplicate and case-colliding source rejection;
- two-way historical reconciliation;
- deterministic destination mapping;
- no original writes, renames, or deletions;
- source-change detection during copy;
- source/destination hash and length equality;
- corrupt or incomplete receipt rejection;
- rejection of every receipt/request/historical binding mismatch and every missing or extra copied
  file;
- final-directory idempotence;
- valid incomplete-directory promotion;
- incomplete-without-receipt cleanup and restart;
- refusal when final and incomplete roots both exist;
- pre-copy and post-copy tree-change detection;
- useful CLI success, invalid-argument, cancellation, and failure behavior; and
- preservation of existing A1 commands and tests.

Tests whose only purpose is malicious local substitution, exact path casing, exact JSON bytes, runtime
Git state, assembly hashing, source-text ordering, document SHA rebinding, artifact aliases, r1/r2
state permutations, authorization promotion, or terminal-token precedence must be deleted.

## 11. Review policy

Independent review remains required by the project operating model, but findings are adjudicated
against this threat model.

A finding is a true positive only when it demonstrates a realistic in-scope risk to:

- original-data integrity;
- input/output containment;
- wrong-source selection;
- copy fidelity;
- interrupted-operation recovery;
- definition-only scope; or
- maintainable correctness of the local workflow.

A finding based only on a malicious local user modifying private files, binaries, Git state, JSON
spelling, or path casing is out of scope unless it also demonstrates an accidental-failure path with
credible impact. Review must prefer deleting machinery or narrowing claims over adding protocols.
After two consecutive structural review rounds, planning must reset rather than continue hardening.

## 12. Git gates

The A2R15 gates are release provenance only; none is a runtime authorization mechanism.

### P15 - simplified plan candidate

`P15` is the direct child of exact `R14` and may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a2-definition-only-intake-correction.md
  atlas-v0-a2-local-definition-intake-simplification.md
```

The exact staged candidate receives independent plan review until `No findings`, then is committed and
pushed unchanged.

### R15 - simplified plan-review authority

`R15` is the direct child of exact `P15` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-local-definition-intake-simplification-plan-review.md
```

Verified shared `R15` activates this plan and authorizes implementation.

The review record minimally binds:

- exact `P15` commit and tree;
- exact reviewed path set;
- governing plan Git blob and SHA-256;
- reviewer identifier and independence statement;
- every review iteration and TP/FP disposition; and
- final `No findings`.

### C15 - simplified implementation candidate

`C15` is the direct child of exact `R15`. It contains the minimum production, schema, and test changes
needed by this plan. It must remove the obsolete A2R14 implementation and protected harness rather
than leaving parallel protocols.

### G15 - release record

`G15` is the direct child of exact reviewed `C15` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-local-definition-intake-simplification-release-gate.md
```

The record binds the candidate commit/tree, plan, reviewed paths, validation, findings and
dispositions, and final `No findings`. It grants no runtime authority and is not inspected by the
application.

## 13. Stop conditions and handoff

Stop and return to planning if implementation requires:

- save traversal, decoding, copying, or write-back;
- writes outside the exact owned output roots;
- a new persistent state machine or authorization protocol;
- a third active contract;
- runtime Git or binary self-attestation;
- network or telemetry access; or
- a threat assumption involving a malicious local user or administrator.

Resume from verified shared `R15`. Implement the single request/receipt workflow, delete obsolete
A2R14 machinery and tests, run repository-safe validation, and obtain independent review under
section 11. Do not run a real definition intake or operate on original save data as part of C15.
