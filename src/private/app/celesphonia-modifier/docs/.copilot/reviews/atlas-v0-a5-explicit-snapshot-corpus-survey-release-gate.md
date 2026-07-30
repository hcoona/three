# Atlas V0 A5 Explicit Snapshot Corpus Survey Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G5R1`

**Increment:** A5R1 - Explicit Snapshot Corpus Survey Runner

**Outcome:** Released only after verified shared `G5R1`

**Final independent result:** `No findings`

**Governing P5R1:** `b6e4150b9cbde7e3ce23b416a9e23422ed8e8974`

**Activation R5R1:** `5a990fdd4b51d3609d48424891015ab5df3561a0`

**Initial C5R1:** `b2e71c6cdb8212f004e09e8465716126a9d38855`

**Verification correction:** `a7ecd53123bf49c8e2bcec5301219941c7e1eb50`

**Final candidate tree:** `5ca9c36f6e62aa21ed32a8145c927d5f016bb204`

**Governing plan:**
`../plans/atlas-v0-a5-explicit-snapshot-corpus-survey.md`

**Governing plan blob:** `9c1b6caf063cf979b95f7d4ac692e2a6bb08f671`

**Governing plan SHA-256:**
`1f9a59b5dbb4d72dfbd700761c74f18ade4d8752abb69be531c1242eae19ab34`

**Plan-review record:**
`atlas-v0-a5-explicit-snapshot-corpus-survey-plan-review.md`

**Plan-review record blob:** `8e89b2996ed9d2d5a705bf095476e131f1bcd279`

**Plan-review record SHA-256:**
`3cd69b27919423fb096b1e3fe4d0d39481a2e192e0a801cd11e4bbb450381e33`

**Planned staged-record reviewer:** `a5r1-release-record-reviewer`

## 1. Exact released candidate

The final candidate is the exact additive range `R5R1..a7ecd531`. The initial implementation and
its correction were committed and pushed before independent review. The final tip matched
`origin/dev/shuaizhang/celesphonia-modifier` before this record was authored.

Its exact no-renames path set is:

```text
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliApplication.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliOperations.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasFinalizedSaveSnapshot.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasSaveSnapshot.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasSnapshotSurvey.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasSnapshotSurveyContracts.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasSnapshotSurveyManifestJson.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanContracts.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasStructuralScanJson.cs
A src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/
  atlas-snapshot-survey-request.schema.json
A src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/
  atlas-snapshot-survey.schema.json
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasCliApplicationTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasProcessSmokeTests.cs
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasSnapshotSurveyTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

## 2. Released capability

The candidate:

- adds `snapshot-survey <request-path>` while preserving released A3 and A4 behavior;
- accepts exactly one explicit finalized A3 receipt and validates every mandatory copied entry;
- never uses the receipt's recorded live `saveRoot` as an input path;
- derives roles only from canonical supported save names and processes documents in receipt order;
- integrates the released A3 reader and A4 scanner sequentially;
- persists one source-bound canonical A4 scan per copied save and one canonical aggregate manifest;
- records only private byte-integrity facts and redacted structural censuses;
- enforces per-document and aggregate observation, byte, manifest, cancellation, and arithmetic bounds;
- validates persisted scan bytes against both their source binding and exact canonical form before
  manifest creation or promotion;
- uses deterministic fixed private output paths with valid-final reuse, valid-incomplete promotion,
  allowlisted nonrecursive incomplete cleanup, and refusal of ambiguous state;
- provides strict request and survey contracts plus executable Draft 2020-12 schemas; and
- exposes fixed payload-free CLI failure diagnostics.

The internal A4 persistence transfer preserves ordinary public `AtlasStructuralScanResult` defensive
copy behavior. Persistence explicitly detaches ownership once, invalidates that transferred result,
disposes generated ownership before reopen, and compares persisted and expected canonical forms by
stream so the survey does not retain overlapping large canonical arrays.

## 3. Review iterations and dispositions

Every reviewer was independent of implementation authorship and used a general-purpose GPT-5.6
agent. Review used tracked repository content and repository-safe synthetic inputs only.

| Candidate                    | Reviewer                       | Result        | Adjudication |
| ---------------------------- | ------------------------------ | ------------- | ------------ |
| Initial C5R1                 | `a5-implementation-reviewer`   | 4 findings    | 4 TP, 0 FP   |
| Complete corrected candidate | `a5-implementation-rereviewer` | `No findings` | Not needed   |

The accepted corrections:

1. release generated scan ownership before reopening persisted bytes and compare canonical forms
   without a second expected canonical byte array;
2. apply the A5 reader bound before opening a declared-oversized snapshot entry and stop hashing a
   forged-small entry after the configured limit plus one byte;
3. bind every global, config, and `file1` through `file20` schema branch to its exact derived scan
   filename; and
4. inject same-length source-mismatched scan substitution during the required pre-promotion reopen
   and prove refusal before manifest creation or promotion.

The independent reviewer then re-examined the complete exact `R5R1..a7ecd531` candidate, including
all original integration, privacy, recovery, resource, schema, CLI, and regression surfaces, and
returned exact `No findings`.

## 4. Validation evidence

The exact final candidate passed:

- `mise exec -- dotnet build dirs.proj -c Release --no-restore -m` with zero warnings and zero
  errors;
- the authoritative direct Microsoft.Testing.Platform Atlas test executable with 530 passed, zero
  failed, and zero skipped tests;
- `mise exec -- dotnet format --verify-no-changes --no-restore` for the Atlas library, CLI, and test
  projects;
- changed-file `mise exec -- hk check --check --profile small --profile medium --no-progress`,
  including EditorConfig, typos, and JSON Biome;
- `git diff --check`; and
- repository commit hooks and commitlint for both implementation commits.

Validation used synthetic repository-safe data only. No real save, live save root, private snapshot,
definition, game installation, ignored private workspace, original-data write, semantic analysis,
or real survey operation occurred.

## 5. Proportional release boundary

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A5R1 addresses credible accidental original access, invalid snapshot acceptance, corpus
omission, wrong-role scanning, private-output corruption, nondeterminism, privacy leakage, resource
exhaustion, cancellation, recovery, regression, and maintainability defects.

It does not add or require malicious-owner defenses, runtime Git or binary attestation,
authorization ceremony, document SHA graphs, r1/r2 state, inventories, multi-party approval,
semantic interpretation, editing, or a persistent protocol state machine. These gates record
release provenance only and are never runtime authorization.

## 6. G5R1 release gate

This exact staged record must:

1. receive independent `No findings`;
2. be committed unchanged as `G5R1`, the direct child of exact
   `a7ecd53123bf49c8e2bcec5301219941c7e1eb50`;
3. be the only path added by `a7ecd531..G5R1`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G5R1` releases only the explicit finalized-snapshot survey runner. It does not claim
or authorize a real private survey, live-save access, definition access, semantic analysis, editing,
encoding, backup, restore, transaction, or original-save write. A real survey remains a separate
A5R2 execution and requires the user's explicit finalized snapshot receipt path.
