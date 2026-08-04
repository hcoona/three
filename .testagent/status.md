# Status: implement-process-fixes

| Phase | State | Evidence |
|---|---|---|
| Research | Complete | `research.md` |
| Plan | Complete | `plan.md` |
| Phase 1 | Complete | Platform tests: 809 passed in final run |
| Phase 2 | Complete | CLI tests: 260 passed; Platform tests: 809 passed |
| Phase 3 | Complete | Platform tests: 809 passed; CLI tests: 260 passed |
| Final validation | Complete | Full build and targeted Platform/CLI tests passed |
| Quality gate | Complete | Pseudo-mutation, assertion-depth, and scenario mapping below |

## Exact validation commands and results

1. `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/Hcoona.AzureAuth.CredProvider.Platform.Tests.csproj --verbosity minimal`
   - Final result: **809 passed, 0 failed, 0 skipped**.
2. `dotnet test --project tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Cli.Tests/Hcoona.AzureAuth.CredProvider.Cli.Tests.csproj --verbosity minimal`
   - Final result: **260 passed, 0 failed, 0 skipped**.
3. `dotnet build dirs.proj --no-incremental --verbosity minimal`
   - Final result: **succeeded, 0 warnings, 0 errors**.

## Mandatory quality gate

### Pseudo-mutation review

`test-gap-analysis` was invoked. Its requested language-extension helper was unavailable, so the
.NET/xUnit review was completed manually against the final source and tests.

- Removing pre-launch timeout validation and relying only on the post-launch timeout source is
  killed by `RunAsyncOversizedTimeoutThrowsBeforeLaunchingProcess` because the internal start
  counter would change from exactly 0.
- Removing the cleanup timeout, replacing the injected 50 ms bound with the two-second default,
  skipping wait, or swallowing the primary result is killed by the three
  `RunAsyncKillFailurePreserves*AndBoundsCleanup` tests through their one-second outer bound,
  exact status/exception, kill/wait counts, and cleanup-token cancellation assertions.
- Re-evaluating caller cancellation after cleanup instead of preserving the triggering timeout is
  killed by `RunAsyncTimeoutRemainsPrimaryWhenCallerCancelsDuringCleanup`.
- Sampling caller cancellation after timeout instead of recording the first cancellation source is
  killed by `RunAsyncTimeoutRemainsPrimaryWhenCallerCancelsAfterTimeoutBeforeLaunchCompletes`.
- Replacing any Git/NuGet Phase 8/10 configure or unconfigure runtime token with
  `CancellationToken.None` is killed by the parameterized CLI tests' exact exit
  130/stdout/stderr assertions.
- Dropping the NuGet doctor token from credential probes is killed by
  `NuGetDoctorPropagatesCancellationDuringCredentialProbe`, which cancels on the third credential
  acquisition after the two Git doctor probes and asserts the exact canceled CLI result.
- Dropping the Git adapter token is killed by token identity and post-cancel state assertions.
- Dropping the CLI-to-Git-adapter handoff is killed by
  `GitCredentialHelperProtocolPropagatesRuntimeCancellation`, which asserts exact token identity
  at acquisition plus exit 130 and the exact cancellation diagnostic.
- Dropping the NuGet request token from acquisition or response sending is killed by exact token
  identity, cancellation observation, response payload, and send-count assertions.
- No in-scope survived mutation remains.

### Assertion-depth review

`assertion-quality` was invoked. Its requested language-extension helper was unavailable, so the
final xUnit tests were inspected manually.

- No generated test is assertion-free, trivial-only, or tautological.
- Process tests combine primary outcome assertions with secondary cleanup attempt/cancellation
  state.
- CLI tests assert exact exit code, empty protocol output, and exact safe diagnostic.
- The dedicated NuGet doctor test asserts cancellation on the third acquisition, after the two
  Git doctor probes, so an earlier aggregate cancellation cannot make it pass vacuously.
- NuGet tests assert token identity plus concrete response status, username, password, send count,
  and cancellation state.

## Acceptance mapping

| Requirement | Exact evidence |
|---|---|
| Oversized/invalid timeout; zero launches | `RunAsyncOversizedTimeoutThrowsBeforeLaunchingProcess` asserts `InvocationCount == 0`; existing `ProcessStartSpecRejectsInvalidTimeoutAndCaptureLimits` covers zero/invalid values. |
| Kill failure preserves cancellation | `RunAsyncKillFailurePreservesCancellationAndBoundsCleanup` asserts `OperationCanceledException`, one kill, one wait, and cleanup-bound cancellation. |
| Kill failure preserves timeout | `RunAsyncKillFailurePreservesTimeoutAndBoundsCleanup` asserts `TimedOut` plus the same cleanup observables. |
| Kill failure preserves output outcome | `RunAsyncKillFailurePreservesOutputLimitAndBoundsCleanup` asserts `OutputTooLarge`, exact bounded stderr `"d"`, and cleanup observables. |
| Git runtime cancellation | `GitCliOperationsPropagateRuntimeCancellation` (5 rows), `GitCredentialHelperProtocolPropagatesRuntimeCancellation`, and `GetPropagatesRuntimeCancellationToCredentialAcquisition`. |
| NuGet runtime cancellation | `NuGetCliOperationsPropagateRuntimeCancellation` (4 rows) and `NuGetDoctorPropagatesCancellationDuringCredentialProbe`, which cancels inside a NuGet credential probe. |
| NuGet per-request cancellation | `AuthenticationRequestTokenFlowsThroughAcquisitionAndResponseSending` and `AuthenticationRequestCancellationStopsBeforeResponseSending`. |
| Internal seams/no public redesign | Internal process start/cleanup strategies, internal Git overload, internal NuGet handler factory/async path, and `InternalsVisibleTo` only. |
| Workspace authoritative/minimal edits | Git status shows only scoped AzureAuth source/tests plus retained `.testagent/`; no restore/reset/clean operation was used. |
| RPI artifacts retained | `.testagent/research.md`, `.testagent/plan.md`, and `.testagent/status.md`. |
| Narrow/full validation | Exact commands and results are recorded under **Exact validation commands and results** above. |
