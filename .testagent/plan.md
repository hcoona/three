# Plan: implement-process-fixes

## Phase 1 — Process validation and bounded cleanup

- Move timeout-token construction ahead of process launch.
- Add an internal cleanup strategy seam and a fixed production cleanup bound.
- Add exact tests:
  - `RunAsyncOversizedTimeoutThrowsBeforeLaunchingProcess`
  - `RunAsyncKillFailurePreservesCancellationAndBoundsCleanup`
  - `RunAsyncKillFailurePreservesTimeoutAndBoundsCleanup`
  - `RunAsyncKillFailurePreservesOutputLimitAndBoundsCleanup`
  - `RunAsyncTimeoutRemainsPrimaryWhenCallerCancelsAfterTimeoutBeforeLaunchCompletes`
- Run the Platform test project immediately.

## Phase 2 — Git and NuGet runtime cancellation

- Pass `CliRuntimeOptions.CancellationToken` to every Phase 8 Git and Phase 10 NuGet
  configure, doctor, and unconfigure operation.
- Pass the runtime token through the Git credential-helper adapter acquisition path.
- Add parameterized CLI tests for Git and NuGet configure/unconfigure, dedicated Git and NuGet
  doctor cancellation tests, and an adapter token-capture test for Git protocol acquisition:
  - `GitCliOperationsPropagateRuntimeCancellation`
  - `NuGetCliOperationsPropagateRuntimeCancellation`
  - `NuGetDoctorPropagatesCancellationDuringCredentialProbe`
  - `GitCredentialHelperProtocolPropagatesRuntimeCancellation`
  - `GetPropagatesRuntimeCancellationToCredentialAcquisition`
- Run the CLI and Platform test projects immediately.

## Phase 3 — NuGet per-request cancellation

- Refactor NuGet handlers to accept the request token asynchronously.
- Pass that token to credential acquisition and `SendResponseAsync`.
- Expose request-handler creation internally for direct deterministic tests.
- Add exact tests proving the same request token reaches acquisition and response sending.
- Add cancellation-during-acquisition coverage proving response sending is not attempted afterward.
- Run the Platform test project immediately.

## Phase 4 — Validation and quality gate

- Build `dirs.proj` non-incrementally.
- Run fresh tests from `dirs.proj` if feasible.
- Invoke `test-gap-analysis` and `assertion-quality`.
- Manually map every acceptance item to exact tests/assertions and record results in
  `.testagent/status.md`.
