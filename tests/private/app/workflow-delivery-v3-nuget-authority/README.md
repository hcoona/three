# NuGet authority process tests

`AuthorityProtocolTests` exercises the built authority through its stdin/stdout
protocol. Each invocation has a two-minute execution deadline. A failed invocation
attempts diagnostic collection before terminating the child process tree; dump
collection has a separate 30-second deadline and cleanup waits are limited to
10 seconds each. Cooperative test-run cancellation skips or interrupts dump collection and proceeds
to bounded cleanup. Forced runner termination can interrupt that cleanup.

Each invocation writes a uniquely named directory under
`NUGET_AUTHORITY_DIAGNOSTICS_DIRECTORY`, or the test results directory locally:

- `process.log`: UTC and elapsed timestamps for launch, request write, stdin close,
  first output, EOF, exit, failure, dump collection and cleanup; command, PID,
  runner image, revision, runtime/OS and a failure-time CPU/thread snapshot.
- `stdout.txt` and `stderr.txt`: output flushed as it arrives, including partial
  output when the helper hangs.
- `host.txt`: .NET host tracing, including runtime selection and startup.
- `runtime.nettrace`: streaming EventPipe runtime events and sampled managed stacks,
  using the runtime's default providers and a 16 MiB buffer.
- `slow-process.txt`: marks a successful invocation taking at least 30 seconds,
  so a recurrence below the new failure deadline still retains its evidence.
- On failure while the child is alive, `hang.dmp` and `dump-stdout.txt` /
  `dump-stderr.txt`: a heap dump and collector diagnostics when
  `NUGET_AUTHORITY_DUMP_TOOL` points to a `dotnet-dump` executable. Missing tools,
  collection errors and cleanup failures are reported in `process.log`.

The Windows .NET CI job installs `dotnet-dump` 10.0.745401 and uploads diagnostics
and the matching helper binaries as `nuget-authority-diagnostics-<run-attempt>`
on failure or a slow successful invocation, retaining them for seven days. Whole-job
cancellation can prevent artifact upload. Download that artifact
before it expires. Use `process.log` and `host.txt` to locate the delayed phase;
use PerfView for the trace and WinDbg or `dotnet-dump analyze` for dump inspection.
Streaming retains evidence before forced termination, but a killed process can
leave an incomplete trace or dump; inspect collector results before relying on it.
EventPipe does not capture kernel scheduling or native CPU stacks. The artifact
supports investigation but does not itself establish why the hosted runner stalled.

To enable local dump capture in PowerShell:

```powershell
$diagnosticTools = Join-Path $env:TEMP "nuget-authority-tools"
dotnet tool install dotnet-dump --version 10.0.745401 --tool-path $diagnosticTools
$env:NUGET_AUTHORITY_DUMP_TOOL = Join-Path $diagnosticTools "dotnet-dump.exe"
$testProject = "tests/private/app/workflow-delivery-v3-nuget-authority/WorkflowDeliveryV3NuGetAuthority.Tests.csproj"
dotnet test --project $testProject -p:RestoreLockedMode=true -bl:{{}}
```

Collection uses the supported [.NET host and EventPipe environment variables](https://learn.microsoft.com/dotnet/core/tools/dotnet-environment-variables)
and [`dotnet-dump collect`](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-dump).
