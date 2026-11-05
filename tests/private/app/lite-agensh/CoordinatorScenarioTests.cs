using System.Diagnostics;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CoordinatorScenarioTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SharedZeroIntervalDispatchReturnsBackingBeforeTheNextAdmissionCompletes()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with { CreditAllocation = "shared" }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using CancellationTokenSource progress =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        progress.CancelAfter(TimeSpan.FromSeconds(15));
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(
                () => AcknowledgedWorkers(fixture) == 2,
                execution,
                progress.Token
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SharedZeroIntervalPauseCancelsAnAdmissionWaitingOnInflightBacking()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with { CreditAllocation = "shared" }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        HashSet<string> before = native.CaptureOperations();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Length == 1
                    && native.Operations(before).Count(operation =>
                        operation["operation"]?.GetValue<string>() == "start") == 2,
                execution,
                token
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
            Assert.AreEqual(
                "released",
                ledger.Reservations().Single(row => row.Worker == "worker2").State
            );
            Assert.IsTrue(ledger.Reservations().All(row =>
                row.State is "reconciled" or "released"));
            Assert.IsFalse(
                ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>()
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task SharedZeroIntervalInsufficientActualCreditStopsBeforeSessionAssociation()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                CreditAllocation = "shared",
                RunAiCreditAllowance = 30,
            }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        JsonObject result = await coordinator.RunAsync("run", token);
        Assert.AreEqual("credit_exhausted", result["code"]!.GetValue<string>());
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.IsNull(fixture.Database.ReadMetadata("worker:worker1:session"));
        Assert.IsNull(fixture.Database.ReadMetadata("worker:worker2:session"));
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        Assert.IsFalse(ledger.Status(fixture.Database.RunId, 30)["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(2, ledger.Reservations().Count(row => row.State == "released"));
        await fixture.AssertOriginalPreservedAsync(token);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("per-session")]
    [DataRow("shared")]
    public async Task PartialRampReconnectLoadsOnlyAssociatedPeersAndStartsUnadmittedPeersFresh(
        string allocation
    )
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(token, clock: clock);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                WorkerStartIntervalSeconds = 30,
                CreditAllocation = allocation,
            }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 1, execution, token);
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            string first = fixture.Database.ReadMetadata("worker:worker1:session")!;
            Assert.IsNotNull(first);
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker2:session"));
            using (SqliteConnection retained = fixture.Database.Open())
            {
                // Isolate retained prefix recovery; separate crash scenarios cover live ownership.
                Sql.Execute(
                    retained,
                    "UPDATE coordinator_segments SET ended_at=NULL WHERE id="
                        + "(SELECT MAX(id) FROM coordinator_segments);"
                );
            }

            HashSet<string> before = native.CaptureOperations();
            execution = coordinator.RunAsync("reconnect", stop.Token);
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Length >= 1,
                execution,
                token
            );
            clock.Advance(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 2, execution, token);
            string second = fixture.Database.ReadMetadata("worker:worker2:session")!;
            Assert.IsNotNull(second);
            Assert.AreEqual(first, fixture.Database.ReadMetadata("worker:worker1:session"));
            Assert.AreEqual(
                first,
                Assert.ContainsSingle(
                    native.Operations(before)
                        .Where(operation => operation["operation"]?.GetValue<string>() == "resume")
                )["sessionId"]!.GetValue<string>()
            );
            Assert.AreEqual(
                second,
                Assert.ContainsSingle(
                    native.Operations(before)
                        .Where(operation => operation["operation"]?.GetValue<string>() == "start")
                )["sessionId"]!.GetValue<string>()
            );
            Assert.AreEqual(
                "fresh-session-with-durable-summary",
                fixture.Database.ReadMetadata("worker:worker2:recovery")
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
            Assert.HasCount(
                1,
                ledger.Reservations(fixture.Database.RunId)
                    .Where(reservation => reservation.State == "released")
            );
            Assert.IsTrue(
                ledger.Reservations(fixture.Database.RunId)
                    .All(reservation => reservation.State is "released" or "reconciled")
            );
            Assert.IsFalse(
                ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>()
            );
            using SqliteConnection settled = fixture.Database.Open();
            Assert.AreEqual(
                0L,
                Sql.Scalar(settled, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("pause")]
    [DataRow("finalize")]
    [DataRow("worker-failure")]
    public async Task PendingColdAdmissionDoesNotBlockControlOrAdmittedWorkerFailure(string action)
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration => configuration with { WorkerCount = 3 },
            clock
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with { WorkerStartIntervalSeconds = 30 }
        );
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Coordinator coordinator = new(
            fixture.Database,
            fixture.Collaboration,
            fixture.Operations,
            (database, _, credits) => new ControlledNativeFactory(
                database,
                credits,
                native.DirectoryPath,
                onStart: async (reservation, cancellationToken) =>
                {
                    if (reservation.Worker == "worker2")
                    {
                        pending.TrySetResult();
                        await release.Task.WaitAsync(cancellationToken);
                    }
                }
            )
        );
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 1, execution, token);
            Scenario.Call(
                fixture.Collaboration,
                "owner",
                "message_send",
                new { recipient = "worker1", body = "first-admission-ready" }
            );
            await WaitUntilAsync(
                () => native.OrdinaryPrompts().Any(text =>
                    text.Contains("first-admission-ready", StringComparison.Ordinal)
                ),
                execution,
                token
            );
            clock.Advance(TimeSpan.FromSeconds(30));
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (action == "worker-failure")
            {
                native.SetMode("failed-turn");
                Scenario.Call(
                    fixture.Collaboration,
                    "owner",
                    "message_send",
                    new { recipient = "worker1", body = "fail-while-peer-starting" }
                );
            }
            else
            {
                coordinator.RequestControl(action);
            }

            JsonObject result = await execution.WaitAsync(TimeSpan.FromSeconds(10), token);
            Scenario.Failure(result, "ownership_or_usage_unresolved");
            Assert.AreEqual(
                action == "worker-failure" ? "native_stopped" : $"owner-{action}",
                fixture.Database.ReadMetadata("lifecycle:stop_reason")
            );
            Assert.IsFalse(release.Task.IsCompleted);
            using SqliteConnection retained = fixture.Database.Open();
            Assert.AreEqual(
                0L,
                Sql.Scalar(
                    retained,
                    "SELECT COUNT(*) FROM coordinator_turns WHERE worker='worker2';"
                )
            );
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker3:session"));
            CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
            Assert.AreEqual(
                "uncertain",
                Assert.ContainsSingle(
                    ledger.Reservations(fixture.Database.RunId)
                        .Where(reservation => reservation.Worker == "worker2")
                ).State
            );
            Assert.AreEqual(
                "released",
                Assert.ContainsSingle(
                    ledger.Reservations(fixture.Database.RunId)
                        .Where(reservation => reservation.Worker == "worker3")
                ).State
            );
            Assert.IsTrue(
                ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>()
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            release.TrySetResult();
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RampedPeersStayColdWhileAdmittedPeersCooperateAndPlanningReleases(
        bool planning
    )
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration => configuration with { WorkerCount = 3 },
            clock
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                WorkerStartIntervalSeconds = 30,
                PlanningSeconds = planning ? 60 : 0,
            }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        HashSet<string> before = native.CaptureOperations();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 1, execution, token);
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker2:session"));
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker3:session"));
            Scenario.Call(
                fixture.Collaboration,
                "owner",
                "message_send",
                new { recipient = "worker1", body = "cooperation-during-ramp" }
            );
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Any(text =>
                    text.Contains("cooperation-during-ramp", StringComparison.Ordinal)
                ),
                execution,
                token
            );
            clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(350), token);
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker2:session"));

            clock.Advance(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1));
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 2, execution, token);
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker3:session"));
            clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(350), token);
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker3:session"));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 3, execution, token);
            Assert.HasCount(
                3,
                native.Operations(before)
                    .Where(operation => operation["operation"]?.GetValue<string>() == "start")
            );

            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopDuringRampReleasesOnlyUnstartedPeersWithoutCreatingTheirSessions(
        bool deadline
    )
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration => configuration with { WorkerCount = 3 },
            clock
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with { WorkerStartIntervalSeconds = 60 }
        );
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        TaskCompletionSource<int> cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationTokenRegistration registration = default;
        Coordinator coordinator = new(
            fixture.Database,
            fixture.Collaboration,
            fixture.Operations,
            (database, _, credits) => new ControlledNativeFactory(
                database,
                credits,
                native.DirectoryPath,
                onStart: (reservation, cancellationToken) =>
                {
                    if (reservation.Worker == "worker1")
                    {
                        registration = cancellationToken.Register(() =>
                            cancelled.TrySetResult(
                                ledger.Reservations(fixture.Database.RunId)
                                    .Count(item => item.State == "reserved")
                            )
                        );
                    }

                    return Task.CompletedTask;
                }
            )
        );
        await native.PrepareAsync(coordinator, token);
        HashSet<string> before = native.CaptureOperations();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 1, execution, token);
            if (deadline)
            {
                clock.Advance(TimeSpan.FromSeconds(120));
            }
            else
            {
                coordinator.RequestControl("pause");
            }

            Scenario.Data(await execution);
            Assert.AreEqual(2, await cancelled.Task.WaitAsync(token));
            Assert.AreEqual(
                deadline ? "deadline" : "owner-pause",
                fixture.Database.ReadMetadata("lifecycle:stop_reason")
            );
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker2:session"));
            Assert.IsNull(fixture.Database.ReadMetadata("worker:worker3:session"));
            Assert.HasCount(
                1,
                native.Operations(before)
                    .Where(operation => operation["operation"]?.GetValue<string>() == "start")
            );
            using SqliteConnection retained = fixture.Database.Open();
            foreach (string worker in new[] { "worker2", "worker3" })
            {
                CreditReservation pending = Assert.ContainsSingle(
                    ledger.Reservations(fixture.Database.RunId)
                        .Where(reservation => reservation.Worker == worker)
                );
                Assert.AreEqual("released", pending.State);
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        retained,
                        "SELECT COUNT(*) FROM native_sessions WHERE session_id=$session;",
                        null,
                        ("$session", pending.SessionId)
                    )
                );
            }

            Assert.IsFalse(
                ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>()
            );
            Assert.AreEqual(
                0L,
                Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
            registration.Dispose();
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("reconnect")]
    [DataRow("resume")]
    [DataRow("fallback")]
    public async Task ConfirmedSessionContinuesWithEventsAndFreshRecoveryBootstraps(string mode)
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        Scenario.Call(
            fixture.Collaboration,
            "owner",
            "task_write",
            new { action = "create", title = "bootstrap-only-task", body = "Retained work." }
        );
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject>? execution = null;
        try
        {
            execution = coordinator.RunAsync("run", stop.Token);
            await WaitUntilAsync(
                () =>
                {
                    using SqliteConnection connection = fixture.Database.Open();
                    return (long)Sql.Scalar(
                        connection,
                        "SELECT COUNT(DISTINCT worker) FROM coordinator_turns "
                            + "WHERE state='acknowledged';"
                    )! == 2;
                },
                execution,
                token
            );
            string first1 = fixture.Database.ReadMetadata("worker:worker1:session")!;
            string first2 = fixture.Database.ReadMetadata("worker:worker2:session")!;
            Assert.IsTrue(
                native.OrdinaryPrompts().All(text =>
                    text.Contains("bootstrap-only-task", StringComparison.Ordinal)
                )
            );

            HashSet<string> before = native.CaptureOperations();
            Scenario.Call(
                fixture.Collaboration,
                "owner",
                "task_write",
                new { action = "create", title = "continuation-event-task", body = "New work." }
            );
            Scenario.Call(
                fixture.Collaboration,
                "worker2",
                "board_publish",
                new { type = "FACT", content = "continuation-unread-peer-fact" }
            );
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Any(text =>
                    text.Contains("continuation-unread-peer-fact", StringComparison.Ordinal)
                ),
                execution,
                token
            );
            string[] continuation = native.OrdinaryPrompts(before);
            Assert.IsTrue(continuation.Any(text =>
                text.Contains("continuation-event-task", StringComparison.Ordinal)
            ));
            foreach (string text in continuation)
            {
                Assert.DoesNotContain("bootstrap-only-task", text);
                Assert.DoesNotContain("Goal: ", text);
                Assert.DoesNotContain("==== DURABLE CHANGES ====", text);
                JsonObject envelope = JsonNode.Parse(
                    text.Split("==== EVENTS ====\n", 2)[1]
                )!.AsObject();
                Assert.IsNull(envelope["sharedContext"]!["tasks"]);
                Assert.IsNull(envelope["sharedContext"]!["goal"]);
            }
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);

            if (mode != "resume")
            {
                using SqliteConnection connection = fixture.Database.Open();
                Sql.Execute(
                    connection,
                    """
                    UPDATE coordinator_segments
                    SET ended_at=NULL,state='running',deadline=$deadline
                    WHERE id=(SELECT MAX(id) FROM coordinator_segments);
                    """,
                    null,
                    ("$deadline", fixture.Database.Now.AddSeconds(90).ToString("O"))
                );
            }
            native.SetMode(mode == "fallback" ? "no-load" : "hold");
            before = native.CaptureOperations();
            execution = coordinator.RunAsync(mode == "resume" ? "resume" : "reconnect", stop.Token);
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Length >= 2,
                execution,
                token
            );
            foreach ((string worker, string previous) in new[]
            {
                ("worker1", first1), ("worker2", first2),
            })
            {
                string recovered = fixture.Database.ReadMetadata($"worker:{worker}:session")!;
                if (mode == "reconnect")
                {
                    Assert.AreEqual(previous, recovered);
                }
                else
                {
                    Assert.AreNotEqual(previous, recovered);
                }
            }
            foreach (string text in native.OrdinaryPrompts(before))
            {
                if (mode == "reconnect")
                {
                    Assert.DoesNotContain("bootstrap-only-task", text);
                    Assert.DoesNotContain("Goal: ", text);
                }
                else
                {
                    Assert.Contains("bootstrap-only-task", text);
                    Assert.Contains("Goal: ", text);
                }
            }
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            if (execution is not null)
            {
                await StopExecutionAsync(stop, execution);
            }
        }
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledExactLoadSettlementCannotAllocateFreshFallback(bool deadline)
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator initial = native.CreateCoordinator(fixture);
        await native.PrepareAsync(initial, token);
        Task<JsonObject> execution = initial.RunAsync("run", token);
        await WaitUntilAsync(() => native.OrdinaryPrompts().Length >= 2, execution, token);
        initial.RequestControl("pause");
        Scenario.Data(await execution);

        using (SqliteConnection connection = fixture.Database.Open())
        {
            Sql.Execute(
                connection,
                """
                UPDATE coordinator_segments SET ended_at=NULL,state='running',deadline=$deadline
                WHERE id=(SELECT MAX(id) FROM coordinator_segments);
                """,
                null,
                ("$deadline", fixture.Database.Now.AddSeconds(deadline ? 15 : 120).ToString("O"))
            );
        }

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Coordinator recovery = new(
            fixture.Database,
            fixture.Collaboration,
            fixture.Operations,
            (database, _, credits) =>
                new ControlledNativeFactory(
                    database,
                    credits,
                    native.DirectoryPath,
                    async loadToken =>
                    {
                        if (deadline)
                        {
                            try
                            {
                                await Task.Delay(Timeout.InfiniteTimeSpan, loadToken);
                            }
                            catch (OperationCanceledException)
                                when (loadToken.IsCancellationRequested)
                            {
                                // The original load has settled when its run deadline arrives.
                            }
                        }
                        else
                        {
                            await stop.CancelAsync();
                        }
                    }
                )
        );
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        string[] reservations = ledger.Reservations().Select(row => row.Id).ToArray();
        HashSet<string> before = native.CaptureOperations();
        native.SetMode("no-load");

        if (deadline)
        {
            Scenario.Data(await recovery.RunAsync("reconnect", stop.Token));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                recovery.RunAsync("reconnect", stop.Token)
            );
        }

        Assert.AreEqual(
            deadline ? "deadline" : "caller-cancelled",
            fixture.Database.ReadMetadata("lifecycle:stop_reason")
        );
        Assert.IsTrue(
            reservations
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(ledger.Reservations().Select(row => row.Id)),
            "Cancellation must not allocate a replacement reservation."
        );
        Assert.Contains(
            "resume",
            native
                .Operations(before)
                .Select(operation => operation["operation"]!.GetValue<string>())
        );
        Assert.IsTrue(ledger.Reservations().All(row => row.State == "reconciled"));
        Assert.IsEmpty(native.OrdinaryPrompts(before));
        ledger.RequirePaidAdmission();
        AssertSettled(fixture);
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ControlledReceiptsRequirePreflightAndCalibrationAndBindExactConfiguration(
        bool ramp
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);

        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.IsNull(fixture.Database.ReadMetadata("native:controlled_preflight_receipt"));
        Scenario.Data(await coordinator.PreflightAsync(token));
        Assert.IsNotNull(fixture.Database.ReadMetadata("native:controlled_preflight_receipt"));
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        Assert.IsEmpty(ledger.Reservations());
        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Scenario.Data(await coordinator.CalibrateAsync(token));
        CreditReservation calibration = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("reconciled", calibration.State);
        Assert.AreEqual(1m, calibration.Actual);
        Assert.IsTrue(ledger.HasCompleteSdkUsage(calibration.Id));
        Assert.IsEmpty(native.OrdinaryPrompts());
        JsonObject receipt = JsonNode
            .Parse(fixture.Database.ReadMetadata("native:controlled_billing_receipt")!)!
            .AsObject();
        Assert.AreEqual(
            "controlled-native-dependency",
            receipt["nativeEvidenceKind"]!.GetValue<string>()
        );
        Assert.IsNull(fixture.Database.ReadMetadata("native:sdk_preflight_receipt"));
        Assert.IsNull(fixture.Database.ReadMetadata("native:sdk_billing_receipt"));
        Coordinator production = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Failure(await production.RunAsync("run", token), "unavailable");

        fixture.Database.SaveConfiguration(
            ramp
                ? fixture.Database.Configuration with { WorkerStartIntervalSeconds = 30 }
                : fixture.Database.Configuration with
                {
                    NativeExecutable = Path.Combine(
                        native.DirectoryPath,
                        "changed-never-launch.exe"
                    ),
                }
        );
        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.HasCount(1, ledger.Reservations(fixture.Database.RunId));
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM coordinator_segments;"));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("identity-mismatch")]
    [DataRow("wrong-limit")]
    public async Task ControlledDiagnosticFailureNeverAdmitsOrdinaryPrompt(string mode)
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, mode, token);
        Coordinator coordinator = native.CreateCoordinator(fixture);

        Scenario.Failure(await coordinator.PreflightAsync(token), "credit_unknown");

        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.IsNull(fixture.Database.ReadMetadata("native:controlled_preflight_receipt"));
        Assert.IsEmpty(
            new CreditLedger(fixture.Database.Configuration.CampaignLedgerPath, 600).Reservations()
        );
        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Assert.IsEmpty(native.OrdinaryPrompts());
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM coordinator_turns;"));
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task ControlledPaidStartupRejectionRetainsFailureAndUncertainReservation()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        Scenario.Data(await coordinator.PreflightAsync(token));
        native.SetMode("reject-startup");

        JsonObject failure = await coordinator.CalibrateAsync(token);

        Scenario.Failure(failure, "credit_or_ownership_unknown");
        Assert.AreEqual("unresolved", fixture.Database.ReadMetadata("lifecycle"));
        Assert.IsNull(fixture.Database.ReadMetadata("native:controlled_billing_receipt"));
        Assert.IsEmpty(native.OrdinaryPrompts());
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation retained = Assert.ContainsSingle(
            ledger.Reservations(fixture.Database.RunId)
        );
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(30m, retained.Allowance);
        Assert.IsNull(retained.Actual);
        Assert.AreEqual(
            30m,
            ledger.Status(fixture.Database.RunId, 120)["campaignCommitted"]!.GetValue<decimal>()
        );
        Assert.IsTrue(ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>());
        using SqliteConnection connection = fixture.Database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
        Assert.AreEqual(1L, Sql.Scalar(connection, "SELECT COUNT(*) FROM native_sessions;"));

        JsonObject blocked = await coordinator.RunAsync("run", token);
        Scenario.Failure(blocked, "credit_unknown");
        Assert.HasCount(
            1,
            new CreditLedger(fixture.Database.Configuration.CampaignLedgerPath, 600).Reservations(
                fixture.Database.RunId
            )
        );
        Assert.IsEmpty(native.OrdinaryPrompts());
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task PauseSettlesWorkersAndResumeUsesFreshSessionsAndDurableSummary()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        Scenario.Call(
            fixture.Collaboration,
            "owner",
            "task_write",
            new
            {
                action = "create",
                title = "durable-only-project-task",
                body = "Recover this unfinished independent fix.",
            }
        );
        Scenario.Call(
            fixture.Collaboration,
            "worker1",
            "board_publish",
            new { type = "FACT", content = "durable-only-peer-finding" }
        );
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject>? execution = null;
        try
        {
            execution = coordinator.RunAsync("run", stop.Token);
            await WaitUntilAsync(() => native.OrdinaryPrompts().Length >= 2, execution, token);
            foreach (string prompt in native.OrdinaryPrompts())
            {
                string[] sections = prompt.Split("==== EVENTS ====\n", 2);
                string payload = sections[1];
                JsonObject envelope = JsonNode.Parse(payload)!.AsObject();
                JsonArray tasks = envelope["sharedContext"]!["tasks"]!.AsArray();
                Assert.HasCount(
                    1,
                    tasks.Where(task =>
                        task!["title"]!.GetValue<string>() == "durable-only-project-task"
                    )
                );
                Assert.DoesNotContain("durable-only-project-task", sections[0]);
                Assert.DoesNotContain("durable-only-peer-finding", sections[0]);
            }

            string first1 = fixture.Database.ReadMetadata("worker:worker1:session")!;
            string first2 = fixture.Database.ReadMetadata("worker:worker2:session")!;
            JsonObject pending = Scenario.Data(coordinator.RequestControl("pause"));
            Assert.AreEqual("pending", pending["state"]!.GetValue<string>());
            Assert.AreEqual(
                pending["requestId"]!.GetValue<long>(),
                Scenario.Data(coordinator.RequestControl("pause"))["requestId"]!.GetValue<long>()
            );
            JsonObject paused = Scenario.Data(await execution);
            Assert.AreEqual("paused", paused["lifecycle"]!.GetValue<string>());
            Assert.AreEqual("owner-pause", paused["stopReason"]!.GetValue<string>());
            AssertSettled(fixture);

            HashSet<string> before = native.CaptureOperations();
            execution = coordinator.RunAsync("resume", stop.Token);
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Length >= 2,
                execution,
                token
            );
            Assert.AreNotEqual(first1, fixture.Database.ReadMetadata("worker:worker1:session"));
            Assert.AreNotEqual(first2, fixture.Database.ReadMetadata("worker:worker2:session"));
            Assert.Contains(
                "fresh-session-with-durable-summary",
                fixture.Database.ReadMetadata("worker:worker1:recovery")!
            );
            Assert.IsTrue(
                native
                    .OrdinaryPrompts(before)
                    .All(text =>
                        text.Contains("durable-only-project-task", StringComparison.Ordinal)
                    )
            );
            Assert.IsTrue(
                native
                    .OrdinaryPrompts(before)
                    .Any(text =>
                        text.Contains("durable-only-peer-finding", StringComparison.Ordinal)
                    )
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);
            Assert.AreEqual("paused", fixture.Database.ReadMetadata("lifecycle"));
            Assert.IsTrue(File.Exists(Path.Combine(fixture.Fixture.StatePath, "run.sqlite")));
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await stop.CancelAsync();
            if (execution is not null)
            {
                try
                {
                    await execution.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    // The coordinator owns settlement even when setup/assertions fail.
                }
            }
        }
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task InitializeRefusesActiveCoordinatorOwnershipBeforeChangingWorktrees()
    {
        using GitScenario fixture = await GitScenario.CreateAsync(testContext.CancellationToken);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        using FileStream owner = new(
            Path.Combine(fixture.Fixture.StatePath, "coordinator.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );

        Scenario.Failure(
            await coordinator.InitializeAsync(testContext.CancellationToken),
            "conflict"
        );

        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM native_sessions;"));
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM coordinator_segments;"));
        await fixture.AssertOriginalPreservedAsync(testContext.CancellationToken);
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow("integration")]
    [DataRow("planning-deadline")]
    public async Task PlannerReleasesWaitingPeerOnlyAfterExactIntegrationOrPlanningDeadline(
        string release
    )
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(token, clock: clock);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        fixture.Database.SaveConfiguration(
            fixture.Database.Configuration with
            {
                PlanningSeconds = 60,
            }
        );
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 1, execution, token);
            string peer = fixture.Database.ReadMetadata("worker:worker2:session")!;
            Assert.IsFalse(HasOrdinaryPrompt(native, peer));
            if (release == "integration")
            {
                await fixture.CommitAsync(
                    "worker1",
                    "a.txt",
                    "real reviewed planner change",
                    token
                );
                JsonObject change = await fixture.SubmitAsync("worker1", token);
                await fixture.ApproveAsync(change, "worker2", token);
                await fixture.QueueAsync(change, token);
            }
            else
            {
                clock.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(1));
                Assert.IsFalse(HasOrdinaryPrompt(native, peer));
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }

            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 2, execution, token);
            Assert.IsTrue(HasOrdinaryPrompt(native, peer));
            Assert.AreEqual(
                release == "integration" ? "true" : null,
                fixture.Database.ReadMetadata("git:plan_integrated")
            );
            using (SqliteConnection retained = fixture.Database.Open())
            {
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        retained,
                        "SELECT COUNT(*) FROM coordinator_turns WHERE state<>'acknowledged';"
                    )
                );
                Assert.IsTrue(
                    (long)
                        Sql.Scalar(
                            retained,
                            "SELECT COUNT(*) FROM delivery_batches WHERE state='sent';"
                        )! >= 2
                );
            }

            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            AssertSettled(fixture);
            Assert.IsTrue(
                new CreditLedger(fixture.Database.Configuration.CampaignLedgerPath, 600).Status(
                    fixture.Database.RunId,
                    120
                )["actualReported"]!.GetValue<decimal>() >= 2
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task RejectedAssociationWriteRollsBackSessionAndReservationBeforeModelPrompt()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        _ = native.CreateCoordinator(fixture);
        fixture.Database.WriteMetadata("worker:worker1:session", "previous-controlled-session");
        fixture.Database.WriteMetadata(
            "worker:worker1:reservation",
            "previous-controlled-reservation"
        );
        string? previousSession = fixture.Database.ReadMetadata("worker:worker1:session");
        string? previousReservation = fixture.Database.ReadMetadata("worker:worker1:reservation");
        using (SqliteConnection connection = fixture.Database.Open())
        {
            Sql.Execute(
                connection,
                """
                CREATE TRIGGER reject_association BEFORE INSERT ON meta
                WHEN NEW.key='worker:worker1:reservation'
                BEGIN SELECT RAISE(ABORT, 'injected atomic association rejection'); END;
                """
            );
        }

        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation reservation = ledger.Reserve(
            fixture.Database.RunId,
            "worker1",
            60,
            120,
            Path.Combine(fixture.Fixture.StatePath, "before-sdk.evidence")
        );
        NativeWorker worker = new(fixture.Database, new(fixture.Database), ledger, reservation);
        SqliteException failed = await Assert.ThrowsExactlyAsync<SqliteException>(() =>
            worker.StartAsync(fixture.Operations.GetWorkerPath("worker1"), null, token)
        );
        Assert.Contains("injected atomic association rejection", failed.Message);
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.AreEqual(previousSession, fixture.Database.ReadMetadata("worker:worker1:session"));
        Assert.AreEqual(
            previousReservation,
            fixture.Database.ReadMetadata("worker:worker1:reservation")
        );
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM native_sessions;"));
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
        Assert.IsNull(
            Sql.Scalar(
                retained,
                "SELECT session_id FROM native_attempts ORDER BY rowid DESC LIMIT 1;"
            )
        );
        Assert.AreEqual("started", Assert.ContainsSingle(ledger.Reservations()).State);
        await Assert.ThrowsExactlyAsync<OperationException>(() => worker.StopAsync());
        Assert.IsTrue(ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(
            60m,
            ledger.Status(fixture.Database.RunId, 120)["campaignCommitted"]!.GetValue<decimal>()
        );
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NormalCoordinatorPollingDoesNotRepublishUnchangedExactCommitHealthChecks()
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    IntegrationChecks =
                    [
                        new()
                        {
                            Executable = "pwsh",
                            Arguments =
                            [
                                "-NoProfile",
                                "-Command",
                                "'unchanged exact commit is healthy'",
                            ],
                        },
                    ],
                },
            clock
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => AcknowledgedWorkers(fixture) == 2, execution, token);
            for (int round = 0; round < 3; round++)
            {
                long message = Scenario.Id(
                    Scenario.Call(
                        fixture.Collaboration,
                        "owner",
                        "message_send",
                        new { recipient = "worker1", body = $"polling-fence-round-{round}" }
                    )
                );
                await WaitUntilAsync(
                    () =>
                    {
                        using SqliteConnection connection = fixture.Database.Open();
                        return (long)
                                Sql.Scalar(
                                    connection,
                                    """
                                    SELECT COUNT(*) FROM events WHERE kind='dm' AND state='sent'
                                        AND json_extract(payload,'$.messageId')=$message;
                                    """,
                                    null,
                                    ("$message", message)
                                )! == 1;
                    },
                    execution,
                    token
                );
            }

            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            using SqliteConnection retained = fixture.Database.Open();
            Assert.AreEqual(
                1L,
                Sql.Scalar(
                    retained,
                    """
                    SELECT COUNT(*) FROM checks WHERE kind='integration' AND commit_hash=$commit;
                    """,
                    null,
                    ("$commit", fixture.Database.Configuration.BaseRevision)
                )
            );
            Assert.AreEqual(
                "passed",
                Sql.Scalar(retained, "SELECT status FROM checks WHERE kind='integration';")
            );
            AssertSettled(fixture);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task FinalizeResumeIntegrateAndPauseRequiresNewFinalizationBeforeOwnedCleanup()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    FinalChecks = [new() { Executable = "git", Arguments = ["diff", "--check"] }],
                }
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        await fixture.CommitAsync("worker1", "a.txt", "finalization-A-fix", token);
        JsonObject first = await fixture.SubmitAsync("worker1", token);
        await fixture.ApproveAsync(first, "worker2", token);
        await fixture.QueueAsync(first, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(
                () =>
                    IsIntegrated(fixture, Scenario.Id(first))
                    && native.OrdinaryPrompts().Length >= 2,
                execution,
                token
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);
            JsonObject finalizedA = Scenario.Data(await coordinator.FinalizeAsync(token));
            Assert.IsTrue(finalizedA["accepted"]!.GetValue<bool>());
            string retainedA = finalizedA["finalCommit"]!.GetValue<string>();

            HashSet<string> before = native.CaptureOperations();
            execution = coordinator.RunAsync("resume", stop.Token);
            await WaitUntilAsync(
                () => native.OrdinaryPrompts(before).Length >= 2,
                execution,
                token
            );
            await fixture.CommitAsync("worker2", "b.txt", "resumed-B-fix", token);
            JsonObject second = await fixture.SubmitAsync("worker2", token);
            await fixture.ApproveAsync(second, "worker1", token);
            await fixture.QueueAsync(second, token);
            await WaitUntilAsync(
                () => IsIntegrated(fixture, Scenario.Id(second)),
                execution,
                token
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await execution);

            Scenario.Failure(await coordinator.CleanupAsync(token), "conflict");

            Assert.IsTrue(Directory.Exists(fixture.Operations.GetWorkerPath("worker1")));
            Assert.IsTrue(Directory.Exists(fixture.Operations.GetWorkerPath("worker2")));
            Assert.AreEqual(
                retainedA,
                (
                    await fixture.GitAsync(
                        fixture.Fixture.RepositoryPath,
                        token,
                        "rev-parse",
                        finalizedA["deliveryRef"]!.GetValue<string>()
                    )
                ).Trim()
            );
            JsonObject finalizedB = Scenario.Data(await coordinator.FinalizeAsync(token));
            Assert.IsTrue(finalizedB["accepted"]!.GetValue<bool>());
            Assert.AreNotEqual(retainedA, finalizedB["finalCommit"]!.GetValue<string>());
            Assert.AreEqual(
                "finalization-A-fix",
                (
                    await fixture.GitAsync(
                        fixture.Fixture.RepositoryPath,
                        token,
                        "show",
                        finalizedB["finalCommit"]!.GetValue<string>() + ":a.txt"
                    )
                ).Trim()
            );
            Assert.AreEqual(
                "resumed-B-fix",
                (
                    await fixture.GitAsync(
                        fixture.Fixture.RepositoryPath,
                        token,
                        "show",
                        finalizedB["finalCommit"]!.GetValue<string>() + ":b.txt"
                    )
                ).Trim()
            );
            Scenario.Data(await coordinator.CleanupAsync(token));
            Assert.IsFalse(Directory.Exists(fixture.Operations.GetWorkerPath("worker1")));
            Assert.IsTrue(File.Exists(Path.Combine(fixture.Fixture.StatePath, "run.sqlite")));
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    private static bool IsIntegrated(GitScenario fixture, long change)
    {
        using SqliteConnection connection = fixture.Database.Open();
        return (string?)
                Sql.Scalar(
                    connection,
                    "SELECT state FROM changes WHERE id=$id;",
                    null,
                    ("$id", change)
                ) == "integrated";
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow("owner-root-closure")]
    [DataRow("deadline")]
    [DataRow("worker-root-closure")]
    public async Task RootActorAndDeadlineStopPolicyCannotFabricateFinalAcceptance(string trigger)
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using GitScenario fixture = await GitScenario.CreateAsync(
            token,
            configuration =>
                configuration with
                {
                    FinalChecks =
                    [
                        new()
                        {
                            Executable = "pwsh",
                            Arguments =
                            [
                                "-NoProfile",
                                "-Command",
                                "'scripted failed acceptance must remain failed'; exit 11",
                            ],
                        },
                    ],
                },
            clock
        );
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject> execution = coordinator.RunAsync("run", stop.Token);
        try
        {
            await WaitUntilAsync(() => native.OrdinaryPrompts().Length >= 2, execution, token);
            if (trigger == "deadline")
            {
                clock.Advance(TimeSpan.FromSeconds(120));
            }
            else
            {
                Scenario.Call(
                    fixture.Collaboration,
                    trigger == "owner-root-closure" ? "owner" : "worker1",
                    "task_write",
                    new { action = "close", id = 1 }
                );
            }

            if (trigger == "worker-root-closure")
            {
                await WaitUntilAsync(
                    () =>
                        Scenario.Call(fixture.Collaboration, "owner", "task_read", new { id = 1 })[
                            "state"
                        ]!.GetValue<string>() == "open",
                    execution,
                    token
                );
                Assert.IsFalse(execution.IsCompleted);
                Assert.IsNull(fixture.Database.ReadMetadata("git:delivery_report"));
                coordinator.RequestControl("pause");
                Assert.AreEqual(
                    "owner-pause",
                    Scenario.Data(await execution)["stopReason"]!.GetValue<string>()
                );
            }
            else
            {
                JsonObject failed = await execution;
                Scenario.Failure(failed, "check_failed");
                Assert.IsFalse(failed["data"]!["accepted"]!.GetValue<bool>());
                Assert.AreEqual(trigger, fixture.Database.ReadMetadata("lifecycle:stop_reason"));
                Assert.AreEqual("finalized", fixture.Database.ReadMetadata("lifecycle"));
                JsonObject check = Assert.ContainsSingle(
                    failed["data"]!["checks"]!
                        .AsArray()
                        .OfType<JsonObject>()
                        .Where(value => value["kind"]!.GetValue<string>() == "final")
                );
                Assert.AreEqual("failed", check["status"]!.GetValue<string>());
                Assert.AreEqual(11, check["exitCode"]!.GetValue<int>());
                Assert.Contains(
                    "scripted failed acceptance",
                    File.ReadAllText(check["logPath"]!.GetValue<string>())
                );
            }

            AssertSettled(fixture);
            Assert.IsTrue(Directory.Exists(fixture.Operations.GetWorkerPath("worker1")));
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            await StopExecutionAsync(stop, execution);
        }
    }

    private static long AcknowledgedWorkers(GitScenario fixture)
    {
        using SqliteConnection connection = fixture.Database.Open();
        return (long)
            Sql.Scalar(
                connection,
                "SELECT COUNT(DISTINCT worker) FROM coordinator_turns WHERE state='acknowledged';"
            )!;
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow(true, "none")]
    [DataRow(false, "none")]
    [DataRow(true, "torn")]
    [DataRow(true, "cross-worker")]
    [DataRow(true, "session-mismatched")]
    [DataRow(true, "cross-run")]
    [DataRow(true, "unsettled-recovery")]
    public async Task CoordinatorCrashReconnectValidatesAssociationBeforeLoadOrFreshSummary(
        bool supportsLoad,
        string corruption
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator coordinator = native.CreateCoordinator(fixture);
        await native.PrepareAsync(coordinator, token);
        string host = await native.BuildCrashHostAsync(fixture, token);
        Scenario.Call(
            fixture.Collaboration,
            "owner",
            "task_write",
            new
            {
                action = "create",
                title = "crash-durable-unfinished-work",
                body = "Recover without losing this task.",
            }
        );
        ProcessStartInfo start = new(Scenario.DotnetHost)
        {
            WorkingDirectory = fixture.Fixture.RepositoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { host, fixture.Fixture.StatePath, native.DirectoryPath })
        {
            start.ArgumentList.Add(argument);
        }

        using Process child = new() { StartInfo = start };
        Assert.IsTrue(child.Start());
        Task<string> output = child.StandardOutput.ReadToEndAsync(token);
        Task<string> error = child.StandardError.ReadToEndAsync(token);
        Task<JsonObject> exited = ObserveExitAsync(child, output, error, token);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonObject>? recovery = null;
        try
        {
            await WaitUntilAsync(() => native.OrdinaryPrompts().Length >= 2, exited, token);
            string first1 = fixture.Database.ReadMetadata("worker:worker1:session")!;
            string first2 = fixture.Database.ReadMetadata("worker:worker2:session")!;
            Assert.IsNotNull(first1);
            Assert.IsNotNull(first2);
            CreditReservation? foreign =
                corruption == "cross-run"
                    ? new CreditLedger(
                        fixture.Database.Configuration.CampaignLedgerPath,
                        600
                    ).Reserve(
                        "foreign-run",
                        "worker1",
                        30,
                        120,
                        Path.Combine(fixture.Fixture.StatePath, "foreign-run.evidence")
                    )
                    : null;
            child.Kill(entireProcessTree: false);
            await child.WaitForExitAsync(token);
            Assert.AreNotEqual(0, child.ExitCode);
            while (!CoordinatorLockReleased(fixture))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
            }

            native.SetMode(supportsLoad ? "hold" : "no-load");
            HashSet<string> before = native.CaptureOperations();
            if (corruption == "unsettled-recovery")
            {
                native.SetMode("unsettled-recovery");
                recovery = coordinator.RunAsync("reconnect", stop.Token);
                Scenario.Failure(await recovery, "ownership_unresolved");
                Assert.IsEmpty(native.OrdinaryPrompts(before));
                Assert.HasCount(
                    1,
                    native
                        .Operations(before)
                        .Where(item => item["operation"]!.GetValue<string>() == "recover")
                );
                Assert.IsFalse(
                    native
                        .Operations(before)
                        .Any(item => item["operation"]!.GetValue<string>() == "recovered-settled")
                );
                CreditLedger uncertain = new(
                    fixture.Database.Configuration.CampaignLedgerPath,
                    600
                );
                CreditReservation[] workers = uncertain
                    .Reservations(fixture.Database.RunId)
                    .Where(item => item.Worker != "calibration")
                    .ToArray();
                Assert.HasCount(2, workers);
                Assert.IsTrue(workers.All(item => item.Actual is null));
                Assert.IsTrue(workers.Any(item => item.State == "uncertain"));
                Assert.AreEqual(
                    120m,
                    uncertain.Status(fixture.Database.RunId, 120)[
                        "campaignCommitted"
                    ]!.GetValue<decimal>()
                );
                Assert.AreEqual("unresolved", fixture.Database.ReadMetadata("lifecycle"));
                return;
            }

            if (corruption != "none")
            {
                // Association rejection precedes coordinator survivor settlement.
                // Settle this owned crash fixture's Git children before isolating that guard.
                await new ProcessRunner(fixture.Database).ResolveSurvivorsAsync(token);
                if (foreign is not null)
                {
                    fixture.Database.WriteMetadata("worker:worker1:session", foreign.SessionId!);
                    fixture.Database.WriteMetadata("worker:worker1:reservation", foreign.Id);
                }
                else if (corruption == "torn")
                {
                    fixture.Database.WriteMetadata("worker:worker1:reservation", "");
                }
                else
                {
                    fixture.Database.WriteMetadata("worker:worker1:session", first2);
                    if (corruption == "cross-worker")
                    {
                        fixture.Database.WriteMetadata(
                            "worker:worker1:reservation",
                            fixture.Database.ReadMetadata("worker:worker2:reservation")!
                        );
                    }
                }

                CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
                string[] reservations = ledger
                    .Reservations(fixture.Database.RunId)
                    .Select(row => row.Id)
                    .ToArray();
                recovery = coordinator.RunAsync("reconnect", stop.Token);
                JsonObject refused = await recovery;
                Scenario.Failure(refused, "credit_unknown");
                Assert.Contains(
                    "saved native session/reservation association",
                    refused["message"]!.GetValue<string>()
                );
                Assert.AreEqual("running", fixture.Database.ReadMetadata("lifecycle"));
                Assert.IsEmpty(native.Operations(before));
                Assert.IsTrue(
                    reservations
                        .ToHashSet(StringComparer.Ordinal)
                        .SetEquals(
                            ledger.Reservations(fixture.Database.RunId).Select(row => row.Id)
                        )
                );
                using SqliteConnection retainedAssociation = fixture.Database.Open();
                Assert.AreEqual(
                    0L,
                    Sql.Scalar(
                        retainedAssociation,
                        "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';"
                    )
                );
                Assert.AreEqual(
                    1L,
                    Sql.Scalar(retainedAssociation, "SELECT COUNT(*) FROM coordinator_segments;")
                );
                Assert.IsNull(
                    Sql.Scalar(
                        retainedAssociation,
                        "SELECT stop_reason FROM coordinator_segments ORDER BY id DESC LIMIT 1;"
                    )
                );
                return;
            }

            recovery = coordinator.RunAsync("reconnect", stop.Token);
            await WaitUntilAsync(() => native.OrdinaryPrompts(before).Length >= 2, recovery, token);
            foreach (
                (string worker, string original) in new[]
                {
                    ("worker1", first1),
                    ("worker2", first2),
                }
            )
            {
                string recovered = fixture.Database.ReadMetadata($"worker:{worker}:session")!;
                string label = fixture.Database.ReadMetadata($"worker:{worker}:recovery")!;
                if (supportsLoad)
                {
                    Assert.AreEqual(original, recovered);
                    Assert.AreEqual("exact-session-load", label);
                    Assert.IsTrue(
                        native
                            .Operations(before)
                            .Any(message =>
                                message["operation"]?.GetValue<string>() == "resume"
                                && message["sessionId"]!.GetValue<string>() == original
                            )
                    );
                }
                else
                {
                    Assert.AreNotEqual(original, recovered);
                    Assert.StartsWith("fresh-summary-fallback:", label);
                }
            }

            Assert.IsTrue(
                native
                    .OrdinaryPrompts(before)
                    .All(text =>
                        text.Contains("crash-durable-unfinished-work", StringComparison.Ordinal)
                    )
            );
            coordinator.RequestControl("pause");
            Scenario.Data(await recovery);
            AssertSettled(fixture);
            using SqliteConnection retained = fixture.Database.Open();
            Assert.AreEqual(
                "crash-reconnect",
                Sql.Scalar(
                    retained,
                    "SELECT stop_reason FROM coordinator_segments ORDER BY id LIMIT 1;"
                )
            );
            Assert.AreEqual(
                "reconnect",
                Sql.Scalar(
                    retained,
                    "SELECT mode FROM coordinator_segments ORDER BY id DESC LIMIT 1;"
                )
            );
            await fixture.AssertOriginalPreservedAsync(token);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: false);
                await child.WaitForExitAsync(CancellationToken.None);
            }

            if (recovery is not null)
            {
                await StopExecutionAsync(stop, recovery);
            }

            await new ProcessRunner(fixture.Database).ResolveSurvivorsAsync(CancellationToken.None);
            await exited;
        }
    }

    private static async Task<JsonObject> ObserveExitAsync(
        Process child,
        Task<string> output,
        Task<string> error,
        CancellationToken token
    )
    {
        await child.WaitForExitAsync(token);
        return new()
        {
            ["exitCode"] = child.ExitCode,
            ["stdout"] = await output,
            ["stderr"] = await error,
        };
    }

    private static bool CoordinatorLockReleased(GitScenario fixture)
    {
        try
        {
            using FileStream released = new(
                Path.Combine(fixture.Fixture.StatePath, "coordinator.lock"),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasOrdinaryPrompt(ScriptedNative native, string session) =>
        native
            .Operations()
            .Any(message =>
                message["operation"]?.GetValue<string>() == "prompt"
                && message["sessionId"]!.GetValue<string>() == session
            );

    private static async Task StopExecutionAsync(
        CancellationTokenSource stop,
        Task<JsonObject> execution
    )
    {
        await stop.CancelAsync();
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Assert paths above validate the normal result.
            // This is failure-path fixture settlement.
        }
    }

    private static void AssertSettled(GitScenario fixture)
    {
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        Assert.IsTrue(
            ledger.Reservations(fixture.Database.RunId).All(row => row.State == "reconciled")
        );
        Assert.IsFalse(ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>());
    }

    private static async Task WaitUntilAsync(
        Func<bool> ready,
        Task<JsonObject> execution,
        CancellationToken token
    )
    {
        while (!ready())
        {
            if (execution.IsCompleted)
            {
                Assert.Fail(
                    "Coordinator stopped before the scripted observation: "
                        + (await execution).ToJsonString()
                );
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), token);
        }
    }
}
