# Atlas V0 A4 Synthetic Structural Scanner

**Lifecycle:** Conditional: proposed governing plan before verified shared `R4R1`; active normative
after verified shared `R4R1`

**Increment:** A4R1 - Synthetic Structural Scanner

**Decision owner:** Project leader

**Base:** Exact released `G3R1`
`376b6f8ccd1c578f6899c9f0fb94574b6eb479f0`

**Purpose:** Add a deterministic, privacy-safe, single-document structural scanner over the released
A3 graph reader without starting private-corpus research, semantic interpretation, editing, or
persistence.

> **No authority by presence**
> This file changes no active authority until its exact persisted candidate is independently reviewed,
> committed, pushed, and activated by verified shared `R4R1`. Git gates establish provenance only.
> They are never runtime authorization and never authorize a real save or private-corpus run.

## 1. Context and threat model

Celesphonia Modifier is trusted, single-user local software. A4R1 addresses credible scanner
correctness, deterministic output, privacy-safe representation, malformed-output handling, practical
resource bounds, cancellation, regression, and maintainability.

A4R1 does not address malicious replacement by the trusted owner or administrator. It adds no
authorization ceremony, runtime Git or binary attestation, document SHA-256 graph, r1/r2 state,
persistent protocol state machine, or private-corpus governance.

The only input is one in-memory, valid `AtlasSaveReadResult` produced by the public A3 reader plus one
closed `AtlasDocumentRole` value:

- `global-save`;
- `config-save`; or
- `slot-save`.

A4R1 is library-only and synthetic-only. It has no CLI, filesystem, snapshot, private-corpus,
multi-input aggregation, semantic interpretation, editing, save writing, or other persistence
surface.

## 2. Relationship to prior plans

Before verified shared `R4R1`:

- released `G3R1` remains the current implementation boundary;
- historical A4 scanner mechanics in `atlas-v0-execution-plan.md` remain blocked; and
- this candidate grants no implementation, scan, or private-data authority.

After verified shared `R4R1`, this plan:

- supersedes only the historical A4 scanner mechanics in section 10 of
  `atlas-v0-execution-plan.md`;
- partially supersedes only historical A4 scanner mechanics in
  `save-semantic-atlas-plan.md`;
- preserves the released A3 reader and every released A1 through A3 contract;
- leaves `LocatorSegmentRedactor` untouched as historical code and neither reuses nor removes it; and
- authorizes only the repository-safe synthetic C4R1 library, schema, and test changes defined here.

Historical A5 and later corpus, semantic, and editor material remains context without authority.
A4R1 does not activate it.

## 3. Outcome and claim

A successful A4R1 scan may claim only that one valid A3 materialized containment graph was traversed
completely in deterministic preorder and represented by a closed redacted structural document whose
census reconciles with the supplied A3 token and graph censuses.

It makes no claim about:

- scalar meaning or value;
- ordinary member names or equality;
- class meaning;
- source location or save identity;
- cross-document presence or variation;
- private-corpus completeness; or
- edit or write safety.

The scanner emits exactly one detached immutable observation for each A3 materialized graph node
occurrence reached through containment. Final models and canonical bytes retain no reference to A3
syntax, node, member, source-memory, or source-byte objects.

## 4. Locator and observation model

### 4.1 Role-relative typed locators

The document role is a closed top-level field. Every locator is relative to that role:

- the root locator has an empty segment list;
- an object child appends one `ordinary-member` segment containing only its zero-based ordinal in
  `AtlasJsonExObject.Members` order; and
- an array child appends one `array-element` segment containing only its zero-based element index.

No locator contains an ordinary key, member name, raw name lexeme, rendered path, JsonEx marker,
schema-key alias, dynamic-key alias, hash, or content-derived label. Duplicate ordinary names remain
distinct solely through occurrence ordinal; output does not reveal whether any names are equal.

A4R1 defines a new locator model. It does not reuse `LocatorSegmentRedactor`, `LocatorSegment`,
`LocatorAliasMap`, or their aliases.

### 4.2 Locator subjects

Every locator has exactly one closed subject:

- `node-occurrence` for a non-reference observation;
- `reference-occurrence` for a reference-wrapper observation; or
- `identity-definition` for an identity definition.

An identity-bearing object or array exposes an `identity-definition` locator with the same role and
segments as its node-occurrence locator. A reference exposes a target `identity-definition` locator.
Raw identity numbers are used only transiently by A3 and are never copied into an A4 model or byte
sequence.

### 4.3 Closed observation variants

The immutable observation family contains only these variants and facts:

| Variant   | Locator subject        | Closed facts                                                                                                                                                 |
| --------- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| scalar    | `node-occurrence`      | scalar kind: `text`, `number`, `true`, `false`, or `null`                                                                                                    |
| object    | `node-occurrence`      | shape: `plain-object` or `identity-object`; ordinary child count; class-marker presence Boolean; identity-definition presence and, when present, its locator |
| array     | `node-occurrence`      | shape: `plain-array` or `identity-array-wrapper`; element child count; identity-definition presence and, when present, its locator                           |
| reference | `reference-occurrence` | target identity-definition locator                                                                                                                           |

Use derived or variant records so irrelevant nullable fields are absent rather than serialized as
`null`.

The output must never contain:

- scalar lexeme, value, string length, numeric bucket, or other value-derived trait;
- source span, source bytes, compressed bytes, or byte counts from the input;
- path, file name, hash, save ID, account metadata, or input alias;
- ordinary property name, name equality, raw lexeme, or key classification;
- opaque class string or raw identity number;
- timestamp, random ID, digest, or content-derived label; or
- a scanner gap record.

## 5. Deterministic traversal

Traversal is iterative and uses two preorder passes. Preorder is exactly:

1. root;
2. object ordinary members in stored `AtlasJsonExObject.Members` order; and
3. array elements in ascending index order.

The first pass:

- assigns every containment occurrence its role-relative location;
- establishes identity-definition locators for identity-bearing objects and arrays;
- checks observation, depth, and retained-segment budgets before growth;
- treats each `AtlasJsonExReference` as a leaf and never traverses `reference.Target`; and
- rejects a containment cycle or any repeated non-reference containment node as unsupported A3
  internal state, distinguishing cycle from completed-node alias when possible.

The second pass:

- visits the first-pass occurrence sequence in the same preorder;
- creates one detached observation per occurrence;
- resolves each reference target only through the first-pass identity-definition map;
- rejects a target absent from containment or a target that is not an identity definition; and
- performs final structural validation and census reconciliation.

Repeated reference objects, if an unsupported internal mutation introduces them at multiple
containment edges, are separate reference occurrences at separate locators. Valid A3 graphs have
every reference target in containment and do not contain repeated non-reference containment nodes.

## 6. Scanner census and reconciliation

`AtlasStructuralScanCensus` contains exactly these nonnegative integer fields:

1. `nodeOccurrences`;
2. `objectOccurrences`;
3. `arrayOccurrences`;
4. `scalarOccurrences`;
5. `referenceOccurrences`;
6. `ordinaryMemberEdges`;
7. `arrayElementEdges`;
8. `identityDefinitions`;
9. `classMarkers`;
10. `identityArrayWrappers`; and
11. `distinctReferencedDefinitions`.

For A3 `TokenCensus` values `MemberOccurrences`, `ArrayElements`, `Scalars`, `IdentityMarkers`,
`ClassMarkers`, `ArrayMarkers`, and `ReferenceMarkers`, and A3 `GraphCensus` values
`MaterializedNodes`, `IdentityDefinitions`, `ReferenceEdges`, and `SharedTargets`, validation
requires checked arithmetic for all of the following:

```text
objectOccurrences + arrayOccurrences + scalarOccurrences + referenceOccurrences
  = nodeOccurrences
nodeOccurrences
  = observations.Count
  = GraphCensus.MaterializedNodes
identityDefinitions
  = TokenCensus.IdentityMarkers
  = GraphCensus.IdentityDefinitions
referenceOccurrences
  = TokenCensus.ReferenceMarkers
  = GraphCensus.ReferenceEdges
classMarkers
  = TokenCensus.ClassMarkers
identityArrayWrappers
  = TokenCensus.ArrayMarkers
arrayElementEdges
  = TokenCensus.ArrayElements
ordinaryMemberEdges
  = TokenCensus.MemberOccurrences
    - TokenCensus.IdentityMarkers
    - TokenCensus.ClassMarkers
    - TokenCensus.ArrayMarkers
    - TokenCensus.ReferenceMarkers
scalarOccurrences
  = TokenCensus.Scalars
    - TokenCensus.IdentityMarkers
    - TokenCensus.ClassMarkers
    - TokenCensus.ReferenceMarkers
nodeOccurrences - 1
  = ordinaryMemberEdges + arrayElementEdges
distinctReferencedDefinitions
  = GraphCensus.SharedTargets
```

`SharedTargets` is the existing released A3 name. A4R1 documents rather than renames it; for a valid
A3 result it denotes the number of distinct identity definitions targeted by references.
`GraphCensus.Cycles` remains an A3 resolved-reference diagnostic and is not renamed or duplicated in
the containment-only scanner census.

Validation also requires:

- unique primary locator paths and exactly one empty root;
- every non-root observation to have exactly one existing parent;
- parent kind and final segment kind to agree;
- contiguous object member ordinals and array indexes from zero;
- container child counts to equal their immediate children;
- no children beneath scalar or reference observations;
- observations to be in exact preorder;
- unique identity-definition locators;
- every identity-definition locator to equal its owning node path;
- every reference target locator to exist; and
- every reference target locator to name an identity definition.

Any omission, duplicate, reordering, target mismatch, overflow, or negative derived count is a census
or locator validation failure. There is no partial-success or gap-record form.

## 7. Canonical in-memory contract

The contract identifier is exactly `atlas-structural-scan/v1`. C4R1 adds one strict JSON Schema
2020-12 file:

```text
docs/.copilot/schemas/atlas-v0/atlas-structural-scan.schema.json
```

The canonical top-level property order is:

1. `schemaVersion`;
2. `documentRole`;
3. `census`; and
4. `observations`.

The census property order is the field order in section 6. Observations remain in traversal preorder.
A locator serializes `subject` then `segments`. Each segment serializes `kind` then its single
nonnegative integer field: `ordinal` for `ordinary-member` or `index` for `array-element`.

Observation property order is:

- scalar: `locator`, `kind`, `scalarKind`;
- object: `locator`, `kind`, `shape`, `childCount`, `classMarkerPresent`,
  `identityDefinitionPresent`, then `identityDefinitionLocator` only when present;
- array: `locator`, `kind`, `shape`, `childCount`, `identityDefinitionPresent`, then
  `identityDefinitionLocator` only when present; and
- reference: `locator`, `kind`, `targetIdentityDefinitionLocator`.

Canonical JSON uses invariant nonnegative integers, UTF-8 without BOM, no insignificant whitespace,
and exactly one trailing LF. Canonical serialization is deterministic and bounded; no path, file,
stream, or persistence API is added. The library may return the immutable model, canonical bytes, or
both through in-memory APIs.

The schema closes every object and variant with required properties, `additionalProperties: false`,
closed enums, integer ranges, and no explicit `null`. C4R1 provides an in-memory round-trip parser
that additionally rejects unknown or duplicate properties, nulls, wrong types, out-of-range
integers, noncanonical variant fields, malformed UTF-8, excess input bytes, and trailing content.
Source-bound validation receives the expected document role and supplied `AtlasSaveReadResult`
transiently, so a different otherwise valid role, wrong reference target, or self-consistent but
source-inconsistent mutation is rejected. The validated result retains none of that A3 input.
Parsing and validation return a fully validated immutable model or fail atomically.

## 8. Bounds, cancellation, and failure

`AtlasStructuralScannerLimits` is injectable and has these defaults:

| Limit                           | Default   |
| ------------------------------- | --------- |
| Observations                    | 1,000,000 |
| Locator depth                   | 256       |
| Total retained locator segments | 8,000,000 |
| Canonical UTF-8 bytes           | 256 MiB   |

The retained-segment total is the checked sum of segment instances logically retained by all primary,
identity-definition, and reference-target locators in the returned model. The scanner checks a limit
before appending, copying, growing a retained collection, or allowing a bounded writer/parser to
cross the canonical-byte limit. Traversal state is iterative; no input-depth recursion is permitted.

Cancellation is checked:

- before any work;
- throughout both traversal passes;
- while resolving references;
- during locator and structural validation;
- during census reconciliation;
- during canonical serialization; and
- during parsing and schema-model validation.

Cancellation throws `OperationCanceledException`. Other failures are classified without embedding
input content. Failure classes include at least:

- observation limit;
- locator-depth limit;
- retained-segment limit;
- canonical-serialization limit;
- duplicate locator;
- invalid locator;
- containment alias;
- containment cycle;
- missing reference target;
- census mismatch;
- malformed scan document; and
- unsupported internal state.

Every operation is atomic: it returns one complete validated result or throws. It returns no partial
model, partial byte buffer, partial observation sequence, or gap record.

## 9. Implementation boundary

C4R1 makes the minimum changes needed in:

- `Hcoona.CelesphoniaModifier.Atlas` for the immutable models, scanner, validation, and canonical
  in-memory serialization/parsing;
- the one schema path in section 7;
- `Hcoona.CelesphoniaModifier.Atlas.Tests` for repository-safe synthetic evidence; and
- project references or boundary tests only where required.

The library must not depend on the CLI. No CLI command is added. No production fault-injection hook,
filesystem adapter, corpus service, alias map, semantic layer, persistence layer, or writer is added.

## 10. Acceptance evidence

All graph-behavior fixtures use repository-safe synthetic compressed documents through the public A3
reader; test-only malformed internal-state fixtures are the sole exception. Tests include:

- exact hand-authored observations and typed locators for scalar, object, array, identity object,
  identity-array wrapper, reference, class marker, and mixed nesting cases;
- an independently implemented syntax-based test oracle that derives expected containment paths and
  censuses from the A3 lossless syntax rather than calling scanner helpers;
- deliberately mutated final models/documents proving rejection of an omitted subtree, object member,
  array element, identity, or reference;
- wrong reference target, duplicate locator, changed member ordinal, changed array index, and changed
  document-role failures;
- empty objects and arrays, sparse shapes, maximum-depth boundaries, self and longer reference cycles,
  shared targets, forward and backward references, duplicate ordinary names, unknown class strings,
  supplementary-plane scalar text, and preservation of A3's unpaired-surrogate JSON rejection;
- containment-alias and containment-cycle internal-state faults created only in test code, without
  production fault injection;
- repeated-run same-input order determinism;
- assertions that canonical output contains no prohibited value, key, raw identity, opaque class,
  source, path, hash, timestamp, random ID, alias, or content-derived label;
- every observation, depth, retained-segment, and canonical-byte limit at below/equal/above boundaries;
- cancellation before work and during traversal, resolution, validation/reconciliation,
  serialization, and parsing;
- strict schema validation, malformed-document rejection, and canonical parse/serialize round trip;
- exact census equations, locator closure, child counts, identity uniqueness, and target existence;
- preservation of all released A3 behavior and tests; and
- project-boundary tests proving that the new A4 scanner, models, serializer, and parser introduce no
  CLI, filesystem, or private-workspace dependency or surface.

The oracle and mutation tests must fail when the production scanner silently skips any materialized
occurrence or edge. Test diagnostics use only fixed repository-safe synthetic labels.

## 11. Explicit deferrals to A5

A4R1 removes historical A4 assumptions about content-derived or hashed key labels and scanner gap
records. It defers all of the following to a separately planned A5:

- private-corpus runs and repeatability;
- multiple input aliases or aggregation;
- input enumeration independence;
- cross-input variation or presence summaries;
- private alias mapping;
- real gap/accounting records;
- corpus manifests;
- persisted private observations; and
- qualification of any literal safe keys.

A5, private-data use, and every real scan remain blocked. A4R1 output is sufficient only as a
synthetic single-document structural primitive.

## 12. Review policy

The required independent holistic reviewer judges the complete exact candidate only for credible:

- scanner completeness or graph/locator/census correctness defects;
- privacy leakage;
- nondeterminism;
- malformed-output or resource-bound failures;
- cancellation failures;
- regression of released behavior; or
- maintainability problems that create one of those risks.

Findings based only on authorization ceremony, runtime Git/binary attestation, SHA document graphs,
r1/r2 state, persistent protocol state, private-corpus governance, semantics, or other deferred A5+
features are out of scope.

After two consecutive review rounds with structural findings, return to the ideal minimal design
instead of continuing incremental hardening. Stop review after the complete candidate and recorded
dispositions receive `No findings`.

## 13. Git gates

The A4R1 gates establish release provenance only. Runtime code never inspects commits, records, Git
state, source hashes, or binary hashes.

### P4R1 - plan candidate

`P4R1` is the direct child of exact `G3R1`
`376b6f8ccd1c578f6899c9f0fb94574b6eb479f0` and changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a4-synthetic-structural-scanner.md
  atlas-v0-execution-plan.md
  save-semantic-atlas-plan.md
```

An independent general-purpose GPT-5.6 reviewer examines the exact four-document candidate
holistically until `No findings`. The candidate is then committed and pushed unchanged.

### R4R1 - plan-review activation record

`R4R1` is the direct child of exact `P4R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a4-synthetic-structural-scanner-plan-review.md
```

The record binds the exact base and candidate, reviewed four-path set, reviewer identity and
independence, review iterations, atomic TP/FP dispositions, repository-safe validation, and final
`No findings`. It requires no document SHA-256 field or runtime check.

Verified shared `R4R1` activates this plan and authorizes only synthetic C4R1.

### C4R1 - implementation candidate

`C4R1` is the direct child of exact `R4R1`. It contains only the minimum library, single schema,
automated-test, and project-boundary changes required by this plan. It uses synthetic repository-safe
data only and is committed and pushed before holistic implementation review.

Review covers the complete exact candidate, acceptance evidence, privacy and resource boundaries,
regression risk, and recorded dispositions until `No findings`.

### G4R1 - release record

`G4R1` is the direct child of exact reviewed `C4R1` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a4-synthetic-structural-scanner-release-gate.md
```

The release record binds the exact candidate, governing plan and R4R1 record, reviewed paths,
validation commands and outcomes, findings and dispositions, reviewer independence, and final
`No findings`. The staged record itself receives independent `No findings`, is committed unchanged,
pushed, and verified as the shared tip.

`G4R1` records release provenance only. It grants no private run, real save access, persistence,
semantic interpretation, edit, or write authority.

## 14. Stop conditions and handoff

Stop and return to planning if implementation requires:

- a CLI, filesystem, snapshot, path, stream-persistence, or corpus surface;
- multiple input documents or cross-input aggregation;
- ordinary names, equality, aliases, hashes, values, class strings, identities, or content-derived
  labels in output;
- traversing `AtlasJsonExReference.Target`;
- recursion proportional to input depth;
- partial results or scanner gap records;
- production fault injection;
- changing or removing `LocatorSegmentRedactor`;
- real saves, definitions, game files, installations, ignored/private content, or `save-snapshot`;
- semantics, editing, or writes; or
- authorization ceremony, runtime attestation, document SHA graphs, or persistent protocol state.

Resume implementation only from verified shared `R4R1`. After verified shared `G4R1`, separately
plan A5 before any private, real, multi-input, persisted, or corpus work.
