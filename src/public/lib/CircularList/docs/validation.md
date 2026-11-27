# Validation and Formal Verification

This record defines how `IO.Github.Hcoona.Collections.CircularList` (`2.0.0-beta.{height}`) behavior, 32-bit index arithmetic, callback/enumerator state transitions, and heap invariants are verified. It records verification architecture and provenance without adding workflow gates or attempt quotas on local iteration.

## Remediated Defect Matrix (`1.x` to `2.0.0-beta`)

| `1.x` Defect / Ambiguity                                                                                  | `2.0.0-beta` Contract                                                                                                         | Verification Layers                                                    |
| --------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `Add(T)` evaluated `this[Count] = item` before incrementing `Count`, throwing when `Count < Capacity`     | `AddCore` writes to physical append slot `GetPhysicalIndex(Count)` and increments `Count` when `Count < Capacity`             | Layer 1 (`CsCheck`), Layer 3 (`TLA+`), Layer 4 (`PAL-CS`)              |
| `new CircularList<T>(0)` succeeded and later threw `DivideByZeroException` on `% Capacity`                | Constructor enforces `capacity >= 1` (`ArgumentOutOfRangeException` on `<= 0`)                                                | Layer 1 (`MSTest`), Layer 2 (`Z3`), Layer 4 (`PAL-CS`)                 |
| `(startIndex + logicalIndex) % Capacity` and `startIndex + count <= Capacity` overflowed signed `Int32`   | Split-segment subtraction `tailLength = Capacity - startIndex` avoids signed 32-bit addition overflow across all valid inputs | Layer 2 (`Z3` `BV-T1`, `BV-T4`), Layer 4 (`PAL-CS`)                    |
| `Clear()` reset `startIndex`/`Count` without clearing `T[]` slots, retaining managed object references    | `Clear()` clears occupied `items` segments via `Array.Clear`, resets `startIndex`/`Count`, and increments `version`           | Layer 1 (`WeakReference` GC test), Layer 3 (`TLA+`), Layer 4           |
| `AsSpan()` silently alternated between a live mutable slice and a detached copy without a 0-alloc API     | `GetSegments()` and `CopyTo(Span<T>)` provide zero-allocation access; `AsSpan()` documents contiguous live-view vs copy rules | Layer 1 (`CsCheck`), Layer 2 (`Z3` `BV-T4`), Layer 3 (`TLA+`)          |
| `OnOverflow` callback ordering relative to ring mutation was unspecified under reentrant subscriber calls | `AddCore` commits ring state and `version` before `Add` invokes `OnOverflow(this, evicted)`                                   | Layer 1 (reentrant `Clear`/`Add`/throw tests), Layer 3 (`Add`/`Clear`) |
| `Enumerator` ignored concurrent/interleaved collection mutations during iteration                         | `version` token increments on `AddCore`, `this[i] = v`, and `Clear()`; `MoveNext()` and `Reset()` fail fast on mismatch       | Layer 1 (`CsCheck`), Layer 3 (`TLA+`), Layer 4 (`PAL-CS`)              |

## Four-Layer Verification Architecture

Following the repository's [shared engineering principles](../../../../../docs/engineering/engineering-principles.md#testing), each tool covers a distinct failure class with explicit epistemic boundaries:

### 1. Stateful Property-Based & Scenario Tests (`CsCheck` + `MSTest`)

- **Location**: `tests/public/lib/CircularList.UnitTest/TestCircularList.cs` (`CsCheck` `5.0.1`, `MSTest.Sdk` `4.3.3`).
- **What is verified**:
    - `StatefulRingOperations_RefineBoundedFifoReferenceModel`: Deterministic seed `"circularlist1951"` executing `400` generated command sequences (up to `64` operations per sequence across capacities `1..12` over `Add`, `SetAt`, and `Clear`). At every step, verifies refinement against a bounded FIFO `List<int>` reference model across `Count`, `Capacity`, `this[i]`, `Contains`, `IndexOf` (including first-occurrence minimality when duplicates straddle wrapped physical segments), `GetSegments()`, `CopyTo(T[], int)`, `CopyTo(Span<T>)`, `AsSpan()` (verifying live backing-storage aliasing when contiguous and snapshot isolation when wrapped), struct and boxed `IEnumerator<T>`/`IEnumerator` traversal, `Reset()`, fail-fast `InvalidOperationException` invalidation on mutation, and exact FIFO `OnOverflow` eviction order.
    - Dedicated deterministic tests covering `capacity <= 0` rejection, `capacity == 1` rollover and wrapped duplicate `IndexOf` ordering, reentrant `OnOverflow` handlers (`Clear()`, `Add()`, and subscriber exceptions), `WeakReference` garbage-collection release after `Clear()`, and guard/unsupported-mutation exceptions (`Insert`, `RemoveAt`, `Remove`) without state corruption.
- **Boundary**: Runtime observations on `.NET 10.0` (`net10.0`, `CircularList.UnitTest`), paired with multi-target Release compilation across `net8.0`, `net9.0`, `netstandard2.0`, `netstandard2.1`, and `net462`.

### 2. 32-Bit Machine-Integer Arithmetic Proofs (`Z3` `QF_BV`)

- **Location**: `src/public/lib/CircularList/scripts/verify_formal_spec.py` (standalone PEP 723 script pinned to `z3-solver==5.4.1.0`, exposed as `mise run circular-list:verify-formal-spec`).
- **What is verified**: Exhaustive quantifier-free bit-vector (`BitVec(32)`) proofs over all valid 32-bit two's-complement machine states (`1 <= Capacity <= 2147483647`, `0 <= startIndex < Capacity`, `0 <= Count <= Capacity`), pairing a `sat` non-vacuity check with an `unsat` negated-obligation proof:
    - `BV-T1`: Defines `tailLength = Capacity - startIndex` and proves via 33-bit (`ZeroExt(1, ...)`) Euclidean decomposition (`sum_33 == quotient_term_33 + phys_33`) that `logicalIndex < tailLength ? startIndex + logicalIndex : logicalIndex - tailLength` never overflows or underflows signed 32-bit `Int32`, stays within `[0, Capacity)`, and equals exact mathematical `(startIndex + logicalIndex) mod Capacity`; extracts a concrete Z3 counterexample against `1.x`'s `(startIndex + logicalIndex) % Capacity` (`Capacity = 2147483521`, `startIndex = 2146165887`, `Count = 4194305`, `logicalIndex = 4194304`, signed sum `-2144607105`).
    - `BV-T2`: Proves physical-index injectivity on `[0, Count)` (bijectivity onto `[0, Capacity)` when `Count == Capacity`), monotonic mapping onto `[startIndex, startIndex + Count)` when `Count <= tailLength`, and partition into `[startIndex, Capacity)` followed by `[0, Count - tailLength)` when wrapped.
    - `BV-T3`: Proves signed 32-bit non-overflow/non-underflow and modular exactness of `AddCore`'s append slot (`Count < Capacity`) and overwrite/rollover transition (`Count == Capacity`), preserving the ring representation invariant and existing logical elements.
    - `BV-T4`: Proves `Count <= Capacity - startIndex`, `GetSegments()` / `Clear()` slice bounds, `CopyTo(T[], int)` destination offset `arrayIndex + tailLength`, and unsigned range-check equivalence (`(uint)logicalIndex < (uint)Count`) never overflow signed 32-bit `Int32` and stay within source `[0, Capacity]` and destination `[0, array.Length]`; extracts a concrete Z3 counterexample against `1.x`'s `startIndex + Count <= Capacity` (`Capacity = 2147483647`, `startIndex = 2147483646`, `Count = 2`, signed sum `-2147483648`).
    - `VEC-BV-01`–`VEC-BV-06`: Six `GetPhysicalIndex` boundary witnesses (`VEC-BV-01` degenerate `Capacity = 1` single-slot ring; `VEC-BV-02` `0x40000000 + 0x40000000 == 0x80000000` signed-overflow boundary; `VEC-BV-03` `0x7FFFFFFF` capacity wraparound to physical `0`; `VEC-BV-04` `0x7FFFFFFF` capacity newest slot at `startIndex - 1`; `VEC-BV-05` exact tail boundary `logicalIndex == tailLength - 1 -> Capacity - 1`; `VEC-BV-06` exact wrap boundary `logicalIndex == tailLength -> 0`).
- **Boundary**: Exhaustive over the modeled 32-bit bit-vector index/segment/copy expressions; complements rather than parses the C# syntax tree directly.

### 3. Finite-State Protocol, Reentrancy & Fail-Fast Model Checking (`TLA+` / `TLC`)

- **Location**: `src/public/lib/CircularList/docs/models/CircularList.tla` and `CircularList.cfg` (bound in `docs/governance/record-families.yaml` and syntax-checked by HK `tla-sany`).
- **What is verified**: Exhaustive breadth-first `TLC` (`2.19`, `rev: 5a47802`) state-space exploration under `MaxCapacity = 3` (`cap \in 1..3`), `Values = {1, 2}`, `Cleared = 0`, `MaxSteps = 5`, and `NumEnumerators = 2` (`29,702` generated states, `8,587` distinct states, search depth `6`, `0` errors). Models `CallerAdd`, `CallerSetItem`, `CallerClear`, synchronous single-level `OnOverflow` reentrancy (`callbackDepth \in 0..1`: `OverflowHandlerReentrantAdd`, `OverflowHandlerReentrantClear`, `OverflowHandlerReturn`), and two independent enumerator lifecycles (`OpenEnumerator`, `StepEnumerator`, `ResetEnumerator`, `DisposeEnumerator` gated on `callbackDepth = 0`). Verifies six invariants in every reachable state:
    1. `TypeAndBoundsInvariant`
    2. `SequenceRefinementInvariant`
    3. `SegmentsPartitionInvariant`
    4. `OverflowCallbackCoherenceInvariant`
    5. `NoStaleReferencesInvariant`
    6. `EnumeratorFailFastInvariant`
- **Boundary**: Sound and exhaustive only within those configured finite bounds (`cap \in 1..3`, `Values = {1, 2}`, `NumEnumerators = 2`, `MaxSteps = 5`, `callbackDepth \in 0..1`) on single-threaded interleavings. Subscriber exception propagation (`OnOverflow` throwing) is verified in Layer 1 (`TestCircularList.cs`) rather than modeled in `TLA+`, and `CircularList<T>` is not thread-safe without external synchronization.

### 4. Offline Deductive Separation-Logic Verification (`PAL-CS` / Pulse / F\*)

- **Location**: `#if PAL` ghost model (`CircularListModel<T>`), separation-logic class/struct invariants (`Class_CircularList`, `Struct_CircularList_Enumerator`), and method contracts embedded in `src/public/lib/CircularList/CircularList.cs`. When `PAL` is not defined (all repository and NuGet builds), `CircularList.dll` has zero `Pal.Annotations` or `FSharp.Core` dependencies.
- **Recorded offline verification provenance**: Verified offline on Ubuntu x86_64 against [`FStarLang/pal-csharp`](https://github.com/FStarLang/pal-csharp) commit `68787f0d828dea85434e6ff75eab0c19f3fa17dc`, [`FStarLang/FStar`](https://github.com/FStarLang/FStar) `2026.10.25~dev` commit `b6b10e72d9cd42a56e5b09c25208de61119bf484` (OCaml `5.3.0`, `Pulse.Lib.CSharp`), and bundled F\* Z3 `5.3.0`. Running `palcs` + `fstar.exe` via `pal-csharp/test/verify.mk` produced and discharged **24 translated modules plus `TranslationErrors` (`25 / 25` `.fst.checked` files)** with `0` translation errors and `0` admitted proof obligations:
    - Type & invariant modules: `TranslationErrors`, `Struct_CircularListModel`, `Class_CircularList` (including fractional-permission `inv_share` / `inv_gather` over `pts_to` and `Seq.ring` refinement), and `Struct_CircularList_Enumerator`.
    - Verified member modules: `.ctor`, `Capacity`, `Count`, `IsReadOnly`, `get_Item`, `set_Item`, `AddCore`, `Contains`, `IndexOf`, `Clear`, `CopyTo(T[], int)`, `Insert`, `Remove`, `RemoveAt`, `GetPhysicalIndex`, `GetEnumerator`, and `Enumerator` (`.ctor`, `Current`, `MoveNext`, `Reset`, `Dispose`).
- **Boundary and offline delivery rationale**:
    1. `PAL-CS` does not currently translate C# `ref struct` span APIs (`Span<T>` / `ReadOnlySpan<T>` in `GetSegments()`, `CopyTo(Span<T>)`, `AsSpan()`) or multicast `event` delegate invocation (`Add(T)`'s `OnOverflow` dispatch around verified `AddCore(T, out T)`). `CircularList.cs` opts into `[assembly: TranslateAnnotatedOnly]` under `#if PAL` so `palcs` translates all annotated types/members while those thin unannotated wrappers are verified by Layers 1–3.
    2. `IndexOf(T)`'s `#if PAL` postcondition proves presence/absence equivalence (`(Result >= 0) == (Pal.IndexOf(@this.Items, item) >= 0)`), `-1` sentinel return on absence, and in-range matching witness (`0 <= Result < SpecCount && Pal.DefaultEquals(Items[Result], item)`) via `Pulse.Lib.CSharp.Seq.ring_index_of` and `index_of_range`. Because upstream `Pulse.Lib.CSharp.Seq.index_of_range` does not state a least-index clause, first-occurrence minimality on duplicate elements is verified by Layer 1 (`CsCheck` stateful refinement and wrapped-duplicate tests).
    3. `PAL-CS` translates non-`[Wrapping]` C# `int` arithmetic to signed 32-bit `FStar.Int32.t` with mandatory no-overflow proof obligations (complementing Layer 2's `Z3` `BitVec(32)` modular-equivalence and untranslated span-slice proofs). By contrast, `[Wrapping] private int version` lowers `this.version++` to 32-bit two's-complement wrapping addition (`Int32.add_wrap`), and `Enumerator.MoveNext()` / `Reset()` contracts compare version stamps (`@this.version == @this.list.SpecVersion`) rather than full mutation histories (whereas Layer 3 `TLA+` models `version \in Nat` within `MaxSteps = 5`), leaving the standard .NET BCL $2^{32}$-mutation `Int32` version-token wraparound collision boundary.
    4. Analogous to `src/public/lib/Nanopass/`'s offline mechanized proofs, `PAL-CS` runs offline rather than in repository CI because upstream `Pal.Annotations` NuGet packaging and external CI integration are still under active upstream design; see the F\* Zulip thread [`Plans for NuGet packaging and external CI integration?`](https://fstar.zulipchat.com/#narrow/channel/184683-questions-and-help/topic/Plans.20for.20NuGet.20packaging.20and.20external.20CI.20integration.3F/with/643981646).

## Execution Commands

Run in-repository build, unit/property tests, Z3 bit-vector proofs, and TLA+ model checking from the repository root:

```bash
mise exec -- dotnet build src/public/lib/CircularList/CircularList.csproj -c Release
mise exec -- dotnet test --project tests/public/lib/CircularList.UnitTest/CircularList.UnitTest.csproj -c Release
mise run circular-list:verify-formal-spec
mise run tla:tlc -- -workers 1 -config src/public/lib/CircularList/docs/models/CircularList.cfg src/public/lib/CircularList/docs/models/CircularList.tla
```

Reproduce the offline `PAL-CS` / Pulse-F\* verification in an isolated temporary workspace outside the repository:

```bash
git clone https://github.com/FStarLang/pal-csharp.git /tmp/palcs-verify/pal-csharp
git -C /tmp/palcs-verify/pal-csharp checkout 68787f0d828dea85434e6ff75eab0c19f3fa17dc
make -C /tmp/palcs-verify/pal-csharp tools all
mkdir -p /tmp/palcs-verify/work
make -j4 -C /tmp/palcs-verify/work -f /tmp/palcs-verify/pal-csharp/test/verify.mk \
  SOURCES="$PWD/src/public/lib/CircularList/CircularList.cs" \
  verify
```
