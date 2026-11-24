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
    private readonly string nonce;
    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource outputCapture = new();
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private Phase6OwnedCreator.CreatorOutput? stdout,
        stderr;
    private bool startMayHaveBeenDelivered,
        checkpointVerified,
        joined,
        deliberateCrash;
    private long? verifiedBirthTicks;
    private int? observedExitCode;
    private Task? cleanupTask;
    internal GitScenarioState Git { get; }
    internal Phase7Evidence Evidence { get; }
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
        bool minimum,
        Phase7Evidence evidence
    )
    {
        Git = git;
        Evidence = evidence;
        nonce = evidence.Id;
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

    internal static async Task<Phase7OwnedCreator> OpenAsync(
        TestContext context, bool minimum, CancellationToken token
    )
    {
        Dictionary<string, string?> environment = Phase6State.CaptureEnvironment();
        Phase7Evidence evidence = Phase7Run.CreateEvidence(context, Guid.NewGuid().ToString("N"));
        using Activity observation = evidence.BeginScope();
        GitScenarioState? git = null;
        Phase7OwnedCreator? creator = null;
        try
        {
            evidence.Phase("git-setup", "started");
            git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
            evidence.Phase("git-setup", "completed");
            creator = new(git, environment, minimum, evidence);
            await creator.StartAsync(token).ConfigureAwait(false);
            return creator;
        }
        catch (Exception primary)
        {
            evidence.Phase("original-owner-setup", "failed", error: primary);
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
            if (git is not null)
            {
                VerifyFailedEnvironment(git, environment, secondary);
            }
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "PHASE7_ORIGINAL_SETUP_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            evidence.Dispose();
            throw;
        }
    }

    // Own the acquisition edge BEFORE Attach. The inner fixture still settles
    // this creator before Git disposal; this cached fallback also covers an
    // Attach/prelude failure without letting cleanup replace its primary.
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
                VerifyFailedEnvironment(Git, Environment, secondary);
            }
            foreach (Exception error in secondary)
            {
                Console.Error.WriteLine(
                    "PHASE7_ORIGINAL_LIFETIME_SECONDARY " + Json.ExceptionDetails(error)
                );
            }
            Evidence.Dispose();
        }
        // A primary rethrows through finally; only a successful body reaches
        // this cleanup-failure propagation.
        if (secondary.Count != 0)
        {
            throw new AggregateException("Original owner lifetime cleanup failed.", secondary);
        }
    }

    private static void VerifyFailedEnvironment(
        GitScenarioState git,
        Dictionary<string, string?> expected,
        List<Exception> secondary
    )
    {
        bool unchanged = false,
            verified = false;
        try
        {
            GitScenarioState.AssertEnvironmentUnchanged();
            unchanged = true;
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
                    ["inheritedEnvironmentCheckAttempted"] = true,
                    ["inheritedEnvironmentUnchanged"] = unchanged,
                    ["baselineVerificationAttempted"] = true,
                    ["baselineVerified"] = verified,
                    ["rootPreserved"] = git.Repository,
                    ["rootDisposalAttempted"] = false,
                }.ToJsonString()
        );
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
                await Phase6OwnedCreator
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
            Assert.AreEqual("phase7-interrupted-owner-setup", Hello["role"]!.GetValue<string>());
            verifiedBirthTicks = VerifyCreatorIdentity(process);
            Evidence.Process("creator-identity", "observed", process.Id, verifiedBirthTicks);
            Assert.AreEqual(
                Phase6OwnedCreator.Hash(Phase6OwnedCreator.AssemblyPath),
                Hello["moduleSha256"]!.GetValue<string>()
            );
            Assert.AreEqual(
                Phase6OwnedCreator.Hash(typeof(Coordinator).Assembly.Location),
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
            // A failed write or flush is not proof that START did not arrive.
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
        // All actual native host processes were normally joined before this
        // point. Kill only the creator identity, not an anonymous process name.
        Evidence.Process("creator-crash", "requested", process.Id, birthTicks);
        Evidence.Flush();
        process.Kill();
        await Phase6OwnedCreator.WaitForCreatorExitAsync(process, token).ConfigureAwait(false);
        joined = true;
        observedExitCode = process.ExitCode;
        Assert.AreNotEqual(0, observedExitCode);
        deliberateCrash = true;
        Evidence.Process("creator-crash", "joined", process.Id, birthTicks, observedExitCode);
        Console.WriteLine(
            "PHASE7_EXPECTED_CREATOR_CRASH "
                + new JsonObject
                {
                    ["pid"] = process.Id,
                    ["birthTicks"] = birthTicks,
                    ["nonce"] = nonce,
                    ["exitCode"] = observedExitCode,
                    ["joined"] = joined,
                    ["nativeNormalSettlementWasEarlier"] = true,
                }.ToJsonString()
        );
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
            using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(
                token
            );
            Task<string?> read = reader!.ReadLineAsync(reading.Token).AsTask();
            await Phase6OwnedCreator
                .ObservePipeStageAsync(read, reading, TimeSpan.FromSeconds(30), token)
                .ConfigureAwait(false);
            string? line = await read.ConfigureAwait(false);
            Assert.IsNotNull(line, "Original creator disconnected before " + stage);
            Assert.IsLessThanOrEqualTo(1_048_576, line.Length);
            JsonObject message = JsonNode.Parse(line)!.AsObject();
            Assert.AreEqual(1, message["version"]!.GetValue<int>());
            Assert.AreEqual(nonce, message["nonce"]!.GetValue<string>());
            Assert.AreEqual(stage, message["stage"]!.GetValue<string>());
            Evidence.Phase(phase, "received");
            return message["data"]!.AsObject();
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
        int? ownedPid = process?.Id;
        bool stopCreatorTree = startMayHaveBeenDelivered && !checkpointVerified;
        string stopOutcome = "not-requested";
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
                    "PHASE7_ORIGINAL_CLEANUP_STAGE_ERROR "
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
                    stopOutcome = "scope-unqualified";
                    if (creator.HasExited)
                    {
                        stopOutcome = "root-already-exited";
                    }
                    else
                    {
                        if (stopCreatorTree)
                        {
                            VerifyCreatorIdentity(creator);
                        }
                        stopOutcome = "request-failed";
                        creator.Kill(entireProcessTree: stopCreatorTree);
                        stopOutcome = "requested";
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
                            await Phase6OwnedCreator.WaitForCreatorExitAsync(
                                creator, CancellationToken.None
                            ).ConfigureAwait(false);
                            joined = true;
                            observedExitCode = creator.ExitCode;
                            Evidence.Process(
                                "creator-cleanup", "joined", creator.Id, verifiedBirthTicks,
                                observedExitCode
                            );
                            Console.WriteLine(
                                "PHASE7_ORIGINAL_EXCEPTIONAL_EXIT "
                                    + $"pid={creator.Id} exit={observedExitCode}; "
                                    + "not normal native settlement"
                            );
                        }
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                stages["creatorJoin"] = "already-observed";
            }
            // The registered sweep is independent of root exit and cannot qualify an
            // unregistered helper from the post-START, pre-proof interval.
            if (startMayHaveBeenDelivered)
            {
                await Attempt(
                        "registeredDescendants",
                        () =>
                        {
                            if (stopCreatorTree)
                            {
                                Phase6State.QualifyExistingCreatorStore(Configuration);
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
        // Same existing five-second cleanup budget, shared by both drains.
        // A pending capture does not establish EOF, even after the creator joins.
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
                        string? text = await Phase6OwnedCreator.DrainCreatorOutputAsync(
                            output, outputCapture, "stdout", drains.Token
                        ).ConfigureAwait(false);
                        if (text is not null)
                        {
                            Console.WriteLine("PHASE7_CREATOR_STDOUT " + text);
                        }
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
                        string? text = await Phase6OwnedCreator.DrainCreatorOutputAsync(
                            error, outputCapture, "stderr", drains.Token
                        ).ConfigureAwait(false);
                        if (text is not null)
                        {
                            Console.WriteLine("PHASE7_CREATOR_STDERR " + text);
                        }
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
        Phase6OwnedCreator.DisposeCreatorOutputWhenTerminal(
            outputCapture, stdout?.Capture, stderr?.Capture
        );
        Console.WriteLine(
            "PHASE7_ORIGINAL_RESOURCE_CLEANUP "
                + new JsonObject
                {
                    ["pid"] = ownedPid,
                    ["birthTicks"] = verifiedBirthTicks,
                    ["startMayHaveBeenDelivered"] = startMayHaveBeenDelivered,
                    ["checkpointVerified"] = checkpointVerified,
                    ["exceptionalStopScope"] = stopCreatorTree
                        ? "owned-creator-tree" : "root-only",
                    ["exceptionalStopOutcome"] = stopOutcome,
                    ["preCheckpointDescendantFinality"] = stopCreatorTree
                        ? "unresolved" : "not-applicable",
                    ["creatorJoinObserved"] = joined,
                    ["observedCreatorExitCode"] = observedExitCode,
                    ["deliberateCrashJoined"] = deliberateCrash,
                    ["stages"] = stages,
                    ["secondaryCount"] = errors.Count,
                }.ToJsonString()
        );
        foreach (Exception secondary in errors)
        {
            Console.Error.WriteLine(
                "PHASE7_ORIGINAL_CLEANUP_SECONDARY " + Json.ExceptionDetails(secondary)
            );
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Original owner cleanup failed.", errors);
        }
    }
}
