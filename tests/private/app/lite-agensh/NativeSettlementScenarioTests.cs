using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeSettlementScenarioTests(TestContext testContext)
{
    private static readonly string[] RetainedQueries =
    [
        "SELECT key,value FROM meta ORDER BY key;",
        "SELECT * FROM coordinator_segments ORDER BY id;",
        "SELECT * FROM coordinator_turns ORDER BY batch_id;",
        "SELECT * FROM process_leases ORDER BY id;",
    ];

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("per-session", "none")]
    [DataRow("shared", "none")]
    [DataRow("shared", "host")]
    [DataRow("per-session", "session")]
    public async Task UndispatchedCancellationWaitsForCohortWithoutInventingMeasuredUsage(
        string allocation, string failure
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, allocation);
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new(failure, token,
            evidenceDatabase: database, emptyStatus: true);
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        foreach (string worker in new[] { "worker1", "worker2" })
        {
            CreditReservation reservation = ledger.Reserve(
                database.RunId, worker, allocation == "shared" ? 0 : 60, 300,
                Path.Combine(fixture.RootPath, worker + ".sdk-evidence"),
                shared: allocation == "shared"
            );
            INativeWorker peer = factory.Create(reservation, calibration: false);
            await peer.StartAsync(fixture.RepositoryPath, null, token);
            if (worker == "worker2")
            {
                await peer.PromptAsync("Controlled request.", token);
            }
        }
        decimal backing = ledger.Status(database.RunId, 300)["campaignCommitted"]!
            .GetValue<decimal>();
        Task completion = factory.CompleteAsync();
        try
        {
            await host.Closing.Task.WaitAsync(token);
            Assert.IsFalse(host.Settled);
            Assert.AreEqual(backing,
                ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
            Assert.IsFalse(ledger.Reservations().Any(row => row.State == "released"));
            host.Release.TrySetResult();
            if (failure != "none")
            {
                await Assert.ThrowsExactlyAsync<OperationException>(() => completion);
                Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
                Assert.AreEqual("credit_unknown",
                    Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
                return;
            }
            await completion;
            CreditReservation cancelled = ledger.Reservations()[0];
            Assert.AreEqual("released", cancelled.State);
            Assert.IsNull(cancelled.Actual);
            Assert.IsFalse(cancelled.GrantOpen);
            Assert.AreEqual("reconciled", ledger.Reservations()[1].State);
            Assert.AreEqual(1m, ledger.Reservations()[1].Actual);
            Assert.AreEqual(1m,
                ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
            ledger.RequirePaidAdmission();
            JsonObject repeated = NoDispatchCancellation.Cancel(
                database, ledger, cancelled.Id, retained: false
            );
            Assert.IsTrue(repeated["idempotent"]!.GetValue<bool>());
            Assert.IsNull(repeated["measuredActual"]);
            Assert.IsNull(repeated["aiCreditsStatus"]);
            Assert.AreEqual("controlled-native-dependency",
                repeated["proof"]!["source"]!["evidenceKind"]!.GetValue<string>());
            using SqliteConnection campaign = Campaign(database);
            Assert.AreEqual(1L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_reports;"));
            Assert.AreEqual(1L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_cancellations;"));
            Assert.AreEqual(0L, Sql.Scalar(campaign,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null, ("$id", cancelled.Id)));
        }
        finally
        {
            host.Release.TrySetResult();
            try
            {
                await completion;
            }
            catch (OperationException)
            {
                // Retain the observed failed settlement.
            }
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task RetainedPublicReconciliationPreservesEndedStateAndRejectsLiveOwnership()
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        var (database, ledger, reservation, coordinator) =
            await PrepareUnpromptedAsync(fixture, token);
        ledger.MarkUncertain(reservation.Id);
        using (FileStream ownership = new(
            Path.Combine(database.StatePath, "coordinator.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Scenario.Failure(coordinator.Reconcile(reservation.Id), "conflict");
        }
        Scenario.Failure(coordinator.Reconcile(reservation.Id), "credit_unknown");
        Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        using (SqliteConnection campaign = Campaign(database))
        {
            Sql.Execute(campaign, "UPDATE credit_sdk_owners SET start_ticks=start_ticks-1;");
        }
        using (SqliteConnection state = database.Open())
        {
            Sql.Execute(state,
                """
                UPDATE process_leases SET start_ticks=start_ticks-1;
                INSERT INTO coordinator_segments(
                    mode,state,started_at,deadline,ended_at,stop_reason)
                VALUES('run','unresolved','start','deadline','end','retained failure');
                """);
        }
        string originalState = RetainedState(database);
        string reportPath = Path.Combine(fixture.RootPath, "review.zh.md");
        File.WriteAllText(reportPath, "Original peer-written report.");
        string callerState = Path.Combine(fixture.RootPath, "caller-state");
        RunDatabase caller = new(callerState, fixture.Configuration with
        {
            StatePath = callerState,
            CampaignLedgerPath = Path.Combine(fixture.RootPath, "caller-credits.sqlite"),
        });
        ProcessRunner runner = new(caller);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ProcessResult process = await runner.RunAsync(
                Scenario.DotnetHost,
                [typeof(Coordinator).Assembly.Location, "reconcile", "--state", database.StatePath,
                    "--reservation", reservation.Id],
                fixture.RepositoryPath, TimeSpan.FromSeconds(30), token
            );
            Assert.AreEqual(0, process.ExitCode, process.StandardOutput + process.StandardError);
            JsonObject envelope = JsonNode.Parse(process.StandardOutput)!.AsObject();
            Assert.AreEqual("controlled-native-dependency",
                envelope["nativeEvidenceKind"]!.GetValue<string>());
            JsonObject result = Scenario.Data(envelope);
            Assert.AreEqual("verified-before-first-dispatch",
                result["classification"]!.GetValue<string>());
            Assert.AreEqual(attempt == 1, result["idempotent"]!.GetValue<bool>());
            Assert.IsNull(result["measuredActual"]);
            Assert.IsNull(result["aiCreditsStatus"]);
            Assert.IsFalse(result["accounting"]!["uncertain"]!.GetValue<bool>());
        }
        Assert.AreEqual(originalState, RetainedState(database));
        Assert.AreEqual("Original peer-written report.", File.ReadAllText(reportPath));
        Assert.IsNull(database.ReadMetadata("native:sdk_preflight_receipt"));
        Assert.IsNull(database.ReadMetadata("native:sdk_billing_receipt"));
        Assert.IsNull(ledger.Reservations()[0].Actual);
        using SqliteConnection retainedCampaign = Campaign(database);
        Assert.AreEqual(0L, Sql.Scalar(retainedCampaign, "SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(1L,
            Sql.Scalar(retainedCampaign, "SELECT COUNT(*) FROM credit_cancellations;"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("dispatch")]
    [DataRow("partial")]
    [DataRow("missing-numeric")]
    [DataRow("nonzero")]
    [DataRow("nonfinite")]
    [DataRow("requests")]
    [DataRow("source")]
    [DataRow("association")]
    public async Task RetainedReconciliationRejectsContradictoryOrMissingProof(string defect)
    {
        using RunFixture fixture = new();
        var (database, ledger, reservation, coordinator) =
            await PrepareUnpromptedAsync(fixture, testContext.CancellationToken);
        ledger.MarkUncertain(reservation.Id);
        using (SqliteConnection campaign = Campaign(database))
        {
            Sql.Execute(campaign, "UPDATE credit_sdk_owners SET start_ticks=start_ticks-1;");
            switch (defect)
            {
                case "dispatch":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET dispatch_count=1;");
                    break;
                case "partial":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET status='partial';");
                    break;
                case "missing-numeric":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET reported_nano_aiu=NULL;");
                    break;
                case "nonzero":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET reported_nano_aiu='1';");
                    break;
                case "nonfinite":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET reported_nano_aiu='NaN';");
                    break;
                case "requests":
                    Sql.Execute(campaign, "UPDATE credit_sdk_usage SET reported_requests=1;");
                    break;
            }
        }
        using (SqliteConnection state = database.Open())
        {
            Sql.Execute(state, "UPDATE process_leases SET start_ticks=start_ticks-1;");
            if (defect == "source")
            {
                Sql.Execute(state, "DELETE FROM meta WHERE key LIKE 'native:dispatch_contract:%';");
            }
            else if (defect == "association")
            {
                Sql.Execute(state, "UPDATE native_attempts SET worker='worker2';");
            }
        }
        string original = RetainedState(database);
        Scenario.Failure(coordinator.Reconcile(reservation.Id), "credit_unknown");
        Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        Assert.AreEqual(original, RetainedState(database));
        Assert.AreEqual("credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("binding")]
    [DataRow("ledger")]
    [DataRow("malformed")]
    public async Task PublicReconciliationDoesNotReconstructMissingOrMalformedEvidence(
        string defect
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        var (database, ledger, reservation, _) = await PrepareUnpromptedAsync(fixture, token);
        ledger.MarkUncertain(reservation.Id);
        string ledgerPath = database.Configuration.CampaignLedgerPath;
        if (defect == "ledger")
        {
            File.Move(ledgerPath, ledgerPath + ".retained");
        }
        else
        {
            using SqliteConnection state = database.Open();
            Sql.Execute(state, defect == "binding"
                ? "DELETE FROM meta WHERE key='native:ledger_binding';"
                : "UPDATE meta SET value='{' WHERE key LIKE 'native:dispatch_contract:%';");
        }
        string original = RetainedState(database);
        using RunFixture callerFixture = new();
        RunDatabase caller = new(callerFixture.StatePath, callerFixture.Configuration);
        ProcessResult process = await new ProcessRunner(caller).RunAsync(
            Scenario.DotnetHost,
            [typeof(Coordinator).Assembly.Location, "reconcile", "--state", database.StatePath,
                "--reservation", reservation.Id],
            fixture.RepositoryPath, TimeSpan.FromSeconds(30), token);
        Assert.AreEqual(1, process.ExitCode);
        Assert.IsFalse(JsonNode.Parse(process.StandardOutput)!["ok"]!.GetValue<bool>());
        Assert.AreEqual(original, RetainedState(database));
        if (defect == "ledger")
        {
            Assert.IsFalse(File.Exists(ledgerPath));
        }
        else
        {
            Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        }
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task DispatchAdmissionAndCancellationCannotBothWin()
    {
        using RunFixture fixture = new();
        var (database, ledger, reservation, _) =
            await PrepareUnpromptedAsync(fixture, testContext.CancellationToken);
        Task<bool> admitted = Task.Run(() => Try(() =>
            ledger.BeginDispatch(database.RunId, reservation.Id, 300)),
            testContext.CancellationToken);
        Task<bool> cancelled = Task.Run(() => Try(() =>
            NoDispatchCancellation.Cancel(database, ledger, reservation.Id, retained: false)),
            testContext.CancellationToken);
        await Task.WhenAll(admitted, cancelled);
        Assert.AreNotEqual(admitted.Result, cancelled.Result);
        Assert.AreEqual(cancelled.Result ? "released" : "active", ledger.Reservations()[0].State);
        using SqliteConnection campaign = Campaign(database);
        Assert.AreEqual(admitted.Result ? 1L : 0L,
            Sql.Scalar(campaign, "SELECT dispatch_count FROM credit_sdk_usage;"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("none")]
    [DataRow("ignored-booleans")]
    [DataRow("source")]
    [DataRow("snapshot")]
    [DataRow("digest")]
    public async Task LegacyReconciliationBindsReferencedObservationsInsteadOfSuccessFlags(
        string defect
    )
    {
        using RunFixture fixture = new();
        var (database, ledger, reservation, coordinator) =
            await PrepareUnpromptedAsync(fixture, testContext.CancellationToken);
        ledger.MarkUncertain(reservation.Id);
        using (SqliteConnection campaign = Campaign(database))
        {
            Sql.Execute(campaign, "UPDATE credit_sdk_owners SET start_ticks=start_ticks-1;");
        }
        using (SqliteConnection state = database.Open())
        {
            Sql.Execute(state,
                """
                UPDATE process_leases SET start_ticks=start_ticks-1;
                DELETE FROM meta WHERE key LIKE 'native:dispatch_contract:%';
                """);
        }
        database.WriteMetadata("worktree:worker1", fixture.RepositoryPath);
        string evidencePath = LegacyFixture(fixture, database, reservation, defect);
        string original = RetainedState(database);
        JsonObject response = coordinator.Reconcile(reservation.Id, evidencePath);
        if (defect is "none" or "ignored-booleans")
        {
            JsonObject result = Scenario.Data(response);
            Assert.AreEqual("verified-before-first-dispatch",
                result["classification"]!.GetValue<string>());
            Assert.IsNull(result["measuredActual"]);
            Assert.IsNull(result["aiCreditsStatus"]);
            Assert.IsTrue(Scenario.Data(coordinator.Reconcile(reservation.Id, evidencePath))
                ["idempotent"]!.GetValue<bool>());
            Assert.AreEqual("verified-before-first-dispatch",
                ledger.Status(database.RunId, 300)["cancellations"]![0]!["classification"]!
                    .GetValue<string>());
        }
        else
        {
            Scenario.Failure(response, "credit_unknown");
            Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        }
        Assert.AreEqual(original, RetainedState(database));
        Assert.IsNull(ledger.Reservations()[0].Actual);
        Assert.IsFalse(File.Exists(
            Path.Combine(fixture.RootPath, "run-after.evidence-only.sqlite-shm")));
        Assert.IsFalse(File.Exists(
            Path.Combine(fixture.RootPath, "campaign-after.evidence-only.sqlite-shm")));
    }

    private static string LegacyFixture(
        RunFixture fixture, RunDatabase database, CreditReservation reservation, string defect
    )
    {
        const string subject = "3805a50490afbdd6c53ec4e3c6e8c64a3150858f";
        JsonObject proof = new()
        {
            ["subject"] = subject,
            ["runId"] = database.RunId,
            ["reservationId"] = reservation.Id,
            ["sessionId"] = reservation.SessionId,
            ["worker"] = reservation.Worker,
            ["checks"] = new JsonObject { ["untrustedSuccessFlag"] = defect != "ignored-booleans" },
            ["proofPathSources"] = JsonNode.Parse(
                """
                [
                  {
                    "path":"src/private/app/lite-agensh/NativeWorker.cs",
                    "sha256":"8e9aed624796322a58f4dc4b085c31621be36263a00cc5b30e16c6ec482fa9ee"
                  },
                  {
                    "path":"src/private/app/lite-agensh/CreditLedger.cs",
                    "sha256":"0c860209d0eb44cbb0003e80fa3c2404e0d8b5e5c000b73666f072c7785c0b11"
                  },
                  {
                    "path":"src/private/app/lite-agensh/NativeSdkSession.cs",
                    "sha256":"3cfc60cc2acfe148d6a928d6952ddad958e05ee37ada1463d0d5dca6b2d197f6"
                  },
                  {
                    "path":"src/private/app/lite-agensh/NativeSdkHost.cs",
                    "sha256":"d5dcdfd62234a721ec402282a706f58914c525c949181e53d748b6523428d254"
                  },
                  {
                    "path":"src/private/app/lite-agensh/NativeRuntime.cs",
                    "sha256":"97efa1a04c3b6e7b31208bda8e391112fc053e939df7caa738eda0018d1d8355"
                  },
                  {
                    "path":"src/private/app/lite-agensh/NativeWorkerFactory.cs",
                    "sha256":"6ec426801be69b76464b882b71bf47f3facdc0595aaa6cdcb75210542ff1ac86"
                  }
                ]
                """),
        };
        File.WriteAllText(Path.Combine(fixture.RootPath, "outcome-private.json"),
            new JsonObject { ["sourceRevision"] = subject, ["runId"] = database.RunId }
                .ToJsonString());
        File.WriteAllText(Path.Combine(fixture.RootPath, "source-after.json"),
            new JsonObject
            {
                ["changedOriginalFiles"] = new JsonArray(),
                ["checkouts"] = new JsonArray(new JsonObject
                {
                    ["path"] = fixture.RepositoryPath,
                    ["headMatches"] = defect != "source",
                    ["indexMatches"] = true,
                    ["status"] = "",
                }),
            }.ToJsonString());
        foreach (string name in new[] { "run", "campaign" })
        {
            using SqliteConnection source = name == "run" ? database.Open() : Campaign(database);
            using SqliteConnection target = new(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(fixture.RootPath, name + "-after.evidence-only.sqlite"),
                Pooling = false,
            }.ToString());
            target.Open();
            source.BackupDatabase(target);
            if (name == "run" && defect == "snapshot")
            {
                Sql.Execute(target, "UPDATE native_attempts SET worker='worker2';");
            }
        }
        JsonObject hashes = new();
        foreach (string name in new[]
        {
            "run-after.evidence-only.sqlite", "campaign-after.evidence-only.sqlite",
            "outcome-private.json", "source-after.json",
        })
        {
            hashes[name] = Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.RootPath, name))));
        }
        proof["inputSha256"] = hashes;
        if (defect == "digest")
        {
            hashes["outcome-private.json"] = new string('0', 64);
        }
        string path = Path.Combine(fixture.RootPath, "legacy-proof.json");
        File.WriteAllText(path, proof.ToJsonString());
        return path;
    }

    private static bool Try(Action operation)
    {
        try
        {
            operation();
            return true;
        }
        catch (OperationException)
        {
            return false;
        }
    }

    private static RunDatabase ControlledDatabase(RunFixture fixture, string allocation) =>
        new(fixture.StatePath, fixture.Configuration with
        {
            NativeExecutable = Path.Combine(fixture.RootPath, "missing-native-runtime.exe"),
            CreditAllocation = allocation,
            RunAiCreditAllowance = 300,
            CampaignAiCreditAllowance = 600,
        });

    private static async Task<(RunDatabase, CreditLedger, CreditReservation, Coordinator)>
        PrepareUnpromptedAsync(RunFixture fixture, CancellationToken token)
    {
        RunDatabase database = ControlledDatabase(fixture, "shared");
        LocalTools tools = new(database);
        Coordinator coordinator = new(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new("none", token,
            evidenceDatabase: database, emptyStatus: true);
        host.Release.TrySetResult();
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        CreditReservation reservation = ledger.Reserve(database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "unprompted.sdk-evidence"), shared: true);
        INativeWorker peer = factory.Create(reservation, calibration: false);
        await peer.StartAsync(fixture.RepositoryPath, null, token);
        await peer.StopAsync();
        await host.DisposeAsync();
        return (database, ledger, reservation, coordinator);
    }

    private static SqliteConnection Campaign(RunDatabase database)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = database.Configuration.CampaignLedgerPath,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string RetainedState(RunDatabase database)
    {
        using SqliteConnection connection = database.Open();
        return string.Join("\n", RetainedQueries.Select(query =>
        {
            using SqliteCommand command = Sql.Command(connection, query);
            using SqliteDataReader reader = command.ExecuteReader();
            List<string> rows = [];
            while (reader.Read())
            {
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.GetValue(index).ToString())));
            }
            return string.Join("\n", rows);
        }));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(1, "per-session", false)]
    [DataRow(2, "shared", true)]
    public async Task ProductionPreflightRetainsObservedUsageInDiagnosticsAndReceipt(
        int peerCount, string allocation, bool missingMetrics
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token, configuration =>
            configuration with
            {
                WorkerCount = peerCount,
                CreditAllocation = allocation,
                NativeExecutable = Path.Combine(
                    configuration.RepositoryPath, "missing-native-runtime.exe"
                ),
            });
        ControlledSdkHost host = new("none", token, missingMetrics);
        host.Release.TrySetResult();
        Coordinator coordinator = new(
            fixture.Database, fixture.Collaboration, fixture.Operations,
            (database, processes, credits) => new NativeWorkerFactory(
                database, processes, credits, () => host
            )
        );

        JsonObject data = Scenario.Data(await coordinator.PreflightAsync(token));
        JsonObject receipt = JsonNode.Parse(
            fixture.Database.ReadMetadata("native:controlled_preflight_receipt")!
        )!.AsObject();
        JsonArray sessions = data["sessions"]!.AsArray();
        Assert.HasCount(peerCount, sessions);
        Assert.IsTrue(JsonNode.DeepEquals(sessions, receipt["sessions"]));
        Assert.IsTrue(JsonNode.DeepEquals(sessions[0], data["diagnostics"]));
        foreach (JsonNode? session in sessions)
        {
            Assert.IsNotNull(session);
            Assert.IsTrue(session.AsObject().ContainsKey("totalUserRequests"));
            Assert.AreEqual(0L, session["totalUserRequests"]!.GetValue<long>());
            if (missingMetrics)
            {
                Assert.IsTrue(session.AsObject().ContainsKey("reportedNanoAiu"));
                Assert.IsNull(session["reportedNanoAiu"]);
                Assert.IsNull(session["aiCreditsStatus"]);
            }
            else
            {
                Assert.AreEqual("0", session["reportedNanoAiu"]!.GetValue<string>());
                Assert.AreEqual("complete", session["aiCreditsStatus"]!.GetValue<string>());
            }
        }
        Assert.IsTrue(host.Settled);
        Assert.IsTrue(host.Sessions.All(session => session.CloseAttempted));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("per-session", "none")]
    [DataRow("shared", "none")]
    [DataRow("shared", "host")]
    [DataRow("per-session", "session")]
    public async Task ProductionCohortRetainsBackingUntilHostSettlement(
        string allocation, string failure
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration with
        {
            NativeExecutable = Path.Combine(fixture.RootPath, "missing-native-runtime.exe"),
            CreditAllocation = allocation,
            RunAiCreditAllowance = 300,
            CampaignAiCreditAllowance = 600,
        });
        CollaborationOperations collaboration = new(database);
        _ = new Coordinator(database, collaboration, new(database, collaboration));
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new(failure, token);
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        List<INativeWorker> peers = [];
        foreach (string worker in new[] { "worker1", "worker2" })
        {
            CreditReservation reservation = ledger.Reserve(
                database.RunId, worker, allocation == "shared" ? 0 : 60, 300,
                Path.Combine(fixture.RootPath, $"usage-{worker}"), shared: allocation == "shared"
            );
            INativeWorker peer = factory.Create(reservation, calibration: false);
            peers.Add(peer);
            await peer.StartAsync(fixture.RepositoryPath, null, token);
        }
        foreach (INativeWorker peer in peers)
        {
            await peer.PromptAsync("Controlled request.", token);
            if (failure == "session")
            {
                await peer.VerifyCreditsAsync(token);
            }
        }
        decimal backing = ledger.Status(database.RunId, 300)["campaignCommitted"]!
            .GetValue<decimal>();
        ledger.RequirePaidAdmission();
        if (failure == "session")
        {
            OperationException stop = await Assert.ThrowsExactlyAsync<OperationException>(
                peers[0].StopAsync
            );
            Assert.Contains("Controlled session disposal failed.", stop.Message);
        }
        else
        {
            await peers[0].StopAsync();
        }
        await peers[1].StopAsync();

        Task completion = factory.CompleteAsync();
        try
        {
            await host.Closing.Task.WaitAsync(token);
            Assert.IsFalse(host.Settled);
            Assert.IsTrue(host.Sessions.All(session => session.CloseAttempted));
            CreditReservation[] staged = ledger.Reservations().ToArray();
            Assert.HasCount(2, staged);
            Assert.IsTrue(staged.All(row => row.State != "reconciled"));
            Assert.IsTrue(staged.All(row => row.Actual == 1m));
            Assert.AreEqual(backing,
                ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
            if (failure == "session")
            {
                Assert.AreEqual("uncertain", staged[0].State);
                Assert.AreEqual("credit_unknown",
                    Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
            }

            host.Release.TrySetResult();
            if (failure == "none")
            {
                await completion;
                Assert.IsTrue(ledger.Reservations().All(row => row.State == "reconciled"));
                Assert.AreEqual(2m,
                    ledger.Status(database.RunId, 300)["campaignCommitted"]!.GetValue<decimal>());
                ledger.RequirePaidAdmission();
                Assert.IsEmpty(factory.SettlementFailures);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<OperationException>(() => completion);
                CreditReservation[] retained = ledger.Reservations().ToArray();
                Assert.AreEqual("uncertain", retained[0].State);
                Assert.AreEqual(failure == "host" ? "uncertain" : "reconciled", retained[1].State);
                Assert.AreEqual("credit_unknown",
                    Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
                JsonNode? evidence = Assert.ContainsSingle(factory.SettlementFailures);
                Assert.IsNotNull(evidence);
                Assert.AreEqual(failure == "host" ? "native-runtime" : "stop",
                    evidence["phase"]!.GetValue<string>());
                if (failure == "session")
                {
                    Assert.AreEqual(retained[0].Id, evidence["reservationId"]!.GetValue<string>());
                    Assert.AreEqual("worker1", evidence["worker"]!.GetValue<string>());
                    Assert.Contains("Controlled session disposal failed.",
                        evidence["message"]!.GetValue<string>());
                }
            }
            Assert.AreEqual(failure != "host", host.Settled);
            using SqliteConnection connection = database.Open();
            Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases;"));
        }
        finally
        {
            host.Release.TrySetResult();
            try
            {
                await completion;
            }
            catch (OperationException)
            {
                // Failed settlement is the observed result, not a cleanup retry.
            }
        }
    }

    // Controlled SDK effects only; production workers, cohort and SQLite accounting remain real.
    private sealed class ControlledSdkHost(
        string failure, CancellationToken token, bool missingMetrics = false,
        RunDatabase? evidenceDatabase = null, bool emptyStatus = false
    ) : INativeSdkHost
    {
        public string Id => "controlled-sdk-host";

        public long LeaseId => 1;

        public List<ControlledSdkSession> Sessions { get; } = [];

        public TaskCompletionSource Closing { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public bool Settled { get; private set; }

        public INativeSdkSession CreateSession(string cwd, bool isolated)
        {
            if (evidenceDatabase is not null && Sessions.Count == 0)
            {
                using Process process = Process.GetCurrentProcess();
                _ = new ProcessRunner(evidenceDatabase).Register(process, $"native:runtime:{Id}");
                RecordState("ready");
            }
            ControlledSdkSession session = new(
                failure == "session" && Sessions.Count == 0, missingMetrics,
                evidenceDatabase, emptyStatus, cwd, Id
            );
            Sessions.Add(session);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            await Release.Task.WaitAsync(token);
            if (failure == "host")
            {
                throw new OperationException("ownership_unresolved", "Controlled host failed.");
            }
            Settled = true;
            if (evidenceDatabase is not null)
            {
                new ProcessRunner(evidenceDatabase).Complete(LeaseId);
                RecordState("settled");
            }
        }

        private void RecordState(string state) => evidenceDatabase!.WriteMetadata(
            $"native:runtime:{Id}", new JsonObject
            {
                ["runtimeId"] = Id,
                ["runtimeLeaseId"] = LeaseId,
                ["runtimeVersion"] = NativeSdkSession.RuntimeVersion,
                ["state"] = state,
            }.ToJsonString());
    }

    private sealed class ControlledSdkSession(
        bool failDispose, bool missingMetrics = false, RunDatabase? evidenceDatabase = null,
        bool emptyStatus = false, string cwd = "", string runtimeId = ""
    ) : INativeSdkSession
    {
        private long requests;

        public string SessionId { get; private set; } = "";

        public bool HasSession { get; private set; }

        public string? NativeFailure => null;

        public JsonObject? Metadata { get; private set; }

        public bool CloseAttempted { get; private set; }

        public Task OpenAsync(
            string worker, string sessionId, decimal? limit, string? exactSession,
            string evidencePath, CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionId = sessionId;
            HasSession = true;
            Metadata = new() { ["sessionId"] = sessionId, ["worker"] = worker };
            if (evidenceDatabase is not null)
            {
                Metadata["workingDirectory"] = cwd;
                Metadata["appliedLimit"] = limit;
                Metadata["selectedModel"] = evidenceDatabase.Configuration.NativeModel
                    ?? "controlled-model";
                Metadata["alreadyInUse"] = false;
                Metadata["isRemote"] = false;
                Metadata["runtimeVersion"] = NativeSdkSession.RuntimeVersion;
                Metadata["sdkVersion"] = NativeSdkSession.SdkVersion;
                Metadata["runtimeId"] = runtimeId;
                Metadata["runtimeLeaseId"] = 1L;
                evidenceDatabase.WriteMetadata(
                    $"native:runtime:{runtimeId}:session:{sessionId}",
                    new JsonObject
                    {
                        ["runtimeId"] = runtimeId,
                        ["runtimeLeaseId"] = 1L,
                        ["sessionId"] = sessionId,
                        ["worker"] = worker,
                        ["workingDirectory"] = cwd,
                    }.ToJsonString());
            }
            return Task.CompletedTask;
        }

        public Task<NativeUsage> ReadAsync(
            string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
            bool validateEvents = true
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NativeUsage(
                SessionId, missingMetrics ? null : requests * 1_000_000_000d,
                missingMetrics || (emptyStatus && requests == 0) ? null : "complete", requests
            ));
        }

        public Task<NativeUsage> UpdateLimitAsync(
            string expectedSession, decimal limit, CancellationToken cancellationToken
        ) => ReadAsync(expectedSession, limit, cancellationToken);

        public Task<JsonObject> SendAsync(
            string text, TimeSpan timeout, CancellationToken cancellationToken,
            Func<CancellationToken, Task<decimal>>? renewLimit = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests++;
            return Task.FromResult(new JsonObject { ["text"] = "Controlled response." });
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Quiesce() { }

        public void RequireHealthy() { }

        public ValueTask DisposeAsync()
        {
            CloseAttempted = true;
            if (failDispose)
            {
                throw new OperationException(
                    "ownership_unresolved", "Controlled session disposal failed."
                );
            }
            HasSession = false;
            return ValueTask.CompletedTask;
        }
    }
}
