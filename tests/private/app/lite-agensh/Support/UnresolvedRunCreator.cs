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
internal sealed class UnresolvedRunCreator : IAsyncDisposable
{
    private readonly GitScenarioState git;
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private readonly NamedPipeServerStream pipe;
    private readonly RunConfiguration configuration;
    private readonly CancellationTokenSource outputCapture = new();
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private OwnedCreatorProcess.OutputCapture? stdout;
    private OwnedCreatorProcess.OutputCapture? stderr;
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

    private UnresolvedRunCreator(GitScenarioState git)
    {
        this.git = git;
        string root = git.PathFor("unresolved-original");
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
            "lite-unresolved-creator-" + nonce,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
    }

    internal static async Task<UnresolvedRunCreator> OpenAsync(CancellationToken token)
    {
        GitScenarioState git = await GitScenarioState.CreateAsync(token).ConfigureAwait(false);
        UnresolvedRunCreator? owner = null;
        try
        {
            owner = new(git);
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
                GitScenarioState.AssertEnvironmentUnchanged();
            }
            throw;
        }
    }

    private async Task StartAsync(CancellationToken token)
    {
        string configPath = Path.Combine(Root, "configuration.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(configuration, Json.Options));
        ProcessStartInfo start = new(OwnedCreatorProcess.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("lite-unresolved-creator-" + nonce);
        start.ArgumentList.Add(nonce);
        start.ArgumentList.Add(configPath);
        process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Bound offline creator did not start.");
        stdout = new(process.StandardOutput);
        stderr = new(process.StandardError);
        stdout.Start(outputCapture.Token);
        stderr.Start(outputCapture.Token);
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
        reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        Hello = await ReceiveAsync("HELLO", token).ConfigureAwait(false);
        Assert.AreEqual("setup-not-business-credit", Hello["role"]!.GetValue<string>());
        Assert.AreEqual(process.Id, Hello["pid"]!.GetValue<int>());
        Assert.AreEqual(
            process.StartTime.ToUniversalTime().Ticks,
            Hello["birthTicks"]!.GetValue<long>()
        );
        Assert.AreEqual(
            OwnedCreatorProcess.Hash(OwnedCreatorProcess.AssemblyPath),
            Hello["moduleSha256"]!.GetValue<string>()
        );
        Assert.AreEqual(
            OwnedCreatorProcess.Hash(typeof(Coordinator).Assembly.Location),
            Hello["productSha256"]!.GetValue<string>()
        );
        await SendAsync("START", token).ConfigureAwait(false);
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
    }

    internal async Task ExitAsync(CancellationToken token)
    {
        await SendAsync("EXIT", token).ConfigureAwait(false);
        Process creator = process!;
        await OwnedCreatorProcess.WaitForExitAsync(creator, token).ConfigureAwait(false);
        exitObserved = true;
        Assert.AreEqual(0, creator.ExitCode, "The original creator did not exit normally.");
        normalExitObserved = true;
    }

    internal async Task CrashAsync(CancellationToken token)
    {
        Assert.IsFalse(process!.HasExited);
        process.Kill(entireProcessTree: true);
        await OwnedCreatorProcess.WaitForExitAsync(process, token).ConfigureAwait(false);
        exitObserved = true;
        await new ProcessRunner(Database).ResolveSurvivorsAsync(token).ConfigureAwait(false);
        registeredDescendantSweepCompleted = true;
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

    private Task<JsonObject> ReceiveAsync(string stage, CancellationToken token) =>
        OwnedCreatorProcess.ReceiveStageAsync(
            reader!,
            nonce,
            stage,
            stage == "OPENED" ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(30),
            token
        );

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
                        if (!creator.HasExited)
                        {
                            creator.Kill(entireProcessTree: true);
                        }
                        using CancellationTokenSource join = new(TimeSpan.FromSeconds(5));
                        await creator.WaitForExitAsync(join.Token).ConfigureAwait(false);
                        exitObserved = true;
                    }).ConfigureAwait(false);
                }
                if (sweepRequired && !registeredDescendantSweepCompleted)
                {
                    await Attempt(async () =>
                    {
                        RunDatabase? database = Database;
                        if (database is null)
                        {
                            RunStateQueries.QualifyExistingCreatorStore(configuration);
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
                        _ = await OwnedCreatorProcess.DrainOutputAsync(
                            output, outputCapture, "stdout", drains.Token
                        ).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }
                if (stderr is { } error)
                {
                    await Attempt(async () =>
                    {
                        string? text = await OwnedCreatorProcess.DrainOutputAsync(
                            error, outputCapture, "stderr", drains.Token
                        ).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(text) && primary is not null)
                        {
                            Console.Error.WriteLine("UNRESOLVED_CREATOR_STDERR " + text);
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
                OwnedCreatorProcess.DisposeOutputWhenTerminal(
                    outputCapture, stdout?.Capture, stderr?.Capture
                )
            );
            if (primary is null && secondary.Count == 0 && ownedFinished)
            {
                Cleanup(git.Dispose);
            }
            else
            {
                Cleanup(GitScenarioState.AssertEnvironmentUnchanged);
            }
        }
        foreach (Exception error in secondary.ToArray())
        {
            Cleanup(() =>
                Console.Error.WriteLine(
                    "UNRESOLVED_CREATOR_CLEANUP_SECONDARY " + Json.ExceptionDetails(error)
                )
            );
        }
        if (primary is not null && secondary.Count != 0)
        {
            primary.Data["CreatorCleanupFailures"] = secondary.ToArray();
        }
        if (primary is null && secondary.Count != 0)
        {
            throw new AggregateException("Unresolved creator cleanup failed.", secondary);
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
