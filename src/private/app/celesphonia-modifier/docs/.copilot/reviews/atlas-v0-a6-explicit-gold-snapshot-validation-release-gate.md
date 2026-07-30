# Atlas v0 A6 Explicit Gold Snapshot Validation Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G6R2`

**Increment:** A6R2 - Explicit Gold Snapshot Validation

**Outcome:** Released only after verified shared `G6R2`

**Final independent implementation result:** `No findings`

**Governing P6R2:** `e31720176a04af479c8cd10a1b23bd69a902cacc`

**Activation R6R2:** `d3703c83cd9b9dac7a5774e5de573fb343914914`

**Initial C6R2:** `773157fa86376c9d0dfe9913421eb3defbb6aba3`

**First correction:** `a7eead658690ff80cd459aa085999ad253a78da3`

**Final correction and candidate:** `c328f098c1d610da5350a1a7a624be5c977d2c7b`

**Final candidate tree:** `b0e55b1bf35fc549654bff64baae8762ef098ee0`

**Governing plan:**
`../plans/atlas-v0-a6-explicit-gold-snapshot-validation.md`

**Governing plan blob:** `1dad90160e7070309ec602107b05095b17ebc035`

**Governing plan SHA-256:**
`63c8447b7416c4fb551d0de8262f5d2f67d44a9b7f6116f5e823fbac27d37ff5`

**Plan-review record:**
`atlas-v0-a6-explicit-gold-snapshot-validation-plan-review.md`

**Plan-review record blob:** `e85f1ae4b9e727fa36b239164af136b06f0c7bb9`

**Plan-review record SHA-256:**
`0eaa98838912650d33f886c3f3950d6e548dfd3af05050482f54246d0b786f47`

**Planned staged-record reviewer:** `a6r2-release-record-reviewer`

## 1. Exact released candidate

The final candidate is the exact cumulative no-renames range
`d3703c83cd9b9dac7a5774e5de573fb343914914..c328f098c1d610da5350a1a7a624be5c977d2c7b`.
The initial implementation and both accepted corrections were committed and pushed before the final
independent review. The final candidate matched origin before this record was authored.

Its exact cumulative no-renames path set is the nine paths required by the governing plan:

```text
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliApplication.cs
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Cli/AtlasCliOperations.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldSnapshotValidation.cs
A src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldSnapshotValidationContracts.cs
A src/private/app/celesphonia-modifier/docs/.copilot/schemas/atlas-v0/
  atlas-gold-snapshot-validation-request.schema.json
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasCliApplicationTests.cs
A tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldSnapshotValidationTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasProcessSmokeTests.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

## 2. Released capability

The candidate:

- accepts only the strict three-property
  `atlas-gold-snapshot-validation-request/v1` request and the exact explicitly identified finalized
  A3 receipt;
- validates the finalized snapshot copy and every receipt entry before selected-slot processing;
- traverses receipt entries in receipt order and processes only canonical `file1.rpgsave` through
  `file20.rpgsave`, excluding `global.rpgsave` and `config.rpgsave`;
- reopens each selected slot through the existing I/O seam, applies the A3 encoded limit only to
  that selected-slot read, and refuses a post-validation length or hash change;
- uses the released A3 reader and A6 Gold read model and returns only reconciled aggregate counts for
  `Consistent`, `Disagree`, and `Incomplete`, plus one of the four derived overall states;
- provides deterministic command routing and exact one-line invariant-culture CLI output;
- preserves the actual A3 result used by the runner, including its compressed-byte copy, lossless
  source representation, graph observations, and semantic-no-op bytes; and
- creates, changes, or deletes no file, performs no output write, and persists no state.

The public result and CLI disclose no save values, per-slot data, candidate-state breakdowns,
filenames, paths, hashes, scalar lexemes, decoded content, request content, receipt content, or
exception text.

## 3. Review iterations and dispositions

Every reviewer was independent of implementation authorship and used a general-purpose GPT-5.6
agent. Review used tracked repository content and repository-safe synthetic data only.

| Candidate                    | Reviewer                         | Result           | Adjudication |
| ---------------------------- | -------------------------------- | ---------------- | ------------ |
| Initial C6R2                 | `a6r2-implementation-reviewer`   | 4 findings       | 4 TP, 0 FP   |
| First corrected candidate    | `a6r2-implementation-rereviewer` | 1 Medium finding | 1 TP, 0 FP   |
| Complete corrected candidate | `a6r2-final-rereviewer`          | `No findings`    | Not needed   |

The initial review reported two Medium and two Low findings:

1. the cancellation test did not prove token propagation;
2. the no-write and live-source test bypassed the I/O seam and inventoried only snapshot files;
3. preservation inspected a separate A3 result rather than the result used by the runner; and
4. the public parameter name differed from the required `requestFilePath`.

The first correction asserted cancellation-token identity and meaningful stage entry, denied all
writes and live-source access through the seam, removed the synthetic live source, inventoried the
complete workspace, captured the runner's actual A3 result for preservation checks, and renamed and
asserted `requestFilePath`.

The rereviewer then found that bounded finalized-snapshot validation incorrectly applied the A3
encoded selected-slot limit to excluded `global.rpgsave` and `config.rpgsave` entries. The final
correction made finalized-copy validation unbounded for every receipt entry, retained the A3 encoded
limit only when reopening selected slots, and added a regression proving oversized excluded global
or config entries do not refuse an otherwise valid slot.

The final rereviewer examined the complete exact
`d3703c83cd9b9dac7a5774e5de573fb343914914..c328f098c1d610da5350a1a7a624be5c977d2c7b`
range and all five true-positive dispositions and returned exact `No findings`.

## 4. Validation evidence

The exact final candidate passed:

- the Release build through `dirs.proj` with zero warnings and zero errors;
- targeted `AtlasGoldSnapshotValidationTests` with 35 passed, zero failed, and zero skipped;
- the full direct Atlas test executable with 611 passed, zero failed, and zero skipped;
- `dotnet format --verify-no-changes` for the Atlas library, CLI, and test projects;
- request-schema runtime and mutation tests and JSON Biome validation;
- applicable HK EditorConfig, typo, and JSON checks;
- `git diff --check`; and
- repository commit hooks and commitlint for every candidate commit.

Validation used repository-safe synthetic data only. No private receipt, snapshot, save, A5 output,
definition, installation, ignored artifact, or original user data was accessed.

## 5. Proportional release boundary

The accepted environment trusts the local user, administrator, checkout, runtime, and selected
binaries. A6R2 releases only a read-only runner that validates an explicitly selected finalized
copy, processes receipt-order `file1.rpgsave` through `file20.rpgsave`, and reports aggregate counts
and one of four states through deterministic CLI behavior.

It creates no output file, performs no write, and persists no private state. It exposes no values,
per-slot data, paths, or hashes. It establishes no Gold semantics, gameplay-valid range, candidate
coupling, corruption interpretation, edit selection, encoder, writer, transaction, recovery,
installer, or WinUI authority. These gates record release provenance only and are never runtime
authorization.

## 6. G6R2 runner release gate

This exact staged record must:

1. receive independent `No findings` from `a6r2-release-record-reviewer`;
2. be committed unchanged as `G6R2`, the direct child of exact
   `c328f098c1d610da5350a1a7a624be5c977d2c7b`;
3. be the only path added by
   `c328f098c1d610da5350a1a7a624be5c977d2c7b..G6R2`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G6R2` releases only the read-only runner. Presence of the runner or this record
grants no private execution authority.

`X6R2` still requires verified shared `G6R2` plus separate, explicit user authorization identifying
the exact finalized receipt. The receipt must not be inferred, and no private execution may begin
before both conditions are satisfied.
