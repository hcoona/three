# Atlas v0 A6R2 Explicit Gold Snapshot Validation

**Lifecycle:** Conditional: proposed governing plan before verified shared `R6R2`; active normative
after verified shared `R6R2`

**Increment:** A6R2 - Explicit Gold Snapshot Validation

**Audience:** Implementers, independent reviewers, and the project leader

**Decision owner:** Project leader

**Base:** Verified shared `G6R1`
`8064e62f95dc25b9f5ab785e5ebce444e68e7c61`

**Purpose:** Determine whether the released fixed A6 Gold candidates are structurally usable across
every slot copy in one explicitly identified, already-finalized A3 snapshot before investing in a
mutation kernel.

> **No authority by presence**
> This file grants no implementation or private-read authority until its exact persisted gates pass.
> A6R2 is read-only. It never establishes Gold semantics, gameplay validity, a write set, or write
> authority.

## 1. Boundary and proportional posture

Celesphonia Modifier is a trusted single-user local save editor. A6R2 addresses ordinary accidental
defects in request parsing, finalized-snapshot validation, slot enumeration, A3 parsing, A6
classification, counting, cancellation, deterministic CLI output, and privacy. It does not defend
against malicious replacement by the trusted owner or administrator.

Synthetic implementation comes first. Private execution is a separate gate after runner release and
requires the user to explicitly authorize and identify the exact finalized A3 receipt. Planning,
implementation, review, and release access no live save root, originals, A5 survey output,
definitions, installation, or other ignored artifact.

A6R2 explicitly rejects historical A7 generated views, semantic claim ledgers, Agent protocols,
high-assurance or runtime attestation, full transaction/recovery/installer/WinUI work, and every
encoder or writer. It adds no output artifact, database, manifest, report, cache, cleanup workflow,
authorization ceremony, or persistent private state.

## 2. Outcome and authority

For every slot copy represented in one valid finalized A3 receipt, the runner applies the released
A3 reader and A6 Gold read model, then returns only an immutable aggregate summary:

- total slot count;
- `Consistent` count;
- `Disagree` count;
- `Incomplete` count; and
- overall state `AllConsistent`, `DisagreementObserved`, `IncompleteObserved`, or
  `DisagreementAndIncompleteObserved`.

The counts must be non-negative and reconcile exactly:

```text
totalSlots = consistent + disagree + incomplete
```

`totalSlots` must be positive. Overall state is derived, never caller-selected:

- `AllConsistent`: every slot is `Consistent`;
- `DisagreementObserved`: at least one `Disagree` and no `Incomplete`;
- `IncompleteObserved`: at least one `Incomplete` and no `Disagree`; and
- `DisagreementAndIncompleteObserved`: both `Disagree` and `Incomplete` occur.

The public result contains no per-slot result, candidate state, value, filename, path, hash, scalar
lexeme, decoded content, or mutable collection. It cannot support inference that either fixed
candidate means Gold, that agreement proves coupling, that disagreement means corruption, that a
range is valid, or that either path may be written.

The public runner contract is:

```csharp
ValueTask<AtlasGoldSnapshotValidationSummary> RunAsync(
    string requestFilePath,
    CancellationToken cancellationToken = default);
```

`AtlasGoldSnapshotValidationSummary` is the immutable result described above. Its constructor or
factory is closed over the reconciliation and derived-state invariants; callers cannot construct a
contradictory summary. Test-only overloads may inject the existing I/O seam and limits already
enforced by reused components, but the public contract exposes neither.

## 3. Request, selection, and processing

The command is:

```text
snapshot-gold-validate <request-file>
```

The strict request schema is `atlas-gold-snapshot-validation-request/v1` and has exactly:

- `schemaVersion`;
- `repositoryRoot`; and
- `snapshotReceiptPath`.

All three properties are required. Unknown, duplicate, missing, null, wrong-type, malformed,
oversized, relative-path, and schema-version inputs are refused with existing request behavior. The
request has no `runId`, output path, selector, limits, or policy field. Runtime derives no receipt
from repository state, prior runs, an installation, or an A5 artifact.

The request limit is 64 KiB of UTF-8 JSON, with maximum JSON depth 8, 256 tokens, 32,768 UTF-16 code
units per string, and 20 ASCII characters per numeric token. These are fixed implementation limits,
not request fields.

The runner must:

1. parse the request using the established strict bounded-request pattern;
2. call `AtlasFinalizedSaveSnapshot` to validate the exact finalized A3 receipt and every copied
   file against it;
3. traverse validated receipt entries in receipt order without sorting or narrowing;
4. process every entry whose canonical name is `file1.rpgsave` through `file20.rpgsave`;
5. exclude `global.rpgsave` and `config.rpgsave`;
6. refuse a valid receipt containing zero slot entries;
7. reopen each selected copied file once after finalized-snapshot validation through the existing
   I/O seam, read it within the A3 encoded input limit while computing its length and SHA-256, and
   refuse with `AtlasSafetyException` unless the read bytes and post-read file length match the
   validated receipt entry;
8. pass those exact verified bytes to A3 `AtlasSaveReader`, then call A6 `AtlasGoldReadModel`;
9. increment exactly one aggregate count from the A6 aggregate state; and
10. return the reconciled immutable summary without retaining per-slot results.

There is no subset mode, general selector, alternate filename rule, or silent skip. Snapshot
validation failure refuses the operation through existing safety behavior. Any selected slot parse
failure refuses the whole operation through existing `AtlasSaveReadException` behavior. An A6
candidate non-present state is not a parse refusal: its slot contributes exactly once to
`Incomplete`. Cancellation remains `OperationCanceledException`. The runner adds no broad catch and
does not translate unexpected failures into accepted results.

Refusal categories are explicit: malformed requests throw `AtlasRequestException`; an invalid,
changed, or zero-slot finalized snapshot throws `AtlasSafetyException`; selected-slot parse failure
propagates `AtlasSaveReadException`; finalized-snapshot validation retains its existing normalization
of I/O failures to `AtlasSafetyException`; ordinary I/O failures after validation propagate their
existing typed exceptions; and unexpected exceptions propagate to the existing CLI
unexpected-failure mapping. All new exception messages are fixed and value-free.

## 4. CLI, privacy, determinism, and preservation

Success writes exactly one invariant-culture LF-terminated line:

```text
Gold snapshot validation: <state>; total=<n>; Consistent=<n>; Disagree=<n>; Incomplete=<n>.
```

The four state names are exact. Decimal slot counts are acceptable low-sensitivity local output.
Stdout and stderr must never contain save values, candidate-state breakdowns, filenames, paths,
hashes, scalar lexemes, decoded content, request content, receipt content, or exception text.
Existing fixed usage, cancellation, I/O, safety, and unexpected diagnostics and exit-code patterns
remain authoritative.

The operation creates, changes, or deletes no file and persists no private state. Repeated execution
over unchanged request and snapshot inputs returns the same summary and exact stdout bytes.
Validation and reading must leave every snapshot copy byte-for-byte unchanged. For every successful
A3 read, its original compressed-byte copy, lossless source representation, graph observations, and
`GetSemanticNoOpBytes()` result remain unchanged before and after A6 classification.

## 5. Exact implementation boundary

`C6R2` changes exactly these paths:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldSnapshotValidation.cs
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldSnapshotValidationContracts.cs
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliApplication.cs
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliOperations.cs
  docs/.copilot/schemas/atlas-v0/
    atlas-gold-snapshot-validation-request.schema.json
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldSnapshotValidationTests.cs
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasCliApplicationTests.cs
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasProcessSmokeTests.cs
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

`AtlasProcessSmokeTests.cs` is required because it carries the exact process-level command inventory
and help bytes. `ProjectBoundaryTests.cs` is required because it enforces exact source, test, and
schema inventories. SDK implicit compilation requires no project-file change. There is one request
schema and no output schema.

## 6. Acceptance evidence

Repository-safe synthetic tests cover:

- all four overall states and constructor/factory rejection of unreconciled or contradictory counts;
- all receipt slot entries processed exactly once in receipt order, with no subset or selector;
- `global.rpgsave` and `config.rpgsave` excluded even when their content would fail parsing;
- refusal when no slot entry exists;
- invalid, changed, incomplete, or otherwise non-finalized snapshot refusal;
- read-time length or hash change refusal before classification;
- selected-slot parse failure refusing the whole operation with no accepted partial result;
- representative A6 non-present candidate outcomes counted as `Incomplete`;
- cancellation before work and during validation, entry reading, parsing, and classification, plus
  explicit checks before aggregate updates and before return;
- deterministic repeated summaries and exact CLI bytes;
- stdout and stderr exclusion of values, candidate-state breakdowns, names, paths, hashes, lexemes,
  decoded content, and exception details;
- no output writes, no persistent state, and byte-identical snapshot copies;
- unchanged A3 source copies, graph observations, and exact semantic-no-op bytes;
- strict request parsing, exact three-property schema parity, and no output schema;
- CLI global/command help, routing, success, request, cancellation, I/O, safety, parse-refusal, and
  unexpected exit behavior; and
- full released A3 through A6 regression coverage and exact project-boundary inventories.

Before implementation review:

1. targeted A6R2 tests pass;
2. the full Atlas test project passes;
3. the Release build through `dirs.proj` has no new warning or error;
4. `dotnet format --verify-no-changes` passes for affected .NET projects;
5. the request schema executes against valid and mutated fixtures;
6. applicable HK checks pass;
7. repository-safe privacy and no-write inspection passes; and
8. the exact diff contains only the nine paths in section 5.

## 7. Review policy

Fresh independent general-purpose GPT-5.6 reviewers examine the complete exact plan, implementation,
and release or completion record candidates against this plan until `No findings`. Review is
ordinary engineering review, not an Agent protocol or high-assurance attestation system.

Credible findings concern request strictness, snapshot validation, complete ordered slot processing,
classification or count derivation, mutation, writes, leakage, nondeterminism, cancellation, CLI
behavior, regressions, or maintainability defects that create those risks. Demands for semantics,
range qualification, coupling claims, private disclosure, general selectors, generated views,
ledgers, Agent machinery, attestation, transactions, recovery, installers, WinUI, encoding, or
writing are out of scope.

Adjudicate every finding under the project planning-correction policy. Review completes only when
the full exact candidate and any dispositions receive `No findings`.

## 8. Exact gates

These gates establish provenance and bounded authority; runtime never inspects Git state.

### P6R2 - plan candidate

`P6R2` is the direct child of exact `G6R1`
`8064e62f95dc25b9f5ab785e5ebce444e68e7c61` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a6-explicit-gold-snapshot-validation.md
```

A fresh independent general-purpose GPT-5.6 reviewer examines the exact two-document candidate
holistically until `No findings`. It is then committed and pushed unchanged.

### R6R2 - activation record

`R6R2` is the direct child of exact `P6R2` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-explicit-gold-snapshot-validation-plan-review.md
```

The record binds the exact base and plan candidate, reviewed paths, reviewer identity and
independence, iterations, TP/FP dispositions, validation, and final `No findings`. Its exact staged
candidate receives fresh independent general-purpose GPT-5.6 `No findings`, is committed unchanged,
pushed, and verified. Verified shared `R6R2` activates this plan and authorizes only synthetic
`C6R2`.

### C6R2 - implementation candidate

The initial `C6R2` implementation is the direct child of exact `R6R2`, changes exactly the nine paths
in section 5, uses only repository-safe synthetic data, and is committed and pushed before review.
Any accepted review corrections descend from that implementation and retain the same exact
cumulative nine-path diff from `R6R2`. A fresh independent general-purpose GPT-5.6 reviewer examines
the complete exact final candidate and acceptance evidence until `No findings`.

### G6R2 - runner release

`G6R2` is the direct child of exact reviewed `C6R2` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-explicit-gold-snapshot-validation-release-gate.md
```

The record binds the exact candidate, governing plan and `R6R2`, reviewed paths, validation,
findings and dispositions, reviewer independence, and final `No findings`. Its exact staged
candidate receives fresh independent general-purpose GPT-5.6 `No findings`, is committed unchanged,
pushed, and verified. `G6R2` releases only the read-only runner and grants no private execution by
presence.

### X6R2 - separately authorized private execution completion

After verified shared `G6R2`, the user must separately and explicitly authorize one private execution
and identify the exact finalized A3 receipt. The receipt must not be inferred. Execution uses the
released unchanged command, creates no output artifact, and performs no repository change before its
completion record.

Any overall state other than `AllConsistent` stops the sequence. The repository-safe completion
classification is limited to `all-consistent-completed`, `validation-stopped-nonconsistent`, or
`validation-refused`; it contains no private counts, detailed state, values, names, paths, hashes,
lexemes, decoded content, or candidate breakdowns.

Successful command exit with `AllConsistent` maps to `all-consistent-completed`; successful command
exit with any other overall state maps to `validation-stopped-nonconsistent`; and an ordinary
request, finalized-snapshot, zero-slot, or parse refusal maps to `validation-refused`. Cancellation,
I/O failure, or unexpected failure produces no `X6R2` candidate: stop and return to planning without
publishing a completion classification.

`X6R2` is the direct child of exact `G6R2` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-explicit-gold-snapshot-validation-private-execution-completion.md
```

The record binds the authorization class, exact released runner, repository-safe completion
classification, and value-free validation procedure. Its exact staged candidate receives fresh
independent general-purpose GPT-5.6 `No findings`, is committed unchanged, pushed, and verified.

## 9. Stop conditions and next boundary

Stop and return to planning if implementation requires any unlisted path, output file, persistent
state, live save or original access, A5 output, definition, installation, ignored artifact, selector,
per-slot output, semantic claim, range rule, coupling inference, writer, encoder, transaction,
recovery system, installer, WinUI work, generated view, ledger, Agent protocol, or attestation.

Resume synthetic implementation only from verified shared `R6R2`. Resume private execution only
after verified shared `G6R2` and the user's separate exact-receipt authorization. A non-consistent or
refused private result stops without semantic interpretation or remediation authority.

Only after verified shared `X6R2` classified `all-consistent-completed` may the next plan target a
synthetic, in-memory, fixed two-path Gold mutation and re-encoding kernel. That future plan must still
exclude filesystem writes and cannot inherit semantic, range, write-set, or write authority from
A6R2.
