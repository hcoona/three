using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Only the interrupted original owner. The native hosts have normally exited
// before CHECKPOINT; killing this exact owner must never substitute for that.
internal sealed class InterruptedRunCreator : IAsyncDisposable
{
    private readonly string nonce;
    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource outputCapture = new();
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private OwnedCreatorProcess.OutputCapture? stdout,
        stderr;
    private bool startMayHaveBeenDelivered,
        checkpointVerified,
        joined;
    private long? verifiedBirthTicks;
    private int? observedExitCode;
    private Task? cleanupTask;
    internal GitScenarioState Git { get; }
    internal FixtureDiagnosticSink Evidence { get; }
    internal RunConfiguration Configuration { get; }
    internal JsonObject Hello { get; private set; } = new();
    internal JsonObject Checkpoint { get; private set; } = new();
    internal string Root => Path.GetDirectoryName(Configuration.StatePath)!;
    internal string JournalPath => Path.Combine(Root, "controlled-operations.json");
    internal string SessionId =>
        Checkpoint["target"]![nameof(CreditReservation.SessionId)]!.GetValue<string>();
    internal string ReservationId => Checkpoint["target"]!["Id"]!.GetValue<string>();

    private InterruptedRunCreator(
        GitScenarioState git,
        bool minimum,
        FixtureDiagnosticSink evidence
    )
    {
        Git = git;
        Evidence = evidence;
        nonce = evidence.Id;
        string root = git.PathFor("interrupted-original");
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
            "lite-interrupted-creator-" + nonce,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
    }

    internal static async Task<InterruptedRunCreator> OpenAsync(
        TestContext context, bool minimum, CancellationToken token
    )
    {
        FixtureDiagnosticSink evidence = CoordinatorScenarioRun.CreateEvidence(
            context, Guid.NewGuid().ToString("N")
        );
        using Activity observation = evidence.BeginScope();
        GitScenarioState? git = null;
        InterruptedRunCreator? creator = null;
        try
        {
            evidence.Phase("git-setup", "started");
            git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
            evidence.Phase("git-setup", "completed");
            creator = new(git, minimum, evidence);
            await creator.StartAsync(token).ConfigureAwait(false);
            return creator;
        }
        catch (Exception primary)
        {
            evidence.Phase("original-owner-setup", "failed", error: primary);
            Console.Error.WriteLine(
                "INTERRUPTED_CREATOR_SETUP_PRIMARY " + Json.ExceptionDetails(primary)
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
            if (git is not null)
            {
                try
                {
                    GitScenarioState.AssertEnvironmentUnchanged();
                }
                catch (Exception error)
                {
                    secondary.Add(error);
                }
            }
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "INTERRUPTED_CREATOR_SETUP_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            evidence.Dispose();
            throw;
        }
    }

    internal async Task RunOwnedAsync(Func<Task> body)
    {
        using Activity observation = Evidence.BeginScope();
        Evidence.Phase("original-owner-scenario", "started");
        Exception? primary = null;
        List<Exception> secondary = [];
        try
        {
            await body().ConfigureAwait(false);
            Evidence.Phase("original-owner-scenario", "completed");
        }
        catch (Exception error)
        {
            primary = error;
            Evidence.Phase("original-owner-scenario", "failed", error: error);
            Console.Error.WriteLine(
                "INTERRUPTED_CREATOR_PRIMARY " + Json.ExceptionDetails(error)
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
                try
                {
                    GitScenarioState.AssertEnvironmentUnchanged();
                }
                catch (Exception error)
                {
                    secondary.Add(error);
                }
            }
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "INTERRUPTED_CREATOR_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            Evidence.Dispose();
        }
        if (secondary.Count != 0)
        {
            throw new AggregateException("Original owner lifetime cleanup failed.", secondary);
        }
    }

    private async Task StartAsync(CancellationToken token)
    {
        string phase = "creator-configuration";
        try
        {
            Evidence.Phase("creator-configuration", "started");
            string configurationPath = Path.Combine(Root, "configuration.json");
            File.WriteAllText(
                configurationPath, JsonSerializer.Serialize(Configuration, Json.Options)
            );
            Evidence.Phase("creator-configuration", "completed");
            ProcessStartInfo start = new(OwnedCreatorProcess.Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (
                string argument in new[]
                {
                "interrupted-exact",
                "lite-interrupted-creator-" + nonce,
                nonce,
                configurationPath,
                }
            )
            {
                start.ArgumentList.Add(argument);
            }
            phase = "creator-launch";
            Evidence.Phase(phase, "started");
            process =
                Process.Start(start)
                ?? throw new InvalidOperationException("Original owner did not start.");
            Evidence.Process("creator-launch", "completed", process.Id, null);
            stdout = new(process.StandardOutput);
            stderr = new(process.StandardError);
            stdout.Start(outputCapture.Token);
            stderr.Start(outputCapture.Token);
            phase = "creator-connect";
            Evidence.Phase(phase, "started");
            using (
                CancellationTokenSource connection =
                    CancellationTokenSource.CreateLinkedTokenSource(token)
            )
            {
                Task accept = pipe.WaitForConnectionAsync(connection.Token);
                await OwnedCreatorProcess
                    .ObservePipeStageAsync(accept, connection, TimeSpan.FromSeconds(5), token)
                    .ConfigureAwait(false);
            }
            Evidence.Phase("creator-connect", "completed");
            reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            phase = "creator-hello";
            Hello = await ReceiveAsync("HELLO", token).ConfigureAwait(false);
            phase = "creator-identity";
            Evidence.Phase(phase, "started");
            Assert.AreEqual("interrupted-owner-setup", Hello["role"]!.GetValue<string>());
            verifiedBirthTicks = VerifyCreatorIdentity(process);
            Evidence.Process("creator-identity", "observed", process.Id, verifiedBirthTicks);
            Assert.AreEqual(
                OwnedCreatorProcess.Hash(OwnedCreatorProcess.AssemblyPath),
                Hello["moduleSha256"]!.GetValue<string>()
            );
            Assert.AreEqual(
                OwnedCreatorProcess.Hash(typeof(Coordinator).Assembly.Location),
                Hello["productSha256"]!.GetValue<string>()
            );
            Evidence.Phase(phase, "completed");
            phase = "creator-start";
            Evidence.Phase("creator-start", "started");
            string startFrame = new JsonObject
            {
                ["version"] = 1,
                ["nonce"] = nonce,
                ["stage"] = "START",
            }.ToJsonString();
            token.ThrowIfCancellationRequested();
            startMayHaveBeenDelivered = true;
            await writer.WriteLineAsync(startFrame.AsMemory(), token).ConfigureAwait(false);
            Evidence.Phase("creator-start", "completed");
            phase = "creator-checkpoint";
            Checkpoint = await ReceiveAsync("CHECKPOINT", token).ConfigureAwait(false);
            phase = "creator-checkpoint-validation";
            Evidence.Phase(phase, "started");
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
            Assert.AreEqual(
                3, Checkpoint["history"]!["generations"]![0]!["events"]!.AsArray().Count
            );
            checkpointVerified = true;
            Evidence.Phase("creator-checkpoint-validation", "completed");
            Evidence.Flush();
        }
        catch (Exception error)
        {
            Evidence.Phase(phase, "failed", error: error);
            throw;
        }
    }

    internal async Task CrashAsync(CancellationToken token)
    {
        Assert.IsFalse(joined);
        Assert.IsTrue(checkpointVerified);
        Assert.IsNotNull(process);
        long birthTicks = VerifyCreatorIdentity(process);
        Assert.IsFalse(process.HasExited);
        Evidence.Process("creator-crash", "requested", process.Id, birthTicks);
        Evidence.Flush();
        process.Kill();
        await OwnedCreatorProcess.WaitForExitAsync(process, token).ConfigureAwait(false);
        joined = true;
        observedExitCode = process.ExitCode;
        Assert.AreNotEqual(0, observedExitCode);
        Evidence.Process("creator-crash", "joined", process.Id, birthTicks, observedExitCode);
    }

    private long VerifyCreatorIdentity(Process creator)
    {
        Assert.AreEqual(Hello["pid"]!.GetValue<int>(), creator.Id);
        long birthTicks = Hello["birthTicks"]!.GetValue<long>();
        Assert.AreEqual(birthTicks, creator.StartTime.ToUniversalTime().Ticks);
        return birthTicks;
    }

    private async Task<JsonObject> ReceiveAsync(string stage, CancellationToken token)
    {
        string phase = "creator-" + stage.ToLowerInvariant();
        Evidence.Phase(phase, "started");
        try
        {
            JsonObject data = await OwnedCreatorProcess
                .ReceiveStageAsync(
                    reader!,
                    nonce,
                    stage,
                    stage == "CHECKPOINT" ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(30),
                    token
                )
                .ConfigureAwait(false);
            Evidence.Phase(phase, "received");
            return data;
        }
        catch (Exception error)
        {
            Evidence.Phase(phase, "failed", error: error);
            throw;
        }
    }

    public ValueTask DisposeAsync() => new(cleanupTask ??= CleanupAsync());

    private async Task CleanupAsync()
    {
        List<Exception> errors = [];
        JsonObject stages = new();
        bool stopCreatorTree = startMayHaveBeenDelivered && !checkpointVerified;
        async Task Attempt(string stage, Func<Task> action)
        {
            stages[stage] = "attempted";
            Evidence.Phase("cleanup-" + stage, "started");
            try
            {
                await action().ConfigureAwait(false);
                stages[stage] = "completed";
                Evidence.Phase("cleanup-" + stage, "completed");
            }
            catch (Exception error)
            {
                stages[stage] = "failed";
                errors.Add(error);
                Evidence.Phase("cleanup-" + stage, "failed", error: error);
                Console.Error.WriteLine(
                    "INTERRUPTED_CREATOR_CLEANUP_STAGE_ERROR "
                        + stage
                        + " "
                        + Json.ExceptionDetails(error)
                );
            }
        }
        if (process is { } creator)
        {
            if (!joined)
            {
                try
                {
                    if (!creator.HasExited)
                    {
                        if (stopCreatorTree)
                        {
                            VerifyCreatorIdentity(creator);
                        }
                        creator.Kill(entireProcessTree: stopCreatorTree);
                    }
                }
                catch (Exception stopFailure)
                {
                    errors.Add(stopFailure);
                    Evidence.Phase("creator-cleanup", "stop-failed", error: stopFailure);
                }
                await Attempt(
                        "creatorJoin",
                        async () =>
                        {
                            await OwnedCreatorProcess.WaitForExitAsync(
                                creator, CancellationToken.None
                            ).ConfigureAwait(false);
                            joined = true;
                            observedExitCode = creator.ExitCode;
                            Evidence.Process(
                                "creator-cleanup", "joined", creator.Id, verifiedBirthTicks,
                                observedExitCode
                            );
                        }
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                stages["creatorJoin"] = "already-observed";
            }
            if (startMayHaveBeenDelivered)
            {
                await Attempt(
                        "registeredDescendants",
                        () =>
                        {
                            if (stopCreatorTree)
                            {
                                RunStateQueries.QualifyExistingCreatorStore(Configuration);
                            }
                            RunDatabase database = new(Configuration.StatePath);
                            return new ProcessRunner(database)
                                .ResolveSurvivorsAsync(CancellationToken.None);
                        }
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                stages["registeredDescendants"] = "not-admitted";
            }
        }
        else
        {
            stages["creatorJoin"] = "not-acquired";
            stages["registeredDescendants"] = "no-creator-acquired";
        }
        using CancellationTokenSource drains = new(TimeSpan.FromSeconds(5));
        if (stdout is { } output)
        {
            bool readStarted = output.Capture is not null;
            if (!readStarted)
            {
                stages["stdoutDrain"] = "not-started";
            }
            await Attempt(
                    readStarted ? "stdoutDrain" : "stdoutReaderRelease",
                    async () =>
                    {
                        _ = await OwnedCreatorProcess.DrainOutputAsync(
                            output, outputCapture, "stdout", drains.Token
                        ).ConfigureAwait(false);
                    }
                )
                .ConfigureAwait(false);
        }
        else
        {
            stages["stdoutDrain"] = "not-acquired";
        }
        if (stderr is { } error)
        {
            bool readStarted = error.Capture is not null;
            if (!readStarted)
            {
                stages["stderrDrain"] = "not-started";
            }
            await Attempt(
                    readStarted ? "stderrDrain" : "stderrReaderRelease",
                    async () =>
                    {
                        _ = await OwnedCreatorProcess.DrainOutputAsync(
                            error, outputCapture, "stderr", drains.Token
                        ).ConfigureAwait(false);
                    }
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
        Evidence.CaptureSqlite(
            [
                ("run", Path.Combine(Configuration.StatePath, "run.sqlite.diagnostics")),
                ("campaign", Configuration.CampaignLedgerPath + ".diagnostics"),
            ]
        );
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
        OwnedCreatorProcess.DisposeOutputWhenTerminal(
            outputCapture, stdout?.Capture, stderr?.Capture
        );
        foreach (Exception secondary in errors)
        {
            Console.Error.WriteLine(
                "INTERRUPTED_CREATOR_CLEANUP_SECONDARY " + Json.ExceptionDetails(secondary)
            );
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Original owner cleanup failed.", errors);
        }
    }
}
