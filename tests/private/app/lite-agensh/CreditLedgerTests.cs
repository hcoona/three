using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class CreditLedgerTests(TestContext testContext)
{
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
        ledger.AttachSession(first.Id, "saved-session");
        ledger.ObserveUsage(first.Id, 45);
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

        restarted.Reconcile(first.Id, 45, "first-final-report");
        restarted.ReleaseUnstarted(second.Id);
        CreditReservation fresh = restarted.Reserve(
            "run-a",
            "worker1",
            75,
            120,
            Usage(fixture, "fresh")
        );
        Assert.AreNotEqual(first.Id, fresh.Id);
        Assert.IsNull(fresh.SessionId);
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
        ledger.AttachSession(session.Id, "session-a");
        ledger.ObserveUsage(session.Id, 15);
        ledger.MarkUncertain(session.Id);

        CreditLedger restarted = new(path, 180);
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("different-run", "worker2", 30, 120, Usage(fixture, "blocked"))
        );
        Assert.AreEqual("credit_unknown", error.Code);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            restarted.ReleaseUnstarted(session.Id)
        );
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            restarted.ReleaseFailedStart(session.Id)
        );
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(15m, retained.Actual);
        JsonObject uncertain = restarted.Status("run-a", 120);
        Assert.IsTrue(uncertain["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(60m, Amount(uncertain, "campaignCommitted"));

        restarted.Reconcile(session.Id, 17.5m, "known-final-report");
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
        ledger.AttachSession(session.Id, "session-overshoot");
        ledger.ObserveUsage(session.Id, 47);

        OperationException prompt = Assert.ThrowsExactly<OperationException>(() =>
            ledger.RequirePromptAllowance("run-a", session.Id, 60)
        );
        Assert.AreEqual("credit_exhausted", prompt.Code);
        Assert.AreEqual(17m, Amount(ledger.Status("run-a", 60), "overshoot"));
        Assert.AreEqual(47m, Amount(ledger.Status("run-a", 60), "campaignCommitted"));

        ledger.MarkUncertain(session.Id);
        ledger.Reconcile(session.Id, 49.5m, "overshoot-final-report");
        CreditLedger restarted = new(path, 120);
        restarted.Reconcile(session.Id, 49.5m, "overshoot-final-report");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            restarted.Reconcile(session.Id, 1, "attempted-revision")
        );
        OperationException fresh = Assert.ThrowsExactly<OperationException>(() =>
            restarted.Reserve("run-a", "worker2", 30, 60, Usage(fixture, "cannot-reset"))
        );

        Assert.AreEqual("credit_exhausted", fresh.Code);
        CreditReservation retained = Assert.ContainsSingle(restarted.Reservations("run-a"));
        Assert.AreEqual("reconciled", retained.State);
        Assert.AreEqual(49.5m, retained.Actual);
        Assert.AreEqual("session-overshoot", retained.SessionId);
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
        bool incrementalReport
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
        ledger.AttachSession(original.Id, "exact-saved-session");
        ledger.ObserveUsage(original.Id, 20);
        ledger.Reconcile(original.Id, 20, "original-report");

        CreditLedger restarted = new(path, 180);
        CreditReservation continued = restarted.ContinueExact(
            original.Id,
            Usage(fixture, "loaded")
        );
        Assert.AreEqual(original.Id, continued.Id);
        Assert.AreEqual("exact-saved-session", continued.SessionId);
        Assert.AreEqual(20m, continued.Actual);
        Assert.AreEqual(60m, continued.Allowance);
        Assert.AreNotEqual(original.UsagePath, continued.UsagePath);
        Assert.AreEqual(60m, Amount(restarted.Status("run-a", 120), "runCommitted"));
        restarted.MarkStarted(continued.Id);
        restarted.AttachSession(continued.Id, "exact-saved-session");

        OperationException decreased = Assert.ThrowsExactly<OperationException>(() =>
            restarted.ObserveUsage(continued.Id, 19)
        );
        Assert.AreEqual("credit_unknown", decreased.Code);
        Assert.AreEqual(20m, Assert.ContainsSingle(restarted.Reservations()).Actual);
        restarted.ObserveUsage(continued.Id, 22);
        restarted.RequirePromptAllowance("run-a", continued.Id, 120);
        restarted.Reconcile(
            continued.Id,
            incrementalReport ? 5 : 25,
            "loaded-report",
            processBaseline: incrementalReport ? 20 : null
        );

        CreditReservation result = Assert.ContainsSingle(
            new CreditLedger(path, 180).Reservations()
        );
        Assert.AreEqual("reconciled", result.State);
        Assert.AreEqual(25m, result.Actual);
        Assert.AreEqual("exact-saved-session", result.SessionId);
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
