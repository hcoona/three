using System.Diagnostics;
using System.Text.Json;
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
    public async Task ScriptedPreflightReceiptIsRequiredAndBoundToExactDescriptorBeforeDispatch()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);

        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.IsNull(fixture.Database.ReadMetadata("native:credit_preflight_receipt"));
        Scenario.Data(await coordinator.PreflightAsync(token));
        Assert.IsNotNull(fixture.Database.ReadMetadata("native:credit_preflight_receipt"));
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation preflight = Assert.ContainsSingle(
            ledger.Reservations(fixture.Database.RunId)
        );
        Assert.AreEqual("reconciled", preflight.State);
        Assert.AreEqual(0m, preflight.Actual);
        Assert.IsEmpty(native.OrdinaryPrompts());

        fixture.Database.WriteMetadata(
            "native:credit_contract",
            JsonSerializer.Serialize(
                ScriptedNative.Contract with
                {
                    EvidenceReference = "changed scripted descriptor invalidates the receipt",
                },
                Json.Options
            )
        );
        Scenario.Failure(await coordinator.RunAsync("run", token), "unavailable");
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.HasCount(1, ledger.Reservations(fixture.Database.RunId));
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM coordinator_segments;"));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("unknown-usage")]
    [DataRow("wrong-limit")]
    public async Task ScriptedPreflightUnknownUsageOrAppliedLimitNeverAdmitsOrdinaryPrompt(
        string mode
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, mode, token);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);

        Scenario.Failure(await coordinator.PreflightAsync(token), "credit_unknown");

        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.IsNull(fixture.Database.ReadMetadata("native:credit_preflight_receipt"));
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
    public async Task ScriptedStartupArgumentRejectionRetainsVisibleFailureAndUncertainReservation()
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "reject-startup", token);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);

        JsonObject failure = await coordinator.PreflightAsync(token);

        Scenario.Failure(failure, "unavailable");
        Assert.Contains(
            "uncertain reservations remain retained",
            failure["message"]!.GetValue<string>()
        );
        Assert.AreEqual("unresolved", fixture.Database.ReadMetadata("lifecycle"));
        Assert.IsNull(fixture.Database.ReadMetadata("native:credit_preflight_receipt"));
        Assert.IsEmpty(native.Wire());
        string rejection = Assert.ContainsSingle(
            Directory.GetFiles(Path.Combine(native.DirectoryPath, "bin"), "rejected-*.txt")
        );
        Assert.Contains("--acp together with --usage-output-file", File.ReadAllText(rejection));
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        CreditReservation retained = Assert.ContainsSingle(
            ledger.Reservations(fixture.Database.RunId)
        );
        Assert.AreEqual("uncertain", retained.State);
        Assert.AreEqual(60m, retained.Allowance);
        Assert.IsNull(retained.Actual);
        Assert.AreEqual(
            60m,
            ledger.Status(fixture.Database.RunId, 120)["campaignCommitted"]!.GetValue<decimal>()
        );
        Assert.IsTrue(ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>());
        using SqliteConnection connection = fixture.Database.Open();
        Assert.AreEqual(
            0L,
            Sql.Scalar(connection, "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';")
        );
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM native_sessions;"));

        JsonObject blocked = await coordinator.RunAsync("run", token);
        Scenario.Failure(blocked, "credit_unknown");
        Assert.Contains(
            "no retained version negotiation evidence",
            blocked["message"]!.GetValue<string>()
        );
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
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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

            HashSet<string> before = native.CaptureWire();
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
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
        string? previousSession = fixture.Database.ReadMetadata("worker:worker1:session");
        string? previousReservation = fixture.Database.ReadMetadata("worker:worker1:reservation");
        Assert.IsNotNull(previousSession);
        Assert.IsNotNull(previousReservation);
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

        JsonObject failed = await coordinator.RunAsync("run", token);

        Scenario.Failure(failed, "unavailable");
        Assert.Contains(
            "injected atomic association rejection",
            failed["message"]!.GetValue<string>()
        );
        Assert.IsEmpty(native.OrdinaryPrompts());
        Assert.AreEqual(previousSession, fixture.Database.ReadMetadata("worker:worker1:session"));
        Assert.AreEqual(
            previousReservation,
            fixture.Database.ReadMetadata("worker:worker1:reservation")
        );
        using SqliteConnection retained = fixture.Database.Open();
        Assert.AreEqual(1L, Sql.Scalar(retained, "SELECT COUNT(*) FROM native_sessions;"));
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
        CreditLedger ledger = new(fixture.Database.Configuration.CampaignLedgerPath, 600);
        Assert.IsFalse(ledger.Status(fixture.Database.RunId, 120)["uncertain"]!.GetValue<bool>());
        Assert.AreEqual(
            0m,
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
        await ScriptedNative.BuildAsync(fixture, "complete", token);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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

            HashSet<string> before = native.CaptureWire();
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
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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
    public async Task CoordinatorCrashReconnectValidatesAssociationBeforeLoadOrFreshSummary(
        bool supportsLoad,
        string corruption
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using GitScenario fixture = await GitScenario.CreateAsync(token);
        ScriptedNative native = await ScriptedNative.BuildAsync(fixture, "hold", token);
        Coordinator coordinator = new(fixture.Database, fixture.Collaboration, fixture.Operations);
        Scenario.Data(await coordinator.PreflightAsync(token));
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
        foreach (
            string argument in new[]
            {
                typeof(RunDatabase).Assembly.Location,
                "run",
                "--state",
                fixture.Fixture.StatePath,
            }
        )
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
            child.Kill(entireProcessTree: false);
            await child.WaitForExitAsync(token);
            Assert.AreNotEqual(0, child.ExitCode);
            while (!CoordinatorLockReleased(fixture))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
            }

            native.SetMode(supportsLoad ? "hold" : "no-load");
            HashSet<string> before = native.CaptureWire();
            if (corruption != "none")
            {
                if (corruption == "torn")
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
                Assert.AreEqual("stopped", fixture.Database.ReadMetadata("lifecycle"));
                Assert.IsEmpty(native.Wire(before));
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
                    2L,
                    Sql.Scalar(retainedAssociation, "SELECT COUNT(*) FROM coordinator_segments;")
                );
                Assert.AreEqual(
                    "credit_unknown",
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
                            .Wire()
                            .Any(message =>
                                message["method"]?.GetValue<string>() == "session/load"
                                && message["params"]!["sessionId"]!.GetValue<string>() == original
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
            .Wire()
            .Any(message =>
                message["method"]?.GetValue<string>() == "session/prompt"
                && message["params"]!["sessionId"]!.GetValue<string>() == session
                && message["params"]!["prompt"]![0]!["text"]!.GetValue<string>() != "/usage"
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
