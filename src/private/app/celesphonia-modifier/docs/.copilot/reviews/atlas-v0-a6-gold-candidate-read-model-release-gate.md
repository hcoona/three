# Atlas V0 A6 Gold Candidate Read Model Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G6R1`

**Increment:** A6R1 - Gold Candidate Read Model

**Outcome:** Released only after verified shared `G6R1`

**Final independent implementation result:** `No findings`

**Governing P6R1:** `6474e9ad6b9748ba7ce79ce5fcb2b3c298afd937`

**Activation R6R1:** `ec157eca9b9463857747215b18b2b06e3fd65fc8`

**Initial C6R1:** `a324ece2061fc9e559911ed7069cb79453c18903`

**Review correction:** `641aab8b67cbbefa3acacb85fda046bef65a1b8d`

**Final candidate tree:** `1f083138e0ae0f90e087301161e3a613738799b0`

**Governing plan:**
`../plans/atlas-v0-a6-gold-candidate-read-model.md`

**Governing plan blob:** `20cee7500bd33bc45165a31fbb1e240a55d32f4a`

**Governing plan SHA-256:**
`c38e02f83bd5411f9f89e8ff4431ff09311c9c06ab2c884425ed51248467a37c`

**Plan-review record:**
`atlas-v0-a6-gold-candidate-read-model-plan-review.md`

**Plan-review record blob:** `bb4af33b0b95659a1376a26afd4ea1df0ffce1ab`

**Plan-review record SHA-256:**
`0adf354d746a1e43e1f3ea423e39d49864833cc1fe45c7487514a9ed58158676`

**Planned staged-record reviewer:** `a6r1-release-record-reviewer`

## 1. Exact released candidate

The final candidate is the exact additive range `R6R1..641aab8b`. The initial implementation and
its test-evidence correction were committed and pushed before the final independent review. The
final tip matched `origin/dev/shuaizhang/celesphonia-modifier` before this record was authored.

Its exact no-renames path set is:

```text
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldReadModel.cs
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldReadModelTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

## 2. Released capability

The candidate:

- adds one in-memory `AtlasGoldReadModel.Read` entry point over an already-read
  `AtlasSaveReadResult`;
- examines only exact ordinal paths `party._gold` and `variables._data[215]`;
- classifies each candidate as `Present`, `Missing`, `Ambiguous`, `WrongShape`, `NonInteger`, or
  `OutsideInt64`;
- returns a value only for `Present`, with closed immutable construction that callers cannot
  contradict;
- derives `Consistent` only for equal present values, `Disagree` only for unequal present values,
  and `Incomplete` otherwise;
- treats duplicate relevant members as ambiguous while ignoring duplicate unrelated members;
- dereferences resolved JsonEx references only at the fixed path steps;
- accepts only the exact integer lexeme grammar `-?(0|[1-9][0-9]*)` and distinguishes integral
  `Int64` overflow from non-integral decimal or exponent forms;
- checks cancellation at entry, between fixed steps, around dereference, during each member-scan
  iteration, and before numeric parsing; and
- returns no source node, lexeme, mutable collection, path, diagnostic payload, or persistence
  contract.

The implementation does not mutate the A3 graph, lossless JSON, original compressed bytes, or
semantic no-op bytes. It adds no CLI, schema, package, project, filesystem, definition, installation,
WinUI, encoder, writer, or persistent state.

## 3. Review iterations and dispositions

Every reviewer was independent of implementation authorship and used a general-purpose GPT-5.6
agent. Review used tracked repository content and repository-safe synthetic documents only.

| Candidate                    | Reviewer             | Result        | Adjudication |
| ---------------------------- | -------------------- | ------------- | ------------ |
| Initial C6R1                 | `a6-gold-reviewer`   | 1 Low finding | 1 TP, 0 FP   |
| Complete corrected candidate | `a6-gold-rereviewer` | `No findings` | Not needed   |

The accepted finding was that the original cancellation test proved only eventual cancellation: a
post-scan check could still satisfy it if the per-member check were removed. The correction
separately calibrates fixed read overhead and the bounded 950,000-member scan, schedules
cancellation in the early scan window, and requires observation before a post-scan-only
implementation could return.

The corrected test remained repository-safe, used the public A3 reader, added no production seam,
and passed repeatedly. The final reviewer re-examined the complete exact
`ec157eca..641aab8b` range, confirmed that the correction was valid and non-flaky, and returned exact
`No findings`.

## 4. Validation evidence

The exact final candidate passed:

- `mise exec -- dotnet build dirs.proj -c Release --no-restore` with zero warnings and zero errors;
- the targeted direct xUnit v3 Atlas executable with 35 passed, zero failed, and zero skipped tests
  on repeated runs;
- the authoritative full direct Atlas test executable with 569 passed, zero failed, and zero skipped
  tests;
- `mise exec -- dotnet format --verify-no-changes --no-restore` for the Atlas library and test
  projects;
- changed-file HK EditorConfig and typo checks;
- `git diff --check`; and
- repository commit hooks and commitlint for both candidate commits.

Validation used synthetic repository-safe documents only. No real save, private snapshot, A5 survey
output, definition, game installation, ignored private content, semantic inference, editing,
encoding, or original-save write occurred.

## 5. Proportional release boundary

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A6R1 addresses credible accidental fixed-path selection, duplicate handling, JsonEx
reference, shape, integer parsing, cancellation, mutation, value-leakage, and regression defects for
a trusted single-user local save editor.

It does not claim that either candidate means Gold, that equality proves coupling, that disagreement
proves corruption, that any representable value is gameplay-valid, or that either location may be
edited. It adds no malicious-owner defenses, authorization ceremony, runtime Git or binary
attestation, semantic claim ledger, cross-save analysis, general path system, multi-party workflow,
writer, or persistent protocol. These gates record release provenance only and are never runtime
authorization.

## 6. G6R1 release gate

This exact staged record must:

1. receive independent `No findings`;
2. be committed unchanged as `G6R1`, the direct child of exact
   `641aab8b67cbbefa3acacb85fda046bef65a1b8d`;
3. be the only path added by `641aab8b..G6R1`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G6R1` releases only the synthetic read-only Gold candidate model. It grants no
private validation, semantic authority, editing, encoding, transaction, backup, restore, or
original-save write authority. Any private Gold validation requires a separate explicitly approved
plan.
