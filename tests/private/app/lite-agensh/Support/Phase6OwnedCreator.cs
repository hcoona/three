using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// Original creator's lifetime only. Git ownership remains with GitScenarioState;
// this launcher is not a normal-runner role or a general process harness.
internal sealed class Phase6OwnedCreator : IAsyncDisposable
{
    private readonly GitScenarioState git;
    private readonly Dictionary<string, string?> environment;
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private readonly NamedPipeServerStream pipe;
    private readonly RunConfiguration configuration;
    private readonly CancellationTokenSource outputCapture = new();
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private CreatorOutput? stdout;
    private CreatorOutput? stderr;
    private bool startMayHaveBeenDelivered;
    private bool exitObserved;
    private bool normalExitObserved;
    private bool registeredDescendantSweepCompleted;
    private Exception? primary;
    private string? initialHead;
    private byte[]? initialIndex;
    internal JsonObject Hello { get; private set; } = new();
    internal JsonObject Opened { get; private set; } = new();
    internal JsonObject Ended { get; private set; } = new();
    internal RunDatabase Database { get; private set; } = null!;
    internal CreditLedger Ledger { get; private set; } = null!;
    internal string Root => Path.GetDirectoryName(configuration.StatePath)!;
    internal string TargetId => Opened["target"]!["Id"]!.GetValue<string>();
    internal CreditReservation Target =>
        Ledger.Reservations(Database.RunId).Single(row => row.Id == TargetId);
    internal static string Executable =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "LiteAgensh.OfflineOwner.exe" : "LiteAgensh.OfflineOwner"
        );
    internal static string AssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "LiteAgensh.OfflineOwner.dll");

    private Phase6OwnedCreator(GitScenarioState git, Dictionary<string, string?> environment)
    {
        this.git = git;
        this.environment = environment;
        string root = git.PathFor("phase6-original");
        Directory.CreateDirectory(root);
        configuration = git.Database.Configuration with
        {
            StatePath = Path.Combine(root, "state"),
            CampaignLedgerPath = Path.Combine(root, "campaign.sqlite"),
            Goal = "Preserve genuine never-dispatched local authorization.",
            NativeModel = "controlled-model",
            WorkerCount = 1,
            RunAiCreditAllowance = 307.25m,
            CampaignAiCreditAllowance = 307.25m,
        };
        pipe = new(
            "lite-phase6-creator-" + nonce,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
    }

    internal static async Task<Phase6OwnedCreator> OpenAsync(CancellationToken token)
    {
        Dictionary<string, string?> environment = Phase6State.CaptureEnvironment();
        GitScenarioState git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
        Phase6OwnedCreator? owner = null;
        try
        {
            owner = new(git, environment);
            await owner.StartAsync(token).ConfigureAwait(false);
            return owner;
        }
        catch (Exception error)
        {
            if (owner is not null)
            {
                owner.primary = error;
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                try
                {
                    GitScenarioState.AssertEnvironmentUnchanged();
                    Phase6State.VerifyEnvironment(environment);
                }
                catch (Exception secondary)
                {
                    Console.Error.WriteLine(
                        "PHASE6_CONSTRUCTOR_CLEANUP_SECONDARY " + Json.ExceptionDetails(secondary)
                    );
                }
                Console.WriteLine("PHASE6_PRESERVED_CONSTRUCTOR_ROOT " + git.Repository);
            }
            throw;
        }
    }

    private async Task StartAsync(CancellationToken token)
    {
        string configPath = Path.Combine(Root, "configuration.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, Json.Options));
        ProcessStartInfo start = new(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("lite-phase6-creator-" + nonce);
        start.ArgumentList.Add(nonce);
        start.ArgumentList.Add(configPath);
        Console.WriteLine("PHASE6_LAUNCH_ENTER");
        process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Bound offline creator did not start.");
        Console.WriteLine("PHASE6_LAUNCH_RETURN pid=" + process.Id);
        stdout = new(process.StandardOutput);
        stderr = new(process.StandardError);
        stdout.Start(outputCapture.Token);
        stderr.Start(outputCapture.Token);
        Console.WriteLine("PHASE6_CONNECT_ENTER");
        using (
            CancellationTokenSource connection =
                CancellationTokenSource.CreateLinkedTokenSource(token)
        )
        {
            Task accept = pipe.WaitForConnectionAsync(connection.Token);
            await ObservePipeStageAsync(accept, connection, TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
        }
        Console.WriteLine("PHASE6_CONNECT_RETURN");
        reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        Hello = await ReceiveAsync("HELLO", token).ConfigureAwait(false);
        Assert.AreEqual("setup-not-business-credit", Hello["role"]!.GetValue<string>());
        Assert.AreEqual(process.Id, Hello["pid"]!.GetValue<int>());
        Assert.AreEqual(
            process.StartTime.ToUniversalTime().Ticks,
            Hello["birthTicks"]!.GetValue<long>()
        );
        Assert.AreEqual(Hash(AssemblyPath), Hello["moduleSha256"]!.GetValue<string>());
        Assert.AreEqual(
            Hash(typeof(Coordinator).Assembly.Location),
            Hello["productSha256"]!.GetValue<string>()
        );
        await SendAsync("START", token).ConfigureAwait(false);
        Console.WriteLine("PHASE6_START_SENT");
        Opened = await ReceiveAsync("OPENED", token).ConfigureAwait(false);
        RunDatabase database = new(configuration.StatePath);
        CreditLedger ledger = new(
            configuration.CampaignLedgerPath, configuration.CampaignAiCreditAllowance
        );
        Assert.AreEqual(database.RunId, Opened["runId"]!.GetValue<string>());
        JsonObject owner = ledger.ReadSdkOwner(TargetId);
        Assert.AreEqual(process.Id, owner["pid"]!.GetValue<int>());
        Assert.AreEqual(
            Hello["birthTicks"]!.GetValue<long>(),
            owner["startTicks"]!.GetValue<long>()
        );
        Database = database;
        Ledger = ledger;
        (initialHead, initialIndex, _) = await ReadCheckoutAsync(token).ConfigureAwait(false);
        Console.WriteLine(
            "PHASE6_CREATOR_OPENED "
                + new JsonObject
                {
                    ["role"] = "setup-not-business-credit",
                    ["root"] = Root,
                    ["hello"] = Hello.DeepClone(),
                    ["opened"] = Opened.DeepClone(),
                }.ToJsonString()
        );
    }

    internal Coordinator AccountingOnly()
    {
        CollaborationOperations collaboration = new(Database);
        return new(
            Database,
            collaboration,
            new(Database, collaboration),
            (_, _, _) => new ForbiddenIoFactory()
        );
    }

    internal async Task EndRunAsync(CancellationToken token)
    {
        await SendAsync("CONTINUE", token).ConfigureAwait(false);
        Ended = await ReceiveAsync("ENDED_UNRESOLVED", token).ConfigureAwait(false);
        Console.WriteLine("PHASE6_CREATOR_TERMINAL " + Ended.ToJsonString());
    }

    internal async Task ExitAsync(CancellationToken token)
    {
        await SendAsync("EXIT", token).ConfigureAwait(false);
        Process creator = process!;
        await WaitForCreatorExitAsync(creator, token).ConfigureAwait(false);
        exitObserved = true;
        Assert.AreEqual(0, creator.ExitCode, "The original creator did not exit normally.");
        normalExitObserved = true;
        Console.WriteLine(
            "PHASE6_CREATOR_EXIT "
                + new JsonObject
                {
                    ["pid"] = Hello["pid"]!.DeepClone(),
                    ["birthTicks"] = Hello["birthTicks"]!.DeepClone(),
                    ["exited"] = creator.HasExited,
                    ["exitCode"] = creator.ExitCode,
                    ["role"] = "setup-not-business-credit",
                }.ToJsonString()
        );
    }

    internal async Task CrashAsync(CancellationToken token)
    {
        // Frozen unfinished-crash routing consumer only. This is NOT a normal
        // producer/proof receipt and cannot be used by eligible Reconcile.
        Assert.IsFalse(process!.HasExited);
        process.Kill(entireProcessTree: true);
        await WaitForCreatorExitAsync(process, token).ConfigureAwait(false);
        exitObserved = true;
        Console.WriteLine(
            $"PHASE6_EXPECTED_OWNED_CRASH pid={process.Id} exit={process.ExitCode}; "
                + "unfinished segment; no normal-host/finality credit"
        );
        await new ProcessRunner(Database).ResolveSurvivorsAsync(token).ConfigureAwait(false);
        registeredDescendantSweepCompleted = true;
    }

    internal static async Task WaitForCreatorExitAsync(Process creator, CancellationToken token)
    {
        using CancellationTokenSource exitWait =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        exitWait.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken waitToken = exitWait.Token;
        Task exit = creator.WaitForExitAsync(waitToken);
        try
        {
            await exit.ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled) when (
            exit.IsCanceled
            && canceled.CancellationToken == waitToken
            && waitToken.IsCancellationRequested
            && !token.IsCancellationRequested
        )
        {
            throw new TimeoutException("The creator exit wait exceeded five seconds.", canceled);
        }
    }

    internal async Task RunBodyAsync(Func<Task> body)
    {
        try
        {
            await body().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal async Task<JsonObject> PreservationAsync(CancellationToken token)
    {
        string cwd = JsonNode.Parse(Database.ReadMetadata($"native:session:{Target.SessionId}")!)![
            "workingDirectory"
        ]!.GetValue<string>();
        (string head, byte[] index, string status) = await ReadCheckoutAsync(token)
            .ConfigureAwait(false);
        Assert.AreEqual("", status);
        Assert.AreEqual(git.BaseCommit, head);
        Assert.AreEqual(initialHead, head);
        Assert.IsNotNull(initialIndex);
        Assert.IsTrue(
            initialIndex.AsSpan().SequenceEqual(index),
            "Actual owned index bytes changed."
        );
        return new()
        {
            ["changedOriginalFiles"] = new JsonArray(),
            ["checkouts"] = new JsonArray(
                new JsonObject
                {
                    ["path"] = cwd,
                    ["headMatches"] = true,
                    ["indexMatches"] = true,
                    ["status"] = status,
                }
            ),
        };
    }

    private async Task<(string Head, byte[] Index, string Status)> ReadCheckoutAsync(
        CancellationToken token
    )
    {
        string cwd = JsonNode.Parse(Database.ReadMetadata($"native:session:{Target.SessionId}")!)![
            "workingDirectory"
        ]!.GetValue<string>();
        string status = await git.GitAsync(cwd, token, "status", "--porcelain")
            .ConfigureAwait(false);
        string head = (
            await git.GitAsync(cwd, token, "rev-parse", "HEAD").ConfigureAwait(false)
        ).Trim();
        string indexPath = (
            await git.GitAsync(cwd, token, "rev-parse", "--git-path", "index").ConfigureAwait(false)
        ).Trim();
        return (head, File.ReadAllBytes(Path.GetFullPath(indexPath, cwd)), status);
    }

    private Task SendAsync(string stage, CancellationToken token)
    {
        string frame = new JsonObject
        {
            ["version"] = 1,
            ["nonce"] = nonce,
            ["stage"] = stage,
        }.ToJsonString();
        if (stage == "START")
        {
            token.ThrowIfCancellationRequested();
            // A failed write or flush is not proof that START did not arrive.
            startMayHaveBeenDelivered = true;
        }
        return writer!.WriteLineAsync(frame.AsMemory(), token);
    }

    private async Task<JsonObject> ReceiveAsync(string stage, CancellationToken token)
    {
        Console.WriteLine("PHASE6_RECEIVE_ENTER " + stage);
        using CancellationTokenSource reading =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<string?> read = reader!.ReadLineAsync(reading.Token).AsTask();
        await ObservePipeStageAsync(read, reading, TimeSpan.FromSeconds(30), token)
            .ConfigureAwait(false);
        string? line = await read.ConfigureAwait(false);
        Assert.IsNotNull(line, "Creator disconnected before " + stage);
        Assert.IsLessThanOrEqualTo(1_048_576, line.Length);
        JsonObject message = JsonNode.Parse(line)!.AsObject();
        Assert.AreEqual(1, message["version"]!.GetValue<int>());
        Assert.AreEqual(nonce, message["nonce"]!.GetValue<string>());
        Assert.AreEqual(stage, message["stage"]!.GetValue<string>());
        Console.WriteLine("PHASE6_RECEIVE_RETURN " + stage);
        return message["data"]!.AsObject();
    }

    internal static async Task ObservePipeStageAsync(
        Task operation, CancellationTokenSource stage, TimeSpan fuse, CancellationToken token
    )
    {
        try
        {
            await operation.WaitAsync(fuse, token).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            List<Exception> secondary = [];
            try
            {
                await stage.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) when (
                operation.IsCanceled
                && stage.IsCancellationRequested
                && (
                    canceled.CancellationToken == stage.Token
                    || !canceled.CancellationToken.CanBeCanceled
                    || (canceled.CancellationToken == token && token.IsCancellationRequested)
                )
            )
            { }
            catch (Exception error)
            {
                if (!ReferenceEquals(error, primary))
                {
                    secondary.Add(error);
                }
            }
            primary.Data["CreatorPipeOperationStatus"] = operation.Status.ToString();
            if (secondary.Count != 0)
            {
                primary.Data["CreatorPipeCleanupFailures"] = secondary.ToArray();
            }
            throw;
        }
    }

    internal sealed class CreatorOutput(StreamReader reader)
    {
        internal StreamReader Reader { get; } = reader;
        internal Stream ReadEnd { get; } = reader.BaseStream;
        internal Task<string>? Capture { get; private set; }

        internal void Start(CancellationToken cancellationToken) =>
            Capture = Reader.ReadToEndAsync(cancellationToken);
    }

    internal static async Task<string?> DrainCreatorOutputAsync(
        CreatorOutput output, CancellationTokenSource captureScope, string stream,
        CancellationToken deadline
    )
    {
        List<Exception> failures = [];
        string? text = null;
        if (output.Capture is { } capture)
        {
            try
            {
                text = await capture.WaitAsync(deadline).ConfigureAwait(false);
            }
            catch (Exception observation)
            {
                failures.Add(observation);
                if (!capture.IsCompleted)
                {
                    if (!captureScope.IsCancellationRequested)
                    {
                        Release(captureScope.Cancel);
                    }
                    if (!capture.IsCompleted)
                    {
                        // Abort the owned stream without disposing its text reader mid-read.
                        Release(output.ReadEnd.Dispose);
                    }
                    if (!capture.IsCompleted)
                    {
                        TimeoutException unresolved = new(
                            $"Creator {stream} capture is unresolved after "
                                + "read-end abort was attempted.",
                            new AggregateException(failures)
                        );
                        unresolved.Data["CreatorOutputOriginalRead"] = capture;
                        unresolved.Data["CreatorOutputLateReaderRelease"] =
                            ReleaseReaderAfterCaptureAsync(output.Reader, capture);
                        throw unresolved;
                    }
                }
                try
                {
                    _ = await capture.ConfigureAwait(false);
                }
                catch (OperationCanceledException canceled) when (
                    observation is OperationCanceledException observedCancellation
                    && canceled.CancellationToken == observedCancellation.CancellationToken
                )
                { } // Both awaits can report the same original cancellation.
                catch (Exception terminal)
                {
                    if (!ReferenceEquals(terminal, observation))
                    {
                        failures.Add(terminal);
                    }
                }
            }
        }
        Release(output.Reader.Dispose);
        if (failures.Count != 0)
        {
            throw new IOException(
                $"Creator {stream} capture or reader release failed.",
                new AggregateException(failures)
            );
        }
        return text;

        void Release(Action release)
        {
            try
            {
                release();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
    }

    private static async Task<Exception?> ReleaseReaderAfterCaptureAsync(
        StreamReader reader, Task<string> capture
    )
    {
        try
        {
            await capture.ConfigureAwait(false);
        }
        catch (Exception) { } // The original task retains the late outcome.

        try
        {
            reader.Dispose();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    internal static void DisposeCreatorOutputWhenTerminal(
        CancellationTokenSource captureScope, Task? stdout, Task? stderr
    )
    {
        // An unresolved read keeps its cancellation scope until the original tasks terminate.
        _ = Task.WhenAll(stdout ?? Task.CompletedTask, stderr ?? Task.CompletedTask)
            .ContinueWith(
                static (settled, scope) =>
                {
                    _ = settled.Exception;
                    ((CancellationTokenSource)scope!).Dispose();
                },
                captureScope,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> secondary = [];
        void Cleanup(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
        }
        async Task Attempt(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
        }
        bool ownedFinished = process is null;
        bool sweepRequired =
            startMayHaveBeenDelivered && (!normalExitObserved || primary is not null);
        try
        {
            if (process is { } creator)
            {
                if (!exitObserved)
                {
                    await Attempt(async () =>
                    {
                        bool exitedBeforeCleanup = creator.HasExited;
                        JsonObject processCleanup = new()
                        {
                            ["pid"] = creator.Id,
                            ["exitedBeforeCleanup"] = exitedBeforeCleanup,
                            ["exitCodeBeforeCleanup"] = exitedBeforeCleanup
                                ? creator.ExitCode
                                : null,
                            ["cleanupKillAttempted"] = false,
                            ["postJoinExitObserved"] = false,
                        };
                        try
                        {
                            if (!exitedBeforeCleanup)
                            {
                                processCleanup["cleanupKillAttempted"] = true;
                                creator.Kill(entireProcessTree: true);
                            }
                            using CancellationTokenSource join = new(TimeSpan.FromSeconds(5));
                            await creator.WaitForExitAsync(join.Token).ConfigureAwait(false);
                            exitObserved = true;
                            processCleanup["postJoinExitObserved"] = true;
                            processCleanup["postJoinExitCode"] = creator.ExitCode;
                        }
                        finally
                        {
                            Cleanup(() => Console.WriteLine(
                                "PHASE6_CREATOR_PROCESS_CLEANUP " + processCleanup.ToJsonString()
                            ));
                        }
                        Console.WriteLine(
                            $"PHASE6_CREATOR_EXCEPTIONAL_EXIT pid={creator.Id} "
                                + $"exit={creator.ExitCode}; not normal settlement"
                        );
                    }).ConfigureAwait(false);
                }
                // Root exit does not retire leases; a sweep covers registered descendants only.
                if (sweepRequired && !registeredDescendantSweepCompleted)
                {
                    await Attempt(async () =>
                    {
                        RunDatabase? database = Database;
                        if (database is null)
                        {
                            Phase6State.QualifyExistingCreatorStore(configuration);
                            database = new(configuration.StatePath);
                        }
                        await new ProcessRunner(database)
                            .ResolveSurvivorsAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        registeredDescendantSweepCompleted = true;
                    }).ConfigureAwait(false);
                }
                using CancellationTokenSource drains = new(TimeSpan.FromSeconds(5));
                if (stdout is { } output)
                {
                    await Attempt(async () =>
                    {
                        string? text = await DrainCreatorOutputAsync(
                            output, outputCapture, "stdout", drains.Token
                        ).ConfigureAwait(false);
                        if (text is not null)
                        {
                            Console.WriteLine("PHASE6_CREATOR_STDOUT " + text);
                        }
                    }).ConfigureAwait(false);
                }
                if (stderr is { } error)
                {
                    await Attempt(async () =>
                    {
                        string? text = await DrainCreatorOutputAsync(
                            error, outputCapture, "stderr", drains.Token
                        ).ConfigureAwait(false);
                        if (text is not null)
                        {
                            Console.WriteLine("PHASE6_CREATOR_STDERR " + text);
                        }
                    }).ConfigureAwait(false);
                }
                ownedFinished =
                    exitObserved
                    && (!sweepRequired || registeredDescendantSweepCompleted)
                    && secondary.Count == 0;
            }
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        finally
        {
            Cleanup(() => writer?.Dispose());
            Cleanup(() => reader?.Dispose());
            try
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                secondary.Add(error);
            }
            Cleanup(() => process?.Dispose());
            Cleanup(() =>
                DisposeCreatorOutputWhenTerminal(outputCapture, stdout?.Capture, stderr?.Capture)
            );
            if (primary is null && secondary.Count == 0 && ownedFinished)
            {
                Cleanup(git.Dispose);
            }
            else
            {
                Cleanup(GitScenarioState.AssertEnvironmentUnchanged);
                Cleanup(() => Console.WriteLine("PHASE6_PRESERVED_OWNED_ROOT " + Root));
            }
            Cleanup(() => Phase6State.VerifyEnvironment(environment));
            string sweepStatus =
                !startMayHaveBeenDelivered ? "not-applicable"
                : registeredDescendantSweepCompleted ? "completed"
                : sweepRequired ? "unresolved"
                : "not-required";
            Cleanup(() => Console.WriteLine(
                $"PHASE6_CREATOR_RESOURCE_CLEANUP finished={ownedFinished}; "
                    + $"rootExitObserved={exitObserved}; normalExitObserved={normalExitObserved}; "
                    + $"registeredDescendantSweepCompleted={registeredDescendantSweepCompleted}; "
                    + $"registeredDescendantSweepStatus={sweepStatus}; "
                    + $"secondaryCount={secondary.Count}"
            ));
        }
        foreach (Exception error in secondary.ToArray())
        {
            Cleanup(() =>
                Console.Error.WriteLine("PHASE6_CLEANUP_SECONDARY " + Json.ExceptionDetails(error))
            );
        }
        if (primary is not null && secondary.Count != 0)
        {
            primary.Data["Phase6CleanupFailures"] = secondary.ToArray();
        }
        if (primary is null && secondary.Count != 0)
        {
            throw new AggregateException("Phase6 creator cleanup failed.", secondary);
        }
    }

    internal sealed class ForbiddenIoFactory : INativeWorkerFactory
    {
        public JsonArray SettlementFailures { get; } = [];
        public JsonArray SettlementDetails { get; } = [];

        public INativeWorker Create(CreditReservation? reservation, bool calibration) =>
            throw new InvalidOperationException("Accounting-only operation attempted replacement.");

        public Task<JsonObject> PreflightAsync(
            IReadOnlyList<(string Worker, string Cwd)> peers,
            CancellationToken token
        ) => throw new InvalidOperationException("Accounting-only operation attempted preflight.");

        public Task CompleteAsync() =>
            throw new InvalidOperationException(
                "Accounting-only operation attempted host settlement."
            );

        public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
            CreditReservation reservation,
            string cwd,
            string evidencePath,
            CancellationToken token
        ) => throw new InvalidOperationException("Closed recovery reader gate was used.");
    }
}
