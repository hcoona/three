using Authentication.Core;

namespace Authentication.Windows;

// One invocation per process. The calling thread is the watchdog: dependency
// prefixes, callbacks, native I/O and cleanup never execute on that thread.
public static class WindowsProcess
{
    // The unofficial display name shared by help and the hidden owned parent.
    internal const string DisplayName = "AzureAuth Unofficial V2 (hcoona/microsoft-authentication-cli)";
    // The command name selected in docs/governance/operational-identities.yaml.
    internal const string CommandName = "azureauth-unofficial";

    public static int Run(string[] arguments, long entryTimestamp)
    {
        var trace = new WindowsMechanismTrace();
        return RunDefault(arguments, entryTimestamp,
            new WindowsHostAdmission(new NativeWindowsHostObservations(), trace),
            token => WindowsLoader.RestrictSearch(token, trace), new MsalSessionFactory(trace),
            () => new MsalHttpClientFactory(new HttpClientHandler()), trace: trace);
    }

    internal static int Run(string[] arguments, long entryTimestamp, IRequestHost host,
        Func<ClientProfile, IAuthenticationProvider> createProvider, IProfileSource? profiles = null) =>
        RunCore(arguments, entryTimestamp, _ => host, (profile, _) => createProvider(profile), profiles, null);

    // Controlled constructors/checkpoints are internal to the scenario executable.
    // The product never selects them through arguments or environment variables.
    internal static int RunDefault(string[] arguments, long entryTimestamp,
        IWindowsHostAdmission admission, Action<CancellationToken> restrictDllSearch,
        IMsalSessionFactory sessions, Func<MsalHttpClientFactory> createHttp,
        Func<Func<Task>, Action, IWindowsHostAdmission, OwnedRequestHost>? createHost = null,
        Action<OwnedProcessCheckpoint>? checkpoint = null, WindowsMechanismTrace? trace = null)
    {
        MsalHttpClientFactory? http = null;
        return RunCore(arguments, entryTimestamp,
            process => createHost is null
                ? new OwnedRequestHost(process.Cancel, process.FailHost, admission: admission, notice: process.Notice)
                : createHost(process.Cancel, process.FailHost, admission),
            (profile, request) => new LocalWindowsProvider(admission, token =>
            {
                token.ThrowIfCancellationRequested();
                // Retain ownership before loader/session initialization can fail.
                http ??= createHttp();
                return MsalAuthenticationProvider.Initialize(profile, request, sessions, http,
                    restrictDllSearch, token, trace);
            }), null, checkpoint, () => http?.Dispose(), trace);
    }

    internal static int RunOwned(string[] arguments, long entryTimestamp,
        Func<ClientProfile, AuthenticationRequest, IAuthenticationProvider> createProvider,
        IProfileSource? profiles = null,
        Func<Func<Task>, Action, OwnedRequestHost>? createHost = null,
        Action<OwnedProcessCheckpoint>? checkpoint = null) =>
        RunCore(arguments, entryTimestamp,
            process => createHost is null
                ? new OwnedRequestHost(process.Cancel, process.FailHost, notice: process.Notice)
                : createHost(process.Cancel, process.FailHost),
            createProvider, profiles, checkpoint);

    private static int RunCore(string[] arguments, long entryTimestamp,
        Func<ProcessState, IRequestHost> createHost,
        Func<ClientProfile, AuthenticationRequest, IAuthenticationProvider> createProvider,
        IProfileSource? profiles, Action<OwnedProcessCheckpoint>? checkpoint, Action? disposeHttp = null,
        WindowsMechanismTrace? trace = null)
    {
        var process = new ProcessState(entryTimestamp);
        try
        {
            new Thread(() => Execute(process, arguments, entryTimestamp, createHost, createProvider,
                profiles ?? new WindowsProfileSource(), checkpoint, disposeHttp, trace)) { IsBackground = true }.Start();

            while (true)
            {
                var completion = process.Completion;
                if (process.ExceededBound(completion?.Timestamp ?? TimeProvider.System.GetTimestamp()))
                {
                    // A complete written result fixes the exit status. Later cleanup,
                    // drain or diagnostic failure ends the process but cannot change it.
                    var exitCode = process.Delivered?.ExitCode ?? 2;
                    WindowsStandardHandles.Terminate(exitCode);
                    return exitCode;
                }
                if (completion is not null) return completion.ExitCode;
                Thread.Sleep(5);
            }
        }
        catch (Exception)
        {
            var exitCode = process.Delivered?.ExitCode ?? 2;
            WindowsStandardHandles.Terminate(exitCode);
            return exitCode;
        }
    }

    private static void Execute(ProcessState process, string[] arguments, long entryTimestamp,
        Func<ProcessState, IRequestHost> createHost,
        Func<ClientProfile, AuthenticationRequest, IAuthenticationProvider> createProvider,
        IProfileSource profiles, Action<OwnedProcessCheckpoint>? checkpoint, Action? disposeHttp,
        WindowsMechanismTrace? trace)
    {
        var exitCode = 2;
        RequestInvocation? invocation = null;
        OwnedRequestHost? ownedHost = null;
        WindowsLifetimePipe? pipe = null;
        LocalActivityTelemetry? observation = null;
        ConsoleCancelEventHandler cancelHandler = (_, notification) =>
        {
            notification.Cancel = true;
            try { _ = process.Cancel(); }
            catch (Exception) { }
        };
        try
        {
            if (arguments.Length == 1 && arguments[0] is "help" or "--help")
            {
                process.Ending();
                exitCode = WindowsStandardHandles.Write(WindowsStandardHandles.Output, Help) ? 0 : 2;
                return;
            }

            if (arguments.Length == 1 && arguments[0] == "--version")
            {
                process.Ending();
                exitCode = WindowsStandardHandles.Write(WindowsStandardHandles.Output, VersionText) ? 0 : 2;
                return;
            }

            var host = createHost(process);
            ownedHost = host as OwnedRequestHost;
            invocation = new RequestInvocation(arguments, host, entryTimestamp, process.CancellationToken);
            process.Publish(invocation, ownedHost);
            observation = WindowsDiagnostics.Start(invocation.Request?.TelemetryStderr == true, entryTimestamp);
            process.PublishObservation(observation);
            Console.CancelKeyPress += cancelHandler;
            if (invocation.Request?.CancelOnStdinClose == true)
            {
                pipe = new WindowsLifetimePipe(process.Cancel);
                pipe.Start();
            }

            _ = invocation.RunAsync(profiles, (profile, request) =>
            {
                if (host is OwnedRequestHost owned) owned.BindProfile(profile);
                return createProvider(profile, request);
            }, pipe?.Admission).GetAwaiter().GetResult();
            checkpoint?.Invoke(OwnedProcessCheckpoint.BeforeCommit);
            if (!process.TryCommit(invocation, out var result)) return;
            checkpoint?.Invoke(OwnedProcessCheckpoint.AfterCommit);
            pipe?.Stop();
            // Deliver the authoritative result before any optional diagnostic work,
            // including the token-shape observation, can consume the shutdown bound.
            // A failed write delivers no result, so no completion indication,
            // token-shape observation or request-span stop event may follow it.
            // Closing local telemetry before the drain drops that later event.
            if (!WindowsStandardHandles.Write(WindowsStandardHandles.Output, result!.Utf8Json))
            {
                observation?.Dispose();
                return;
            }
            exitCode = result.ExitCode;
            // Publish before any drain or diagnostic wait: from here on, the
            // watchdog can end the process only with this typed status.
            process.PublishDelivered(exitCode);
            process.TrackDiagnostic(WindowsDiagnostics.Completed(result, invocation.Request?.TelemetryStderr == true,
                entryTimestamp, trace, observation));
        }
        catch (Exception)
        {
            // Request-body exceptions already have the core's safe typed mapping.
            // Host/transport failure cannot fabricate a replacement result.
            process.Ending();
            // Nor can it replace a result already delivered.
            exitCode = process.Delivered?.ExitCode ?? 2;
        }
        finally
        {
            var drained = false;
            try
            {
                Console.CancelKeyPress -= cancelHandler;
                pipe?.Stop();
                // Dispose also terminates an invocation that failed before Run.
                // Pending operations/callbacks retain the original process bound.
                invocation?.Dispose();
                invocation?.CompleteAsync().GetAwaiter().GetResult();
                // The invocation must finish before this snapshot: pending work
                // could otherwise start a window after a thread-null observation.
                ownedHost?.Completion.GetAwaiter().GetResult();
                pipe?.Completion.GetAwaiter().GetResult();
                process.CancellationCompletion.GetAwaiter().GetResult();
                if (disposeHttp is not null)
                {
                    checkpoint?.Invoke(OwnedProcessCheckpoint.BeforeHttpDisposal);
                    disposeHttp();
                }
                drained = true;
            }
            catch (Exception)
            {
                // Required drain failed. A delivered result keeps its published status;
                // the watchdog ends the process when the shutdown bound expires.
                process.Ending();
            }
            observation?.Dispose();
            if (drained)
            {
                // If the bound expires while only diagnostics are pending, the watchdog
                // exits with the delivered status, so a blocked writer cannot change it.
                checkpoint?.Invoke(OwnedProcessCheckpoint.BeforeDiagnosticDrain);
                process.WaitForDiagnostics(observation);
                process.Finish(exitCode);
            }
        }
    }

    // Raw string literals take the line endings of the checked-out source. Normalize
    // them so the help bytes are LF-only whatever the checkout line-ending setting.
    internal static byte[] Help => System.Text.Encoding.UTF8.GetBytes($"""
        {DisplayName}
        Unofficial project: not a Microsoft product and not supported by Microsoft.
        Version {ProductVersion.Full}

        Usage:
          {CommandName} authenticate --protocol 1 --profile <absolute-Windows-path>
            --account-email <full-email> --scope <scope>
            --interaction <non-interactive-only|interactive-if-needed|force-interactive>
            [--scope <scope> ...] [--tenant <common|tenant-guid>]
            [--timeout-seconds <1..600>] [--cancel-on-stdin-close]
            [--telemetry <off|stderr>]
          {CommandName} --version

        --profile selects an explicit, existing Client Profile file.
        --cancel-on-stdin-close requires a dedicated caller-owned lifetime pipe.
        Authentication writes one protocol 1 JSON result to stdout.

        """.ReplaceLineEndings("\n"));

    // One line: the command name and the full SemVer 2.0 version with build metadata.
    internal static byte[] VersionText =>
        System.Text.Encoding.UTF8.GetBytes($"{CommandName} {ProductVersion.Full}\n");

    private sealed record ProcessCompletion(int ExitCode, long Timestamp);

    private sealed class ProcessState(long entryTimestamp)
    {
        private readonly object commitment = new();
        private readonly CancellationTokenSource callerCancellation = new();
        private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RequestInvocation? invocation;
        private OwnedRequestHost? ownedHost;
        private ProcessCompletion? completion;
        private long cancellationTimestamp = long.MinValue;
        private long endingTimestamp = long.MinValue;
        private int timeoutSeconds = 120;

        internal CancellationToken CancellationToken => callerCancellation.Token;
        internal ProcessCompletion? Completion => Volatile.Read(ref completion);
        // Set only after the complete typed result has been written to stdout.
        private ProcessCompletion? delivered;
        internal ProcessCompletion? Delivered => Volatile.Read(ref delivered);
        internal void PublishDelivered(int exitCode) => Volatile.Write(ref delivered,
            new ProcessCompletion(exitCode, TimeProvider.System.GetTimestamp()));
        internal Task CancellationCompletion => Volatile.Read(ref cancellationTimestamp) == long.MinValue
            ? Task.CompletedTask : cancelled.Task;

        private LocalActivityTelemetry? observation;
        private readonly List<Task> diagnostics = [];

        internal void PublishObservation(LocalActivityTelemetry? value) => Volatile.Write(ref observation, value);

        internal void TrackDiagnostic(Task write)
        {
            lock (diagnostics) diagnostics.Add(write);
        }

        internal void WaitForDiagnostics(LocalActivityTelemetry? local)
        {
            try
            {
                Task[] pending;
                lock (diagnostics) pending = [.. diagnostics];
                if (local is not null) pending = [.. pending, local.Completion];
                _ = Task.WaitAll(pending, RemainingShutdown());
            }
            catch (Exception) { }
        }

        // Never blocks the owned UI thread: telemetry enqueues, otherwise a
        // background thread performs the one bounded stderr write.
        internal void Notice(byte[] bytes)
        {
            if (Volatile.Read(ref observation) is { } local)
            {
                _ = local.TryWriteNotice(bytes);
                return;
            }
            TrackDiagnostic(WindowsDiagnostics.WriteError(bytes));
        }

        internal void Publish(RequestInvocation request, OwnedRequestHost? host)
        {
            Volatile.Write(ref timeoutSeconds, request.Request?.TimeoutSeconds ?? 120);
            Volatile.Write(ref invocation, request);
            Volatile.Write(ref ownedHost, host); // Publish before Run can produce host events.
        }

        internal Task Cancel()
        {
            var observed = TimeProvider.System.GetTimestamp();
            lock (commitment)
            {
                if (cancellationTimestamp == long.MinValue)
                {
                    Volatile.Write(ref cancellationTimestamp, observed);
                    _ = ObserveCancellationAsync();
                }
            }
            return cancelled.Task;
        }

        internal void FailHost()
        {
            void Notify(OwnedHostObservation observation)
            {
                lock (commitment)
                {
                    // Delayed fault forwarding must consume an earlier local
                    // cancellation before the core can select a competing failure.
                    if (observation == OwnedHostObservation.UserCancellation) _ = Cancel();
                    Volatile.Read(ref invocation)?.FailHost();
                }
            }

            var host = Volatile.Read(ref ownedHost);
            if (host is null) Notify(OwnedHostObservation.None);
            else host.WithLocalObservation(Notify);
        }

        private async Task ObserveCancellationAsync()
        {
            try { await callerCancellation.CancelAsync().ConfigureAwait(false); }
            catch (Exception) { }
            cancelled.TrySetResult();
        }

        internal bool TryCommit(RequestInvocation request, out SerializedResult? result)
        {
            SerializedResult? selected = null;
            var committed = false;
            void Commit(OwnedHostObservation observation)
            {
                // Lock order is owned host, process commitment, then core lifetime.
                // No provider work, native I/O or wait runs under these gates.
                lock (commitment)
                {
                    if (observation == OwnedHostObservation.UserCancellation) _ = Cancel();
                    else if (observation == OwnedHostObservation.HostFault) request.FailHost();
                    committed = request.TryCommitResult(out selected);
                }
            }

            var host = Volatile.Read(ref ownedHost);
            if (host is null) Commit(OwnedHostObservation.None);
            else host.WithLocalObservation(Commit);
            result = selected;
            return committed;
        }

        internal void Ending() => Interlocked.CompareExchange(ref endingTimestamp,
            TimeProvider.System.GetTimestamp(), long.MinValue);

        internal bool ExceededBound(long timestamp)
        {
            return RemainingBound(timestamp) < TimeSpan.Zero;
        }

        // Time left before the earliest armed process bound, less a fixed margin
        // that keeps Finish observably inside it for the watchdog's next poll.
        internal TimeSpan RemainingShutdown()
        {
            var remaining = RemainingBound(TimeProvider.System.GetTimestamp()) - TimeSpan.FromMilliseconds(50);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private TimeSpan RemainingBound(long timestamp)
        {
            var clock = TimeProvider.System;
            var remaining = TimeSpan.FromSeconds(Volatile.Read(ref timeoutSeconds) + 1)
                - clock.GetElapsedTime(entryTimestamp, timestamp);
            var terminal = Volatile.Read(ref invocation)?.TerminalTimestamp;
            var cancellation = Volatile.Read(ref cancellationTimestamp);
            var ending = Volatile.Read(ref endingTimestamp);
            var hostEnding = Volatile.Read(ref ownedHost)?.EndingTimestamp;
            foreach (var start in new[] { terminal ?? long.MinValue, cancellation, ending, hostEnding ?? long.MinValue })
            {
                if (start == long.MinValue) continue;
                var left = TimeSpan.FromSeconds(1) - clock.GetElapsedTime(start, timestamp);
                if (left < remaining) remaining = left;
            }
            return remaining;
        }

        internal void Finish(int exitCode) => Volatile.Write(ref completion,
            new ProcessCompletion(exitCode, TimeProvider.System.GetTimestamp()));
    }
}

internal enum OwnedProcessCheckpoint
{
    BeforeCommit,
    AfterCommit,
    BeforeHttpDisposal,
    BeforeDiagnosticDrain,
}
