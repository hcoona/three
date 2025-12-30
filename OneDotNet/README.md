---
author: Shuai Zhang<zhangshuai.ustc@gmail.com>
---

# OneDotNet

**A comprehensive .NET monorepo containing reusable libraries, tools, and experimental projects.**

OneDotNet is a unified monorepo housing all of my .NET projects, libraries, and tools. This repository follows the monorepo pattern to enable better code sharing, consistent tooling, and streamlined dependency management across projects.

## Issues and Support

If you encounter any bugs or have feature requests, please report them on our GitHub Issues page:

[🐛 Report Issues](https://github.com/hcoona/OneDotNet/issues)

## Why Monorepo?

This repository adopts the monorepo approach for several key benefits:

- **Code Sharing**: Easily share common utilities and libraries across projects
- **Consistent Tooling**: Unified build, test, and deployment processes
- **Dependency Management**: Centralized package version management
- **Atomic Changes**: Make cross-project changes in a single commit
- **Developer Experience**: Single checkout for all related projects

For more insights on monorepo benefits, see Google's article: [Why Google Stores Billions of Lines of Code in a Single Repository](https://cacm.acm.org/magazines/2016/7/204032-why-google-stores-billions-of-lines-of-code-in-a-single-repository/fulltext).

## Repository Structure

```text
OneDotNet/
├── srcs/                        # Source code projects
│   ├── public/                  # Public libraries (published to NuGet)
│   │   ├── CircularList/        # Thread-safe circular list implementation
│   │   ├── Memoization/         # Memoization utilities
│   │   ├── PhiFailureDetector/  # Phi accrual failure detector
│   │   └── ...                  # Other public libraries
│   └── private/                 # Private tools and utilities
├── tests/                       # Unit and integration tests
├── codelab/                     # Experimental projects and prototypes
├── Directory.Build.props        # Global MSBuild properties
└── OneDotNet.sln               # Solution file
```

Central package versions are defined at the repository root in `../Directory.Packages.props`.

## Getting Started

### Prerequisites

Before you begin, ensure you have the following installed:

- **Visual Studio 2022** (recommended) or any compatible IDE
- **.NET 8.0 SDK** or later
- **Git** for version control

### Quick Start

1. **Clone the repository:**

    ```powershell
    git clone https://zhangshuai89@dev.azure.com/zhangshuai89/Public/_git/OneDotNet
    cd OneDotNet
    ```

2. **Build all projects:**

    ```powershell
    dotnet build dirs.proj
    ```

3. **Run tests:**

    ```powershell
    dotnet test
    ```

### Development Workflow

#### Building Specific Projects

To build a specific project:

```powershell
dotnet build srcs/public/CircularList/CircularList.csproj
```

#### Running Tests

Run all tests:

```powershell
dotnet test
```

Run tests for a specific project:

```powershell
dotnet test tests/CircularList.UnitTest/
```

### Visual Studio Solution Generation

Generate a Visual Studio solution file using [SlnGen](https://microsoft.github.io/slngen/) and [vswhere](https://github.com/microsoft/vswhere):

```powershell
slngen --nologo --ignoreMainProject --launch false --loadprojects false `
   -vs (vswhere -nocolor -format value -property productPath) `
   --folders true --collapsefolders true `
   -o OneDotNet.sln
```

This command creates a comprehensive solution file that includes all projects organized in folders.

### Packaging

#### Development Packages

For development and testing:

```powershell
dotnet pack <project_path> -c Release
```

#### Production Packages

For formal releases, include continuous integration flags:

```powershell
dotnet pack <project_path> -c Release /p:PublicRelease=true /p:ContinuousIntegrationBuild=true
```

## Featured Libraries

### Public Libraries (Available on NuGet)

- **CircularList** - Thread-safe circular list implementation with efficient operations
- **Memoization** - High-performance memoization utilities for .NET applications
- **MicrosoftExtensions.Logging.MSTest** - MSTest integration for Microsoft.Extensions.Logging with test output forwarding
- **MicrosoftExtensions.Logging.Xunit** - Integration between Microsoft.Extensions.Logging and xUnit testing framework
- **MicrosoftExtensions.Options.DedupChangeExtensions** - Deduplication extensions for IOptionsMonitor to reduce noise in configuration change notifications
- **PhiFailureDetector** - Implementation of the Phi Accrual Failure Detector algorithm
- **WebHdfs.Extensions.FileProviders** - ASP.NET Core file provider for WebHDFS

### Private Tools

- **DotNetLockFileLister** - Utility for analyzing .NET package lock files
- **OxfordDictExtractor** - Dictionary data extraction utilities
- **OxfordLearnersDictionaryProcessor** - Advanced Oxford Learner's Dictionary processing tool with AI integration
- **OxfordWordlistExtractor** - Specialized tool for extracting word lists from Oxford resources

## License

This project is licensed under the terms specified in the [LICENSE](LICENSE) file.

## Deprecated Packages

### Deprecated Public Libraries

The following packages have been deprecated and their source code removed. Links redirect to the last commit before removal:

| Package                                                                                                | Reason                             | Alternative                                                                                                                                                                                   |
| ------------------------------------------------------------------------------------------------------ | ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [RateLimiter](https://github.com/hcoona/OneDotNet/tree/7b14411/RateLimiter)                            | Superseded by better alternatives  | Use [Polly](https://github.com/App-vNext/Polly)                                                                                                                                               |
| [Clocks.Net](https://github.com/hcoona/OneDotNet/tree/7cc2064/srcs/public/Clocks.Net)                  | Native .NET 8.0 support available  | Use [TimeProvider](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider?view=net-8.0) or [Microsoft.Bcl.TimeProvider](https://www.nuget.org/packages/Microsoft.Bcl.TimeProvider/) |
| [TimeLimiter](https://github.com/hcoona/OneDotNet/tree/5ab8904/TimeLimiter)                            | Performance issues and poor design | Use [Polly](https://github.com/App-vNext/Polly)                                                                                                                                               |
| [HCOONa.Grpc.MicrosoftExtension.Logging](https://github.com/hcoona/OneDotNet/tree/7b14411/GrpcAdapter) | Grpc.Core deprecated               | Use modern gRPC implementations                                                                                                                                                               |

### Deprecated Private Tools

| Package                                                                                                             | Reason                                            | Alternative                                                                                                                                            |
| ------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| [QiDianBookDownloader](https://github.com/hcoona/OneDotNet/tree/main/srcs/private/QiDianBookDownloader)             | Login authentication challenges with Playwright   | Use [mcp-chrome](https://github.com/hangwin/mcp-chrome) approaches, continued in [OnePython](https://dev.azure.com/zhangshuai89/Public/_git/OnePython) |
| [HtmlEmbeddedImageConverter](https://github.com/hcoona/OneDotNet/tree/main/srcs/private/HtmlEmbeddedImageConverter) | Superseded by better alternatives                 | Use [HTMLArk](https://github.com/BitPhinix/HTMLArk)                                                                                                    |
| [SwigDoc2Latex](https://github.com/hcoona/OneDotNet/tree/b687bee/SwigDoc2Latex)                                     | Unmaintained, website changes broke functionality | N/A                                                                                                                                                    |
| [GeothermalResearchInstitute](https://github.com/hcoona/OneDotNet/tree/73a338a/GeothermalResearchInstitute)         | Project discontinued                              | N/A                                                                                                                                                    |
