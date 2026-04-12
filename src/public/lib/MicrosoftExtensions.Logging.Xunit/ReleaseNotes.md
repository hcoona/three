# Release Notes

## Version 1.2.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 1.1.1

### Features

- **Core**: High-performance xUnit integration for Microsoft.Extensions.Logging
- **Multi-framework**: Support for .NET Standard 2.0/2.1, .NET Framework 4.6.2, .NET 8.0, and .NET 9.0
- **Thread-safe**: Thread-safe logger implementation for concurrent test execution
- **Full logging support**: Complete implementation of ILogger interface with all log levels
- **Exception handling**: Comprehensive exception logging with stack traces
- **Structured logging**: Support for message templates and structured data
- **Easy integration**: Simple setup with ITestOutputHelper for immediate test output visibility

### Core Components

- **XunitLoggerProvider**: Creates logger instances that output to xUnit test output
- **XunitLogger**: Full-featured ILogger implementation with formatted output
- **Thread-safe operations**: Safe for use in parallel test execution scenarios
- **Memory-efficient**: Optimized string building and message formatting

### API Features

- Complete ILogger interface implementation
- Support for all log levels (Trace, Debug, Information, Warning, Error, Critical)
- Structured logging with message templates
- Exception logging with full stack trace output
- Category-based logger creation (string and generic type-based)
- BeginScope support (delegates to NullLogger)
- Always-enabled logging (IsEnabled always returns true)

### Compatibility

- **Target Frameworks**: netstandard2.0, netstandard2.1, net462, net8.0, net9.0
- **Dependencies**:
  - Microsoft.Extensions.Logging.Abstractions
  - xunit.abstractions
- **Nullable Reference Types**: Enabled for modern .NET versions (net8.0, net9.0, netstandard2.1)
- **Documentation**: Comprehensive XML documentation for all public APIs

### Performance

- Efficient string building using ThreadStatic StringBuilder
- Minimal allocation overhead for log message formatting
- Zero-overhead when test output is not captured
- Optimized for high-frequency logging scenarios in tests

### Usage Scenarios

- Unit testing with logging output capture
- Integration testing with service dependencies
- Debugging complex test scenarios
- Capturing application logs during test execution
- Testing logging-dependent components and services

## Future Enhancements

- Enhanced formatting options and customization
- Log filtering and level configuration support
- Performance improvements for high-volume logging
- Additional output formatting options
