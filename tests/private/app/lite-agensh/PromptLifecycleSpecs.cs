using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class PromptLifecycleSpecs(TestContext testContext)
{
    [TestMethod]
    [TestCategory("ConfirmationRecoveryAndAck")]
    [TestCategory("ExactFreshAndResumeLabels")]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task GenuineInterruptedExactSessionRetainsUsageAndConfirmedOrLegacyBootstrap(
        bool legacyMarkerAbsent
    )
    {
        CancellationToken token = testContext.CancellationToken;
        InterruptedRunCreator creator = await InterruptedRunCreator
            .OpenAsync(testContext, false, token)
            .ConfigureAwait(false);
        await creator
            .RunOwnedAsync(async () =>
            {
                CoordinatorScenarioRun state = CoordinatorScenarioRun.Attach(creator, token);
                await state
                    .RunBodyAsync(async () =>
                    {
                        string session = creator.SessionId;
                        string reservation = creator.ReservationId;
                        string key = "native:bootstrap:" + session;
                        string journalBefore = OwnedCreatorProcess.Hash(creator.JournalPath);
                        Assert.AreEqual(
                            "true",
                            creator.Checkpoint["confirmation"]!.GetValue<string>()
                        );
                        Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                        Assert.AreEqual(200m, state.Reservation("worker1").Allowance);
                        Assert.AreEqual(
                            creator.Checkpoint["clock"]!.GetValue<DateTimeOffset>(),
                            state.Clock.GetUtcNow()
                        );
                        await creator.CrashAsync(token).ConfigureAwait(false);
                        Coordinator inspector = state.Coordinator("status");
                        if (legacyMarkerAbsent)
                        {
                            // Simulate legacy state missing only the bootstrap confirmation key.
                            string statusBefore = StatusData(inspector).ToJsonString();
                            string financialBefore = state
                                .Ledger.Status(
                                    state.Database.RunId,
                                    state.Database.Configuration.RunAiCreditAllowance
                                )
                                .ToJsonString();
                            using (SqliteConnection connection = state.Database.Open())
                            {
                                Sql.Execute(
                                    connection,
                                    "DELETE FROM meta WHERE key=$key;",
                                    null,
                                    ("$key", key)
                                );
                            }
                            Assert.AreEqual(statusBefore, StatusData(inspector).ToJsonString());
                            Assert.AreEqual(
                                journalBefore,
                                OwnedCreatorProcess.Hash(creator.JournalPath)
                            );
                            Assert.AreEqual(
                                financialBefore,
                                state
                                    .Ledger.Status(
                                        state.Database.RunId,
                                        state.Database.Configuration.RunAiCreditAllowance
                                    )
                                    .ToJsonString()
                            );
                        }

                        const string continueBody =
                            "Continue the actual interrupted decision, do not replay work.";
                        _ = state.Invoke(
                            "owner",
                            "message_send",
                            new()
                            {
                                ["recipient"] = "worker1",
                                ["body"] = continueBody,
                                ["urgent"] = true,
                            }
                        );
                        _ = state.Start("reconnect");
                        ControlledSessionPool.Turn next = await state
                            .FixedAsync(
                                nextToken => state.Sessions.NextTurnAsync("worker1", nextToken),
                                token
                            )
                            .ConfigureAwait(false);
                        long batch = state.SendingBatch("worker1");
                        JsonObject workerStatus = WorkerStatus(inspector, "worker1");
                        Assert.AreEqual(session, next.Session.SessionId);
                        Assert.AreEqual(session, workerStatus["sessionId"]!.GetValue<string>());
                        Assert.AreEqual(
                            "exact-session-load",
                            workerStatus["recovery"]!.GetValue<string>()
                        );
                        Assert.AreEqual(reservation, state.Reservation("worker1").Id);
                        Assert.AreEqual(
                            creator.Checkpoint["workDeadline"]!.GetValue<DateTimeOffset>(),
                            state.WorkDeadline
                        );
                        Assert.Contains(continueBody, next.Text);
                        if (legacyMarkerAbsent)
                        {
                            Assert.Contains(
                                "Goal: " + state.Database.Configuration.Goal,
                                next.Text
                            );
                            Assert.Contains("==== DURABLE CHANGES ====", next.Text);
                        }
                        else
                        {
                            Assert.StartsWith("==== EVENTS ====\n", next.Text);
                            Assert.DoesNotContain(
                                "Goal: " + state.Database.Configuration.Goal,
                                next.Text
                            );
                            Assert.DoesNotContain("==== DURABLE CHANGES ====", next.Text);
                        }

                        ControlledSessionJournal journal = state.Sessions.Journal!;
                        JsonObject loaded = journal.Snapshot(session);
                        JsonArray generations = loaded["generations"]!.AsArray();
                        Assert.HasCount(2, generations);
                        Assert.AreEqual(
                            10_000_000_000d,
                            generations[1]!["openingNano"]!.GetValue<double>()
                        );
                        Assert.AreEqual(1L, generations[1]!["openingRequests"]!.GetValue<long>());
                        Assert.AreEqual(
                            session,
                            generations[1]!["exactSession"]!.GetValue<string>()
                        );
                        Assert.AreEqual(190m, generations[1]!["appliedLimit"]!.GetValue<decimal>());
                        Assert.AreNotEqual(
                            generations[0]!["runtimeId"]!.GetValue<string>(),
                            generations[1]!["runtimeId"]!.GetValue<string>()
                        );
                        Assert.IsFalse(creator.Checkpoint["history"]!["pending"]!.GetValue<bool>());
                        Assert.HasCount(1, generations[0]!["dispatches"]!.AsArray());
                        Assert.HasCount(1, generations[1]!["dispatches"]!.AsArray());

                        const string followupBody =
                            "Confirm incremental continuation without replay.";
                        _ = state.Invoke(
                            "owner",
                            "message_send",
                            new()
                            {
                                ["recipient"] = "worker1",
                                ["body"] = followupBody,
                                ["urgent"] = true,
                            }
                        );
                        next.Complete();
                        await state
                            .WaitAckAsync(batch, state.WorkDeadline, token)
                            .ConfigureAwait(false);
                        ControlledSessionPool.Turn followup = await state
                            .FixedAsync(
                                nextToken => state.Sessions.NextTurnAsync("worker1", nextToken),
                                token
                            )
                            .ConfigureAwait(false);
                        long followupBatch = state.SendingBatch("worker1");
                        Assert.AreEqual(session, followup.Session.SessionId);
                        Assert.StartsWith("==== EVENTS ====\n", followup.Text);
                        Assert.DoesNotContain(
                            "Goal: " + state.Database.Configuration.Goal,
                            followup.Text
                        );
                        Assert.DoesNotContain("==== DURABLE CHANGES ====", followup.Text);
                        Assert.DoesNotContain(continueBody, followup.Text);
                        Assert.Contains(followupBody, followup.Text);

                        ObservedManualClock.ObservedPoll ownerPoll = await state
                            .ObserveOwnerPollAsync(token)
                            .ConfigureAwait(false);
                        followup.Complete();
                        await state
                            .WaitAckAsync(followupBatch, state.WorkDeadline, token)
                            .ConfigureAwait(false);
                        Assert.IsEmpty(
                            NoticeItems(state.Collaboration.AddNotices("worker1", new()))
                        );
                        GitScenarioState.Data(
                            await state
                                .StopRunningAsync("pause", ownerPoll, token)
                                .ConfigureAwait(false)
                        );
                        JsonObject finalStatus = StatusData(inspector);
                        Assert.AreEqual("paused", finalStatus["lifecycle"]!.GetValue<string>());
                        Assert.AreEqual(
                            "owner-pause",
                            finalStatus["stopReason"]!.GetValue<string>()
                        );
                        Assert.IsFalse(finalStatus["active"]!.GetValue<bool>());
                        Assert.AreEqual(3L, state.Dispatches(reservation));
                        Assert.AreEqual(
                            3L,
                            journal.Snapshot(session)["requests"]!.GetValue<long>()
                        );
                        Assert.IsFalse(journal.Snapshot(session)["pending"]!.GetValue<bool>());
                        Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                        Assert.AreEqual(30m, state.Reservation("worker1").Actual);
                    })
                    .ConfigureAwait(false);
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("BootstrapAndIncrementalContext")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ConfirmedSessionUsesIncrementalEventsWithFullBacklogDirectNoticeAndClaim()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn first = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                JsonObject initial = Events(first.Text);
                Assert.AreEqual(
                    1,
                    first
                        .Text.Split(
                            "Goal: " + state.Database.Configuration.Goal,
                            StringSplitOptions.None
                        )
                        .Length - 1
                );
                Assert.Contains("board_publish CLAIM", first.Text);
                Assert.IsFalse(initial["sharedContext"]!.AsObject().ContainsKey("goal"));
                Assert.IsFalse(initial["sharedContext"]!.AsObject().ContainsKey("cooperation"));
                Assert.IsTrue(initial["sharedContext"]!.AsObject().ContainsKey("tasks"));
                long firstBatch = state.SendingBatch("worker1");

                string firstBacklogBody =
                    "Backlog decision 00; "
                    + new string('x', 4000)
                    + " Preserve this complete earlier evidence suffix.";
                List<long> messages = [];
                for (int index = 0; index < 35; index++)
                {
                    JsonObject message = state.Invoke(
                        "owner",
                        "message_send",
                        new()
                        {
                            ["recipient"] = "team",
                            ["body"] = index == 0
                                ? firstBacklogBody
                                : $"Backlog decision {index:D2}; preserve its order.",
                        }
                    );
                    messages.Add(message["id"]!.GetValue<long>());
                }
                JsonObject direct = state.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker1",
                        ["body"] = "Urgent direct collision decision.",
                        ["urgent"] = true,
                    }
                );
                JsonObject claim = state.Invoke(
                    "owner",
                    "board_publish",
                    new()
                    {
                        ["type"] = "CLAIM",
                        ["content"] = "Advisory claim for #1",
                        ["workLabel"] = "prompt-advisory",
                        ["ttlSeconds"] = 60,
                    }
                );
                first.Complete();
                await state
                    .WaitAckAsync(firstBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                ControlledSessionPool.Turn later = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    "prompting",
                    WorkerStatus(inspector, "worker1")["lifecycle"]!.GetValue<string>()
                );
                Assert.AreEqual(first.Session.SessionId, later.Session.SessionId);
                Assert.StartsWith("==== EVENTS ====\n", later.Text);
                Assert.DoesNotContain("==== DURABLE CHANGES ====", later.Text);
                Assert.DoesNotContain("Goal: " + state.Database.Configuration.Goal, later.Text);
                JsonObject incremental = Events(later.Text);
                JsonObject context = incremental["sharedContext"]!.AsObject();
                Assert.IsFalse(context.ContainsKey("members"));
                Assert.IsFalse(context.ContainsKey("tasks"));
                Assert.IsFalse(context.ContainsKey("goal"));
                Assert.IsFalse(context.ContainsKey("cooperation"));
                Assert.IsEmpty(context["messages"]?.AsArray() ?? new JsonArray());
                JsonObject[] events = incremental["events"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .ToArray();
                JsonObject history = events.Single(item =>
                    item["kind"]!.GetValue<string>() == "group_message_history"
                );
                const int earlierMessages = 3;
                long claimId = claim["id"]!.GetValue<long>();
                Assert.AreEqual(35, history["total"]!.GetValue<int>());
                Assert.AreEqual(earlierMessages, history["earlier"]!.GetValue<int>());
                CollectionAssert.AreEqual(
                    messages
                        .Skip(earlierMessages)
                        .Select(id => (Kind: "group", Id: id))
                        .Append(("dm", direct["id"]!.GetValue<long>()))
                        .Append(("board", claimId))
                        .ToArray(),
                    events
                        .Where(item =>
                            item["kind"]!.GetValue<string>() is "group" or "dm" or "board"
                        )
                        .Select(item =>
                            (
                                Kind: item["kind"]!.GetValue<string>(),
                                Id: item["kind"]!.GetValue<string>() == "board"
                                    ? item["payload"]!["gist"]!["id"]!.GetValue<long>()
                                    : item["payload"]!["message"]!["id"]!.GetValue<long>()
                            )
                        )
                        .ToArray()
                );
                string path = history["path"]!.GetValue<string>();
                Assert.StartsWith(
                    Path.GetFullPath(state.Database.StatePath) + Path.DirectorySeparatorChar,
                    Path.GetFullPath(path)
                );
                JsonObject[] full = File.ReadAllLines(path)
                    .Select(line => JsonNode.Parse(line)!.AsObject())
                    .ToArray();
                Assert.HasCount(35, full);
                Assert.AreEqual(
                    string.Join(",", messages),
                    string.Join(
                        ",",
                        full.Select(item => item["payload"]!["message"]!["id"]!.GetValue<long>())
                    )
                );
                JsonObject retainedFirstMessage = full.Single(item =>
                    item["payload"]!["message"]!["id"]!.GetValue<long>() == messages[0]
                )["payload"]!["message"]!.AsObject();
                Assert.AreEqual(
                    firstBacklogBody,
                    retainedFirstMessage["body"]!.GetValue<string>()
                );
                JsonObject dm = events.Single(item => item["kind"]!.GetValue<string>() == "dm");
                Assert.AreEqual(
                    direct["id"]!.GetValue<long>(),
                    dm["payload"]!["message"]!["id"]!.GetValue<long>()
                );
                Assert.IsTrue(dm["payload"]!["urgent"]!.GetValue<bool>());
                Assert.DoesNotContain(
                    claimId,
                    context["board"]!
                        .AsArray()
                        .OfType<JsonObject>()
                        .Select(item => item["id"]!.GetValue<long>())
                );
                long batch = state.SendingBatch("worker1");
                Assert.AreEqual(
                    claimId,
                    Assert.ContainsSingle(
                        state.Collaboration.ReadSharedContext("worker1")["board"]!
                            .AsArray()
                            .OfType<JsonObject>()
                    )["id"]!.GetValue<long>()
                );
                ObservedManualClock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                later.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                Assert.IsEmpty(
                    state.Collaboration.ReadSharedContext("worker1")["board"]!.AsArray()
                );
                Assert.IsEmpty(NoticeItems(state.Collaboration.AddNotices("worker1", new())));
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", ownerPoll, token)
                        .ConfigureAwait(false)
                );
                Assert.AreEqual(20m, state.Reservation("worker1").Actual);
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ConfirmationRecoveryAndAck")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ConfirmedInitialTurnTransitionsNextSameSessionTurnToIncrementalEvents()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                Assert.Contains("Goal: " + state.Database.Configuration.Goal, turn.Text);
                Assert.Contains("==== DURABLE CHANGES ====", turn.Text);
                const string followupBody = "Follow-up after confirmed initial turn.";
                _ = state.Invoke(
                    "owner",
                    "message_send",
                    new()
                    {
                        ["recipient"] = "worker1",
                        ["body"] = followupBody,
                        ["urgent"] = true,
                    }
                );
                turn.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);

                ControlledSessionPool.Turn followup = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long followupBatch = state.SendingBatch("worker1");
                Assert.AreEqual(turn.Session.SessionId, followup.Session.SessionId);
                Assert.StartsWith("==== EVENTS ====\n", followup.Text);
                Assert.DoesNotContain(
                    "Goal: " + state.Database.Configuration.Goal,
                    followup.Text
                );
                Assert.DoesNotContain("==== DURABLE CHANGES ====", followup.Text);
                JsonObject[] followupEvents = Events(followup.Text)["events"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .ToArray();
                Assert.DoesNotContain(
                    "kickoff",
                    followupEvents.Select(item => item["kind"]!.GetValue<string>())
                );
                Assert.Contains(followupBody, followup.Text);

                ObservedManualClock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                followup.Complete();
                await state
                    .WaitAckAsync(followupBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", ownerPoll, token)
                        .ConfigureAwait(false)
                );
                JsonObject status = StatusData(inspector);
                JsonObject worker = WorkerStatus(inspector, "worker1");
                Assert.AreEqual("paused", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("owner-pause", status["stopReason"]!.GetValue<string>());
                Assert.AreEqual(turn.Session.SessionId, worker["sessionId"]!.GetValue<string>());
                Assert.AreEqual(
                    "fresh-session-with-durable-summary",
                    worker["recovery"]!.GetValue<string>()
                );
                CreditReservation settled = state.Reservation("worker1");
                Assert.AreEqual("reconciled", settled.State);
                Assert.AreEqual(20m, settled.Actual);
                Assert.AreEqual(2L, state.Dispatches(settled.Id));
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ConfirmationRecoveryAndAck")]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task FailedOrCancelledSendCannotConfirmBootstrapOrAcknowledgeNotices(bool cancel)
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                JsonObject unacknowledged = state.Invoke(
                    "owner",
                    "board_publish",
                    new()
                    {
                        ["type"] = "FACT",
                        ["content"] = "Unacknowledged bootstrap finding",
                    }
                );
                _ = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.Contains("Goal: " + state.Database.Configuration.Goal, turn.Text);
                Assert.Contains("Unacknowledged bootstrap finding", turn.Text);
                if (cancel)
                {
                    state.CancelCaller();
                    JsonObject result = await state.Active.WaitAsync(token).ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.IsNull(state.Reservation("worker1").Actual);
                }
                else
                {
                    const string reason = "Controlled failed send.";
                    turn.Fail(new OperationException("bootstrap_send_failed", reason));
                    JsonObject result = await state.Active.WaitAsync(token).ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                }

                JsonObject status = StatusData(inspector);
                Assert.AreEqual("unresolved", status["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    cancel ? "caller-cancelled" : "bootstrap_send_failed",
                    status["stopReason"]!.GetValue<string>()
                );
                Assert.IsFalse(status["active"]!.GetValue<bool>());
                Assert.AreEqual(0, turn.Session.CompletedTurns);
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    unacknowledged["id"]!.GetValue<long>(),
                    Assert.ContainsSingle(
                        state.Collaboration.ReadSharedContext("worker1")["board"]!
                            .AsArray()
                            .OfType<JsonObject>()
                    )["id"]!.GetValue<long>()
                );
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
                if (!cancel)
                {
                    state.Clock.Advance(TimeSpan.FromMilliseconds(999));
                    DeliveryBatch? stoppingNotice = state.Collaboration.ClaimBatch("worker1", true);
                    Assert.IsNotNull(stoppingNotice);
                    Assert.DoesNotContain("Initial peer kickoff", stoppingNotice.Prompt);
                    state.Collaboration.CompleteBatch(stoppingNotice.Id);
                    Assert.IsNull(state.Collaboration.ClaimBatch("worker1", true));
                    state.Clock.Advance(TimeSpan.FromMilliseconds(1));
                    DeliveryBatch? retry = state.Collaboration.ClaimBatch("worker1", true);
                    Assert.IsNotNull(retry);
                    Assert.AreEqual(2, retry.Attempt);
                    Assert.Contains("Initial peer kickoff", retry.Prompt);
                }
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ConfirmationRecoveryAndAck")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AckFailureRetainsCompletedSendConfirmationWithoutClaimingAcknowledgement()
    {
        CancellationToken token = testContext.CancellationToken;
        CoordinatorScenarioRun state = await CoordinatorScenarioRun
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                Coordinator inspector = state.Coordinator("status");
                JsonObject gist = state.Invoke(
                    "owner",
                    "board_publish",
                    new()
                    {
                        ["type"] = "FACT",
                        ["content"] = "Completed turn notice awaiting durable ack",
                    }
                );
                state.Execute(
                    """
                    CREATE TRIGGER test_reject_ack BEFORE UPDATE OF state ON delivery_batches
                    WHEN OLD.state='sending' AND NEW.state='sent'
                    BEGIN SELECT RAISE(ABORT,'Phase7 controlled ACK storage failure.'); END;
                    """
                );
                Task<JsonObject> originalRun = state.Start();
                ControlledSessionPool.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.Contains("Completed turn notice awaiting durable ack", turn.Text);
                turn.Complete();
                JsonObject result = await originalRun.WaitAsync(token).ConfigureAwait(false);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("post_delivery_failed", result["code"]!.GetValue<string>());
                Assert.Contains(
                    "Phase7 controlled ACK storage failure.",
                    result["message"]!.GetValue<string>()
                );
                JsonObject stoppedStatus = StatusData(inspector);
                Assert.AreEqual("stopped", stoppedStatus["lifecycle"]!.GetValue<string>());
                Assert.AreEqual(
                    "post_delivery_failed",
                    stoppedStatus["stopReason"]!.GetValue<string>(),
                    result.ToJsonString()
                );
                Assert.AreEqual(
                    "true",
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    gist["id"]!.GetValue<long>(),
                    Assert.ContainsSingle(
                        state.Collaboration.ReadSharedContext("worker1")["board"]!
                            .AsArray()
                            .OfType<JsonObject>()
                    )["id"]!.GetValue<long>()
                );
                CreditReservation originalUsage = state.Reservation("worker1");
                Assert.AreEqual(10m, originalUsage.Actual);
                Assert.AreEqual("reconciled", originalUsage.State);
                Assert.IsFalse(originalUsage.GrantOpen);
                ControlledSessionPool.Host originalHost = state.Sessions.Hosts.Single(host =>
                    host.Operation == "run"
                );
                Assert.IsTrue(
                    originalHost.Witness["managedSettlementBeforeWrapperDispose"]!.GetValue<bool>()
                );

                state.Execute("DROP TRIGGER test_reject_ack;");
                _ = state.Start("resume");
                ControlledSessionPool.Turn resumed = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.IsFalse(resumed.Response.IsCompleted);
                Assert.AreNotEqual(turn.Session.SessionId, resumed.Session.SessionId);
                Assert.AreEqual(
                    "fresh-session-with-durable-summary",
                    WorkerStatus(inspector, "worker1")["recovery"]!.GetValue<string>()
                );
                long resumedBatch = state.SendingBatch("worker1");
                Assert.IsEmpty(
                    state.Collaboration.ReadSharedContext("worker1")["board"]!.AsArray()
                );
                JsonObject[] resumedEvents = Events(resumed.Text)["events"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .ToArray();
                Assert.DoesNotContain(
                    "board",
                    resumedEvents.Select(item => item["kind"]!.GetValue<string>())
                );
                Assert.ContainsSingle(
                    resumedEvents.Where(item => item["kind"]!.GetValue<string>() == "kickoff")
                );

                ObservedManualClock.ObservedPoll resumedPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                resumed.Complete();
                await state
                    .WaitAckAsync(resumedBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state.StopRunningAsync("pause", resumedPoll, token).ConfigureAwait(false)
                );
                JsonObject pausedStatus = StatusData(inspector);
                Assert.AreEqual("paused", pausedStatus["lifecycle"]!.GetValue<string>());
                Assert.AreEqual("owner-pause", pausedStatus["stopReason"]!.GetValue<string>());
                Assert.AreEqual(
                    10m,
                    state
                        .Ledger.Reservations(state.Database.RunId)
                        .Single(row => row.Id == originalUsage.Id)
                        .Actual
                );
                CreditReservation resumedUsage = state.Reservation("worker1");
                Assert.AreNotEqual(originalUsage.Id, resumedUsage.Id);
                Assert.AreEqual(10m, resumedUsage.Actual);
                Assert.AreEqual("reconciled", resumedUsage.State);
                Assert.IsFalse(resumedUsage.GrantOpen);
            })
            .ConfigureAwait(false);
    }

    private static JsonObject StatusData(Coordinator inspector) =>
        GitScenarioState.Data(inspector.Status());

    private static JsonObject WorkerStatus(Coordinator inspector, string worker) =>
        StatusData(inspector)["workers"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == worker);

    private static JsonObject[] NoticeItems(JsonObject result) =>
        result["notices"]!["items"]!.AsArray().OfType<JsonObject>().ToArray();

    private static JsonObject Events(string prompt)
    {
        const string separator = "==== EVENTS ====\n";
        int index = prompt.LastIndexOf(separator, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index);
        return JsonNode.Parse(prompt[(index + separator.Length)..])!.AsObject();
    }
}
