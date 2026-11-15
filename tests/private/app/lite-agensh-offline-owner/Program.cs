using System.Diagnostics;
using System.IO.Pipes;
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
    private static async Task<int> Main(string[] args)
    {
        if (args is ["phase7-exact", _, _, _])
        {
            return await Phase7CreatorProgram.RunAsync(args[1..]).ConfigureAwait(false);
        }
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "N", out _))
        {
            return 2;
        }
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(120));
        CancellationToken token = deadline.Token;
        using NamedPipeClientStream pipe = new(
            ".",
            args[0],
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        Phase6NativeControl? control = null;
        int exitCode = 0;
        try
        {
            await pipe.ConnectAsync(token)
                .WaitAsync(TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
            using StreamReader reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true,
            };
            using Process self = Process.GetCurrentProcess();
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
            await ReceiveAsync(reader, args[1], "START", token).ConfigureAwait(false);
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
            RunDatabase database = new(configuration.StatePath, configuration);
            CreditLedger ledger = new(
                configuration.CampaignLedgerPath,
                configuration.CampaignAiCreditAllowance
            );
            Phase6NativeControl nativeControl = new(database);
            control = nativeControl;
            Func<CreditReservation, Task> opened = async reservation =>
            {
                using var campaign = OpenCampaign(configuration.CampaignLedgerPath);
                Sql.Execute(
                    campaign,
                    "CREATE TRIGGER phase6_retain_target BEFORE UPDATE ON credit_reservations "
                        + "WHEN OLD.id='"
                        + reservation.Id.Replace("'", "''", StringComparison.Ordinal)
                        + "' AND NEW.state='released' BEGIN "
                        + "SELECT RAISE(ABORT,'phase6-target-release-blocked'); END;"
                );
                await SendAsync(
                        writer,
                        args[1],
                        "OPENED",
                        new()
                        {
                            ["runId"] = database.RunId,
                            ["target"] = JsonSerializer.SerializeToNode(
                                ledger
                                    .Reservations(database.RunId)
                                    .Single(row => row.Id == reservation.Id)
                            ),
                        },
                        token
                    )
                    .ConfigureAwait(false);
                await ReceiveAsync(reader, args[1], "CONTINUE", token).ConfigureAwait(false);
            };
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
                            opened
                        )
                );
            JsonObject initialized = await NewCoordinator()
                .InitializeAsync(token)
                .ConfigureAwait(false);
            RequireSuccess(initialized);
            JsonObject preflight = await NewCoordinator()
                .PreflightAsync(token)
                .ConfigureAwait(false);
            RequireSuccess(preflight);
            JsonObject calibrated = await NewCoordinator()
                .CalibrateAsync(token)
                .ConfigureAwait(false);
            RequireSuccess(calibrated);
            JsonObject run = await NewCoordinator().RunAsync("run", token).ConfigureAwait(false);
            // Same terminal only: no factory Complete/worker Dispose after removal.
            if (
                run["ok"]!.GetValue<bool>()
                || !factory!
                    .SettlementFailures.ToJsonString()
                    .Contains("phase6-target-release-blocked", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException("Creator did not reach its selected rollback.");
            }
            using (var campaign = OpenCampaign(configuration.CampaignLedgerPath))
            {
                Sql.Execute(campaign, "DROP TRIGGER phase6_retain_target;");
            }
            await control.DisposeAsync().ConfigureAwait(false);
            JsonArray runtimes = [];
            foreach (Phase6NativeControl.Host host in control.Hosts)
            {
                runtimes.Add(host.Witness);
            }
            control = null;
            await SendAsync(
                    writer,
                    args[1],
                    "ENDED_UNRESOLVED",
                    new()
                    {
                        ["role"] = "setup-not-business-credit",
                        ["runId"] = database.RunId,
                        ["initialize"] = initialized,
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
            await ReceiveAsync(reader, args[1], "EXIT", token).ConfigureAwait(false);
            Console.WriteLine("PHASE6_CREATOR_SETUP_COMPLETE zero-business-credit");
        }
        catch (Exception error)
        {
            exitCode = 1;
            Console.Error.WriteLine(
                "PHASE6_CREATOR_PRIMARY " + LiteAgensh.Json.ExceptionDetails(error).ToJsonString()
            );
        }
        finally
        {
            if (control is not null)
            {
                try
                {
                    await control.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception secondary)
                {
                    Console.Error.WriteLine(
                        "PHASE6_CREATOR_CLEANUP_SECONDARY "
                            + LiteAgensh.Json.ExceptionDetails(secondary).ToJsonString()
                    );
                    exitCode = 1;
                }
            }
        }
        return exitCode;
    }

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
        string? line = await reader
            .ReadLineAsync(token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), token)
            .ConfigureAwait(false);
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

    private sealed class Factory(NativeWorkerFactory real, Func<CreditReservation, Task> opened)
        : INativeWorkerFactory
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
        Func<CreditReservation, Task> opened
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
            await opened(reservation).ConfigureAwait(false);
            throw new OperationException(
                "phase6_controlled_startup_stop",
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
