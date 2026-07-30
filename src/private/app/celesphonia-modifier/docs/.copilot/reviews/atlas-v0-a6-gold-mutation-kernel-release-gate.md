# Atlas V0 A6 Gold Mutation Kernel Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G6R3`

**Increment:** A6R3 - Gold Mutation Kernel

**Outcome:** Released only after verified shared `G6R3`

**Final independent implementation result:** `No findings`

**Final governing P6R3:** `fe21d472d4ea14a8c15b92a3bb13dbfe62adf865`

**Activation R6R3:** `468d687480c5974bcf530a058ed2b5821f664635`

**Initial and final C6R3:** `d0035bad1d519d2624f2fe72000ebcc6b746d6ea`

**Final candidate tree:** `7635d450ce7ecc44de5613d9d979adf80617fac2`

**Governing plan:**
`../plans/atlas-v0-a6-gold-mutation-kernel.md`

**Governing plan blob:** `7b60f5619640c0a6310a0c2208c80369d4a3645d`

**Governing plan SHA-256:**
`e0b9d515a256dac3e0c8d1ff2368024383bd8835252fb19117d4812bd4ea944b`

**Plan-review record:**
`atlas-v0-a6-gold-mutation-kernel-plan-review.md`

**Plan-review record blob:** `3a04f16f833057a9e20defc4f8df3d541bf91267`

**Plan-review record SHA-256:**
`ef0e72af8dcc92fcdda2bcb22be7235516babed19fe85a6a0c1f667d9d326086`

**Planned staged-record reviewer:** `a6r3-release-record-reviewer`

## 1. Exact released candidate

The final candidate is the exact no-renames range
`468d687480c5974bcf530a058ed2b5821f664635..d0035bad1d519d2624f2fe72000ebcc6b746d6ea`.
The implementation was committed and pushed before independent review. It matched the shared
development-branch tip before this record was authored.

Its exact four-path set is:

```text
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldMutationKernel.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldReadModel.cs
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldMutationKernelTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

The candidate blobs and SHA-256 values are:

| Path                                                                                                            | Git blob                                   | SHA-256                                                            |
| --------------------------------------------------------------------------------------------------------------- | ------------------------------------------ | ------------------------------------------------------------------ |
| `src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas/AtlasGoldReadModel.cs`                   | `cefc88d578708a7f5829b75e2c4429c7b2cc8a34` | `33422d277eaf25a98294c2b1ab568e117d8ce9ad22f4643111e069e8833747d3` |
| `src/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas/AtlasGoldMutationKernel.cs`              | `223af166bddec133ea248c7c4f6ee90461b0d3b2` | `daa62d04ec5800f9a9d19d08dffcb599bce275f7f53c4091083f3056fa02b756` |
| `tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldMutationKernelTests.cs` | `49ec779e4ba32c2fb88f89941bf8b452f5568678` | `ec2b4dbd6f3769d91b088ca5d5fe636f695a60fa2e5092747c01aa113393ba57` |
| `tests/private/app/celesphonia-modifier/Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs`         | `a4ce90d18db6fe9dbd6663beca2be09f5ed4a2d2` | `767074b6f5d6eb19eb4e10f3b3a55e488cd9d1df796df0047b027ee8fe0179ef` |

## 2. Released capability

The candidate:

- adds one pure in-memory `AtlasGoldMutationKernel.CreateCandidate` entry point over an already-read
  `AtlasSaveReadResult`;
- reuses one transient fixed A6 inspection for `party._gold` and
  `variables._data[215]`, preserving the released public read-model behavior;
- refuses incomplete or disagreeing sources before mutation;
- returns the exact original compressed bytes for a semantic no-op, including `-0` requested as
  `0`, without span normalization, encoding, or re-read;
- validates each changed scalar span against the exact current integer lexeme, deduplicates equal
  spans, and refuses overlap only after both distinct spans are individually valid;
- replaces only the validated scalar UTF-8 spans with invariant decimal `Int64` ASCII while
  preserving every unrelated UTF-8 byte;
- strictly decodes the constructed UTF-8, applies the supplied reader limits, compresses with the
  released game codec, re-reads with the same limits, and verifies exact lossless bytes plus A6
  consistency;
- propagates cancellation and returns immutable owned compressed bytes through defensive copies;
  and
- exposes only closed dispositions, fixed value-free failures, and the compressed candidate.

The implementation adds no filesystem, path, stream, CLI, schema, logging, private execution,
gameplay range, configurable path, general patcher, writer, backup, transaction, recovery,
persistence, or WinUI surface.

## 3. Independent review

The implementation reviewer, `a6r3-implementation-reviewer`, was a fresh independent
general-purpose GPT-5.6 agent and did not author the candidate. It reviewed the complete exact
four-path range against the governing plan, released A3/A6 and codec contracts, public API,
classification, span mechanics, byte preservation, limits, verification, cancellation, ownership,
tests, regressions, and no-I/O boundary.

It returned exact `No findings`. There were zero implementation findings and therefore no
implementation correction commits or dispositions.

Review used tracked repository content and repository-safe synthetic data only. No private receipt,
snapshot, save, survey output, definition, installation, ignored artifact, candidate value, or
original user data was accessed.

## 4. Validation evidence

The exact final candidate passed:

- targeted direct xUnit v3
  `Hcoona.CelesphoniaModifier.Atlas.Tests.AtlasGoldMutationKernelTests` with 94 passed, zero errors,
  zero failed, and zero skipped;
- the authoritative full direct Atlas test executable with 706 passed, zero errors, zero failed,
  and zero skipped;
- `mise exec -- dotnet build dirs.proj --configuration Release --no-restore --verbosity quiet`
  with zero warnings and zero errors;
- `dotnet format --verify-no-changes --no-restore` for the Atlas library and test projects;
- changed-file HK EditorConfig and typo checks;
- `git diff --check`;
- exact cumulative four-path inspection; and
- repository commit hooks and commitlint for C6R3.

Validation used repository-safe synthetic data only. No private input, filesystem mutation API, or
game-save mutation occurred.

## 5. Proportional release boundary

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A6R3 addresses credible accidental fixed-path selection, source disagreement, invalid
span, overlap, encoding, limit, verification, cancellation, ownership, and regression defects for a
trusted single-user local save editor.

It does not establish that either location means Gold, that equality proves gameplay coupling, that
any representable value is gameplay-valid, or that a candidate should be applied. It adds no
malicious-owner defense, runtime Git or binary attestation, private execution, filesystem write,
operation authority, transaction, backup, recovery, installer, or WinUI. These gates record release
provenance only and are never runtime authorization.

## 6. G6R3 kernel release gate

This exact staged record must:

1. receive independent `No findings` from `a6r3-release-record-reviewer`;
2. be committed unchanged as `G6R3`, the direct child of exact
   `d0035bad1d519d2624f2fe72000ebcc6b746d6ea`;
3. be the only path added by `d0035bad1d519d2624f2fe72000ebcc6b746d6ea..G6R3`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G6R3` releases only pure in-memory candidate generation. It grants no filesystem
write, private execution, semantic, gameplay-range, operation, transaction, recovery, persistence,
or application authority. Any file read or write requires a separate approved governing plan.
