using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
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
        Phase7OwnedCreator creator = await Phase7OwnedCreator
            .OpenAsync(testContext, false, token)
            .ConfigureAwait(false);
        await creator
            .RunOwnedAsync(async () =>
            {
                Phase7Run state = Phase7Run.Attach(creator, token);
                await state
                    .RunBodyAsync(async () =>
                    {
                        string session = creator.SessionId,
                            reservation = creator.ReservationId;
                        string key = "native:bootstrap:" + session;
                        string journalBefore = Phase6OwnedCreator.Hash(creator.JournalPath);
                        Assert.AreEqual(
                            "true",
                            creator.Checkpoint["confirmation"]!.GetValue<string>()
                        );
                        Assert.AreEqual("true", state.Database.ReadMetadata(key));
                        Assert.AreEqual(
                            "sent",
                            state.Value(
                                "SELECT state FROM delivery_batches WHERE id=$id;",
                                ("$id", creator.Checkpoint["batch"]!.GetValue<long>())
                            )
                        );
                        Assert.AreEqual(10m, state.Reservation("worker1").Actual);
                        Assert.AreEqual(200m, state.Reservation("worker1").Allowance);
                        Assert.AreEqual(
                            creator.Checkpoint["clock"]!.GetValue<DateTimeOffset>(),
                            state.Clock.GetUtcNow()
                        );
                        await creator.CrashAsync(token).ConfigureAwait(false);
                        Assert.AreEqual(
                            1L,
                            state.Number(
                                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                            )
                        );
                        if (legacyMarkerAbsent)
                        {
                            // Intentional APPLICATION-key-only compatibility input derived
                            // from the genuine current producer, not old-binary history.
                            string OtherMetadata() =>
                                (string)
                                    state.Value(
                                        "SELECT group_concat(encoded,'|') FROM "
                                            + (
                                                "(SELECT hex(key)||':'||hex(value) AS encoded "
                                                + "FROM meta WHERE key<>$key ORDER BY key);"
                                            ),
                                        ("$key", key)
                                    )!;
                            string others = OtherMetadata();
                            long count = state.Number("SELECT COUNT(*) FROM meta;");
                            string financial = state
                                .Ledger.Status(
                                    state.Database.RunId,
                                    state.Database.Configuration.RunAiCreditAllowance
                                )
                                .ToJsonString();
                            using (var connection = state.Database.Open())
                            {
                                Sql.Execute(
                                    connection,
                                    "DELETE FROM meta WHERE key=$key;",
                                    null,
                                    ("$key", key)
                                );
                            }
                            Assert.AreEqual(count - 1, state.Number("SELECT COUNT(*) FROM meta;"));
                            Assert.AreEqual(others, OtherMetadata());
                            Assert.IsNull(state.Database.ReadMetadata(key));
                            Assert.AreEqual(
                                journalBefore,
                                Phase6OwnedCreator.Hash(creator.JournalPath)
                            );
                            Assert.AreEqual(
                                financial,
                                state
                                    .Ledger.Status(
                                        state.Database.RunId,
                                        state.Database.Configuration.RunAiCreditAllowance
                                    )
                                    .ToJsonString()
                            );
                            Console.WriteLine(
                                "PHASE7_LEGACY_APPLICATION_MARKER_DELTA "
                                    + new JsonObject
                                    {
                                        ["sessionId"] = session,
                                        ["removedKey"] = key,
                                        ["producerActuallyConfirmed"] = creator.Checkpoint[
                                            "confirmation"
                                        ]!.DeepClone(),
                                        ["onlyOtherMetadataUnchanged"] = true,
                                        ["originalJournalSha256"] = journalBefore,
                                        ["notAnOldBinaryOrInstalledNativeHistoryClaim"] = true,
                                    }.ToJsonString()
                            );
                        }
                        state.Execute(
                            (
                                "CREATE TABLE phase7_exact_ack(batch_id INTEGER PRIMARY "
                                + "KEY,session_id TEXT,marker TEXT);\nCREATE TRIGGER "
                                + "phase7_exact_ack_observer BEFORE UPDATE OF state ON "
                                + "delivery_batches\nWHEN OLD.state='sending' AND "
                                + "NEW.state='sent'\nBEGIN\n    INSERT INTO "
                                + "phase7_exact_ack(batch_id,session_id,marker)\n    SELECT "
                                + "NEW.id,session.value,confirmation.value\n    FROM meta session "
                                + "LEFT JOIN meta confirmation\n        ON "
                                + "confirmation.key='native:bootstrap:' || session.value\n    "
                                + "WHERE session.key='worker:' || NEW.recipient || ':session';\n"
                                + "END;"
                            )
                        );
                        JsonObject message = state.Invoke(
                            "owner",
                            "message_send",
                            new()
                            {
                                ["recipient"] = "worker1",
                                ["body"] =
                                    "Continue the actual interrupted decision, do not replay work.",
                                ["urgent"] = true,
                            }
                        );
                        _ = state.Start("reconnect");
                        Phase7Sessions.Turn next = await state
                            .FixedAsync(
                                nextToken => state.Sessions.NextTurnAsync("worker1", nextToken),
                                token
                            )
                            .ConfigureAwait(false);
                        long batch = state.SendingBatch("worker1");
                        Assert.AreEqual(session, next.Session.SessionId);
                        Assert.AreEqual(reservation, state.Reservation("worker1").Id);
                        Assert.AreEqual(
                            "exact-session-load",
                            state.Database.ReadMetadata("worker:worker1:recovery")
                        );
                        Assert.AreEqual(
                            creator.Checkpoint["workDeadline"]!.GetValue<DateTimeOffset>(),
                            state.WorkDeadline
                        );
                        Assert.AreEqual(
                            2L,
                            state.Number("SELECT COUNT(*) FROM coordinator_segments;")
                        );
                        Assert.AreEqual(
                            "crash-reconnect",
                            state.Value(
                                "SELECT stop_reason FROM coordinator_segments WHERE mode='run';"
                            )
                        );
                        Assert.AreEqual(
                            1L,
                            state.Number(
                                "SELECT COUNT(*) FROM coordinator_segments WHERE ended_at IS NULL;"
                            )
                        );
                        Assert.Contains(
                            "Continue the actual interrupted decision, do not replay work.",
                            next.Text
                        );
                        Assert.AreEqual(
                            legacyMarkerAbsent ? null : "true",
                            state.Database.ReadMetadata(key)
                        );
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
                        Phase7Journal journal = state.Sessions.Journal!;
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
                        Assert.AreEqual(
                            "sending",
                            state.Value(
                                "SELECT state FROM delivery_batches WHERE id=$id;",
                                ("$id", batch)
                            )
                        );
                        Assert.AreEqual(0L, state.Number("SELECT COUNT(*) FROM phase7_exact_ack;"));
                        Phase7Clock.ObservedPoll ownerPoll = await state
                            .ObserveOwnerPollAsync(token)
                            .ConfigureAwait(false);
                        next.Complete();
                        await state
                            .WaitAckAsync(batch, state.WorkDeadline, token)
                            .ConfigureAwait(false);
                        Assert.AreEqual(
                            "true",
                            state.Value(
                                "SELECT marker FROM phase7_exact_ack WHERE batch_id=$id;",
                                ("$id", batch)
                            )
                        );
                        Assert.AreEqual(
                            session,
                            state.Value(
                                "SELECT session_id FROM phase7_exact_ack WHERE batch_id=$id;",
                                ("$id", batch)
                            )
                        );
                        Assert.AreEqual("true", state.Database.ReadMetadata(key));
                        Assert.AreEqual(2L, state.Dispatches(reservation));
                        Assert.AreEqual(
                            2L,
                            journal.Snapshot(session)["requests"]!.GetValue<long>()
                        );
                        Assert.IsFalse(journal.Snapshot(session)["pending"]!.GetValue<bool>());
                        Assert.AreEqual(
                            1L,
                            state.Number(
                                (
                                    "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                                    + "e.id=s.event_id "
                                )
                                    + (
                                        "WHERE e.batch_id=$batch AND e.kind='dm' AND "
                                        + "json_extract(e.payload,'$.message.id')=$message;"
                                    ),
                                ("$batch", batch),
                                ("$message", message["id"]!.GetValue<long>())
                            )
                        );
                        GitScenarioState.Data(
                            await state
                                .StopRunningAsync("pause", ownerPoll, token)
                                .ConfigureAwait(false)
                        );
                        Assert.AreEqual(
                            "acknowledged",
                            state.Value(
                                "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                                ("$id", batch)
                            )
                        );
                        Assert.AreEqual("reconciled", state.Reservation("worker1").State);
                        Assert.AreEqual(20m, state.Reservation("worker1").Actual);
                        Console.WriteLine(
                            "PHASE7_EXACT_RECONSTRUCTION "
                                + new JsonObject
                                {
                                    ["legacyApplicationMarkerAbsent"] = legacyMarkerAbsent,
                                    ["sessionId"] = session,
                                    ["reservationId"] = reservation,
                                    ["baseline"] = 10m,
                                    ["nextCumulative"] = 20m,
                                    ["nextDelta"] = 10m,
                                    ["exactCap"] = 190m,
                                    ["originalRuntimeId"] = creator.Checkpoint[
                                        "runtimeId"
                                    ]!.DeepClone(),
                                    ["history"] = journal.Snapshot(session),
                                }.ToJsonString()
                        );
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
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn first = await state
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
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + first.Session.SessionId)
                );

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
                Assert.AreEqual(
                    "true",
                    state.Database.ReadMetadata("native:bootstrap:" + first.Session.SessionId)
                );
                Phase7Sessions.Turn later = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.AreEqual(
                    "prompting", state.Database.ReadMetadata("worker:worker1:lifecycle")
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
                    0L,
                    state.Number(
                        (
                            "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                            + "e.id=s.event_id WHERE e.batch_id=$batch;"
                        ),
                        ("$batch", batch)
                    )
                );
                Phase7Clock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                later.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                Assert.AreEqual(
                    0L,
                    state.Number(
                        (
                            "SELECT COUNT(*) FROM events e LEFT JOIN shown_notices s ON "
                            + "s.event_id=e.id AND s.recipient=e.recipient "
                        )
                            + "WHERE e.batch_id=$batch AND s.event_id IS NULL;",
                        ("$batch", batch)
                    )
                );
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
    public async Task CompletedSendConfirmationAlreadyExistsAtActualAckStorageBoundary()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                // Observation only: this trigger neither manufactures confirmation
                // nor gates/fails product ACK. It records the value visible there.
                state.Execute(
                    (
                        "CREATE TABLE phase7_ack_observation(batch_id INTEGER PRIMARY "
                        + "KEY,session_id TEXT,marker TEXT);\nCREATE TRIGGER "
                        + "phase7_observe_ack BEFORE UPDATE OF state ON delivery_batches\nWHEN "
                        + "OLD.state='sending' AND NEW.state='sent'\nBEGIN\n    INSERT INTO "
                        + "phase7_ack_observation(batch_id,session_id,marker)\n    SELECT "
                        + "NEW.id,session.value,confirmation.value\n    FROM meta session LEFT "
                        + "JOIN meta confirmation\n        ON "
                        + "confirmation.key='native:bootstrap:' || session.value\n    WHERE "
                        + "session.key='worker:' || NEW.recipient || ':session';\nEND;"
                    )
                );
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    "sending",
                    state.Value("SELECT state FROM delivery_batches WHERE id=$id;", ("$id", batch))
                );
                Phase7Clock.ObservedPoll ownerPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                turn.Complete();
                await state.WaitAckAsync(batch, state.WorkDeadline, token).ConfigureAwait(false);
                Assert.AreEqual(
                    turn.Session.SessionId,
                    state.Value(
                        "SELECT session_id FROM phase7_ack_observation WHERE batch_id=$id;",
                        ("$id", batch)
                    )
                );
                Assert.AreEqual(
                    "true",
                    state.Value(
                        "SELECT marker FROM phase7_ack_observation WHERE batch_id=$id;",
                        ("$id", batch)
                    )
                );
                await state.WaitDurableAsync(
                    () => (string?)state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", batch)
                    ) == "acknowledged",
                    state.WorkDeadline,
                    token
                ).ConfigureAwait(false);
                Assert.AreEqual(
                    "acknowledged",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", batch)
                    )
                );
                GitScenarioState.Data(
                    await state
                        .StopRunningAsync("pause", ownerPoll, token)
                        .ConfigureAwait(false)
                );
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
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                _ = state.Start();
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long batch = state.SendingBatch("worker1");
                if (cancel)
                {
                    state.CancelCaller();
                    JsonObject result = await state.Active.WaitAsync(token).ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(
                        "ownership_or_usage_unresolved",
                        result["code"]!.GetValue<string>()
                    );
                    Assert.AreEqual(
                        "caller-cancelled",
                        state.Database.ReadMetadata("lifecycle:stop_reason")
                    );
                    Assert.AreEqual("unresolved", state.Database.ReadMetadata("lifecycle"));
                    Assert.AreEqual(
                        1L,
                        state.Number(
                            (
                                "SELECT COUNT(*) FROM coordinator_segments\nWHERE "
                                + "state='unresolved' AND stop_reason='caller-cancelled' AND "
                                + "ended_at IS NOT NULL;"
                            )
                        )
                    );
                    Assert.IsTrue(turn.Token.IsCancellationRequested);
                    Assert.AreEqual(0, turn.Session.CompletedTurns);
                    Assert.IsNull(state.Reservation("worker1").Actual);
                    Assert.AreNotEqual(
                        "sent",
                        state.Value(
                            "SELECT state FROM delivery_batches WHERE id=$id;",
                            ("$id", batch)
                        )
                    );
                }
                else
                {
                    const string reason = "Controlled failed send.";
                    long kickoff = state.Number(
                        """
                        SELECT id FROM events
                        WHERE batch_id=$batch AND kind='kickoff' AND state='sending';
                        """,
                        ("$batch", batch)
                    );
                    turn.Fail(new OperationException("bootstrap_send_failed", reason));
                    await state.Active.WaitAsync(token).ConfigureAwait(false);
                    _ = await state.Active.ConfigureAwait(false);
                    Assert.AreEqual(
                        "bootstrap_send_failed",
                        state.Database.ReadMetadata("lifecycle:stop_reason")
                    );
                    Assert.AreEqual(
                        reason,
                        state.Value(
                            """
                            SELECT last_error FROM delivery_batches
                            WHERE id=$id AND state='failed';
                            """,
                            ("$id", batch)
                        ) as string
                    );
                    Assert.IsGreaterThan(
                        state.Database.Now.ToUnixTimeMilliseconds(),
                        state.Number(
                            """
                            SELECT next_retry_at FROM events
                            WHERE id=$id AND batch_id=$batch AND state='failed' AND attempts=1
                                AND last_error=$reason;
                            """,
                            ("$id", kickoff),
                            ("$batch", batch),
                            ("$reason", reason)
                        ),
                        "The failed kickoff must retain its reason and a first-attempt retry."
                    );
                }
                Assert.IsNull(
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        "SELECT COUNT(*) FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", batch)
                    )
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        (
                            "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                            + "e.id=s.event_id WHERE e.batch_id=$id;"
                        ),
                        ("$id", batch)
                    )
                );
                Assert.AreEqual("uncertain", state.Reservation("worker1").State);
                Assert.IsGreaterThan(0m, state.Reservation("worker1").Allowance);
            })
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("ConfirmationRecoveryAndAck")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AckFailureRetainsCompletedSendConfirmationWithoutClaimingAcknowledgement()
    {
        CancellationToken token = testContext.CancellationToken;
        Phase7Run state = await Phase7Run
            .CreateAsync(testContext, token, workers: 1, ownedHosts: false)
            .ConfigureAwait(false);
        await state
            .RunBodyAsync(async () =>
            {
                state.Execute(
                    """
                    CREATE TRIGGER phase7_reject_ack BEFORE UPDATE OF state ON delivery_batches
                    WHEN OLD.state='sending' AND NEW.state='sent'
                    BEGIN SELECT RAISE(ABORT,'Phase7 controlled ACK storage failure.'); END;
                    """
                );
                Task<JsonObject> originalRun = state.Start();
                Phase7Sessions.Turn turn = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                long originalBatch = state.SendingBatch("worker1");
                long originalEvent = state.Number(
                    "SELECT id FROM events WHERE batch_id=$batch AND kind='kickoff';",
                    ("$batch", originalBatch)
                );
                Assert.IsGreaterThan(0L, originalEvent);
                turn.Complete();
                JsonObject result = await originalRun.WaitAsync(token).ConfigureAwait(false);
                Assert.IsFalse(result["ok"]!.GetValue<bool>());
                Assert.AreEqual("post_delivery_failed", result["code"]!.GetValue<string>());
                Assert.Contains(
                    "Phase7 controlled ACK storage failure.",
                    result["message"]!.GetValue<string>()
                );
                Assert.AreEqual(
                    "true",
                    state.Database.ReadMetadata("native:bootstrap:" + turn.Session.SessionId)
                );
                Assert.AreEqual(
                    "native-complete",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", originalBatch)
                    )
                );
                Assert.AreEqual(
                    "sending",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", originalBatch)
                    )
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        (
                            "SELECT COUNT(*) FROM shown_notices s JOIN events e ON "
                            + "e.id=s.event_id WHERE e.batch_id=$id;"
                        ),
                        ("$id", originalBatch)
                    )
                );
                CreditReservation originalUsage = state.Reservation("worker1");
                Assert.AreEqual(10m, originalUsage.Actual);
                Assert.AreEqual("reconciled", originalUsage.State);
                Assert.IsFalse(originalUsage.GrantOpen);
                Assert.AreEqual(
                    "post_delivery_failed",
                    state.Database.ReadMetadata("lifecycle:stop_reason"),
                    result.ToJsonString()
                );
                Assert.AreEqual("stopped", state.Database.ReadMetadata("lifecycle"));
                Phase7Sessions.Host originalHost = state.Sessions.Hosts.Single(host =>
                    host.Operation == "run"
                );
                Assert.IsTrue(
                    originalHost.Witness["managedSettlementBeforeWrapperDispose"]!.GetValue<bool>()
                );

                state.Execute("DROP TRIGGER phase7_reject_ack;");
                _ = state.Start("resume");
                Phase7Sessions.Turn resumed = await state
                    .FixedAsync(next => state.Sessions.NextTurnAsync("worker1", next), token)
                    .ConfigureAwait(false);
                Assert.IsFalse(resumed.Response.IsCompleted);
                Assert.AreNotEqual(turn.Session.SessionId, resumed.Session.SessionId);
                long resumedBatch = state.SendingBatch("worker1");
                Assert.IsGreaterThan(0L, resumedBatch);
                Assert.AreNotEqual(originalBatch, resumedBatch);
                Assert.AreEqual(
                    "sent",
                    state.Value(
                        "SELECT state FROM delivery_batches WHERE id=$id;",
                        ("$id", originalBatch)
                    )
                );
                Assert.AreEqual(
                    "acknowledged",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", originalBatch)
                    )
                );
                Assert.AreEqual(
                    1L,
                    state.Number(
                        """
                        SELECT COUNT(*) FROM shown_notices
                        WHERE event_id=$id AND recipient='worker1';
                        """,
                        ("$id", originalEvent)
                    )
                );
                Assert.AreEqual(
                    0L,
                    state.Number(
                        "SELECT COUNT(*) FROM events WHERE id=$event AND batch_id=$batch;",
                        ("$event", originalEvent),
                        ("$batch", resumedBatch)
                    )
                );
                // A fresh bootstrap can still describe retained work outside the event list.
                JsonObject[] resumedEvents = Events(resumed.Text)["events"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .ToArray();
                Assert.DoesNotContain(
                    originalEvent,
                    resumedEvents
                        .Where(item => item["id"] is not null)
                        .Select(item => item["id"]!.GetValue<long>())
                );
                Assert.Contains(
                    "kickoff",
                    resumedEvents.Select(item => item["kind"]!.GetValue<string>())
                );

                Phase7Clock.ObservedPoll resumedPoll = await state
                    .ObserveOwnerPollAsync(token)
                    .ConfigureAwait(false);
                resumed.Complete();
                await state
                    .WaitAckAsync(resumedBatch, state.WorkDeadline, token)
                    .ConfigureAwait(false);
                GitScenarioState.Data(
                    await state.StopRunningAsync("pause", resumedPoll, token).ConfigureAwait(false)
                );
                Assert.AreEqual(
                    "acknowledged",
                    state.Value(
                        "SELECT state FROM coordinator_turns WHERE batch_id=$id;",
                        ("$id", resumedBatch)
                    )
                );
                Assert.AreEqual(
                    "post_delivery_failed",
                    state.Value(
                        """
                        SELECT stop_reason FROM coordinator_segments
                        WHERE mode='run' AND ended_at IS NOT NULL;
                        """
                    )
                );
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

    private static JsonObject Events(string prompt)
    {
        const string separator = "==== EVENTS ====\n";
        int index = prompt.LastIndexOf(separator, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index);
        return JsonNode.Parse(prompt[(index + separator.Length)..])!.AsObject();
    }
}
