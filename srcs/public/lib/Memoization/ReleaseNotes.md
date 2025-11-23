# Release Notes

## Version 1.2.1

- **Chore**: Replace T4 templates with source generators.
- **Relicense**: Re-licensed NuGet package to LGPL-3.0-or-later WITH LGPL-3.0-linking-exception.

## Version 1.1.1

- **Breaking**: Multi-framework targeting - now supports .NET Standard 2.0/2.1, .NET Framework 4.6.2, .NET 8.0, and .NET 9.0
- **Feature**: Added nullable reference types support for modern .NET versions
- **Improvement**: Enhanced package metadata with better description and tags
- **Improvement**: Added comprehensive documentation generation
- **Improvement**: Improved NuGet package configuration following modern best practices

## Version 1.1.0-beta.1 (August 17, 2022)

- **Feature**: Migrated project to OneDotNet monorepo structure
- **Improvement**: Updated build configuration and dependency management
- **Improvement**: Enhanced package versioning with Nerdbank.GitVersioning
- **Fix**: Updated dependencies and resolved compatibility issues

## Version 1.0.0 (Initial Release)

- **Feature**: Initial release with core memoization functionality
- **Feature**: Support for Microsoft.Extensions.Caching.Abstractions integration
- **Feature**: Text template-based code generation for multiple function signatures
- **Feature**: High-performance function result caching
- **Feature**: Efficient memory management for cached results
- **Target**: .NET Standard 2.0 support for broad compatibility
