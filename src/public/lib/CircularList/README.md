# CircularList

A high-performance, generic circular buffer (circular list) library for .NET, supporting multiple target frameworks. Compatible with .NET Framework 4.6.1+, .NET Core 2.0+, .NET 6/8/9+, and modern .NET platforms.

## Features

- Fixed positive capacity (`capacity >= 1`), automatically overwriting the oldest element on overflow
- Generic, implementing `IList<T>` and `IReadOnlyList<T>`
- Multi-targeting: `netstandard2.0`, `netstandard2.1`, `net462`, `net8.0`, `net9.0`
- Zero-allocation dual-segment `GetSegments()` and `CopyTo(Span<T>)`, plus `AsSpan()` contiguous view/snapshot
- Overflow event notification (`OnOverflow`) invoked after ring state transitions complete
- Value-type enumerator with modification-version (`InvalidOperationException`) checks
- Not thread-safe (add external synchronization for concurrent callers)

## Installation

Install from NuGet:

```shell
dotnet add package IO.Github.Hcoona.Collections.CircularList
```

## Quick Start

```csharp
using IO.Github.Hcoona.Collections;

var list = new CircularList<int>(capacity: 3);
list.OnOverflow += (sender, overwritten) =>
{
    Console.WriteLine($"Overwritten: {overwritten}");
};

list.Add(1);
list.Add(2);
list.Add(3);
list.Add(4); // Overwrites 1; list now contains [2, 3, 4]

foreach (var item in list)
{
    Console.WriteLine(item); // Outputs 2, 3, 4
}

var (first, second) = list.GetSegments();
```

## Main API

- `CircularList<T>(int capacity)`: Create a circular list with `capacity >= 1` (`ArgumentOutOfRangeException` if `capacity <= 0`)
- `void Add(T item)`: Append an element, or overwrite the oldest element when full and raise `OnOverflow`
- `T this[int index]`: Logical indexer (`0` is the oldest element; `set` updates in place and invalidates active enumerators)
- `int Capacity` / `int Count`: Fixed capacity and current element count (`0 <= Count <= Capacity`)
- `event EventHandler<T>? OnOverflow`: Raised after an overwrite completes with the evicted element
- `CircularList<T>.Segments GetSegments()`: Zero-allocation `(First, Second)` `ReadOnlySpan<T>` view over internal storage (invalidated on mutation)
- `ReadOnlySpan<T> AsSpan()`: Zero-allocation view when contiguous in the backing array, or an allocated contiguous snapshot when wrapped
- `void CopyTo(Span<T> destination)` / `void CopyTo(T[] array, int arrayIndex)`: Copy active elements in logical order
- `bool Contains(T item)` / `int IndexOf(T item)`: Search active elements in logical order using `EqualityComparer<T>.Default`
- `void Clear()`: Reset count and release element references in cleared slots
- `CircularList<T>.Enumerator GetEnumerator()`: Value-type enumerator that throws `InvalidOperationException` if the collection is mutated during enumeration

## Verification

Contract and invariant verification coverage is recorded in [`docs/validation.md`](docs/validation.md).

## Multi-target Framework Support

| Target Framework | Compatible Platforms  | Modern Features       |
| ---------------- | --------------------- | --------------------- |
| netstandard2.0   | .NET Framework 4.6.1+ | Basic functionality   |
| netstandard2.1   | .NET Core 3.0+/Mono   | Span&lt;T&gt; support |
| net462           | .NET Framework 4.6.2+ | Basic functionality   |
| net8.0           | .NET 8                | All features          |
| net9.0           | .NET 9                | All features          |

> **Note:** On netstandard2.0, System.Memory is referenced automatically to provide Span&lt;T&gt; compatible APIs.

## Performance & Use Cases

- Ideal for logs, caches, sliding windows, and any fixed-capacity, cyclic-overwrite scenario
- Backed by an array, O(1) for indexing and adding
- Remove/RemoveAt/Insert operations are not supported (throw NotSupportedException)
- CopyTo/IndexOf/Contains are fully supported for read operations

## Releasing

Workflow Delivery releases the `IO.Github.Hcoona.Collections.CircularList` package
through the manually dispatched
`.github/workflows/workflow-delivery-v3-nuget-release.yml` caller with `unit:
circular-list`, as declared by `workflow-delivery.release-unit.yml`. Use `registry:
github-packages` for GitHub Packages (Buddy) from any branch, or `registry: nugetorg`
for nuget.org (Official) from `main` only. The package version is the NBGV projection of
this directory's `version.json` at the selected revision. Preparation packs every target
framework, checks the native package contents and consumes the original package in a
clean project before any publication.

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

---

Feedback and contributions are welcome! Open an issue or PR.
