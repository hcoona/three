# MicrosoftExtensions.Options.DedupChangeExtensions

[![NuGet](https://img.shields.io/nuget/v/MicrosoftExtensions.Options.DedupChangeExtensions.svg)](https://www.nuget.org/packages/MicrosoftExtensions.Options.DedupChangeExtensions/)

This library provides extension methods for `IOptionsMonitor<T>` to prevent duplicate change notifications. It solves the common problem where configuration change callbacks are fired multiple times for the same logical configuration change, which can lead to unnecessary processing and performance issues.

## Problem Statement

When using `IOptionsMonitor<T>` in ASP.NET Core applications, change callbacks are often triggered multiple times in quick succession for a single configuration file change. This happens because:

1. File system watchers can fire multiple events for a single file modification
2. Text editors may perform multiple write operations when saving files
3. The underlying `ChangeToken.OnChange` mechanism doesn't deduplicate notifications

This issue is well-documented in [ASP.NET Core issue #2542](https://github.com/aspnet/Home/issues/2542), where developers reported seeing their change handlers called twice or more for each configuration update.

## Solution

This library implements **hash-based deduplication** to ensure your change callbacks are only invoked when the configuration values have actually changed. It works by:

1. Computing a hash of the serialized options object when the callback is first registered
2. Computing a new hash each time a change notification occurs
3. Only invoking your callback if the hash values differ
4. Updating the stored hash for future comparisons

## Installation

Install the package via NuGet:

```bash
dotnet add package MicrosoftExtensions.Options.DedupChangeExtensions
```

Or via Package Manager Console:

```powershell
Install-Package MicrosoftExtensions.Options.DedupChangeExtensions
```

## Usage

### Basic Usage (Default Options)

```csharp
using Microsoft.Extensions.Options;

public class MyService
{
    private readonly IDisposable _changeSubscription;

    public MyService(IOptionsMonitor<MyOptions> optionsMonitor)
    {
        // Register for deduplicated change notifications
        _changeSubscription = optionsMonitor.OnChangeDedup(options =>
        {
            // This callback will only fire when MyOptions actually changes
            Console.WriteLine("Configuration changed!");
            HandleConfigurationChange(options);
        });
    }

    private void HandleConfigurationChange(MyOptions options)
    {
        // Your configuration change logic here
    }

    public void Dispose()
    {
        _changeSubscription?.Dispose();
    }
}
```

### Named Options

```csharp
public class MyService
{
    private readonly IDisposable _changeSubscription;

    public MyService(IOptionsMonitor<DatabaseOptions> optionsMonitor)
    {
        // Monitor a specific named options instance
        _changeSubscription = optionsMonitor.OnChangeDedup("ProductionDB", (options, name) =>
        {
            Console.WriteLine($"Configuration '{name}' changed!");
            ReconfigureDatabase(options);
        });
    }

    private void ReconfigureDatabase(DatabaseOptions options)
    {
        // Reconfigure your database connection
    }

    public void Dispose()
    {
        _changeSubscription?.Dispose();
    }
}
```

### Dependency Injection Setup

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Configure your options as usual
        services.Configure<MyOptions>(Configuration.GetSection("MySection"));

        // Register your service that uses deduplicated change notifications
        services.AddSingleton<MyService>();
    }
}
```

## How It Works

The deduplication mechanism uses the following approach:

1. **Serialization**: Objects are serialized using `BinaryFormatter` to capture their complete state
2. **Hashing**: The serialized data is hashed using SHA-1 to create a compact fingerprint
3. **Comparison**: Hash values are compared using constant-time comparison to detect changes
4. **Thread Safety**: Uses `Interlocked.Exchange` for thread-safe hash updates

### Hash Calculation Process

```csharp
// Simplified version of the internal process
object options = optionsMonitor.Get(name);
byte[] serializedData = BinaryFormatter.Serialize(options);
byte[] hash = SHA1.ComputeHash(serializedData);

// Compare with previous hash
if (!previousHash.SequenceEqual(hash))
{
    // Invoke your callback
    listener(options, name);
    previousHash = hash;
}
```

## API Reference

### Extension Methods

#### `OnChangeDedup<TOptions>(Action<TOptions> listener)`

Registers a change callback for the default named options instance that only fires when values change.

**Parameters:**

- `listener`: The callback to invoke when options change

**Returns:** `IDisposable` to unregister the callback

#### `OnChangeDedup<TOptions>(string name, Action<TOptions, string> listener)`

Registers a change callback for a specific named options instance that only fires when values change.

**Parameters:**

- `name`: The name of the options instance to monitor
- `listener`: The callback to invoke when options change

**Returns:** `IDisposable` to unregister the callback

## Performance Considerations

- **Serialization Overhead**: The library uses `BinaryFormatter` for serialization, which has some overhead. This is typically negligible compared to configuration reload operations.
- **Memory Usage**: Hash values (20 bytes for SHA-1) are stored per monitored options instance.
- **Thread Safety**: All operations are thread-safe and use efficient atomic operations.

## Compatibility

This library supports the following target frameworks:

- .NET Standard 2.0 (for broad compatibility)
- .NET Standard 2.1
- .NET Framework 4.6.2
- .NET 8.0
- .NET 9.0

It's compatible with:

- ASP.NET Core 2.0+
- .NET Framework applications using Microsoft.Extensions.Options
- Any application using the Microsoft.Extensions.Options package

## Thread Safety

All methods in this library are thread-safe and can be called concurrently from multiple threads. The internal hash storage uses atomic operations to ensure consistency.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception. See the [LICENSE](https://github.com/hcoona/three/blob/main/LICENSES/LGPL-3.0-linking-exception.txt) for details.

## Related Issues

- [ASP.NET Core issue #2542](https://github.com/aspnet/Home/issues/2542) - Original discussion about duplicate change notifications
- [Configuration issue #624](https://github.com/aspnet/Configuration/issues/624) - Related configuration reload issues

## Alternatives

If this library doesn't meet your needs, consider these alternatives:

1. **Delay-based approach**: Add a delay before processing changes (as suggested in the original issue)
2. **Manual deduplication**: Implement your own hash-based or timestamp-based deduplication
3. **Framework solutions**: Wait for potential framework-level solutions in future .NET releases
