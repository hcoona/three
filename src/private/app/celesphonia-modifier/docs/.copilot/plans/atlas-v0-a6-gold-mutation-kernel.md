# Atlas v0 A6R3 Synthetic Gold Mutation Kernel

**Lifecycle:** Conditional: proposed governing plan before verified shared `R6R3`; active normative
after verified shared `R6R3`

**Increment:** A6R3 - Synthetic Gold Mutation Kernel

**Audience:** Implementers, independent reviewers, and the project leader

**Decision owner:** Project leader

**Base:** Verified shared `X6R2`
`85db738d466dbaa918683bb8e4c56775c5e7544f`

**Purpose:** Add a pure in-memory transform that replaces the released A6 fixed two-path Gold
candidate in an already-read A3 save and returns only game-compatible re-encoded candidate bytes.

> **No authority by presence**
> This file grants no implementation authority until its exact persisted gates pass. Verified
> `X6R2` is sequencing evidence only: it establishes no Gold semantics, gameplay range, coupling,
> write set, edit authority, filesystem-write authority, or operation authority.

## 1. Boundary and proportional posture

Celesphonia Modifier is trusted, single-user local software. A6R3 addresses ordinary accidental
defects in fixed-path location, source-span handling, byte replacement, encoding, limits,
verification, cancellation, immutability, and failure classification. It does not defend against
malicious replacement by the trusted owner or administrator.

The kernel uses only:

- one caller-supplied `AtlasSaveReadResult`;
- the released A3 lossless UTF-8 source and `AtlasLzStringCodec`;
- the released A6 fixed locators `party._gold` and `variables._data[215]`;
- one caller-supplied `Int64`; and
- the exact `AtlasSaveReaderLimits` profile used to read the source.

It performs no filesystem, stream, path, CLI, schema, private-data, installation, snapshot, survey,
definition, WinUI, persistence, transaction, backup, recovery, or operation work. Historical
semantic-platform and high-assurance ceremony remain rejected.

## 2. Public contract

C6R3 adds this closed public surface:

```csharp
public enum AtlasGoldMutationDisposition
{
    Unchanged,
    Changed,
}

public enum AtlasGoldMutationFailure
{
    SourceIncomplete,
    SourceDisagrees,
    InvalidSourceSpan,
    OverlappingSourceSpans,
    CandidateLimitExceeded,
    CandidateVerificationFailed,
    UnsupportedInternalState,
}

public sealed class AtlasGoldMutationException : Exception
{
    public AtlasGoldMutationFailure Failure { get; }
}

public sealed class AtlasGoldMutationResult
{
    public AtlasGoldMutationDisposition Disposition { get; }

    public byte[] GetCompressedBytes(
        CancellationToken cancellationToken = default);
}

public static class AtlasGoldMutationKernel
{
    public static AtlasGoldMutationResult CreateCandidate(
        AtlasSaveReadResult source,
        long value,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken = default);
}
```

`AtlasGoldMutationException` and `AtlasGoldMutationResult` have no public constructors. The result is
immutable, owns its compressed bytes, and returns a fresh cancellable copy on every getter call. The
kernel has no nullable or default limits: `AtlasSaveReadResult` does not retain the profile used to
read it, so supplying that same profile is an explicit caller precondition. Null arguments and
invalid limit objects use the established fixed, value-free argument exceptions; non-cancellation
operational refusals use only `AtlasGoldMutationException`.

At method entry, require non-null `source` and `limits`, then call the existing `limits.Validate()`
before cancellation, inspection, no-op selection, or candidate work. Invalid reader profiles
therefore retain the reader's `ArgumentOutOfRangeException` contract and never become mutation
failures.

The exception messages are fixed:

| Failure                       | Message                                                           |
| ----------------------------- | ----------------------------------------------------------------- |
| `SourceIncomplete`            | `The Gold source is incomplete.`                                  |
| `SourceDisagrees`             | `The Gold source candidates disagree.`                            |
| `InvalidSourceSpan`           | `A Gold source span is invalid.`                                  |
| `OverlappingSourceSpans`      | `Gold source spans overlap.`                                      |
| `CandidateLimitExceeded`      | `The Gold candidate exceeds the configured limits.`               |
| `CandidateVerificationFailed` | `The Gold candidate could not be verified.`                       |
| `UnsupportedInternalState`    | `The Gold mutation kernel reached an unsupported internal state.` |

No exception, diagnostic, or result metadata contains a value, lexeme, source fragment, path, hash,
or compressed content. The result's sole byte-bearing output is the defensive candidate copy from
`GetCompressedBytes`.

## 3. One fixed A6 inspection

Refactor `AtlasGoldReadModel` internally so one transient fixed inspection supplies:

- the unchanged public `AtlasGoldReadModelResult`; and
- when requested internally, the resolved final Number scalar's `AtlasJsonSourceSpan` for each
  `Present` candidate.

`AtlasGoldReadModel.Read` keeps its exact public API and behavior. The mutation kernel calls the same
inspection; it must not duplicate either fixed traversal. Inspection continues to use ordinal exact
member lookup, duplicate handling, bounded iterative scans, existing one-step reference resolution,
shape classification, integer grammar, and `Int64` parsing.

Source spans come from the resolved scalar syntax, including scalar candidates reached through
reference-backed objects or arrays. Released A3 parsing remains container-identity-only; A6R3 does
not broaden its graph semantics. The transient inspection retains or exposes no graph node,
collection, lexeme, or source buffer. Public A6 results gain no span or graph surface.

## 4. Source decision and failure rules

The kernel checks cancellation and runs the shared inspection once.

- `Disagree` maps to `SourceDisagrees`.
- Every `Incomplete` aggregate or non-`Present` candidate maps to `SourceIncomplete`, including
  missing, ambiguous, wrong-shape, non-integer, and outside-`Int64` subclasses.
- `Consistent` requires both candidates `Present` and equal. Any contradictory internal inspection
  state maps to `UnsupportedInternalState`.
- No gameplay range is imposed beyond representable `Int64`.

If `value` equals the consistent semantic value, return an `Unchanged` result owning the exact bytes
from `source.GetSemanticNoOpBytes()`. Do not normalize spans, construct UTF-8, encode, or re-read.
This preserves the exact compressed source and lexemes such as `-0`.

## 5. Changed candidate construction

For `Changed`, format `value` as invariant decimal `Int64` ASCII without a sign for non-negative
values. Normalize the one or two inspected spans against `source.Json.Utf8Source`:

1. cancellation is checked before and during normalization;
2. each span has non-negative start, positive length, checked end, and an end within the source;
3. the exact source slice is an A6-valid integer Number lexeme whose parsed value equals the
   inspected current value;
4. exact equal spans are deduplicated;
5. only after both distinct spans pass those checks, same-start/different-length, containment, and
   partial overlap fail as `OverlappingSourceSpans`; and
6. malformed, overflowed, out-of-range, or source-mismatched spans therefore take precedence and
   fail as `InvalidSourceSpan`.

The normalization and overlap classifier is an internal pure helper so unreachable invalid and
overlap cases can be tested without fault injection or a general patch API.

Compute the candidate length with checked arithmetic. Allocate one output buffer and copy source
segments plus the replacement in ascending span order using bounded chunks with cancellation checks.
Replace exactly the normalized number spans and preserve every other UTF-8 byte, including
whitespace, member order, unknown properties, markers, reference wrappers, and non-target lexemes.
Work is linear in source bytes plus at most two spans; add no recursion, configurable path language,
or general patcher.

## 6. Encoding, re-read, and verification

Decode the finite constructed buffer once with a strict, exception-throwing UTF-8 decoder solely
because `AtlasLzStringCodec.CompressToBase64` accepts a string. Check cancellation immediately
before and after decoding, then require the decoded string length not to exceed
`limits.MaximumDecompressedCodeUnits`. Compress with the released codec. Before re-reading,
explicitly require the encoded candidate length not to exceed `limits.MaximumEncodedBytes`.

Re-read the encoded bytes with `AtlasSaveReader.Read` and the same supplied limits, then:

1. require the reparsed `Json.Utf8Source` to equal the constructed candidate byte-for-byte using a
   bounded chunk comparison with cancellation between chunks;
2. run the shared A6 inspection once on the reparsed result;
3. require both candidates `Present`, equal to `value`, and aggregate `Consistent`; and
4. return a `Changed` result owning the compressed bytes.

Map codec or reader failures caused by configured encoded, decompressed, JSON depth/token, scalar,
graph-node, identity, or reference limits to `CandidateLimitExceeded`. Map strict UTF-8 failure and
every other generated-candidate parse, lossless-byte, or A6 verification failure to
`CandidateVerificationFailed`. Impossible arithmetic, contradictory internal state, or an
unsupported codec state maps to `UnsupportedInternalState`. Cancellation always propagates as
`OperationCanceledException`.

Keep limit classification and post-read verification as small internal pure helpers. This permits
complete fixed-failure evidence for branches that a valid source plus two number replacements cannot
produce, without adding a production fault-injection seam or general mutation facility.

Check cancellation at entry, during inspection, span normalization, every chunked copy, immediately
before and after the single strict decode, compression, re-read, every byte-comparison chunk,
verification inspection, result creation, and every chunk of `GetCompressedBytes`.

The kernel must not mutate or retain mutable access to the source graph, lossless JSON, compressed
bytes, semantic no-op bytes, members, arrays, identities, or references. No logging or diagnostics
are added.

## 7. Exact C6R3 implementation boundary

C6R3 changes exactly:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldReadModel.cs
  Hcoona.CelesphoniaModifier.Atlas/AtlasGoldMutationKernel.cs
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/AtlasGoldMutationKernelTests.cs
  Hcoona.CelesphoniaModifier.Atlas.Tests/ProjectBoundaryTests.cs
```

`ProjectBoundaryTests.cs` changes only to admit and enforce the new library and test files and the
closed no-filesystem/API boundary. SDK implicit compilation requires no project-file edit.

## 8. Synthetic acceptance and validation

Repository-safe synthetic tests cover, without exhaustive pair matrices:

- both dispositions and every fixed failure/message, using internal pure-helper evidence for
  structurally unreachable invalid-span, overlap, verification, limit-mapping, and unsupported-state
  branches;
- null source, null limits, and every non-positive reader-limit property use the established argument
  exceptions before mutation classification;
- zero, negatives, `Int64.MinValue`, `Int64.MaxValue`, shorter and longer replacement lexemes, and
  no gameplay range;
- exact same-value `-0` no-op bytes without encoding or reparse;
- A3 reference-backed objects and arrays, with scalar candidates reached through them;
- two distinct spans end-to-end and exact-equal-span deduplication through the shared
  inspection/normalization helper;
- malformed or source-mismatched spans classified as `InvalidSourceSpan` before overlap is
  considered, plus overlap between two individually valid distinct spans classified as
  `OverlappingSourceSpans`;
- every representative A6 incomplete subclass and disagreement refusal;
- integer lexeme varieties and refusal of fraction, exponent, and outside-`Int64` Number lexemes;
- end-to-end encoded, decompressed, and scalar limit failures, plus pure mapping evidence for every
  remaining configured reader limit class;
- strict candidate UTF-8, codec round-trip, exact reparsed lossless bytes, and verified A6 equality;
- byte-exact preservation of all non-target UTF-8, including unknown members, markers, wrappers,
  whitespace, order, and unrelated number lexemes;
- unchanged source graph observations, JSON bytes, compressed bytes, and semantic no-op bytes;
- defensive result copies and a pre-canceled token observed without conversion to a domain failure;
- existing focused codec and reader tests remain authoritative for cancellation inside compression
  and re-read; the kernel adds no progress seam or timing-dependent phase test;
- cancellation checks in kernel-owned scans, normalization, chunked construction, verification,
  and result getter where deterministic evidence is available;
- a large bounded document demonstrating linear work and cancellable chunking;
- exact public-surface and project inventories proving no filesystem, stream/path, CLI, schema,
  writer, persistence, or other API boundary; and
- all released A3 through A6R2 regressions.

Before C6R3 review:

1. targeted mutation-kernel tests pass;
2. the full Atlas test project passes;
3. Release build through `dirs.proj` has no new warning or error;
4. `dotnet format --verify-no-changes` passes for affected .NET projects;
5. applicable HK checks pass;
6. repository-safe privacy, value-free failure, and no-I/O inspection passes; and
7. the cumulative diff from `R6R3` contains exactly the four paths in section 7.

## 9. Review policy

Fresh independent general-purpose GPT-5.6 reviewers examine the complete exact plan, activation
record, implementation, and release-record candidates against this plan until each returns
`No findings`. This is proportional engineering review, not a semantic Agent protocol or
high-assurance attestation system.

Credible findings concern fixed-location reuse, classification, span validity or overlap, byte
preservation, codec compatibility, limit mapping, reparse verification, cancellation, immutability,
API leakage, regressions, or maintainability defects that create those risks. Demands for private
execution, semantic proof, gameplay ranges, a general patcher, filesystem writing, operation
authority, transactions, recovery, installers, WinUI, persistent state, ledgers, or runtime
attestation are out of scope.

Adjudicate every finding under the project planning-correction policy. Corrections must retain the
accepted outcome, threat model, exclusions, and exact path budget. Review completes only when the
full exact candidate and any dispositions receive `No findings`.

## 10. Exact gates

These gates establish provenance and bounded authority; runtime never inspects Git state.

### P6R3 - plan candidate

The initial `P6R3` is the direct child of exact `X6R2`
`85db738d466dbaa918683bb8e4c56775c5e7544f`. Accepted review corrections may descend from that
candidate while the cumulative diff from `X6R2` changes exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/README.md
src/private/app/celesphonia-modifier/docs/.copilot/plans/
  atlas-v0-a6-gold-mutation-kernel.md
```

A fresh independent general-purpose GPT-5.6 reviewer examines the exact two-document candidate
holistically until `No findings`. The exact final corrected P6R3 is then committed, pushed, and
verified before activation-record authoring.

### R6R3 - activation record

`R6R3` is the direct child of exact final reviewed `P6R3` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-gold-mutation-kernel-plan-review.md
```

The record binds the exact base and plan candidate, reviewed paths, reviewer identity and
independence, iterations, TP/FP dispositions, validation, and final `No findings`. Its exact staged
candidate receives fresh independent general-purpose GPT-5.6 `No findings`, is committed unchanged,
pushed, and verified. Verified shared `R6R3` activates this plan and authorizes only synthetic
`C6R3`.

### C6R3 - implementation candidate

The initial `C6R3` is the direct child of exact `R6R3`, changes exactly the four paths in section 7,
and uses repository-safe synthetic data only. It is committed and pushed before review. Accepted
corrections may descend from it while the cumulative diff from `R6R3` remains exactly those four
paths. A fresh independent general-purpose GPT-5.6 reviewer examines the complete exact final
candidate and acceptance evidence until `No findings`.

### G6R3 - kernel release

`G6R3` is the direct child of exact reviewed `C6R3` and adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a6-gold-mutation-kernel-release-gate.md
```

The record binds the exact candidate, governing plan and `R6R3`, reviewed paths, validation,
findings and dispositions, reviewer independence, and final `No findings`. Its exact staged
candidate receives fresh independent general-purpose GPT-5.6 `No findings`, is committed unchanged,
pushed, and verified.

`G6R3` releases only pure in-memory candidate generation. It grants no filesystem-write, writer,
operation, private-execution, semantic, range, coupling, transaction, recovery, or persistence
authority.

## 11. Stop conditions and handoff

Stop and return to planning if implementation requires an unlisted path, private or ignored input,
save/snapshot/survey/definition/installation access, a third candidate, a configurable path, a
general patcher, gameplay validation, filesystem or stream/path API, CLI, schema, writer, WinUI,
persistent state, backup, transaction, recovery, installer, operation authority, or historical
semantic/high-assurance machinery.

Resume implementation only from verified shared `R6R3`. After verified shared `G6R3`, report the
released pure candidate-generation boundary and stop. Any use that reads or writes a file requires a
separate approved governing plan.
