# Memoization.Net

A high-performance memoization library for .NET that provides automatic function result caching to improve performance by avoiding redundant computations.

[![NuGet](https://img.shields.io/nuget/v/Memoization.Net.svg)](https://www.nuget.org/packages/Memoization.Net/)
[![License: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception](https://img.shields.io/badge/License-LGPL--3.0--or--later%20WITH%20LGPL--3.0--linking--exception-blue.svg)](https://github.com/hcoona/three/blob/main/LICENSE.LGPLv3-linking-exception.txt)

## Overview

[Memoization](https://en.wikipedia.org/wiki/Memoization) is an optimization technique that stores the results of expensive function calls and returns the cached result when the same inputs occur again. This library provides a simple and efficient way to memoize functions in C# using Microsoft's `IMemoryCache`.

## Features

- **High Performance**: Built on top of `Microsoft.Extensions.Caching.Abstractions` for optimal performance
- **Generic Support**: Supports functions with up to 16 parameters
- **Flexible Caching**: Use global default cache or specify per-function cache instances
- **Cache Options**: Full support for `MemoryCacheEntryOptions` (TTL, size limits, etc.)
- **Exception Safety**: Failed computations don't pollute the cache
- **Thread Safe**: Operations are thread-safe when using thread-safe `IMemoryCache` implementations
- **Multi-Target**: Supports .NET Standard 2.0/2.1, .NET Framework 4.6.2, .NET 8.0, and .NET 9.0

## Installation

```bash
dotnet add package Memoization.Net
```

## Quick Start

### 1. Initialize Default Cache

```csharp
using Microsoft.Extensions.Caching.Memory;
using Memoization;

// Set up the default cache (typically done once at application startup)
Memoization.DefaultCache = new MemoryCache(new MemoryCacheOptions
{
    SizeLimit = 1000,
    CompactionPercentage = 0.25
});
```

### 2. Create Memoized Functions

```csharp
// Example: Expensive recursive function
static int Fibonacci(int n)
{
    Console.WriteLine($"Computing Fibonacci({n})");
    return n < 2 ? 1 : Fibonacci(n - 1) + Fibonacci(n - 2);
}

// Create memoized version
var memoizedFib = Memoization.Create<int, int>(Fibonacci);

// First call - computes and caches result
var result1 = memoizedFib(10); // Prints computation messages

// Second call - returns cached result instantly
var result2 = memoizedFib(10); // No computation, returns cached result
```

## Advanced Usage

### Custom Cache Instance

```csharp
// Use a specific cache instance
var customCache = new MemoryCache(new MemoryCacheOptions());
var memoizedFunc = Memoization.Create(expensiveFunction, customCache);
```

### Cache Options

```csharp
// Configure cache entry options
var options = new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
    SlidingExpiration = TimeSpan.FromMinutes(5),
    Priority = CacheItemPriority.High
};

var memoizedFunc = Memoization.Create(expensiveFunction, options);
```

### Multi-Parameter Functions

```csharp
// Function with multiple parameters
static double ExpensiveCalculation(double x, double y, int iterations)
{
    // Simulate expensive computation
    return Math.Pow(x + y, iterations % 10);
}

var memoized = Memoization.Create<double, double, int, double>(ExpensiveCalculation);
var result = memoized(3.14, 2.71, 1000);
```

## Performance Considerations

### Cache Key Generation

The library uses tuples of input parameters as cache keys. For best performance:

- Ensure parameter types implement efficient `GetHashCode()` and `Equals()` methods
- Avoid using mutable objects as parameters
- Consider the memory overhead of storing keys and values

### Memory Management

```csharp
// Configure cache with size limits to prevent memory issues
var options = new MemoryCacheOptions
{
    SizeLimit = 1000,
    CompactionPercentage = 0.20
};

var cache = new MemoryCache(options);
```

### Exception Handling

Failed function calls do not pollute the cache:

```csharp
var memoized = Memoization.Create<int, string>(x =>
{
    if (x < 0) throw new ArgumentException("Negative input");
    return x.ToString();
});

try { memoized(-1); } catch { /* Exception thrown, nothing cached */ }
var result = memoized(5); // Will compute and cache successfully
```

## Thread Safety

All memoization operations depend on the thread safety of the underlying `IMemoryCache` implementation. Since this library uses `Microsoft.Extensions.Caching.Abstractions`, the thread safety depends on the specific `IMemoryCache` implementation being used. The default `Microsoft.Extensions.Caching.Memory` implementation is thread-safe, so multiple threads can safely call memoized functions concurrently:

```csharp
var memoized = Memoization.Create(expensiveFunction);

// Safe to call from multiple threads (with thread-safe IMemoryCache implementation)
Parallel.For(0, 100, i =>
{
    var result = memoized(i % 10); // Concurrent access is safe
});
```

**Note**: While cache operations are typically thread-safe, the memoized function itself should be thread-safe (or pure) to avoid issues with concurrent execution. Additionally, when the same inputs are being processed concurrently by multiple threads, the expensive function may be executed multiple times before the result is cached.

## Best Practices

1. **Initialize Once**: Set up `DefaultCache` once during application startup
2. **Pure Functions**: Only memoize pure functions (same input always produces same output)
3. **Immutable Parameters**: Use immutable types as function parameters when possible
4. **Cache Sizing**: Configure appropriate cache size limits to prevent memory leaks
5. **Monitoring**: Monitor cache hit ratios and memory usage in production

## Examples

### Database Query Caching

```csharp
// Cache expensive database queries
var cachedQuery = Memoization.Create<int, User>(userId =>
{
    return database.Users.FirstOrDefault(u => u.Id == userId);
}, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
});
```

### Computational Results

```csharp
// Cache complex mathematical computations
var cachedCompute = Memoization.Create<double[], double>(weights =>
{
    return weights.Select((w, i) => w * Math.Pow(i, 2)).Sum();
});
```

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See [LICENSE](https://github.com/hcoona/three/blob/main/LICENSE.LGPLv3-linking-exception.txt) for details.

## Related Projects

- [Microsoft.Extensions.Caching.Memory](https://github.com/dotnet/extensions) - The underlying caching implementation
- [Memoization (Wikipedia)](https://en.wikipedia.org/wiki/Memoization) - Background on the memoization technique
