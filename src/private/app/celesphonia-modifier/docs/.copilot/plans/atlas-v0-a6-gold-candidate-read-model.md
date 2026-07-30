# Atlas v0 A6R1 Gold Candidate Read Model

**Lifecycle:** Conditional: proposed governing plan before verified shared `R6R1`; active normative
after verified shared `R6R1`

**Increment:** A6R1 - Gold Candidate Read Model

**Audience:** Implementers, independent reviewers, and the project leader

**Decision owner:** Project leader

**Base:** Exact completed `G5R2`
`ebed5699f83d8aa91aa7867934e081bce58ac87b`

**Purpose:** Add a small in-memory Atlas-library API that reports two fixed Gold candidates from one
already-read slot without deciding semantics, authorizing editing, or using private input.

> **No authority by presence**
> This file changes no active authority until its exact persisted candidate is independently reviewed,
> committed, pushed, and activated by verified shared `R6R1`. The Git gates below establish provenance
> only. A6R1 is read-only and never grants write authority.

## 1. Context and proportional threat model

Celesphonia Modifier is trusted, single-user local WinUI software. Gold remains the required writable
MVP capability in `celesphonia-modifier-plan.md`, but that product requirement does not make either
observed location authoritative, coupled, gameplay-valid, or writable.

The released implementation base is:

- A3R1 reader at `G3R1`, corrected for game codec compatibility at `G3R2`;
- A4R1 structural scanner at `G4R1`;
- A5R1 explicit snapshot survey runner at `G5R1`; and
- private A5R2 completion at `G5R2`.

The A5R2 completion establishes sequencing only. A6R1 consumes no private result, receipt, survey
output, save, definition, installation, or ignored artifact. Its evidence is repository-safe
synthetic data passed through the released A3 reader.

A6R1 addresses credible accidental defects in exact member selection, duplicate handling, JsonEx
reference handling, shape classification, integer parsing, cancellation, input preservation,
value-free failures, and released-behavior regression. It does not address malicious replacement by
the trusted owner or administrator and adds no authorization ceremony, runtime Git or binary
attestation, manifest protocol, persistent state, or multi-party workflow.

## 2. Relationship to historical A6

After verified shared `R6R1`, this plan supersedes the historical A6 execution section in
`atlas-v0-execution-plan.md` and the corresponding semantic-correlation direction in
`save-semantic-atlas-plan.md` only for A6 execution.

A6R1 explicitly removes or defers:

- concept, claim, evidence, review, and revision schemas;
- a claim ledger, claim lifecycle, or frozen annotation population;
- semantic author and blind-review Agent passes;
- an installed-definition manifest prerequisite;
- source-coordinate and extracted-source-fact ledgers;
- cross-save variation, correlation, or outlier enumeration;
- Agent session, model, or semantic-provenance protocols;
- generated Atlas views or value-selection artifacts; and
- private execution or any dependency on private survey output.

Future work may propose those capabilities only through a separately justified increment. A6R1
retains only explicit uncertainty, fixed bounded extraction, privacy, no writes, and ordinary
independent plan and release review. Only general-purpose GPT-5.6 agents participate in those
reviews; A6R1 adds no product Agent runtime.

## 3. Outcome and authority

A successful A6R1 call may claim only that two fixed structural candidates were independently
examined in one valid `AtlasSaveReadResult` and classified under this plan:

1. exact object-member path `party`, then `_gold`; and
2. exact object-member path `variables`, then `_data`, then array index `215`.

It must not claim:

- that either candidate means Gold;
- that either candidate is authoritative, mirrored, derived, safe, or gameplay-valid;
- that equality proves coupling or disagreement proves corruption;
- that a negative or otherwise representable `Int64` value is allowed by the game;
- that either location may be edited; or
- that any write set, encoder, transaction, or E0-E3 evidence class is approved.

The aggregate is a read model for later product and research decisions. It is not an operation
specification, validator, capability gate, or write authority.

## 4. Public contract

C6R1 adds one public library entry point, named `AtlasGoldReadModel` or a clearly equivalent name:

```csharp
AtlasGoldReadModelResult Read(
    AtlasSaveReadResult source,
    CancellationToken cancellationToken = default);
```

The exact type names may follow established C# conventions, but the public information model is
closed:

- candidate state: `Present`, `Missing`, `Ambiguous`, `WrongShape`, `NonInteger`, or
  `OutsideInt64`;
- candidate result: one state and one nullable `Int64` value;
- aggregate state: `Consistent`, `Disagree`, or `Incomplete`; and
- aggregate result: the `party._gold` result, the `variables._data[215]` result, and aggregate state.

Immutable constructors or factories enforce:

1. candidate value is non-null if and only if candidate state is `Present`;
2. aggregate state is `Consistent` if and only if both candidates are `Present` with equal values;
3. aggregate state is `Disagree` if and only if both candidates are `Present` with unequal values;
4. aggregate state is `Incomplete` otherwise; and
5. callers cannot construct a contradictory result.

The API exposes no general path type, semantic registry, claim identifier, confidence score,
diagnostic payload, mutable collection, source node, or persistence contract.

## 5. Fixed extraction rules

Each candidate is evaluated independently with ordinal, case-sensitive member-name comparison.
Only ordinary `AtlasJsonExObject.Members` participate. JsonEx marker syntax has already been
materialized by A3 and is not treated as an ordinary candidate path.

At each fixed step:

1. check cancellation;
2. if the current node is a resolved `AtlasJsonExReference`, use its identity-bearing `Target` for
   the expected kind check;
3. require the exact object, array, or scalar kind for that step; and
4. perform only the one fixed lookup or index operation required by the candidate.

Object-member lookup has these outcomes:

- zero exact matches: `Missing`;
- one exact match: continue; and
- more than one exact match: `Ambiguous`.

Duplicates at an earlier relevant step stop only that candidate. Duplicate unrelated names do not
affect either result.

Shape and scalar rules are:

- an expected object, array, or final number scalar of another kind is `WrongShape`;
- array index `215` beyond the stored element count is `Missing`;
- a stored `null` or other non-number element at index `215` is `WrongShape`;
- a Number scalar is integral only when its exact A3 lexeme matches JSON decimal integer grammar
  `-?(0|[1-9][0-9]*)`;
- a Number scalar with a fraction or exponent is `NonInteger`;
- a syntactically integral lexeme outside `Int64.MinValue` through `Int64.MaxValue` is
  `OutsideInt64`; and
- every in-range integer, including negative values and both `Int64` boundaries, is `Present`.

The implementation uses fixed bounded steps and iterative member scans. It adds no recursion,
general traversal, configurable path language, or cross-document work. Cancellation is checked at
entry, between path steps, while scanning members, before and after reference dereference, and before
numeric parsing.

## 6. Immutability, privacy, and failures

The read model must not mutate or retain mutable access to:

- `AtlasSaveReadResult`;
- its lossless JSON document or UTF-8 source;
- its JsonEx graph, members, arrays, identities, or resolved references; or
- its original compressed bytes.

`GetSemanticNoOpBytes()` must return the same exact bytes before and after extraction. Returned
results contain only the two requested `Int64?` values and closed states; they contain no source
nodes, lexemes, names beyond the fixed public candidate labels, private paths, hashes, account data,
or other save content.

Ordinary absent, ambiguous, malformed-shape, non-integral, and overflow outcomes are states, not
exceptions. Cancellation throws `OperationCanceledException`. Any argument or internal-contract
exception uses fixed text and must not include a candidate value, scalar lexeme, source fragment, or
compressed bytes. The API performs no logging or diagnostics.

## 7. Implementation boundary

C6R1 changes only the minimum required files:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldReadModel.cs
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldReadModelTests.cs
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

`ProjectBoundaryTests.cs` changes only because the released test suite enforces exact library and
test file inventories. SDK implicit compilation requires no project-file edit.

C6R1 adds no CLI, schema, package, project, filesystem, snapshot, A5 manifest, definition,
installation, WinUI, encoder, writer, or persistence change.

## 8. Synthetic acceptance evidence

All fixtures are repository-safe synthetic compressed slot documents read through the public A3
reader. Cover:

- exact equal values producing `Consistent`;
- exact unequal values producing `Disagree`;
- each candidate state at least once without enumerating every status pair;
- a non-object root producing `WrongShape` for both candidates;
- representative `Missing`, `Ambiguous`, and `WrongShape` evidence where applicable at the `party`,
  `_gold`, `variables`, and `_data` member boundaries;
- duplicate `party`, `_gold`, `variables`, and `_data` members at their relevant object steps;
- reference-backed `party` and `variables` objects and a reference-backed `_data` array;
- short arrays and synthetic sparse-shaped arrays, distinguishing a missing index from a stored
  non-number at index `215`;
- zero, negative values, `Int64.MinValue`, `Int64.MaxValue`, and positive and negative overflow;
- rejection of decimal and exponent Number lexemes as `NonInteger`;
- `Incomplete` aggregate state for every representative non-present boundary fixture, without
  pairwise status combinations;
- cancellation before work and during a bounded member scan without production fault injection;
- unchanged graph observations, lossless JSON bytes, original compressed bytes, and exact
  semantic-no-op bytes before and after extraction;
- fixed value-free exception messages and absence of value-bearing logging or diagnostics;
- preservation of all released A3, G3R2, A4, and A5 tests and project boundaries; and
- constructor or factory rejection of every contradictory candidate or aggregate invariant.

Required validation before C6R1 review:

1. targeted Gold read-model tests;
2. the full Atlas test project;
3. Release build through `dirs.proj` with no new warnings;
4. `dotnet format --verify-no-changes` for the Atlas library and test projects;
5. applicable HK checks;
6. repository-safe content and privacy inspection; and
7. an exact diff proving only the three C6R1 paths changed.

## 9. Review policy

Fresh independent general-purpose GPT-5.6 reviewers examine the complete exact candidate against this
plan. Review is ordinary independent engineering review, not the historical semantic
author/blind-review protocol.

Findings are limited to credible:

- incorrect candidate or aggregate classification;
- ordinal-name, duplicate, reference, shape, or integer-boundary defects;
- mutation, value leakage, cancellation failure, or unbounded work;
- regression of released A3 through A5 behavior; or
- maintainability problems that create one of those risks.

Findings that demand semantic authority, gameplay-range validation, private evidence, definitions,
cross-save analysis, claim ledgers, Agent provenance machinery, generated views, editing, writing,
runtime attestation, or persistent protocols are out of scope.

Adjudicate findings under the project planning-correction policy. After two consecutive rounds with
structural findings, return to the ideal minimal design. Review completes only when the full exact
candidate and any dispositions receive `No findings`.

## 10. Exact Git gates

These gates are provenance controls, not runtime checks.

### P6R1 - plan candidate

`P6R1` is the direct child of exact `G5R2`
`ebed5699f83d8aa91aa7867934e081bce58ac87b` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a6-gold-candidate-read-model.md
  atlas-v0-execution-plan.md
  save-semantic-atlas-plan.md
```

An independent general-purpose GPT-5.6 reviewer examines the exact four-document candidate
holistically until `No findings`. The candidate is then committed and pushed unchanged.

### R6R1 - plan-review activation record

`R6R1` is the direct child of exact `P6R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-gold-candidate-read-model-plan-review.md
```

The record binds the exact base and candidate, reviewed four-path set, reviewer identity and
independence, iterations, TP/FP dispositions, repository-safe validation, and final `No findings`.
The exact staged record receives independent `No findings`, is committed unchanged, pushed, and
verified.

Verified shared `R6R1` activates this plan and authorizes only synthetic C6R1 implementation.

### C6R1 - implementation candidate

`C6R1` is the direct child of exact `R6R1`, changes only the three paths in section 7, and uses
repository-safe synthetic data only. It is committed and pushed before complete independent
implementation review.

### G6R1 - release record

`G6R1` is the direct child of exact reviewed `C6R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-gold-candidate-read-model-release-gate.md
```

The release record binds the exact candidate, governing plan and `R6R1`, reviewed paths, validation,
findings and dispositions, reviewer independence, and final `No findings`. The exact staged record
also receives independent `No findings`, is committed unchanged, pushed, and verified.

`G6R1` releases only the synthetic read-only Gold candidate model. Any private validation after
`G6R1` requires a separate explicitly approved plan and is not authorized here.

## 11. Stop conditions and handoff

Stop and return to planning if implementation requires:

- any private input, real save, definition, installation, ignored artifact, or A5 private output;
- a third Gold candidate or configurable/general path machinery;
- cross-save aggregation, semantic claims, gameplay range qualification, or write-set inference;
- a CLI, schema, package, project, filesystem, A5 manifest, WinUI, encoder, writer, or persistence
  change;
- mutation of the A3 graph, JSON, or compressed bytes;
- diagnostics or errors containing candidate values or source content;
- recursion or work beyond the fixed paths and ordinary member scans; or
- historical claim-ledger, dual-Agent, definition-manifest, generated-view, or provenance machinery.

Resume implementation only from verified shared `R6R1`. After verified shared `G6R1`, report the
released read-only boundary and stop. Do not inspect private data or plan private validation unless
the project leader separately authorizes that next increment.
