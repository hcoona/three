# Test Generation Research

## Project Overview
- **Path**: `/home/shuaizhang/.copilot/session-state/95cabf48-3a10-4649-a1ed-355a4dc6580c/files/worktrees/fourth-npm-cli`
- **Language**: C# on .NET 10 (`global.json` pins SDK `10.0.300`, target `net10.0`)
- **Framework**: .NET SDK projects; the CLI composes the Platform and Contracts projects
- **Test Framework**: xUnit v3 (`xunit.v3.mtp-v2` 3.2.2) on Microsoft Testing Platform

## Dependency Graph
- **Leaf types** (no in-scope dependencies): the planned typed npm resolution status/result/exception and executable-resolution value types, colocated in `NpmPhase12VerticalSliceService.cs`. Existing process primitives (`IProcessRunner`, `ProcessStartSpec`, `ProcessResult`, `ProcessExecutionStatus`) and `IFileSystem` are bounded dependencies, not test targets.
- **Mid-layer types** (depend on leaves): `NpmPhase12VerticalSliceService`, its options, registry declaration, plan request, and doctor result. It also depends on configuration/contracts policies and injectable filesystem/process/environment abstractions.
- **Top-layer types** (depend on mid-layer): `CliApplication` and `CliRuntimeOptions`; doctor aggregation also invokes Yarn and configuration services, which are relevant only to prove continuation after an expected npm failure.

## Build & Test Commands
Run all commands from the worktree root.

- **Build**: `dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug && dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug`
- **Test (scoped — fix cycles, platform)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12VerticalSliceServiceTests Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12NpmIntegrationTests`
- **Test (scoped — fix cycles, CLI doctor)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Cli.Tests.CliApplicationTests --filter-method '*Doctor*'`
- **Test (full bounded projects)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug && dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug`
- **Test (harness-equivalent — discovery check)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --list-tests && dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug --list-tests`
- **Lint**: no bounded standalone lint command was found. `dotnet build` is the required check: repository props enable latest recommended analyzers, code-style enforcement, and warnings-as-errors.

The .NET 10 MTP syntax intentionally uses `--project` and passes xUnit options such as `--filter-class` directly, with no `--` separator. Root CI similarly enumerates MTP test projects and invokes `dotnet test --project ...`.

## Scope
- **Boundary**: npm workspace-root resolution and its production-like CLI/doctor integration under `src/private/app/azureauth-credprovider`, only.
- **Production targets**:
  - `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/VerticalSlice/NpmPhase12VerticalSliceService.cs`
  - `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli/CliApplication.cs`
- **Exact test inventory**:
  - update `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/NpmPhase12VerticalSliceServiceTests.cs`
  - add `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/NpmPhase12NpmIntegrationTests.cs`
  - update `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/CliApplicationTests.cs`
- **Representative existing tests**:
  - `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/NpmPhase12VerticalSliceServiceTests.cs`
  - `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/CliApplicationTests.cs`

## Files to Test

### High Priority
| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|------|-------------------|-------------|-------------------|-------|
| `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/VerticalSlice/NpmPhase12VerticalSliceService.cs` | `NpmPhase12VerticalSliceService`; options/declaration/request/doctor records; workspace and npm executable resolution | High | Partial | Existing fakes cover legacy behavior, but not typed outcomes, real npm membership, Windows launch layouts, true async/cancellation, operation-scoped reuse, or failure-tolerant doctor behavior. |
| `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli/CliApplication.cs` | `Run`, `HandleDoctor`, npm doctor rendering/success, npm/Yarn service factories, `CliRuntimeOptions` | Medium | Partial | Large top-layer file; use injected options/process runners and assert observable output/call counts. Missing expected npm-failure continuation and one-time current-directory capture. |

### Medium Priority
| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|------|-------------------|-------------|-------------------|-------|
| `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/TestDoubles/FakeProcessRunner.cs` | queued async handler support | High | Substantial | Reuse as-is where possible for pending tasks, cancellation, exceptions, and process call counts; change only if a narrowly required observation is unavailable. |

### Low Priority / Skip
| File | Reason |
|------|--------|
| `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/Processes/*.cs` | Stable injected process abstraction; this scope consumes it rather than changing generic process behavior. |
| Yarn/configuration production sources | Only their continued execution/output is asserted at the CLI boundary; no Yarn/config behavior change is requested. |
| Contracts project | npm status types are implementation/doctor concerns in this scope; no frozen wire-contract change is requested. |

## Existing Tests & Coverage Classification
- `NpmPhase12VerticalSliceService.cs` → `NpmPhase12VerticalSliceServiceTests.cs`: **partial**. The static analyzer paired them, and the file has 24 facts plus 4 theories (38 currently executed cases). It covers npm-prefix delegation with a fake, generic launch/timeout/nonzero/oversize/invalid failures, npm/pnpm plan behavior, and doctor basics. It does not cover the requested typed model or concurrency/lifetime/real-npm/Windows requirements.
- `CliApplication.cs` → `CliApplicationTests.cs`: **partial for this slice**. The static analyzer paired them. The large file has 82 facts and 43 theories; the current doctor-filtered baseline executes 29 cases. Existing npm/Yarn output checks use explicitly isolated options, so they do not establish production default-directory behavior or continuation after npm resolution failure.
- Analyzer command was run exactly once with `--include-tested` against this worktree. Relevant pairings above are a **static parse/identifier heuristic, not line or branch coverage**.

## Existing Test Projects
- **Project file**: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj`
  - **Target source project**: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/Hcoona.AzureAuth.CredProvider.Platform.csproj`
  - **Bounded test files**: existing `NpmPhase12VerticalSliceServiceTests.cs`; planned `NpmPhase12NpmIntegrationTests.cs`
- **Project file**: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj`
  - **Target source project**: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli/Hcoona.AzureAuth.CredProvider.Cli.csproj`
  - **Bounded test file**: `CliApplicationTests.cs`

Both projects reference MTP, xUnit v3, TRX/code-coverage extensions, and expose source internals to their paired test assemblies.

## Testing Patterns
- xUnit v3 `[Fact]`/`[Theory]`, descriptive PascalCase names, direct `Assert.*`, and `TestContext.Current.CancellationToken` for async tests.
- Platform tests use `InMemoryFileSystem` with explicit POSIX/Windows semantics and `FakeProcessRunner` queued results/handlers; assert the complete `ProcessStartSpec` and secret-safe failures.
- CLI tests invoke `CliApplication.Run` through `InvokeWithRuntime`, inject a `CliRuntimeOptions`, and assert exit code plus exact/selected `key: value` doctor lines.
- OS tests use xUnit v3 conditional skip properties (for example `SkipWhen = nameof(IsWindows)`). Real npm tests should create isolated temporary package trees, execute the actual installed npm through production process plumbing, avoid network/install commands, and skip with a concise reason only when npm is unavailable.
- Keep filesystem/environment-changing tests in the existing nonparallel collection or otherwise serialize them and restore process state in `finally`.

## Recommendations
1. Define and test an explicit workspace-resolution contract with statuses `Succeeded`, `NotRequired`, `LaunchFailure`, `TimedOut`, `NonZeroExit`, `OutputTooLarge`, and `InvalidOutput`; failures used by configure/plan must throw the specific typed exception and expose no stderr/secrets.
2. Preserve `npm prefix` as the authority. Real integration fixtures must prove:
   - member: `workspaces: ["packages/*"]`, invocation `packages/member` → root;
   - non-member: same root declaration, invocation `tools/nonmember` → its own package directory;
   - character class: `workspaces: ["packages/[a-z]*"]`, invocation `packages/apple` → root.
   Local probing with npm 11.9.0 confirmed those three outputs.
3. Add deterministic Windows-layout unit cases for a direct executable and standard Node/npm shim layout, plus missing/invalid candidates. Add a Windows-only native smoke using installed npm when available. Do not rely on Linux to execute `.cmd`.
4. Make `RunDoctorAsync` observably asynchronous with a pending process task; assert caller cancellation reaches the runner. A successful doctor operation must make one resolution probe reused by declaration and both npm/pnpm/CI plan-shadow checks. A second doctor call must make a new probe (no service-lifetime cache).
5. Expected typed resolution failure should produce a doctor result with concise status, failed/skipped npm checks, and no throw. Unexpected exceptions must propagate. CLI tests must prove Yarn and configuration output still appears and aggregate doctor fails.
6. Capture the production-like CLI current directory once and use it for default npm and Yarn workspace options without adding an abstraction. Add an isolated current-directory test and a separate test proving explicitly injected npm/Yarn options remain unchanged.
7. Likely blockers: current production synchronously blocks on `RunAsync`; broad catches erase failure type; doctor repeats workspace resolution through nested calls; Linux cannot validate native Windows launch behavior; and environment/current-directory mutation can make tests flaky unless serialized.

## Baseline
- Platform npm class: **38 passed**, 0 failed/skipped.
- CLI doctor method filter: **29 passed**, 0 failed/skipped.
- Environment: npm `11.9.0`, Node `v24.14.0`; CI also installs Node 24.
