using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class CreditLedgerTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ExistingVersionOneCampaignMigratesWithoutLosingIdentityOrConfirmedSpend()
    {
        using RunFixture fixture = new();
        string path = fixture.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(path, 180);
        CreditReservation old = ledger.Reserve(
            "old", "worker1", 60, 120, Usage(fixture, "legacy")
        );
        ledger.MarkStarted(old.Id);
        ledger.AttachSession(old.Id, old.SessionId!);
        ledger.BeginDispatch("old", old.Id, 120);
        ledger.RecordSdkUsage(
            old.Id, new(old.SessionId!, 45_000_000_000, "complete", 1), final: true
        );
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            Sql.Execute(connection, """
                ALTER TABLE credit_reservations DROP COLUMN shared;
                ALTER TABLE credit_reservations DROP COLUMN grant_open;
                ALTER TABLE credit_reservations DROP COLUMN overshoot;
                UPDATE credit_campaign SET schema_version=1;
                """);
        }

        CreditLedger migrated = new(path, 180);
        Assert.AreEqual(ledger.Identity, migrated.Identity);
        Assert.AreEqual(45m, Assert.ContainsSingle(migrated.Reservations()).Actual);
        CreditReservation next = migrated.Reserve(
            "new", "worker1", 0, 120, Usage(fixture, "new"), shared: true
        );
        CreditReservation granted = await migrated.AcquireSharedWindowAsync(
            next.Id, testContext.CancellationToken
        );
        Assert.AreEqual(100m, granted.Allowance);
        Assert.AreEqual(145m, Amount(migrated.Status("new", 120), "campaignCommitted"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("partial")]
    [DataRow("regressing-cost")]
    [DataRow("regressing-requests")]
    [DataRow("unbacked-usage")]
    public async Task SharedIncompleteOrRegressingUsageBlocksFurtherPaidAdmission(string failure)
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 300);
        CreditReservation first = ledger.Reserve(
            "shared", "worker1", 0, 300, Usage(fixture, "uncertain-shared"), shared: true
        );
        CreditReservation second = ledger.Reserve(
            "shared", "worker2", 0, 300, Usage(fixture, "blocked-shared"), shared: true
        );
        await ledger.AcquireSharedWindowAsync(first.Id, testContext.CancellationToken);
        ledger.MarkStarted(first.Id);
        ledger.AttachSession(first.Id, first.SessionId!);
        ledger.BeginDispatch("shared", first.Id, 300);
        ledger.RecordSdkUsage(first.Id, new(first.SessionId!, 20_000_000_000, "complete", 2));
        if (failure == "unbacked-usage")
        {
            ledger.CloseSharedWindow(first.Id);
        }
        NativeUsage observation = new(
            first.SessionId!,
            failure == "regressing-cost" ? 19_000_000_000 : 30_000_000_000,
            failure == "partial" ? "pending" : "complete",
            failure == "regressing-requests" ? 1 : 3
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() =>
                ledger.RecordSdkUsage(first.Id, observation)).Code
        );
        ledger = new(fixture.Configuration.CampaignLedgerPath, 300);
        foreach (bool final in new[] { false, true, true })
        {
            Assert.AreEqual(
                "credit_unknown",
                Assert.ThrowsExactly<OperationException>(() =>
                    ledger.RecordSdkUsage(first.Id, observation, final)).Code
            );
            Assert.IsTrue(ledger.Status("shared", 300)["uncertain"]!.GetValue<bool>());
        }
        Assert.AreEqual(
            "credit_unknown",
            (await Assert.ThrowsExactlyAsync<OperationException>(() =>
                ledger.AcquireSharedWindowAsync(second.Id, testContext.CancellationToken))).Code
        );
        Assert.AreEqual(20m, ledger.Reservations().Single(row => row.Id == first.Id).Actual);
        Assert.IsTrue(ledger.Status("shared", 300)["uncertain"]!.GetValue<bool>());
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task SharedRampLetsAnEarlyPeerReuseIdleBackingBeyondItsFormerEqualShare()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 4000);
        CreditReservation[] peers = [.. Enumerable.Range(1, 32).Select(index =>
            ledger.Reserve("shared", $"worker{index}", 0, 2000, Usage(fixture, $"shared-{index}"),
                shared: true))];
        CreditReservation first = await ledger.AcquireSharedWindowAsync(
            peers[0].Id, testContext.CancellationToken
        );
        Assert.AreEqual(100m, Amount(ledger.Status("shared", 2000), "runCommitted"));
        ledger.MarkStarted(first.Id);
        ledger.AttachSession(first.Id, first.SessionId!);
        for (int turn = 1; turn <= 5; turn++)
        {
            CreditReservation grant = await ledger.AcquireSharedWindowAsync(
                first.Id, testContext.CancellationToken
            );
            Assert.AreEqual(100m, grant.Allowance - (grant.Actual ?? 0));
            ledger.BeginDispatch("shared", first.Id, 2000);
            ledger.RecordSdkUsage(
                first.Id, new(first.SessionId!, turn * 45_000_000_000d, "complete", turn)
            );
            ledger.CloseSharedWindow(first.Id);
        }

        JsonObject status = ledger.Status("shared", 2000);
        Assert.AreEqual(225m, Amount(status, "actualReported"));
        Assert.AreEqual(225m, Amount(status, "runCommitted"));
        Assert.AreEqual(0m, Amount(status, "outstandingAuthorization"));
        Assert.AreEqual(31, ledger.Reservations().Count(row =>
            row.State == "reserved" && row.Allowance == 0));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(() =>
                ledger.BeginDispatch("shared", first.Id, 2000)).Code
        );
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharedWaitDoesNotLendInflightAuthorityAndCanBeCanceled(bool cancel)
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 120);
        CreditReservation first = ledger.Reserve(
            "shared", "worker1", 0, 120, Usage(fixture, "held"), shared: true
        );
        CreditReservation second = ledger.Reserve(
            "shared", "worker2", 0, 120, Usage(fixture, "waiting"), shared: true
        );
        await ledger.AcquireSharedWindowAsync(first.Id, testContext.CancellationToken);
        ledger.MarkStarted(first.Id);
        ledger.AttachSession(first.Id, first.SessionId!);
        ledger.BeginDispatch("shared", first.Id, 120);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken
        );
        Task<CreditReservation> waiting = ledger.AcquireSharedWindowAsync(second.Id, stop.Token);
        Assert.IsFalse(waiting.IsCompleted);
        Assert.AreEqual(100m, Amount(ledger.Status("shared", 120), "runCommitted"));
        if (cancel)
        {
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
            Assert.AreEqual(100m, Amount(ledger.Status("shared", 120), "runCommitted"));
        }
        else
        {
            Assert.ThrowsExactly<OperationException>(() => ledger.CloseSharedWindow(first.Id));
            ledger.RecordSdkUsage(first.Id, new(first.SessionId!, 20_000_000_000, "complete", 1));
            Assert.IsFalse(waiting.IsCompleted);
            ledger.CloseSharedWindow(first.Id);
            Assert.AreEqual(100m, (await waiting).Allowance);
            Assert.AreEqual(120m, Amount(ledger.Status("shared", 120), "runCommitted"));
            ledger.MarkStarted(second.Id);
            ledger.AttachSession(second.Id, second.SessionId!);
            ledger.BeginDispatch("shared", second.Id, 120);
            ledger.RecordSdkUsage(second.Id, new(second.SessionId!, 90_000_000_000, "complete", 1));
            ledger.CloseSharedWindow(second.Id);
            Assert.AreEqual(
                "credit_exhausted",
                (await Assert.ThrowsExactlyAsync<OperationException>(() =>
                    ledger.AcquireSharedWindowAsync(first.Id, testContext.CancellationToken))).Code
            );
        }
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task SharedConcurrentPeersCannotOverbookAndWaitWithoutSpending()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 200);
        CreditReservation[] peers = [.. Enumerable.Range(1, 4).Select(index =>
            ledger.Reserve("shared", $"worker{index}", 0, 200, Usage(fixture, $"race-{index}"),
                shared: true))];
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken
        );
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CreditReservation>[] acquisitions = [.. peers.Select(peer => Task.Run(async () =>
        {
            await release.Task.WaitAsync(stop.Token);
            return await ledger.AcquireSharedWindowAsync(peer.Id, stop.Token);
        }, stop.Token))];
        release.SetResult();
        Task<CreditReservation> first = await Task.WhenAny(acquisitions);
        await first;
        Task<CreditReservation> second = await Task.WhenAny(
            acquisitions.Where(task => task != first)
        );
        await second;
        Assert.AreEqual(200m, Amount(ledger.Status("shared", 200), "campaignCommitted"));
        await stop.CancelAsync();
        foreach (Task<CreditReservation> acquisition in acquisitions)
        {
            try
            {
                await acquisition;
            }
            catch (OperationCanceledException) { }
        }
        Assert.AreEqual(2, ledger.Reservations().Count(peer => peer.GrantOpen));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharedWindowRecyclingRetainsOvershootAndRecoverySpendsOnlyTheRemainingPool(
        bool exact
    )
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 300);
        CreditReservation original = ledger.Reserve(
            "shared", "worker1", 0, 300, Usage(fixture, "recycled"), shared: true
        );
        await ledger.AcquireSharedWindowAsync(original.Id, testContext.CancellationToken);
        ledger.MarkStarted(original.Id);
        ledger.AttachSession(original.Id, original.SessionId!);
        for (int turn = 1; turn <= 2; turn++)
        {
            await ledger.AcquireSharedWindowAsync(original.Id, testContext.CancellationToken);
            ledger.BeginDispatch("shared", original.Id, 300);
            ledger.RecordSdkUsage(
                original.Id, new(original.SessionId!, turn * 110_000_000_000d, "complete", turn)
            );
            ledger.CloseSharedWindow(original.Id);
        }
        ledger.RecordSdkUsage(
            original.Id, new(original.SessionId!, 220_000_000_000, "complete", 2), final: true
        );
        CreditReservation next = exact
            ? ledger.ContinueExact(original.Id, Usage(fixture, "exact-next"))
            : ledger.Reserve(
                "shared", "worker1", 0, 300, Usage(fixture, "fresh"), shared: true
            );
        CreditReservation grant = await ledger.AcquireSharedWindowAsync(
            next.Id, testContext.CancellationToken
        );
        Assert.AreEqual(80m, grant.Allowance - (grant.Actual ?? 0));
        Assert.AreEqual(300m, Amount(ledger.Status("shared", 300), "runCommitted"));
        Assert.AreEqual(20m, Amount(ledger.Status("shared", 300), "overshoot"));
        Assert.AreEqual(exact, original.SessionId == grant.SessionId);
    }

    [TestMethod]
    public void ReserveRestartAndFreshSessionsRespectRunAndCampaignAllowances()
    {
        using RunFixture fixture = new();
        string path = fixture.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(path, 180);
        CreditReservation first = ledger.Reserve(
            "run-a",
            "worker1",
            60,
            120,
            Usage(fixture, "first")
        );
        ledger.MarkStarted(first.Id);
        ledger.AttachSession(first.Id, first.SessionId!);
        ledger.BeginDispatch("run-a", first.Id, 120);
        ledger.RecordSdkUsage(first.Id, new(first.SessionId!, 45_000_000_000, "complete", 1));
        CreditReservation second = ledger.Reserve(
            "run-a",
            "worker2",
            60,
            120,
            Usage(fixture, "second")
        );

        CreditLedger restarted = new(path, 180);
        Assert.AreEqual(ledger.Identity, restarted.Identity);
        OperationException runFull = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("run-a", "worker1", 30, 120, Usage(fixture, "blocked"))
        );
        Assert.AreEqual("credit_exhausted", runFull.Code);
        Assert.HasCount(2, restarted.Reservations());
        Assert.AreEqual(120m, Amount(restarted.Status("run-a", 120), "runCommitted"));

        restarted.RecordSdkUsage(
            first.Id,
            new(first.SessionId!, 45_000_000_000, "complete", 1),
            final: true
        );
        restarted.ReleaseUnstarted(second.Id);
        CreditReservation fresh = restarted.Reserve(
            "run-a",
            "worker1",
            75,
            120,
            Usage(fixture, "fresh")
        );
        Assert.AreNotEqual(first.Id, fresh.Id);
        Assert.IsNotNull(fresh.SessionId);
        Assert.AreNotEqual(first.SessionId, fresh.SessionId);
        Assert.IsTrue(restarted.HasSdkAssociation(fresh.Id));
        Assert.AreEqual(120m, Amount(restarted.Status("run-a", 120), "runCommitted"));
        Assert.AreEqual(45m, Amount(restarted.Status("run-a", 120), "actualReported"));

        CreditReservation otherRun = restarted.Reserve(
            "run-b",
            "worker1",
            60,
            60,
            Usage(fixture, "other-run")
        );
        OperationException campaignFull = Assert.ThrowsExactly<OperationException>(() =>
            new CreditLedger(path, 180).Reserve(
                "run-c",
                "worker1",
                30,
                90,
                Usage(fixture, "campaign-blocked")
            )
        );
        Assert.AreEqual("credit_exhausted", campaignFull.Code);
        Assert.AreEqual(180m, Amount(restarted.Status("run-b", 60), "campaignCommitted"));
        Assert.AreEqual(60m, Amount(restarted.Status("run-b", 60), "runCommitted"));
        Assert.AreEqual(otherRun.Id, Assert.ContainsSingle(restarted.Reservations("run-b")).Id);

        OperationException resetRun = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("run-a", "worker2", 30, 300, Usage(fixture, "reset-run"))
        );
        Assert.AreEqual("credit_limit_changed", resetRun.Code);
        Assert.ThrowsExactly<InvalidOperationException>(() => new CreditLedger(path, 181));
        Assert.HasCount(4, new CreditLedger(path, 180).Reservations());
    }

    [TestMethod]
    public void ReserveUncertainUsageBlocksCampaignUntilReconciled()
    {
        using RunFixture fixture = new();
        string path = fixture.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(path, 180);
        CreditReservation session = ledger.Reserve(
            "run-a",
            "worker1",
            60,
            120,
            Usage(fixture, "uncertain")
        );
        ledger.MarkStarted(session.Id);
        ledger.AttachSession(session.Id, session.SessionId!);
        ledger.BeginDispatch("run-a", session.Id, 120);
        ledger.RecordSdkUsage(session.Id, new(session.SessionId!, 15_000_000_000, "complete", 1));
        ledger.MarkUncertain(session.Id);

        CreditLedger restarted = new(path, 180);
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("different-run", "worker2", 30, 120, Usage(fixture, "blocked"))
        );
        Assert.AreEqual("credit_unknown", error.Code);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            restarted.ReleaseUnstarted(session.Id)
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    restarted.BeginDispatch("run-a", session.Id, 120)
                )
                .Code
        );
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(15m, retained.Actual);
        JsonObject uncertain = restarted.Status("run-a", 120);
        Assert.IsTrue(uncertain["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(60m, Amount(uncertain, "campaignCommitted"));

        restarted.RecordSdkUsage(
            session.Id,
            new(session.SessionId!, 17_500_000_000, "complete", 1),
            final: true
        );
        CreditReservation next = restarted.Reserve(
            "run-a",
            "worker2",
            102.5m,
            120,
            Usage(fixture, "after-reconciliation")
        );
        JsonObject reconciled = new CreditLedger(path, 180).Status("run-a", 120);
        Assert.IsFalse(reconciled["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(120m, Amount(reconciled, "runCommitted"));
        Assert.AreEqual(17.5m, Amount(reconciled, "actualReported"));
        Assert.AreEqual("reserved", next.State);
        Assert.HasCount(2, restarted.Reservations("run-a"));
    }

    [TestMethod]
    public void ReconcileOvershootRetainsActualAndBlocksBudgetEvasion()
    {
        using RunFixture fixture = new();
        string path = fixture.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(path, 120);
        CreditReservation session = ledger.Reserve(
            "run-a",
            "worker1",
            30,
            60,
            Usage(fixture, "overshoot")
        );
        ledger.MarkStarted(session.Id);
        ledger.AttachSession(session.Id, session.SessionId!);
        ledger.BeginDispatch("run-a", session.Id, 60);
        ledger.RecordSdkUsage(session.Id, new(session.SessionId!, 47_000_000_000, "complete", 1));

        OperationException prompt = Assert.ThrowsExactly<OperationException>(() =>
            ledger.RequirePromptAllowance("run-a", session.Id, 60)
        );
        Assert.AreEqual("credit_exhausted", prompt.Code);
        Assert.AreEqual(17m, Amount(ledger.Status("run-a", 60), "overshoot"));
        Assert.AreEqual(47m, Amount(ledger.Status("run-a", 60), "campaignCommitted"));

        ledger.MarkUncertain(session.Id);
        ledger.RecordSdkUsage(
            session.Id,
            new(session.SessionId!, 49_500_000_000, "complete", 1),
            final: true
        );
        CreditLedger restarted = new(path, 120);
        restarted.RecordSdkUsage(
            session.Id,
            new(session.SessionId!, 49_500_000_000, "complete", 1),
            final: true
        );
        OperationException fresh = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("run-a", "worker2", 30, 60, Usage(fixture, "cannot-reset"))
        );

        Assert.AreEqual("credit_exhausted", fresh.Code);
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    restarted.RecordSdkUsage(
                        session.Id,
                        new(session.SessionId!, 1_000_000_000, "complete", 1),
                        final: true
                    )
                )
                .Code
        );
        Assert.AreEqual("uncertain", Assert.ContainsSingle(restarted.Reservations()).State);
        Assert.AreEqual(49.5m, Assert.ContainsSingle(restarted.Reservations()).Actual);
        restarted.RecordSdkUsage(
            session.Id,
            new(session.SessionId!, 49_500_000_000, "complete", 1),
            final: true
        );
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations("run-a"));
        Assert.AreEqual("reconciled", retained.State);
        Assert.AreEqual(49.5m, retained.Actual);
        Assert.AreEqual(session.SessionId, retained.SessionId);
        JsonObject status = restarted.Status("run-a", 60);
        Assert.AreEqual(49.5m, Amount(status, "actualReported"));
        Assert.AreEqual(49.5m, Amount(status, "campaignCommitted"));
        Assert.AreEqual(19.5m, Amount(status, "overshoot"));
        Assert.Contains("soft-limit", status["semantics"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ContinueExactReopenedLedgerPreservesSessionAndCumulativeUsage(
        bool intermediateSnapshot
    )
    {
        using RunFixture fixture = new();
        string path = fixture.Configuration.CampaignLedgerPath;
        CreditLedger ledger = new(path, 180);
        CreditReservation original = ledger.Reserve(
            "run-a",
            "worker1",
            60,
            120,
            Usage(fixture, "original")
        );
        ledger.MarkStarted(original.Id);
        ledger.AttachSession(original.Id, original.SessionId!);
        ledger.BeginDispatch("run-a", original.Id, 120);
        ledger.RecordSdkUsage(
            original.Id,
            new(original.SessionId!, 20_000_000_000, "complete", 1),
            final: true
        );

        CreditLedger restarted = new(path, 180);
        CreditReservation continued = restarted.ContinueExact(
            original.Id,
            Usage(fixture, "loaded")
        );
        Assert.AreEqual(original.Id, continued.Id);
        Assert.AreEqual(original.SessionId, continued.SessionId);
        Assert.AreEqual(20m, continued.Actual);
        Assert.AreEqual(60m, continued.Allowance);
        Assert.AreNotEqual(original.UsagePath, continued.UsagePath);
        Assert.AreEqual(60m, Amount(restarted.Status("run-a", 120), "runCommitted"));
        restarted.MarkStarted(continued.Id);
        restarted.AttachSession(continued.Id, continued.SessionId!);
        restarted.BeginDispatch("run-a", continued.Id, 120);
        if (intermediateSnapshot)
        {
            restarted.RecordSdkUsage(
                continued.Id,
                new(continued.SessionId!, 22_000_000_000, "complete", 2)
            );
            restarted.RequirePromptAllowance("run-a", continued.Id, 120);
        }

        restarted.RecordSdkUsage(
            continued.Id,
            new(continued.SessionId!, 25_000_000_000, "complete", 2),
            final: true
        );
        OperationException decreased = Assert.ThrowsExactly<OperationException>(() =>
            restarted.RecordSdkUsage(
                continued.Id,
                new(continued.SessionId!, 19_000_000_000, "complete", 2)
            )
        );
        Assert.AreEqual("credit_unknown", decreased.Code);
        Assert.AreEqual(25m, Assert.ContainsSingle(restarted.Reservations()).Actual);
        restarted.RecordSdkUsage(
            continued.Id,
            new(continued.SessionId!, 25_000_000_000, "complete", 2),
            final: true
        );

        CreditReservation result = Assert.ContainsSingle(
            new CreditLedger(path, 180).Reservations()
        );
        Assert.AreEqual("reconciled", result.State);
        Assert.AreEqual(25m, result.Actual);
        Assert.AreEqual(original.SessionId, result.SessionId);
        Assert.AreEqual(25m, Amount(restarted.Status("run-a", 120), "runCommitted"));
        Assert.AreEqual(0m, Amount(restarted.Status("run-a", 120), "overshoot"));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ReserveConcurrentRequestsCannotOverbookCampaign()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 120);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string>[] attempts = Enumerable
            .Range(1, 3)
            .Select(index =>
                Task.Run(
                    async () =>
                    {
                        await release.Task.WaitAsync(testContext.CancellationToken);
                        try
                        {
                            ledger.Reserve(
                                "run-a",
                                $"worker{index}",
                                60,
                                180,
                                Usage(fixture, $"concurrent-{index}")
                            );
                            return "reserved";
                        }
                        catch (OperationException exception)
                        {
                            return exception.Code;
                        }
                    },
                    testContext.CancellationToken
                )
            )
            .ToArray();

        release.SetResult();
        string[] results = await Task.WhenAll(attempts);

        Assert.AreEqual(2, results.Count(result => result == "reserved"));
        Assert.AreEqual(1, results.Count(result => result == "credit_exhausted"));
        Assert.HasCount(2, ledger.Reservations("run-a"));
        Assert.AreEqual(
            120m,
            Amount(
                new CreditLedger(fixture.Configuration.CampaignLedgerPath, 120).Status(
                    "run-a",
                    180
                ),
                "campaignCommitted"
            )
        );
    }

    private static string Usage(RunFixture fixture, string name) =>
        Path.Combine(fixture.RootPath, $"usage-{name}.json");

    private static decimal Amount(JsonObject status, string name) =>
        status[name]!.GetValue<decimal>();
}
