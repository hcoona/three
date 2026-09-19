# xUnit -> MSTest Mapping Cheatsheet

Comprehensive reference loaded by the `migrate-xunit-to-mstest` skill. Look up specific xUnit constructs and their MSTest v4 equivalents, including edge cases and "no equivalent -- manual" calls.

Target framework throughout: **MSTest v4** (the few v3-only spellings are explicitly marked).

## Table of contents

- [1. Test discovery (class + method attributes)](#1-test-discovery-class--method-attributes)
- [2. Data-driven tests](#2-data-driven-tests)
- [3. Assertions](#3-assertions)
    - [3.1 Equality, null, reference](#31-equality-null-reference)
    - [3.2 Boolean](#32-boolean)
    - [3.3 Type checks](#33-type-checks)
    - [3.4 Numeric / comparison](#34-numeric--comparison)
    - [3.5 String](#35-string)
    - [3.6 Collection](#36-collection)
    - [3.7 Exceptions](#37-exceptions)
    - [3.8 Async exception assertions](#38-async-exception-assertions)
    - [3.9 Skip / inconclusive](#39-skip--inconclusive)
    - [3.10 Fail](#310-fail)
    - [3.11 No-equivalent assertions](#311-no-equivalent-assertions)
- [4. Fixtures and lifecycle](#4-fixtures-and-lifecycle)
- [5. Output / TestContext](#5-output--testcontext)
- [6. Cancellation and timeouts (xUnit v3 specifics)](#6-cancellation-and-timeouts-xunit-v3-specifics)
- [7. Parallelization](#7-parallelization)
- [8. Assembly-level attributes](#8-assembly-level-attributes)
- [9. Packages](#9-packages)
- [10. Companion / extension libraries](#10-companion--extension-libraries)

## 1. Test discovery (class + method attributes)

| xUnit                             | MSTest                                                                                                                                                                                                                           |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| _(no class attribute)_            | `[TestClass]` (required)                                                                                                                                                                                                         |
| _(no class modifier)_             | Preserve the original hierarchy. Do **not** add `sealed` mechanically -- base/derived test classes are common in xUnit and sealing would break them. `writing-mstest-tests` can apply `sealed` as a follow-up where appropriate. |
| `[Fact]`                          | `[TestMethod]`                                                                                                                                                                                                                   |
| `[Theory]`                        | `[TestMethod]` (MSTest 3+ unified; `[DataTestMethod]` still works but is not needed)                                                                                                                                             |
| `[Fact(DisplayName = "x")]`       | MSTest 4: `[TestMethod(DisplayName = "x")]`; MSTest 3: `[TestMethod("x")]`                                                                                                                                                       |
| `[Theory(DisplayName = "x")]`     | Same as above on the `[TestMethod]`                                                                                                                                                                                              |
| `[Fact(Skip = "reason")]`         | `[TestMethod]` + `[Ignore("reason")]` (the `[Ignore]` attribute alone does not discover a test -- you still need `[TestMethod]`)                                                                                                 |
| `[Fact(Timeout = 5000)]`          | `[TestMethod]` + `[Timeout(5000)]` (same -- `[Timeout]` is a modifier, not a discovery attribute)                                                                                                                                |
| `[Trait("Category", "Unit")]`     | `[TestCategory("Unit")]`                                                                                                                                                                                                         |
| `[Trait("Owner", "alice")]`       | Method-only `[Owner("alice")]` only when the scope rule below preserves every affected test's effective Owner values                                                                                                             |
| `[Collection("Db")]`              | **Manual** -- preserve collection sharing and serialization; see Section 4. `[DoNotParallelize]` can serialize more broadly than the source collection.                                                                          |
| Custom `FactAttribute` subclass   | Custom `TestMethodAttribute` subclass overriding `ExecuteAsync` (MSTest v4). See `writing-mstest-tests` and `migrate-mstest-v3-to-v4` for `CallerInfo` constructor pattern                                                       |
| Custom `TheoryAttribute` subclass | Same -- subclass `TestMethodAttribute`; expose data via `ITestDataSource`                                                                                                                                                        |

> Both `[TestCategory]` and `[TestProperty]` are **filterable** at runtime:
>
> - `[TestCategory("Unit")]` -> `--filter "TestCategory=Unit"` (VSTest) / `--filter-trait "TestCategory=Unit"` (MTP); targets `Assembly`, `Class`, and `Method`
> - `[Owner("alice")]` -> `--filter "Owner=alice"` (VSTest) / `--filter-trait "Owner=alice"` (MTP)
> - `[TestProperty("Key", "Value")]` -> `--filter "Key=Value"` (VSTest) / `--filter-trait "Key=Value"` (MTP); targets `Class` and `Method` only (no `AttributeTargets.Assembly`)
>
> **Owner scope:** MSTest `[Owner]` targets methods only and permits one value. Determine each test's effective Owner values from applicable method, class, and assembly traits, including inherited metadata. Use `[Owner(value)]` only when that method declaration preserves the effective Owner values of every affected test; do not emit it on classes or assemblies. Otherwise, including multiple distinct values, require an explicit manual mapping that preserves metadata and filtering/reporting behavior; do not silently choose, overwrite, drop, or join values. `Owner` is reserved in VSTest, so `[TestProperty("Owner", value)]` is not a substitute.
>
> Use `[TestCategory]` for the conventional category trait and `[TestProperty]` for other keys at class/method scope, excluding Owner as described above. An `[assembly: Trait("Category", ...)]` in xUnit can be migrated to `[assembly: TestCategory(...)]`. An assembly-level `[Trait]` with another key (neither Category nor Owner) cannot map to `[assembly: TestProperty(...)]` -- collapse it to `[assembly: TestCategory(...)]` or move it down to every class (see Section 8).
>
> **Conditional skips** (xUnit `[Trait("OS", "Windows")]` patterns that gate execution): MSTest 3.10+ offers dedicated condition attributes -- `[OSCondition]` and `[CICondition]` -- which are usually a better fit than overloading `[TestCategory]` for environmental gating. (There is no `ArchitectureCondition` or `NonParallelizableCondition` attribute in MSTest; for non-parallel intent use `[DoNotParallelize]`, and for architecture gating fall back to `if (RuntimeInformation.OSArchitecture != ...) Assert.Inconclusive(...)`.) See Section 3.9.

## 2. Data-driven tests

| xUnit                                                                | MSTest                                                                                                                                                                                                                                                                                                                                                                             |
| -------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[InlineData(1, 2)]`                                                 | `[DataRow(1, 2)]`                                                                                                                                                                                                                                                                                                                                                                  |
| `[InlineData(1, DisplayName = "case 1")]`                            | `[DataRow(1, DisplayName = "case 1")]`                                                                                                                                                                                                                                                                                                                                             |
| `[InlineData(null)]`                                                 | `[DataRow(null)]`                                                                                                                                                                                                                                                                                                                                                                  |
| `[MemberData(nameof(Cases))]` returning `IEnumerable<object[]>`      | `[DynamicData(nameof(Cases))]` returning `IEnumerable<object[]>`                                                                                                                                                                                                                                                                                                                   |
| `[MemberData(nameof(Cases), MemberType = typeof(X))]`                | `[DynamicData(nameof(Cases), typeof(X))]`                                                                                                                                                                                                                                                                                                                                          |
| `[MemberData(nameof(Cases))]` returning `TheoryData<int, string>`    | `[DynamicData(nameof(Cases))]` returning `IEnumerable<object[]>`, `IEnumerable<(int, string)>` (MSTest 3.7+ ValueTuple), or `IEnumerable<TestDataRow<(int, string)>>` (strongly-typed with per-row `DisplayName`/`Ignore` metadata -- see [docs](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-data-driven#supported-data-source-types)) |
| `[MemberData(nameof(Method), arg1, arg2)]` (parameterized member)    | **Manual** -- convert to a parameterless property/method, or move parameter logic into the test method                                                                                                                                                                                                                                                                             |
| `[ClassData(typeof(MyData))]` where `MyData : IEnumerable<object[]>` | Expose a static `IEnumerable<object[]> Cases => new MyData();` and use `[DynamicData(nameof(Cases))]`                                                                                                                                                                                                                                                                              |
| `[ClassData(typeof(MyData))]` where `MyData : TheoryData<...>`       | Same approach; convert `TheoryData<...>` to `IEnumerable<object[]>` or ValueTuples                                                                                                                                                                                                                                                                                                 |
| Custom `DataAttribute` subclass                                      | **Manual** -- implement `ITestDataSource` (`GetData` + `GetDisplayName`)                                                                                                                                                                                                                                                                                                           |

**Literal-type trap.** MSTest's `[DataRow]` enforces exact type matching against method parameters. xUnit's `[InlineData]` is more permissive. After conversion, audit literals:

| Parameter type | Required literal |
| -------------- | ---------------- |
| `int`          | `1`, `0`, `-1`   |
| `long`         | `1L`             |
| `float`        | `1.0f`           |
| `double`       | `1.0` or `1.0d`  |
| `decimal`      | `1.0m`           |
| `uint`         | `1U`             |
| `Type`         | `typeof(...)`    |

## 3. Assertions

### 3.1 Equality, null, reference

| xUnit                                                   | MSTest                                                                                                                                                                                                                             |
| ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assert.Equal(expected, actual)`                        | `Assert.AreEqual(expected, actual)`                                                                                                                                                                                                |
| `Assert.Equal(expected, actual, comparer)`              | `Assert.AreEqual(expected, actual, comparer)`                                                                                                                                                                                      |
| `Assert.Equal(expected, actual, precision[, rounding])` | **Manual** -- preserve the numeric overload, decimal-place precision, and default or explicit rounding mode; round both operands as the source assertion does and compare the results exactly. Precision is not an absolute delta. |
| `Assert.Equal("a", "A", ignoreCase: true)`              | See the [string equality mapping](#35-string).                                                                                                                                                                                     |
| `Assert.NotEqual(a, b)` (scalar equality)               | `Assert.AreNotEqual(a, b)`                                                                                                                                                                                                         |
| `Assert.Same(a, b)`                                     | `Assert.AreSame(a, b)`                                                                                                                                                                                                             |
| `Assert.NotSame(a, b)`                                  | `Assert.AreNotSame(a, b)`                                                                                                                                                                                                          |
| `Assert.Null(x)`                                        | `Assert.IsNull(x)`                                                                                                                                                                                                                 |
| `Assert.NotNull(x)`                                     | `Assert.IsNotNull(x)`                                                                                                                                                                                                              |
| `Assert.Equivalent(expected, actual)`                   | **Manual** -- no built-in deep-equality assertion. Use a third-party library (FluentAssertions `.Should().BeEquivalentTo(...)`) or write member-by-member assertions                                                               |

### 3.2 Boolean

| xUnit                   | MSTest                    |
| ----------------------- | ------------------------- |
| `Assert.True(x)`        | `Assert.IsTrue(x)`        |
| `Assert.False(x)`       | `Assert.IsFalse(x)`       |
| `Assert.True(x, "msg")` | `Assert.IsTrue(x, "msg")` |

### 3.3 Type checks

| xUnit                                           | MSTest                                                                                            |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `Assert.IsType<T>(x)` (exact type, returns `T`) | `var t = Assert.IsExactInstanceOfType<T>(x);` (MSTest 4.1+; returns the typed value, exact match) |
| `Assert.IsNotType<T>(x)` (exact type)           | `Assert.IsNotExactInstanceOfType<T>(x);` (MSTest 4.1+)                                            |
| `Assert.IsAssignableFrom<T>(x)`                 | `Assert.IsInstanceOfType<T>(x)` -- semantically equivalent (assignable-from check)                |

> MSTest 4.1+ adds `Assert.IsExactInstanceOfType<T>(x)` -- the proper equivalent of xUnit's exact-type `Assert.IsType<T>` (returns `T`, single call). On pre-4.1 MSTest, fall back to `var t = Assert.IsInstanceOfType<T>(x); Assert.AreEqual(typeof(T), x.GetType());`. `Assert.IsInstanceOfType<T>(x)` on its own is **assignable-only** (= xUnit `Assert.IsAssignableFrom<T>`); silently mapping `IsType<T>` to it loses exact-type semantics.
>
> MSTest v4's `Assert.IsInstanceOfType<T>(x)` returns the typed value (no out param). MSTest v3 uses `Assert.IsInstanceOfType<T>(x, out var typed)`.

### 3.4 Numeric / comparison

| xUnit                                 | MSTest                                                                                                                                                                              |
| ------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assert.InRange(value, low, high)`    | `Assert.IsInRange(low, high, value)` for supported value types, valid bounds, and the same comparison; otherwise **Manual** -- preserve the source comparison and inclusive bounds. |
| `Assert.NotInRange(value, low, high)` | **Manual** -- assert that the value compares below `low` or above `high` using the source comparison; preserve custom comparers and inclusive-bound behavior.                       |
| _(no direct API)_                     | `Assert.IsGreaterThan(low, value)`                                                                                                                                                  |
| _(no direct API)_                     | `Assert.IsLessThan(high, value)`                                                                                                                                                    |

Manual range mappings must retain the source overload's null-argument contract.

### 3.5 String

The default source substring/prefix/suffix overloads use `CurrentCulture`; carry over an explicit source `StringComparison` instead when present.

| xUnit                                      | MSTest                                                                                                                                                                                                |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assert.Contains("sub", str)`              | `Assert.Contains("sub", str, StringComparison.CurrentCulture)`                                                                                                                                        |
| `Assert.DoesNotContain("sub", str)`        | `Assert.DoesNotContain("sub", str, StringComparison.CurrentCulture)`                                                                                                                                  |
| `Assert.StartsWith("p", str)`              | `Assert.StartsWith("p", str, StringComparison.CurrentCulture)`                                                                                                                                        |
| `Assert.EndsWith("s", str)`                | `Assert.EndsWith("s", str, StringComparison.CurrentCulture)`                                                                                                                                          |
| `Assert.Matches("\\d+", str)`              | `Assert.MatchesRegex(@"\d+", str)`                                                                                                                                                                    |
| `Assert.DoesNotMatch("\\d+", str)`         | `Assert.DoesNotMatchRegex(@"\d+", str)`                                                                                                                                                               |
| `Assert.Equal("a", "A", ignoreCase: true)` | **Manual** -- preserve the source case-folding and other string-comparison options. MSTest `ignoreCase` uses linguistic comparison and is not a general equivalent of xUnit's character-wise folding. |

### 3.6 Collection

Direct equality and membership mappings require the same effective element equality as the source. For nested/structural elements or differing comparers, use an explicit manual mapping preserving that equality; a default target comparer is not a general replacement for xUnit's assertion comparer.

Sequence `Assert.NotEqual` requires a manual ordered element/count inequality check using the source comparison and null semantics; scalar `Assert.AreNotEqual` can accept equal-content sequences by reference.

| xUnit                                                               | MSTest                                                                                                                                                                                                                                                                                                       |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Assert.Contains(item, collection)`                                 | `Assert.Contains(item, collection)`                                                                                                                                                                                                                                                                          |
| `Assert.DoesNotContain(item, collection)`                           | `Assert.DoesNotContain(item, collection)`                                                                                                                                                                                                                                                                    |
| `Assert.Contains(collection, x => predicate)`                       | `Assert.IsTrue(collection.Any(x => predicate))`                                                                                                                                                                                                                                                              |
| `Assert.Empty(collection)`                                          | `Assert.IsEmpty(collection)`                                                                                                                                                                                                                                                                                 |
| `Assert.NotEmpty(collection)`                                       | `Assert.IsNotEmpty(collection)`                                                                                                                                                                                                                                                                              |
| `Assert.Single(collection)`                                         | `var item = Assert.ContainsSingle(collection);` (returns the element)                                                                                                                                                                                                                                        |
| `Assert.Single(collection, predicate)`                              | `var item = Assert.ContainsSingle(collection.Where(predicate));`                                                                                                                                                                                                                                             |
| `Assert.Collection(items, e1 => ..., e2 => ...)`                    | **Manual** -- assert count, then per-element. No idiomatic MSTest equivalent                                                                                                                                                                                                                                 |
| `Assert.All(items, x => assertion(x))`                              | **Manual** -- `foreach (var x in items) assertion(x);`                                                                                                                                                                                                                                                       |
| `Assert.Equal(expected, actual)` on `IEnumerable<T>` (element-wise) | `Assert.AreSequenceEqual(expected, actual)` (MSTest 4.3+); pre-4.3: `CollectionAssert.AreEqual(expected.ToList(), actual.ToList())` (`IList` required). Plain `Assert.AreEqual` does **not** compare element-wise (MSTEST0065).                                                                              |
| `Assert.Equal(expected, actual, comparer)` on collections           | `Assert.AreSequenceEqual(expected, actual, comparer)` (MSTest 4.3+); pre-4.3: **Manual** -- compare lengths and elements in order using the original `IEqualityComparer<T>`, preserving null behavior. `CollectionAssert` requires a non-generic ordering comparer and cannot accept this comparer directly. |
| `Assert.Distinct(collection)`                                       | **Manual** -- `var seen = new HashSet<T>(); foreach (var item in collection) Assert.IsTrue(seen.Add(item));`, where `T` is the source element type. For the explicit-comparer overload, pass the original comparer to the set and preserve the source overload's null contract.                              |
| `Assert.Superset(expected, actual)`                                 | **Manual** -- `Assert.IsTrue(actual.IsSupersetOf(expected))` for non-null sets; retain the source receiver and its comparer.                                                                                                                                                                                 |

### 3.7 Exceptions

> **Semantic trap**: xUnit `Assert.Throws<T>` = **exact type**. xUnit `Assert.ThrowsAny<T>` = **derived types also match**. The names invert between the frameworks.

| xUnit                                                               | MSTest                                                                                                                                                            |
| ------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assert.Throws<T>(() => ...)`                                       | **`Assert.ThrowsExactly<T>(() => ...)`**                                                                                                                          |
| `Assert.ThrowsAny<T>(() => ...)`                                    | **`Assert.Throws<T>(() => ...)`**                                                                                                                                 |
| `Assert.Throws<T>(paramName, () => ...)` (ArgumentException family) | `var ex = Assert.ThrowsExactly<T>(() => ...); Assert.AreEqual(paramName, ex.ParamName);`                                                                          |
| `Record.Exception(() => ...)`                                       | **Manual** -- `try { ...; return null; } catch (Exception ex) { return ex; }`. If you only need to assert a specific type, use `Assert.ThrowsExactly<T>` directly |

### 3.8 Async exception assertions

| xUnit                                        | MSTest                                                                               |
| -------------------------------------------- | ------------------------------------------------------------------------------------ |
| `await Assert.ThrowsAsync<T>(() => task)`    | `await Assert.ThrowsExactlyAsync<T>(() => task)`                                     |
| `await Assert.ThrowsAnyAsync<T>(() => task)` | `await Assert.ThrowsAsync<T>(() => task)`                                            |
| `await Record.ExceptionAsync(() => task)`    | **Manual** -- `try { await task; return null; } catch (Exception ex) { return ex; }` |

### 3.9 Skip / inconclusive

> xUnit `Assert.Skip*` is **runtime** (decided inside the test body). MSTest `[Ignore]` is **compile-time** (decided at discovery). They are not interchangeable -- mapping `SkipUnless` to `[Ignore]` will permanently exclude the test on machines where it should have run.
>
> **Prefer MSTest's condition attributes** (`[OSCondition]` and `[CICondition]` -- MSTest 3.10+) over `Assert.Inconclusive` when the condition is OS- or CI-environmental. They are discoverable, reportable per-condition, and do not pollute the test body with skip plumbing. (MSTest does **not** ship an `ArchitectureCondition` or `NonParallelizableCondition` attribute -- for architecture gating fall back to runtime `Assert.Inconclusive`; for "do not run in parallel" use `[DoNotParallelize]`.)

| xUnit                                                                      | MSTest                                                                                                                                         |
| -------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `[Fact(Skip = "reason")]`                                                  | `[TestMethod]` + `[Ignore("reason")]`                                                                                                          |
| `Assert.Skip("reason")` (xUnit v3)                                         | `Assert.Inconclusive("reason")`                                                                                                                |
| `Assert.SkipWhen(condition, "reason")` (xUnit v3)                          | If `condition` is environmental: `[OSCondition(...)]` / `[CICondition(...)]` / etc. Otherwise: `if (condition) Assert.Inconclusive("reason");` |
| `Assert.SkipUnless(condition, "reason")` (xUnit v3)                        | Same -- prefer a condition attribute when the predicate is environmental; otherwise `if (!condition) Assert.Inconclusive("reason");`           |
| `Assert.SkipUnless(OperatingSystem.IsWindows(), "...")`                    | `[OSCondition(OperatingSystems.Windows)]` on the method                                                                                        |
| `Assert.SkipWhen(Environment.GetEnvironmentVariable("CI") != null, "...")` | `[CICondition(ConditionMode.Exclude)]` on the method                                                                                           |

### 3.10 Fail

| xUnit                   | MSTest                  |
| ----------------------- | ----------------------- |
| `Assert.Fail("reason")` | `Assert.Fail("reason")` |

### 3.11 No-equivalent assertions

These xUnit assertions have no MSTest equivalent. Convert each manually:

| xUnit                                                               | Manual replacement                                                               |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `Assert.Collection(items, e1Inspector, e2Inspector, ...)`           | `Assert.HasCount(N, items); var arr = items.ToArray(); e1Inspector(arr[0]); ...` |
| `Assert.All(items, inspector)`                                      | `foreach (var item in items) inspector(item);`                                   |
| `Assert.Equivalent(expected, actual)`                               | Deep-compare manually, or use FluentAssertions / Verify                          |
| `Assert.Raises<T>(addHandler, removeHandler, () => trigger())`      | Manual subscribe/flag/unsubscribe                                                |
| `Assert.RaisesAny<T>(...)`                                          | Same -- manual handler                                                           |
| `Assert.PropertyChanged(notifier, "Prop", () => action)`            | Subscribe to `INotifyPropertyChanged.PropertyChanged`, set a flag, assert        |
| `Assert.PropertyChangedAsync(notifier, "Prop", async () => action)` | Same, with `await`                                                               |

## 4. Fixtures and lifecycle

### Test-class lifecycle (per-test)

| xUnit                                         | MSTest                                                                                                             |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| Constructor (sync setup)                      | Keep the constructor (MSTest also instantiates one instance per test method)                                       |
| Constructor taking `ITestOutputHelper output` | Constructor taking `TestContext testContext` (MSTest 3.6+)                                                         |
| `Dispose()`                                   | Keep `Dispose()` (MSTest supports `IDisposable`) **or** convert to `[TestCleanup] public void Cleanup()`           |
| `IAsyncDisposable.DisposeAsync()`             | Keep `DisposeAsync()` (MSTest supports `IAsyncDisposable`) **or** `[TestCleanup] public async Task CleanupAsync()` |
| `IAsyncLifetime.InitializeAsync()`            | `[TestInitialize] public async Task InitAsync()`                                                                   |
| `IAsyncLifetime.DisposeAsync()`               | `[TestCleanup] public async Task CleanupAsync()`                                                                   |

> Per `writing-mstest-tests`: prefer the constructor for sync initialization (it allows `readonly` fields and works correctly with nullability). Use `[TestInitialize]` only for async setup or when `TestContext` is needed but you have not adopted constructor injection.

### Class-level fixtures (shared across tests in one class)

xUnit `IClassFixture<T>` -- one fixture instance per test class, shared by every test method in that class:

```csharp
// xUnit
public class DbFixture : IDisposable { /* ... */ }

public class OrderTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;
    public OrderTests(DbFixture fixture) => _fixture = fixture;
}
```

```csharp
// MSTest equivalent
[TestClass]
public sealed class OrderTests
{
    private static DbFixture? s_fixture;

    [ClassInitialize]
    public static void ClassInit(TestContext context) => s_fixture = new DbFixture();

    [ClassCleanup]
    public static void ClassCleanup() => s_fixture?.Dispose();
}
```

### Cross-class fixtures (`ICollectionFixture<T>` / `[CollectionDefinition]`)

In xUnit collection mode, collection fixtures share an instance across member classes, and those classes run serially. `DisableParallelization` additionally controls overlap with other collections; it is not required for member serialization. Preserve sharing, lifetime, and serialization separately in MSTest:

- **Sharing** -> `[AssemblyInitialize]` (genuinely process-wide) **or** static `Lazy<T>` shared helper referenced by each class's `[ClassInitialize]`
- **Serialization** -> preserve serial member execution even when `DisableParallelization` is false. `[DoNotParallelize]` on each member class is conservative and may reduce overlap with unrelated classes; report that difference. A shared `Lazy<T>` does not serialize test access.

Map deliberately:

| xUnit collection setup                                                                                                      | MSTest adaptation                                                                                                                                                                                              |
| --------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[CollectionDefinition("Db")]` + `ICollectionFixture<DbFixture>`, member classes have `[Collection("Db")]`, collection mode | Preserve the shared fixture and its lifetime, and serialize member classes as described above. Do not omit serialization because `DisableParallelization` is false.                                            |
| Same but `[CollectionDefinition("Db", DisableParallelization = true)]`                                                      | Preserve sharing and member serialization, plus the source restriction on overlap with other collections. `[DoNotParallelize]` on member classes is conservative; report any remaining scheduling differences. |
| Genuinely process-wide singleton (e.g., `WebApplicationFactory` for a TestServer the whole assembly hits)                   | `[AssemblyInitialize]` + `[AssemblyCleanup]` in a dedicated `AssemblySetup` class -- with the user's explicit acknowledgement that scope widens to the whole assembly                                          |
| Custom `ITestCollectionOrderer`                                                                                             | **Manual** -- MSTest's `[TestMethodAttribute]` ordering model is different; flag for review                                                                                                                    |

### Assembly-level fixtures

| xUnit                                                                                              | MSTest                                                                                                                                                                        |
| -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| _(no built-in -- emulated via assembly-scoped `[CollectionDefinition]` + `ICollectionFixture<T>`)_ | `[AssemblyInitialize] public static void AssemblyInit(TestContext context)` and `[AssemblyCleanup] public static void AssemblyCleanup()` -- in any class marked `[TestClass]` |

## 5. Output / TestContext

| xUnit                                                            | MSTest                                                                                                                                                                                                                                                                                              |
| ---------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ITestOutputHelper` constructor parameter                        | `TestContext` constructor parameter (MSTest 3.6+) or `public TestContext TestContext { get; set; } = null!;` property                                                                                                                                                                               |
| `_output.WriteLine("...")`                                       | `_testContext.WriteLine("...")`                                                                                                                                                                                                                                                                     |
| `_output.WriteLine("fmt {0}", arg)` (xUnit v2)                   | `_testContext.WriteLine($"fmt {arg}")` (interpolation -- MSTest v4 dropped most format-string overloads)                                                                                                                                                                                            |
| `TestContext.Current.TestOutputHelper.WriteLine(...)` (xUnit v3) | `_testContext.WriteLine(...)`                                                                                                                                                                                                                                                                       |
| `TestContext.Current.AddAttachment(name, contents)` (xUnit v3)   | **Manual** -- write the attachment contents to a test-specific file, call `_testContext.AddResultFile(pathOnDisk)`, and retain the file until the runner collects it. Preserve the attachment name and media type where the selected runner supports them; report unsupported metadata differences. |
| `TestContext.Current.TestMethod.MethodInfo.Name` (xUnit v3)      | `_testContext.TestName`                                                                                                                                                                                                                                                                             |
| `TestContext.Current.TestClass.Class.Name` (xUnit v3)            | `_testContext.FullyQualifiedTestClassName`                                                                                                                                                                                                                                                          |

## 6. Cancellation and timeouts (xUnit v3 specifics)

| xUnit v3                                | MSTest                                                                                                                                                                                                                 |
| --------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TestContext.Current.CancellationToken` | `_testContext.CancellationToken` (MSTest 3.6+; instance `TestContext` from constructor or property injection -- **never** replace with a new `CancellationTokenSource`, that breaks linkage to test-host cancellation) |
| `[Fact(Timeout = 5000)]`                | `[Timeout(5000)]`                                                                                                                                                                                                      |
| `[Fact(Timeout = -1)]` (no timeout)     | Omit `[Timeout]` (MSTest default = no timeout)                                                                                                                                                                         |

xUnit v2 has no equivalent of `TestContext.Current.CancellationToken` -- skip this row for v2 sources.

## 7. Parallelization

| xUnit default                                                                                          | MSTest adaptation                                                                                                                |
| ------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| Default collection mode, with one collection per class: parallel across classes, serial within a class | `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]`                                                        |
| xUnit + `[CollectionBehavior(DisableTestParallelization = true)]`                                      | Omit `[assembly: Parallelize]`                                                                                                   |
| xUnit + `[CollectionBehavior(MaxParallelThreads = N)]`                                                 | `[assembly: Parallelize(Workers = N, Scope = ExecutionScope.ClassLevel)]`                                                        |
| `[Collection("Db")]` in collection mode                                                                | Preserve member serialization; see Section 4 for the conservative `[DoNotParallelize]` option and its broader scheduling effect. |
| `[CollectionDefinition("Db", DisableParallelization = true)]`                                          | Also preserve restricted overlap with other collections; see Section 4.                                                          |

> Do not use `ExecutionScope.MethodLevel` to emulate xUnit collection mode; it allows methods within one class to overlap. Other source parallel modes require explicit adaptation.

### `xunit.runner.json`

Delete `xunit.runner.json` after porting the settings that affect behavior:

| xUnit setting                                                | MSTest adaptation                                                                                                                                                                                                                  |
| ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `"parallelizeAssembly": false`                               | Preserve scheduling across test assemblies in the selected runner or invocation configuration. This does not disable concurrency within an assembly; do not remove `[assembly: Parallelize]` solely because this setting is false. |
| `"parallelizeTestCollections": false`                        | MSTest's serial default; omit `[assembly: Parallelize]`                                                                                                                                                                            |
| `"maxParallelThreads": N`                                    | `[assembly: Parallelize(Workers = N, Scope = ExecutionScope.ClassLevel)]`                                                                                                                                                          |
| `"diagnosticMessages": true`                                 | Use diagnostic CLI output or `.runsettings` verbosity                                                                                                                                                                              |
| `"longRunningTestSeconds": N`                                | Diagnostic threshold only; no direct `[Timeout]` equivalent. Preserve diagnostic monitoring where supported, or report its loss. Do not add a failure timeout unless that behavior change is explicitly intended.                  |
| `"methodDisplay"` / `"preEnumerateTheories"` / `"appDomain"` | No direct equivalent; document the behavior change                                                                                                                                                                                 |

## 8. Assembly-level attributes

xUnit assembly attributes split into two groups: a few have direct MSTest equivalents (and stay at assembly scope); the rest must be removed or reimplemented against MSTest extensibility.

| xUnit                                                            | Disposition                                                                                                                                                                                                                                                |
| ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[assembly: CollectionBehavior(...)]`                            | Remove -- replaced by `[assembly: Parallelize(...)]` (Section 7)                                                                                                                                                                                           |
| `[assembly: TestCaseOrderer(...)]`                               | Remove + reimplement with MSTest extensibility if needed (flag for manual)                                                                                                                                                                                 |
| `[assembly: TestCollectionOrderer(...)]`                         | Remove + flag for manual                                                                                                                                                                                                                                   |
| `[assembly: TestFramework(...)]`                                 | Remove                                                                                                                                                                                                                                                     |
| `[assembly: CaptureConsole]` (xUnit v3)                          | Remove -- MSTest does not capture console by default                                                                                                                                                                                                       |
| `[assembly: Xunit.Trait("Category", "v")]`                       | `[assembly: TestCategory("v")]` (applies the category to every test in the assembly -- `TestCategoryAttribute` targets `Assembly`, `Class`, and `Method`)                                                                                                  |
| `[assembly: Xunit.Trait("Owner", "v")]`                          | Apply the Owner scope rule in Section 1 to affected test methods; no assembly/class `[Owner]` or `TestProperty("Owner", ...)` mapping                                                                                                                      |
| `[assembly: Xunit.Trait("k", "v")]` (neither Category nor Owner) | **No direct equivalent at assembly scope** -- `TestPropertyAttribute` targets only `Class`/`Method`. Either collapse to `[assembly: TestCategory("v")]` if the value alone filters cleanly, or push down to every test class as `[TestProperty("k", "v")]` |

## 9. Packages

**Remove** xUnit references from the migrated projects. For shared `Directory.Build.props` references and `Directory.Packages.props` versions, preserve entries and conditions needed by other projects; remove them only when no remaining consumer needs them. Scope shared-props changes to the migrated projects. Packages to inspect:

- `xunit`, `xunit.abstractions`, `xunit.assert`, `xunit.core`
- `xunit.extensibility.core`, `xunit.extensibility.execution`
- `xunit.runner.visualstudio`
- `xunit.v3`, `xunit.v3.assert`, `xunit.v3.core`, `xunit.v3.extensibility.core`
- `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2`
- `YTest.MTP.XUnit2` (xUnit v2 MTP shim)

**Add** MSTest v4 -- pick exactly one of:

Option A is the metapackage (TestFramework, TestAdapter, Analyzers, and Microsoft.NET.Test.Sdk). Preserve the effective package-management mode. With `ManagePackageVersionsCentrally=true`:

```xml
<!-- Project ItemGroup -->
<PackageReference Include="MSTest" />
<!-- Applicable Directory.Packages.props ItemGroup -->
<PackageVersion Include="MSTest" Version="4.1.0" />
```

Reuse a compatible central item, preserving its conditions and import ownership; do not duplicate or downgrade it. Only for non-CPM projects:

```xml
<PackageReference Include="MSTest" Version="4.1.0" />
```

Remove an older explicit `Microsoft.NET.Test.Sdk` reference or update it to 18.0.1+; keeping 17.x alongside MSTest 4.1.0 causes `NU1605`.

```xml
<!-- Option B: MSTest.Sdk -- defaults to MTP; set <UseVSTest>true</UseVSTest> to preserve VSTest. -->
<!--           UseVSTest pulls in Microsoft.NET.Test.Sdk automatically -- no extra PackageReference needed. -->
<Project Sdk="MSTest.Sdk/4.1.0">
  <PropertyGroup>
    <!-- Keep the project's existing TargetFramework; do not change it during migration. -->
    <UseVSTest>true</UseVSTest> <!-- omit this line to stay on MTP -->
  </PropertyGroup>
</Project>
```

Prefer pinning the `MSTest.Sdk` version in `global.json` (especially in solutions with several test projects) so the version lives in one place:

```json
{
    "msbuild-sdks": {
        "MSTest.Sdk": "4.1.0"
    }
}
```

With the pin in `global.json`, the project line simplifies to `<Project Sdk="MSTest.Sdk">`.

`MSTest.Sdk` adds `Microsoft.VisualStudio.TestTools.UnitTesting` as an **implicit global using**, so:

- **Do not** add `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` to the project file -- it's redundant noise.
- **Do not** add `using Microsoft.VisualStudio.TestTools.UnitTesting;` to each test file -- it's already in scope.

(Option A -- the `MSTest` metapackage -- does not bring the global using; per-file `using Microsoft.VisualStudio.TestTools.UnitTesting;` is still required there.)

## 10. Companion / extension libraries

| xUnit companion                                                        | MSTest equivalent                                                                                                                                                                                                                                        |
| ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Xunit.SkippableFact` (`[SkippableFact]`, `Skip.If`, `Skip.IfNot`)     | `[Ignore]` (compile-time) or `Assert.Inconclusive("reason")` (runtime). Remove the package                                                                                                                                                               |
| `Xunit.Combinatorial` (`[CombinatorialData]`, `[CombinatorialValues]`) | [`Combinatorial.MSTest`](https://github.com/Youssef1313/Combinatorial.MSTest) (community port) -- attribute surface is the same as xUnit.Combinatorial. Alternatively, expand combinations into explicit `[DataRow]`s or compute them in `[DynamicData]` |
| `Xunit.StaFact` (`[StaFact]`, `[WpfFact]`)                             | No equivalent -- manual STA thread or flag for review                                                                                                                                                                                                    |
| `Xunit.Priority` (`[TestCaseOrderer]`)                                 | MSTest ordering is different -- flag for manual                                                                                                                                                                                                          |
| `Verify.Xunit`                                                         | `Verify.MSTest` (swap the package; same usage)                                                                                                                                                                                                           |
| `FluentAssertions` / `Shouldly` / `AwesomeAssertions`                  | Keep -- assertion libraries are framework-agnostic.                                                                                                                                                                                                      |
| `Moq` / `NSubstitute` / `FakeItEasy`                                   | Keep -- mocking libraries are framework-agnostic                                                                                                                                                                                                         |
| `AutoFixture.Xunit2` (`[AutoData]`)                                    | `AutoFixture` core works, but the auto-data attribute integration requires the xUnit-specific package -- flag for manual                                                                                                                                 |

Source attribution and the MIT notice are retained in the [skill](../SKILL.md#source-and-license).
