# Atlas V0 A2 Current Baseline Observation

**Lifecycle:** Active subordinate; planning-only before verified shared `R10`

**Status:** Observer construction blocked

**Increment:** A2R10 - Current Baseline Observation

**Decision owner:** Project leader

**Audience:** Project leader, observer implementers, independent reviewers, and future resumers

**Purpose:** Record and validate the exact current baseline bytes needed to choose the next A2 step
without claiming historical identity or a simultaneous filesystem snapshot.

**Implementation language:** Session-only C#

**Base:** `463ff25f6be8e993543d90d5d1e1a78eb2558867`

**Governing sources:**

- `project-operating-model.md`;
- `atlas-v0-a2-intake-safety-plan.md`;
- `../reviews/atlas-v0-a2-approved-manifest-authority-correction-release-gate.md`.

**Historical provenance:**

- `atlas-v0-a2-baseline-authority-diagnosis.md`; and
- `../reviews/atlas-v0-a2-baseline-authority-diagnosis-plan-review.md`.

The historical A2R9 sources establish why that increment stopped. They grant no A2R10 authority and
do not import A2R9 execution controls.

**Dependencies:** Verified shared A2R9 `R`, unchanged A2R8 Atlas source, preserved current inputs,
the approved `trusted-local-filesystem/v1` profile, and independent review of this plan and the
exact session observer source.

**Planned plan-review record:**
`../reviews/atlas-v0-a2-current-baseline-observation-plan-review.md`

**Planned completion record:**
`../reviews/atlas-v0-a2-current-baseline-observation-completion.md`

## 1. Correction and claim

A2R9 correctly stopped because no retained SHA-256 can prove that the current discovery request is
the request used by the consumed A2R8 attempt. A current hash must not be presented as historical
evidence.

A2R10 observes current state instead. It records the exact bytes of each document successfully
returned by a released reader and validates their released contracts and relationships. Every
fingerprint is a per-file observation. A refusal may contain only the fingerprints returned before
or during its terminal stage. A2R10 does not claim that:

- any current byte was used by A2R8;
- different files represent one simultaneous point in time;
- a path or file remained unchanged after its own read completed; or
- the observation authorizes discovery, repair, confirmation, copy, cleanup, or a write to an
  existing private input or operational Atlas artifact.

The earlier draft drifted beyond this claim. This plan removes the A2R8 wrapper interpreter, wrapper
hash binding, held-request protocol, one-shot invocation authority, process journal, Git
hermeticity checks, and result-oblivious routing. Those mechanisms addressed threats outside the
accepted profile or compensated for claims A2R10 no longer makes.

## 2. Threat model and scope

A2R10 inherits `trusted-local-filesystem/v1`. The local operator, repository, toolchain, and host
are trusted. Hostile local process inspection, malicious Git configuration, adversarial filesystem
races, and system compromise are out of scope. Ordinary malformed, missing, moved, substituted, or
inconsistent inputs remain in scope and must fail closed.

In scope:

- bind execution to reviewed repository and observer source;
- locate exactly one current Atlas workspace beneath the canonical private Atlas root;
- load the current discovery request, baseline manifest, current inventory, and conditional
  discovered-inventory backup through released Atlas readers;
- record SHA-256 and byte length for every successfully returned loaded document;
- apply the released request, manifest, approval, digest, inventory-transition, alias,
  manifest-row, and next-ordinal checks used at the A2R8 baseline boundary;
- write one create-new private observation report under protected session state;
- emit only a fixed process signal; and
- allow a failed host or report-write attempt to be repeated with a fresh report path.

Out of scope:

- reading or executing the A2R8 wrapper;
- proving prior-request identity or interpreting the consumed A2R8 result;
- invoking `AtlasDiscovery.DiscoverAsync` or any other state-changing command;
- reading game, save, definition, executable, live-source, copy, or publication content;
- production, test, schema, package, project, or CLI changes;
- modifying, moving, deleting, repairing, or replacing any existing private input;
- defending against a hostile local actor or adversarial filesystem;
- runtime Git inspection, process containment, invocation journaling, or one-shot authority;
- publishing private paths, names, values, hashes, counts, content, or the local result; and
- requiring future public work to be indistinguishable across all possible local results.

## 3. Observer contract

### 3.1 Inputs and workspace selection

The observer receives:

1. the public repository root; and
2. a 32-character lowercase hexadecimal run identifier.

The observer derives the protected session `files` root from its exact reviewed Release build
location. It requires the expected `a2r10-current-baseline-observer/bin/Release/<target-framework>/`
chain, validates the derived `files` root through
`AtlasDiscovery.ValidateExistingOrdinaryDirectory`, validates the actual observer project directory
as an ordinary non-reparse report parent, and requires the derived report path to remain directly
beneath that parent. It derives the report name from the fixed prefix, run identifier, and `.json`
suffix and refuses any existing report leaf.

The observer then derives the canonical private Atlas root from the repository root and validates
that complete existing directory through `AtlasDiscovery.ValidateExistingOrdinaryDirectory` before
enumeration. It enumerates only that root's immediate ordinary, non-reparse directories and selects
the single directory containing the canonical `intake/requests/discover.json` relative path. Zero
or multiple candidates produce outcome `refused` at terminal stage `workspace-selection`. No
candidate name or path enters process output.

The selected request is loaded exactly once through
`AtlasIntakeContracts.ReadDiscoveryRequestAsync`. The observer then constructs
`AtlasWorkspaceLayout` from the loaded request and requires the selected request, project root,
workspace root, survey alias, baseline manifest, inventory, and conditional backup to equal their
released canonical paths. Before their readers run, the observer validates the baseline manifest
and current inventory through `AtlasDiscovery.ValidateExistingOrdinaryFile`. It applies the same
check to the conditional backup when that path exists.

### 3.2 Per-file observation

The observer performs this sequence:

1. load and validate the discovery request;
2. construct and validate its canonical workspace layout;
3. load and validate the baseline manifest;
4. require approved confirmation status, the request-bound manifest SHA-256, and baseline manifest
   revision;
5. load the current inventory through `AtlasIntakeContracts.ReadInventoryAsync`;
6. apply the exact released inventory-transition predicates: when current SHA-256 matches, require
   backup absence and use current as prior; otherwise require and load the backup, require its
   SHA-256 to match, and use backup as prior;
7. construct the resulting `PhaseInventoryContext`;
8. resolve discovery aliases;
9. locate the baseline manifest inventory row; and
10. derive the next discovery destination artifact ordinal.

For every `AtlasLoadedDocument` successfully returned before or during the terminal stage, the
report records only a fixed role, the byte-array length, and the SHA-256 already computed over that
same byte array. If the current inventory is also the prior inventory, it appears once. If
transition logic loads the backup, the current inventory and backup appear as separate roles. A
reader that consumes bytes and then refuses parsing or validation returns no document, so the
report records only its terminal stage and fingerprints from other successful reader returns. The
observer does not call or decompose `LoadPhaseInventoryAsync`; it reproduces only the predicates
listed above over documents returned by the released strict readers.

The observer does not hold files after their reader returns and does not add cross-file atomicity
machinery. This matches the accepted profile: each fingerprint identifies the exact bytes used for
that file's validation, while later mutation remains an accepted local risk.

### 3.3 Private report and process signal

The observer builds the complete report in memory and writes it with create-new semantics only to
the derived protected report path. This report is the sole private write authorized by A2R10. The
report contains:

- schema `atlas-a2-current-baseline-observation/v1`;
- outcome `valid` or `refused`;
- terminal stage `workspace-selection`, `request`, `layout`, `baseline-manifest`,
  `inventory-transition`, `discovery-aliases`, `manifest-row`, `next-ordinal`, or `complete`; and
- the fixed-role fingerprints observed before or during that stage.

It contains no document content or literal input path. The report is private evidence and never
enters Git, a subagent prompt, or process output.

The only standard-output values are:

```text
observation-recorded
observation-not-recorded
```

Each is followed by `\n`; standard error is empty. `observation-recorded` means only that a
well-formed create-new report was flushed. It does not reveal whether current baseline validation
succeeded. Every exception after managed entry is caught without inspecting or emitting its
message, stack, path, or inner exception.

The observer starts no process and writes no file other than its derived report. Synthetic
self-tests may create and remove only their own resolved temporary directory.

## 4. Candidates and execution

Plan candidate `P10` is the direct child of the base commit and may change only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a2-baseline-authority-diagnosis.md
    atlas-v0-a2-current-baseline-observation.md
    atlas-v0-a2-intake-safety-plan.md
```

Plan-review record `R10` is the direct child of final `P10` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-current-baseline-observation-plan-review.md
```

Only after verified shared `R10`, create:

```text
<session-state>/files/a2r10-current-baseline-observer/
  ObserveAtlasA2CurrentBaseline.csproj
  Program.cs
  bin/
  obj/
  a2r10-current-baseline-observation-<run-id>.json
```

The C# project references the released Atlas project and uses the existing test friend-assembly
name only to call the released internal readers and baseline helpers without changing production
visibility. It supports a synthetic `--self-test` mode and a private observation mode.

Before the private mode:

1. verify `HEAD` and upstream equal `R10`;
2. verify tracked, staged, and untracked repository state is clean;
3. verify Atlas source and project inputs are unchanged from A2R8 `G`;
4. build the Atlas tests project and the observer through `mise exec -- dotnet`;
5. retain exact source, Atlas assembly, and observer assembly hashes in protected session state;
6. run synthetic self-tests; and
7. obtain independent `No findings` for the exact observer source.

These are procedural provenance gates. The observer performs no runtime Git or tool inspection.

Completion candidate `G10` is the direct child of `R10` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-current-baseline-observation-completion.md
```

The completion record contains repository-safe provenance, source hashes, synthetic validation,
privacy attestation, and whether a final report was recorded. It omits the report hash, outcome,
terminal stage, fingerprints, private values, and subsequent route.

## 5. Acceptance criteria

A2R10 may enter private observation only when:

1. final `P10` and record-only `R10` are reviewed, committed, pushed, and verified;
2. the repository and A2R8 source bindings pass;
3. the exact observer source builds with the repository SDK;
4. static review finds no wrapper access, discovery call, live-source access, child process, input
   write, or dynamic output; private mode writes only its derived report, while self-test mode
   writes only inside its owned resolved synthetic temporary directory;
5. synthetic tests cover protected-report containment, create-new behavior, and zero, one, and
   multiple workspace candidates;
6. synthetic tests prove exact fingerprints for successfully returned request, manifest,
   current-inventory, and backup documents, including same-stage post-read refusal and partial
   fingerprints on later refusal;
7. synthetic tests cover each fixed terminal stage and both process signals;
8. synthetic tests prove report create-new behavior and empty standard error;
9. all observer findings receive `TP` or `FP` adjudication under the project instructions;
10. every `TP` is resolved and every `FP` receives independent concurrence; and
11. a fresh independent reviewer returns `No findings` for the complete exact observer source.

A2R10 closes when:

1. one final create-new private observation report is recorded;
2. the report parses against its closed schema;
3. no existing private input changed;
4. no discovery, repair, confirmation, copy, cleanup, or other private operation ran;
5. the completion record reveals none of the report's private evidence or result;
6. the exact completion record receives independent `No findings`; and
7. record-only `G10` is pushed and verified as the clean shared tip.

If the host or report write fails before a final report exists, retry with a fresh run identifier is
allowed because the observer is read-only and no consumable execution authority exists. A recorded
refusal outcome still completes A2R10; it selects a separately planned correction without
authorizing that correction.

## 6. Privacy, stop, and resume

The local report may guide the project leader and operating session. A later repository plan may
record only a privacy-reviewed safe conclusion needed to justify its scope. It must not publish the
report, fingerprints, literal paths, values, content, or reconstructable private evidence.

Stop before private observation if:

- the plan, source, or candidate review has an unresolved finding;
- repository, source, build, or clean-state provenance fails;
- workspace selection would inspect outside the canonical private Atlas root;
- the observer would read live source or invoke a state-changing command;
- an existing private input or report would be overwritten;
- process output could contain dynamic or private data; or
- the implementation requires any mechanism excluded by section 2.

To resume:

1. review and persist final `P10`, then record-only `R10`;
2. create the two session source files;
3. build and run only synthetic self-tests;
4. independently review the exact observer source until `No findings`;
5. run one final private observation to a new protected report;
6. inspect the report only inside the operating session;
7. create and independently review the repository-safe completion record; and
8. commit and verify record-only `G10`.
