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
    private Process? process;
    private StreamReader? reader;
    private StreamWriter? writer;
    private Task<string>? stdout;
    private Task<string>? stderr;
    private bool exitObserved;
    private bool normalExitObserved;
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
        Path.Combine(AppContext.BaseDirectory, "offline-owner", "LiteAgensh.Tests.exe");

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
                    git.RestoreEnvironment();
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
        process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Bound offline creator did not start.");
        stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await pipe.WaitForConnectionAsync(token)
            .WaitAsync(TimeSpan.FromSeconds(5), token)
            .ConfigureAwait(false);
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
            Hash(Path.ChangeExtension(Executable, ".dll")),
            Hello["moduleSha256"]!.GetValue<string>()
        );
        Assert.AreEqual(
            Hash(typeof(Coordinator).Assembly.Location),
            Hello["productSha256"]!.GetValue<string>()
        );
        await SendAsync("START", token).ConfigureAwait(false);
        Opened = await ReceiveAsync("OPENED", token).ConfigureAwait(false);
        Database = new(configuration.StatePath);
        Ledger = new(configuration.CampaignLedgerPath, configuration.CampaignAiCreditAllowance);
        Assert.AreEqual(Database.RunId, Opened["runId"]!.GetValue<string>());
        JsonObject owner = Ledger.ReadSdkOwner(TargetId);
        Assert.AreEqual(process.Id, owner["pid"]!.GetValue<int>());
        Assert.AreEqual(
            Hello["birthTicks"]!.GetValue<long>(),
            owner["startTicks"]!.GetValue<long>()
        );
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
        await process!
            .WaitForExitAsync(token)
            .WaitAsync(TimeSpan.FromSeconds(5), token)
            .ConfigureAwait(false);
        exitObserved = true;
        Assert.AreEqual(0, process.ExitCode, await stderr!.ConfigureAwait(false));
        normalExitObserved = true;
        Console.WriteLine(
            "PHASE6_CREATOR_EXIT "
                + new JsonObject
                {
                    ["pid"] = Hello["pid"]!.DeepClone(),
                    ["birthTicks"] = Hello["birthTicks"]!.DeepClone(),
                    ["exited"] = process.HasExited,
                    ["exitCode"] = process.ExitCode,
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
        await process
            .WaitForExitAsync(token)
            .WaitAsync(TimeSpan.FromSeconds(5), token)
            .ConfigureAwait(false);
        exitObserved = true;
        Console.WriteLine(
            $"PHASE6_EXPECTED_OWNED_CRASH pid={process.Id} exit={process.ExitCode}; "
                + "unfinished segment; no normal-host/finality credit"
        );
        await new ProcessRunner(Database).ResolveSurvivorsAsync(token).ConfigureAwait(false);
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

    private Task SendAsync(string stage, CancellationToken token) =>
        writer!.WriteLineAsync(
            new JsonObject
            {
                ["version"] = 1,
                ["nonce"] = nonce,
                ["stage"] = stage,
            }
                .ToJsonString()
                .AsMemory(),
            token
        );

    private async Task<JsonObject> ReceiveAsync(string stage, CancellationToken token)
    {
        string? line = await reader!
            .ReadLineAsync(token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), token)
            .ConfigureAwait(false);
        Assert.IsNotNull(line, "Creator disconnected before " + stage);
        Assert.IsLessThanOrEqualTo(1_048_576, line.Length);
        JsonObject message = JsonNode.Parse(line)!.AsObject();
        Assert.AreEqual(1, message["version"]!.GetValue<int>());
        Assert.AreEqual(nonce, message["nonce"]!.GetValue<string>());
        Assert.AreEqual(stage, message["stage"]!.GetValue<string>());
        return message["data"]!.AsObject();
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> secondary = [];
        bool ownedFinished = process is null;
        try
        {
            if (process is not null)
            {
                if (!exitObserved)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    await process
                        .WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                    Console.WriteLine(
                        $"PHASE6_CREATOR_EXCEPTIONAL_EXIT pid={process.Id} "
                            + $"exit={process.ExitCode}; not normal settlement"
                    );
                    if (Database is not null)
                    {
                        await new ProcessRunner(Database)
                            .ResolveSurvivorsAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                if (stdout is not null && stderr is not null)
                {
                    Console.WriteLine(
                        "PHASE6_CREATOR_STDOUT " + await stdout.ConfigureAwait(false)
                    );
                    Console.WriteLine(
                        "PHASE6_CREATOR_STDERR " + await stderr.ConfigureAwait(false)
                    );
                }
                ownedFinished = true;
            }
        }
        catch (Exception error)
        {
            secondary.Add(error);
        }
        finally
        {
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
            if (primary is null && secondary.Count == 0 && ownedFinished)
            {
                Cleanup(git.Dispose);
            }
            else
            {
                Cleanup(git.RestoreEnvironment);
                Console.WriteLine("PHASE6_PRESERVED_OWNED_ROOT " + Root);
            }
            Cleanup(() => Phase6State.VerifyEnvironment(environment));
            Console.WriteLine(
                $"PHASE6_CREATOR_RESOURCE_CLEANUP finished={ownedFinished}; "
                    + $"normalExitObserved={normalExitObserved}; secondaryCount={secondary.Count}"
            );
        }
        foreach (Exception error in secondary)
        {
            Console.Error.WriteLine("PHASE6_CLEANUP_SECONDARY " + Json.ExceptionDetails(error));
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
