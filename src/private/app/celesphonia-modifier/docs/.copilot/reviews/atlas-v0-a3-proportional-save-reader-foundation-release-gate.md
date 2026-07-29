# Atlas V0 A3 Proportional Save Reader Foundation Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G3R1`

**Increment:** A3R1 - Proportional Save Snapshot and Lossless Reader Foundation

**Outcome:** Release ready after verified shared `G3R1`

**Final implementation result:** `No findings`

**R3R1:** `45666c5e2608a35fb2a7f8d89d4f765a735a3059`

**Initial C3R1:** `97abcc8d0b44ce74c81e4e64124f73c0b05c6618`

**Cancellation correction:** `45d6d87acf142790e0e1ba2c62fc917429c13722`

**Device-path correction:** `037e01401ae51827c2c9da9d11849c7b216960c7`

**Final reviewed implementation tip:** `3c5cd0ff37dcf5b03960cd86a45b47d85f652a9a`

**Final implementation tree:** `b4c8c13b903ee4bdfd86deaf7b506592a3e899e2`

**Governing plan:**
`../plans/atlas-v0-a3-proportional-save-reader-foundation.md`

**Governing plan blob:** `7d987f0f27d21569e0ffb52dc20c4f4683d43b7d`

**Governing plan SHA-256:**
`dccc5ed1f2380f7407f7e17ae5f5d5dadc45d001575d4328f7920499f353571e`

**Plan-review authority:**
`atlas-v0-a3-proportional-save-reader-foundation-plan-review.md`

**Plan-review record blob:** `b2661f0fb7c574981240704ed700a7798eaf1c0c`

**Plan-review record SHA-256:**
`ccf17d321571b7b673244c7fe8b56df01666af603d575815cfd2cfd5ef4e3fa5`

**Final implementation reviewer:** `a3r1-final-committed-review`

**Planned staged-record reviewer:** `a3r1-release-record-reviewer`

## 1. Exact implementation candidate

The initial C3R1 commit is the direct child of exact R3R1. Post-commit review found three
release-blocking local-correctness defects, so they were corrected through transparent additive
commits rather than rewritten history. The final reviewed implementation candidate is the complete
exact range `R3R1..3c5cd0ff`; its tip was pushed and verified as
`origin/dev/shuaizhang/celesphonia-modifier` before this record was authored.

The exact final no-renames path set and Git blobs are:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/
    AtlasCliApplication.cs
      a932097233062736e4cfc42b75c8bd6b8cc8a574
    AtlasCliOperations.cs
      4742e27de898bf113b38515343a24c2bf9742ae4
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDirectoryPath.cs
      7b2a46a54f37b7964e215fb5cb8d19e27fbe56aa
    AtlasDiscovery.cs
      e9775816c07bce8fc8d495a76f36d702a00cf129
    AtlasLzStringCodec.cs
      9dc0521d140e43e2f5344d7fa810944c94c128cd
    AtlasSaveReader.cs
      9111a4dbf6db6a49076d714b7f0900c796ee5daa
    AtlasSaveSnapshot.cs
      41050da434a26fc7d85344197f491003b9aace1a
    AtlasSaveSnapshotContracts.cs
      24ecabff551988897407b09e70142605e7dc8896
  docs/.copilot/schemas/atlas-v0/
    atlas-save-snapshot-receipt.schema.json
      120a3fbb9a12c8023f5040d67f9153b043643e86
    atlas-save-snapshot-request.schema.json
      137df8317ce058ea350c01ac2fba99e469b9cb22
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasCliApplicationTests.cs
      4330a67d1acc9d379cd97a9e9dfc970b384c8fbc
    AtlasIntakeContractTests.cs
      70429239f1862c68c47adb3b2bc943d774068e52
    AtlasProcessSmokeTests.cs
      ae13cb18b5aeaa881e5c8e688654ee5a34079fc3
    AtlasSaveReaderTests.cs
      a32f52b095881dd9fff20dcc81222cdd5d215a5b
    AtlasSaveSnapshotTests.cs
      de12b585e9a70c8667395e1332212882e8461fe6
    ProjectBoundaryTests.cs
      007612f98c851d4b5a043d4994da900131f0d6bb
```

## 2. Released architecture

The corrected A3R1 candidate:

- adds `save-snapshot <request-path>` while preserving every released command and contract;
- selects only supported immediate-child save names and copies originals read-only;
- uses strict bounded request and receipt contracts with deterministic private output paths;
- validates lexical containment and Windows physical containment before cleanup, output creation, or
  incomplete promotion;
- resolves local mapped-drive, short-name, junction, and SUBST aliases through normalized final
  volume paths, fails closed when a network share cannot produce that form, and treats only explicit
  file/path-not-found errors as absence;
- verifies source stability, destination length and SHA-256, complete receipt bindings, and the
  pre/post selected-file set before promotion;
- performs directory-based final/incomplete recovery without recursive cleanup;
- implements an independent canonical RPG Maker MV LZ-String Base64 codec;
- retains exact compressed bytes, ordered duplicate JSON members, scalar lexemes, unknown members,
  and source UTF-8;
- constructs bounded JsonEx identity, class, array, reference, shared-target, and cyclic graphs; and
- reconciles independent token and graph censuses.

New snapshot copies require Windows file-sharing semantics. Valid finalized recovery remains
source-independent. Valid incomplete recovery may perform only the minimum final-path metadata probe
needed to prevent a physically aliased live save directory from being renamed.

## 3. Review iterations and disposition

Every reviewer was independent of implementation authorship and used a general-purpose GPT-5.6
agent. Findings were accepted only for credible local correctness, data-loss, compatibility,
resource-bound, cancellation, or contract defects under the trusted single-user WinUI threat model.

| Iteration | Reviewer                          | Result        | Disposition |
| --------: | --------------------------------- | ------------- | ----------- |
|         1 | `a3r1-candidate-reviewer`         | 5 findings    | 5 TP        |
|         2 | `a3r1-candidate-rereviewer`       | 1 finding     | 1 TP        |
|         3 | `a3r1-rereviewer`                 | 4 findings    | 4 TP        |
|         4 | `a3r1-final-candidate-review`     | 1 finding     | 1 TP        |
|         5 | `a3r1-release-readiness-review`   | `No findings` | Not needed  |
|         6 | `a3r1-committed-candidate-review` | 1 finding     | 1 TP        |
|         7 | `a3r1-corrected-commit-review`    | 1 finding     | 1 TP        |
|         8 | `a3r1-alias-fix-review`           | `No findings` | Not needed  |
|         9 | `a3r1-definitive-commit-review`   | 1 finding     | 1 TP        |
|        10 | `a3r1-identity-fix-review`        | 1 finding     | 1 TP        |
|        11 | `a3r1-identity-fix-rereview`      | 1 finding     | 1 TP        |
|        12 | `a3r1-final-path-review`          | 1 finding     | 1 TP        |
|        13 | `a3r1-tristate-path-review`       | `No findings` | Not needed  |
|        14 | `a3r1-final-committed-review`     | `No findings` | Not needed  |

The corrected true positives were:

1. scalar limits checked after allocation;
2. superlinear compression allocations;
3. noncanonical copied filename casing;
4. reference nodes omitted from graph limits and census;
5. delayed cancellation during decompression preprocessing;
6. unconditional case-insensitive A3 path comparison;
7. Unix file-sharing semantics weaker than the snapshot stability claim;
8. weak graph-node reconciliation;
9. cancellation masked during save-root selection;
10. missing exact Unicode and surrogate compatibility vectors;
11. invalid escaped surrogates escaping classified JSON failures;
12. cancellation arriving immediately before either directory promotion;
13. Windows device namespace path aliases bypassing lexical overlap;
14. mapped-drive, UNC, short-name, junction, or SUBST aliases sharing a physical directory;
15. physical comparison omitting the actual incomplete/final mutation roots;
16. lexical parent walking missing ancestors above an alias target; and
17. inaccessible roots being mistaken for absent roots by `Directory.Exists`.

The final exact committed range then received `No findings`.

## 4. Validation

The final corrected committed candidate passed:

- `mise exec -- dotnet build dirs.proj -c Release --no-restore -m` with zero warnings and zero
  errors;
- the authoritative Microsoft.Testing.Platform executable with 458 passed, zero failed, and zero
  skipped tests;
- `mise exec -- dotnet format` for the Atlas library and test projects;
- changed-file `mise exec -- hk check --check --profile small --profile medium --no-progress`,
  including EditorConfig, typos, and JSON Biome checks where applicable;
- `git diff --check`; and
- commit-time repository hooks and commitlint for every implementation commit.

Validation used only synthetic repository-safe data. No real save root, game installation,
definition, ignored private workspace, original-data write, WinUI operation, telemetry, or real
snapshot operation occurred.

## 5. G3R1 release gate

This exact staged record must:

1. receive an independent `No findings` review;
2. be committed unchanged as G3R1, the direct child of exact final implementation tip
   `3c5cd0ff37dcf5b03960cd86a45b47d85f652a9a`;
3. be the only path added by the final implementation tip through G3R1;
4. retain the independently reviewed staged blob; and
5. be pushed and verified as the shared development-branch tip.

G3R1 records release provenance only. The application never inspects this commit, record, Git state,
source hashes, or binary hashes at runtime. It grants no permission for a real snapshot, private
corpus access, decoded-data persistence, WinUI editing, or original-save write.
