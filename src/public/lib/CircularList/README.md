# CircularList

A high-performance, generic circular buffer (circular list) library for .NET, supporting multiple target frameworks. Compatible with .NET Framework 4.6.1+, .NET Core 2.0+, .NET 6/8/9+, and modern .NET platforms.

## Features

- Fixed capacity, automatically overwrites the oldest element on overflow
- Generic, fully implements `IList<T>` API
- Multi-targeting: `netstandard2.0`, `netstandard2.1`, `net462`, `net8.0`, `net9.0`
- `Span<T>`/`Memory<T>` support on modern platforms
- Overflow event notification when an element is overwritten
- Not thread-safe (add your own locking for concurrency)

## Installation

Install from NuGet (assuming package name is CircularList):

```shell
# .NET CLI
dotnet add package CircularList
```

## Quick Start

```csharp
using IO.Github.Hcoona.Collections;

var list = new CircularList<int>(capacity: 3);
list.Add(1);
list.Add(2);
list.Add(3);
list.Add(4); // Now 1 is overwritten, list contains [2,3,4]

foreach (var item in list)
{
    Console.WriteLine(item); // Outputs 2, 3, 4
}

// Subscribe to overflow event
list.OnOverflow += (sender, overwritten) =>
{
    Console.WriteLine($"Overwritten: {overwritten}");
};
```

## Main API

- `CircularList<T>(int capacity)`: Create a circular list with the specified capacity
- `void Add(T item)`: Add an element, overwriting the oldest if full
- `T this[int index]`: Indexer, 0 is the oldest element
- `int Capacity`: The fixed capacity
- `int Count`: Current number of elements
- `event EventHandler<T> OnOverflow`: Raised when an element is overwritten
- `ReadOnlySpan<T> AsSpan()`: Efficient access to the circular list contents
- `bool Contains(T item)`: Check if the list contains a specific item
- `int IndexOf(T item)`: Find the logical index of a specific item
- `void Clear()`: Remove all items from the list
- `void CopyTo(T[] array, int arrayIndex)`: Copy all items to an array

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

Before the first GitHub Packages release, the owner bootstraps the package once as described in [NuGet Release integration](../../../private/app/workflow-delivery/docs/integration/dotnet.md#concrete-nuget-release-integration).

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

---

Feedback and contributions are welcome! Open an issue or PR.
