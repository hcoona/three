using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using LiteAgensh.Tests.Support;

namespace LiteAgensh.Tests.OfflineOwner;

// Distinct ordinary-executable arrangement. No test module or test runner.
internal static class Phase7CreatorProgram
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "N", out _))
        {
            return 2;
        }
        using Phase7Evidence evidence = new(
            args[1], "original-creator",
            Environment.GetEnvironmentVariable("ThreeTestResultsDirectory")
        );
        using Activity evidenceScope = evidence.BeginScope();
        (string Resource, string Directory)[] diagnostics = [];
        string phase = "pipe-connect";
        evidence.Phase(phase, "started");
        using CancellationTokenSource protocol = new(TimeSpan.FromSeconds(120));
        CancellationToken token = protocol.Token;
        Phase7Sessions? sessions = null;
        Task<JsonObject>? active = null;
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        Exception? primary = null;
        try
        {
            using NamedPipeClientStream pipe = new(
                ".",
                args[0],
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await pipe.ConnectAsync(token)
                .WaitAsync(TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
            evidence.Phase(phase, "completed");
            using StreamReader reader = new(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true,
            };
            using Process self = Process.GetCurrentProcess();
            phase = "hello";
            evidence.Phase(phase, "prepared");
            await SendAsync(
                    "HELLO",
                    new()
                    {
                        ["role"] = "phase7-interrupted-owner-setup",
                        ["pid"] = self.Id,
                        ["birthTicks"] = self.StartTime.ToUniversalTime().Ticks,
                        ["moduleSha256"] = Hash(typeof(Phase7CreatorProgram).Assembly.Location),
                        ["productSha256"] = Hash(typeof(Coordinator).Assembly.Location),
                    }
                )
                .ConfigureAwait(false);
            phase = "start-command";
            evidence.Phase(phase, "started");
            string? line = await reader
                .ReadLineAsync(token)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(30), token)
                .ConfigureAwait(false);
            JsonObject command = JsonNode
                .Parse(line ?? throw new IOException("Missing START."))!
                .AsObject();
            Require(
                command["version"]?.GetValue<int>() == 1
                    && command["nonce"]?.GetValue<string>() == args[1]
                    && command["stage"]?.GetValue<string>() == "START",
                "Invalid creator START."
            );
            evidence.Phase(phase, "completed");
            phase = "configuration";
            evidence.Phase(phase, "started");
            string configurationPath = Path.GetFullPath(args[2]);
            RunConfiguration configuration = JsonSerializer.Deserialize<RunConfiguration>(
                File.ReadAllText(configurationPath),
                LiteAgensh.Json.Options
            )!;
            string root = Path.GetDirectoryName(configurationPath)! + Path.DirectorySeparatorChar;
            Require(
                configuration.StatePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && configuration.CampaignLedgerPath.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase
                    ),
                "Creator paths must belong to its supplied root."
            );
            diagnostics =
            [
                ("run", Path.Combine(configuration.StatePath, "run.sqlite.diagnostics")),
                ("campaign", configuration.CampaignLedgerPath + ".diagnostics"),
            ];
            evidence.Phase(phase, "completed");
            phase = "fixture-state";
            evidence.Phase(phase, "started");
            Phase7Clock clock = new(DateTimeOffset.UtcNow);
            RunDatabase database = new(configuration.StatePath, configuration, clock);
            CreditLedger ledger = new(
                configuration.CampaignLedgerPath,
                configuration.CampaignAiCreditAllowance
            );
            Phase7Journal journal = new(Path.Combine(root, "controlled-operations.json"));
            Phase7Sessions control = new(
                database,
                OwnedChildControl.ControlledNativeHostRole,
                journal,
                evidence: evidence
            );
            sessions = control;
            CollaborationOperations collaboration = new(database);
            TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource returnGate = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Factory? originalFactory = null;
            bool pauseFinalReturn = false;
            Coordinator NewCoordinator(string operation) =>
                new(
                    database,
                    collaboration,
                    new(database, collaboration),
                    (_, _, _) =>
                    {
                        Factory factory = new(
                            new(
                                database,
                                new(database),
                                ledger,
                                () => control.CreateHost(operation)
                            ),
                            operation == "run"
                                ? async () =>
                                {
                                    // Real factory Complete has already settled workers/common host
                                    // and booked usage. Normal wrapper cleanup is separate and
                                    // real.
                                    await control.DisposeAsync().ConfigureAwait(false);
                                    evidence.Phase(
                                        "native-common-settlement", "completed", clock.GetUtcNow()
                                    );
                                    if (pauseFinalReturn)
                                    {
                                        evidence.Phase(
                                            "public-return-gate", "held", clock.GetUtcNow()
                                        );
                                        evidence.Flush();
                                        settled.TrySetResult();
                                        await returnGate
                                            .Task.WaitAsync(token)
                                            .ConfigureAwait(false);
                                    }
                                }
                        : null
                        );
                        if (operation == "run")
                        {
                            originalFactory = factory;
                        }
                        return factory;
                    }
                );
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            evidence.BindRun(database.RunId, clock.GetUtcNow());
            phase = "preflight";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            RequireSuccess(
                await NewCoordinator("preflight").PreflightAsync(token).ConfigureAwait(false)
            );
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            phase = "calibration";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            RequireSuccess(
                await NewCoordinator("calibration").CalibrateAsync(token).ConfigureAwait(false)
            );
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            using IDisposable observation = clock.BeginObservation();
            DateTimeOffset workDeadline = clock
                .GetUtcNow()
                .AddSeconds(configuration.DurationSeconds);
            clock.WorkDeadline = workDeadline;
            phase = "run-start";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            active = NewCoordinator("run").RunAsync("run", caller.Token);
            evidence.Phase(phase, "invoked", clock.GetUtcNow());
            phase = "first-send";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(
                token
            );
            Task<Phase7Sessions.Turn> entered = control.NextTurnAsync("worker1", read.Token);
            Phase7Sessions.Turn first;
            try
            {
                await WaitFixedAsync(entered).ConfigureAwait(false);
                first = await entered.ConfigureAwait(false);
                evidence.Phase(phase, "entered", clock.GetUtcNow());
            }
            finally
            {
                read.Cancel();
                try
                {
                    await entered.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (read.IsCancellationRequested) { }
            }
            long batch = Number(
                "SELECT id FROM delivery_batches WHERE recipient='worker1' AND state='sending';"
            );
            string sessionId = first.Session.SessionId;
            Require(
                database.ReadMetadata("native:bootstrap:" + sessionId) is null,
                "Bootstrap was confirmed before the original Send completed."
            );
            evidence.Phase("controlled-response", "requested", clock.GetUtcNow());
            first.Complete();
            Require(clock.GetUtcNow() < workDeadline, "Original ACK observation reached deadline.");
            phase = "acknowledgement";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            using (CancellationTokenSource ackRead =
                CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task acknowledged = ReadAckAsync(ackRead.Token);
                Exception? acknowledgementFailure = null;
                try
                {
                    await WaitFixedAsync(acknowledged).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    acknowledgementFailure = error;
                    throw;
                }
                finally
                {
                    ackRead.Cancel();
                    try
                    {
                        await acknowledged.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ackRead.IsCancellationRequested) { }
                    catch (Exception error) when (acknowledgementFailure is not null)
                    {
                        Console.Error.WriteLine(
                            "PHASE7_READINESS_SECONDARY " + LiteAgensh.Json.ExceptionDetails(error)
                        );
                    }
                }
            }
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            Require(clock.GetUtcNow() < workDeadline, "Original ACK observation reached deadline.");
            phase = "bootstrap-confirmation";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            string confirmation =
                database.ReadMetadata("native:bootstrap:" + sessionId)
                ?? throw new InvalidOperationException(
                    "Completed original Send did not confirm bootstrap."
                );
            Require(
                first.Response.IsCompletedSuccessfully,
                "Original Send did not actually complete."
            );
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            pauseFinalReturn = true;
            phase = "shared-runtime-settlement";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            caller.Cancel();
            await WaitFixedAsync(settled.Task).ConfigureAwait(false);
            await settled.Task.ConfigureAwait(false);
            evidence.Phase(phase, "completed", clock.GetUtcNow());

            async Task WaitFixedAsync(Task goal)
            {
                token.ThrowIfCancellationRequested();
                Task<JsonObject> subject = active
                    ?? throw new InvalidOperationException("Original run has not started.");
                await Task.WhenAny(goal, subject).WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (subject.IsCompleted)
                {
                    await subject.ConfigureAwait(false);
                    Require(
                        goal.IsCompleted, "Original run ended before the required observation."
                    );
                }
                await goal.WaitAsync(token).ConfigureAwait(false);
            }

            async Task ReadAckAsync(CancellationToken next)
            {
                next.ThrowIfCancellationRequested();
                while ((string?)Scalar(
                    "SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch)
                ) != "sent")
                {
                    await Task.Delay(10, next).ConfigureAwait(false);
                }
                next.ThrowIfCancellationRequested();
            }
            phase = "checkpoint-state";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            CreditReservation target = ledger
                .Reservations(database.RunId)
                .Single(row => row.Worker == "worker1");
            Require(
                target.State == "reconciled" && target.Actual == 10m && target.Allowance > 10m,
                "Original target lacks reconciled positive retained backing."
            );
            Require(
                !active.IsCompleted
                    && Number("SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;")
                        == 1,
                "The original public operation must still hold its unfinished segment."
            );
            Require(
                originalFactory!.SettlementFailures.Count == 0,
                "Original native settlement failed."
            );
            phase = "ownership-probe";
            evidence.Phase(phase, "started", clock.GetUtcNow());
            JsonObject ownership = await NewCoordinator("probe")
                .RunAsync("reconnect", token)
                .ConfigureAwait(false);
            Require(
                ownership["code"]?.GetValue<string>() == "conflict",
                "Checkpoint does not retain real exclusive coordinator ownership."
            );
            evidence.Phase(phase, "completed", clock.GetUtcNow());
            Phase7Sessions.Host host = control.Hosts.Single(item => item.Operation == "run");
            JsonObject history = journal.Snapshot(sessionId);
            Require(
                !history["pending"]!.GetValue<bool>() && history["requests"]!.GetValue<long>() == 1,
                "Original controlled work is not terminal."
            );
            phase = "checkpoint";
            evidence.Phase(phase, "prepared", clock.GetUtcNow());
            evidence.CaptureSqlite(diagnostics);
            evidence.Flush();
            await SendAsync(
                    "CHECKPOINT",
                    new()
                    {
                        ["runId"] = database.RunId,
                        ["target"] = JsonSerializer.SerializeToNode(target),
                        ["clock"] = clock.GetUtcNow(),
                        ["workDeadline"] = workDeadline,
                        ["batch"] = batch,
                        ["confirmation"] = confirmation,
                        ["producerPrompt"] = first.Text,
                        ["runtimeId"] = host.Id,
                        ["nativeHost"] = host.Witness,
                        ["history"] = history,
                        ["segmentCount"] = Number("SELECT COUNT(*) FROM coordinator_segments;"),
                        ["unfinished"] = Number(
                            "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                        ),
                        ["ownerProbe"] = ownership,
                        ["publicOperationReturned"] = active.IsCompleted,
                        ["preflightReceipt"] = database.ReadMetadata(
                            "native:controlled_preflight_receipt"
                        ),
                        ["billingReceipt"] = database.ReadMetadata(
                            "native:controlled_billing_receipt"
                        ),
                    }
                )
                .ConfigureAwait(false);
            // Genuine crash is the parent's selected action. This independently
            // bounded protocol never returns the decorated Complete successfully.
            await active.WaitAsync(token).ConfigureAwait(false);
            throw new InvalidOperationException("Interrupted-owner gate returned without a crash.");

            Task SendAsync(string stage, JsonObject data) =>
                writer.WriteLineAsync(
                    new JsonObject
                    {
                        ["version"] = 1,
                        ["nonce"] = args[1],
                        ["stage"] = stage,
                        ["data"] = data,
                    }
                        .ToJsonString()
                        .AsMemory(),
                    token
                );

            object? Scalar(string sql, params (string Name, object? Value)[] parameters)
            {
                using var connection = database.Open();
                return Sql.Scalar(connection, sql, null, parameters);
            }
            long Number(string sql) =>
                Convert.ToInt64(Scalar(sql), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception error)
        {
            primary = error;
            evidence.Phase(phase, "failed", error: error);
            Console.Error.WriteLine(
                "PHASE7_CREATOR_PRIMARY " + LiteAgensh.Json.ExceptionDetails(error)
            );
        }
        finally
        {
            bool cleanupFailed = false;
            evidence.Phase("creator-cleanup", "started");
            caller.Cancel();
            if (active is not null)
            {
                // Release only the protocol wait by cancellation; never make a
                // successful Complete/ended-segment receipt on the failure path.
                protocol.Cancel();
                try
                {
                    await active.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    cleanupFailed = true;
                    evidence.Phase("creator-join", "failed", error: error);
                    Console.Error.WriteLine(
                        "PHASE7_CREATOR_JOIN_SECONDARY " + LiteAgensh.Json.ExceptionDetails(error)
                    );
                }
            }
            if (sessions is not null)
            {
                try
                {
                    await sessions.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    cleanupFailed = true;
                    evidence.Phase("creator-session-cleanup", "failed", error: error);
                    Console.Error.WriteLine(
                        "PHASE7_CREATOR_CLEANUP_SECONDARY "
                            + LiteAgensh.Json.ExceptionDetails(error)
                    );
                }
            }
            evidence.CaptureSqlite(diagnostics);
            evidence.Phase("creator-cleanup", cleanupFailed ? "failed" : "completed");
            evidence.Flush();
        }
        return primary is null ? 0 : 1;
    }

    private static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess(JsonObject result) =>
        Require(result["ok"]?.GetValue<bool>() == true, "Creator public setup failed: " + result);

    private sealed class Factory(NativeWorkerFactory real, Func<Task>? afterComplete)
        : INativeWorkerFactory
    {
        public JsonArray SettlementFailures => real.SettlementFailures;
        public JsonArray SettlementDetails => real.SettlementDetails;

        public INativeWorker Create(CreditReservation? reservation, bool calibration) =>
            real.Create(reservation, calibration);

        public Task<JsonObject> PreflightAsync(
            IReadOnlyList<(string Worker, string Cwd)> peers,
            CancellationToken token
        ) => real.PreflightAsync(peers, token);

        public async Task CompleteAsync()
        {
            await real.CompleteAsync().ConfigureAwait(false);
            if (afterComplete is not null)
            {
                await afterComplete().ConfigureAwait(false);
            }
        }

        public Task<RecoveredNativeUsage> ReadSettledUsageAsync(
            CreditReservation reservation,
            string cwd,
            string evidencePath,
            CancellationToken token
        ) => real.ReadSettledUsageAsync(reservation, cwd, evidencePath, token);
    }
}
