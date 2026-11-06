using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GitHub.Copilot;
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
    [DataRow("verify")]
    [DataRow("stop")]
    public async Task CompletedTurnRetainsBackingUntilBackgroundCompactionSettles(string operation)
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new("none", token, evidenceDatabase: database);
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        CreditReservation reservation = ledger.Reserve(
            database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "compaction.sdk-evidence"), shared: true
        );
        INativeWorker peer = factory.Create(reservation, calibration: false);
        await peer.StartAsync(fixture.RepositoryPath, null, token);
        await peer.PromptAsync("Controlled request.", token);
        ControlledSdkSession session = Assert.ContainsSingle(host.Sessions);
        session.BeginCompaction();
        Task reading = operation == "verify"
            ? peer.VerifyCreditsAsync(token) : factory.CompleteAsync();
        try
        {
            await session.SettlementReadStarted.Task.WaitAsync(token);
            Assert.IsFalse(reading.IsCompleted);
            Assert.IsFalse(host.Closing.Task.IsCompleted);
            Assert.IsTrue(Assert.ContainsSingle(ledger.Reservations()).GrantOpen);
            session.CompleteCompaction();
            if (operation == "verify")
            {
                await reading;
                CreditReservation closed = Assert.ContainsSingle(ledger.Reservations());
                Assert.AreEqual(3.422970m, closed.Actual);
                Assert.IsFalse(closed.GrantOpen);
                reading = factory.CompleteAsync();
            }
            await host.Closing.Task.WaitAsync(token);
            Assert.AreNotEqual("reconciled", Assert.ContainsSingle(ledger.Reservations()).State);
            host.Release.TrySetResult();
            await reading;
            CreditReservation final = Assert.ContainsSingle(ledger.Reservations());
            Assert.AreEqual("reconciled", final.State);
            Assert.AreEqual(3.422970m, final.Actual);
            ledger.RequirePaidAdmission();
        }
        finally
        {
            session.CompleteCompaction();
            host.Release.TrySetResult();
            try
            {
                await reading;
                await factory.CompleteAsync();
            }
            catch (OperationException)
            {
                // Preserve a failed settlement for the scenario's assertions.
            }
        }
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task InterruptedSendCannotSettleWithUndeliveredCompactionEvents()
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new("none", token, evidenceDatabase: database,
            onClose: session => session.CompleteCompaction(deliver: false));
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        CreditReservation reservation = ledger.Reserve(
            database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "interrupted.sdk-evidence"), shared: true);
        INativeWorker peer = factory.Create(reservation, calibration: false);
        await peer.StartAsync(fixture.RepositoryPath, null, token);
        ControlledSdkSession session = Assert.ContainsSingle(host.Sessions);
        session.OnSend = () =>
        {
            session.BeginCompaction(deliver: false);
            throw new OperationCanceledException(token);
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            peer.PromptAsync("Controlled interrupted request.", token));
        host.Release.TrySetResult();
        await Assert.ThrowsExactlyAsync<OperationException>(() => factory.CompleteAsync());
        Assert.IsTrue(session.CloseAttempted);
        Assert.IsTrue(host.Settled);
        CreditReservation retained = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.IsTrue(retained.GrantOpen);
        Assert.AreEqual("credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("read")]
    [DataRow("session-close")]
    [DataRow("host-close")]
    public async Task WorkChangingDuringCaptureOrFinalStagingCannotBeFinalized(string phase)
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        static void ChangeWork(ControlledSdkSession session)
        {
            session.BeginCompaction();
            session.CompleteCompaction();
        }
        ControlledSdkHost host = new("none", token, evidenceDatabase: database,
            onClose: phase == "session-close" ? ChangeWork : null);
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        CreditReservation reservation = ledger.Reserve(
            database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "race.sdk-evidence"), shared: true);
        INativeWorker peer = factory.Create(reservation, calibration: false);
        await peer.StartAsync(fixture.RepositoryPath, null, token);
        await peer.PromptAsync("Controlled request.", token);
        ControlledSdkSession session = Assert.ContainsSingle(host.Sessions);
        Task completion;
        if (phase == "read")
        {
            session.OnRead = () => ChangeWork(session);
            await Assert.ThrowsExactlyAsync<OperationException>(() =>
                peer.VerifyCreditsAsync(token));
            Assert.AreEqual("uncertain", Assert.ContainsSingle(ledger.Reservations()).State);
            Assert.IsTrue(Assert.ContainsSingle(ledger.Reservations()).GrantOpen);
            session.OnRead = null;
            completion = factory.CompleteAsync();
        }
        else
        {
            completion = factory.CompleteAsync();
        }
        await host.Closing.Task.WaitAsync(token);
        if (phase == "host-close")
        {
            ChangeWork(session);
        }
        host.Release.TrySetResult();
        if (phase == "read")
        {
            await completion;
            Assert.AreEqual(3.422970m, Assert.ContainsSingle(ledger.Reservations()).Actual);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<OperationException>(() => completion);
            Assert.AreEqual("uncertain", Assert.ContainsSingle(ledger.Reservations()).State);
            Assert.AreEqual("credit_unknown",
                Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
        }
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("cancel")]
    [DataRow("failed")]
    [DataRow("unmatched")]
    public async Task MissingOrContradictoryCompactionEvidenceRetainsUncertainty(string failure)
    {
        CancellationToken token = testContext.CancellationToken;
        NativeUsageFence fence = new();
        fence.Observe(CompactionEvent(start: true));
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        bool read = false;
        Task<NativeUsage> capture = fence.CaptureAsync(_ =>
        {
            read = true;
            return Task.FromResult(new NativeUsage("controlled", 1_000_000_000, "complete", 1));
        }, stop.Token);
        Assert.IsFalse(read);
        if (failure == "cancel")
        {
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => capture);
        }
        else
        {
            fence.Observe(CompactionEvent(start: false, success: failure != "failed",
                attempt: failure == "unmatched" ? "another-attempt" : "controlled-compaction"));
            await Assert.ThrowsExactlyAsync<OperationException>(() => capture);
        }
        Assert.IsFalse(read);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(62.422970d)]
    [DataRow(150d)]
    public async Task ExactRecoveryBooksClosedWindowDeltaOnlyAfterTemporaryHostSettles(double total)
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        CreditReservation other = ledger.Reserve(
            "another-run", "worker2", 30, 60, Path.Combine(fixture.RootPath, "other.sdk-evidence"));
        CreditReservation reservation =
            await ClosedReservationAsync(fixture, database, ledger, token);
        ledger.MarkStarted(other.Id);
        ledger.MarkUncertain(other.Id);
        ControlledSdkHost host = new("none", token, onCreate: session =>
            session.Readings.Enqueue(new(
                reservation.SessionId!, total * 1_000_000_000, "complete", 10)));
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        Task<RecoveredNativeUsage> recovery = factory.ReadSettledUsageAsync(
            reservation, fixture.RepositoryPath, "controlled-recovery", token);
        await host.Closing.Task.WaitAsync(token);
        Assert.IsFalse(recovery.IsCompleted);
        Assert.AreEqual(60m, ledger.Reservations().Single(row => row.Id == reservation.Id).Actual);
        host.Release.TrySetResult();
        RecoveredNativeUsage recovered = await recovery;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ledger = new(database.Configuration.CampaignLedgerPath, 600);
            ledger.RecordRecoveredSdkUsage(recovered);
        }
        CreditReservation final = ledger.Reservations().Single(row => row.Id == reservation.Id);
        Assert.AreEqual((decimal)total, final.Actual);
        Assert.AreEqual(Math.Max(0, (decimal)total - 100), final.Overshoot);
        Assert.AreEqual("reconciled", final.State);
        Assert.IsFalse(final.GrantOpen);
        Assert.AreEqual("uncertain", ledger.Reservations().Single(row => row.Id == other.Id).State);
        Assert.AreEqual("credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
        using SqliteConnection campaign = Campaign(database);
        Assert.AreEqual(2L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(1L, Sql.Scalar(campaign,
            "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
            null, ("$id", reservation.Id)));
        Assert.IsFalse(Assert.ContainsSingle(host.Sessions).Sent);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("host")]
    [DataRow("session")]
    [DataRow("late-work")]
    [DataRow("partial")]
    [DataRow("regressing")]
    [DataRow("requests")]
    [DataRow("association")]
    public async Task ExactRecoveryRejectsFailedSettlementOrInvalidFinalUsage(string failure)
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        CreditReservation reservation =
            await ClosedReservationAsync(fixture, database, ledger, token);
        ControlledSdkHost host = new(failure, token,
            onClose: failure == "late-work" ? session =>
            {
                session.BeginCompaction();
                session.CompleteCompaction();
            }
        : null,
            onCreate: session => session.Readings.Enqueue(new(
                reservation.SessionId!, failure == "regressing" ? 59_000_000_000 : 62_000_000_000,
                failure == "partial" ? "partial" : "complete", failure == "requests" ? 9 : 10)));
        host.Release.TrySetResult();
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        Task<RecoveredNativeUsage> recovery = factory.ReadSettledUsageAsync(
            reservation, fixture.RepositoryPath, "controlled-recovery", token);
        if (failure is "host" or "session" or "late-work")
        {
            await Assert.ThrowsExactlyAsync<OperationException>(() => recovery);
        }
        else
        {
            RecoveredNativeUsage recovered = await recovery;
            if (failure == "association")
            {
                recovered = recovered with
                {
                    Reservation = reservation with { Worker = "worker2" },
                };
            }
            Assert.ThrowsExactly<OperationException>(() =>
                ledger.RecordRecoveredSdkUsage(recovered));
            using SqliteConnection campaign = Campaign(database);
            Assert.AreEqual(failure == "partial" ? "partial" : "complete",
                Sql.Scalar(campaign, "SELECT status FROM credit_sdk_usage;"));
        }
        CreditReservation retained = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(60m, retained.Actual);
        Assert.IsFalse(retained.GrantOpen);
        Assert.AreEqual("credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
    }

    private static async Task<CreditReservation> ClosedReservationAsync(
        RunFixture fixture, RunDatabase database, CreditLedger ledger, CancellationToken token
    )
    {
        CreditReservation reservation = ledger.Reserve(database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "closed.sdk-evidence"), shared: true);
        await ledger.AcquireSharedWindowAsync(reservation.Id, token);
        ledger.MarkStarted(reservation.Id);
        ledger.AttachSession(reservation.Id, reservation.SessionId!);
        ledger.BeginDispatch(database.RunId, reservation.Id, 300);
        ledger.RecordSdkUsage(reservation.Id,
            new(reservation.SessionId!, 60_000_000_000, "complete", 10));
        ledger.CloseSharedWindow(reservation.Id);
        ledger.MarkUncertain(reservation.Id);
        return ledger.Reservations().Single(row => row.Id == reservation.Id);
    }

    private static SessionEvent CompactionEvent(
        bool start, bool success = true, string attempt = "controlled-compaction"
    ) => SessionEvent.FromJson(new JsonObject
    {
        ["id"] = Guid.NewGuid().ToString("D"),
        ["timestamp"] = "2026-01-01T00:00:00Z",
        ["type"] = start ? "session.compaction_start" : "session.compaction_complete",
        ["data"] = new JsonObject { ["compactionAttemptId"] = attempt, ["success"] = success },
    }.ToJsonString())!;

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

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("fallback", true)]
    [DataRow("model-case", true)]
    [DataRow("fractional", true)]
    [DataRow("different-model", false)]
    [DataRow("adjacent-cap", false)]
    [DataRow("exact-label", false)]
    public async Task CancellationPreservesAcceptedFreshSessionMetadata(
        string variant, bool accepted
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration with
        {
            NativeExecutable = Path.Combine(fixture.RootPath, "missing-native-runtime.exe"),
            NativeModel = "gpt-6.1-terra",
            RunAiCreditAllowance = 300,
            CampaignAiCreditAllowance = 600,
        });
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        double? cap = variant switch
        {
            "fractional" => 30.123456789012287d,
            "adjacent-cap" => 30.12345678901229d,
            _ => null,
        };
        ControlledSdkHost host = new("none", token,
            evidenceDatabase: database, emptyStatus: true,
            modelReadback: variant switch
            {
                "model-case" => "Gpt-6.1-Terra",
                "different-model" => "another-model",
                _ => null,
            }, limitReadback: cap);
        host.Release.TrySetResult();
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        decimal allowance = cap is null ? 60 : 30.12345678901229m;
        CreditReservation reservation = ledger.Reserve(database.RunId, "worker1", allowance, 300,
            Path.Combine(fixture.RootPath, "metadata.sdk-evidence"));
        INativeWorker peer = factory.Create(reservation, calibration: false);
        string? recovery = variant switch
        {
            "fallback" => "fresh-summary-fallback: exact-session load unavailable or failed",
            "exact-label" => "exact-session-load",
            _ => null,
        };
        await peer.StartAsync(fixture.RepositoryPath, null, token, recovery);
        if (accepted)
        {
            await factory.CompleteAsync();
            Assert.AreEqual("released", ledger.Reservations()[0].State);
            ledger.RequirePaidAdmission();
        }
        else
        {
            await Assert.ThrowsExactlyAsync<OperationException>(() => factory.CompleteAsync());
            Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        }
        Assert.IsNull(ledger.Reservations()[0].Actual);
        using SqliteConnection campaign = Campaign(database);
        Assert.AreEqual(0L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(0L, Sql.Scalar(campaign,
            "SELECT dispatch_count FROM credit_sdk_usage;"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(1d, "complete")]
    [DataRow(double.NaN, "complete")]
    [DataRow(null, "complete")]
    [DataRow(0d, "partial")]
    public async Task LaterZeroCannotErasePreDispatchContradictoryUsage(
        double? nano, string status
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "shared");
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost host = new("none", token,
            evidenceDatabase: database, emptyStatus: true);
        host.Release.TrySetResult();
        NativeWorkerFactory factory = new(database, new(database), ledger, () => host);
        CreditReservation reservation = ledger.Reserve(database.RunId, "worker1", 0, 300,
            Path.Combine(fixture.RootPath, "contradictory.sdk-evidence"), shared: true);
        INativeWorker peer = factory.Create(reservation, calibration: false);
        await peer.StartAsync(fixture.RepositoryPath, null, token);
        host.Sessions[0].Readings.Enqueue(new NativeUsage(peer.SessionId!, nano, status, 0));
        if (nano is double observed && observed != 0)
        {
            await Assert.ThrowsExactlyAsync<OperationException>(() =>
                peer.VerifyCreditsAsync(token));
        }
        else
        {
            await peer.VerifyCreditsAsync(token);
        }
        await Assert.ThrowsExactlyAsync<OperationException>(() => factory.CompleteAsync());
        Assert.AreEqual("uncertain", ledger.Reservations()[0].State);
        Assert.IsNull(ledger.Reservations()[0].Actual);
        using SqliteConnection campaign = Campaign(database);
        Assert.AreEqual("0", Sql.Scalar(campaign,
            "SELECT reported_nano_aiu FROM credit_sdk_usage;"));
        Assert.AreEqual(0L, Sql.Scalar(campaign,
            "SELECT dispatch_count FROM credit_sdk_usage;"));
        Assert.AreEqual(0L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(1L, Sql.Scalar(campaign,
            "SELECT COUNT(*) FROM credit_cancellation_disqualifiers;"));
        Assert.AreEqual("credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task FreshResumedSessionCancellationPreservesEarlierMeasuredTurn()
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = ControlledDatabase(fixture, "per-session");
        LocalTools tools = new(database);
        _ = new Coordinator(database, tools.Collaboration, tools.Git);
        CreditLedger ledger = new(database.Configuration.CampaignLedgerPath, 600);
        ControlledSdkHost firstHost = new("none", token,
            evidenceDatabase: database, emptyStatus: true);
        firstHost.Release.TrySetResult();
        NativeWorkerFactory first = new(database, new(database), ledger, () => firstHost);
        CreditReservation previous = ledger.Reserve(database.RunId, "worker1", 60, 300,
            Path.Combine(fixture.RootPath, "previous.sdk-evidence"));
        INativeWorker original = first.Create(previous, calibration: false);
        await original.StartAsync(fixture.RepositoryPath, null, token);
        await original.PromptAsync("Earlier completed turn.", token);
        await first.CompleteAsync();
        using (SqliteConnection state = database.Open())
        {
            Sql.Execute(state,
                """
                INSERT INTO coordinator_turns(batch_id,worker,state,completed_at)
                VALUES(901,'worker1','acknowledged','earlier');
                """);
        }
        CreditReservation previousAccounting = ledger.Reservations()
            .Single(row => row.Id == previous.Id);
        ControlledSdkHost resumedHost = new("none", token,
            evidenceDatabase: database, emptyStatus: true);
        resumedHost.Release.TrySetResult();
        NativeWorkerFactory resumed = new(database, new(database), ledger, () => resumedHost);
        CreditReservation fresh = ledger.Reserve(database.RunId, "worker1", 60, 300,
            Path.Combine(fixture.RootPath, "resumed.sdk-evidence"));
        INativeWorker replacement = resumed.Create(fresh, calibration: false);
        await replacement.StartAsync(fixture.RepositoryPath, null, token);
        Assert.AreNotEqual(original.SessionId, replacement.SessionId);
        await resumed.CompleteAsync();
        Assert.AreEqual("released", ledger.Reservations().Single(row => row.Id == fresh.Id).State);
        Assert.AreEqual(previousAccounting,
            ledger.Reservations().Single(row => row.Id == previous.Id));
        ledger.RequirePaidAdmission();
        using SqliteConnection retained = database.Open();
        Assert.AreEqual("acknowledged", Sql.Scalar(retained,
            "SELECT state FROM coordinator_turns WHERE batch_id=901;"));
        Assert.AreEqual("earlier", Sql.Scalar(retained,
            "SELECT completed_at FROM coordinator_turns WHERE batch_id=901;"));
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

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("stop")]
    [DataRow("final-usage")]
    [DataRow("native-runtime")]
    public async Task ProductionSettlementRetainsCausesInPrivateCoordinatorDiagnostics(string phase)
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token, configuration =>
            configuration with
            {
                WorkerCount = 1,
                PlanningSeconds = 0,
                RunAiCreditAllowance = 120,
                CampaignAiCreditAllowance = 600,
                NativeExecutable = Path.Combine(
                    configuration.RepositoryPath, "missing-native-runtime.exe"
                ),
            });
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        bool armed = false;
        List<ControlledSdkHost> hosts = [];
        Coordinator coordinator = new(
            fixture.Database, fixture.Collaboration, fixture.Operations,
            (database, processes, credits) => new NativeWorkerFactory(
                database, processes, credits, () =>
                {
                    ControlledSdkHost host = new(
                        armed && phase == "native-runtime" ? "host" : "none",
                        token, evidenceDatabase: database,
                        onClose: session =>
                        {
                            if (!armed || phase != "stop"
                                || session.Metadata?["worker"]?.GetValue<string>()
                                != "worker1")
                            {
                                return;
                            }
                            using SqliteConnection connection = Campaign(database);
                            Sql.Execute(connection,
                                """
                                CREATE TRIGGER reject_stop_usage BEFORE UPDATE ON credit_sdk_usage
                                WHEN NEW.reservation_id IN
                                    (SELECT id FROM credit_reservations WHERE worker='worker1')
                                BEGIN SELECT RAISE(ABORT, 'controlled final usage failure'); END;
                                """);
                        });
                    if (!armed || phase != "final-usage")
                    {
                        host.Release.TrySetResult();
                    }
                    hosts.Add(host);
                    return host;
                }
            )
        );
        Scenario.Data(await coordinator.PreflightAsync(token));
        Scenario.Data(await coordinator.CalibrateAsync(token));
        armed = true;
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            using SqliteConnection connection = fixture.Database.Open();
            while ((long)Sql.Scalar(connection,
                "SELECT COUNT(*) FROM coordinator_turns WHERE state='acknowledged';")! == 0)
            {
                if (execution.IsCompleted)
                {
                    Assert.Fail((await execution).ToJsonString());
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
            }
            coordinator.RequestControl("pause");
            if (phase == "final-usage")
            {
                await hosts[^1].Closing.Task.WaitAsync(token);
                Assert.AreEqual("stopped",
                    fixture.Database.ReadMetadata("worker:worker1:lifecycle"));
                Assert.IsTrue(hosts[^1].Sessions.All(session => session.CloseAttempted));
                using SqliteConnection campaign = Campaign(fixture.Database);
                Sql.Execute(campaign,
                    """
                    CREATE TRIGGER reject_final_usage BEFORE UPDATE ON credit_sdk_usage
                    WHEN NEW.reservation_id IN
                        (SELECT id FROM credit_reservations WHERE worker='worker1')
                    BEGIN SELECT RAISE(ABORT, 'controlled final usage failure'); END;
                    """);
                hosts[^1].Release.TrySetResult();
            }
            JsonObject failed = await execution;
            Scenario.Failure(failed, "ownership_or_usage_unresolved");
            Assert.Contains(phase == "native-runtime"
                ? "Controlled host failed."
                : "SQLite Error 19: 'controlled final usage failure'.",
                failed["message"]!.GetValue<string>());
            CreditReservation peer = Assert.ContainsSingle(ledger.Reservations()
                .Where(row => row.Worker == "worker1"));
            Assert.AreEqual("uncertain", peer.State);
            Assert.AreEqual("credit_unknown",
                Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code);
            Assert.AreEqual(phase != "native-runtime", hosts[^1].Settled);
            string? details = Sql.Scalar(connection,
                "SELECT value FROM meta WHERE key LIKE 'lifecycle:failure_details:%';") as string;
            Assert.IsNotNull(details);
            JsonObject diagnostics = JsonNode.Parse(details)!.AsObject();
            string key = phase == "stop" ? "settlements" : "factorySettlements";
            Assert.IsNotNull(diagnostics[key]);
            JsonObject settlement = Assert.ContainsSingle(
                diagnostics[key]!.AsArray().OfType<JsonObject>());
            Assert.AreEqual(phase, settlement["phase"]!.GetValue<string>());
            JsonObject error = settlement["error"]!.AsObject();
            JsonObject[] publicFailures = Scenario.Data(coordinator.Status())["settlementFailures"]!
                .AsArray().OfType<JsonObject>().ToArray();
            JsonObject publicFailure = Assert.ContainsSingle(publicFailures.Where(item =>
                item["phase"]!.GetValue<string>() == phase));
            if (phase == "native-runtime")
            {
                Assert.IsNull(settlement["worker"]);
                Assert.IsNull(settlement["reservationId"]);
                Assert.IsNull(publicFailure["worker"]);
                Assert.AreEqual("ownership_unresolved", error["operationCode"]!.GetValue<string>());
                Assert.Contains("ControlledSdkHost.DisposeAsync",
                    error["stackTrace"]!.GetValue<string>());
            }
            else
            {
                Assert.AreEqual("worker1", settlement["worker"]!.GetValue<string>());
                Assert.AreEqual(peer.Id, settlement["reservationId"]!.GetValue<string>());
                Assert.AreEqual(peer.Id, publicFailure["reservationId"]!.GetValue<string>());
                JsonObject cause = error;
                if (phase == "stop")
                {
                    Assert.AreEqual("credit_or_ownership_unknown",
                        error["operationCode"]!.GetValue<string>());
                    Assert.IsNotNull(error["innerException"]);
                    cause = error["innerException"]!.AsObject();
                }
                Assert.AreEqual("Microsoft.Data.Sqlite.SqliteException",
                    cause["type"]!.GetValue<string>());
                Assert.AreEqual(19, cause["sqliteErrorCode"]!.GetValue<int>());
                Assert.AreEqual(1811, cause["sqliteExtendedErrorCode"]!.GetValue<int>());
                Assert.Contains("CreditLedger.RecordSdkUsage",
                    cause["stackTrace"]!.GetValue<string>());
            }
            foreach (JsonObject publicResult in new[] { failed, coordinator.Status() })
            {
                Assert.DoesNotContain("\"stackTrace\"", publicResult.ToJsonString());
                Assert.DoesNotContain("\"sqliteExtendedErrorCode\"", publicResult.ToJsonString());
                Assert.DoesNotContain("\"innerException\"", publicResult.ToJsonString());
            }
            Assert.AreEqual("unresolved", fixture.Database.ReadMetadata("lifecycle"));
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            foreach (ControlledSdkHost host in hosts)
            {
                host.Release.TrySetResult();
            }
            await stop.CancelAsync();
            await execution.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }

    // Controlled SDK effects only; production workers, cohort and SQLite accounting remain real.
    private sealed class ControlledSdkHost(
        string failure, CancellationToken token, bool missingMetrics = false,
        RunDatabase? evidenceDatabase = null, bool emptyStatus = false,
        string? modelReadback = null, double? limitReadback = null,
        Action<ControlledSdkSession>? onClose = null,
        Action<ControlledSdkSession>? onCreate = null
    ) : INativeSdkHost
    {
        public string Id { get; } = "controlled-sdk-host-" + Guid.NewGuid().ToString("N");

        public long LeaseId { get; private set; } = 1;

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
                LeaseId = new ProcessRunner(evidenceDatabase)
                    .Register(process, $"native:runtime:{Id}");
                RecordState("ready");
            }
            ControlledSdkSession session = new(
                failure == "session" && Sessions.Count == 0, missingMetrics,
                evidenceDatabase, emptyStatus, cwd, Id, LeaseId, modelReadback, limitReadback,
                onClose
            );
            Sessions.Add(session);
            onCreate?.Invoke(session);
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
        bool emptyStatus = false, string cwd = "", string runtimeId = "", long runtimeLeaseId = 1,
        string? modelReadback = null, double? limitReadback = null,
        Action<ControlledSdkSession>? onClose = null
    ) : INativeSdkSession
    {
        private long requests;
        private readonly NativeUsageFence usageFence = new();
        private bool compacting;
        private double compactionNanoAiu;

        public TaskCompletionSource SettlementReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public void BeginCompaction(bool deliver = true)
        {
            compacting = true;
            if (deliver)
            {
                usageFence.Observe(CompactionEvent(start: true));
            }
        }

        public void CompleteCompaction(bool deliver = true)
        {
            if (compacting)
            {
                compactionNanoAiu = 2_422_970_000;
                compacting = false;
                if (deliver)
                {
                    usageFence.Observe(CompactionEvent(start: false));
                }
            }
        }

        public Action? OnRead { get; set; }

        public Action? OnSend { get; set; }

        public bool Sent { get; private set; }

        public string SessionId { get; private set; } = "";

        public bool HasSession { get; private set; }

        public string? NativeFailure => null;

        public JsonObject? Metadata { get; private set; }

        public bool CloseAttempted { get; private set; }

        public Queue<NativeUsage> Readings { get; } = [];

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
                if (limitReadback is double observed)
                {
                    Metadata["appliedLimit"] = observed;
                }
                Metadata["selectedModel"] = modelReadback
                    ?? evidenceDatabase.Configuration.NativeModel
                    ?? "controlled-model";
                Metadata["alreadyInUse"] = false;
                Metadata["isRemote"] = false;
                Metadata["runtimeVersion"] = NativeSdkSession.RuntimeVersion;
                Metadata["sdkVersion"] = NativeSdkSession.SdkVersion;
                Metadata["runtimeId"] = runtimeId;
                Metadata["runtimeLeaseId"] = runtimeLeaseId;
                evidenceDatabase.WriteMetadata(
                    $"native:runtime:{runtimeId}:session:{sessionId}",
                    new JsonObject
                    {
                        ["runtimeId"] = runtimeId,
                        ["runtimeLeaseId"] = runtimeLeaseId,
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
            if (compacting)
            {
                SettlementReadStarted.TrySetResult();
            }
            OnRead?.Invoke();
            if (Readings.TryDequeue(out NativeUsage? reading))
            {
                return Task.FromResult(reading);
            }
            return Task.FromResult(new NativeUsage(
                SessionId, missingMetrics ? null : requests * 1_000_000_000d + compactionNanoAiu,
                missingMetrics || (emptyStatus && requests == 0) ? null : "complete", requests
            ));
        }

        public Task<NativeUsage> UpdateLimitAsync(
            string expectedSession, decimal limit, CancellationToken cancellationToken
        ) => ReadAsync(expectedSession, limit, cancellationToken);

        public async Task<NativeUsage> ReadSettledAsync(
            string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
            bool validateEvents = true
        )
        {
            SettlementReadStarted.TrySetResult();
            return await usageFence.CaptureAsync(
                token => ReadAsync(expectedSession, expectedLimit, token, validateEvents),
                cancellationToken);
        }

        public void WithSettledUsage(NativeUsage usage, Action record) =>
            usageFence.Use(usage, record);

        public Task<JsonObject> SendAsync(
            string text, TimeSpan timeout, CancellationToken cancellationToken,
            Func<CancellationToken, Task<decimal>>? renewLimit = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent = true;
            requests++;
            usageFence.SetTurnPending(true);
            OnSend?.Invoke();
            usageFence.SetTurnPending(false);
            return Task.FromResult(new JsonObject
            {
                ["text"] = text == "Reply with exactly READY and nothing else."
                    ? "READY" : "Controlled response.",
                ["stopReason"] = "end_turn",
            });
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
            onClose?.Invoke(this);
            return ValueTask.CompletedTask;
        }
    }
}
