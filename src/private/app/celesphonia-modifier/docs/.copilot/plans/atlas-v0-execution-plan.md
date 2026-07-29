# Atlas v0 Execution Plan

> **Conditional partial supersession by A2R14**
> Before exact verified shared `R14`, this plan's prior status remains in force. After that event,
> `atlas-v0-a2-definition-only-intake-correction.md` replaces the old A2 save requirements with
> definition-only intake and blocks the A3 save-codec increment named below. A2R14 approves or designs
> no replacement A3. Presence of this notice or the A2R14 plan creates no implementation or private-run
> authority.

<!-- Separate independent authority notices. -->

> **Conditional A3R1 replacement**
> Before exact verified shared `R3R1`, the old A3 section remains blocked. After that event,
> `atlas-v0-a3-proportional-save-reader-foundation.md` supersedes only that section and governs the
> proportional save snapshot and lossless reader foundation. Presence of the plan, notice, or future
> record grants no implementation or real-snapshot authority.

<!-- Separate independent authority notices. -->

> **Conditional A4R1 replacement**
> Before exact verified shared `R4R1`, the historical A4 section remains blocked. After that event,
> `atlas-v0-a4-synthetic-structural-scanner.md` supersedes only its scanner mechanics and governs the
> synthetic single-document library increment. A5 and later sections remain historical context with
> no authority from A4R1.

**Status:** Confirmed execution baseline

> **Conditional A2 amendment**
> The revised A2 subsection takes effect only when the exact
> `atlas-v0-a2-intake-safety-plan.md` plan-review record is verified. Until then, A2 has no
> implementation authority.

**Implementation language:** C# on the repository-pinned .NET SDK

**Product scope:** Personal and private research; no WinUI or external distribution

## 1. Purpose

This plan turns `save-semantic-atlas-plan.md` into finite execution increments. An increment does
not begin until its acceptance criteria are recorded and testable. Completion means that all
criteria pass or the project leader explicitly narrows or stops the increment, and the independent
release gate in `project-operating-model.md` reports no findings. Effort alone is not progress
evidence.

## 2. Technology decision

Reusable research and production implementation starts in C#:

- Use the repository-pinned .NET SDK and `$(CurrentTargetFramework)`.
- Run .NET commands through `mise exec -- dotnet`.
- Build a reusable library, a thin command-line entry point, and automated tests.
- Keep decoding, traversal, canonicalization, redaction, schema validation, and view generation
  in reusable C# components.
- Keep PowerShell limited to orchestration and environment setup.
- Keep `research-tools/save-research-decoder.js` as non-executable historical context only. Do not
  run, extend, or use it as a compatibility oracle. After verified shared `R3R1`, compatibility
  evidence uses reviewed public and repository-safe synthetic vectors plus independently derived
  format facts.
- Permit another language only for a disposable experiment whose output is not canonical and
  whose code will not be migrated or shipped.

Atlas v0 has no in-product AI or ML dependency. Agents may propose and review semantic claims
from a validated redacted envelope. The C# tool validates schema, references, scope, provenance,
and redaction. No Agent output can alter scanner facts or issue E0 to E3 authority.

## 3. Proposed project boundary

The first scaffold contains:

- one reusable C# library for codec, structural scanning, canonical records, validation, and
  generation;
- one thin C# CLI for research-only commands; and
- one C# test project using repository test conventions.

Do not create the WinUI application, installer, writer kernel, graph database, Agent runtime, or
network service during Atlas v0.

Exact assembly names are an implementation detail, but the library must not depend on the CLI.
The future WinUI application must be able to reuse the library without invoking a child process.

## 4. Global acceptance rules

Every increment must satisfy:

1. its declared outputs exist in the intended private or repository-safe location;
2. targeted automated tests pass;
3. the relevant projects build with no new warnings;
4. cancellation and malformed-input behavior are explicit rather than silently ignored;
5. no command accepts a live save directory as a writable target;
6. no committed output contains raw saves, decoded values, private paths, save hashes, copied
   game text, account metadata, or narrative state;
7. failures produce a nonzero exit code and a classified diagnostic;
8. documentation and schemas change before or with behavior, never after an undocumented
   format change; and
9. any scope narrowing names the excluded item, evidence, consequence, approver, and revisit
   condition; and
10. an independent subagent reviews the complete increment and reports no findings after any
    required repair and re-review iterations.

After verified shared `R4R1`, rule 7 applies to A4R1 only if a later increment adds a command. The
library-only A4R1 surface instead throws a classified exception or `OperationCanceledException` and
has no process exit-code contract.

Any privacy leak, mutation of an original save, unexplained nondeterminism, silent structural
omission, or unapproved scope narrowing is a stop condition.

## 5. Canonical determinism contract

Canonical records use:

- UTF-8 without BOM and LF newlines;
- invariant culture and ordinal comparisons;
- schema-defined property order and ordinal record sorting;
- typed locator segments rather than platform path strings;
- normalized integers and schema-defined null handling;
- no current timestamps, machine names, private paths, or random IDs generated during
  projection;
- JSON Canonicalization Scheme-compatible encoding where a digest is required; and
- explicit digests for source commit, built scanner binary, .NET SDK, schema, redaction policy,
  configuration, and safe reference corpus.

After verified shared `R4R1`, the A4R1 canonical contract is the narrower contract in
`atlas-v0-a4-synthetic-structural-scanner.md`. Where this section conflicts, A4R1 uses its fixed
property order and traversal preorder rather than record sorting, contains no digest fields, and
requires no JCS compatibility beyond its explicitly defined canonical UTF-8 representation. The
sorting, digest, and JCS requirements above do not apply to A4R1.

Private-corpus **repeatability** means the same frozen private inputs and exact toolchain produce
the same canonical bytes. Independent **reproducibility** means a separate implementation run
reproduces expected outputs from a repository-safe synthetic reference corpus. Agent claims are
not regenerated for reproducibility; frozen validated claim ledgers are explicit projection
inputs.

## 6. Increment A0: Scope Decision and Research Contract

This increment is documentation and human confirmation. It does not require the scanner to
exist.

### Outputs

- Finite live-save discovery rule.
- Finite installed-definition root and file-selection rule.
- Versioned corpus-intake manifest schema.
- Versioned deny-by-default redaction policy.
- Private artifact inventory and lifecycle policy.
- Atlas v0 accounting-unit, denominator, and gap definitions.
- Agent egress policy.
- Test-data policy distinguishing synthetic, redacted, and private evidence.

### Acceptance criteria

- The discovery rule enumerates all existing `fileN.rpgsave`, `global.rpgsave`, and
  `config.rpgsave` entries before copying and terminally records every other encountered file.
- The installed-definition scope is a finite set of approved roots and file-selection rules;
  every discovered file receives an included, excluded, unreadable, or unsupported status.
- The default acceptable gap threshold is zero for in-scope baseline save roles and decoded
  regions.
- Unsupported, unreadable, excluded, or opaque in-scope content blocks Atlas v0 unless the
  project leader approves an explicit scope narrowing.
- Locator segments use a closed policy. The `atlas-schema-key-allowlist/v1` literal-key allowlist is
  empty, so every key receives a deterministic survey-local alias. Only schema-defined non-key
  tokens and numeric indexes may remain literal.
- The private artifact inventory records custody, purpose, last-use milestone, expiry, cleanup
  action, and verification method.
- Agent input is limited to a schema-validated redacted envelope. Raw or decoded saves, private
  values, private paths, private hashes, raw installed source, and uncontrolled prompt or log
  retention are prohibited.
- The user confirms corpus, fingerprint, installed-definition scope, exclusions, privacy policy,
  and narrowing authority before private intake begins.

### Stop conditions

- The corpus or installed-definition denominator remains open ended.
- Redaction requires guessing whether an arbitrary key is safe.
- Required private artifacts have no last-use milestone or destruction rule.
- Agent analysis requires unrestricted private-workspace or raw-source access.

## 7. Increment A1: C# Foundation

### Outputs

- Reusable C# library project.
- Thin C# CLI project or executable boundary.
- C# test project.
- Initial command model, dependency direction, cancellation, and exit-code conventions.

### Acceptance criteria

- A clean checkout restores, builds, and runs the targeted tests through repository commands.
- Nullable reference types and implicit usings follow repository conventions.
- The library has no dependency on the CLI, WinUI, JavaScript runtime, Agent SDK, or network
  service.
- The CLI contains no save semantics beyond argument binding and result presentation.
- A cancellation token reaches every asynchronous file and scan boundary.
- One smoke command emits a schema-versioned, deterministic empty survey result.

### Stop conditions

- The scaffold introduces a speculative architecture beyond the three project boundaries above.
- Core behavior can be exercised only through process invocation.

## 8. Increment A2: Intake and Safety Harness

### Outputs

- Read-only live discovery command.
- Human-reviewable discovery manifest.
- Qualified read-only snapshot command and private provenance map.
- Trusted-local-filesystem preflight and copy-verification checks.
- Non-deleting private-workspace lifecycle preflight.
- Locator-segment redaction classifier.

### Acceptance criteria

- Discovery runs before copying and enumerates every live save-directory entry.
- The user approves the exact discovery manifest and installed-definition manifest before copy.
- Live inputs are opened read-only and never used as scan inputs.
- Visible reparse checks, controlled roots, create-new destination semantics, source stability,
  and private fidelity hashes qualify copies under `trusted-local-filesystem/v1`.
- Private hashes verify byte-for-byte copy fidelity and remain outside Git.
- Every discovered live file and installed definition reaches one terminal intake status.
- `steam_autocloud.vdf` is always excluded.
- Dynamic locator segments are aliased before they can enter a canonical record.
- Lifecycle preflight reports eligibility without mutation; final deletion remains deferred to A8.

### Stop conditions

- The controlled workspace cannot satisfy the trusted-local-filesystem profile.
- Any discovered source lacks a terminal status.
- A dynamic locator segment can bypass deny-by-default aliasing.

## 9. Increment A3: Lossless Decode and Graph Reader

> **Conditional lifecycle**
> This historical A3 section is blocked before verified shared `R3R1`. After verified shared `R3R1`,
> it is superseded for A3 execution by
> `atlas-v0-a3-proportional-save-reader-foundation.md` and remains only as historical supporting
> context. The section below is intentionally not rewritten.

### Outputs

- C# LZ-String Base64 codec.
- JSON reader for `global.rpgsave` and `config.rpgsave`.
- JsonEx graph reader supporting `@`, `@c`, `@a`, and `@r`.
- Bounded parse profile and classified failures.
- Compatibility vectors against the existing JavaScript reference decoder.
- Independent token and graph census records.

### Acceptance criteria

- All approved reference vectors decode to structurally equivalent results in C# and JavaScript.
- Encode/decode compatibility vectors are byte-identical where the reference contract requires
  it.
- Every valid in-scope baseline save decodes successfully. A failure blocks progression unless
  the project leader narrows the declared corpus.
- Identity, array, class, and reference markers remain distinguishable in the graph model.
- Duplicate identities, dangling references, malformed wrappers, excessive depth, excessive
  nodes, cancellation, and decompression limits have automated tests.
- A semantic no-op path preserves the original compressed bytes rather than rebuilding them.
- No decoded private value appears in normal logs or test output.
- A token-level census records JSON containers, properties, array elements, scalars, and marker
  occurrences before graph resolution.
- A separate graph census records materialized occurrences, identity definitions, and reference
  edges.

### Stop conditions

- The C# implementation cannot match the reference vectors.
- A malformed input can cause unbounded allocation, recursion, or process-wide failure.
- The model discards unknown wrapper or reference information.
- Any in-scope baseline input remains unreadable without approved narrowing.

## 10. Increment A4: Deterministic Structural Scanner

> **Conditional lifecycle**
> Before verified shared `R4R1`, this historical A4 section remains blocked. After verified shared
> `R4R1`, its scanner mechanics are superseded by
> `atlas-v0-a4-synthetic-structural-scanner.md`; the text below remains historical supporting context
> and is intentionally not rewritten. A4R1 does not activate A5 or later work.

### Outputs

- Typed raw-locator model.
- Immutable redacted `Observation` model.
- Complete graph traversal.
- Presence, shape, wrapper, identity, reference, and cross-input variation summaries.
- Scanner visitation census and explicit scanner-gap records.

### Acceptance criteria

- Every reachable graph occurrence produces a locator-level observation; aggregation exists only
  in generated views, never in canonical structural coverage.
- Object properties, array positions, JsonEx identities, references, and document roles cannot
  collide in locator identity.
- Scanner output contains structural traits but no scalar values, user-authored text, or
  unclassified dynamic keys.
- Private-corpus runs follow the canonical determinism contract.
- Reversing input enumeration order does not change canonical output.
- Token census, graph census, and scanner visitation reconcile using schema-defined units.
- A separate test traversal and injected skipped-subtree faults prove that an omitted child,
  property, array element, identity, or reference causes acceptance failure.
- Automated tests cover empty, sparse, cyclic, shared-reference, malformed, and limit-boundary
  graphs.

### Stop conditions

- A reachable structure can disappear without a reconciliation failure.
- Locator identity depends on a semantic label or private value.
- Determinism requires retaining private source paths.
- Canonical output includes a segment not classified by the closed redaction policy.

## 11. Increment A5: Full Private Corpus Survey

### Outputs

- Frozen private survey manifest.
- Frozen installed-definition manifest.
- Redacted canonical observations.
- Structural completeness and gap report.
- Private evidence inventory with updated last-use milestones.

### Acceptance criteria

- Every source in the human-approved discovery manifests is terminally accounted for.
- No live original is opened for write or used as the scan source.
- Every in-scope baseline save decodes and traverses with zero opaque structural gaps.
- All token, graph, visitation, input, and installed-definition denominators reconcile.
- A structural redaction validator accepts every emitted field and locator segment.
- Repository-safe content checks find no prohibited private or game-owned payload.
- A second private-corpus run reproduces the canonical redacted structural output.
- Private decoded and correlation evidence remains protected until all A6 and A8
  private-evidence-dependent reviews complete.

### Stop conditions

- Any approved source or traversed region is unaccounted for.
- Redaction requires post hoc content guessing.
- Private repeatability produces an unexplained difference.
- Progress would require deleting evidence before its last review.

## 12. Increment A6: Preliminary Source Correlation and Claim Ledger

This increment is bounded preliminary annotation for value comparison. It does not perform
manual game actions, deep semantic proof, or operation qualification.

### Outputs

- C#-validated concept, claim, evidence, review, and revision schemas.
- Safe source-coordinate and extracted-source-fact model.
- Agent annotation envelope and independent-review protocol.
- Finite annotation-population manifest.
- Preliminary claim ledger with explicit independently-supported, unknown, disputed, rejected,
  and invalidated states.

### Claim lifecycle

```text
draft
  -> provenance-valid
  -> independently-supported

draft/provenance-valid/independently-supported
  -> disputed | rejected | invalidated
```

`independently-supported` means that one admissible interpretation has cited support and survived
one independent review. It is not accepted truth or write authority. `challenges` and
`needs-evidence` force `disputed` or `unknown`.

### Finite annotation population and effort limit

The C# tool generates the population before any Agent call:

- one item for each unique normalized combination of document role, typed locator pattern, and
  structural shape;
- every deterministic source-coordinate candidate;
- every cross-save correlation candidate;
- every variation outlier; and
- every explicit structural gap or unresolved redaction class.

Items without a safe source or correlation candidate become `unknown` automatically and do not
consume an Agent call.

Each eligible item receives:

1. at most one semantic author pass; and
2. at most one blind independent review pass.

One mechanical correction is allowed for a schema or provenance rejection. It may not introduce
a new semantic argument. There is no third Agent debate, recursive source expansion, runtime
experiment, or manual game action in A6. Every population item must end as
`independently-supported`, `disputed`, `unknown`, `rejected`, or `invalidated`.

### Acceptance criteria

- Every claim targets exact observations, concepts, claims, or source coordinates in one
  declared scope.
- The annotation-population manifest is generated before Agent work and records the exact item
  count, eligibility reason, maximum pass count, and terminal disposition.
- Every non-unknown semantic claim cites admissible evidence.
- Source coordinates validate against the frozen installed-definition manifest and exact
  private digest.
- Every independently-supported claim has a supporting review from a different author identity
  and Agent session. Review provenance records actor, session, Agent type, and model identifier.
- Reviewers receive frozen author output only after submitting an independent interpretation
  where the protocol requires blind review.
- Competing admissible claims remain present and disputed; no majority result is computed.
- Agent envelopes contain only redacted observations, safe source coordinates, and
  deterministic extracted facts. They contain no raw saves, decoded values, private paths,
  private hashes, or copied installed source.
- The C# validator rejects dangling references, invalid scopes, overwritten revisions,
  unsupported state transitions, self-review, stale source coordinates, unsupported tags, and
  private payloads.
- The C# validator rejects Agent passes beyond the frozen population budget.
- Atlas evidence tags remain separate from operation E0 to E3.

### Stop conditions

- Agent prose must be trusted without schema and provenance validation.
- A semantic label becomes the identity of a raw location.
- A challenged or needs-evidence claim becomes independently supported.
- Source reasoning requires uncontrolled raw-source or private-data egress.
- Agent work expands beyond the frozen population or pass budget.

## 13. Increment A7: Generated Atlas Views and Value Selection Brief

### Outputs

- Structure view.
- Meaning and decisions view.
- Unknown and dispute queue.
- Local relation and crosswalk projections.
- Completeness report.
- Value Selection Brief.

### Acceptance criteria

- Every observation is reachable from the Structure view.
- Every displayed semantic relationship traces to exact claim revisions and valid evidence.
- Unknown, independently-supported, disputed, rejected, and invalidated counts reconcile with
  canonical records.
- No internal key of any representation is used as a heading, breadcrumb, filename,
  user-facing path, or semantic label.
- Generated files are reproducible from frozen canonical observations and validated ledgers.
- Generated files contain no manually maintained facts.
- A candidate brief traces user outcome to relevant concepts, raw observations, claims,
  decisive unknowns, risk, estimated focused-research effort, and next experiment.
- The user can compare candidates without raw save values or a rigid domain taxonomy.

### Stop conditions

- A generated view becomes a competing source of truth.
- A global graph or fixed product IA is required to navigate Atlas v0.
- Human value review requires direct access to decoded private saves.

## 14. Increment A8: Atlas v0 Acceptance and Final Cleanup

### Outputs

- Final redacted Atlas v0 snapshot.
- Private-corpus repeatability report.
- Safe-reference-corpus independent reproducibility report.
- Projection reproducibility report from frozen observation and claim ledgers.
- Privacy, Agent-egress, and content-boundary report.
- Open-gap, unknown, and dispute register.
- Human-ready Value Selection Brief.
- Final cleanup attestation.

### Acceptance criteria

- All A0 through A7 acceptance criteria remain satisfied.
- A clean C# build reproduces the expected synthetic reference-corpus outputs.
- A second run over the frozen private copies reproduces the canonical structural outputs.
- Frozen validated claim ledgers reproduce all semantic projections without rerunning Agents.
- Independent review finds no silent structural omission, private-data leak, uncontrolled Agent
  egress, unsupported semantic promotion, or operation-authority claim.
- Every observation is classified as semantically independently supported, disputed, or
  unknown.
- The snapshot states its corpus, fingerprint, source manifest, scanner binary, source commit,
  SDK, schema, redaction policy, configuration, and limitation scope.
- All private-evidence-dependent reviews finish before cleanup.
- The final cleanup removes decoded temporary content and records verified disposition for every
  private artifact.
- Cleanup and its attestation finish before the exact A8 release candidate is committed for
  independent review.
- No E2 or E3 authority is issued merely because Atlas v0 is complete.

### Stop conditions

- Any independent denominator does not reconcile.
- Required private evidence is deleted before last use or retained past its policy.
- The Value Selection Brief cannot trace a candidate to canonical evidence.
- Independent reproduction works only with private corpus access.

## 15. Product Checkpoint After Atlas v0

After A8, the project leader presents the Value Selection Brief. The user confirms:

1. which user outcomes merit focused research;
2. which disputes materially affect those outcomes;
3. which targeted manual game actions are acceptable; and
4. the maximum research effort before narrowing or stopping a candidate.

Only then may the project create a focused experiment plan or an E2 disposable-copy
authorization.
