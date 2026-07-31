# Atlas V0 A6R6 Gold Writable-Domain Evidence

**Status:** Conditional governing plan before verified shared `R6R6`

**Base:** Exact shared suspension handoff
`33060488687eeb0b4dde9e96c359efa8b41c726d`

**Decision owner:** Project leader

**Outcome:** Correct the released definition-intake extension mismatch, then establish whether the
declared Celesphonia v1.05 Steam build 13624401 baseline has positive evidence for one Gold writable
domain, without reading or writing any save or changing the editor implementation

## 1. Authority and lifecycle

The project leader selected Gold-domain correction as the next outcome after G6R5 real-save writes
were suspended. This plan persists only the prerequisite evidence increment.

Presence of this plan grants no implementation or private definition access. Verified shared `R6R6`
authorizes only the five-path synthetic definition-intake and analysis-tool candidate in Section 9.
Verified shared `T6R6` authorizes the exact reviewed tool to perform the bounded private definition
copy and read-only analysis. No A6R7 correction implementation may be planned as authoritative until
verified shared `G6R6` records one of the terminal evidence outcomes below.

Historical A6R1-A6R5 plans and release records remain immutable evidence of their original
boundaries. This increment does not reactivate G6R5 real-save writes.

## 2. Evidence question and terminal outcomes

A6R6 answers one question:

> For the one declared Celesphonia v1.05 Steam build 13624401 baseline, what exact integer Gold
> domain is positively supported by the copied engine and game definitions, and what relationship,
> if any, do those definitions establish between `party._gold` and the observed candidate at
> variable 215?

The repository-safe result must be exactly one of:

- `EngineDefaultApplicable`: the copied baseline establishes the RPG Maker MV closed range
  `0..99,999,999`, complete load and mutation-sink closure contains no relevant override,
  contradictory limit, alternate normalization, sentinel use, or unresolved dynamic behavior, and
  positive definition evidence establishes the required variable 215 relationship;
- `Contradicted`: copied baseline definitions establish a different or incompatible rule; or
- `InsufficientEvidence`: intake, corpus reconciliation, static closure, mirror interpretation, or
  applicability remains ambiguous.

Only `EngineDefaultApplicable` permits an A6R7 correction plan to propose `0..99,999,999`. Neither
other outcome permits a write-capable Gold editor.

## 3. Scope and exclusions

In scope:

- a bounded A2R15 correction that permits the historically included `.html` web entry in addition
  to `.js` and `.json`;
- the corrected `definition-intake` command after independent tool review;
- one new protected, Git-ignored definition snapshot;
- the complete historical 496-file included definition set for the declared baseline;
- the copied package JSON, web entry HTML, engine, plugin, codec, and game-data definition bytes;
- public RPG Maker MV CoreScript corroboration from
  `https://github.com/rpgtkoolmv/corescript`, file `js/rpg_objects/Game_Party.js`, pinned to commit
  `182e31449707ba7e406db0485c44c2a9d11e2dcd` and `Game_Party.js` blob
  `2b607ee2db6a008649fe29d15523d7e49319946c`;
- read-only static analysis of Gold and variable 215 behavior; and
- one redacted repository-safe evidence record.

Out of scope:

- every live, historical, copied, decoded, or synthetic save;
- launching the game or WinUI application;
- writing, renaming, deleting, patching, or instrumenting installed game files;
- runtime hooks, process inspection, memory inspection, or gameplay automation;
- Gold reader, mutation, file-application, WinUI, or Gold-test changes;
- another game version, build, installation, language, mod, or plugin set;
- E3, compatibility recognition, distribution, or reactivation of private writes; and
- proof against intentionally obfuscated or adversarial game code.

The accepted local threat model trusts the game definitions as non-adversarial program text.
Ambiguous dynamic construction that could affect Gold is still `InsufficientEvidence`; it is not
silently treated as absence of an override.

## 4. Definition-intake correction

The preliminary 496-file definition denominator includes one `www\index.html` web entry required for
script-loading context. Released A2R15 rejects every historically included extension other than
`.js` and `.json`, so it cannot copy that approved denominator as specified.

The bounded correction:

- permits an included source extension of `.html`, case-insensitively;
- normalizes its destination extension to lowercase exactly as for `.js` and `.json`;
- extends the strict receipt schema destination pattern to admit only `.html`, `.js`, or `.json`;
- preserves all A2R15 traversal, historical reconciliation, original-read-only, per-file fidelity,
  recovery, and output-containment behavior; and
- adds synthetic tests for one included HTML entry, uppercase extension normalization, receipt
  validation, unsupported-extension refusal, and unchanged exclusion of `Game.exe`, `save`, and
  `www/save`.

The candidate also adds two repository-safe tools:

- `Analyze-GoldWritableDomain.ps1` performs the exact finite load, reference, alias, call, and
  mutation-sink analysis in Section 6. It is the sole producer of `intake-result.json`: it invokes
  the exact reviewed `definition-intake` candidate with the exact request, captures only fixed
  command tokens and the exit class, then either stops on intake refusal or assigns deterministic
  analysis classifications with every unproved case unresolved and emits the named private analysis
  JSON; and
- `Confirm-GoldWritableDomainEvidence.ps1` independently validates the finalized copy receipt,
  reruns the exact analyzer into verifier-owned private staging, requires byte-equal analysis
  outputs, validates copied-entry coverage, schemas, classification completeness, output bindings,
  and branch invariants, then emits the private `analysis-attestation.json`.

Both tools support a synthetic self-test mode covering applicable-default, contradiction,
unresolved-load, unresolved-dynamic-sink, unresolved-variable-215, missing-entry, and
tampered-analysis cases. They contain no private path, source alias, game excerpt, or baseline
digest. No private definition is accessed while implementing or reviewing this candidate.

## 5. Private inputs and outputs

The operator creates one strict `atlas-definition-intake-request/v1` under protected Git-ignored
storage. Its historical digest, revision, application ID, build ID, and definition root are derived
from the existing approved A2 authority and local baseline, not copied into Git or conversation.

The tracked analyzer and confirmer may also read the exact protected A2R15 historical request anchor
and historical definition authority already used by `definition-intake`. They must verify the
authority digest and baseline binding before parsing. This read-only authority supplies the original
relative paths, ordered groups, include/exclude decisions, and excluded-entry mappings that the
opaque copy receipt intentionally omits. The tools must not infer those mappings from source aliases
or destination names.

After verified shared `T6R6`, invoke its exact reviewed analyzer with one strict request containing a
fresh 32-character lowercase hexadecimal run ID. The analyzer invokes the exact reviewed
`definition-intake` candidate. That command must reconcile the complete historical definition
projection before and after copying, exclude `Game.exe`, `save`, and `www/save`, and create only its
defined incomplete/final snapshot and receipt paths.

The complete A6R6 private workspace inventory is:

```text
src\private\app\celesphonia-modifier\.private\
  atlas-definition-intake\<runId>\
    request.json
    intake-result.json
    definition-snapshot.incomplete\
      definition-copy-receipt.json
      definitions\
    definition-snapshot\
      definition-copy-receipt.json
      definitions\
    analysis\
      load-graph.json
      gold-mutation-sinks.json
      variable-215-sinks.json
      classification-summary.json
      analysis-attestation.json
      confirmation-staging\
        intake-result.json
        load-graph.json
        gold-mutation-sinks.json
        variable-215-sinks.json
        classification-summary.json
```

`definition-snapshot.incomplete` is optional operation-owned recovery state and is normally absent
after success. A refusal or interruption may retain it exactly as A2R15 permits. The analyzer and
confirmer do not delete or reinterpret it outside the reviewed A2R15 recovery behavior.

`intake-result.json` uses the strict private `gold-domain-intake-result/v1` contract. The tracked
analyzer creates it for every invocation with exactly:

- `schemaVersion`;
- analyzer and definition-intake candidate Git blobs;
- request SHA-256;
- `Success`, `UsageError`, `Canceled`, `IoError`, `SafetyError`, `ApprovalRequired`, or
  `UnexpectedError` exit class;
- one allowlisted stdout token and one allowlisted stderr token or `null`;
- one fixed phase token: `request`, `intake`, `receipt`, `analysis`, or `completed`; and
- UTC start and end timestamps.

It contains no request content, definition path, source alias, game content, or diagnostic excerpt.
On a non-success intake class the analyzer writes this file atomically and creates no snapshot or
analysis claim of its own.

After successful intake, the reviewed tracked analyzer reads only the finalized copy and writes only
the first four named analysis files. It also reads the verified protected historical authority solely
to restore original path and include/exclude mappings. The reviewed tracked confirmer reads the
historical authority, intake result, finalized receipt when present, copied definitions when present,
and available analysis files. It reruns the analyzer with the same exact request and candidate
beneath the exact `confirmation-staging` directory. For a successful intake it requires byte-equal
analysis outputs; for an intake refusal it requires the same exit class, fixed tokens, and phase while
allowing timestamps to differ. It then writes `analysis-attestation.json`. The JSON files are strict
private records:

- `load-graph.json` classifies every copied script as ordered-loaded, conditionally loaded,
  data-only, or unreachable and records every resolved edge and unresolved load expression;
- `gold-mutation-sinks.json` records every Gold definition, call, assignment, alias, property
  mutation, string-held reference, and dynamic sink with reachability and effect classifications;
- `variable-215-sinks.json` records every structured or script-held variable 215 read, write,
  comparison, range, alias, and dynamic sink with its semantic classification; and
- `classification-summary.json` binds the copied-entry count, per-category totals, zero-unclassified
  assertions, boundary conclusions, and terminal outcome.
- `analysis-attestation.json` has a required `Passed` or `Refused` status and binds the exact tracked
  analyzer and confirmer Git blobs, intake result, available receipt and analysis-output digests,
  copied-entry coverage, branch-specific invariant results, aggregate counters, and fixed refusal
  stage when applicable, without exposing private digests outside the run workspace.

These private files may contain source aliases, match locations, and minimal excerpts supporting the
deterministic classifications. They are analyzer outputs and must not be manually reclassified or
edited. They must never be staged, committed, attached, quoted into a review prompt, or supplied to
a subagent. A6R6 retains the exact run workspace read-only through G6R6 and any authorized A6R7
correction. Deletion requires a later explicit cleanup decision and must target only the exact
resolved run path without wildcards.

The confirmer requires `confirmation-staging` to be absent before it starts. On successful byte
comparison it removes only that exact resolved directory after durably writing the attestation. On
refusal or interruption it retains the exact staging directory for bounded diagnosis. A later rerun
must refuse until the operator explicitly removes only that exact directory after inspecting the
recorded refusal; no wildcard cleanup is permitted.

No A6R6 step accesses any save directory or file.

## 6. Analysis procedure

Analyze only the finalized copied definition snapshot.

### 6.1 Load and reachability closure

Establish a finite effective load graph before interpreting name occurrences:

1. bind every copied alias to its original relative path and historical decision using the
   digest-verified protected authority;
2. parse both copied package manifests and the copied web entry;
3. preserve the web entry's script-reference order and require every reference to resolve to one
   classified copied definition or one historically explicit non-semantic exclusion;
4. identify the copied bootstrap and plugin-loader behavior;
5. parse the copied plugin configuration, including enabled state and order, and resolve every
   configured plugin;
6. classify conservatively included but unlisted scripts as unreachable only with positive
   load-graph evidence; and
7. resolve literal and finitely enumerable dynamic loads from copied scripts and JSON.

Every copied entry and every referenced historical exclusion must be classified. An excluded entry
is accepted only when the digest-verified historical authority and copied load graph positively bind
it to an already approved non-semantic exclusion. A missing mapping or edge, unresolved computed
load, injected script path, unknown module source, or ambiguous enabled state is
`InsufficientEvidence`.

### 6.2 Engine rule

Identify the copied engine definition that provides `Game_Party`. Require one effective baseline
definition each for:

- `gold`;
- `gainGold`;
- `loseGold`; and
- `maxGold`.

The `EngineDefaultApplicable` branch requires evidence that:

- `maxGold` returns exactly `99999999`;
- `gainGold` assigns `_gold` through a closed clamp from `0` through `maxGold()`;
- `loseGold` delegates through `gainGold`; and
- the resulting range is exactly representable by the RPG Maker MV JavaScript runtime.

The pinned public CoreScript source is corroboration. The copied baseline engine definition is the
authority for the supported game/build.

### 6.3 Mutation-sink closure

Perform syntax-aware JavaScript analysis, structured traversal of every copied JSON definition, and
an exhaustive lexical fallback over every copied HTML, JavaScript, and JSON byte. Trace direct,
bracketed, aliased, destructured, or string-held references relevant to:

- `Game_Party`, `gold`, `gainGold`, `loseGold`, `maxGold`, and `_gold`;
- prototype assignment, property definition, alias replacement, monkey patching, and dynamic
  evaluation touching those names;
- numeric Gold limits, clamps, normalization, sentinels, or alternate currency storage; and
- plugin parameters or event-script content that changes Gold behavior.

Classify all writes through direct assignment, update operators, reflective property APIs,
prototype replacement, method aliasing, `$gameParty` calls, event-command scripts, plugin command
handlers, `eval`, `Function`, and finitely resolved computed names. Follow aliases and call edges to
a fixed point within the finite copied load graph.

Every relevant occurrence and possible mutation sink must receive a private classification. Any
unclassified occurrence, unresolved alias, unknown call target, computed property, constructed code
string, or reachable dynamic evaluation that could affect Gold makes the result
`InsufficientEvidence`. An effective override or contradictory rule makes it `Contradicted`.

### 6.4 Variable 215 hypothesis

Inspect the complete copied JSON and script corpus for variable 215 reads, writes, comparisons,
event-command ranges, script calls, plugin parameters, and indirect access patterns. Reconcile those
uses with only the released structural observation that all surveyed candidate pairs were present
and equal. Equality does not prove authority, mirroring, derivation, gameplay validity, or coupling.

`EngineDefaultApplicable` requires positive evidence that writing the same integer in
`0..99,999,999` to `party._gold` and variable 215 is the required game behavior and introduces no
different range, precision, sentinel, normalization, directionality, lifecycle, or cross-field
requirement. Absence of a contradictory reference is insufficient. If copied definitions do not
positively establish the relationship and write set, the outcome is `InsufficientEvidence`.

### 6.5 Exactness and boundaries

Record that every integer from `0` through `99,999,999` is exactly representable by both .NET
`Int64` and JavaScript `Number`. Evidence must explicitly classify:

- `-1`;
- `0`;
- `1`;
- `99,999,998`;
- `99,999,999`; and
- `100,000,000`.

No disclaimer, confirmation, or silent clamp may convert an unsupported value into an accepted
write.

## 7. Repository-safe evidence record

The candidate creates only:

`src/private/app/celesphonia-modifier/docs/.copilot/research/gold-writable-domain-evidence.md`

The record contains:

- declared game/build scope;
- public CoreScript repository, commit, blob, and observed default behavior;
- corrected definition-intake candidate and aggregate copied-file count;
- confirmation that the full historical included set reconciled;
- aggregate load-graph and mutation-sink closure counts;
- aggregate occurrence and classification counts by semantic category;
- the positive or unresolved variable 215 conclusion;
- the tracked analyzer and confirmer blobs, synthetic self-test outcome, private confirmer outcome,
  `Passed` or `Refused` status, fixed stage, and attested branch invariants;
- exactness and boundary conclusions;
- the terminal outcome and confidence;
- limitations and requalification triggers; and
- a statement that no save was accessed and no private path, hash, source alias, excerpt, or game
  text was committed.

The record must not contain private paths, source aliases, file hashes, plugin names or parameters,
game text, source excerpts, save values, or reconstructable private provenance.

Independent Assurance reviews the exact tracked analyzer and confirmer, their synthetic self-tests,
the repository-safe evidence record, and the branch invariants required by this plan. The private
confirmer revalidates the exact retained snapshot and analysis outputs after analysis is complete.
This is the privacy-safe assurance path for exact private evidence: the reviewer does not receive
private definitions or outputs, but does review the code that produced and verified them, the
synthetic fault evidence, and the redacted attestation conclusions.

## 8. Acceptance and terminal completion

Common completion requirements for every terminal outcome:

1. exact R6R6 ancestry and a clean synthetic implementation worktree are established;
2. the bounded definition-intake correction passes its targeted tests, full Atlas tests, build,
   formatting, analyzer/confirmer synthetic self-tests, and independent tool review;
3. exact T6R6 ancestry and a clean pre-private-execution worktree are established;
4. one bounded private intake/analysis attempt reaches one defined terminal branch without any save
   access or original-data mutation;
5. the confirmer emits one durable `Passed` or `Refused` private attestation bound to the exact
   available attempt artifacts;
6. the repository-safe record satisfies Section 7 and states no stronger conclusion than its
   terminal branch permits;
7. formatting, Markdown lint, spelling, EditorConfig, links, and `git diff --check` pass;
8. a fresh independent reviewer receives no private content, reviews the exact tracked tools,
   synthetic evidence, repository-safe candidate, and attested branch invariants against this plan,
   and returns `No findings`; and
9. the release record binds the exact candidates, T6R6 tool activation, terminal outcome, and
   branch-specific completion evidence.

`EngineDefaultApplicable` additionally requires:

1. the final receipt validates all 496 copied definitions, including the web entry HTML;
2. the load graph classifies every copied entry and resolves every potentially semantic load edge;
3. analysis covers every copied HTML, JavaScript, and JSON definition;
4. every possible Gold and variable 215 occurrence, alias, call edge, and mutation sink is classified
   with no unresolved dynamic path;
5. positive copied-definition evidence establishes the closed `0..99,999,999` engine rule and the
   required variable 215 relationship and write set; and
6. the private confirmer passes every applicable-default invariant.

`Contradicted` additionally requires:

1. a valid complete 496-entry snapshot;
2. positive copied-definition evidence that establishes one incompatible Gold rule, variable 215
   rule, or write-set requirement;
3. the private confirmer validates the contradiction classification and its evidence binding; and
4. the record makes no claim about an alternative writable domain unless full closure separately
   proves it.

`InsufficientEvidence` additionally requires:

1. a precise bounded stage: intake refusal, incomplete load graph, unresolved mutation sink,
   unresolved variable 215 relationship, analyzer/confirmer refusal, or another named plan stop;
2. private retention of the exact available request, receipt, analysis, and attestation artifacts
   without fabricating absent outputs;
3. a `Refused` attestation that validates the available artifacts and exact fixed refusal stage;
4. a repository-safe aggregate reason that exposes no private identifier or content; and
5. an explicit statement that no Gold write-capable correction is authorized.

The evidence reviewer then reviews the branch-specific candidate and returns `No findings`. A
negative terminal outcome is a completed A6R6 result, not a failed release process.

Stop and select `InsufficientEvidence` rather than broadening authority when:

- the installed definition set differs from historical authority;
- a copied file cannot be read or classified;
- the web-entry, bootstrap, plugin, module, or data load graph does not close;
- relevant dynamic behavior is ambiguous;
- variable 215 has an unresolved meaning or constraint;
- the tracked analyzer or confirmer cannot validate the exact branch;
- private evidence would need to enter Git or a subagent prompt;
- any save access appears necessary; or
- the conclusion would depend only on absence of known override evidence.

Select `Contradicted` when positive copied-definition evidence establishes an incompatible rule.

## 9. Gates

### P6R6 - plan candidate

All gate inventories below are repository-relative. `P6R6` is a direct descendant of exact
`33060488687eeb0b4dde9e96c359efa8b41c726d`. Its cumulative diff contains exactly:

1. `src/private/app/celesphonia-modifier/docs/.copilot/README.md`;
2. `src/private/app/celesphonia-modifier/docs/.copilot/plans/atlas-v0-a2-local-definition-intake-simplification.md`;
   and
3. `src/private/app/celesphonia-modifier/docs/.copilot/plans/atlas-v0-a6-gold-writable-domain-evidence.md`.

The candidate receives holistic independent review and correction until `No findings`.

### R6R6 - plan-review activation

After final P6R6 review, create only:

`src/private/app/celesphonia-modifier/docs/.copilot/reviews/atlas-v0-a6-gold-writable-domain-evidence-plan-review.md`

R6R6 is the direct child of P6R6 and binds the base, exact plan candidate, three-path inventory,
private-data boundary, procedure, findings, dispositions, and final `No findings`. It authorizes
only the synthetic definition-intake and analysis-tool candidate.

### C6R6T - definition-intake and analysis-tool candidate

C6R6T descends directly from exact R6R6. Its cumulative diff contains exactly:

1. `src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas/AtlasDefinitionIntake.cs`;
2. `src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/atlas-definition-copy-receipt.schema.json`;
3. `src/private/app/celesphonia-modifier/docs/.copilot/research-tools/Analyze-GoldWritableDomain.ps1`;
4. `src/private/app/celesphonia-modifier/docs/.copilot/research-tools/Confirm-GoldWritableDomainEvidence.ps1`;
   and
5. `tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasDefinitionIntakeTests.cs`.

The candidate receives targeted and full Atlas validation, PowerShell formatting and analysis,
synthetic analyzer/confirmer self-tests, and fresh independent review until `No findings`. Private
definitions and saves remain unaccessed.

### T6R6 - private evidence activation

After final C6R6T review, create only:

`src/private/app/celesphonia-modifier/docs/.copilot/reviews/atlas-v0-a6-gold-writable-domain-evidence-tool-review.md`

T6R6 is the direct child of final C6R6T and binds R6R6, the exact tool candidate and tree, five-path
inventory, validation, review iterations, final `No findings`, and the no-save boundary. Only verified
shared T6R6 authorizes Sections 5-8 against one fresh protected definition copy.

### C6R6E - evidence candidate

C6R6E descends directly from exact T6R6 and adds only:

`src/private/app/celesphonia-modifier/docs/.copilot/research/gold-writable-domain-evidence.md`

The private request, snapshot, receipt, analysis JSON, and classifications remain untracked.
Reviewed corrections may change only the repository-safe evidence record.

### G6R6 - evidence release gate

After exact C6R6E reaches one terminal branch under Section 8 and passes independent review, create
only:

`src/private/app/celesphonia-modifier/docs/.copilot/reviews/atlas-v0-a6-gold-writable-domain-evidence-release-gate.md`

G6R6 is the direct child of final C6R6E. It records the terminal outcome, exact tool and evidence
candidates and trees, governing R6R6 and T6R6, validation, review iterations, privacy attestation,
and next authority.

Only verified shared G6R6 with `EngineDefaultApplicable` permits drafting an A6R7 Gold correction
plan using `0..99,999,999`. G6R6 never authorizes implementation or private-save execution.

## 10. Resume procedure

- Before R6R6, review only the exact three-path P6R6 documentation candidate.
- After R6R6, implement and validate only the exact five-path definition-intake and analysis-tool
  candidate.
- After C6R6T, perform only tool review, bounded correction, and T6R6 activation.
- After T6R6, create one fresh protected definition snapshot and perform only Sections 5-8.
- After C6R6E, perform only repository-safe validation, independent review, bounded evidence-record
  correction, and G6R6 release gating.
- After G6R6, update the zero-context handoff and stop before implementation planning unless the
  terminal outcome is `EngineDefaultApplicable`.
