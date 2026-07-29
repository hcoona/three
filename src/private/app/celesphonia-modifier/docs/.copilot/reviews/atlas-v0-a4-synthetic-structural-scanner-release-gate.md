# Atlas V0 A4 Synthetic Structural Scanner Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G4R1`

**Increment:** A4R1 - Synthetic Structural Scanner

**Outcome:** Released only after verified shared `G4R1`

**Final independent result:** `No findings`

**Governing P4R1:** `fdd362a657bc589524126cef23a688a7089ba21b`

**Activation R4R1:** `6f7c2a70acfe1334ffc994c61b38131f553130b6`

**Initial C4R1:** `84ae31c2e4881b39d319a8dc099392c14576bf63`

**Validation correction:** `58a09c803986444e6f6d49b94af9e6b55c219bd7`

**Schema-evidence correction:** `68e8037d6678bfee0250c0003603dffcfde8574f`

**Byte-copy correction:** `89a16b63f5426089d17ad50c28128705a7c6fe67`

**Completeness-evidence correction:** `d10dd8a953f7e94f71e06816e51801cb28df2555`

**Final candidate tree:** `4f3420bbaeaad4131c49b0990d59d99b196d57a0`

**Governing plan:**
`../plans/atlas-v0-a4-synthetic-structural-scanner.md`

**Plan-review record:**
`atlas-v0-a4-synthetic-structural-scanner-plan-review.md`

**Planned staged-record reviewer:** `a4r1-release-record-reviewer`

## 1. Exact released candidate

The final candidate is the exact additive range `R4R1..d10dd8a9`. Its no-renames path set is:

```text
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanContracts.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanJson.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanValidator.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanner.cs
A src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/
  atlas-structural-scan.schema.json
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasStructuralScanJsonTests.cs
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasStructuralScannerTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

Every implementation and correction commit was pushed before the next independent review. The final
candidate and `origin/dev/shuaizhang/celesphonia-modifier` matched before this record was authored.

## 2. Released capability

The candidate:

- accepts one valid in-memory A3 result and one closed document role;
- performs deterministic iterative two-pass containment traversal;
- treats references as leaf occurrences and resolves targets without traversing target edges;
- emits one detached immutable redacted observation per materialized graph occurrence;
- identifies object members only by occurrence ordinal and arrays only by element index;
- retains no scalar values, ordinary names or equality, opaque class strings, raw identities, source
  bytes or objects, paths, hashes, aliases, timestamps, or content-derived labels;
- validates locator uniqueness, root and parent closure, preorder, child counts, identity ownership,
  reference targets, scanner census, and exact A3 token/graph reconciliation;
- provides the strict bounded canonical `atlas-structural-scan/v1` in-memory contract and one
  Draft 2020-12 schema;
- parses only exact canonical bytes and validates them against the transient expected A3 source;
- bounds observations, depth, retained locator segments, canonical bytes, and token work;
- observes cancellation through traversal, reference resolution, validation/reconciliation,
  serialization, parsing, and canonical-byte copying; and
- adds no CLI, filesystem, snapshot integration, persistence, private-corpus operation,
  multi-input aggregation, semantics, editing, or writes.

`LocatorSegmentRedactor` remains unchanged and is not reused by A4R1.

## 3. Review iterations and dispositions

Every reviewer was independent of implementation authorship and used a general-purpose GPT-5.6
agent. Review used tracked repository content and repository-safe synthetic inputs only.

| Candidate                          | Reviewer                           | Result        | Adjudication |
| ---------------------------------- | ---------------------------------- | ------------- | ------------ |
| Initial C4R1                       | `a4-implementation-reviewer`       | 2 medium      | 2 TP, 0 FP   |
| First corrected candidate          | `a4-implementation-rereviewer`     | 1 P2          | 1 TP, 0 FP   |
| Second corrected candidate         | `a4-implementation-final-reviewer` | 1 P2          | 1 TP, 0 FP   |
| Third corrected candidate          | `a4-release-reviewer`              | 2 medium      | 2 TP, 0 FP   |
| Complete final corrected candidate | `a4-final-gate-reviewer`           | `No findings` | Not needed   |

The accepted corrections:

1. reject class-marker presence on plain objects in both validator and schema;
2. replace uncancellable whole-input UTF-8 validation with bounded cancellable canonical lexical
   validation and closed token limits;
3. execute the schema against canonical all-variant output and required negative mutations with a
   compact independent test evaluator;
4. remove duplicate canonical-byte copies, transfer owned exact arrays internally, and make stream
   finalization and defensive output copies bounded and cancellable; and
5. add distinct source-bound omission, identity, reference, and ordinal mutations plus isolated
   validation/reconciliation, serialization, and reference-dominated cancellation evidence.

The complete final candidate then received exact `No findings`.

## 4. Validation evidence

The exact final candidate passed:

- `mise exec -- dotnet build dirs.proj -c Release --no-restore` with zero warnings and errors;
- the authoritative direct Microsoft.Testing.Platform Atlas test executable with 482 passed,
  zero failed, zero skipped, and zero not run;
- `mise exec -- dotnet format` verification for the Atlas test project;
- changed-range HK checks for typos, EditorConfig, and JSON Biome;
- `git diff --check`;
- repository commit hooks; and
- commitlint for every candidate and correction commit.

The final exact candidate was independently reviewed only after all corrections and validation were
committed and pushed.

No historical JavaScript helper, real snapshot, original save, definition, game installation,
ignored private workspace, decoded private data, persistence operation, or write operation was used.

## 5. Proportional release boundary

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A4R1 addresses scanner completeness, correctness, privacy-safe representation,
determinism, malformed contract handling, practical bounds, cancellation, regression, and
maintainability.

It does not add or require authorization ceremony, runtime Git or binary attestation, document SHA
graphs, r1/r2 state, persistent protocol state, or A5 private-corpus governance. These gates record
provenance only and are never runtime authorization.

## 6. G4R1 release gate

This exact staged record must:

1. receive independent `No findings`;
2. be committed unchanged as `G4R1`, the direct child of exact
   `d10dd8a953f7e94f71e06816e51801cb28df2555`;
3. be the only path added by `d10dd8a9..G4R1`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G4R1` releases only the synthetic single-document structural scanner. It grants no
CLI scan, filesystem scan, real save access, snapshot integration, private corpus, persisted
observation set, multi-input aggregation, semantic interpretation, editing, or write authority.
A5 and every private or real operation remain blocked pending a separately reviewed plan and
explicit user authority.
