# Release Notes

## Version 2.1.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 2.0.2

### Features

- Initial release of the Phi Failure Detector implementation
- Support for .NET Standard 2.0, 2.1, .NET Framework 4.6.2, .NET 8.0, and .NET 9.0
- Adaptive failure detection algorithm based on statistical analysis
- Configurable phi thresholds for different reliability requirements
- High-performance implementation optimized for distributed systems
- Comprehensive unit tests and documentation

### API

- `PhiFailureDetector` class for failure detection
- `Heartbeat()` method to record node activity
- `IsAvailable()` method to check node availability
- `Phi()` method to get current suspicion level
- `IWithStatistics` interface for statistical monitoring

### Dependencies

- Microsoft.Bcl.TimeProvider is required only for .NET Standard 2.0, .NET Standard 2.1, and .NET Framework 4.6.2. For .NET 8.0 and .NET 9.0, TimeProvider is built into the runtime and no package reference is needed.

### Documentation

- Complete API documentation
- Usage examples and best practices
- Algorithm explanation and references
