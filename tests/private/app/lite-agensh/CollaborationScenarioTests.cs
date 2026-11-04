using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class CollaborationScenarioTests(TestContext testContext)
{
    private static readonly string[] ThirdPartyMention = ["worker2"];

    [TestMethod]
    public void PeersMultipleClaimsRemainAdvisoryAndExpireWithoutClosingTask()
    {
        using RunFixture fixture = new();
        ScenarioClock clock = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration, clock);
        CollaborationOperations peers = new(database);
        JsonObject task = Scenario.Call(
            peers,
            "worker1",
            "task_write",
            new
            {
                action = "create",
                title = "Independent fix",
                body = "Preserve the other worker's changes.",
            }
        );
        long id = Scenario.Id(task);
        foreach (string worker in fixture.Configuration.WorkerIds)
        {
            Scenario.Call(
                peers,
                worker,
                "board_publish",
                new
                {
                    type = "CLAIM",
                    content = $"Working on #{id}",
                    ttlSeconds = 60,
                }
            );
        }

        JsonObject shared = Scenario.Call(peers, "worker2", "task_read", new { id });
        string[] claimants = shared["assignees"]!
            .AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();
        Assert.HasCount(2, claimants);
        Assert.Contains("worker1", claimants);
        Assert.Contains("worker2", claimants);
        JsonObject updated = Scenario.Call(
            peers,
            "worker2",
            "task_write",
            new
            {
                action = "update",
                id,
                title = "Both peers can coordinate",
            }
        );
        Assert.AreEqual(
            "Preserve the other worker's changes.",
            updated["body"]!.GetValue<string>()
        );
        Scenario.Call(
            peers,
            "worker1",
            "task_write",
            new
            {
                action = "comment",
                id,
                body = "Split the edits.",
            }
        );
        clock.Advance(TimeSpan.FromSeconds(60) - TimeSpan.FromMilliseconds(1));
        Assert.HasCount(
            2,
            Scenario.Call(peers, "owner", "task_read", new { id })["assignees"]!.AsArray()
        );
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsEmpty(
            Scenario.Call(peers, "owner", "task_read", new { id })["assignees"]!.AsArray()
        );
        Assert.HasCount(2, Scenario.Items(Scenario.Call(peers, "owner", "board_recent", new { })));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsEmpty(Scenario.Items(Scenario.Call(peers, "owner", "board_recent", new { })));

        JsonObject expired = Scenario.Call(
            new CollaborationOperations(new RunDatabase(fixture.StatePath, timeProvider: clock)),
            "worker1",
            "task_read",
            new { id }
        );
        Assert.IsEmpty(expired["assignees"]!.AsArray());
        Assert.AreEqual("open", expired["state"]!.GetValue<string>());
        Assert.AreEqual("Both peers can coordinate", expired["title"]!.GetValue<string>());
        Assert.AreEqual(
            "Split the edits.",
            Assert.ContainsSingle(expired["comments"]!.AsArray().OfType<JsonObject>())[
                "body"
            ]!.GetValue<string>()
        );
        Assert.HasCount(
            2,
            Scenario.Items(
                Scenario.Call(peers, "worker1", "board_search", new { query = $"#{id}" })
            )
        );
    }

    [TestMethod]
    public void MessagesTeamDirectAndThreadsRespectBoundActorAndPagination()
    {
        using RunFixture fixture = new();
        CollaborationOperations peers = new(fixture.CreateDatabase());
        JsonObject team = Scenario.Call(
            peers,
            "worker1",
            "message_send",
            new { recipient = "team", body = "shared decision" }
        );
        JsonObject root = Scenario.Call(
            peers,
            "owner",
            "message_send",
            new { recipient = "worker1", body = "private decision" }
        );
        long threadId = Scenario.Id(root);
        JsonObject reply = Scenario.Call(
            peers,
            "worker1",
            "message_send",
            new
            {
                recipient = "owner",
                body = "private reply",
                threadId,
            }
        );
        Assert.AreEqual("dm:owner:worker1", reply["channelId"]!.GetValue<string>());
        Assert.AreEqual(threadId, reply["threadId"]!.GetValue<long>());
        JsonObject page = Scenario.Call(
            peers,
            "worker1",
            "message_thread",
            new { threadId, limit = 1 }
        );
        Assert.AreEqual(threadId, Scenario.Id(Assert.ContainsSingle(Scenario.Items(page))));
        JsonObject next = Scenario.Call(
            peers,
            "worker1",
            "message_thread",
            new
            {
                threadId,
                cursor = page["nextCursor"]!.GetValue<long>(),
                limit = 1,
            }
        );
        Assert.AreEqual(
            "private reply",
            Assert.ContainsSingle(Scenario.Items(next))["body"]!.GetValue<string>()
        );
        Assert.IsNull(next["nextCursor"]);
        JsonObject[] visible = Scenario.Items(
            Scenario.Call(peers, "worker2", "message_search", new { query = "decision" })
        );
        Assert.AreEqual(Scenario.Id(team), Scenario.Id(Assert.ContainsSingle(visible)));
        Scenario.Failure(
            peers.Invoke("worker2", "message_thread", Json.SerializePayload(new { threadId })),
            "not_visible"
        );
        Scenario.Failure(
            peers.Invoke(
                "worker2",
                "message_read",
                Json.SerializePayload(new { channelId = "dm:owner:worker1" })
            ),
            "not_visible"
        );
        Scenario.Failure(
            peers.Invoke(
                "worker1",
                "message_send",
                Json.SerializePayload(
                    new
                    {
                        recipient = "owner",
                        body = "must not widen visibility",
                        mentions = ThirdPartyMention,
                    }
                )
            ),
            "not_visible"
        );
        Scenario.Failure(
            peers.Invoke(
                "worker1",
                "message_send",
                Json.SerializePayload(
                    new
                    {
                        recipient = "team",
                        body = "wrong destination",
                        threadId,
                    }
                )
            ),
            "invalid_arguments"
        );
        Assert.HasCount(
            2,
            Scenario.Items(
                Scenario.Call(peers, "owner", "message_search", new { query = "private" })
            )
        );
        Assert.AreEqual(
            "shared decision",
            Assert.ContainsSingle(
                Scenario.Items(
                    Scenario.Call(peers, "worker2", "message_read", new { channelId = "team" })
                )
            )["body"]!.GetValue<string>()
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ConcurrentDomainWritesAndEventsCommitTogetherOrRollbackTogether()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations peers = new(database);
        using (SqliteConnection connection = database.Open())
        {
            Sql.Execute(
                connection,
                """
                CREATE TRIGGER reject_recipient BEFORE INSERT ON events WHEN NEW.recipient='worker2'
                BEGIN SELECT RAISE(ABORT, 'injected event failure'); END;
                """
            );
        }

        Assert.ThrowsExactly<SqliteException>(() =>
            peers.Invoke(
                "worker1",
                "task_write",
                Json.SerializePayload(
                    new
                    {
                        action = "create",
                        title = "must rollback",
                        body = "not committed",
                    }
                )
            )
        );
        using (SqliteConnection connection = database.Open())
        {
            Assert.AreEqual(1L, Sql.Scalar(connection, "SELECT COUNT(*) FROM tasks;"));
            Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM events;"));
            Sql.Execute(connection, "DROP TRIGGER reject_recipient;");
        }

        Task<JsonObject>[] writers = Enumerable
            .Range(1, 3)
            .Select(index =>
                Task.Run(
                    () =>
                        Scenario.Call(
                            peers,
                            "worker1",
                            "task_write",
                            new
                            {
                                action = "create",
                                title = $"concurrent-{index}",
                                body = "committed with events",
                            }
                        ),
                    testContext.CancellationToken
                )
            )
            .ToArray();
        JsonObject[] tasks = await Task.WhenAll(writers);
        Assert.HasCount(3, tasks.Select(Scenario.Id).Distinct());
        CollaborationOperations restarted = new(new RunDatabase(fixture.StatePath));
        Assert.HasCount(
            3,
            Scenario.Items(
                Scenario.Call(restarted, "owner", "work_search", new { query = "concurrent-" })
            )
        );
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(6L, Sql.Scalar(retained, "SELECT COUNT(*) FROM events WHERE kind='task';"));
        Assert.AreEqual(
            0L,
            Sql.Scalar(
                retained,
                """
                SELECT COUNT(*) FROM events e WHERE NOT EXISTS(
                    SELECT 1 FROM tasks t WHERE t.id=json_extract(e.payload,'$.taskId'));
                """
            )
        );
    }

    [TestMethod]
    public void DeliveryFailuresRespectRetryScheduleAndFiveAttemptBound()
    {
        using RunFixture fixture = new();
        ScenarioClock clock = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration, clock);
        CollaborationOperations peers = new(database);
        peers.QueueKickoff("worker1", "perform the first independent change");
        peers.QueueKickoff("worker2", "perform the second independent change");
        DeliveryBatch batch = peers.ClaimBatch("worker1")!;
        Assert.IsNotNull(batch);
        Assert.IsNull(peers.ClaimBatch("worker1"));
        DeliveryBatch other = peers.ClaimBatch("worker2")!;
        Assert.IsNotNull(other);
        Assert.Contains("second independent change", other.Prompt);
        peers.CompleteBatch(other.Id);
        int[] delays = [1, 5, 30, 120];
        long eventId = Assert.ContainsSingle(batch.EventIds);
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Assert.AreEqual(attempt, batch.Attempt);
            Assert.AreEqual(eventId, Assert.ContainsSingle(batch.EventIds));
            peers.FailBatch(batch.Id, $"scripted turn failure {attempt}");
            if (attempt < 5)
            {
                clock.Advance(TimeSpan.FromMilliseconds(delays[attempt - 1] * 1000 - 1));
                Assert.IsNull(peers.ClaimBatch("worker1"));
                clock.Advance(TimeSpan.FromMilliseconds(1));
                batch = peers.ClaimBatch("worker1")!;
                Assert.IsNotNull(batch);
            }
        }

        clock.Advance(TimeSpan.FromDays(1));
        Assert.IsNull(
            new CollaborationOperations(
                new RunDatabase(fixture.StatePath, timeProvider: clock)
            ).ClaimBatch("worker1")
        );
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            "failed",
            Sql.Scalar(retained, "SELECT state FROM events WHERE id=$id;", null, ("$id", eventId))
        );
        Assert.AreEqual(
            5L,
            Sql.Scalar(
                retained,
                "SELECT attempts FROM events WHERE id=$id;",
                null,
                ("$id", eventId)
            )
        );
        Assert.IsNull(
            Sql.Scalar(retained, "SELECT sent_at FROM events WHERE id=$id;", null, ("$id", eventId))
        );
    }

    [TestMethod]
    public void DeliveryRestartDedupAndInterruptedSendingRequireCompletedAcknowledgement()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations peers = new(database);
        Enqueue(peers, database);
        DeliveryBatch interrupted = peers.ClaimBatch("worker1")!;
        Assert.IsNotNull(interrupted);
        CollaborationOperations restarted = new(new RunDatabase(fixture.StatePath));
        restarted.RecoverInterrupted();
        Enqueue(restarted, database);
        DeliveryBatch replay = restarted.ClaimBatch("worker1")!;
        Assert.IsNotNull(replay);
        Assert.AreNotEqual(interrupted.Id, replay.Id);
        Assert.AreEqual(2, replay.Attempt);
        Assert.IsTrue(interrupted.EventIds.SequenceEqual(replay.EventIds));
        using (SqliteConnection connection = database.Open())
        {
            Assert.AreEqual(
                "sending",
                Sql.Scalar(connection, "SELECT state FROM events WHERE dedup_key='stable-event';")
            );
            Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM shown_notices;"));
        }

        restarted.CompleteBatch(replay.Id);
        restarted.CompleteBatch(replay.Id);
        Enqueue(new CollaborationOperations(new RunDatabase(fixture.StatePath)), database);
        Assert.IsNull(restarted.ClaimBatch("worker1"));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            1L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM events WHERE dedup_key='stable-event';")
        );
        Assert.AreEqual(
            "sent",
            Sql.Scalar(retained, "SELECT state FROM events WHERE dedup_key='stable-event';")
        );
        Assert.AreEqual(
            1L,
            Sql.Scalar(retained, "SELECT COUNT(*) FROM shown_notices WHERE recipient='worker1';")
        );
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ToolBoundaryNoticesHonorLeaseCursorAndShownReconciliation()
    {
        using RunFixture fixture = new();
        ScenarioClock clock = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration, clock);
        LocalTools tools = new(database);
        JsonObject gist = Scenario.Call(
            tools.Collaboration,
            "worker2",
            "board_publish",
            new
            {
                type = "FACT",
                content = "A peer's useful verified finding",
                detail = "Do not repeat the experiment.",
            }
        );
        Scenario.Call(
            tools.Collaboration,
            "worker2",
            "message_send",
            new { recipient = "worker1", body = "coordinate before editing" }
        );
        JsonObject snapshot = tools.Collaboration.ReadSharedContext("worker1");
        Assert.AreEqual(
            "coordinate before editing",
            Assert.ContainsSingle(snapshot["messages"]!.AsArray().OfType<JsonObject>())[
                "body"
            ]!.GetValue<string>()
        );
        using (SqliteConnection connection = database.Open())
        {
            Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM shown_notices;"));
            Sql.Execute(
                connection,
                "INSERT INTO notice_leases VALUES('worker1','held',$expiry);",
                null,
                ("$expiry", clock.GetUtcNow().AddSeconds(1).ToUnixTimeMilliseconds())
            );
        }

        JsonObject held = await tools.InvokeAsync(
            "worker1",
            "task_read",
            Json.SerializePayload(new { id = 1 }),
            testContext.CancellationToken
        );
        Assert.IsEmpty(held["notices"]!["items"]!.AsArray());
        clock.Advance(TimeSpan.FromSeconds(1));
        JsonObject shown = await tools.InvokeAsync(
            "worker1",
            "task_read",
            Json.SerializePayload(new { id = 1 }),
            testContext.CancellationToken
        );
        Scenario.Data(shown);
        JsonObject[] notices = shown["notices"]!["items"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.HasCount(2, notices);
        Assert.IsTrue(notices.Any(notice => notice["kind"]!.GetValue<string>() == "dm"));
        JsonObject board = Assert.ContainsSingle(
            notices.Where(notice => notice["kind"]!.GetValue<string>() == "board")
        );
        Assert.AreEqual(Scenario.Id(gist), board["data"]!["id"]!.GetValue<long>());
        Assert.IsFalse(board["data"]!.AsObject().ContainsKey("detail"));
        JsonObject again = await tools.InvokeAsync(
            "worker1",
            "task_read",
            Json.SerializePayload(new { id = 1 }),
            testContext.CancellationToken
        );
        Assert.IsEmpty(again["notices"]!["items"]!.AsArray());
        Assert.IsNull(tools.Collaboration.ClaimBatch("worker1"));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            Scenario.Id(gist),
            Sql.Scalar(retained, "SELECT gist_id FROM notice_cursors WHERE recipient='worker1';")
        );
        Assert.AreEqual(
            2L,
            Sql.Scalar(
                retained,
                "SELECT COUNT(*) FROM events WHERE recipient='worker1' AND state='pending';"
            )
        );
    }

    [TestMethod]
    [DataRow("worker1", true)]
    [DataRow("owner", false)]
    public void RootClosureReopensOnlyWorkerStopSignals(string actor, bool reopened)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations peers = new(database);
        JsonObject closed = Scenario.Call(
            peers,
            actor,
            "task_write",
            new { action = "close", id = 1 }
        );
        Assert.AreEqual(actor, closed["closedBy"]!.GetValue<string>());

        Assert.AreEqual(reopened, peers.ReopenWorkerClosedRoot());

        JsonObject root = Scenario.Call(peers, "worker2", "task_read", new { id = 1 });
        Assert.AreEqual(reopened ? "open" : "closed", root["state"]!.GetValue<string>());
        Assert.AreEqual(actor, database.ReadMetadata("lifecycle:root_closed_by"));
        Assert.IsFalse(peers.ReopenWorkerClosedRoot());
    }

    private static void Enqueue(CollaborationOperations operations, RunDatabase database)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        operations.EnqueueEvent(
            connection,
            transaction,
            "worker1",
            "kickoff",
            "stable-event",
            """{"summary":"resume this retained task"}"""
        );
        transaction.Commit();
    }
}
