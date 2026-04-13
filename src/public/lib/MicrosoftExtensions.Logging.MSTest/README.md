# IO.Github.Hcoona.MicrosoftExtensions.Logging.MSTest

A high-performance MSTest integration library for Microsoft.Extensions.Logging, enabling seamless log output capture in unit tests. Compatible with .NET Framework 4.6.2+, .NET Core 2.0+, .NET 6/8/9+, and modern .NET platforms.

## Features

- Microsoft Extensions Logging compatible `ILogger` implementation for MSTest tests
- Forwards log messages to MSTest's `TestContext` for test output visibility
- Multi-targeting: `netstandard2.0`, `netstandard2.1`, `net462`, `net8.0`, `net9.0`
- Full log level support with formatted output
- Exception logging with stack traces
- Thread-safe logger implementation
- Easy integration with existing logging infrastructure

## Installation

Install from NuGet:

```shell
# .NET CLI
dotnet add package IO.Github.Hcoona.MicrosoftExtensions.Logging.MSTest

# Package Manager Console
Install-Package IO.Github.Hcoona.MicrosoftExtensions.Logging.MSTest
```

## Quick Start

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class UnitTest1
{
    public TestContext TestContext { get; set; }
    private ILoggerFactory loggerFactory;

    [TestInitialize]
    public void TestInitialize()
    {
        this.loggerFactory = new LoggerFactory(new[] { new MSTestLoggerProvider(TestContext) });
    }

    [TestCleanup]
    public void TestCleanup()
    {
        this.loggerFactory?.Dispose();
    }

    [TestMethod]
    public void TestMethod1()
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
[TestClass]
public class ServiceTest
{
    public TestContext TestContext { get; set; }
    private ServiceProvider serviceProvider;

    [TestInitialize]
    public void TestInitialize()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(new MSTestLoggerProvider(TestContext));
        });
        services.AddTransient<MyService>();

        this.serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        this.serviceProvider?.Dispose();
    }

    [TestMethod]
    public void TestService()
    {
        var service = serviceProvider.GetRequiredService<MyService>();
        service.DoWork(); // Logs will appear in test output
    }
}
```

### Base Test Class Pattern

For better reusability, you can create a base test class:

```csharp
[TestClass]
public abstract class BaseTest
{
    public TestContext TestContext { get; set; }
    protected ILoggerFactory LoggerFactory { get; private set; }

    [TestInitialize]
    public virtual void TestInitialize()
    {
        LoggerFactory = new LoggerFactory(new[] { new MSTestLoggerProvider(TestContext) });
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        LoggerFactory?.Dispose();
    }

    protected ILogger<T> CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
    protected ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);
}

[TestClass]
public class MyServiceTest : BaseTest
{
    [TestMethod]
    public void TestMyService()
    {
        var logger = CreateLogger<MyServiceTest>();
        logger.LogInformation("Testing my service...");
        // Your test logic here
    }
}
```

### Custom Log Formatting

The logger automatically formats messages with log level, category, and timestamp information for clear test output readability.

## Example Output

When running tests, log messages appear in the MSTest test output:

```text
info: Test1[0]
      Hello World!
warn: Test1[0]
      Processing 42 items
fail: Test1[0]
      An error occurred during processing
      System.InvalidOperationException: Test exception
         at UnitTest1.TestMethod1() in C:\Example\UnitTest1.cs:line 35
```

## Comparison with Other Test Frameworks

| Feature              | MSTest          | xUnit                    | NUnit           |
| -------------------- | --------------- | ------------------------ | --------------- |
| Test Context         | ✅ TestContext  | ✅ ITestOutputHelper     | ✅ TestContext  |
| Dependency Injection | ✅ Manual setup | ✅ Constructor injection | ✅ Manual setup |
| Parallel Tests       | ✅ Supported    | ✅ Supported             | ✅ Supported    |
| .NET Core Support    | ✅ Full support | ✅ Full support          | ✅ Full support |

## Requirements

- .NET Framework 4.6.2+ or .NET Core 2.0+ or .NET 6/8/9+
- MSTest.TestFramework 2.2.1+
- Microsoft.Extensions.Logging.Abstractions 2.1.0+

## License

LGPL-3.0-or-later WITH LGPL-3.0-linking-exception

---

Feedback and contributions are welcome! Open an issue or PR on [GitHub](https://github.com/hcoona/three).
