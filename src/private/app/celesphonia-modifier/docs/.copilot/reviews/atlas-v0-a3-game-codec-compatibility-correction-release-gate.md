# Atlas V0 A3 Game Codec Compatibility Correction Release Gate

**Lifecycle:** Proposed release evidence before verified shared `G3R2`

**Increment:** A3R2 - Game-Compatible LZ-String Base64 Correction

**Outcome:** Released only after verified shared `G3R2`

**Final independent result:** `No findings`

**Original G3R1:** `376b6f8ccd1c578f6899c9f0fb94574b6eb479f0`

**Current base G5R1:** `5e61a4a43109abdc422c037ceff08bd18c22fe7b`

**Correction C3R2:** `4baead672cd88e78d74c29db1de0b614acf967c6`

**Correction tree:** `dc8957861e645784ede879847bb868785fe9f698`

**Governing plan:**
`../plans/atlas-v0-a3-proportional-save-reader-foundation.md`

**Governing plan blob:** `7d987f0f27d21569e0ffb52dc20c4f4683d43b7d`

**Governing plan SHA-256:**
`dccc5ed1f2380f7407f7e17ae5f5d5dadc45d001575d4328f7920499f353571e`

**Original release record:**
`atlas-v0-a3-proportional-save-reader-foundation-release-gate.md`

**Original release-record blob:** `c4c6b8c55d09b63645290210e6296c7784ab967f`

**Original release-record SHA-256:**
`1ff01cf9d08ed5231d9a96aeee4a7ad1bb47e092d9b6d195c71b79008f870b14`

**Planned final staged-record reviewer:** `a3r2-release-record-final-reviewer`

## 1. Correction scope

The original A3 plan requires the exact canonical `compressToBase64` profile used by the supported
RPG Maker MV game. A private A5R2 execution against one newly finalized read-only A3 snapshot exposed
that the released compressor implemented a different LZ-String Base64 packing profile.

The exact correction range `G5R1..C3R2` changes only:

```text
M src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasLzStringCodec.cs
M tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasSaveReaderTests.cs
```

The final blobs are:

```text
AtlasLzStringCodec.cs
  7c14fb303aaa6be215f87c950345dd9797d496d1
AtlasSaveReaderTests.cs
  3479c0abb463d1506bed180f4dfde88999f141ee
```

`C3R2` is the direct child of exact `G5R1`, was pushed, and matched
`origin/dev/shuaizhang/celesphonia-modifier` before this record was authored.

## 2. Root cause and correction

The supported game's bundled LZ-String implementation:

1. emits the LZ bitstream as 16-bit words;
2. writes each word as two big-endian bytes; and
3. applies standard Base64 to those bytes.

The released C# compressor instead emitted the LZ bitstream directly as six-bit Base64 alphabet
characters. Small synthetic values overlapped sufficiently to hide the profile mismatch, while
realistic canonical saves decoded and then failed exact canonical recompression.

The correction:

- replaces only the compressor's six-bit character writer with a bounded 16-bit word writer followed
  by standard Base64;
- preserves the existing strict alphabet, padding, end-code, truncation, and exact-recompression
  validation;
- preserves encoded and decompressed limits, cancellation, allocation bounds, Unicode and unpaired
  surrogate behavior, and exact original-byte retention for semantic no-op; and
- replaces the incorrect exact vectors with game-compatible synthetic vectors, including dictionary
  growth, padded, and unpadded forms, while retaining rejection of the old noncanonical profile.

No snapshot, reader, JsonEx, scanner, survey, CLI, schema, recovery, or original-data behavior was
otherwise changed.

## 3. Evidence and review

The private diagnosis established only these repository-safe conclusions:

- every document in the finalized snapshot was valid JSON after decoding with the preserved
  game-compatible implementation;
- re-encoding each decoded document with that implementation reproduced its original copied bytes;
- the released C# reader accepted only a subset before the correction; and
- the corrected C# reader accepts every finalized copied document.

No private path, payload, decoded value, hash, filename set, structural count, or scan is published by
this record. The finalized A3 snapshot was not modified. The refused A5 attempt published no survey
manifest or accepted partial corpus.

The defect is one true positive against the existing A3 compatibility claim. It required no plan,
scope, threat-model, authority, protocol, or recovery change.

Independent general-purpose GPT-5.6 reviews were:

| Candidate                       | Reviewer                   | Result        |
| ------------------------------- | -------------------------- | ------------- |
| Initial two-file correction     | `a3-codec-compat-reviewer` | `No findings` |
| Final exact two-file correction | `a3-codec-final-reviewer`  | `No findings` |

The final review covered bit ordering, word completion, Base64 padding, Unicode and surrogate
behavior, canonical rejection, malformed input, limits, allocation, cancellation, semantic no-op,
synthetic evidence, and A4/A5 regression risk.

The staged release record itself required two corrections before final review:

| Iteration | Reviewer                         | Result                   | Disposition |
| --------: | -------------------------------- | ------------------------ | ----------- |
|         1 | `a3r2-release-record-reviewer`   | Release topology finding | 1 TP        |
|         2 | `a3r2-release-record-rereviewer` | Review-history omission  | 1 TP        |

The first TP removed the README index change so `G3R2` changes only this gate record. The second TP
added both staged-record review iterations and their dispositions to this exact candidate. A fresh
final staged-record reviewer must return `No findings` before commit; that result cannot be inserted
after review without changing and invalidating the reviewed bytes.

## 4. Validation

The exact correction passed:

- `mise exec -- dotnet build dirs.proj -c Release --no-restore -m` with zero warnings and zero
  errors;
- the authoritative direct Microsoft.Testing.Platform Atlas executable with 534 passed, zero failed,
  and zero skipped tests;
- `mise exec -- dotnet format --verify-no-changes --no-restore` for the Atlas library and test
  projects;
- changed-file HK checks for EditorConfig and typos;
- `git diff --check`;
- repository commit hooks and commitlint; and
- validation-only reading of the finalized copied snapshot, without printing private content.

One timing-sensitive cancellation test failed once during an intermediate run, then passed three
targeted reruns and both final full-suite executions. No product change was made for that transient
test-runner timing event.

## 5. Proportional boundary

A3R2 restores the already-approved game codec compatibility claim. It adds no authorization
ceremony, runtime attestation, document hash graph, state machine, inventory, malicious-owner
defense, semantic interpretation, editing, encoding workflow, backup, restore, transaction, or
original-save write.

The private A5R2 survey remains incomplete until this correction is released and the released survey
command successfully validates and promotes the existing finalized snapshot survey.

## 6. G3R2 release gate

The exact staged correction record must:

1. receive independent `No findings`;
2. be committed unchanged as `G3R2`, the direct child of exact
   `4baead672cd88e78d74c29db1de0b614acf967c6`;
3. be the only path changed by `C3R2..G3R2`;
4. retain the independently reviewed staged bytes; and
5. be pushed and verified as the shared development-branch tip.

Verified shared `G3R2` releases only the game-compatible codec correction. It does not claim that the
private A5R2 survey completed and grants no new private-data, editing, or write authority.
