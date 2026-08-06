# Test Implementation Plan

## Overview

Use a targeted, three-phase test-only strategy for the partially covered npm vertical slice and CLI doctor integration. Implement leaf contract/status cases first, then real npm integration, then top-level CLI aggregation and option composition. Use the existing xUnit v3/Microsoft Testing Platform projects and conventions. Do not plan production changes; a test-double-only observation hook is acceptable only if an existing abstraction already supports it.

## Commands

- **Build**: `dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug && dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug`
- **Test (platform scope)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12VerticalSliceServiceTests Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12NpmIntegrationTests`
- **Test (CLI doctor scope)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Cli.Tests.CliApplicationTests --filter-method '*Doctor*'`
- **Test (full bounded projects)**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug && dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug`
- **Discovery**: `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --list-tests && dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug --list-tests`
- **Lint**: no separate bounded command; `dotnet build` enforces analyzers, style, and warnings-as-errors.

## Phase Summary

| Phase | Focus | Test Files | Est. Cases |
|---|---|---:|---:|
| 1 | Typed platform outcomes, Windows resolution, async/cancellation, operation-scoped probing | 1 | 27-31 |
| 2 | Real npm prefix integration and Windows native smoke | 1 new | 4 |
| 3 | CLI failure aggregation and current-directory option composition | 1 | 3-4 |

---

## Phase 1: Platform Contract and Operation Semantics

### Overview

Establish the leaf typed outcome contract before testing the service's mid-layer behavior. Keep all additions in the existing platform test project and use `InMemoryFileSystem`, `FakeProcessRunner`, complete `ProcessStartSpec` assertions, and `TestContext.Current.CancellationToken`.

### Files to Test

#### 1. NpmPhase12VerticalSliceService.cs

- **Source**: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/VerticalSlice/NpmPhase12VerticalSliceService.cs`
- **Test File**: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/NpmPhase12VerticalSliceServiceTests.cs`
- **Test Class**: `NpmPhase12VerticalSliceServiceTests`

**Typed workspace-resolution outcome tests**

1. `ResolveWorkspaceAsync_ReturnsSucceeded_ForZeroExitWithValidPrefix`
   - Queue exit code zero with one valid absolute npm-prefix line.
   - Assert status `Succeeded`, the normalized workspace root, no failure detail, and the exact `npm prefix` process specification.

2. `ResolveWorkspaceAsync_ReturnsNotRequired_WhenRegistryDoesNotRequireNpmResolution`
   - Use a registry/declaration path that does not require npm workspace lookup.
   - Assert status `NotRequired`, no workspace root/failure detail, and zero process probes.

3. `ResolveWorkspaceAsync_ReturnsLaunchFailure_WhenProcessCannotLaunch`
   - Queue the process abstraction's launch-failure result.
   - Assert status `LaunchFailure`; ensure stderr, command secrets, and environment secrets are absent from the public result.

4. `ResolveWorkspaceAsync_ReturnsTimedOut_WhenNpmPrefixTimesOut`
   - Queue the timeout execution status.
   - Assert status `TimedOut` and concise, secret-free detail.

5. `ResolveWorkspaceAsync_ReturnsNonZeroExit_WhenNpmPrefixExitsNonZero`
   - Queue a nonzero exit with sensitive stderr.
   - Assert status `NonZeroExit`; do not expose stderr or secrets.

6. `ResolveWorkspaceAsync_ReturnsOutputTooLarge_WhenNpmPrefixExceedsLimit`
   - Queue the output-limit execution status.
   - Assert status `OutputTooLarge`, with no partial output exposed.

7. `ResolveWorkspaceAsync_ReturnsInvalidOutput_WhenNpmPrefixOutputIsNotOneValidDirectory`
   - Cover empty/whitespace, multiple nonempty lines, malformed path, and nonexistent path as theory rows.
   - Assert status `InvalidOutput` for every row.

**Typed configure/plan exception tests**

8. `ConfigurePath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus`
   - Run the existing configure/discovery entry path for each expected failure status: `LaunchFailure`, `TimedOut`, `NonZeroExit`, `OutputTooLarge`, and `InvalidOutput`.
   - Assert the exact typed npm workspace-resolution exception, its matching status, concise message, and absence of stderr/secrets.

9. `PlanPath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus`
   - Run the existing npm/pnpm plan entry path for the same five failure-status rows.
   - Assert the same typed exception contract rather than a generic exception or erased status.

**Windows executable resolver tests**

10. `ResolveNpmExecutable_ReturnsDirectExecutable_WhenConfiguredPathIsLaunchable`
    - Use Windows in-memory path semantics with a direct executable candidate.
    - Assert the executable and arguments used for `npm prefix`.

11. `ResolveNpmExecutable_ReturnsNodeAndNpmCliScript_ForStandardWindowsShimLayout`
    - Model the standard Node/npm shim layout.
    - Assert launch through the resolved Node executable with the npm CLI script and `prefix` arguments, without trying to execute `.cmd` on Linux.

12. `ResolveNpmExecutable_ReturnsMissingCandidateFailure_WhenNodeExecutableIsAbsent`
    - Include the npm shim/script but omit Node.
    - Assert the resolver's deterministic missing-candidate failure.

13. `ResolveNpmExecutable_ReturnsMissingCandidateFailure_WhenNpmCliScriptIsAbsent`
    - Include Node but omit the npm CLI script.
    - Assert the deterministic missing-candidate failure.

14. `ResolveNpmExecutable_ReturnsInvalidCandidateFailure_WhenCandidateLayoutIsUnsupported`
    - Supply a malformed or unsupported candidate layout.
    - Assert typed invalid-candidate resolution and no process launch.

15. `ResolveWorkspaceAsync_ReturnsLaunchFailure_WhenResolvedWindowsCommandCannotLaunch`
    - Resolve a structurally valid Windows command and queue launch failure.
    - Assert the failure maps to `LaunchFailure`, distinct from missing/invalid candidate failures.

**True async, cancellation, reuse, and cache-lifetime tests**

16. `RunDoctorAsync_RemainsIncomplete_WhileResolutionProbeIsPending`
    - Queue a `TaskCompletionSource<ProcessResult>` with asynchronous continuations.
    - Invoke `RunDoctorAsync`, assert the returned task remains incomplete, then complete the probe and await the successful result. This detects synchronous blocking.

17. `RunDoctorAsync_ForwardsCallerCancellationToResolutionProbe`
    - Have the runner record the supplied token and remain pending until cancellation.
    - Cancel the caller token, assert the runner observed that exact token/cancellation, and assert the doctor task completes with normal cancellation semantics rather than hanging.

18. `RunDoctorAsync_ReusesOneResolutionProbeAcrossDeclarationAndPlanChecks`
    - Return one successful prefix.
    - Assert exactly one resolution process call supplies declaration discovery plus npm, pnpm, and CI plan-shadow checks; assert all dependent checks use that same resolved root.

19. `RunDoctorAsync_DoesNotCacheResolutionAcrossCalls`
    - Queue two successful probes with distinct roots and call the same service instance twice.
    - Assert two process calls total and that each result uses its corresponding root, proving operation-scoped reuse without a service-lifetime cache.

**Expected and unexpected doctor failure tests**

20. `RunDoctorAsync_MapsExpectedResolutionFailureToTypedDoctorResult`
    - Theory rows: `LaunchFailure`, `TimedOut`, `NonZeroExit`, `OutputTooLarge`, and `InvalidOutput`.
    - For each row, assert no throw, exact typed resolution status, unsuccessful aggregate result, failed resolution check, skipped/failed declaration and npm/pnpm/CI dependent checks, and concise secret-free detail.

21. `RunDoctorAsync_PropagatesUnexpectedResolutionException`
    - Make the process/resolver dependency throw a sentinel exception not belonging to the typed expected-failure contract.
    - Assert the same exception propagates and is not converted to a doctor status/result.

### Narrow Validation

1. `dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug`
2. `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12VerticalSliceServiceTests`

### Success Criteria

- [ ] All seven typed statuses have explicit cases.
- [ ] Configure and plan preserve the typed exception/status.
- [ ] Windows layouts and deterministic failures are covered without native `.cmd` execution.
- [ ] Async, cancellation, one-probe reuse, and no lifetime cache are observable.
- [ ] Expected failures map to results; unexpected exceptions propagate.
- [ ] The platform project builds and the scoped class passes.

---

## Phase 2: Real npm Prefix Integration

### Overview

Exercise actual installed npm through production process plumbing in isolated temporary package trees. These tests preserve `npm prefix` as the authority and must not implement an independent workspace glob matcher or invoke network/install commands.

### Files to Test

#### 1. NpmPhase12VerticalSliceService.cs

- **Source**: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/VerticalSlice/NpmPhase12VerticalSliceService.cs`
- **Test File**: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/NpmPhase12NpmIntegrationTests.cs`
- **Test Class**: `NpmPhase12NpmIntegrationTests`

**Tests**

1. `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForWorkspaceMember`
   - Root `package.json`: `workspaces: ["packages/*"]`; invocation directory: `packages/member`.
   - Give root and member distinct declarations so selection is observable.
   - Assert `Succeeded` and the root selected by actual `npm prefix`.

2. `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForNonWorkspacePackage`
   - Same root workspace declaration; invocation directory: `tools/nonmember`.
   - Give root and nonmember distinct declarations.
   - Assert `Succeeded` and the nonmember package directory selected by actual `npm prefix`, not the workspace root.

3. `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForCharacterClassWorkspaceMember`
   - Root `package.json`: `workspaces: ["packages/[a-z]*"]`; invocation directory: `packages/apple`.
   - Assert `Succeeded` and the root selected by actual `npm prefix`.

4. `ResolveWorkspaceAsync_UsesNativeInstalledNpm_OnWindows`
   - Windows-only smoke using an isolated member fixture and production executable/process resolution.
   - Assert successful native resolver launch and the expected prefix. Skip on non-Windows; on Windows, skip with a concise reason only if npm is genuinely unavailable.

For every test, avoid changing process current directory where possible. If unavoidable, use the existing nonparallel collection, capture state once, restore it in `finally`, and recursively remove the temporary tree in `finally`.

### Narrow Validation

1. `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Platform.Tests.NpmPhase12NpmIntegrationTests`
2. On Windows, repeat the same command and confirm `ResolveWorkspaceAsync_UsesNativeInstalledNpm_OnWindows` passes when npm is installed.

### OS and Environment Blockers

- Native Windows launch behavior cannot be validated on the current Linux host; only deterministic Windows-path unit tests run cross-platform.
- The three real npm cases require installed npm. The researched host has npm 11.9.0/Node 24.14.0; another host may record a concise missing-tool skip.
- Do not run `npm install`, access the network, or execute Windows `.cmd` files on Linux.

### Success Criteria

- [ ] Real npm proves member, non-member, and character-class behavior.
- [ ] Assertions consume npm's prefix result rather than duplicating glob semantics.
- [ ] Temporary/process state is always restored.
- [ ] Windows native smoke is passed on a suitable Windows host or explicitly recorded as OS/tool blocked.

---

## Phase 3: CLI Aggregation and Option Composition

### Overview

Test the top-layer CLI after the platform contract is fixed. Reuse `InvokeWithRuntime`, injected `CliRuntimeOptions`, existing service fakes, and exact doctor output conventions. Do not modify Yarn/configuration production behavior.

### Files to Test

#### 1. CliApplication.cs

- **Source**: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli/CliApplication.cs`
- **Test File**: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/CliApplicationTests.cs`
- **Test Class**: `CliApplicationTests`

**Tests**

1. `Doctor_ContinuesAggregationAfterExpectedNpmResolutionFailure`
   - Inject a representative typed expected npm failure (`TimedOut`) and successful, distinguishable Yarn/configuration results.
   - Assert a nonzero doctor exit, the exact canonical npm `TimedOut` status line, failed/skipped npm-dependent lines, Yarn output, configuration output, and failed final aggregate.
   - Assert Yarn and configuration services were each invoked once after the npm failure.

2. `Doctor_UsesCapturedCurrentDirectoryForDefaultNpmAndYarnOptions`
   - Invoke the production-like CLI from an isolated current directory.
   - Leave npm and Yarn workspace directories at their production defaults.
   - Assert both constructed services discover configuration from that directory; production code must capture `Environment.CurrentDirectory` once without adding an abstraction.

3. `Doctor_PreservesExplicitNpmAndYarnWorkspaceDirectories`
   - Inject distinct explicit npm and Yarn workspace directories and invoke from a different current directory.
   - Assert each service receives its explicit path unchanged and neither is overwritten by the default captured value.
   - The behavioral assertion is preservation of both injected paths.

4. `Doctor_DoesNotReclassifyUnexpectedNpmExceptionAsExpectedResolutionFailure` (only if the existing CLI fatal-error convention exposes this distinction)
   - Inject the same sentinel unexpected exception used in Phase 1.
   - Assert the established CLI fatal-error behavior, and specifically that no typed expected-resolution status or continuation output is emitted.
   - Omit this duplicate boundary case if the CLI abstraction cannot inject it without a production seam; Phase 1 remains the required propagation proof.

### Narrow Validation

1. `dotnet build tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug`
2. `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj -c Debug --filter-class Hcoona.AzureAuth.CredProvider.Cli.Tests.CliApplicationTests --filter-method '*Doctor*'`

### Success Criteria

- [ ] Expected npm resolution failure is rendered, aggregation continues through Yarn/configuration, and the final result fails.
- [ ] Default npm and Yarn options share exactly one current-directory capture.
- [ ] Explicit npm and Yarn directories remain unchanged.
- [ ] The CLI project builds and doctor-scoped tests pass.

---

## Final Validation

1. Run both full bounded test projects with the command above.
2. Run both `--list-tests` commands and confirm every exact test name and `NpmPhase12NpmIntegrationTests` are discovered.
3. Repeat the platform integration class on Windows and record the native-smoke result.
4. No standalone lint is required beyond successful warning-as-error builds.

## Constraints and Known Blockers

- No production fix is part of this test plan. Tests may initially fail because research found synchronous blocking, broad exception erasure, repeated probes, expected doctor throws, and missing default-directory composition.
- Do not add a new test project. Modify only the three test files in the research inventory.
- Prefer existing fakes. A minimal change to `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/TestDoubles/FakeProcessRunner.cs` is allowed only to observe an already-supported async/cancellation interaction; do not introduce a production seam.
- Native Windows smoke requires a Windows agent with Node/npm installed. Linux results cannot close that OS-specific acceptance item.

## Step 8 Final Requirement Audit (2026-08-06)

The final generated tests were re-read after the pseudo-mutation and assertion-quality strengthening edits. No acceptance gap was found, so Step 8 made no test-file changes.

| Requirement | Final evidence |
|---|---|
| All seven resolution statuses | `ResolveWorkspaceAsync_ReturnsSucceeded_ForZeroExitWithValidPrefix`, `ResolveWorkspaceAsync_ReturnsNotRequired_WhenRegistryDoesNotRequireNpmResolution`, `ResolveWorkspaceAsync_ReturnsLaunchFailure_WhenProcessCannotLaunch`, `ResolveWorkspaceAsync_ReturnsTimedOut_WhenNpmPrefixTimesOut`, `ResolveWorkspaceAsync_ReturnsNonZeroExit_WhenNpmPrefixExitsNonZero`, `ResolveWorkspaceAsync_ReturnsOutputTooLarge_WhenNpmPrefixExceedsLimit`, and `ResolveWorkspaceAsync_ReturnsInvalidOutput_WhenNpmPrefixOutputIsNotOneValidDirectory`; each asserts its literal status, root/default state, and exact probe specification. |
| Typed configure and plan failures | `ConfigurePath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus` and `PlanPath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus` use literal rows for all five failure statuses and assert exception type, matching status, concise/redacted message, and one complete `npm prefix` probe. |
| Windows executable layouts and failures | The seven `ResolveNpmExecutable_*`/resolved-command tests cover direct executable success, standard Node/npm shim success, missing direct executable, missing Node, missing CLI script, unsupported layout, and resolved launch failure with literal Windows paths, arguments, working directory, timeout, and output limits. |
| True async and caller cancellation | `RunDoctorAsync_RemainsIncomplete_WhileResolutionProbeIsPending` asserts the invocation returns while the probe and doctor task remain incomplete. `RunDoctorAsync_ForwardsCallerCancellationToResolutionProbe` asserts token identity, cancellation observation, cancellation completion, and one complete probe. |
| Exact probe count and cache lifetime | `RunDoctorAsync_ReusesOneResolutionProbeAcrossDeclarationAndPlanChecks` uses `Assert.Single`; `RunDoctorAsync_DoesNotCacheResolutionAcrossCalls` requires exactly two probes and distinct `/repo` then `/repo/packages` results. |
| Expected/unexpected doctor classification | `RunDoctorAsync_MapsExpectedResolutionFailureToTypedDoctorResult` has five literal failure-status rows and asserts failed/null/skipped dependent state plus redaction. `RunDoctorAsync_PropagatesUnexpectedResolutionException` asserts sentinel identity. |
| Aggregation continues after expected npm failure | `Doctor_ContinuesAggregationAfterExpectedNpmResolutionFailure` asserts exit `1`, ordered npm → Yarn → configuration observations, exact npm/Yarn/configuration lines, one Yarn/configuration output section, failed aggregate, and no diagnostic leak. |
| Concise canonical status | `Doctor_ContinuesAggregationAfterExpectedNpmResolutionFailure` calls `AssertDoctorCheck(..., "npm-workspace-resolution-status", "TimedOut")`; the helper requires exactly one literal `npm-workspace-resolution-status: TimedOut` line. Platform failure helpers also bound public failure messages to 1–300 characters. |
| Exact current-directory capture | `Doctor_UsesOneCurrentDirectoryCaptureForDefaultNpmAndYarnOptions` asserts `captureCount == 1`, both default services select artifacts under `first-capture`, no later-directory output, preserved readers, and no npm probe. |
| Preserve both injected npm/Yarn options | `Doctor_PreservesExplicitNpmAndYarnWorkspaceDirectories` uses distinct `explicit-npm` and `explicit-yarn` directories and asserts both declarations, both injected readers, the injected npm runner, and absence of the captured default. |
| Literal real-npm member/non-member/character class | `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForWorkspaceMember` uses `"packages/*"` + `"packages/member"`; `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForNonWorkspacePackage` uses `"packages/*"` + `"tools/nonmember"`; `ResolveWorkspaceAsync_UsesRealNpmPrefix_ForCharacterClassWorkspaceMember` uses `"packages/[a-z]*"` + `"packages/apple"`. Each asserts the distinct declaration and the complete real `npm prefix` invocation. |
| Native Windows npm smoke | `ResolveWorkspaceAsync_UsesNativeInstalledNpm_OnWindows` asserts the literal root declaration URL and complete native invocation; it remains OS-blocked on Linux. |
| Assertion-gate strengthening retained coverage | The strengthened process-specification, null-state, injected-reader, exact-exit-code, and Windows URL assertions add observables to the same requirement tests; no test name, literal fixture, status row, continuation assertion, capture assertion, or option-preservation assertion was removed. |

### Final Counts

- Phase 1: 22 generated methods / 38 discovered cases.
- Phase 2: 4 generated methods / 4 discovered cases.
- Phase 3: 4 generated methods / 4 discovered cases.
- Generated total: **30 methods / 46 discovered cases**.
- Latest assertion-gate runs: platform scoped total **80** (41 passed, 38 production-blocked failures, 1 Windows skip); CLI generated scope **4** (1 passed, 3 production-blocked failures).
- Generated-only combined: **46** (4 passed, 41 production-blocked failures, 1 OS-blocked skip).

### Final Blockers

- Platform production lacks the typed workspace/executable contracts, asynchronous cancellation flow, one-probe operation scope, and expected-failure doctor mapping required by 38 Phase 1 cases.
- CLI production lacks expected npm failure continuation and the injectable one-capture current-directory composition seam required by 3 Phase 3 cases.
- Native installed npm execution remains unvalidated on a Windows host.
