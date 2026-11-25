using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.OfflineOwner;

// Sole ordinary executable entry; never a test runner or business-test result.
internal static class Program
{
    private const string ControlledStartupStopCode = "controlled_startup_stop";

    private static async Task<int> Main(string[] args)
    {
        if (args is ["interrupted-exact", _, _, _])
        {
            return await InterruptedRunCreatorProgram.RunAsync(args[1..]).ConfigureAwait(false);
        }
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "N", out _))
        {
            return 2;
        }
        using FixtureDiagnosticSink evidence = new(
            args[1],
            "unresolved-creator",
            Environment.GetEnvironmentVariable("ThreeTestResultsDirectory"),
            report: Console.Error.WriteLine
        );
        using Activity evidenceScope = evidence.BeginScope();
        (string Resource, string Directory)[] diagnostics = [];
        string phase = "pipe-connect";
        evidence.Phase(phase, "started");
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(120));
        CancellationToken token = deadline.Token;
        using NamedPipeClientStream pipe = new(
            ".",
            args[0],
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        StreamReader? ownedReader = null;
        StreamWriter? ownedWriter = null;
        OfflineNativeControl? control = null;
        ExceptionDispatchInfo? startupProtocolFailure = null;
        int exitCode = 0;
        try
        {
            await pipe.ConnectAsync(TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
            evidence.Phase(phase, "completed");
            StreamReader reader = ownedReader = new(
                pipe, Encoding.UTF8, false, 4096, leaveOpen: true
            );
            StreamWriter writer = ownedWriter = new(
                pipe, new UTF8Encoding(false), 4096, leaveOpen: true
            );
            writer.AutoFlush = true;
            using Process self = Process.GetCurrentProcess();
            phase = "hello";
            evidence.Phase(phase, "prepared");
            await SendAsync(
                    writer,
                    args[1],
                    "HELLO",
                    new()
                    {
                        ["role"] = "setup-not-business-credit",
                        ["pid"] = self.Id,
                        ["birthTicks"] = self.StartTime.ToUniversalTime().Ticks,
                        ["moduleSha256"] = Hash(typeof(Program).Assembly.Location),
                        ["productSha256"] = Hash(typeof(Coordinator).Assembly.Location),
                    },
                    token
                )
                .ConfigureAwait(false);
            phase = "start-command";
            evidence.Phase(phase, "started");
            await ReceiveAsync(reader, args[1], "START", token).ConfigureAwait(false);
            evidence.Phase(phase, "completed");
            phase = "configuration";
            evidence.Phase(phase, "started");
            string configurationPath = Path.GetFullPath(args[2]);
            RunConfiguration configuration =
                JsonSerializer.Deserialize<RunConfiguration>(
                    File.ReadAllText(configurationPath),
                    LiteAgensh.Json.Options
                ) ?? throw new InvalidOperationException("Missing owned creator configuration.");
            string root = Path.GetDirectoryName(configurationPath)! + Path.DirectorySeparatorChar;
            if (
                !configuration.StatePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !configuration.CampaignLedgerPath.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException(
                    "Creator paths must belong to its supplied root."
                );
            }
            diagnostics =
            [
                ("run", Path.Combine(configuration.StatePath, "run.sqlite.diagnostics")),
                ("campaign", configuration.CampaignLedgerPath + ".diagnostics"),
            ];
            evidence.Phase(phase, "completed");
            phase = "fixture-state";
            evidence.Phase(phase, "started");
            RunDatabase database = new(configuration.StatePath, configuration);
            CreditLedger ledger = new(
                configuration.CampaignLedgerPath,
                configuration.CampaignAiCreditAllowance
            );
            OfflineNativeControl nativeControl = new(database);
            control = nativeControl;
            evidence.Phase(phase, "completed");
            evidence.BindRun(database.RunId, database.Now);
            bool continueReceived = false;
            async Task OpenedAsync(CreditReservation reservation, CancellationToken admissionToken)
            {
                using var campaign = OpenCampaign(configuration.CampaignLedgerPath);
                Sql.Execute(
                    campaign,
                    "CREATE TRIGGER retain_target_reservation BEFORE UPDATE ON credit_reservations "
                        + "WHEN OLD.id='"
                        + reservation.Id.Replace("'", "''", StringComparison.Ordinal)
                        + "' AND NEW.state='released' BEGIN "
                        + "SELECT RAISE(ABORT,'target-release-blocked'); END;"
                );
                using CancellationTokenSource startup =
                    CancellationTokenSource.CreateLinkedTokenSource(token, admissionToken);
                string stage = "OPENED";
                try
                {
                    evidence.Phase("opened-handshake", "sending");
                    await SendAsync(
                            writer,
                            args[1],
                            stage,
                            new()
                            {
                                ["runId"] = database.RunId,
                                ["target"] = JsonSerializer.SerializeToNode(
                                    ledger
                                        .Reservations(database.RunId)
                                        .Single(row => row.Id == reservation.Id)
                                ),
                            },
                            startup.Token
                        )
                        .ConfigureAwait(false);
                    stage = "CONTINUE";
                    evidence.Phase("continue-command", "waiting");
                    await ReceiveAsync(reader, args[1], stage, startup.Token).ConfigureAwait(false);
                    startup.Token.ThrowIfCancellationRequested();
                    continueReceived = true;
                    evidence.Phase("continue-command", "received");
                }
                catch (Exception error)
                {
                    error.Data["liteagensh.phase"] = stage;
                    startupProtocolFailure = ExceptionDispatchInfo.Capture(error);
                    throw;
                }
            }
            Factory? factory = null;
            CollaborationOperations collaboration = new(database);
            Coordinator NewCoordinator() =>
                new(
                    database,
                    collaboration,
                    new(database, collaboration),
                    (_, _, _) =>
                        factory = new(
                            new(database, new(database), ledger, nativeControl.CreateHost),
                            OpenedAsync
                        )
                );
            phase = "preflight";
            evidence.Phase(phase, "started");
            JsonObject preflight = await NewCoordinator()
                .PreflightAsync(token)
                .ConfigureAwait(false);
            RequireSuccess(preflight);
            evidence.Phase(phase, "completed");
            phase = "calibration";
            evidence.Phase(phase, "started");
            JsonObject calibrated = await NewCoordinator()
                .CalibrateAsync(token)
                .ConfigureAwait(false);
            RequireSuccess(calibrated);
            evidence.Phase(phase, "completed");
            phase = "run-start";
            evidence.Phase(phase, "started");
            JsonObject run = await NewCoordinator().RunAsync("run", token).ConfigureAwait(false);
            startupProtocolFailure?.Throw();
            evidence.Phase(phase, "completed");
            // Same terminal only: no factory Complete/worker Dispose after removal.
            if (
                run["ok"]!.GetValue<bool>()
                || !continueReceived
                || database.ReadMetadata("lifecycle:stop_reason") != ControlledStartupStopCode
                || !factory!
                    .SettlementFailures.ToJsonString()
                    .Contains("target-release-blocked", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Creator did not receive CONTINUE and reach its controlled startup stop"
                        + " and selected rollback."
                );
            }
            using (var campaign = OpenCampaign(configuration.CampaignLedgerPath))
            {
                Sql.Execute(campaign, "DROP TRIGGER retain_target_reservation;");
            }
            await control.DisposeAsync().ConfigureAwait(false);
            JsonArray runtimes = [];
            foreach (OfflineNativeControl.Host host in control.Hosts)
            {
                runtimes.Add(host.Witness);
            }
            control = null;
            phase = "ended-unresolved";
            evidence.Phase(phase, "sending");
            await SendAsync(
                    writer,
                    args[1],
                    "ENDED_UNRESOLVED",
                    new()
                    {
                        ["role"] = "setup-not-business-credit",
                        ["runId"] = database.RunId,
                        ["preflight"] = preflight,
                        ["calibration"] = calibrated,
                        ["run"] = run,
                        ["settlementFailures"] = factory.SettlementFailures.DeepClone(),
                        ["settlementDetails"] = factory.SettlementDetails.DeepClone(),
                        ["runtimes"] = runtimes,
                        ["preflightReceipt"] = database.ReadMetadata(
                            "native:controlled_preflight_receipt"
                        ),
                        ["billingReceipt"] = database.ReadMetadata(
                            "native:controlled_billing_receipt"
                        ),
                    },
                    token
                )
                .ConfigureAwait(false);
            phase = "exit-command";
            evidence.Phase(phase, "waiting");
            await ReceiveAsync(reader, args[1], "EXIT", token).ConfigureAwait(false);
            evidence.Phase(phase, "completed");
            Console.WriteLine("UNRESOLVED_CREATOR_SETUP_COMPLETE zero-business-credit");
        }
        catch (Exception error)
        {
            exitCode = 1;
            Exception primary = startupProtocolFailure?.SourceException ?? error;
            evidence.Phase(phase, "failed", error: primary);
            ReportFailure("PRIMARY", primary);
            if (!ReferenceEquals(error, primary))
            {
                ReportFailure("COORDINATOR_SECONDARY", error);
            }
        }
        finally
        {
            DisposePipeWrapper(ownedWriter);
            DisposePipeWrapper(ownedReader);
            if (control is not null)
            {
                try
                {
                    await control.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception secondary)
                {
                    ReportFailure("CLEANUP_SECONDARY", secondary);
                    exitCode = 1;
                }
            }
            evidence.CaptureSqlite(diagnostics);
        }
        return exitCode;

        void DisposePipeWrapper(IDisposable? wrapper)
        {
            try
            {
                wrapper?.Dispose();
            }
            catch (Exception error)
            {
                ReportFailure(exitCode == 0 ? "PRIMARY" : "CLEANUP_SECONDARY", error);
                exitCode = 1;
            }
        }
    }

    private static void ReportFailure(string kind, Exception error) =>
        Console.Error.WriteLine(
            $"UNRESOLVED_CREATOR_{kind} " + LiteAgensh.Json.ExceptionDetails(error).ToJsonString()
        );

    private static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static SqliteConnection OpenCampaign(string path)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                DefaultTimeout = 5,
                Pooling = false,
            }.ToString()
        );
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void RequireSuccess(JsonObject result)
    {
        if (result["ok"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException("Creator public setup failed: " + result);
        }
    }

    private static Task SendAsync(
        StreamWriter writer,
        string nonce,
        string stage,
        JsonObject data,
        CancellationToken token
    ) =>
        writer.WriteLineAsync(
            new JsonObject
            {
                ["version"] = 1,
                ["nonce"] = nonce,
                ["stage"] = stage,
                ["data"] = data,
            }
                .ToJsonString()
                .AsMemory(),
            token
        );

    private static async Task ReceiveAsync(
        StreamReader reader,
        string nonce,
        string stage,
        CancellationToken token
    )
    {
        using CancellationTokenSource reading =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<string?> read = reader.ReadLineAsync(reading.Token).AsTask();
        string? line;
        try
        {
            line = await read.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            primary.Data["liteagensh.phase"] = stage;
            List<(string Kind, Exception Error)> cleanupFailures = [];
            try
            {
                await reading.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception secondary)
            {
                cleanupFailures.Add(("PIPE_CANCEL_SECONDARY", secondary));
            }
            try
            {
                // A line delivered after the failed observation cannot complete this stage.
                await read.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) when (
                read.IsCanceled && canceled.CancellationToken == reading.Token
            )
            { }
            catch (Exception secondary)
            {
                if (!ReferenceEquals(primary, secondary))
                {
                    cleanupFailures.Add(("PIPE_READ_SECONDARY", secondary));
                }
            }
            foreach ((string kind, Exception secondary) in cleanupFailures)
            {
                secondary.Data["liteagensh.phase"] = stage;
                ReportFailure(kind, secondary);
            }
            throw;
        }
        if (
            line is null
            || line.Length > 4096
            || JsonNode.Parse(line) is not JsonObject message
            || message["version"]?.GetValue<int>() != 1
            || message["nonce"]?.GetValue<string>() != nonce
            || message["stage"]?.GetValue<string>() != stage
        )
        {
            throw new InvalidOperationException("Creator protocol did not receive " + stage);
        }
    }

    private sealed class Factory(
        NativeWorkerFactory real,
        Func<CreditReservation, CancellationToken, Task> opened
    ) : INativeWorkerFactory
    {
        public JsonArray SettlementFailures => real.SettlementFailures;
        public JsonArray SettlementDetails => real.SettlementDetails;

        public INativeWorker Create(CreditReservation? reservation, bool calibration)
        {
            INativeWorker worker = real.Create(reservation, calibration);
            return calibration ? worker : new Startup(worker, reservation!, opened);
        }

        public Task<JsonObject> PreflightAsync(
            IReadOnlyList<(string Worker, string Cwd)> peers,
            CancellationToken token
        ) => real.PreflightAsync(peers, token);

        public Task CompleteAsync() => real.CompleteAsync();

        public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
            CreditReservation reservation,
            string cwd,
            string evidencePath,
            CancellationToken token
        ) => throw new InvalidOperationException("Original creator cannot recover usage.");
    }

    private sealed class Startup(
        INativeWorker real,
        CreditReservation reservation,
        Func<CreditReservation, CancellationToken, Task> opened
    ) : INativeWorker
    {
        public string? SessionId => real.SessionId;
        public string ReservationId => real.ReservationId;
        public JsonObject? Diagnostic => real.Diagnostic;
        public string? NativeFailure => real.NativeFailure;
        public Exception? FirstFailure => real.FirstFailure;

        public void BindOwner(CancellationToken token) => real.BindOwner(token);

        public NativeRequest BeginRequest(CancellationToken token) => real.BeginRequest(token);

        public async Task StartAsync(
            string cwd,
            string? exactSession,
            CancellationToken token,
            string? recoveryLabel = null,
            WorkAdmission? admission = null
        )
        {
            await real.StartAsync(cwd, exactSession, token, recoveryLabel, admission)
                .ConfigureAwait(false);
            await opened(reservation, token).ConfigureAwait(false);
            throw new OperationException(
                ControlledStartupStopCode,
                "Controlled startup stopped after real Start and before any target dispatch."
            );
        }

        public Task<JsonObject> PromptAsync(
            string text,
            CancellationToken token,
            WorkAdmission? admission = null
        ) => throw new InvalidOperationException("Never-dispatched creator attempted a prompt.");

        public Task VerifyCreditsAsync(CancellationToken token) => real.VerifyCreditsAsync(token);

        public Task StopAsync() => real.StopAsync();

        public ValueTask DisposeAsync() => real.DisposeAsync();
    }
}
