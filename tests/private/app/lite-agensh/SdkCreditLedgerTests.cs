using System.ComponentModel;
using System.Globalization;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class SdkCreditLedgerTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task FailedPreSessionExactLoadSettlesAndPreservesCompleteUsageForFreshAdmission()
    {
        using RunFixture fixture = new();
        RunDatabase database = NativeDatabase(fixture);
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation previous = CompleteForContinuation(database, ledger, fixture);
        CreditReservation continuation = ledger.ContinueExact(previous.Id, Evidence(fixture));
        NativeWorker worker = new(database, new(database), ledger, continuation);

        await Assert.ThrowsExactlyAsync<Win32Exception>(() =>
            worker.StartAsync(
                fixture.RepositoryPath,
                continuation.SessionId,
                testContext.CancellationToken
            )
        );
        Assert.IsNull(worker.SessionId);
        Assert.AreEqual("uncertain", Assert.ContainsSingle(ledger.Reservations()).State);
        Assert.AreEqual(60m, Committed(ledger));

        await worker.DisposeAsync();
        await worker.DisposeAsync();

        CreditReservation settled = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("reconciled", settled.State);
        Assert.AreEqual(10m, settled.Actual);
        Assert.AreEqual(previous.SessionId, settled.SessionId);
        Assert.AreEqual(10m, Committed(ledger));
        Assert.IsTrue(ledger.HasCompleteSdkUsage(settled.Id));
        ledger.RequirePaidAdmission();
        CreditReservation fresh = ledger.Reserve(
            database.RunId,
            "worker1",
            60,
            120,
            Evidence(fixture)
        );
        Assert.AreNotEqual(previous.SessionId, fresh.SessionId);
        Assert.AreEqual(70m, Committed(ledger));
        using SqliteConnection connection = database.Open();
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases;"));
        using SqliteConnection campaign = Open(fixture);
        Assert.AreEqual(1L, Sql.Scalar(campaign, "SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow("new")]
    [DataRow("partial")]
    [DataRow("pending-dispatch")]
    [DataRow("changed-after-failure")]
    public async Task PreSessionFailureCannotReleaseNewUnknownOrPendingUsage(string scenario)
    {
        using RunFixture fixture = new();
        RunDatabase database = NativeDatabase(fixture);
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation reservation =
            scenario == "new"
                ? ledger.Reserve(database.RunId, "worker1", 60, 120, Evidence(fixture))
                : ledger.ContinueExact(
                    CompleteForContinuation(database, ledger, fixture).Id,
                    Evidence(fixture)
                );
        if (scenario is "partial" or "pending-dispatch")
        {
            using SqliteConnection campaign = Open(fixture);
            Sql.Execute(
                campaign,
                """
                UPDATE credit_sdk_usage SET status=$status,
                    dispatch_count=dispatch_count+$extra WHERE reservation_id=$id;
                """,
                null,
                ("$status", scenario == "partial" ? "partial" : "pending"),
                ("$extra", scenario == "pending-dispatch" ? 1 : 0),
                ("$id", reservation.Id)
            );
        }

        NativeWorker worker = new(database, new(database), ledger, reservation);
        await Assert.ThrowsExactlyAsync<Win32Exception>(() =>
            worker.StartAsync(
                fixture.RepositoryPath,
                reservation.SessionId,
                testContext.CancellationToken
            )
        );
        if (scenario == "changed-after-failure")
        {
            using SqliteConnection campaign = Open(fixture);
            Sql.Execute(
                campaign,
                """
                UPDATE credit_sdk_usage SET status='pending',dispatch_count=dispatch_count+1
                WHERE reservation_id=$id;
                """,
                null,
                ("$id", reservation.Id)
            );
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            worker.StopAsync()
        );

        Assert.AreEqual("credit_or_ownership_unknown", error.Code);
        CreditReservation retained = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(scenario == "new" ? (decimal?)null : 10m, retained.Actual);
        Assert.AreEqual(60m, Committed(ledger));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code
        );
        using SqliteConnection connection = database.Open();
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases;"));
    }

    [TestMethod]
    public void MinimumWindowRejectsBelowThirtyBeforePersistingAnyAssociation()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ledger.Reserve("run-a", "worker1", 29.999m, 120, Evidence(fixture))
        );
        Assert.IsEmpty(ledger.Reservations());
        using SqliteConnection connection = Open(fixture);
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM credit_sdk_usage;"));
        CreditReservation accepted = ledger.Reserve("run-a", "worker1", 30, 120, Evidence(fixture));
        Assert.AreEqual(30m, Committed(ledger));
        Assert.IsTrue(ledger.HasSdkAssociation(accepted.Id));
    }

    [TestMethod]
    [DataRow(29_999_999_999d, "29.999999999", true)]
    [DataRow(30_000_000_000d, "30", false)]
    [DataRow(30_000_000_001d, "30.000000001", false)]
    public void NextDispatchHonorsExactSessionCapWithoutClampingReportedUsage(
        double nano,
        string expected,
        bool allowed
    )
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = ledger.Reserve("run-a", "worker1", 30, 120, Evidence(fixture));
        ledger.MarkStarted(row.Id);
        ledger.AttachSession(row.Id, row.SessionId!);
        ledger.BeginDispatch("run-a", row.Id, 120);
        ledger.RecordSdkUsage(row.Id, new(row.SessionId!, nano, "complete", 1));
        if (allowed)
        {
            ledger.RequirePromptAllowance("run-a", row.Id, 120);
        }
        else
        {
            Assert.AreEqual(
                "credit_exhausted",
                Assert
                    .ThrowsExactly<OperationException>(() =>
                        ledger.RequirePromptAllowance("run-a", row.Id, 120)
                    )
                    .Code
            );
        }

        decimal expectedCredits = decimal.Parse(expected, CultureInfo.InvariantCulture);
        Assert.AreEqual(expectedCredits, Assert.ContainsSingle(ledger.Reservations()).Actual);
        Assert.AreEqual(Math.Max(30m, expectedCredits), Committed(ledger));
        Assert.AreEqual("active", Assert.ContainsSingle(ledger.Reservations()).State);
    }

    [TestMethod]
    public void PreboundSessionRejectsForeignAttachAndSurvivesRestartBeforeDispatch()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = ledger.Reserve("run-a", "worker1", 60, 120, Evidence(fixture));
        Assert.IsNotNull(row.SessionId);
        Assert.IsTrue(Guid.TryParse(row.SessionId, out _));
        ledger.MarkStarted(row.Id);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ledger.AttachSession(row.Id, Guid.NewGuid().ToString("D"))
        );

        CreditLedger restarted = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations());
        Assert.AreEqual(row.SessionId, retained.SessionId);
        Assert.AreEqual("started", retained.State);
        Assert.IsNull(retained.Actual);
        Assert.IsTrue(restarted.HasSdkAssociation(row.Id));
        restarted.AttachSession(row.Id, row.SessionId);
        Assert.AreEqual("active", Assert.ContainsSingle(restarted.Reservations()).State);
        Assert.AreEqual(60m, Committed(restarted));
    }

    [TestMethod]
    [DataRow(0d, 0L)]
    [DataRow(1_000_000_000d, 1L)]
    public void NoDispatchMeansNoFinalBillingProofEvenWithCompleteMetrics(
        double finalNano,
        long finalRequests
    )
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = Activate(ledger, fixture);
        ledger.RecordSdkUsage(row.Id, new(row.SessionId!, 0, "complete"));
        Assert.IsNull(Assert.ContainsSingle(ledger.Reservations()).Actual);
        Assert.IsFalse(ledger.HasCompleteSdkUsage(row.Id));
        Assert.AreEqual(60m, Committed(ledger));
        Assert.ThrowsExactly<InvalidOperationException>(() => ledger.ReleaseUnstarted(row.Id));

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            ledger.RecordSdkUsage(
                row.Id,
                new(row.SessionId!, finalNano, "complete", finalRequests),
                final: true
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual("uncertain", Assert.ContainsSingle(ledger.Reservations()).State);
        Assert.IsNull(Assert.ContainsSingle(ledger.Reservations()).Actual);
        Assert.AreEqual(60m, Committed(ledger));
        Assert.IsFalse(ledger.HasCompleteSdkUsage(row.Id));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code
        );
        using SqliteConnection connection = Open(fixture);
        Assert.AreEqual(
            0L,
            Sql.Scalar(
                connection,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    public void PaidZeroRequiresDispatchAndReportedRequestsAndFinalizationIsIdempotent()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = Activate(ledger, fixture);
        ledger.BeginDispatch("run-a", row.Id, 120);
        NativeUsage measured = new(row.SessionId!, 0, "complete", 1);
        ledger.RecordSdkUsage(row.Id, measured);
        ledger.RecordSdkUsage(row.Id, measured);
        Assert.AreEqual("active", Assert.ContainsSingle(ledger.Reservations()).State);
        Assert.AreEqual(0m, Assert.ContainsSingle(ledger.Reservations()).Actual);
        Assert.AreEqual(60m, Committed(ledger));
        Assert.IsFalse(ledger.HasCompleteSdkUsage(row.Id));

        ledger.RecordSdkUsage(row.Id, measured, final: true);
        new CreditLedger(fixture.Configuration.CampaignLedgerPath, 180).RecordSdkUsage(
            row.Id,
            measured,
            final: true
        );

        Assert.AreEqual("reconciled", Assert.ContainsSingle(ledger.Reservations()).State);
        Assert.AreEqual(0m, Committed(ledger));
        Assert.IsTrue(ledger.HasCompleteSdkUsage(row.Id));
        using SqliteConnection connection = Open(fixture);
        Assert.AreEqual(1L, Sql.Scalar(connection, "SELECT COUNT(*) FROM credit_reports;"));
        Assert.AreEqual(
            1L,
            Sql.Scalar(
                connection,
                "SELECT dispatch_count FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.AreEqual(
            1L,
            Sql.Scalar(
                connection,
                "SELECT reported_requests FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("partial")]
    [DataRow("nonfinite")]
    [DataRow("negative")]
    [DataRow("regressing")]
    [DataRow("foreign")]
    [DataRow("negative-requests")]
    public void InvalidSdkSnapshotRetainsChargeAndAtomicallyBlocksPaidCampaign(string scenario)
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = Activate(ledger, fixture);
        ledger.BeginDispatch("run-a", row.Id, 120);
        ledger.RecordSdkUsage(row.Id, new(row.SessionId!, 10_000_000_000, "complete", 1));
        NativeUsage invalid = scenario switch
        {
            "missing" => new(row.SessionId!, null, "complete", 1),
            "partial" => new(row.SessionId!, 12_000_000_000, "partial", 1),
            "nonfinite" => new(row.SessionId!, double.NaN, "complete", 1),
            "negative" => new(row.SessionId!, -1, "complete", 1),
            "regressing" => new(row.SessionId!, 9_000_000_000, "complete", 1),
            "foreign" => new("foreign-scripted-session", 90_000_000_000, "complete", 1),
            "negative-requests" => new(row.SessionId!, 12_000_000_000, "complete", -1),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            ledger.RecordSdkUsage(row.Id, invalid)
        );

        Assert.AreEqual("credit_unknown", error.Code);
        CreditLedger restarted = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(row.SessionId, retained.SessionId);
        Assert.AreEqual(10m, retained.Actual);
        Assert.AreEqual(60m, Committed(restarted));
        Assert.IsFalse(restarted.HasCompleteSdkUsage(row.Id));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(restarted.RequirePaidAdmission).Code
        );
        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    restarted.Reserve("other-run", "worker2", 30, 120, Evidence(fixture))
                )
                .Code
        );
        using SqliteConnection connection = Open(fixture);
        double? retainedNano = scenario == "foreign" ? 10_000_000_000 : invalid.TotalNanoAiu;
        Assert.AreEqual(
            retainedNano?.ToString("R", CultureInfo.InvariantCulture),
            Sql.Scalar(
                connection,
                "SELECT reported_nano_aiu FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.AreEqual(
            scenario == "foreign" ? "complete" : invalid.Status,
            Sql.Scalar(
                connection,
                "SELECT status FROM credit_sdk_usage WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            )
        );
        Assert.AreEqual(1L, Sql.Scalar(connection, "SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    public void PendingDispatchPreventsOverlappingCallsButDoesNotBlockAnotherLivePeer()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation first = Activate(ledger, fixture);
        ledger.BeginDispatch("run-a", first.Id, 120);

        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    ledger.BeginDispatch("run-a", first.Id, 120)
                )
                .Code
        );

        CreditReservation second = ledger.Reserve("run-a", "worker2", 60, 120, Evidence(fixture));
        ledger.MarkStarted(second.Id);
        ledger.AttachSession(second.Id, second.SessionId!);
        ledger.BeginDispatch("run-a", second.Id, 120);
        ledger.RecordSdkUsage(second.Id, new(second.SessionId!, 2_000_000_000, "complete", 1));
        Assert.AreEqual(120m, Committed(ledger));
        Assert.AreEqual(2m, ledger.Status("run-a", 120)["actualReported"]!.GetValue<decimal>());
        Assert.IsFalse(ledger.Status("run-a", 120)["uncertain"]!.GetValue<bool>());
        Assert.HasCount(2, ledger.Reservations());
    }

    [TestMethod]
    public void LegacyUnknownRowCannotGainSdkProofOrBeReleasedByEmptyMetrics()
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = Activate(ledger, fixture);
        using (SqliteConnection connection = Open(fixture))
        {
            Sql.Execute(
                connection,
                """
                DELETE FROM credit_sdk_usage WHERE reservation_id=$id;
                DELETE FROM credit_sdk_owners WHERE reservation_id=$id;
                UPDATE credit_reservations SET state='uncertain' WHERE id=$id;
                """,
                null,
                ("$id", row.Id)
            );
        }

        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(() =>
                    ledger.RecordSdkUsage(
                        row.Id,
                        new(row.SessionId!, 0, "complete", 1),
                        final: true
                    )
                )
                .Code
        );

        CreditLedger restarted = new(fixture.Configuration.CampaignLedgerPath, 180);
        Assert.IsFalse(restarted.HasSdkAssociation(row.Id));
        Assert.IsFalse(restarted.HasCompleteSdkUsage(row.Id));
        Assert.IsNull(Assert.ContainsSingle(restarted.Reservations()).Actual);
        Assert.AreEqual("uncertain", Assert.ContainsSingle(restarted.Reservations()).State);
        Assert.AreEqual(60m, Committed(restarted));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(restarted.RequirePaidAdmission).Code
        );
        using SqliteConnection retained = Open(fixture);
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM credit_reports;"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingOrReusedOwnerIdentityRetainsReservationAndBlocksReplacement(bool reusedPid)
    {
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 180);
        CreditReservation row = Activate(ledger, fixture);
        using (SqliteConnection connection = Open(fixture))
        {
            Sql.Execute(
                connection,
                reusedPid
                    ? "UPDATE credit_sdk_owners SET start_ticks=start_ticks+1 "
                        + "WHERE reservation_id=$id;"
                    : "DELETE FROM credit_sdk_owners WHERE reservation_id=$id;",
                null,
                ("$id", row.Id)
            );
        }

        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code
        );

        Assert.IsTrue(ledger.Status("run-a", 120)["uncertain"]!.GetValue<bool>());
        Assert.AreEqual("active", Assert.ContainsSingle(ledger.Reservations()).State);
        Assert.AreEqual(60m, Committed(ledger));
        Assert.IsNull(Assert.ContainsSingle(ledger.Reservations()).Actual);
    }

    private static RunDatabase NativeDatabase(RunFixture fixture)
    {
        RunDatabase database = new(
            fixture.StatePath,
            fixture.Configuration with
            {
                NativeExecutable = Path.Combine(fixture.RootPath, "missing-native-runtime.exe"),
                RunAiCreditAllowance = 120,
                CampaignAiCreditAllowance = 180,
            }
        );
        CollaborationOperations collaboration = new(database);
        _ = new Coordinator(database, collaboration, new(database, collaboration));
        return database;
    }

    private static CreditReservation CompleteForContinuation(
        RunDatabase database,
        CreditLedger ledger,
        RunFixture fixture
    )
    {
        CreditReservation row = ledger.Reserve(
            database.RunId,
            "worker1",
            60,
            120,
            Evidence(fixture)
        );
        ledger.MarkStarted(row.Id);
        ledger.AttachSession(row.Id, row.SessionId!);
        ledger.BeginDispatch(database.RunId, row.Id, 120);
        ledger.RecordSdkUsage(
            row.Id,
            new(row.SessionId!, 10_000_000_000, "complete", 1),
            final: true
        );
        return row;
    }

    private static CreditReservation Activate(CreditLedger ledger, RunFixture fixture)
    {
        CreditReservation row = ledger.Reserve("run-a", "worker1", 60, 120, Evidence(fixture));
        ledger.MarkStarted(row.Id);
        ledger.AttachSession(row.Id, row.SessionId!);
        return row;
    }

    private static string Evidence(RunFixture fixture) =>
        Path.Combine(fixture.RootPath, $"sdk-evidence-{Guid.NewGuid():N}");

    private static decimal Committed(CreditLedger ledger) =>
        ledger.Status("run-a", 120)["campaignCommitted"]!.GetValue<decimal>();

    private static SqliteConnection Open(RunFixture fixture)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = fixture.Configuration.CampaignLedgerPath,
                Pooling = false,
                ForeignKeys = true,
            }.ToString()
        );
        connection.Open();
        return connection;
    }
}
