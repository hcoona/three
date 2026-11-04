using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ControlledFailureScenarioTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task NativeFailuresRemainVisibleWhenAnotherWorkerFailureIsSelected()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        native.SetMode("native-error");

        JsonObject failed = await coordinator.RunAsync("run", token);

        Scenario.Failure(failed, "native_stopped");
        string message = failed["message"]!.GetValue<string>();
        foreach (string worker in fixture.Database.Configuration.WorkerIds)
        {
            Assert.Contains(
                fixture.Database.ReadMetadata($"worker:{worker}:session")!,
                message
            );
        }

        Assert.AreEqual(
            message,
            Scenario.Data(coordinator.Status())["failure"]!.GetValue<string>()
        );
        Assert.HasCount(2, native.OrdinaryPrompts());
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        Assert.IsTrue(ledger.Reservations().All(row => row.State == "reconciled"));
        ledger.RequirePaidAdmission();
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task CoordinatorChecksRealCreditGateBeforeEnteringControlledPrompt()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        native.SetMode("uncertain-after-start");

        JsonObject failed = await coordinator.RunAsync("run", token);

        Scenario.Failure(failed, "ownership_or_usage_unresolved");
        Assert.IsEmpty(
            native
                .Operations()
                .Where(item => item["operation"]!.GetValue<string>() == "prompt-enter")
        );
        Assert.IsEmpty(native.OrdinaryPrompts());
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation[] workers = ledger
            .Reservations()
            .Where(item => item.Worker != "calibration")
            .ToArray();
        Assert.HasCount(2, workers);
        Assert.IsTrue(workers.All(item => item.State == "uncertain" && item.Actual is null));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code
        );
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task CompletedTurnsSurviveLateAcknowledgementFailureWithoutReplayingOldBatch()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using (SqliteConnection connection = fixture.Database.Open())
        {
            Sql.Execute(
                connection,
                """
                CREATE TRIGGER reject_ack BEFORE UPDATE OF state ON coordinator_turns
                WHEN NEW.state='acknowledged'
                BEGIN SELECT RAISE(ABORT, 'controlled late acknowledgement failure'); END;
                """
            );
        }

        JsonObject failed = await coordinator.RunAsync("run", token);
        Scenario.Failure(failed, "post_delivery_failed");
        Assert.HasCount(2, native.OrdinaryPrompts());
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(
            2L,
            Sql.Scalar(
                retained,
                "SELECT COUNT(*) FROM coordinator_turns WHERE state='native-complete';"
            )
        );
        Assert.AreEqual(
            0L,
            Sql.Scalar(
                retained,
                """
                SELECT COUNT(*) FROM delivery_batches WHERE id IN
                    (SELECT batch_id FROM coordinator_turns) AND state<>'sent';
                """
            )
        );
        long lastBatch = (long)
            Sql.Scalar(retained, "SELECT MAX(batch_id) FROM coordinator_turns;")!;
        Sql.Execute(retained, "DROP TRIGGER reject_ack;");
        native.SetMode("hold");
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> resumed = coordinator.RunAsync("resume", stop.Token);
        try
        {
            while (native.OrdinaryPrompts().Length < 4)
            {
                if (resumed.IsCompleted)
                {
                    Assert.Fail((await resumed).ToJsonString());
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
            }

            Assert.AreEqual(
                2L,
                Sql.Scalar(
                    retained,
                    """
                    SELECT COUNT(*) FROM coordinator_turns
                    WHERE batch_id<=$last AND state='acknowledged';
                    """,
                    null,
                    ("$last", lastBatch)
                )
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await resumed);
            Assert.HasCount(4, native.OrdinaryPrompts());
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await stop.CancelAsync();
            await resumed.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("unknown-usage")]
    [DataRow("unsettled")]
    public async Task IncompleteCalibrationOrUnsettledStopRetainsReservationWithoutReceipt(
        string mode
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        Scenario.Data(await coordinator.PreflightAsync(token));
        native.SetMode(mode);

        JsonObject failed = await coordinator.CalibrateAsync(token);

        Scenario.Failure(failed, "credit_or_ownership_unknown");
        Assert.AreEqual(
            "controlled-native-dependency",
            failed["nativeEvidenceKind"]!.GetValue<string>()
        );
        Assert.IsNull(fixture.Database.ReadMetadata("native:controlled_billing_receipt"));
        Assert.IsEmpty(native.OrdinaryPrompts());
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation retained = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(30m, retained.Allowance);
        Assert.AreEqual(mode == "unsettled" ? 1m : (decimal?)null, retained.Actual);
        Assert.IsFalse(ledger.HasCompleteSdkUsage(retained.Id));
        Assert.AreEqual(
            "credit_unknown",
            Assert.ThrowsExactly<OperationException>(ledger.RequirePaidAdmission).Code
        );
        Assert.AreEqual("unresolved", fixture.Database.ReadMetadata("lifecycle"));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("failed-turn", false)]
    [DataRow("partial-after-turn", true)]
    public async Task FailedOrCompletedUnknownTurnDoesNotAutomaticallyReplayPaidWork(
        string mode,
        bool completed
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        native.SetMode(mode);

        JsonObject failed = await coordinator.RunAsync("run", token);

        Scenario.Failure(failed, "ownership_or_usage_unresolved");
        JsonObject status = Scenario.Data(coordinator.Status());
        Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
        Assert.AreEqual(
            failed["message"]!.GetValue<string>(),
            status["failure"]!.GetValue<string>()
        );
        if (!completed)
        {
            using SqliteConnection failures = fixture.Database.Open();
            Assert.IsGreaterThan(
                0L,
                (long)Sql.Scalar(
                    failures,
                    """
                    SELECT COUNT(*) FROM delivery_batches
                    WHERE last_error IS NOT NULL
                        AND substr($message,1,length(last_error))=last_error;
                    """,
                    null,
                    ("$message", failed["message"]!.GetValue<string>())
                )!
            );
        }

        JsonArray settlementFailures = status["settlementFailures"]!.AsArray();
        Assert.HasCount(2, settlementFailures);
        Assert.IsTrue(
            settlementFailures.All(item => item!["phase"]!.GetValue<string>() == "stop")
        );
        string[] failedWorkers = settlementFailures
            .Select(item => item!["worker"]!.GetValue<string>())
            .ToArray();
        Assert.Contains("worker1", failedWorkers);
        Assert.Contains("worker2", failedWorkers);
        string[] prompts = native.OrdinaryPrompts();
        Assert.IsNotEmpty(prompts);
        Assert.IsTrue(prompts.Length <= 2, "A failed/unknown paid turn must not be replayed.");
        using SqliteConnection connection = fixture.Database.Open();
        long acknowledged = (long)
            Sql.Scalar(
                connection,
                "SELECT COUNT(*) FROM coordinator_turns WHERE state='acknowledged';"
            )!;
        Assert.AreEqual(completed ? (long)prompts.Length : 0L, acknowledged);
        if (completed)
        {
            Assert.AreEqual(
                0L,
                Sql.Scalar(
                    connection,
                    """
                    SELECT COUNT(*) FROM delivery_batches WHERE id IN
                        (SELECT batch_id FROM coordinator_turns WHERE state='acknowledged')
                        AND state<>'sent';
                    """
                )
            );
        }

        Assert.AreEqual(
            "credit_unknown",
            Assert
                .ThrowsExactly<OperationException>(
                    new CreditLedger(
                        fixture.Database.Configuration.CampaignLedgerPath,
                        600
                    ).RequirePaidAdmission
                )
                .Code
        );
        long before = prompts.Length;
        Scenario.Failure(await coordinator.RunAsync("resume", token), "conflict");
        Scenario.Failure(await coordinator.RunAsync("run", token), "conflict");
        Scenario.Failure(await coordinator.CalibrateAsync(token), "credit_unknown");
        Assert.AreEqual(before, (long)native.OrdinaryPrompts().Length);
        await fixture.AssertOriginalPreservedAsync(token);
    }
}
