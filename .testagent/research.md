# Research: implement-process-fixes

## Scope and strategy

- Workspace: `/home/shuaizhang/s/github.com/hcoona/three-workspaces/fix-azureauth-process`
- Branch: `dev/shuaizhang/azureauth-fix-process`
- Strategy: single pass. The requested behavior spans the process runner, Git/NuGet CLI dispatch, and the NuGet plugin request handler, but is bounded to one product.
- The workspace is authoritative. No missing source will be restored or reconstructed.

## Existing conventions

- Language/runtime: C# on .NET 10 (`global.json` pins `10.0.300`).
- Tests: xUnit v3 with Microsoft.Testing.Platform.
- Product tests append to the canonical projects under
  `tests/private/app/azureauth-credprovider/`.
- Test naming uses descriptive PascalCase method names; async tests return `Task`.
- Test projects already have `InternalsVisibleTo`, so internal-only seams are preferred.
- Narrow commands:
  - `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj`
  - `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj`
- Full validation command: `dotnet build dirs.proj --no-incremental`, followed by
  `dotnet test dirs.proj`.

## Bounded target inventory

| Source | Canonical tests | Finding |
|---|---|---|
| `Processes/SystemProcessRunner.cs` and `ProcessStartSpec.cs` | `SystemProcessRunnerTests.cs` | Timeout CTS is created after launch; cleanup can wait forever when kill fails. |
| `Cli/CliApplication.cs` | `CliApplicationTests.cs` | Phase 8 Git and Phase 10 NuGet configure/doctor/unconfigure calls omit the runtime token. |
| `AdapterHost/GitCredentialHelperAdapter.cs` | `GitCredentialHelperAdapterTests.cs` | Git protocol acquisition has no runtime-token path. |
| `AdapterHost/NuGetPluginAdapter.cs` | `NuGetPluginAdapterTests.cs` | Per-request token is checked only before handling; acquisition and response sending use no token. |

## Acceptance checklist

1. Oversized/invalid timeout is rejected before process launch; evidence must prove zero launches.
2. Failed kill cannot make cleanup unbounded and must preserve each primary outcome:
   caller cancellation, configured timeout, and bounded-output failure.
3. Runtime cancellation reaches affected Git CLI operations.
4. Runtime cancellation reaches affected NuGet CLI operations.
5. NuGet request cancellation reaches credential acquisition and response sending.
6. Use internal test seams rather than expanding public API.
7. Treat the delivered workspace as authoritative; never restore missing source.
8. Limit production edits to the requested fixes and small testability seams.
9. Retain `.testagent/research.md`, `plan.md`, and `status.md`.
10. Run narrow tests after each phase and feasible full build/fresh tests; record exact commands.
11. Run `test-gap-analysis`, `assertion-quality`, and manual exact scenario mapping after final changes.

## Design constraints

- Keep `SystemProcessRunner` public surface unchanged. Add an internal process-cleanup seam and
  an internal constructor only.
- Bound cleanup independently of the canceled operation token.
- Keep existing synchronous adapter APIs for compatibility; add internal async/cancellation paths
  used by protocol handlers.
- Preserve all existing tests and append focused tests to their canonical files.
