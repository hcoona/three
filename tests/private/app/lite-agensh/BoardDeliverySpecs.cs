using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class BoardDeliverySpecs
{
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
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM events WHERE dedup_key=$key;",
                ("$key", $"board:{Id(first)}")
            )
        );
        JsonObject live = state.Publish("worker1", "FACT", "Fresh cache finding");
        DeliveryBatch actionable = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        JsonObject[] boardEvents = JsonNode.Parse(actionable.Prompt)!["events"]!
            .AsArray()
            .OfType<JsonObject>()
            .Where(item => item["kind"]!.GetValue<string>() == "board")
            .ToArray();
        Assert.AreEqual(
            Id(live),
            Assert.ContainsSingle(boardEvents)["payload"]!["gistId"]!.GetValue<long>()
        );
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT COUNT(*) FROM batch_gists WHERE batch_id=$batch AND gist_id=$gist;",
                ("$batch", actionable.Id),
                ("$gist", Id(first))
            )
        );
        state.Operations.CompleteBatch(actionable.Id);
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT COUNT(*) FROM shown_gists WHERE recipient='worker2' AND gist_id=$gist;",
                ("$gist", Id(first))
            )
        );
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
        JsonObject collapsed = Items(state.Invoke("owner", "board_recent", new()))
            .Single(item => Id(item) == Id(fact));
        Assert.AreEqual(2, collapsed["duplicateCount"]!.GetValue<int>());
        Assert.AreEqual(6L, state.Count("SELECT COUNT(*) FROM board_history;"));
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
        Assert.AreEqual(12L, state.Count("SELECT COUNT(*) FROM events WHERE kind='board';"));
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void BooleanHistorySearchTreatsWildcardsAndSqlAsLiteralText()
    {
        using BoardDeliveryState state = new();
        JsonObject literal = state.Publish("worker1", "FACT", "cache 100% a_b ' OR 1=1 --");
        JsonObject other = state.Publish("worker2", "OBSERVED", "queue 100x axb");
        JsonObject both = state.Publish("worker1", "FACT", "cache queue");
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
        Assert.AreEqual(3L, state.Count("SELECT COUNT(*) FROM board_history;"));
        JsonObject rejected = state.Operations.Invoke(
            "worker1",
            "board_publish",
            new() { ["type"] = "FACT", ["content"] = "none" }
        );
        Assert.AreEqual("invalid_arguments", rejected["code"]!.GetValue<string>());
        Assert.AreEqual(6L, state.Count("SELECT COUNT(*) FROM events;"));
    }

    [TestMethod]
    [TestCategory("BoardLifecycleAndVisibility")]
    public void OverflowEvictsOldestOfTwoThousandRecentSlotsButNotItsHistory()
    {
        using BoardDeliveryState state = new();
        JsonObject oldest = state.Publish(
            "worker1",
            "FACT",
            "Oldest real admission",
            "Retained oldest evidence"
        );
        using (SqliteConnection connection = state.Database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            // E02: only pre-boundary history/recent pairs are arranged, in one transaction.
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
        Assert.AreEqual(2000L, state.Count("SELECT COUNT(*) FROM board_recent;"));
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM board_recent WHERE revision_id=$id;",
                ("$id", Id(oldest))
            )
        );
        Assert.AreEqual(2L, state.Count("SELECT COUNT(*) FROM events WHERE kind='board';"));
        JsonObject newest = state.Publish(
            "worker2",
            "FACT",
            "Newest real overflow admission",
            "Overflow evidence"
        );
        Assert.AreEqual(2000L, state.Count("SELECT COUNT(*) FROM board_recent;"));
        Assert.AreEqual(
            0L,
            state.Count(
                "SELECT COUNT(*) FROM board_recent WHERE revision_id=$id;",
                ("$id", Id(oldest))
            )
        );
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM board_recent WHERE revision_id=$id;",
                ("$id", Id(newest))
            )
        );
        Assert.AreEqual(2001L, state.Count("SELECT COUNT(*) FROM board_history;"));
        JsonObject recent = state.Invoke("owner", "board_recent", new() { ["limit"] = 200 });
        Assert.HasCount(200, Items(recent));
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
        Assert.AreEqual(2000L, state.Count("SELECT COUNT(*) FROM board_recent;"));
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
        // Only the two public admissions broadcast; arranged pairs are not admissions.
        Assert.AreEqual(4L, state.Count("SELECT COUNT(*) FROM events WHERE kind='board';"));
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
        Assert.AreEqual(2L, state.Count("SELECT COUNT(*) FROM messages;"));
        Assert.AreEqual(
            2L,
            state.Count(
                """
                SELECT COUNT(*) FROM messages m JOIN events e
                    ON json_extract(e.payload,'$.messageId')=m.id
                    AND e.dedup_key='message:'||m.id AND e.recipient=m.recipient
                    AND json_extract(e.payload,'$.message.body')=m.body
                WHERE e.kind='dm' AND e.state='pending';
                """
            )
        );
        JsonObject[] notices = NoticeItems(state.Operations.AddNotices("worker2", new()));
        CollectionAssert.AreEqual(
            sent.OrderBy(Id).Select(item => item["body"]!.GetValue<string>()).ToArray(),
            notices.Select(item => item["data"]!["message"]!["body"]!.GetValue<string>()).ToArray()
        );
        Assert.AreEqual(
            2L,
            state.Count("SELECT COUNT(*) FROM shown_notices WHERE recipient='worker2';")
        );
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker1", new())));
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void RejectedDomainTransactionLeavesNoMessageEventOrNotice()
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
        JsonObject[] rollbackRecords = Directory
            .EnumerateFiles(
                Path.Combine(state.Database.StatePath, "run.sqlite.diagnostics"),
                "activity-*.json"
            )
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject())
            .SelectMany(snapshot => snapshot["recent"]!.AsArray().OfType<JsonObject>())
            .Where(item =>
                item["actor"]?.GetValue<string>() == "worker1"
                && item["operation"]?.GetValue<string>() == "Invoke"
                && item["phase"]?.GetValue<string>() == "rollback-succeeded"
            )
            .ToArray();
        Assert.IsNotEmpty(
            rollbackRecords,
            "The rejected domain operation must use the real SQLite rollback path."
        );
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM messages;"));
        Assert.AreEqual(
            0L,
            state.Count("SELECT COUNT(*) FROM messages WHERE body='Must not escape';")
        );
        Assert.AreEqual(2L, state.Count("SELECT COUNT(*) FROM events;"));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));
        Assert.AreEqual(
            "Visible root",
            Assert.ContainsSingle(
                Items(state.Invoke("worker2", "message_read", new() { ["channelId"] = "team" }))
            )["body"]!.GetValue<string>()
        );
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void EventEnqueueFailureRollsBackItsCallersDomainWriteAndEarlierEvent()
    {
        using BoardDeliveryState state = new();
        // EnqueueEvent explicitly accepts its caller's domain transaction. These are
        // caller writes, not claimed task_write admissions or generated broadcasts.
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using SqliteConnection connection = state.Database.Open();
            using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
            Sql.Execute(
                connection,
                """
                INSERT INTO task_comments(task_id,actor,body,created_at)
                VALUES(1,'worker1','Tentative evidence',$now);
                """,
                transaction,
                ("$now", state.Database.Now.ToString("O", CultureInfo.InvariantCulture))
            );
            state.Operations.EnqueueEvent(
                connection,
                transaction,
                "worker2",
                "task",
                "comment:tentative",
                """{"taskId":1,"action":"comment","summary":"Tentative evidence","urgent":true}"""
            );
            state.Operations.EnqueueEvent(
                connection,
                transaction,
                "owner",
                "task",
                "comment:invalid",
                "[]"
            );
            transaction.Commit();
        });
        Assert.IsEmpty(
            state.Invoke("worker2", "task_read", new() { ["id"] = 1 })["comments"]!.AsArray()
        );
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM events;"));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));

        using (SqliteConnection connection = state.Database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            Sql.Execute(
                connection,
                """
                INSERT INTO task_comments(task_id,actor,body,created_at)
                VALUES(1,'worker1','Committed evidence',$now);
                """,
                transaction,
                ("$now", state.Database.Now.ToString("O", CultureInfo.InvariantCulture))
            );
            state.Operations.EnqueueEvent(
                connection,
                transaction,
                "worker2",
                "task",
                "comment:committed",
                """{"taskId":1,"action":"comment","summary":"Committed evidence","urgent":true}"""
            );
            transaction.Commit();
        }
        JsonObject comment = Assert.ContainsSingle(
            state.Invoke("worker2", "task_read", new() { ["id"] = 1 })["comments"]!
                .AsArray()
                .OfType<JsonObject>()
                .ToArray()
        );
        Assert.AreEqual("Committed evidence", comment["body"]!.GetValue<string>());
        JsonObject notice = Assert.ContainsSingle(
            NoticeItems(state.Operations.AddNotices("worker2", new()))
        );
        Assert.AreEqual("Committed evidence", notice["data"]!["summary"]!.GetValue<string>());
        Assert.AreEqual(1L, notice["data"]!["taskId"]!.GetValue<long>());
        Assert.AreEqual(
            1L,
            state.Count("SELECT COUNT(*) FROM events WHERE dedup_key='comment:committed';")
        );
        Assert.AreEqual(
            1L,
            state.Count("SELECT COUNT(*) FROM shown_notices WHERE recipient='worker2';")
        );
    }

    [TestMethod]
    [TestCategory("AtomicDeliveryRestartAndRetry")]
    public void FailedDeliveryUsesEveryRetryBoundaryAndFifthAttemptIsTerminal()
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
            state.Operations.FailBatch(batch.Id, "controlled delivery failure");
            Assert.AreEqual(
                "failed",
                state.Text("SELECT state FROM events WHERE id=$id;", ("$id", eventId))
            );
            Assert.AreEqual(
                (long)attempt,
                state.Count("SELECT attempts FROM events WHERE id=$id;", ("$id", eventId))
            );
            if (attempt < 5)
            {
                Assert.AreEqual(
                    state.Database.Now.ToUnixTimeMilliseconds() + delays[attempt - 1] * 1000L,
                    state.Count("SELECT next_retry_at FROM events WHERE id=$id;", ("$id", eventId))
                );
                state.Clock.Advance(
                    TimeSpan.FromSeconds(delays[attempt - 1]) - TimeSpan.FromMilliseconds(1)
                );
                Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
                state.Clock.Advance(TimeSpan.FromMilliseconds(1));
                batch = RequireBatch(state.Operations.ClaimBatch("worker2", false));
            }
        }
        Assert.AreEqual(
            0L,
            state.Count("SELECT COUNT(*) FROM events WHERE next_retry_at IS NOT NULL;")
        );
        state.Clock.Advance(TimeSpan.FromDays(1));
        state.Restart();
        state.Operations.RecoverInterrupted();
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM messages;"));
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
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));
        state.Restart();
        // An upstream replay of the same identity must not add another logical event.
        using (SqliteConnection connection = state.Database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            string payload = (string)
                Sql.Scalar(
                    connection,
                    "SELECT payload FROM events WHERE recipient='worker2' AND dedup_key=$key;",
                    transaction,
                    ("$key", $"message:{Id(message)}")
                )!;
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
        state.Operations.CompleteBatch(replay.Id);
        state.Operations.CompleteBatch(replay.Id);
        state.Restart();
        state.Operations.RecoverInterrupted();
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(
            2L,
            state.Count("SELECT COUNT(*) FROM shown_notices WHERE recipient='worker2';")
        );
        Assert.AreEqual(
            1L,
            state.Count(
                "SELECT COUNT(*) FROM shown_gists WHERE recipient='worker2' AND gist_id=$id;",
                ("$id", Id(gist))
            )
        );
        Assert.AreEqual(
            2L,
            state.Count("SELECT COUNT(*) FROM events WHERE recipient='worker2' AND state='sent';")
        );
        Assert.AreEqual(1L, state.Count("SELECT COUNT(*) FROM messages;"));
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
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM notice_cursors;"));
        DeliveryBatch turn = RequireBatch(state.Operations.ClaimBatch("worker2", false));
        AssertMessage(turn, Id(firstMessage), "First direct notice");
        Assert.AreEqual(
            1L,
            state.Count("SELECT COUNT(*) FROM batch_gists WHERE batch_id=$id;", ("$id", turn.Id))
        );
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_gists;"));
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
        Assert.AreEqual(
            Id(lateGist),
            state.Count("SELECT gist_id FROM notice_cursors WHERE recipient='worker2';")
        );
        Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM notice_leases;"));
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM events WHERE recipient='worker2' AND state='pending';"
            )
        );
        Assert.AreEqual(
            2L,
            state.Count("SELECT COUNT(*) FROM shown_notices WHERE recipient='worker2';")
        );
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));

        state.Operations.CompleteBatch(turn.Id);
        Assert.AreEqual(
            2L,
            state.Count("SELECT COUNT(*) FROM events WHERE recipient='worker2' AND state='sent';")
        );
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM events WHERE recipient='worker2' AND state='pending';"
            )
        );
        Assert.AreEqual(
            4L,
            state.Count("SELECT COUNT(*) FROM shown_notices WHERE recipient='worker2';")
        );
        Assert.AreEqual(
            Id(lateGist),
            state.Count("SELECT gist_id FROM notice_cursors WHERE recipient='worker2';")
        );
        state.Restart();
        state.Operations.CompleteBatch(turn.Id);
        Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
        Assert.IsEmpty(NoticeItems(state.Operations.AddNotices("worker2", new())));
        Assert.AreEqual(
            2L,
            state.Count(
                "SELECT COUNT(*) FROM events WHERE recipient='worker2' AND state='pending';"
            )
        );
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
            Assert.AreEqual(
                1L,
                state.Count(
                    (
                        "SELECT COUNT(*) FROM events WHERE recipient='worker2' AND "
                        + "kind='board' AND state='pending' AND "
                        + "json_extract(payload,'$.gistId')=$id;"
                    ),
                    ("$id", claimId)
                )
            );
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM delivery_batches;"));
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_gists;"));
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));

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
            long[] deliveredGists = JsonNode.Parse(delivery.Prompt)!["events"]!
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
            Assert.AreEqual(
                (long)expectedRecent.Length,
                state.Count(
                    "SELECT COUNT(*) FROM batch_gists WHERE batch_id=$batch AND gist_id=$gist;",
                    ("$batch", delivery.Id),
                    ("$gist", claimId)
                )
            );
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_gists;"));
            Assert.AreEqual(0L, state.Count("SELECT COUNT(*) FROM shown_notices;"));
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
        string before = CollaborationDomainAndNoticeRows(state);

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
        Assert.AreEqual(before, CollaborationDomainAndNoticeRows(state));
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
        string before = CollaborationDomainAndNoticeRows(state);

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
        Assert.AreEqual(before, CollaborationDomainAndNoticeRows(state));
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
        string before = CollaborationDomainAndNoticeRows(state);
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
        Assert.AreEqual(before, CollaborationDomainAndNoticeRows(state));
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
        string before = CollaborationDomainAndNoticeRows(state);
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
        Assert.AreEqual(before, CollaborationDomainAndNoticeRows(state));
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
        string before = CollaborationDomainAndNoticeRows(state);

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
        Assert.AreEqual(before, CollaborationDomainAndNoticeRows(state));
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

    private static string CollaborationDomainAndNoticeRows(BoardDeliveryState state)
    {
        using SqliteConnection connection = state.Database.Open();
        JsonObject result = new();
        // Application domain/delivery rows only; diagnostic rollback activity may legitimately
        // change.
        foreach (
            string table in new[]
            {
                "tasks",
                "task_comments",
                "messages",
                "board_history",
                "board_recent",
                "events",
                "delivery_batches",
                "batch_gists",
                "shown_notices",
                "shown_gists",
                "notice_cursors",
                "notice_leases",
            }
        )
        {
            result[table] = Phase6State.Rows(connection, $"SELECT * FROM {table} ORDER BY rowid;");
        }
        return result.ToJsonString();
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
