# Release Notes

## Version 1.1.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 1.0.1

### Features

- **MSTest Integration**: Seamless integration with MSTest framework for logging output capture
- **ILogger Implementation**: Full Microsoft.Extensions.Logging compatible ILogger implementation
- **TestContext Support**: Forwards log messages to MSTest's TestContext for test output visibility
- **Multi-Target Framework Support**: Compatible with netstandard2.0, netstandard2.1, net462, net8.0, and net9.0
- **Thread-Safe Logging**: Thread-safe logger implementation for parallel test execution
- **Structured Logging**: Support for structured logging with parameters and formatting
- **Exception Logging**: Complete exception logging with stack traces
- **Log Level Support**: Full support for all Microsoft.Extensions.Logging log levels (Trace, Debug, Information, Warning, Error, Critical)

### Benefits

- **Easy Debugging**: Log messages appear directly in MSTest test output for easy debugging
- **No Configuration Required**: Works out-of-the-box with minimal setup
- **Performance Optimized**: High-performance implementation designed for test scenarios
- **Enterprise Ready**: Suitable for enterprise-grade testing scenarios with comprehensive logging needs
- **Developer Friendly**: Simple API that integrates seamlessly with existing logging infrastructure

### Technical Details

- **Dependencies**:
  - Microsoft.Extensions.Logging.Abstractions 2.1.0+
  - MSTest.TestFramework 2.2.1+
- **Package ID**: IO.Github.Hcoona.MicrosoftExtensions.Logging.MSTest
- **License**: GPL-3.0-or-later
- **Documentation**: Comprehensive README with examples and best practices

### Usage Examples

- Basic logging with ILogger
- Dependency injection integration
- Base test class patterns for reusability
- Structured logging with parameters
- Exception handling and logging

### Compatibility

- .NET Framework 4.6.2+
- .NET Core 2.0+
- .NET 6/8/9+
- All modern .NET platforms
- Visual Studio Test Explorer
- Azure DevOps Test Results
- GitHub Actions and other CI/CD platforms
