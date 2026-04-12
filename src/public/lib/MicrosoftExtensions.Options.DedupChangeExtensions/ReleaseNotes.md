# Release Notes

## Version 1.2.1

- Relicense: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 1.1.1

### New Features

- **Multi-Target Framework Support**: Added support for .NET 8.0 and .NET 9.0 alongside existing frameworks
- **Enhanced Type Safety**: Added nullable reference types support for .NET Standard 2.1+ and .NET Core targets
- **Comprehensive Documentation**: Added extensive XML documentation with detailed examples and security considerations

### Improvements

- **Modern C# Features**: Leveraged nullable annotations for better compile-time safety on supported frameworks
- **Performance Optimizations**: Improved memory usage patterns with `MemoryExtensions.SequenceEqual` for hash comparison on modern frameworks
- **Security Enhancements**:
  - Added proper warning suppression for SYSLIB0011 (BinaryFormatter security warning)
  - Enabled `EnableUnsafeBinaryFormatterSerialization` for .NET 9.0 with proper safeguards
  - Enhanced null handling with defensive programming practices

### Framework Compatibility

- .NET Standard 2.0 (unchanged for broad compatibility)
- .NET Standard 2.1 (with nullable support)
- .NET Framework 4.6.2 (with System.ValueTuple dependency)
- .NET 8.0 (with modern APIs and optimizations)
- .NET 9.0 (with latest features and security configurations)

### Technical Improvements

- **Thread Safety**: Enhanced thread-local storage patterns for better concurrency
- **Hash Algorithm**: Continued use of SHA1 for hash-based deduplication with optimized implementation
- **Error Handling**: Improved null reference handling across all target frameworks
- **Code Quality**: Enhanced code documentation and inline comments for maintainability

### Migration Path

This version maintains full backward compatibility with previous releases while adding modern framework support. No breaking changes for existing users.

---

## Version 1.0.2 (2018-01-18)

### Bug Fixes

- Minor stability improvements and package metadata updates

---

## Version 1.0.1 (2017-2018)

### Bug Fixes & Improvements

- Improved reliability of hash-based comparison
- Enhanced thread safety for concurrent scenarios

---

## Version 1.0.0 (2017)

### Initial Release

- **Core Functionality**: Implemented hash-based deduplication for `IOptionsMonitor<T>` change callbacks
- **Problem Solved**: Addressed the issue documented in [ASP.NET Core issue #2542](https://github.com/aspnet/Home/issues/2542) where configuration change callbacks were fired multiple times for the same logical configuration change
- **Hash-Based Detection**: Used `BinaryFormatter` serialization followed by `SHA1` hashing for reliable change detection
- **Thread Safety**: Implemented thread-safe hash token comparison using `Interlocked.Exchange`
- **API Design**: Provided both named and default options monitoring with clean extension method API

### Key Features

- `OnChangeDedup<TOptions>()` extension methods for `IOptionsMonitor<T>`
- Support for both named options instances and default options
- Automatic deduplication prevents unnecessary callback invocations
- Thread-safe implementation suitable for high-concurrency scenarios
- Minimal performance overhead with efficient hash-based comparison

### Target Framework

- .NET Standard 2.0 for broad compatibility with .NET Framework and .NET Core

### Dependencies

- Microsoft.Extensions.Options (2.0.0+)

---

## Project History

### 2022-08-17: Project Migration

- Moved from standalone repository [hcoona/MicrosoftExtensions.Options.DedupChangeExtensions](https://github.com/hcoona/MicrosoftExtensions.Options.DedupChangeExtensions) to [OneDotNet monorepo](https://github.com/hcoona/OneDotNet)
- Integrated into unified build and packaging system
- Maintained full backward compatibility during migration

### Original Problem Statement

This library was created to solve a common issue in ASP.NET Core applications where `IOptionsMonitor<T>` change callbacks would be triggered multiple times in quick succession for a single configuration file change. This happened because:

1. File system watchers can fire multiple events for a single file modification
2. Text editors may perform multiple write operations when saving files
3. The underlying `ChangeToken.OnChange` mechanism doesn't deduplicate notifications

### Solution Approach

The library implements **hash-based deduplication** by:

1. Computing a hash of the serialized options object when the callback is first registered
2. Computing a new hash each time a change notification occurs
3. Only invoking the user callback if the hash values differ
4. Updating the stored hash for future comparisons

This ensures that duplicate notifications are filtered out while preserving legitimate configuration changes.

---

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](https://www.gnu.org/licenses/gpl-3.0-standalone.html) for details.

## Contributing

This project is part of the OneDotNet ecosystem. For contributions, please refer to the main repository guidelines.
