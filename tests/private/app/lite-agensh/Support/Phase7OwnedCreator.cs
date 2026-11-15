using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Only the interrupted original owner. The native hosts have normally exited
// before CHECKPOINT; killing this exact owner must never substitute for that.
internal sealed class Phase7OwnedCreator : IAsyncDisposable
{
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private readonly NamedPipeServerStream pipe;
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private Task<string>? stdout,
        stderr;
    private bool joined,
        deliberateCrash;
    private int? observedExitCode;
    private Task? cleanupTask;
    internal GitScenarioState Git { get; }
    internal Dictionary<string, string?> Environment { get; }
    internal RunConfiguration Configuration { get; }
    internal JsonObject Hello { get; private set; } = new();
    internal JsonObject Checkpoint { get; private set; } = new();
    internal string Root => Path.GetDirectoryName(Configuration.StatePath)!;
    internal string JournalPath => Path.Combine(Root, "controlled-operations.json");
    internal string SessionId =>
        Checkpoint["target"]![nameof(CreditReservation.SessionId)]!.GetValue<string>();
    internal string ReservationId => Checkpoint["target"]!["Id"]!.GetValue<string>();

    private Phase7OwnedCreator(
        GitScenarioState git,
        Dictionary<string, string?> environment,
        bool minimum
    )
    {
        Git = git;
        Environment = environment;
        string root = git.PathFor("phase7-original");
        Directory.CreateDirectory(root);
        Configuration = git.Database.Configuration with
        {
            StatePath = Path.Combine(root, "state"),
            CampaignLedgerPath = Path.Combine(root, "campaign.sqlite"),
            NativeModel = "controlled-model",
            WorkerCount = 1,
            PlanningSeconds = 0,
            DurationSeconds = 1800,
            WorkerStartIntervalSeconds = 0,
            RunAiCreditAllowance = minimum ? 37.25m : 207.25m,
            CampaignAiCreditAllowance = 407.25m,
        };
        pipe = new(
            "lite-phase7-creator-" + nonce,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
    }

    internal static async Task<Phase7OwnedCreator> OpenAsync(bool minimum, CancellationToken token)
    {
        Dictionary<string, string?> environment = Phase6State.CaptureEnvironment();
        GitScenarioState git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
        Phase7OwnedCreator? creator = null;
        try
        {
            creator = new(git, environment, minimum);
            await creator.StartAsync(token).ConfigureAwait(false);
            return creator;
        }
        catch (Exception primary)
        {
            Console.Error.WriteLine(
                "PHASE7_ORIGINAL_SETUP_PRIMARY " + Json.ExceptionDetails(primary)
            );
            List<Exception> secondary = [];
            if (creator is not null)
            {
                try
                {
                    await creator.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    secondary.Add(error);
                }
            }
            RestoreFailedEnvironment(git, environment, secondary);
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "PHASE7_ORIGINAL_SETUP_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            throw;
        }
    }

    // Own the acquisition edge BEFORE Attach. The inner fixture still settles
    // this creator before Git disposal; this cached fallback also covers an
    // Attach/prelude failure without letting cleanup replace its primary.
    internal async Task RunOwnedAsync(Func<Task> body)
    {
        Exception? primary = null;
        List<Exception> secondary = [];
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            Console.Error.WriteLine(
                "PHASE7_ORIGINAL_LIFETIME_PRIMARY " + Json.ExceptionDetails(error)
            );
            throw;
        }
        finally
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
            if (primary is not null || secondary.Count != 0)
            {
                RestoreFailedEnvironment(Git, Environment, secondary);
            }
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "PHASE7_ORIGINAL_LIFETIME_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
        }
        // A primary rethrows through finally; only a successful body reaches
        // this cleanup-failure propagation.
        if (secondary.Count != 0)
        {
            throw new AggregateException("Original owner lifetime cleanup failed.", secondary);
        }
    }

    private static void RestoreFailedEnvironment(
        GitScenarioState git,
        Dictionary<string, string?> expected,
        List<Exception> secondary
    )
    {
        bool restored = false,
            verified = false;
        try
        {
            git.RestoreEnvironment();
            restored = true;
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        try
        {
            foreach ((string name, string? value) in expected)
            {
                if (
                    !string.Equals(
                        value,
                        System.Environment.GetEnvironmentVariable(name),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        "Git environment baseline mismatch: " + name
                    );
                }
            }
            verified = true;
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        Console.WriteLine(
            "PHASE7_ORIGINAL_ENVIRONMENT_CLEANUP "
                + new JsonObject
                {
                    ["restoreAttempted"] = true,
                    ["restoreReturned"] = restored,
                    ["baselineVerificationAttempted"] = true,
                    ["baselineVerified"] = verified,
                    ["rootPreserved"] = git.Repository,
                    ["rootDisposalAttempted"] = false,
                }.ToJsonString()
        );
    }

    private async Task StartAsync(CancellationToken token)
    {
        string configurationPath = Path.Combine(Root, "configuration.json");
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(Configuration, Json.Options));
        ProcessStartInfo start = new(Phase6OwnedCreator.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            string argument in new[]
            {
                "phase7-exact",
                "lite-phase7-creator-" + nonce,
                nonce,
                configurationPath,
            }
        )
        {
            start.ArgumentList.Add(argument);
        }
        process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Original owner did not start.");
        stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await pipe.WaitForConnectionAsync(token)
            .WaitAsync(TimeSpan.FromSeconds(5), token)
            .ConfigureAwait(false);
        reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        Hello = await ReceiveAsync("HELLO", token).ConfigureAwait(false);
        Assert.AreEqual("phase7-interrupted-owner-setup", Hello["role"]!.GetValue<string>());
        Assert.AreEqual(process.Id, Hello["pid"]!.GetValue<int>());
        Assert.AreEqual(
            process.StartTime.ToUniversalTime().Ticks,
            Hello["birthTicks"]!.GetValue<long>()
        );
        Assert.AreEqual(
            Phase6OwnedCreator.Hash(Path.ChangeExtension(Phase6OwnedCreator.Executable, ".dll")),
            Hello["moduleSha256"]!.GetValue<string>()
        );
        Assert.AreEqual(
            Phase6OwnedCreator.Hash(typeof(Coordinator).Assembly.Location),
            Hello["productSha256"]!.GetValue<string>()
        );
        await writer
            .WriteLineAsync(
                new JsonObject
                {
                    ["version"] = 1,
                    ["nonce"] = nonce,
                    ["stage"] = "START",
                }
                    .ToJsonString()
                    .AsMemory(),
                token
            )
            .ConfigureAwait(false);
        Checkpoint = await ReceiveAsync("CHECKPOINT", token).ConfigureAwait(false);
        Assert.IsFalse(process.HasExited);
        Assert.IsFalse(Checkpoint["publicOperationReturned"]!.GetValue<bool>());
        Assert.AreEqual(1L, Checkpoint["unfinished"]!.GetValue<long>());
        Assert.AreEqual(1L, Checkpoint["segmentCount"]!.GetValue<long>());
        Assert.AreEqual("conflict", Checkpoint["ownerProbe"]!["code"]!.GetValue<string>());
        JsonObject native = Checkpoint["nativeHost"]!.AsObject();
        Assert.IsTrue(native["normalExitBeforeWrapperDispose"]!.GetValue<bool>());
        Assert.IsTrue(native["wrapperDisposed"]!.GetValue<bool>());
        Assert.AreEqual(0, native["observedExitCode"]!.GetValue<int>());
        Assert.AreNotEqual(process.Id, native["readyPid"]!.GetValue<int>());
        Assert.AreEqual(10m, Checkpoint["target"]!["Actual"]!.GetValue<decimal>());
        Assert.AreEqual("reconciled", Checkpoint["target"]!["State"]!.GetValue<string>());
        Assert.AreEqual(1L, Checkpoint["history"]!["requests"]!.GetValue<long>());
        Assert.IsFalse(Checkpoint["history"]!["pending"]!.GetValue<bool>());
        Assert.AreEqual(3, Checkpoint["history"]!["generations"]![0]!["events"]!.AsArray().Count);
        Console.WriteLine(
            "PHASE7_ORIGINAL_CHECKPOINT "
                + new JsonObject
                {
                    ["root"] = Root,
                    ["hello"] = Hello.DeepClone(),
                    ["checkpoint"] = Checkpoint.DeepClone(),
                    ["journalSha256"] = Phase6OwnedCreator.Hash(JournalPath),
                }.ToJsonString()
        );
    }

    internal async Task CrashAsync(CancellationToken token)
    {
        Assert.IsFalse(joined);
        Assert.IsNotNull(process);
        Assert.AreEqual(Hello["pid"]!.GetValue<int>(), process.Id);
        Assert.AreEqual(
            Hello["birthTicks"]!.GetValue<long>(),
            process.StartTime.ToUniversalTime().Ticks
        );
        Assert.IsFalse(process.HasExited);
        // All actual native host processes were normally joined before this
        // point. Kill only the creator identity, not an anonymous process name.
        process.Kill();
        await process
            .WaitForExitAsync(token)
            .WaitAsync(TimeSpan.FromSeconds(5), token)
            .ConfigureAwait(false);
        joined = true;
        observedExitCode = process.ExitCode;
        Assert.AreNotEqual(0, observedExitCode);
        deliberateCrash = true;
        Console.WriteLine(
            "PHASE7_EXPECTED_CREATOR_CRASH "
                + new JsonObject
                {
                    ["pid"] = process.Id,
                    ["birthTicks"] = Hello["birthTicks"]!.DeepClone(),
                    ["nonce"] = nonce,
                    ["exitCode"] = observedExitCode,
                    ["joined"] = joined,
                    ["nativeNormalSettlementWasEarlier"] = true,
                }.ToJsonString()
        );
    }

    private async Task<JsonObject> ReceiveAsync(string stage, CancellationToken token)
    {
        string? line = await reader!
            .ReadLineAsync(token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), token)
            .ConfigureAwait(false);
        Assert.IsNotNull(line, "Original creator disconnected before " + stage);
        Assert.IsLessThanOrEqualTo(1_048_576, line.Length);
        JsonObject message = JsonNode.Parse(line)!.AsObject();
        Assert.AreEqual(1, message["version"]!.GetValue<int>());
        Assert.AreEqual(nonce, message["nonce"]!.GetValue<string>());
        Assert.AreEqual(stage, message["stage"]!.GetValue<string>());
        return message["data"]!.AsObject();
    }

    public ValueTask DisposeAsync() => new(cleanupTask ??= CleanupAsync());

    private async Task CleanupAsync()
    {
        List<Exception> errors = [];
        JsonObject stages = new();
        int? ownedPid = process?.Id;
        async Task Attempt(string stage, Func<Task> action)
        {
            stages[stage] = "attempted";
            try
            {
                await action().ConfigureAwait(false);
                stages[stage] = "completed";
            }
            catch (Exception error)
            {
                stages[stage] = "failed";
                errors.Add(error);
                Console.Error.WriteLine(
                    "PHASE7_ORIGINAL_CLEANUP_STAGE_ERROR "
                        + stage
                        + " "
                        + Json.ExceptionDetails(error)
                );
            }
        }
        if (process is not null)
        {
            await Attempt(
                    "creatorJoin",
                    async () =>
                    {
                        if (!joined)
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                            }
                            await process
                                .WaitForExitAsync(CancellationToken.None)
                                .WaitAsync(TimeSpan.FromSeconds(5))
                                .ConfigureAwait(false);
                            joined = true;
                            observedExitCode = process.ExitCode;
                            Console.WriteLine(
                                "PHASE7_ORIGINAL_EXCEPTIONAL_EXIT "
                                    + $"pid={process.Id} exit={observedExitCode}; "
                                    + "not normal native settlement"
                            );
                        }
                    }
                )
                .ConfigureAwait(false);
            // Cleanup of registered actual owned descendants is separately
            // attempted even if the creator join or a pipe operation failed.
            await Attempt(
                    "registeredDescendants",
                    async () =>
                    {
                        RunDatabase database = new(Configuration.StatePath);
                        await new ProcessRunner(database)
                            .ResolveSurvivorsAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["creatorJoin"] = "not-acquired";
            stages["registeredDescendants"] = "no-creator-acquired";
        }
        // Same existing five-second cleanup budget, shared by both drains.
        // A failed/unknown join or an inherited open stream cannot prevent the
        // remaining disposal stages or the outer environment restoration.
        using CancellationTokenSource drains = new(TimeSpan.FromSeconds(5));
        if (stdout is not null)
        {
            await Attempt(
                    "stdoutDrain",
                    async () =>
                        Console.WriteLine(
                            "PHASE7_CREATOR_STDOUT "
                                + await stdout.WaitAsync(drains.Token).ConfigureAwait(false)
                        )
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["stdoutDrain"] = "not-acquired";
        }
        if (stderr is not null)
        {
            await Attempt(
                    "stderrDrain",
                    async () =>
                        Console.WriteLine(
                            "PHASE7_CREATOR_STDERR "
                                + await stderr.WaitAsync(drains.Token).ConfigureAwait(false)
                        )
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["stderrDrain"] = "not-acquired";
        }
        if (writer is not null)
        {
            await Attempt(
                    "writerDispose",
                    () =>
                    {
                        writer.Dispose();
                        return Task.CompletedTask;
                    }
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["writerDispose"] = "not-acquired";
        }
        if (reader is not null)
        {
            await Attempt(
                    "readerDispose",
                    () =>
                    {
                        reader.Dispose();
                        return Task.CompletedTask;
                    }
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["readerDispose"] = "not-acquired";
        }
        await Attempt("pipeDispose", () => pipe.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (process is not null)
        {
            await Attempt(
                    "processDispose",
                    () =>
                    {
                        process.Dispose();
                        return Task.CompletedTask;
                    }
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["processDispose"] = "not-acquired";
        }
        Console.WriteLine(
            "PHASE7_ORIGINAL_RESOURCE_CLEANUP "
                + new JsonObject
                {
                    ["pid"] = ownedPid,
                    ["birthTicks"] = Hello["birthTicks"]?.DeepClone(),
                    ["creatorJoinObserved"] = joined,
                    ["observedCreatorExitCode"] = observedExitCode,
                    ["deliberateCrashJoined"] = deliberateCrash,
                    ["stages"] = stages,
                    ["secondaryCount"] = errors.Count,
                }.ToJsonString()
        );
        foreach (Exception error in errors)
        {
            Console.Error.WriteLine(
                "PHASE7_ORIGINAL_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
            );
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Original owner cleanup failed.", errors);
        }
    }
}
