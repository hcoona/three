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
    private StreamWriter? writer;
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
        Path.Combine(AppContext.BaseDirectory, "LiteAgensh.TestProcess.exe");

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
        await pipe.WaitForConnectionAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);
        reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        string? line = await reader
            .ReadLineAsync(cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);
        return ParseReady(line);
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
        try
        {
            await Task.WhenAny(producer, connection)
                .WaitAsync(observation.Token)
                .ConfigureAwait(false);
            // Deliberately prefer an actual terminal producer, including simultaneous completion.
            await RequireLiveProducerAsync(producer).ConfigureAwait(false);
            await connection.ConfigureAwait(false);
            observation.Token.ThrowIfCancellationRequested();
            reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
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

    private static OwnedChildIdentity ParseReady(string? line)
    {
        string[] parts = line?.Split('|') ?? [];
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
            throw new InvalidOperationException($"Invalid owned-helper readiness: {line}");
        }

        return new(pid, birth, descendantPid, descendantBirth);
    }

    internal async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        RecordProtocol("release-attempt");
        try
        {
            if (writer is null)
            {
                throw new InvalidOperationException(
                    "Release requires the actual readiness connection."
                );
            }

            await writer
                .WriteLineAsync("RELEASE".AsMemory(), cancellationToken)
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
        bool writerDisposed = false,
            readerDisposed = false,
            pipeDisposed = false;
        bool exitObserved = false,
            processDisposed = false,
            stopAttempted = false;
        int? pid = null;
        long? birth = null;
        try
        {
            writer?.Dispose();
            writerDisposed = true;
        }
        catch (Exception error)
        {
            error.Data["ownedCleanupStep"] = "writer-dispose";
            failures.Add(error);
        }
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
                    ["writerDisposed"] = writerDisposed,
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
