# MicrosoftExtensions.Logging.Xunit

A high-performance xUnit integration library for Microsoft.Extensions.Logging, enabling seamless log output capture in unit tests. Compatible with .NET Framework 4.6.2+, .NET Core 2.0+, .NET 6/8/9+, and modern .NET platforms.

## Features

- Microsoft Extensions Logging compatible `ILogger` implementation for xUnit tests
- Forwards log messages to xUnit's `ITestOutputHelper` for test output visibility
- Multi-targeting: `netstandard2.0`, `netstandard2.1`, `net462`, `net8.0`, `net9.0`
- Full log level support with formatted output
- Exception logging with stack traces
- Thread-safe logger implementation
- Easy integration with existing logging infrastructure

## Installation

Install from NuGet:

```shell
# .NET CLI
dotnet add package Microsoft.Extensions.Logging.Xunit

# Package Manager Console
Install-Package Microsoft.Extensions.Logging.Xunit
```

## Quick Start

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Xunit;
using Xunit;
using Xunit.Abstractions;

public class UnitTest1
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly ILoggerFactory loggerFactory;

    public UnitTest1(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        this.loggerFactory = new LoggerFactory(new[] { new XunitLoggerProvider(testOutputHelper) });
    }

    [Fact]
    public void Test1()
    {
        var logger = loggerFactory.CreateLogger("Test1");
        logger.LogInformation("Hello World!");

        var typedLogger = loggerFactory.CreateLogger<UnitTest1>();
        typedLogger.LogInformation("Hello from typed logger!");

        // Log with structured data
        logger.LogWarning("Processing {ItemCount} items", 42);

        // Log exceptions
        try
        {
            throw new InvalidOperationException("Test exception");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during processing");
        }
    }
}
```

## Advanced Usage

### Using with Dependency Injection

```csharp
public class ServiceTest
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly ServiceProvider serviceProvider;

    public ServiceTest(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(testOutputHelper));
        });
        services.AddTransient<MyService>();

        this.serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void TestService()
    {
        var service = serviceProvider.GetRequiredService<MyService>();
        service.DoWork(); // Logs will appear in test output
    }
}
```

### Custom Log Formatting

The logger automatically formats messages with log level, category, and timestamp information for clear test output readability.

## Example Output

When running tests, log messages appear in the test output:

```text
info: Test1[0]
      Hello World!
warn: Test1[0]
      Processing 42 items
fail: Test1[0]
      An error occurred during processing
      System.InvalidOperationException: Test exception
         at UnitTest1.Test1() in C:\Example\UnitTest1.cs:line 25
```

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

---

Feedback and contributions are welcome! Open an issue or PR.
