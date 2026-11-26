using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class BoardDeliverySpecs(TestContext context)
{
    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void RootClosureReopenClearsWorkerClosureAndPreservesOwnerClosure()
    {
        using BoardDeliveryState state = new();
        JsonObject close = new() { ["action"] = "close", ["id"] = 1 };
        JsonObject root = new() { ["id"] = 1 };
        JsonObject workerClosed = state.Invoke("worker1", "task_write", close);
        Assert.AreEqual("closed", workerClosed["state"]!.GetValue<string>());
        Assert.AreEqual("worker1", workerClosed["closedBy"]!.GetValue<string>());
        Assert.IsTrue(state.Operations.ReopenWorkerClosedRoot());
        JsonObject reopened = state.Invoke("owner", "task_read", root);
        Assert.AreEqual("open", reopened["state"]!.GetValue<string>());
        Assert.IsNull(reopened["closedBy"]);
        Assert.IsFalse(state.Operations.ReopenWorkerClosedRoot());

        // Normalize legacy retained state where an earlier build reopened the task without
        // clearing worker closed_by / root_closed_by metadata.
        using (SqliteConnection legacy = state.Database.Open())
        {
            Sql.Execute(
                legacy,
                """
                UPDATE tasks SET closed_by='worker1' WHERE id=1;
                INSERT OR REPLACE INTO meta(key,value) VALUES('lifecycle:root_closed_by','worker1');
                """
            );
        }
        Assert.IsFalse(state.Operations.ReopenWorkerClosedRoot());
        Assert.IsNull(state.Invoke("owner", "task_read", root)["closedBy"]);
        Assert.IsNull(state.Database.ReadMetadata("lifecycle:root_closed_by"));

        JsonObject ownerClosed = state.Invoke("owner", "task_write", close);
        Assert.AreEqual("owner", ownerClosed["closedBy"]!.GetValue<string>());
        Assert.IsFalse(state.Operations.ReopenWorkerClosedRoot());
        JsonObject retained = state.Invoke("owner", "task_read", root);
        Assert.AreEqual("closed", retained["state"]!.GetValue<string>());
        Assert.AreEqual("owner", retained["closedBy"]!.GetValue<string>());
    }

    [TestMethod]
    [TestCategory("NoticeLeaseShowAndAck")]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ContendedNoticeLeasePreservesToolResultWithDegradedEnvelope()
    {
        using BoardDeliveryState state = new();
        LocalTools tools = new(state.Database);
        JsonObject message = state.Send("worker2", "worker1", "Retain this pending notice.");
        CancellationToken token = context.CancellationToken;
        JsonObject result;
        using (SqliteConnection writer = state.Database.Open())
        using (SqliteTransactionScope held = Sql.BeginTransaction(writer, deferred: false))
        {
            Sql.Execute(writer,
                "INSERT INTO notice_leases(recipient,token,expires_at) VALUES('owner','held',0);",
                held);
            result = await tools.InvokeAsync("worker1", "run_info", new(), token)
                .ConfigureAwait(false);
        }

        Assert.AreEqual(state.Database.Configuration.Goal,
            GitScenarioState.Data(result)["goal"]!.GetValue<string>());
        JsonObject notices = result["notices"]!.AsObject();
        Assert.AreEqual("worker1", notices["recipient"]!.GetValue<string>());
        Assert.IsEmpty(notices["items"]!.AsArray());
        Assert.IsTrue(notices["degraded"]!.GetValue<bool>());
        Assert.IsFalse(notices.ContainsKey("leaseToken"));
        string diagnostics = Path.Combine(state.Database.StatePath, "run.sqlite.diagnostics");
        Assert.IsTrue(Directory.EnumerateFiles(diagnostics, "incident-*.json")
            .Select(path => JsonNode.Parse(File.ReadAllText(path))).Any(incident =>
            incident?["failure"]?["primaryCode"]?.GetValue<int>() == 5
            && incident["failure"]!["actor"]?.GetValue<string>() == "worker1"),
            "The failed notice lease must retain a SQLite contention incident.");

        JsonObject retried = await tools.InvokeAsync("worker1", "run_info", new(), token)
            .ConfigureAwait(false);
        Assert.ContainsSingle(NoticeItems(retried).Where(notice =>
            notice["data"]?["messageId"]?.GetValue<long>() == Id(message)));
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void AdvisoryClaimsExpireWithoutLockingTaskOrDestroyingHistory()
    {
        using BoardDeliveryState state = new();
        JsonObject task = state.Invoke(
            "owner",
            "task_write",
            new()
            {
                ["action"] = "create",
                ["title"] = "Repair cache",
                ["body"] = "Keep eviction deterministic.",
            }
        );
        long taskId = Id(task);
        JsonObject first = state.Publish(
            "worker1",
            "CLAIM",
            $"Inspect cache for #{taskId}",
            "Inspect eviction trace before editing.",
            ttl: 10
        );
        JsonObject second = state.Publish(
            "worker2",
            "CLAIM",
            $"Check tests for #{taskId}",
            ttl: 20
        );
        JsonObject read = state.Invoke("worker2", "task_read", new() { ["id"] = taskId });
        JsonArray assignees = read["assignees"]!.AsArray();
        Assert.HasCount(2, assignees);
        Assert.AreEqual("worker1", assignees[0]!.GetValue<string>());
        Assert.AreEqual("worker2", assignees[1]!.GetValue<string>());
        Assert.AreEqual("open", read["state"]!.GetValue<string>());
        Assert.HasCount(2, Items(state.Invoke("owner", "board_recent", new())));

        state.Clock.Advance(TimeSpan.FromSeconds(10));
        read = state.Invoke("worker1", "task_read", new() { ["id"] = taskId });
        Assert.AreEqual(
            "worker2",
            Assert.ContainsSingle(read["assignees"]!.AsArray())!.GetValue<string>()
        );
        const string updatedBody = "Keep eviction deterministic while comparing both traces.";
        _ = state.Invoke(
            "worker1",
            "task_write",
            new() { ["action"] = "update", ["id"] = taskId, ["body"] = updatedBody }
        );
        read = state.Invoke("worker2", "task_read", new() { ["id"] = taskId });
        Assert.AreEqual(updatedBody, read["body"]!.GetValue<string>());
        Assert.AreEqual(
            "worker2",
            Assert.ContainsSingle(read["assignees"]!.AsArray())!.GetValue<string>()
        );
        // Recent visibility is inclusive at the timestamp; actionable claims are exclusive.
        Assert.Contains(Id(first), Ids(state.Invoke("owner", "board_recent", new())));
        state.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain(Id(first), Ids(state.Invoke("owner", "board_recent", new())));
        Assert.Contains(Id(second), Ids(state.Invoke("owner", "board_recent", new())));
        JsonObject history = state.Invoke(
            "worker2",
            "board_search",
            new() { ["query"] = "Inspect cache" }
        );
        Assert.AreEqual(Id(first), Id(Assert.ContainsSingle(Items(history))));
        JsonObject expanded = state.Invoke(
            "worker2",
            "board_expand",
            new() { ["labels"] = new JsonArray(first["label"]!.GetValue<string>()) }
        );
        Assert.AreEqual(
            "Inspect eviction trace before editing.",
            Assert.ContainsSingle(Items(expanded))["detail"]!.GetValue<string>()
        );
        Assert.IsEmpty(expanded["unknownLabels"]!.AsArray());

        JsonObject live = state.Publish("worker1", "FACT", "Fresh cache finding");
        DeliveryBatch actionable = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        JsonObject actionablePrompt = JsonNode.Parse(actionable.Prompt)!.AsObject();
        JsonObject[] boardEvents = actionablePrompt["events"]!
            .AsArray()
            .OfType<JsonObject>()
            .Where(item => item["kind"]!.GetValue<string>() == "board")
            .ToArray();
        Assert.AreEqual(
            Id(live),
            Assert.ContainsSingle(boardEvents)["payload"]!["gistId"]!.GetValue<long>()
        );
        Assert.DoesNotContain(
            Id(first),
            actionablePrompt["sharedContext"]!["board"]!
                .AsArray()
                .OfType<JsonObject>()
                .Select(Id)
        );
        state.Operations.CompleteBatch(actionable.Id);
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void ReplacementInvalidationAndSemanticCollapseRetainHistoricalDetail()
    {
        using BoardDeliveryState state = new();
        JsonObject first = state.Publish(
            "worker1",
            "PATCH_SUMMARY",
            "files=cache.py | idea=bound eviction | evidence=test_cache passed",
            "First candidate evidence",
            "cache"
        );
        JsonObject replacement = state.Publish(
            "worker1",
            "PATCH_SUMMARY",
            "files=cache.py | idea=stable eviction | evidence=test_cache verified",
            "Replacement candidate evidence",
            "cache"
        );
        Assert.IsTrue(replacement["verifiedCheckpoint"]!.GetValue<bool>());
        Assert.AreEqual(
            Id(replacement),
            Id(Assert.ContainsSingle(Items(state.Invoke("owner", "board_recent", new()))))
        );

        JsonObject failure = state.Publish(
            "worker1",
            "FAIL",
            "cache.py line 12 still evicts live entries",
            workLabel: "cache"
        );
        JsonObject current = Items(state.Invoke("owner", "board_recent", new()))
            .Single(item => Id(item) == Id(replacement));
        Assert.IsFalse(current["verifiedCheckpoint"]!.GetValue<bool>());
        Assert.AreEqual(
            failure["content"]!.GetValue<string>(),
            current["invalidatedReason"]!.GetValue<string>()
        );
        Assert.AreEqual(1L, current["localFailures"]!.GetValue<long>());
        JsonObject next = state.Publish(
            "worker1",
            "PATCH_SUMMARY",
            "files=cache.py | idea=preserve live entries | evidence=test_cache passed",
            "Third candidate evidence",
            "cache"
        );
        Assert.AreEqual(1L, next["localFailures"]!.GetValue<long>());
        Assert.IsTrue(next["verifiedCheckpoint"]!.GetValue<bool>());

        JsonObject fact = state.Publish(
            "worker2",
            "FACT",
            "cache.py line 12 def evict confirms ordering"
        );
        state.Publish("worker2", "FACT", "cache.py line 12 def evict records ordering");
        JsonObject[] recent = Items(state.Invoke("owner", "board_recent", new()));
        Assert.HasCount(3, recent);
        CollectionAssert.AreEqual(
            new[] { Id(failure), Id(next), Id(fact) },
            recent.Select(Id).ToArray()
        );
        JsonObject collapsed = recent.Single(item => Id(item) == Id(fact));
        Assert.AreEqual(2, collapsed["duplicateCount"]!.GetValue<int>());
        Assert.HasCount(
            6,
            Items(state.Invoke("owner", "board_search", new() { ["query"] = "cache.py" }))
        );
        JsonObject expanded = state.Invoke(
            "owner",
            "board_expand",
            new()
            {
                ["labels"] = new JsonArray(
                    first["label"]!.GetValue<string>(),
                    replacement["label"]!.GetValue<string>()
                ),
            }
        );
        JsonObject[] historical = Items(expanded);
        Assert.HasCount(2, historical);
        Assert.AreEqual("First candidate evidence", historical[0]["detail"]!.GetValue<string>());
        Assert.AreEqual(
            "Replacement candidate evidence",
            historical[1]["detail"]!.GetValue<string>()
        );
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void BooleanHistorySearchTreatsWildcardsAndSqlAsLiteralText()
    {
        using BoardDeliveryState state = new();
        JsonObject literal = state.Publish("worker1", "FACT", "cache 100% a_b ' OR 1=1 --");
        JsonObject other = state.Publish("worker2", "OBSERVED", "cache 100x axb");
        JsonObject both = state.Publish("worker1", "FACT", "cache queue");
        JsonObject queue = state.Publish("worker2", "OBSERVED", "queue follow-up recorded");
        CollectionAssert.AreEqual(
            new[] { Id(literal), Id(both) },
            Ids(
                state.Invoke(
                    "worker2",
                    "board_search",
                    new() { ["query"] = "cache & (100% | queue)" }
                )
            )
        );
        CollectionAssert.AreEqual(
            new[] { Id(literal), Id(both), Id(queue) },
            Ids(
                state.Invoke(
                    "worker2",
                    "board_search",
                    new() { ["query"] = "queue | cache & 100%" }
                )
            )
        );
        Assert.AreEqual(
            Id(literal),
            Id(
                Assert.ContainsSingle(
                    Items(state.Invoke("owner", "board_search", new() { ["query"] = "a_b" }))
                )
            )
        );
        Assert.AreEqual(
            Id(literal),
            Id(
                Assert.ContainsSingle(
                    Items(
                        state.Invoke("owner", "board_search", new() { ["query"] = "' OR 1=1 --" })
                    )
                )
            )
        );
        Assert.AreEqual(
            Id(other),
            Id(
                Assert.ContainsSingle(
                    Items(state.Invoke("owner", "board_search", new() { ["query"] = "100x" }))
                )
            )
        );
        Assert.HasCount(
            4,
            Items(state.Invoke("owner", "board_search", new() { ["query"] = "cache | queue" }))
        );
        JsonObject rejected = state.Operations.Invoke(
            "worker1",
            "board_publish",
            new() { ["type"] = "FACT", ["content"] = "none" }
        );
        Assert.AreEqual("invalid_arguments", rejected["code"]!.GetValue<string>());
        Assert.HasCount(4, Items(state.Invoke("owner", "board_recent", new())));
        CollectionAssert.AreEqual(
            new[] { "g" + Id(literal), "g" + Id(both) },
            NoticeItems(state.Operations.AddNotices("worker2", new()))
                .Select(item => item["id"]!.GetValue<string>())
                .ToArray()
        );
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void OverflowEvictsOldestOfTwoThousandRecentSlotsButNotItsHistory()
    {
        using BoardDeliveryState state = new();
        JsonObject oldest = state.Publish(
            "worker1",
            "CLAIM",
            "Oldest real admission #1",
            "Retained oldest evidence"
        );
        using (SqliteConnection connection = state.Database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            // Seed pre-boundary slots in one transaction to avoid per-row diagnostic flushes.
            for (int index = 1; index <= 1999; index++)
            {
                string text =
                    "Distinct retained observation " + index.ToString(CultureInfo.InvariantCulture);
                Sql.Execute(
                    connection,
                    """
                    INSERT INTO board_history(actor,type,content,created_at,verified_checkpoint)
                    VALUES('worker1','OBSERVED',$text,$now,0);
                    INSERT INTO board_recent(revision_id,first_at) VALUES(last_insert_rowid(),$now);
                    """,
                    transaction,
                    ("$text", text),
                    ("$now", state.Database.Now.ToString("O", CultureInfo.InvariantCulture))
                );
            }
            transaction.Commit();
        }
        Assert.AreEqual(
            "worker1",
            Assert.ContainsSingle(
                state.Invoke("owner", "task_read", new() { ["id"] = 1 })["assignees"]!.AsArray()
            )!.GetValue<string>()
        );
        JsonObject newest = state.Publish(
            "worker2",
            "FACT",
            "Newest real overflow admission",
            "Overflow evidence"
        );
        Assert.IsEmpty(
            state.Invoke("owner", "task_read", new() { ["id"] = 1 })["assignees"]!.AsArray()
        );
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        JsonObject recent = state.Invoke("owner", "board_recent", new() { ["limit"] = 200 });
        Assert.HasCount(200, Items(recent));
        Assert.AreEqual(2000L, state.Count("SELECT COUNT(*) FROM board_recent;"));
        Assert.AreEqual(Id(newest), Id(Items(recent)[^1]));
        Assert.DoesNotContain(Id(oldest), Ids(recent));
        int[] invalidLimits = [0, 201];
        foreach (int limit in invalidLimits)
        {
            JsonObject rejected = state.Operations.Invoke(
                "owner",
                "board_recent",
                new() { ["limit"] = limit }
            );
            Assert.IsFalse(rejected["ok"]!.GetValue<bool>());
            Assert.AreEqual("invalid_arguments", rejected["code"]!.GetValue<string>());
        }
        Assert.AreEqual(
            "Retained oldest evidence",
            Assert.ContainsSingle(
                Items(
                    state.Invoke(
                        "owner",
                        "board_expand",
                        new() { ["labels"] = new JsonArray(oldest["label"]!.GetValue<string>()) }
                    )
                )
            )["detail"]!.GetValue<string>()
        );
        Assert.AreEqual(
            Id(oldest),
            Id(
                Assert.ContainsSingle(
                    Items(
                        state.Invoke(
                            "owner",
                            "board_search",
                            new() { ["query"] = "Oldest real admission" }
                        )
                    )
                )
            )
        );
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public async Task CompetingDomainWritesPersistMatchingRecipientEventsAndVisibleNotices()
    {
        using BoardDeliveryState state = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<JsonObject> first = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            return state.Send("worker1", "worker2", "Inspect eviction before merging.");
        });
        Task<JsonObject> second = Task.Run(async () =>
        {
            await release.Task.ConfigureAwait(false);
            return state.Send("owner", "worker2", "Preserve the regression evidence.");
        });
        release.SetResult();
        JsonObject[] sent = await Task.WhenAll(first, second).ConfigureAwait(false);
        foreach (JsonObject message in sent)
        {
            string channel = message["channelId"]!.GetValue<string>();
            JsonObject stored = Assert.ContainsSingle(
                Items(state.Invoke("worker2", "message_read", new() { ["channelId"] = channel }))
            );
            Assert.AreEqual(Id(message), Id(stored));
            Assert.AreEqual(
                message["body"]!.GetValue<string>(),
                stored["body"]!.GetValue<string>()
            );
        }
        JsonObject[] notices = NoticeItems(state.Operations.AddNotices("worker2", new()));
        CollectionAssert.AreEqual(
            sent.OrderBy(Id).Select(item => item["body"]!.GetValue<string>()).ToArray(),
            notices.Select(item => item["data"]!["message"]!["body"]!.GetValue<string>()).ToArray()
        );
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker1", new())));
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void RejectedMessageLeavesNoMessageEventOrNotice()
    {
        using BoardDeliveryState state = new();
        JsonObject original = state.Send("worker1", "team", "Visible root");
        JsonObject failure = state.Operations.Invoke(
            "worker1",
            "message_send",
            new()
            {
                ["recipient"] = "worker2",
                ["body"] = "Must not escape",
                ["threadId"] = Id(original),
            }
        );
        Assert.AreEqual("invalid_arguments", failure["code"]!.GetValue<string>());
        Assert.IsFalse(failure["ok"]!.GetValue<bool>());
        Assert.IsNull(failure["data"]);
        Assert.IsEmpty(
            Items(
                state.Invoke(
                    "owner",
                    "message_read",
                    new() { ["channelId"] = "dm:worker1:worker2" }
                )
            )
        );
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(
            "Visible root",
            Assert.ContainsSingle(
                Items(state.Invoke("worker2", "message_read", new() { ["channelId"] = "team" }))
            )["body"]!.GetValue<string>()
        );
        DeliveryBatch teamBatch = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        JsonObject teamEvent = Assert.ContainsSingle(
            JsonNode.Parse(teamBatch.Prompt)!["events"]!.AsArray().OfType<JsonObject>()
        );
        Assert.AreEqual("group", teamEvent["kind"]!.GetValue<string>());
        Assert.AreEqual(Id(original), teamEvent["payload"]!["messageId"]!.GetValue<long>());
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void EventEnqueueFailureRollsBackItsCallersDomainWriteAndEarlierEvent()
    {
        using BoardDeliveryState state = new();
        const string body = "Exercise atomic team fan-out.";
        const string injectedFailure = "BoardDelivery controlled second recipient insert failure.";
        JsonObject arguments = new() { ["recipient"] = "team", ["body"] = body };
        using (SqliteConnection connection = state.Database.Open())
        {
            Sql.Execute(
                connection,
                $"""
                CREATE TRIGGER reject_later_team_event BEFORE INSERT ON events
                WHEN NEW.kind='group' AND EXISTS (
                    SELECT 1 FROM messages m JOIN events prior
                        ON m.id=json_extract(prior.payload,'$.messageId')
                    WHERE m.id=json_extract(NEW.payload,'$.messageId')
                        AND m.actor='owner' AND m.channel_id='team' AND m.body='{body}'
                        AND prior.kind='group' AND prior.recipient<>NEW.recipient
                )
                BEGIN SELECT RAISE(ABORT,'{injectedFailure}'); END;
                """
            );
        }

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            state.Operations.Invoke("owner", "message_send", arguments)
        );
        Assert.Contains(injectedFailure, error.Message);
        Assert.IsEmpty(
            Items(state.Invoke("owner", "message_read", new() { ["channelId"] = "team" }))
        );
        Assert.IsNull(state.Operations.ClaimBatch("worker1", false));
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker1", new())));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));

        using (SqliteConnection connection = state.Database.Open())
        {
            Sql.Execute(connection, "DROP TRIGGER reject_later_team_event;");
        }

        JsonObject message = state.Invoke("owner", "message_send", arguments);
        Assert.AreEqual(
            Id(message),
            Id(
                Assert.ContainsSingle(
                    Items(state.Invoke("owner", "message_read", new() { ["channelId"] = "team" }))
                )
            )
        );
        foreach (string recipient in new[] { "worker1", "worker2" })
        {
            DeliveryBatch fanout = RequireBatch(state.Operations.ClaimBatch(recipient, false));
            JsonObject delivered = Assert.ContainsSingle(
                JsonNode.Parse(fanout.Prompt)!["events"]!.AsArray().OfType<JsonObject>()
            );
            Assert.AreEqual("group", delivered["kind"]!.GetValue<string>());
            Assert.AreEqual(Id(message), delivered["payload"]!["messageId"]!.GetValue<long>());
            Assert.AreEqual(body, delivered["payload"]!["message"]!["body"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedDeliveryUsesEveryRetryBoundaryAndFifthAttemptIsTerminal(bool interruptFifth)
    {
        using BoardDeliveryState state = new();
        JsonObject message = state.Send("worker1", "worker2", "Retry this exact payload.");
        DeliveryBatch batch = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        long eventId = Assert.ContainsSingle(batch.EventIds);
        int[] delays = [1, 5, 30, 120];
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Assert.AreEqual(attempt, batch.Attempt);
            Assert.AreEqual(eventId, Assert.ContainsSingle(batch.EventIds));
            AssertMessage(batch, Id(message), "Retry this exact payload.");
            if (interruptFifth && attempt == 5)
            {
                break;
            }
            state.Operations.FailBatch(batch.Id, "controlled delivery failure");
            if (attempt < 5)
            {
                state.Clock.Advance(
                    TimeSpan.FromSeconds(delays[attempt - 1]) - TimeSpan.FromMilliseconds(1)
                );
                Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
                state.Clock.Advance(TimeSpan.FromMilliseconds(1));
                batch = RequireBatch(state.Operations.ClaimBatch("worker2", false));
            }
        }
        state.Clock.Advance(TimeSpan.FromDays(1));
        state.Restart();
        state.Operations.RecoverInterrupted();
        Assert.AreEqual(
            interruptFifth ? "interrupted" : "failed",
            state.Text("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch.Id))
        );
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        JsonObject retained = Assert.ContainsSingle(
            Items(
                state.Invoke(
                    "worker2",
                    "message_read",
                    new() { ["channelId"] = message["channelId"]!.GetValue<string>() }
                )
            )
        );
        Assert.AreEqual(Id(message), Id(retained));
        Assert.AreEqual("Retry this exact payload.", retained["body"]!.GetValue<string>());
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AdmissionClosingBeforeDeliveryWriterHandoffPreservesPendingSource()
    {
        using BoardDeliveryState state = new();
        JsonObject message = state.Send("worker1", "worker2", "Keep this attempt available.");
        using WorkAdmission admission = new(
            state.Clock, state.Clock.GetUtcNow().AddSeconds(1), context.CancellationToken
        );
        CancellationToken admitted = admission.Token;
        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
        TaskCompletionSource<Activity> requested = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId && options.Name == "sqlite.acquisition"
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (activity.TraceId == traceId && activity.OperationName == "sqlite.acquisition")
                {
                    requested.TrySetResult(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        Task<DeliveryBatch?>? pending = null;
        Exception? primary = null;
        try
        {
            using (SqliteConnection writer = state.Database.Open())
            using (SqliteTransactionScope held = Sql.BeginTransaction(
                writer, deferred: false, operation: "DeliveryClaimHeldWriter"
            ))
            {
                pending = Task.Run(() =>
                {
                    using Activity scope = new Activity("delivery-claim.handoff")
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId(
                            traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded
                        ).Start();
                    return state.Operations.ClaimBatch("worker2", false, admission);
                });
                Task first = await Task.WhenAny(requested.Task, pending)
                    .WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken)
                    .ConfigureAwait(false);
                if (ReferenceEquals(first, pending))
                {
                    await pending.ConfigureAwait(false);
                    Assert.Fail("Delivery claim returned before its acquisition was observed.");
                }
                Assert.IsFalse(admitted.IsCancellationRequested);
                Assert.IsFalse(pending.IsCompleted);
                state.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.IsTrue(admitted.IsCancellationRequested);
                Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
            }

            Assert.IsNull(await pending.ConfigureAwait(false));
            Activity acquired = await requested.Task.ConfigureAwait(false);
            Assert.ContainsSingle(
                acquired.Events.Where(item => item.Name == "acquisition-succeeded")
            );

            using WorkAdmission open = new(
                state.Clock, state.Clock.GetUtcNow().AddMinutes(1), context.CancellationToken
            );
            DeliveryBatch claimed = RequireBatch(
                state.Operations.ClaimBatch("worker2", false, open)
            );
            Assert.AreEqual(1L, claimed.Id);
            Assert.AreEqual(1, claimed.Attempt);
            AssertMessage(claimed, Id(message), "Keep this attempt available.");
            state.Operations.CompleteBatch(claimed.Id);
            Assert.IsNull(state.Operations.ClaimBatch("worker2", false, open));
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            try
            {
                await admission.CloseAsync().ConfigureAwait(false);
                if (pending is not null)
                {
                    await pending.ConfigureAwait(false);
                }
            }
            catch (Exception secondary) when (primary is not null)
            {
                primary.Data["DeliveryClaimContenderCleanup"] = Json.ExceptionDetails(secondary);
            }
        }
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void InterruptedBatchReplaysSameSourceAndSuccessfulCompletionSurvivesRestart()
    {
        using BoardDeliveryState state = new();
        JsonObject message = state.Send(
            "worker1",
            "worker2",
            "Keep source identity across restart."
        );
        JsonObject gist = state.Publish(
            "worker1",
            "FACT",
            "cache.py line 8 def evict is stable",
            "Full trace"
        );
        DeliveryBatch interrupted = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        state.Restart();
        // An upstream replay of the same identity must not add another logical event.
        using (SqliteConnection connection = state.Database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            string payload = new JsonObject
            {
                ["messageId"] = Id(message),
                ["actor"] = "worker1",
                ["urgent"] = false,
                ["summary"] = "worker1 in dm:worker1:worker2: Keep source identity across restart.",
                ["message"] = message.DeepClone(),
            }.ToJsonString(Json.Options);
            state.Operations.EnqueueEvent(
                connection,
                transaction,
                "worker2",
                "dm",
                $"message:{Id(message)}",
                payload
            );
            transaction.Commit();
        }
        state.Operations.RecoverInterrupted();
        Assert.AreEqual(
            "interrupted",
            state.Text("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", interrupted.Id))
        );
        DeliveryBatch replay = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        CollectionAssert.AreEqual(interrupted.EventIds, replay.EventIds);
        Assert.AreEqual(2, replay.Attempt);
        Assert.AreEqual(interrupted.Prompt, replay.Prompt);
        AssertMessage(replay, Id(message), "Keep source identity across restart.");
        Assert.Contains(
            Id(gist),
            JsonNode.Parse(replay.Prompt)!["events"]!
                .AsArray()
                .OfType<JsonObject>()
                .Where(item => item["kind"]!.GetValue<string>() == "board")
                .Select(item => item["payload"]!["gistId"]!.GetValue<long>())
        );
        state.Operations.CompleteBatch(replay.Id);
        state.Operations.CompleteBatch(replay.Id);
        state.Restart();
        state.Operations.RecoverInterrupted();
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.IsEmpty(state.Operations.ReadSharedContext("worker2")["board"]!.AsArray());
        Assert.ContainsSingle(
            Items(
                state.Invoke(
                    "worker2",
                    "message_read",
                    new() { ["channelId"] = message["channelId"]!.GetValue<string>() }
                )
            )
        );
    }

    [TestMethod]
    [TestCategory("NoticeLeaseShowAndAck")]
    public void IncrementalBatchRetainsFreshGistOutsideSelectedEvents()
    {
        using BoardDeliveryState state = new();
        List<long> messages = [];
        for (int index = 0; index < 32; index++)
        {
            messages.Add(
                Id(state.Send("worker1", "worker2", $"Earlier direct notice {index:D2}"))
            );
        }
        JsonObject gist = state.Publish("worker1", "FACT", "Fresh peer finding after the backlog");

        DeliveryBatch batch = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        JsonObject prompt = JsonNode.Parse(batch.Prompt)!.AsObject();
        JsonObject[] events = prompt["events"]!.AsArray().OfType<JsonObject>().ToArray();
        CollectionAssert.AreEqual(
            messages.ToArray(),
            events
                .Where(item => item["kind"]!.GetValue<string>() == "dm")
                .Select(item => item["payload"]!["messageId"]!.GetValue<long>())
                .ToArray()
        );
        Assert.AreEqual(
            Id(gist),
            Id(
                Assert.ContainsSingle(
                    prompt["sharedContext"]!["board"]!.AsArray().OfType<JsonObject>()
                )
            )
        );
        Assert.DoesNotContain(
            Id(gist),
            events
                .Where(item => item["kind"]!.GetValue<string>() == "board")
                .Select(item => item["payload"]!["gist"]!["id"]!.GetValue<long>())
        );

        state.Operations.CompleteBatch(batch.Id);
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));

        JsonObject lateMessage = state.Send("worker1", "worker2", "New notice after the turn");
        JsonObject notice = Assert.ContainsSingle(
            NoticeItems(state.Operations.AddNotices("worker2", new()))
        );
        Assert.AreEqual("dm", notice["kind"]!.GetValue<string>());
        Assert.AreEqual(Id(lateMessage), notice["data"]!["messageId"]!.GetValue<long>());
    }

    [TestMethod]
    [TestCategory("NoticeLeaseShowAndAck")]
    public void ReadSnapshotTurnLeaseToolShowAndTurnAcknowledgementRemainDistinct()
    {
        using BoardDeliveryState state = new();
        JsonObject firstMessage = state.Send("worker1", "worker2", "First direct notice");
        JsonObject firstGist = state.Publish(
            "worker1",
            "FACT",
            "First peer finding",
            "Expand first detail"
        );
        JsonObject snapshot = state.Operations.ReadSharedContext("worker2");
        Assert.AreEqual(
            Id(firstMessage),
            Id(
                Assert.ContainsSingle(
                    snapshot["messages"]!.AsArray().OfType<JsonObject>().ToArray()
                )
            )
        );
        Assert.AreEqual(
            Id(firstGist),
            Id(Assert.ContainsSingle(snapshot["board"]!.AsArray().OfType<JsonObject>().ToArray()))
        );

        DeliveryBatch turn = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        AssertMessage(turn, Id(firstMessage), "First direct notice");
        Assert.AreEqual(
            Id(firstGist),
            Id(
                Assert.ContainsSingle(
                    state.Operations.ReadSharedContext("worker2")["board"]!
                        .AsArray()
                        .OfType<JsonObject>()
                )
            )
        );
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));

        JsonObject lateMessage = state.Send("worker1", "worker2", "Arrived after snapshot");
        JsonObject lateGist = state.Publish(
            "worker1",
            "OBSERVED",
            "Late peer finding",
            "Expand late detail"
        );
        JsonObject envelope = state.Operations.AddNotices(
            "worker2",
            new() { ["answer"] = "Tool result" }
        );
        Assert.AreEqual("Tool result", envelope["answer"]!.GetValue<string>());
        JsonObject[] notices = NoticeItems(envelope);
        Assert.HasCount(2, notices);
        Assert.AreEqual("dm", notices[0]["kind"]!.GetValue<string>());
        Assert.AreEqual(Id(lateMessage), notices[0]["data"]!["messageId"]!.GetValue<long>());
        Assert.AreEqual(
            "Arrived after snapshot",
            notices[0]["data"]!["message"]!["body"]!.GetValue<string>()
        );
        Assert.AreEqual(
            "g" + Id(lateGist).ToString(CultureInfo.InvariantCulture),
            notices[1]["id"]!.GetValue<string>()
        );
        Assert.AreEqual("Late peer finding", notices[1]["data"]!["content"]!.GetValue<string>());
        Assert.IsTrue(notices[1]["data"]!["hasDetail"]!.GetValue<bool>());
        Assert.IsNull(notices[1]["data"]!["detail"]);
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(
            Id(firstGist),
            Id(
                Assert.ContainsSingle(
                    state.Operations.ReadSharedContext("worker2")["board"]!
                        .AsArray()
                        .OfType<JsonObject>()
                )
            )
        );

        state.Operations.CompleteBatch(turn.Id);
        Assert.IsEmpty(state.Operations.ReadSharedContext("worker2")["board"]!.AsArray());
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        state.Restart();
        state.Operations.CompleteBatch(turn.Id);
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.IsEmpty(state.Operations.ReadSharedContext("worker2")["board"]!.AsArray());
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void DefaultClaimExpiresAtOneHourWithoutDeliveryConsumptionMaskingTheBoundary()
    {
        // Independent instances keep the same recipient's delivery unconsumed at every observation.
        foreach (int offsetMilliseconds in new[] { -1, 0, 1 })
        {
            using BoardDeliveryState state = new();
            JsonObject task = state.Invoke(
                "owner",
                "task_write",
                new()
                {
                    ["action"] = "create",
                    ["title"] = "Inspect default expiry",
                    ["body"] = "Keep the claim advisory.",
                }
            );
            DateTimeOffset publishedAt = state.Database.Now;
            JsonObject claim = state.Publish(
                "worker1",
                "CLAIM",
                $"Default expiry witness #{Id(task)}",
                "Historical default claim evidence."
            );
            long claimId = Id(claim);

            state.Clock.Advance(
                TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(offsetMilliseconds)
            );

            Assert.AreEqual(
                publishedAt + TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(offsetMilliseconds),
                state.Database.Now
            );
            JsonObject currentTask = state.Invoke(
                "worker2",
                "task_read",
                new() { ["id"] = Id(task) }
            );
            Assert.AreEqual("open", currentTask["state"]!.GetValue<string>());
            string[] assignees = currentTask["assignees"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            string[] expectedAssignees = offsetMilliseconds < 0 ? ["worker1"] : [];
            CollectionAssert.AreEqual(
                expectedAssignees,
                assignees,
                $"Active assignment at offset {offsetMilliseconds}ms."
            );
            long[] expectedRecent = offsetMilliseconds <= 0 ? [claimId] : [];
            CollectionAssert.AreEqual(
                expectedRecent,
                Ids(state.Invoke("owner", "board_recent", new())),
                $"Recent visibility at offset {offsetMilliseconds}ms."
            );

            DeliveryBatch delivery = RequireBatch(state.Operations.ClaimBatch("worker2", false));
            JsonObject deliveryPrompt = JsonNode.Parse(delivery.Prompt)!.AsObject();
            long[] deliveredGists = deliveryPrompt["events"]!
                .AsArray()
                .OfType<JsonObject>()
                .Where(item => item["kind"]!.GetValue<string>() == "board")
                .Select(item => item["payload"]!["gistId"]!.GetValue<long>())
                .ToArray();
            CollectionAssert.AreEqual(
                expectedRecent,
                deliveredGists,
                $"Unconsumed delivery eligibility at offset {offsetMilliseconds}ms."
            );
            Assert.IsEmpty(
                deliveryPrompt["sharedContext"]!["board"]!.AsArray(),
                $"Board prefix at offset {offsetMilliseconds}ms."
            );
            JsonObject historical = Assert.ContainsSingle(
                Items(
                    state.Invoke(
                        "owner",
                        "board_search",
                        new() { ["query"] = "Default expiry witness" }
                    )
                )
            );
            Assert.AreEqual(claimId, Id(historical));
            JsonObject detail = Assert.ContainsSingle(
                Items(
                    state.Invoke(
                        "owner",
                        "board_expand",
                        new() { ["labels"] = new JsonArray(claim["label"]!.GetValue<string>()) }
                    )
                )
            );
            Assert.AreEqual(claimId, Id(detail));
            Assert.AreEqual(
                "Historical default claim evidence.",
                detail["detail"]!.GetValue<string>()
            );
        }
    }

    [TestMethod]
    [DataRow("CLAIM", 0)]
    [DataRow("CLAIM", -1)]
    [DataRow("FACT", 10)]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void InvalidClaimTtlLeavesPriorBoardEventsAndNoticeStateUnchanged(string type, int ttl)
    {
        using BoardDeliveryState state = new();
        JsonObject prior = state.Publish(
            "worker1",
            "FACT",
            "Retained valid TTL control",
            "Original board evidence."
        );
        state.Send("owner", "worker2", "Existing notice before rejected TTL");
        Assert.IsNotEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        string before = PublicCollaborationSnapshot(state);

        JsonObject failure = state.Operations.Invoke(
            "worker1",
            "board_publish",
            new()
            {
                ["type"] = type,
                ["content"] = "Otherwise admissible TTL witness",
                ["ttlSeconds"] = ttl,
            }
        );

        Assert.IsFalse(failure["ok"]!.GetValue<bool>());
        Assert.AreEqual("invalid_arguments", failure["code"]!.GetValue<string>());
        Assert.IsNull(failure["data"]);
        Assert.AreEqual(before, PublicCollaborationSnapshot(state));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.IsNull(state.Operations.ClaimBatch("worker1", false));
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        JsonObject retained = Assert.ContainsSingle(
            Items(
                state.Invoke(
                    "owner",
                    "board_search",
                    new() { ["query"] = "Retained valid TTL control" }
                )
            )
        );
        Assert.AreEqual(Id(prior), Id(retained));
        Assert.AreEqual("Original board evidence.", retained["detail"]!.GetValue<string>());
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void OrdinaryThirdWorkerCannotReadSearchOrOpenAnotherPairsDirectThread()
    {
        using BoardDeliveryState state = new(workerCount: 3);
        Assert.AreEqual(
            "worker3",
            state.Invoke("owner", "agent_info", new() { ["id"] = "worker3" })[
                "id"
            ]!.GetValue<string>()
        );
        JsonObject team = state.Send("worker1", "team", "Privacy witness team");
        JsonObject direct = state.Send("worker1", "worker2", "Privacy witness direct");
        JsonObject reply = state.Invoke(
            "worker2",
            "message_send",
            new()
            {
                ["recipient"] = "worker1",
                ["body"] = "Privacy witness reply",
                ["threadId"] = Id(direct),
            }
        );
        Assert.AreEqual(Id(direct), reply["threadId"]!.GetValue<long>());
        string channel = direct["channelId"]!.GetValue<string>();
        string before = PublicCollaborationSnapshot(state);

        CollectionAssert.AreEqual(
            new[] { Id(team) },
            Ids(state.Invoke("worker3", "message_read", new() { ["channelId"] = "team" }))
        );
        JsonObject[] visible = Items(
            state.Invoke("worker3", "message_search", new() { ["query"] = "Privacy witness" })
        );
        CollectionAssert.AreEqual(new[] { Id(team) }, visible.Select(Id).ToArray());
        Assert.AreEqual(
            "Privacy witness team",
            Assert.ContainsSingle(visible)["body"]!.GetValue<string>()
        );
        foreach (
            (string operation, JsonObject arguments) in new (string, JsonObject)[]
            {
                ("message_read", new() { ["channelId"] = channel }),
                ("message_thread", new() { ["threadId"] = Id(direct) }),
            }
        )
        {
            JsonObject failure = state.Operations.Invoke("worker3", operation, arguments);
            Assert.IsFalse(failure["ok"]!.GetValue<bool>());
            Assert.AreEqual("not_visible", failure["code"]!.GetValue<string>());
            Assert.IsNull(failure["data"]);
        }
        JsonObject[] ownerSearch = Items(
            state.Invoke("owner", "message_search", new() { ["query"] = "Privacy witness" })
        );
        CollectionAssert.AreEqual(
            new[] { Id(team), Id(direct), Id(reply) },
            ownerSearch.Select(Id).ToArray()
        );
        string[] expectedBodies =
        [
            "Privacy witness team",
            "Privacy witness direct",
            "Privacy witness reply",
        ];
        CollectionAssert.AreEqual(
            expectedBodies,
            ownerSearch.Select(item => item["body"]!.GetValue<string>()).ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { Id(direct), Id(reply) },
            Ids(state.Invoke("owner", "message_read", new() { ["channelId"] = channel }))
        );
        CollectionAssert.AreEqual(
            new[] { Id(direct), Id(reply) },
            Ids(state.Invoke("owner", "message_thread", new() { ["threadId"] = Id(direct) }))
        );
        Assert.AreEqual(before, PublicCollaborationSnapshot(state));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker3", new())));
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void ActualLimitOneContinuationExcludesPriorIdsAndEndsWithoutOmissionOrRepetition()
    {
        using BoardDeliveryState state = new();
        long[] expected =
        [
            Id(state.Publish("worker1", "FACT", "Pagination witness alpha")),
            Id(state.Publish("worker2", "OBSERVED", "Pagination witness beta")),
            Id(state.Publish("worker1", "FACT", "Pagination witness gamma")),
        ];
        string before = PublicCollaborationSnapshot(state);
        List<long> returned = [];
        long cursor = 0;
        for (int pageIndex = 0; pageIndex < expected.Length; pageIndex++)
        {
            JsonObject page = state.Invoke(
                "worker2",
                "board_search",
                new()
                {
                    ["query"] = "Pagination witness",
                    ["limit"] = 1,
                    ["cursor"] = cursor,
                }
            );
            JsonObject item = Assert.ContainsSingle(Items(page));
            long id = Id(item);
            Assert.AreEqual(expected[pageIndex], id);
            Assert.IsGreaterThan(cursor, id);
            Assert.DoesNotContain(id, returned);
            returned.Add(id);
            if (pageIndex == expected.Length - 1)
            {
                Assert.IsNull(page["nextCursor"]);
            }
            else
            {
                long next = page["nextCursor"]!.GetValue<long>();
                Assert.AreEqual(id, next);
                cursor = next;
            }
        }
        CollectionAssert.AreEqual(expected, returned.ToArray());
        Assert.AreEqual(before, PublicCollaborationSnapshot(state));
    }

    [TestMethod]
    public async Task WorkSearchContinuationRetainsTaskAndChangeWithTheSameNumericId()
    {
        CancellationToken token = context.CancellationToken;
        using GitScenarioState state = await GitScenarioState.CreateAsync(token)
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        const string query = "Mixed work pagination witness";
        JsonObject task = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "owner",
                "task_write",
                new() { ["action"] = "create", ["title"] = query, ["body"] = "Open task." }
            )
        );
        _ = await state
            .InvokeAsync(
                "worker1",
                "change_write",
                new()
                {
                    ["action"] = "submit",
                    ["title"] = "Unrelated change",
                    ["body"] = "First change increments change sequence.",
                },
                token
            )
            .ConfigureAwait(false);
        JsonObject change = await state
            .InvokeAsync(
                "worker1",
                "change_write",
                new()
                {
                    ["action"] = "submit",
                    ["title"] = query,
                    ["body"] = "Open change.",
                },
                token
            )
            .ConfigureAwait(false);
        long id = Id(task);
        Assert.AreEqual(id, Id(change));

        JsonObject first = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "worker2",
                "work_search",
                new() { ["query"] = query, ["limit"] = 1 }
            )
        );
        JsonObject firstItem = Assert.ContainsSingle(Items(first));
        Assert.IsNotNull(first["nextCursor"]);
        JsonObject second = GitScenarioState.Data(
            state.Collaboration.Invoke(
                "worker2",
                "work_search",
                new()
                {
                    ["query"] = query,
                    ["limit"] = 1,
                    ["cursor"] = first["nextCursor"]!.GetValue<long>(),
                }
            )
        );
        JsonObject secondItem = Assert.ContainsSingle(Items(second));
        Assert.AreEqual(id, Id(firstItem));
        Assert.AreEqual(id, Id(secondItem));
        HashSet<string> kinds =
        [
            firstItem["kind"]!.GetValue<string>(),
            secondItem["kind"]!.GetValue<string>(),
        ];
        Assert.HasCount(2, kinds);
        Assert.Contains("task", kinds);
        Assert.Contains("change", kinds);
        Assert.IsNull(second["nextCursor"]);
    }

    [TestMethod]
    [DataRow("cursor")]
    [DataRow("limit")]
    [DataRow("id")]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void InvalidNumericOperationInputCannotMutateGenuinePriorState(string scenario)
    {
        using BoardDeliveryState state = new();
        JsonObject task = state.Invoke(
            "owner",
            "task_write",
            new()
            {
                ["action"] = "create",
                ["title"] = "Valid numeric identity",
                ["body"] = "Reject only malformed input.",
            }
        );
        Assert.AreEqual(
            Id(task),
            Id(state.Invoke("worker1", "task_read", new() { ["id"] = Id(task) }))
        );
        JsonObject gist = state.Publish("worker1", "FACT", "Numeric guard witness");
        Assert.AreEqual(
            Id(gist),
            Id(
                Assert.ContainsSingle(
                    Items(
                        state.Invoke(
                            "worker2",
                            "board_search",
                            new()
                            {
                                ["query"] = "Numeric guard witness",
                                ["cursor"] = 0,
                                ["limit"] = 1,
                            }
                        )
                    )
                )
            )
        );
        string before = PublicCollaborationSnapshot(state);
        string operation = "board_search";
        JsonObject arguments = new()
        {
            ["query"] = "Numeric guard witness",
            ["cursor"] = 0,
            ["limit"] = 1,
        };
        if (scenario == "id")
        {
            operation = "task_read";
            arguments = new() { ["id"] = Id(task).ToString(CultureInfo.InvariantCulture) };
        }
        else
        {
            arguments[scenario] = -1;
        }

        JsonObject failure = state.Operations.Invoke("worker1", operation, arguments);

        Assert.IsFalse(failure["ok"]!.GetValue<bool>());
        Assert.AreEqual("invalid_arguments", failure["code"]!.GetValue<string>());
        Assert.IsNull(failure["data"]);
        Assert.AreEqual(before, PublicCollaborationSnapshot(state));
        Assert.AreEqual(
            "Valid numeric identity",
            state.Invoke("worker1", "task_read", new() { ["id"] = Id(task) })[
                "title"
            ]!.GetValue<string>()
        );
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void KnownNonparticipantMentionCannotWidenAnOtherwiseValidDirectThread()
    {
        using BoardDeliveryState state = new(workerCount: 3);
        Assert.AreEqual(
            "worker3",
            state.Invoke("owner", "agent_info", new() { ["id"] = "worker3" })[
                "id"
            ]!.GetValue<string>()
        );
        JsonObject direct = state.Send("worker1", "worker2", "Existing direct mention root");
        JsonObject reply = state.Invoke(
            "worker2",
            "message_send",
            new()
            {
                ["recipient"] = "worker1",
                ["body"] = "Existing same-channel reply",
                ["threadId"] = Id(direct),
            }
        );
        CollectionAssert.AreEqual(
            new[] { Id(direct), Id(reply) },
            Ids(state.Invoke("worker1", "message_thread", new() { ["threadId"] = Id(direct) }))
        );
        Assert.IsNotEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        string before = PublicCollaborationSnapshot(state);

        JsonObject failure = state.Operations.Invoke(
            "worker1",
            "message_send",
            new()
            {
                ["recipient"] = "worker2",
                ["body"] = "Must reject known nonparticipant mention",
                ["threadId"] = Id(direct),
                ["mentions"] = new JsonArray("worker3"),
            }
        );

        Assert.IsFalse(failure["ok"]!.GetValue<bool>());
        Assert.AreEqual("not_visible", failure["code"]!.GetValue<string>());
        Assert.IsNull(failure["data"]);
        Assert.AreEqual(before, PublicCollaborationSnapshot(state));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker3", new())));
        Assert.IsNull(state.Operations.ClaimBatch("worker3", false));
        JsonObject[] retained = Items(
            state.Invoke("owner", "message_thread", new() { ["threadId"] = Id(direct) })
        );
        CollectionAssert.AreEqual(new[] { Id(direct), Id(reply) }, retained.Select(Id).ToArray());
        string[] expectedBodies = ["Existing direct mention root", "Existing same-channel reply"];
        CollectionAssert.AreEqual(
            expectedBodies,
            retained.Select(item => item["body"]!.GetValue<string>()).ToArray()
        );
    }

    private static string PublicCollaborationSnapshot(BoardDeliveryState state)
    {
        JsonObject channels = state.Invoke("owner", "channel_list", new());
        JsonObject messages = new();
        foreach (JsonObject channel in Items(channels))
        {
            string id = channel["id"]!.GetValue<string>();
            messages[id] = state
                .Invoke("owner", "message_read", new() { ["channelId"] = id })
                .DeepClone();
        }

        JsonObject contexts = new();
        foreach (string member in state.Database.Members)
        {
            JsonObject snapshot = state.Operations.ReadSharedContext(member);
            snapshot.Remove("snapshotAt");
            contexts[member] = snapshot;
        }

        return new JsonObject
        {
            ["tasks"] = state.Invoke("owner", "task_list", new() { ["limit"] = 200 }).DeepClone(),
            ["recentBoard"] = state
                .Invoke("owner", "board_recent", new() { ["limit"] = 200 })
                .DeepClone(),
            ["messages"] = messages,
            ["sharedContexts"] = contexts,
        }.ToJsonString();
    }

    private static long Id(JsonObject item) => item["id"]!.GetValue<long>();

    private static JsonObject[] Items(JsonObject result) =>
        result["items"]!.AsArray().OfType<JsonObject>().ToArray();

    private static long[] Ids(JsonObject result) => Items(result).Select(Id).ToArray();

    private static JsonObject[] NoticeItems(JsonObject result) =>
        result["notices"]!["items"]!.AsArray().OfType<JsonObject>().ToArray();

    private static DeliveryBatch RequireBatch(DeliveryBatch? batch)
    {
        Assert.IsNotNull(batch);
        return batch;
    }

    private static void AssertMessage(DeliveryBatch batch, long messageId, string body)
    {
        JsonObject prompt = JsonNode.Parse(batch.Prompt)!.AsObject();
        JsonObject messageEvent = prompt["events"]!
            .AsArray()
            .OfType<JsonObject>()
            .Single(item => item["kind"]!.GetValue<string>() == "dm");
        Assert.AreEqual(messageId, messageEvent["payload"]!["messageId"]!.GetValue<long>());
        Assert.AreEqual(body, messageEvent["payload"]!["message"]!["body"]!.GetValue<string>());
        Assert.AreEqual("worker2", messageEvent["recipient"]!.GetValue<string>());
    }
}
