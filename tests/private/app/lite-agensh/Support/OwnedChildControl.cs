using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace LiteAgensh.Tests.Support;

internal sealed record OwnedChildIdentity(
    int Pid,
    long BirthTicks,
    int DescendantPid,
    long DescendantBirthTicks
);

// Only the owned-helper protocol and its own directly launched children belong here.
// ProcessRunner's command task remains the consuming test's cancellation/drain responsibility.
internal sealed class OwnedChildControl : IAsyncDisposable
{
    internal const string ControlledNativeHostRole = "controlled-native-host";
    internal const string HeldCandidateCheckRole = "held-candidate-check";
    private readonly NamedPipeServerStream pipe;
    private StreamReader? reader;
    private Process? directProcess;
    private string? directRole;
    private long? directlyStarted;
    internal int? ObservedExitCode { get; private set; }

    internal OwnedChildControl()
    {
        PipeName = $"lite-agensh-own-{Guid.NewGuid():N}";
        pipe = new(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
    }

    internal static string ExecutablePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "LiteAgensh.TestProcess.exe" : "LiteAgensh.TestProcess"
        );

    internal string PipeName { get; }

    internal string[] Arguments(
        int exitCode = 0,
        string? descendantPipe = null,
        string? role = null
    )
    {
        if (role is not (null or ControlledNativeHostRole or HeldCandidateCheckRole))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown owned-helper role.");
        }
        if (role is not null)
        {
            if (descendantPipe is not null)
            {
                throw new ArgumentException(
                    "Controlled helper roles do not create descendants.",
                    nameof(descendantPipe)
                );
            }
            return [role, PipeName, exitCode.ToString(CultureInfo.InvariantCulture)];
        }
        return descendantPipe is null
            ? ["hold", PipeName, exitCode.ToString(CultureInfo.InvariantCulture)]
            : ["hold", PipeName, exitCode.ToString(CultureInfo.InvariantCulture), descendantPipe];
    }

    internal Process StartDirect(
        int exitCode = 0,
        string? descendantPipe = null,
        string? role = null
    )
    {
        if (directProcess is not null)
        {
            throw new InvalidOperationException(
                "This control already owns a directly started child."
            );
        }

        ProcessStartInfo start = new(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments(exitCode, descendantPipe, role))
        {
            start.ArgumentList.Add(argument);
        }

        directRole = role ?? "default";
        directlyStarted = Stopwatch.GetTimestamp();
        directProcess =
            Process.Start(start)
            ?? throw new InvalidOperationException("Own built helper did not start.");
        return directProcess;
    }

    internal async Task<OwnedChildIdentity> ReadReadyAsync(CancellationToken cancellationToken)
    {
        using (
            CancellationTokenSource connectionStage =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        )
        {
            await ObserveDirectReadinessStageAsync(
                pipe.WaitForConnectionAsync(connectionStage.Token),
                connectionStage, cancellationToken
            ).ConfigureAwait(false);
        }
        reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        using CancellationTokenSource readyStage = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        Task<string?> line = reader.ReadLineAsync(readyStage.Token).AsTask();
        await ObserveDirectReadinessStageAsync(line, readyStage, cancellationToken)
            .ConfigureAwait(false);
        return ParseReady(await line.ConfigureAwait(false));
    }

    private static async Task ObserveDirectReadinessStageAsync(
        Task operation,
        CancellationTokenSource stage,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // The observation's winner is independent of how late I/O responds to cancellation.
            await operation.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            OperationCanceledException? callerCancellation = null;
            if (
                error is OperationCanceledException ioCancellation
                && cancellationToken.IsCancellationRequested
                && ioCancellation.CancellationToken != cancellationToken
                && IsStageCancellation(ioCancellation)
            )
            {
                callerCancellation = new(
                    ioCancellation.Message, ioCancellation, cancellationToken
                );
            }

            List<Exception> secondary = [];
            try
            {
                await stage.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                secondary.Add(cleanup);
            }
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) when (IsStageCancellation(canceled)) { }
            catch (Exception cleanup)
            {
                if (!ReferenceEquals(cleanup, error))
                {
                    secondary.Add(cleanup);
                }
            }
            if (secondary.Count != 0)
            {
                (callerCancellation ?? error).Data["OwnedReadinessObservationCleanup"] =
                    Json.ExceptionDetails(
                        new AggregateException(
                            "Owned readiness observation cleanup failed.", secondary
                        )
                    );
            }
            if (callerCancellation is not null)
            {
                throw callerCancellation;
            }
            throw;
        }

        bool IsStageCancellation(OperationCanceledException error) =>
            operation.IsCanceled
            && stage.IsCancellationRequested
            && (
                error.CancellationToken == stage.Token
                || !error.CancellationToken.CanBeCanceled
                || (error.CancellationToken == cancellationToken
                    && cancellationToken.IsCancellationRequested)
            );
    }

    // Only the product-owned held-check producer uses this prelaunch-aware path.
    // The caller supplies its case-entry deadline, not merely an unbounded host token.
    internal async Task<OwnedChildIdentity> ReadReadyAsync(
        Task producer,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource observation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task connection = pipe.WaitForConnectionAsync(observation.Token);
        Task<string?>? line = null;
        Exception? primary = null;
        OwnedChildIdentity? result = null;
        AggregateException? cleanupFailure = null;
        string stage = "connection";
        try
        {
            await Task.WhenAny(producer, connection)
                .WaitAsync(observation.Token)
                .ConfigureAwait(false);
            // Deliberately prefer an actual terminal producer, including simultaneous completion.
            await RequireLiveProducerAsync(producer).ConfigureAwait(false);
            await connection.ConfigureAwait(false);
            observation.Token.ThrowIfCancellationRequested();
            stage = "ready-frame";
            reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            // The original five-second READY stage starts only after actual connection.
            observation.CancelAfter(TimeSpan.FromSeconds(5));
            line = reader.ReadLineAsync(observation.Token).AsTask();
            await Task.WhenAny(producer, line)
                .WaitAsync(observation.Token)
                .ConfigureAwait(false);
            await RequireLiveProducerAsync(producer).ConfigureAwait(false);
            result = ParseReady(await line.ConfigureAwait(false));
            await RequireLiveProducerAsync(producer).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            try
            {
                Console.Error.WriteLine(
                    "OWNED_READINESS_FAILURE "
                        + new JsonObject
                        {
                            ["stage"] = stage,
                            ["producerTaskStatus"] = producer.Status.ToString(),
                            ["connectionTaskStatus"] = connection.Status.ToString(),
                            ["readyTaskStatus"] = line?.Status.ToString(),
                            ["errorType"] = error.GetType().FullName,
                        }.ToJsonString()
                );
            }
            catch (Exception secondary)
            {
                error.Data["OwnedReadinessCaptureError"] = Json.ExceptionDetails(secondary);
            }
            throw;
        }
        finally
        {
            List<Exception> failures = [];
            try
            {
                await observation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            // Join the underlying I/O tasks themselves, not just WhenAny/WaitAsync wrappers.
            foreach (Task? pending in new Task?[] { connection, line })
            {
                if (pending is null)
                {
                    continue;
                }
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (observation.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    if (!ReferenceEquals(error, primary))
                    {
                        failures.Add(error);
                    }
                }
            }
            if (failures.Count != 0)
            {
                AggregateException cleanup = new(
                    "Owned readiness observation cleanup failed.", failures
                );
                if (primary is null)
                {
                    cleanupFailure = cleanup;
                }
                else
                {
                    primary.Data["OwnedReadinessObservationCleanup"] =
                        Json.ExceptionDetails(cleanup);
                }
            }
        }
        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
        return result!;
    }

    private static async Task RequireLiveProducerAsync(Task producer)
    {
        if (producer.IsCompleted)
        {
            // Propagate its real fault/cancellation; successful exit is not a held check.
            await producer.ConfigureAwait(false);
            throw new InvalidOperationException(
                "The held-check producer completed before a live held check was established."
            );
        }
    }

    internal static OwnedChildIdentity ParseReady(string? line)
    {
        string[] parts = line is { Length: > 0 and <= 128 } ? line.Split('|') : [];
        if (
            line is null
            || line.Length > 128
            || parts.Length != 5
            || parts[0] != "READY"
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || pid <= 0
            || !long.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long birth
            )
            || birth <= 0
            || !int.TryParse(
                parts[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int descendantPid
            )
            || descendantPid < 0
            || !long.TryParse(
                parts[4],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long descendantBirth
            )
            || ((descendantPid == 0) != (descendantBirth == 0))
            || descendantBirth < 0
        )
        {
            throw new InvalidOperationException(
                "Invalid owned-helper readiness: "
                    + new JsonObject
                    {
                        ["stage"] = "ready-frame",
                        ["frameKind"] = line switch
                        {
                            null => "eof",
                            "" => "empty",
                            { Length: > 128 } => "oversized",
                            _ => "malformed",
                        },
                        ["frameLength"] = line?.Length,
                        ["maximumFrameLength"] = 128,
                    }.ToJsonString()
            );
        }

        return new(pid, birth, descendantPid, descendantBirth);
    }

    internal async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        RecordProtocol("release-attempt");
        try
        {
            if (reader is null)
            {
                throw new InvalidOperationException(
                    "Release requires the actual readiness connection."
                );
            }

            await pipe
                .WriteAsync("RELEASE\n"u8.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            RecordProtocol("release-written");
        }
        catch (Exception error)
        {
            RecordProtocol("release-error", error);
            throw;
        }
    }

    private void RecordProtocol(string phase, Exception? error = null)
    {
        JsonObject receipt = new()
        {
            ["phase"] = phase,
            ["role"] = directRole,
            ["ageMilliseconds"] = directlyStarted is { } began
                ? Stopwatch.GetElapsedTime(began).TotalMilliseconds
                : null,
            ["error"] = error is null ? null : Json.ExceptionDetails(error),
        };
        try
        {
            receipt["pid"] = directProcess?.Id;
            receipt["birthTicks"] = directProcess?.StartTime.ToUniversalTime().Ticks;
            bool? exited = directProcess?.HasExited;
            receipt["observedExited"] = exited;
            receipt["observedExitCode"] = exited == true ? directProcess!.ExitCode : null;
        }
        catch (Exception inspectionError)
        {
            receipt["inspectionError"] = Json.ExceptionDetails(inspectionError);
        }
        Console.WriteLine("OWNED_CHILD_PROTOCOL " + receipt.ToJsonString());
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        bool readerDisposed = false,
            pipeDisposed = false;
        bool exitObserved = false,
            processDisposed = false,
            stopAttempted = false;
        int? pid = null;
        long? birth = null;
        try
        {
            reader?.Dispose();
            readerDisposed = true;
        }
        catch (Exception error)
        {
            error.Data["ownedCleanupStep"] = "reader-dispose";
            failures.Add(error);
        }
        try
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            pipeDisposed = true;
        }
        catch (Exception error)
        {
            error.Data["ownedCleanupStep"] = "pipe-dispose";
            failures.Add(error);
        }
        if (directProcess is not null)
        {
            try
            {
                pid = directProcess.Id;
                birth = directProcess.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception error)
            {
                error.Data["ownedCleanupStep"] = "owned-identity";
                failures.Add(error);
            }
            try
            {
                if (!directProcess.HasExited)
                {
                    stopAttempted = true;
                    directProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception error)
            {
                error.Data["ownedCleanupStep"] = "owned-check-stop";
                failures.Add(error);
            }
            try
            {
                await directProcess
                    .WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
                exitObserved = directProcess.HasExited;
                ObservedExitCode = directProcess.ExitCode;
            }
            catch (Exception error)
            {
                error.Data["ownedCleanupStep"] = "owned-exit-join";
                failures.Add(error);
            }
            try
            {
                directProcess.Dispose();
                processDisposed = true;
            }
            catch (Exception error)
            {
                error.Data["ownedCleanupStep"] = "owned-process-dispose";
                failures.Add(error);
            }
        }
        Console.WriteLine(
            "OWNED_CHILD_CLEANUP "
                + new JsonObject
                {
                    ["pipe"] = PipeName,
                    ["directlyOwned"] = directProcess is not null,
                    ["pid"] = pid,
                    ["birthTicks"] = birth,
                    ["role"] = directRole,
                    ["ageMilliseconds"] = directlyStarted is { } began
                        ? Stopwatch.GetElapsedTime(began).TotalMilliseconds
                        : null,
                    ["observedExitCode"] = ObservedExitCode,
                    ["readerDisposed"] = readerDisposed,
                    ["pipeDisposed"] = pipeDisposed,
                    ["stopAttempted"] = stopAttempted,
                    ["ownedExitObserved"] = exitObserved,
                    ["ownedProcessDisposed"] = processDisposed,
                    ["failureCount"] = failures.Count,
                }.ToJsonString()
        );
        if (failures.Count != 0)
        {
            throw new AggregateException("Owned helper cleanup failed.", failures);
        }
    }
}
